@echo off
rem =============================================================================
rem sync-obfuz-local.bat - double-click wrapper for sync-obfuz-local.sh
rem Optional version argument, e.g.: sync-obfuz-local.bat v3.1.0
rem =============================================================================
setlocal
set "BASH="
if exist "D:\Program Files\Git\bin\bash.exe" set "BASH=D:\Program Files\Git\bin\bash.exe"
if not defined BASH if exist "C:\Program Files\Git\bin\bash.exe" set "BASH=C:\Program Files\Git\bin\bash.exe"
if not defined BASH (
  echo [ERROR] Git Bash not found. Please run sync-obfuz-local.sh manually in Git Bash.
  pause
  exit /b 1
)
"%BASH%" "%~dp0sync-obfuz-local.sh" %*
echo.
pause
