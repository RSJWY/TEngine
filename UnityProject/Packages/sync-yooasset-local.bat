@echo off
rem =============================================================================
rem sync-yooasset-local.bat - double-click wrapper for sync-yooasset-local.sh
rem Optional version argument, e.g.: sync-yooasset-local.bat 3.0.6
rem =============================================================================
setlocal
set "BASH="
if exist "D:\Program Files\Git\bin\bash.exe" set "BASH=D:\Program Files\Git\bin\bash.exe"
if not defined BASH if exist "C:\Program Files\Git\bin\bash.exe" set "BASH=C:\Program Files\Git\bin\bash.exe"
if not defined BASH (
  echo [ERROR] Git Bash not found. Please run sync-yooasset-local.sh manually in Git Bash.
  pause
  exit /b 1
)
"%BASH%" "%~dp0sync-yooasset-local.sh" %*
echo.
pause
