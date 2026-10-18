@echo off
rem =============================================================================
rem sync-unitask-local.bat - double-click wrapper for sync-unitask-local.sh
rem Optional version argument, e.g.: sync-unitask-local.bat 2.5.11
rem =============================================================================
setlocal
set "BASH="
if exist "D:\Program Files\Git\bin\bash.exe" set "BASH=D:\Program Files\Git\bin\bash.exe"
if not defined BASH if exist "C:\Program Files\Git\bin\bash.exe" set "BASH=C:\Program Files\Git\bin\bash.exe"
if not defined BASH (
  echo [ERROR] Git Bash not found. Please run sync-unitask-local.sh manually in Git Bash.
  pause
  exit /b 1
)
"%BASH%" "%~dp0sync-unitask-local.sh" %*
echo.
pause
