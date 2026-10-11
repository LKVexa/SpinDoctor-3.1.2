@echo off
setlocal EnableExtensions
cd /d "%~dp0"
echo ============================================================
echo   Lunar127 Full Moonshot Research Demo 0.7.0
 echo ============================================================
echo.
echo [1/3] Verifying original TIFF/GIF carrier and harness...
call VERIFY.cmd
if errorlevel 1 goto :fail
echo.
echo [2/3] Opening VCK-1 measured virtual-command throughput benchmark...
start "" "%CD%\VIRTUAL_COMMAND_DASHBOARD.html"
echo.
echo [3/3] Opening 17T@1M moonshot MVP...
start "" "%CD%\17T_AT_1M_DASHBOARD.html?autorun=1"
echo.
echo Both research dashboards are open.
pause
exit /b 0
:fail
echo.
echo Carrier verification failed. 17T@1M dashboard was not launched automatically.
pause
exit /b 1
