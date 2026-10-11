@echo off
setlocal EnableExtensions
cd /d "%~dp0"
echo ============================================================
echo  Lunar127 - 1 MHz Holographic Availability Test
echo  Research benchmark 0.5.0 / AVK-1
echo ============================================================
echo.
echo Opening the deterministic availability dashboard.
echo The test will auto-run at the default 10,000,000-epoch depth.
echo.
start "" "%CD%\HOLOGRAPHIC_AVAILABILITY_DASHBOARD.html?autorun=1"
echo If the dashboard does not open, launch:
echo   HOLOGRAPHIC_AVAILABILITY_DASHBOARD.html
echo.
pause
