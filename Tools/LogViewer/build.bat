@echo off
echo ===================================
echo TEngine Log Viewer - Build Script
echo ===================================
echo.

REM 检查 Wails 是否安装
where wails >nul 2>&1
if %errorlevel% neq 0 (
    echo [错误] 未找到 Wails CLI，请先安装：
    echo   go install github.com/wailsapp/wails/v2/cmd/wails@latest
    exit /b 1
)

echo [1/3] 下载依赖...
go mod tidy
if %errorlevel% neq 0 (
    echo [错误] 依赖下载失败
    exit /b 1
)

echo.
echo [2/3] 构建应用...
wails build -clean
if %errorlevel% neq 0 (
    echo [错误] 构建失败
    exit /b 1
)

echo.
echo [3/3] 完成
echo 输出文件: build\bin\LogViewer.exe
echo.
echo ===================================
echo 构建成功！
echo 双击 build\bin\LogViewer.exe 启动应用
echo ===================================

pause
