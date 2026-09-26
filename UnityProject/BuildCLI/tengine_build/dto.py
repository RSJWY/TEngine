"""构建请求 DTO：表单状态 → CLIBridge JSON。"""

from __future__ import annotations

import json
from dataclasses import asdict
from pathlib import Path

from .config_store import BuildFormState

REQUEST_FILENAME = "build_request.json"


def dump_request(state: BuildFormState, log_dir: Path) -> Path:
    """把表单序列化为 CLIBridge.BuildRequestDTO 兼容 JSON，写入本次构建日志目录。"""
    data = asdict(state)
    # action/unityExePath 等环境字段保留在 JSON 中，CLIBridge 只消费它认识的字段
    payload = {k: v for k, v in data.items() if k != "unityExePath"}
    request_path = log_dir / REQUEST_FILENAME
    request_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    return request_path


def build_command_line(unity_exe: Path, project_dir: Path, request_path: Path, log_file: Path) -> list[str]:
    """生成 Unity batchmode 命令行。Player 构建需要 GPU，不加 -nographics。"""
    return [
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


def default_log_dir(base: Path) -> Path:
    import datetime

    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    log_dir = base / stamp
    log_dir.mkdir(parents=True, exist_ok=True)
    return log_dir
