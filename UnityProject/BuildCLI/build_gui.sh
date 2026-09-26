#!/bin/bash
# TEngine BuildGUI 启动器（macOS/Linux）：自动创建/复用 venv 并安装依赖，然后启动 GUI
set -e
cd "$(dirname "$0")"

if [ ! -d ".venv" ]; then
    echo "[BuildCLI] 创建虚拟环境..."
    python3 -m venv .venv
fi

. .venv/bin/activate
python -m pip install -q -r requirements.txt
python -m tengine_build
