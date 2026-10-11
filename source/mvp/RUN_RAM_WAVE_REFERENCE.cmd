@echo off
setlocal
cd /d "%~dp0"
set "XPU_PYTHON="
if exist "%LOCALAPPDATA%\..\..\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe" set "XPU_PYTHON=%LOCALAPPDATA%\..\..\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe"
if exist "%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe" set "XPU_PYTHON=%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe"
if defined XPU_PYTHON goto run
where python.exe >nul 2>nul
if not errorlevel 1 set "XPU_PYTHON=python.exe"
if not defined XPU_PYTHON (
  echo A local Python runtime with NumPy is required. No package is downloaded by this launcher.
  pause
  exit /b 1
)
:run
"%XPU_PYTHON%" -c "import numpy" >nul 2>nul
if errorlevel 1 (
  echo The selected Python runtime does not have NumPy. Install NumPy in your local runtime before launching.
  pause
  exit /b 1
)
echo Launching the contained CPU + GPU XPU on 127.0.0.1.
echo Keep this console open while using the native viewer. Ctrl+C stops it and releases RAM.
"%XPU_PYTHON%" native\xpu_server.py --viewer CPU_GPU_RAM_WAVE.html
if errorlevel 1 pause
