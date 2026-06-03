# TEngine Log Viewer - PowerShell 构建脚本
# 用法：在本目录右键“使用 PowerShell 运行”，或执行  ./build.ps1
# 若提示执行策略限制，可先运行：
#   powershell -ExecutionPolicy Bypass -File build.ps1

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# 切换到脚本所在目录
Set-Location -Path $PSScriptRoot

Write-Host "===================================" -ForegroundColor Cyan
Write-Host "TEngine Log Viewer - Build Script" -ForegroundColor Cyan
Write-Host "===================================" -ForegroundColor Cyan
Write-Host ""

# ---- 定位 Wails CLI ----
function Resolve-Wails {
    $cmd = Get-Command wails -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    # 回退到 GOPATH\bin
    $gopath = (& go env GOPATH 2>$null)
    if ($gopath -and (Test-Path "$gopath\bin\wails.exe")) {
        return "$gopath\bin\wails.exe"
    }

    # 回退到 %USERPROFILE%\go\bin
    $fallback = Join-Path $env:USERPROFILE "go\bin\wails.exe"
    if (Test-Path $fallback) { return $fallback }

    return $null
}

$wails = Resolve-Wails
if (-not $wails) {
    Write-Host "[错误] 未找到 Wails CLI，请先安装：" -ForegroundColor Red
    Write-Host "  go install github.com/wailsapp/wails/v2/cmd/wails@latest"
    Write-Host "安装后请重开终端，或确认 GOPATH\bin 已在 PATH 中。"
    exit 1
}

Write-Host "[1/3] 下载依赖..." -ForegroundColor Yellow
go mod tidy
if ($LASTEXITCODE -ne 0) {
    Write-Host "[错误] 依赖下载失败" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "[2/3] 构建应用..." -ForegroundColor Yellow
& $wails build -clean
if ($LASTEXITCODE -ne 0) {
    Write-Host "[错误] 构建失败" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "[3/3] 完成" -ForegroundColor Green
Write-Host "输出文件: build\bin\LogViewer.exe"
Write-Host ""
Write-Host "===================================" -ForegroundColor Cyan
Write-Host "构建成功！双击 build\bin\LogViewer.exe 启动" -ForegroundColor Green
Write-Host "===================================" -ForegroundColor Cyan
