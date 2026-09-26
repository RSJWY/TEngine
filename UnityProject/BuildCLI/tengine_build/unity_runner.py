"""Unity batchmode 进程管理：启动、日志 tail、结果判定、取消、日志自动清理。"""

from __future__ import annotations

import os
import re
import shutil
import subprocess
import time
from pathlib import Path

from . import dto, unity_locator
from .config_store import BuildFormState

LOGS_ROOT = unity_locator.REPO_ROOT / "BuildCLI" / "logs"

# 自动清理默认策略：保留最近 N 次；超过保留天数的删除（满足任一即删）
DEFAULT_KEEP_COUNT = 30
DEFAULT_KEEP_DAYS = 7
_LOG_DIR_PATTERN = re.compile(r"^\d{8}-\d{6}$")

# 结果哨兵（与 CLIBridge 输出对齐）
SENTINEL_DONE = "[TEngineCLI] ========== 构建完成 =========="
_SENTINELS = {
    "build": ("[TEngineCLI] ========== 构建完成 ==========", "[TEngineCLI] ========== 构建失败 =========="),
    "buildAb": ("[TEngineCLI] ========== 构建完成 ==========", "[TEngineCLI] ========== 构建失败 =========="),
    "buildPlayer": ("[TEngineCLI] ========== Player 构建完成 ==========", "[TEngineCLI] Player 构建失败。"),
    "publish": ("[TEngineCLI] ========== 发布整理完成 ==========", "[TEngineCLI] 发布整理失败。"),
    "hotfixDll": ("[TEngineCLI] ========== 热更 DLL 编译拷贝完成 ==========", None),
    "generateAll": ("[TEngineCLI] ========== GenerateAll 完成 ==========", None),
    "syncAotManifest": ("[TEngineCLI] ========== AOT 元数据清单同步完成 ==========", None),
    "copyAotDll": ("[TEngineCLI] ========== AOT 元数据 DLL 拷贝完成 ==========", None),
    "switchPlatform": ("[TEngineCLI] ========== 平台已切换到", None),
}
GENERIC_FAILURE_HINTS = ("Aborting batchmode", "[TEngineCLI] 未知 action", "Scripts have compiler errors")


def cleanup_logs(keep_count: int = DEFAULT_KEEP_COUNT, keep_days: int = DEFAULT_KEEP_DAYS,
                 root: Path | None = None) -> list[Path]:
    """清理历史构建日志目录：超出保留数量或超龄的删除（满足任一即删）。

    只匹配 logs/ 下 `YYYYMMDD-HHMMSS` 命名的目录，返回被删除的路径列表。
    root 供测试注入临时目录，默认 LOGS_ROOT。
    """
    logs_root = root or LOGS_ROOT
    if not logs_root.is_dir():
        return []

    dirs = sorted(
        (d for d in logs_root.iterdir() if d.is_dir() and _LOG_DIR_PATTERN.match(d.name)),
        key=lambda d: d.name,
        reverse=True,
    )

    removed: list[Path] = []
    now = time.time()
    cutoff_seconds = keep_days * 86400

    # 进行中的构建（10 分钟内活跃且无 result.json）完全排除，不占保留名额
    def _is_running(log_dir: Path) -> bool:
        return not (log_dir / "result.json").exists() and (now - log_dir.stat().st_mtime) < 600

    candidates = [d for d in dirs if not _is_running(d)]

    for index, log_dir in enumerate(candidates):
        expired = cutoff_seconds > 0 and (now - log_dir.stat().st_mtime) > cutoff_seconds
        over_count = keep_count > 0 and index >= keep_count
        if not (expired or over_count):
            continue
        try:
            shutil.rmtree(log_dir)
            removed.append(log_dir)
        except OSError:
            continue
    return removed


