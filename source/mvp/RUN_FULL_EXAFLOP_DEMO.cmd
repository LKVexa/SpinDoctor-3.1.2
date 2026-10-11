@echo off
setlocal EnableExtensions
cd /d "%~dp0"
echo ============================================================
echo  Lunar127 Full EXAFLOP Raster-Cube Research Demo 0.9.0
echo ============================================================
echo.
call VERIFY.cmd
if errorlevel 1 echo [WARN] Original carrier verify did not complete cleanly.
echo.
echo Running static EXA/VRC reference checks...
where py >nul 2>&1
if not errorlevel 1 py "%CD%\exa\verify_reference_vectors.py"
echo.
start "" "%CD%\EXAFLOP_AT_1MHZ_RASTER_CUBE_DASHBOARD.html"
echo EXA@1M raster-cube dashboard opened.
pause
