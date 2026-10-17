@echo off
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0"
echo ============================================================
echo  Lunar127 - Full Research Demonstration
echo  Carrier verification + AVK-1 availability + VCK-1 commands
echo ============================================================
echo.
call BUILD_HARNESS.cmd
if errorlevel 1 (
  echo.
  echo [WARN] Native evaluator build failed.
  echo The browser AVK-1 availability benchmark can still run.
  goto :OPEN
)
echo.
echo Running the existing carrier verify/smoke benchmark...
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%CD%\benchmark\Run-Benchmark.ps1" -Iterations 3
if errorlevel 1 (
  echo.
  echo [WARN] Host evaluator benchmark failed; continuing to AVK-1.
)
:OPEN
echo.
echo Opening 1 MHz Holographic Availability Lab...
start "" "%CD%\HOLOGRAPHIC_AVAILABILITY_DASHBOARD.html?autorun=1"
timeout /t 1 /nobreak >nul
echo Opening VCK-1 Virtual Command Throughput Lab...
start "" "%CD%\VIRTUAL_COMMAND_DASHBOARD.html?autorun=1"
echo.
echo Both browser benchmarks use fixed reference vectors generated
echo separately from their timed implementations.
pause
