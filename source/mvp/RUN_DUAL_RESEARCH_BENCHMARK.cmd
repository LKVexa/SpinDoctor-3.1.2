@echo off
setlocal EnableExtensions
cd /d "%~dp0"
echo ============================================================
echo  Lunar127 - Dual Research Demonstration 0.6.0
echo  Test A: 1 MHz Holographic Availability (AVK-1)
echo  Test B: Virtual Command Throughput (VCK-1)
echo ============================================================
echo.
echo Opening both benchmark consoles. Each benchmark measures a different axis.
start "" "%CD%\HOLOGRAPHIC_AVAILABILITY_DASHBOARD.html?autorun=1"
timeout /t 1 /nobreak >nul
start "" "%CD%\VIRTUAL_COMMAND_DASHBOARD.html?autorun=1"
echo.
echo AVK-1 measures valid hologram epochs and deterministic availability.
echo VCK-1 measures valid virtual commands retired per second.
echo.
pause
