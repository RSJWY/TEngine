"""Unity batchmode 进程管理：启动、日志 tail、结果判定、取消。"""

from __future__ import annotations

import os
import subprocess
from pathlib import Path

from . import dto, unity_locator
from .config_store import BuildFormState

LOGS_ROOT = unity_locator.REPO_ROOT / "BuildCLI" / "logs"

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
        self._emit_done()
        return False

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
