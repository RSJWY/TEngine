#!/bin/bash

echo "==================================="
echo "TEngine Log Viewer - Build Script"
echo "==================================="
echo ""

# 切换到脚本所在目录
cd "$(dirname "$0")" || exit 1

# ---- 定位 Wails CLI ----
WAILS_CMD=""
if command -v wails &> /dev/null; then
    WAILS_CMD="wails"
elif [ -x "$(go env GOPATH)/bin/wails" ]; then
    WAILS_CMD="$(go env GOPATH)/bin/wails"
elif [ -x "$HOME/go/bin/wails" ]; then
    WAILS_CMD="$HOME/go/bin/wails"
fi

if [ -z "$WAILS_CMD" ]; then
    echo "[错误] 未找到 Wails CLI，请先安装："
    echo "  go install github.com/wailsapp/wails/v2/cmd/wails@latest"
    echo "安装后请确认 \$(go env GOPATH)/bin 已在 PATH 中。"
    exit 1
fi

echo "[1/3] 下载依赖..."
go mod tidy
if [ $? -ne 0 ]; then
    echo "[错误] 依赖下载失败"
    exit 1
fi

echo ""
echo "[2/3] 构建应用..."
"$WAILS_CMD" build -clean
if [ $? -ne 0 ]; then
    echo "[错误] 构建失败"
    exit 1
fi

echo ""
echo "[3/3] 完成"
echo "输出文件: build/bin/LogViewer"
echo ""
echo "==================================="
echo "构建成功！"
echo "运行: ./build/bin/LogViewer"
echo "==================================="