def load_run_history(limit: int = 10) -> list[dict]:
    """读取最近 N 次执行记录（按日志目录名倒序）。每条含 action/result/耗时/目录。"""
    import json

    if not LOGS_ROOT.is_dir():
        return []

    history: list[dict] = []
    dirs = sorted(
        (d for d in LOGS_ROOT.iterdir() if d.is_dir() and _LOG_DIR_PATTERN.match(d.name)),
        key=lambda d: d.name,
        reverse=True,
    )
    for log_dir in dirs:
        result_file = log_dir / "result.json"
        if not result_file.is_file():
            continue
        try:
            record = json.loads(result_file.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue
        record["logDir"] = str(log_dir)
        history.append(record)
        if len(history) >= limit:
            break
    return history


class BuildRun:
    """一次 Unity batchmode 执行。GUI 用信号/回调拿日志与结果。"""

    def __init__(self, state: BuildFormState, unity_exe: Path):
        self.state = state
        self.unity_exe = unity_exe
        self.log_dir = dto.default_log_dir(LOGS_ROOT)
        self.log_file = self.log_dir / "unity.log"
        self.request_path: Path | None = None
        self.process: subprocess.Popen | None = None
        self._log_callbacks: list = []
        self._done_callbacks: list = []
        self._cancelled = False
        self._started_at: float | None = None
        self.result: str | None = None  # None=未结束, "success", "failed", "cancelled"

    # ---- 回调注册 ----
    def on_log(self, callback) -> None:
        self._log_callbacks.append(callback)

    def on_done(self, callback) -> None:
        self._done_callbacks.append(callback)

    def _emit_log(self, line: str) -> None:
        for cb in self._log_callbacks:
            try:
                cb(line)
            except Exception:
                pass

    def _emit_done(self) -> None:
        for cb in self._done_callbacks:
            try:
                cb(self.result)
            except Exception:
                pass

    # ---- 生命周期 ----
    def start(self) -> bool:
        if not self.unity_exe or not Path(self.unity_exe).is_file():
            self._emit_log("[BuildCLI] 未找到 Unity.exe，请在设置中手动指定路径。")
            self.result = "failed"
            self._emit_done()
            return False

        self.request_path = dto.dump_request(self.state, self.log_dir)
        self._started_at = time.time()
        project_dir = Path(self.state.projectDir) if self.state.projectDir else unity_locator.PROJECT_DIR
        cmd = dto.build_command_line(Path(self.unity_exe), project_dir, self.request_path, self.log_file)
        self._emit_log(f"[BuildCLI] 命令：{' '.join(cmd)}")
        self._emit_log(f"[BuildCLI] 日志：{self.log_file}")

        creation_flags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
        try:
            self.process = subprocess.Popen(
                cmd,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                creationflags=creation_flags,
            )
        except OSError as e:
            self._emit_log(f"[BuildCLI] 启动 Unity 失败：{e}")
            self.result = "failed"
            self._emit_done()
            return False
        return True

    def cancel(self) -> None:
        self._cancelled = True
        if self.process and self.process.poll() is None:
            try:
                self.process.kill()
            except OSError:
                pass
        self.result = "cancelled"
        self._emit_done()

    def pump(self) -> bool:
        """轮询推进：tail 日志 + 检查进程退出。返回是否仍在运行。"""
        if self.result == "cancelled":
            return False

        self._tail_log()

        if self.process is None:
            return False

        exit_code = self.process.poll()
        if exit_code is None:
            return True

        self._tail_log(final=True)
        self._emit_log(f"[BuildCLI] Unity 退出码：{exit_code}")

        if self._cancelled:
            self.result = "cancelled"
        elif exit_code == 0 and self._check_success_sentinel():
            self.result = "success"
        else:
            self.result = "failed"

        self._run_log_cleanup()
        self._write_result(exit_code)
        self._emit_done()
        return False

    def _write_result(self, exit_code: int) -> None:
        """把本次执行结果落盘 result.json，供历史列表读取。"""
        import json

        try:
            (self.log_dir / "result.json").write_text(
                json.dumps(
                    {
                        "action": self.state.action,
                        "result": self.result,
                        "exitCode": exit_code,
                        "finishedAt": time.strftime("%Y-%m-%d %H:%M:%S"),
                        "durationSeconds": round(time.time() - self._started_at, 1) if self._started_at else None,
                    },
                    ensure_ascii=False,
                ),
                encoding="utf-8",
            )
        except OSError:
            pass

    def _run_log_cleanup(self) -> None:
        """构建结束后顺带清理历史日志（策略取表单配置，默认 30 次/7 天）。"""
        keep_count = getattr(self.state, "logKeepCount", DEFAULT_KEEP_COUNT) or 0
        keep_days = getattr(self.state, "logKeepDays", DEFAULT_KEEP_DAYS) or 0
        try:
            removed = cleanup_logs(keep_count, keep_days)
        except Exception:
            removed = []
        if removed:
            self._emit_log(f"[BuildCLI] 已自动清理 {len(removed)} 个过期日志目录（保留最近 {keep_count} 次 / {keep_days} 天内）。")

    # ---- 内部 ----
    _log_offset = 0

    def _tail_log(self, final: bool = False) -> None:
        if not self.log_file.exists():
            if final:
                self._emit_log(f"[BuildCLI] 未找到 Unity 日志文件：{self.log_file}")
            return
        try:
            with open(self.log_file, "r", encoding="utf-8", errors="replace") as f:
                f.seek(self._log_offset)
                chunk = f.read()
                if chunk:
                    self._log_offset = f.tell()
                    for line in chunk.splitlines():
                        self._emit_log(line)
        except OSError:
            pass

    def _check_success_sentinel(self) -> bool:
        success_sentinel = _SENTINELS.get(self.state.action, (SENTINEL_DONE, None))[0]
        try:
            text = self.log_file.read_text(encoding="utf-8", errors="replace")
        except OSError:
            return False
        if success_sentinel in text:
            return True
        # 无失败哨兵的动作（如 generateAll）以退出码为准
        failure_sentinel = _SENTINELS.get(self.state.action, (None, None))[1]
        if failure_sentinel and failure_sentinel in text:
            return False
        return not any(hint in text for hint in GENERIC_FAILURE_HINTS) or success_sentinel in text
