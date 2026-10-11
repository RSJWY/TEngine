@echo off
rem =============================================================================
rem sync-obfuz4hybridclr-local.bat - double-click wrapper for sync-obfuz4hybridclr-local.sh
rem Optional version argument, e.g.: sync-obfuz4hybridclr-local.bat v3.0.1
rem =============================================================================
setlocal
set "BASH="
if exist "D:\Program Files\Git\bin\bash.exe" set "BASH=D:\Program Files\Git\bin\bash.exe"
if not defined BASH if exist "C:\Program Files\Git\bin\bash.exe" set "BASH=C:\Program Files\Git\bin\bash.exe"
if not defined BASH (
  echo [ERROR] Git Bash not found. Please run sync-obfuz4hybridclr-local.sh manually in Git Bash.
  pause
  exit /b 1
)
"%BASH%" "%~dp0sync-obfuz4hybridclr-local.sh" %*
echo.
pause
