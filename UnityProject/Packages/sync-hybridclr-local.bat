@echo off
rem =============================================================================
rem sync-hybridclr-local.bat - double-click wrapper for sync-hybridclr-local.sh
rem Optional version argument, e.g.: sync-hybridclr-local.bat v8.13.0
rem =============================================================================
setlocal
set "BASH="
if exist "D:\Program Files\Git\bin\bash.exe" set "BASH=D:\Program Files\Git\bin\bash.exe"
if not defined BASH if exist "C:\Program Files\Git\bin\bash.exe" set "BASH=C:\Program Files\Git\bin\bash.exe"
if not defined BASH (
  echo [ERROR] Git Bash not found. Please run sync-hybridclr-local.sh manually in Git Bash.
  pause
  exit /b 1
)
"%BASH%" "%~dp0sync-hybridclr-local.sh" %*
echo.
pause
