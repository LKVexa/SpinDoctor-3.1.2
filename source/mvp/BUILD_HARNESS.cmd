@echo off
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0"
set "PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%PS%" (
  echo [SpinDoctor] Windows PowerShell was not found.
  exit /b 3
)
"%PS%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0PACKAGE_TOOLS.ps1" -Mode Build %*
exit /b %ERRORLEVEL%
