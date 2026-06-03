@echo off
setlocal enabledelayedexpansion

echo ===================================
echo TEngine Log Viewer - Build Script
echo ===================================
echo.

REM ---- Locate Wails CLI ----
set "WAILS_CMD="
where wails >nul 2>&1
if %errorlevel%==0 (
    set "WAILS_CMD=wails"
) else (
    for /f "delims=" %%i in ('go env GOPATH 2^>nul') do set "GOPATH_DIR=%%i"
    if exist "!GOPATH_DIR!\bin\wails.exe" (
        set "WAILS_CMD=!GOPATH_DIR!\bin\wails.exe"
    ) else if exist "%USERPROFILE%\go\bin\wails.exe" (
        set "WAILS_CMD=%USERPROFILE%\go\bin\wails.exe"
    )
)

if not defined WAILS_CMD (
    echo [ERROR] Wails CLI not found. Please install it first:
    echo   go install github.com/wailsapp/wails/v2/cmd/wails@latest
    echo Make sure GOPATH\bin is in PATH, or reopen the terminal after install.
    pause
    exit /b 1
)

echo [1/3] Downloading dependencies...
go mod tidy
if %errorlevel% neq 0 (
    echo [ERROR] Failed to download dependencies
    pause
    exit /b 1
)

echo.
echo [2/3] Building application...
"!WAILS_CMD!" build -clean
if %errorlevel% neq 0 (
    echo [ERROR] Build failed
    pause
    exit /b 1
)

echo.
echo [3/3] Done
echo Output: build\bin\LogViewer.exe
echo.
echo ===================================
echo Build succeeded!
echo Run build\bin\LogViewer.exe to start.
echo ===================================
pause
