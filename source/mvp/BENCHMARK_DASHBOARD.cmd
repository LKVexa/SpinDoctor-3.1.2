@echo off
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0"
echo ============================================================
echo  Lunar127 TIFF-GIF Processable Hologram Performance Lab
echo  Package 0.4.0 / Carrier 0.3.1
echo ============================================================
echo.
call BUILD_HARNESS.cmd
if errorlevel 1 (
  echo.
  echo [FAILED] Harness build failed. See workspace\logs\build.log
  pause
  exit /b 1
)
echo.
echo Running machine-local verification/smoke benchmark...
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%CD%\benchmark\Run-Benchmark.ps1" -Iterations 5
if errorlevel 1 (
  echo.
  echo [FAILED] Benchmark did not complete.
  pause
  exit /b 2
)
echo.
echo Opening performance dashboard...
start "" "%CD%\PERFORMANCE_DASHBOARD.html"
echo.
echo Dashboard opened. This window may be closed.
pause
