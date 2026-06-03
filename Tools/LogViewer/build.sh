#!/bin/bash

echo "==================================="
echo "TEngine Log Viewer - Build Script"
echo "==================================="
echo ""

# 检查 Wails 是否安装
if ! command -v wails &> /dev/null; then
    echo "[错误] 未找到 Wails CLI，请先安装："
    echo "  go install github.com/wailsapp/wails/v2/cmd/wails@latest"
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
wails build -clean
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
