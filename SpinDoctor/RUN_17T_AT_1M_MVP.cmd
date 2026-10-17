@echo off
setlocal EnableExtensions
cd /d "%~dp0"
echo ============================================================
echo   Lunar127 17T @ 1M MVP 0.7.0
echo   17 TFLOP/s-eq represented compute at verified 1 MHz
 echo ============================================================
echo.
start "" "%CD%\17T_AT_1M_DASHBOARD.html?autorun=1"
echo Dashboard launched. Full mode physically materializes one 34 MB compute checkpoint.
echo.
pause
