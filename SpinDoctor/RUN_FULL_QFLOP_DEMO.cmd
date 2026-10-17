@echo off
setlocal EnableExtensions
cd /d "%~dp0"
echo ============================================================
echo  Lunar127 Full QFLOP Research Demo 0.8.0
echo ============================================================
echo.
call VERIFY.cmd
if errorlevel 1 echo [WARN] Original carrier verify did not complete cleanly.
start "" "%CD%\QFLOP_AT_1MHZ_RASTER_DASHBOARD.html"
echo.
echo QFLOP virtual-raster dashboard opened.
pause
