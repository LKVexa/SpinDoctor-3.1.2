@echo off
setlocal
cd /d "%~dp0"
set "XOR_PYTHON="
if exist "%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe" set "XOR_PYTHON=%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe"
if defined XOR_PYTHON goto run
where python.exe >nul 2>nul
if not errorlevel 1 set "XOR_PYTHON=python.exe"
if not defined XOR_PYTHON (
  echo A local Python runtime with NumPy is required. This launcher downloads nothing.
  pause
  exit /b 1
)
:run
"%XOR_PYTHON%" -c "import numpy" >nul 2>nul
if errorlevel 1 (
  echo The selected local Python runtime requires NumPy.
  pause
  exit /b 1
)
echo Launching the RAM-resident black/white XOR experiment on 127.0.0.1.
echo Keep this console open while using the native viewer. Ctrl+C stops it and releases RAM.
"%XOR_PYTHON%" native\xor_reference_server.py --viewer BLACK_WHITE_XOR_REFERENCE.html
if errorlevel 1 pause
