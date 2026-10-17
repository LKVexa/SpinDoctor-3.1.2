@echo off
setlocal EnableExtensions
cd /d "%~dp0"
echo ============================================================
echo  Lunar127 - Virtual Command Throughput Test
echo  Research benchmark 0.6.0 / VCK-1
echo ============================================================
echo.
echo This benchmark measures valid carrier-anchored virtual commands/s.
echo A command is counted only after synthesize, decode, execute, state commit,
echo and retirement. It is NOT a count of native CPU instructions.
echo.
start "" "%CD%\VIRTUAL_COMMAND_DASHBOARD.html?autorun=1"
echo If the dashboard does not open, launch:
echo   VIRTUAL_COMMAND_DASHBOARD.html
echo.
pause
