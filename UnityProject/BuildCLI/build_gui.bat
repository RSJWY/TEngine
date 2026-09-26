@echo off
rem TEngine BuildGUI launcher: create/reuse venv, install deps, start GUI
setlocal
cd /d "%~dp0"

where py >nul 2>nul
if %errorlevel%==0 (
    set PYCMD=py -3
) else (
    set PYCMD=python
)

if not exist ".venv" (
    echo [BuildCLI] Creating virtual environment...
    %PYCMD% -m venv .venv
    if errorlevel 1 goto :error
)

call ".venv\Scripts\activate.bat"
python -m pip install -q -r requirements.txt
if errorlevel 1 goto :error

python -m tengine_build
if errorlevel 1 goto :error
exit /b 0

:error
echo [BuildCLI] Startup failed. Check Python 3.10+ availability.
pause
exit /b 1
