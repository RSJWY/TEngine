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
    "buildInstaller": ("[TEngineCLI] ========== 安装包构建完成", "[TEngineCLI] 安装包构建失败："),
    "hotfixDll": ("[TEngineCLI] ========== 热更 DLL 编译拷贝完成 ==========", None),
    "generateAll": ("[TEngineCLI] ========== GenerateAll 完成 ==========", None),
    "syncAotManifest": ("[TEngineCLI] ========== AOT 元数据清单同步完成 ==========", None),
    "copyAotDll": ("[TEngineCLI] ========== AOT 元数据 DLL 拷贝完成 ==========", None),
    "switchPlatform": ("[TEngineCLI] ========== 平台已切换到", None),
}
GENERIC_FAILURE_HINTS = ("Aborting batchmode", "[TEngineCLI] 未知 action", "Scripts have compiler errors")

# 失败摘要提取：首个匹配行（含其后的异常消息行）
_FAILURE_SUMMARY_PATTERNS = (
    "[TEngineCLI] 构建异常终止",
    "[TEngineCLI] 安装包构建失败",
    "[TEngineCLI] Player 构建失败",
    "[TEngineCLI] 发布整理失败",
    "[TEngineCLI] ========== 构建失败",
    "[BuildWithConfig] AssetBundle构建失败",
    "[BuildWithConfig] 未找到可构建的资源包",
    "[BuildWithConfig] Player 平台",
    "[TEngineCLI] 配置文件不存在",
    "[TEngineCLI] 配置 JSON 解析失败",
    "[TEngineCLI] 未知 action",
    "Aborting batchmode",
    "Scripts have compiler errors",
    "Compilation failed",
    "Exception:",
    "error CS",
)


def extract_failure_summary(log_file: Path, max_lines: int = 15) -> str:
    """从 Unity 日志提取首个错误相关行及其上下文，作为失败摘要。"""
    try:
        text = log_file.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return ""
    lines = text.splitlines()
    for index, line in enumerate(lines):
        if any(p in line for p in _FAILURE_SUMMARY_PATTERNS):
            tail = lines[index:index + max_lines]
            return "\n".join(tail).strip()
    return ""


