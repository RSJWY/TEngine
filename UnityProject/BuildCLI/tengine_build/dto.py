"""构建请求 DTO：表单状态 → CLIBridge JSON。"""

from __future__ import annotations

import json
from dataclasses import asdict
from pathlib import Path

from .config_store import BuildFormState

REQUEST_FILENAME = "build_request.json"

# Python 侧专有字段，不下发给 CLIBridge
_LOCAL_ONLY_FIELDS = {"unityExePath", "projectDir", "logKeepCount", "logKeepDays", "buildTimeoutMinutes", "batchName"}

# 触发域重载、必须独立成段（单独一个 Unity 进程）的动作
DOMAIN_RELOAD_ACTIONS = {"generateAll", "switchPlatform"}

RESULT_FILENAME = "unity_result.json"

# 全部合法动作（与 CLIBridge.Execute / GUI 按钮对齐）
ALL_ACTIONS = [
    "build", "buildAb", "buildPlayer", "publish", "hotfixDll",
    "generateAll", "syncAotManifest", "copyAotDll", "switchPlatform", "buildInstaller",
]


def dump_request(state: BuildFormState, log_dir: Path, actions: list[str] | None = None) -> Path:
    """把表单序列化为 CLIBridge.BuildRequestDTO 兼容 JSON，写入本次构建日志目录。

    actions：本次要执行的动作列表。None/空 = 单动作（用 state.action，向后兼容）；
    多个时写入 actions[]，CLIBridge 在同一进程内按顺序执行。
    """
    data = asdict(state)
    payload = {k: v for k, v in data.items() if k not in _LOCAL_ONLY_FIELDS}
    if actions is None:
        actions = [state.action] if state.action else ["build"]
    actions = [a for a in actions if a]
    if len(actions) == 1:
        payload["action"] = actions[0]
        payload["actions"] = actions
    elif actions:
        payload["action"] = actions[0]
        payload["actions"] = actions
    else:
        payload["action"] = "build"
        payload["actions"] = ["build"]
    request_path = log_dir / REQUEST_FILENAME
    request_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    return request_path


def build_command_line(unity_exe: Path, project_dir: Path, request_path: Path, log_file: Path,
                       result_path: Path | None = None) -> list[str]:
    """生成 Unity batchmode 命令行。Player 构建需要 GPU，不加 -nographics。"""
    cmd = [
        str(unity_exe),
        "-projectPath",
        str(project_dir),
        "-batchmode",
        "-quit",
        "-executeMethod",
        "TEngine.CLIBridge.Run",
        "-logFile",
        str(log_file),
        f"-tengineConfig={request_path}",
    ]
    if result_path is not None:
        cmd.append(f"-tengineResult={result_path}")
    return cmd


def default_log_dir(base: Path) -> Path:
    import datetime

    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    log_dir = base / stamp
    log_dir.mkdir(parents=True, exist_ok=True)
    return log_dir