def check_unity_lock(project_dir: Path) -> str | None:
    """检测项目是否被打开的 Unity 编辑器锁定。返回提示文案，None 表示可用。"""
    lock_file = Path(project_dir) / "Temp" / "UnityLockfile"
    if lock_file.exists():
        return (f"项目正被 Unity 编辑器占用（存在 {lock_file}）。\n"
                "请关闭正在打开该项目的 Unity 编辑器后重试。")
    return None


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
    """一次 Unity batchmode 执行。GUI 用信号/回调拿日志与结果。

    actions 为 None 时按单动作（state.action）执行（向后兼容）；
    传入列表时下发给 CLIBridge 在同一进程内顺序执行。
    """

    def __init__(self, state: BuildFormState, unity_exe: Path, actions: list[str] | None = None):
        self.state = state
        self.unity_exe = unity_exe
        self.actions = actions
        self.log_dir = dto.default_log_dir(LOGS_ROOT)
        self.log_file = self.log_dir / "unity.log"
        self.result_file = self.log_dir / dto.RESULT_FILENAME
        self.request_path: Path | None = None
        self.process: subprocess.Popen | None = None
        self._log_callbacks: list = []
        self._done_callbacks: list = []
        self._cancelled = False
        self._started_at: float | None = None
        self.result: str | None = None  # None=未结束, "success", "failed", "cancelled"
        self.failure_summary: str = ""  # 失败时的错误摘要（来自日志）
        self.unity_result: dict | None = None  # C# 侧结构化结果（unity_result.json）

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
            self.failure_summary = "未找到 Unity.exe"
            self.result = "failed"
            self._emit_done()
            return False

        project_dir = Path(self.state.projectDir) if self.state.projectDir else unity_locator.PROJECT_DIR
        lock_hint = check_unity_lock(project_dir)
        if lock_hint:
            self._emit_log(f"[BuildCLI] {lock_hint}")
            self.failure_summary = lock_hint
            self.result = "failed"
            self._emit_done()
            return False

        self.request_path = dto.dump_request(self.state, self.log_dir, self.actions)
        self._started_at = time.time()
        cmd = dto.build_command_line(Path(self.unity_exe), project_dir, self.request_path,
                                     self.log_file, self.result_file)
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
        self._kill_process()
        self.result = "cancelled"
        self._emit_done()

    def _kill_process(self) -> None:
        if self.process and self.process.poll() is None:
            try:
                self.process.kill()
            except OSError:
                pass

    def pump(self) -> bool:
        """轮询推进：tail 日志 + 检查进程退出 + 超时看门狗。返回是否仍在运行。"""
        if self.result == "cancelled":
            return False

        # 超时看门狗：Unity 卡死（license 弹窗/对话框）时主动终止
        timeout_minutes = getattr(self.state, "buildTimeoutMinutes", 0) or 0
        if (timeout_minutes > 0 and self._started_at is not None and self.process is not None
                and self.process.poll() is None
                and (time.time() - self._started_at) > timeout_minutes * 60):
            self._emit_log(f"[BuildCLI] 构建超时（{timeout_minutes} 分钟），正在终止 Unity 进程。")
            self._cancelled = True
            self._kill_process()
            self.failure_summary = f"构建超时（{timeout_minutes} 分钟）被终止。"
            self.result = "failed"
            self._run_log_cleanup()
            self._write_result(-1)
            self._emit_done()
            return False

        self._tail_log()

        if self.process is None:
            return False

        exit_code = self.process.poll()
        if exit_code is None:
            return True

        self._tail_log(final=True)
        self._emit_log(f"[BuildCLI] Unity 退出码：{exit_code}")

        self.unity_result = self._load_unity_result()
        if self._cancelled:
            self.result = "cancelled"
        elif self.unity_result is not None:
            # 有结构化结果（含按步记录）时以其 success 为准；request_version 多动作时更可靠
            self.result = "success" if self.unity_result.get("success") else "failed"
        elif exit_code == 0 and self._check_success_sentinel():
            self.result = "success"
        else:
            self.result = "failed"

        if self.result == "failed":
            self.failure_summary = self._resolve_failure_summary()

        self._run_log_cleanup()
        self._write_result(exit_code)
        self._emit_done()
        return False

    def _resolve_failure_summary(self) -> str:
        """优先取 C# 结构化结果的 error 字段，其次扫日志。"""
        if self.unity_result and self.unity_result.get("error"):
            return str(self.unity_result["error"])
        return extract_failure_summary(self.log_file)

    def _load_unity_result(self) -> dict | None:
        """读取 C# 侧落盘的 unity_result.json（CLIBridge -tengineResult）。"""
        import json

        if not self.result_file.is_file():
            return None
        try:
            data = json.loads(self.result_file.read_text(encoding="utf-8"))
            return data if isinstance(data, dict) else None
        except (OSError, json.JSONDecodeError):
            return None

    def _write_result(self, exit_code: int) -> None:
        """把本次执行结果落盘 result.json，供历史列表读取。"""
        import json

        actions = self.actions or ([self.state.action] if self.state.action else ["build"])
        payload: dict = {
            "action": "->".join(actions) if len(actions) > 1 else actions[0],
            "result": self.result,
            "exitCode": exit_code,
            "finishedAt": time.strftime("%Y-%m-%d %H:%M:%S"),
            "durationSeconds": round(time.time() - self._started_at, 1) if self._started_at else None,
        }
        if self.failure_summary:
            payload["failureSummary"] = self.failure_summary[:2000]
        if self.unity_result:
            # 合并 C# 侧关键字段（包记录/Player/安装包输出/按步记录）
            for key in ("packages", "playerOutputPath", "playerSizeBytes", "installerOutputPath",
                        "durationSeconds", "steps", "executedActions"):
                value = self.unity_result.get(key)
                if value:
                    payload[key] = value
        try:
            (self.log_dir / "result.json").write_text(
                json.dumps(payload, ensure_ascii=False),
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
        # 多动作：C# 侧已写结构化结果时不会走到这里；无结果文件则以退出码为准
        if self.actions and len(self.actions) > 1:
            return not any(_load_failure_hints(self.log_file))
        actions = self.actions or ([self.state.action] if self.state.action else ["build"])
        success_sentinel = _SENTINELS.get(actions[0], (SENTINEL_DONE, None))[0]
        try:
            text = self.log_file.read_text(encoding="utf-8", errors="replace")
        except OSError:
            return False
        if success_sentinel in text:
            return True
        # 无失败哨兵的动作（如 generateAll）以退出码为准
        failure_sentinel = _SENTINELS.get(actions[0], (None, None))[1]
        if failure_sentinel and failure_sentinel in text:
            return False
        return not any(hint in text for hint in GENERIC_FAILURE_HINTS) or success_sentinel in text


def _load_failure_hints(log_file: Path) -> list[str]:
    """读取日志中出现的通用失败提示（供多动作无结果文件时兜底判断）。"""
    try:
        text = log_file.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return list(GENERIC_FAILURE_HINTS)
    return [hint for hint in GENERIC_FAILURE_HINTS if hint in text]


def split_into_segments(actions: list[str]) -> list[list[str]]:
    """把动作队列按"是否触发域重载"切成分段列表。

    generateAll / switchPlatform 触发脚本重编译 + 域重载，会中断 -executeMethod
    执行流，必须各自独占一个 Unity 进程；其余动作合并进同一进程顺序执行。
    示例：[generateAll, hotfixDll, build, publish] → [[generateAll], [hotfixDll, build, publish]]
    """
    from .config_store import DOMAIN_RELOAD_ACTIONS

    segments: list[list[str]] = []
    current: list[str] = []
    for action in actions:
        if not action:
            continue
        if action in DOMAIN_RELOAD_ACTIONS:
            if current:
                segments.append(current)
                current = []
            segments.append([action])
        else:
            current.append(action)
    if current:
        segments.append(current)
    return segments


class BatchRun:
    """批量执行编排器：把动作队列分段，逐段起 Unity 进程，失败即停。

    GUI/CLI 复用。每段是一个 BuildRun；进度通过 on_step / on_log / on_done 回调上报。
    """

    def __init__(self, state: BuildFormState, unity_exe: Path, actions: list[str],
                 stop_on_failure: bool = True, name: str = ""):
        self.state = state
        self.unity_exe = unity_exe
        self.actions = [a for a in actions if a]
        self.stop_on_failure = stop_on_failure
        self.name = name

        self.segments = split_into_segments(self.actions)
        self.total_steps = len(self.actions)
        self.completed_steps = 0          # 已成功的步骤数（跨段累计）
        self.current_segment = 0
        self.run: BuildRun | None = None  # 当前段
        self.result: str | None = None    # None=未结束, "success", "failed", "cancelled"
        self.failure_summary: str = ""

        self._log_callbacks: list = []
        self._step_callbacks: list = []
        self._done_callbacks: list = []
        self._cancelled = False
        self._started_at: float | None = None

    # ---- 回调 ----
    def on_log(self, callback) -> None:
        self._log_callbacks.append(callback)

    def on_step(self, callback) -> None:
        """callback(step_index(1-based), action, segment_index(1-based), total_segments)"""
        self._step_callbacks.append(callback)

    def on_done(self, callback) -> None:
        self._done_callbacks.append(callback)

    def _emit_log(self, line: str) -> None:
        for cb in self._log_callbacks:
            try:
                cb(line)
            except Exception:
                pass

    def _emit_step(self, action: str) -> None:
        for cb in self._step_callbacks:
            try:
                cb(self.completed_steps + 1, action, self.current_segment, len(self.segments))
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
        if not self.segments:
            self._emit_log("[BuildCLI] 批量任务没有可执行的动作。")
            self.result = "failed"
            self.failure_summary = "批量任务没有可执行的动作"
            self._emit_done()
            return False
        if not self.unity_exe or not Path(self.unity_exe).is_file():
            self._emit_log("[BuildCLI] 未找到 Unity.exe，请在设置中手动指定路径。")
            self.failure_summary = "未找到 Unity.exe"
            self.result = "failed"
            self._emit_done()
            return False
        self._started_at = time.time()
        if self.name:
            self._emit_log(f"[BuildCLI] ====== 批量任务「{self.name}」：{' → '.join(self.actions)} ======")
        self._start_segment(0)
        return self.run is not None

    def _start_segment(self, index: int) -> None:
        self.current_segment = index + 1
        segment = self.segments[index]
        first_action = segment[0]
        self._emit_step(first_action)
        if len(segment) > 1:
            self._emit_log(f"[BuildCLI] ---- 段 {index + 1}/{len(self.segments)}："
                           f"{' → '.join(segment)}（同进程合并执行）----")
        else:
            self._emit_log(f"[BuildCLI] ---- 段 {index + 1}/{len(self.segments)}：{first_action} ----")

        self.run = BuildRun(self.state, self.unity_exe, actions=segment)
        self.run.on_log(self._emit_log)
        self.run.on_done(lambda _r, idx=index: self._on_segment_done(idx))

    def pump(self) -> bool:
        """轮询推进当前段。返回是否仍在运行。"""
        if self.result is not None:
            return False
        if self.run is None:
            return False
        if not self.run.pump():
            return False
        return True

    def _on_segment_done(self, index: int) -> None:
        if self.run is None:
            return

        if self.result == "cancelled" or self._cancelled:
            self.result = "cancelled"
            self._emit_done()
            return

        segment_result = self.run.result
        if segment_result == "success":
            self.completed_steps += len(self.run.actions or [])
            next_index = index + 1
            if next_index < len(self.segments):
                self._start_segment(next_index)
                if not self.run.start():
                    self._finish_failed("启动 Unity 进程失败")
                return
            self.result = "success"
            self._emit_log(f"[BuildCLI] ====== 批量任务完成（{self.total_steps} 步 / "
                           f"{len(self.segments)} 段）======")
            self._emit_done()
            return

        # 失败 / 取消
        if segment_result == "cancelled":
            self.result = "cancelled"
            self._emit_done()
            return
        self._finish_failed(self.run.failure_summary or f"段 {index + 1} 执行失败")

    def _finish_failed(self, summary: str) -> None:
        stop_hint = "" if self.stop_on_failure else "（stopOnFailure=false，但批量编排仍停止）"
        self.result = "failed"
        self.failure_summary = f"第 {self.completed_steps + 1} 步失败，已停止。{summary}"
        self._emit_log(f"[BuildCLI] ====== 批量任务失败：已完成 {self.completed_steps}/{self.total_steps} 步。"
                       f"{summary}{stop_hint} ======")
        self._emit_done()

    def cancel(self) -> None:
        self._cancelled = True
        if self.run:
            self.run.cancel()
        self.result = "cancelled"
        self._emit_done()
