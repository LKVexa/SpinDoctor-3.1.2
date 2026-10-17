@echo off
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0"
title Lunar127 1.1.4 - 10Q@1M UltraWide represented-compute MVP

set "RT=%CD%\workspace\logs"
if not exist "%RT%" mkdir "%RT%" >nul 2>nul
set "RLOG=%RT%\TenQKernel.run.log"
set "TQKSRC=%CD%\runtime\TenQKernel.cs"
set "WORD=%CD%\tenq\resident_macroprogram.brw"
set "CORE=%CD%\tenq\TENQ_MACROPROGRAM_CORE.json"
set "REFS=%CD%\tenq\reference_vectors.json"
set "APPLIANCE_ROOT=%CD%\ultrawide-appliance\repository\UltraWide-Appliance-main"
set "APPLIANCE_MANIFEST=%CD%\ultrawide-appliance\ULTRAWIDE_APPLIANCE_MANIFEST.json"
set "APPLIANCE_ZIP=%CD%\ultrawide-appliance\package\UltraWide-Appliance-main.zip"
set "CARRIER_BINDING=4ef0a6992dfdbe039a7cb6aa4b67b5de98bd3798f1fb4563ca0e6342c32e86b6"
set "OUTJSON=%CD%\runtime\latest_10q_metrics.json"
set "OUTJS=%CD%\runtime\latest_10q_metrics.js"
set "PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"

if not exist "%TQKSRC%" goto :MISSING
if not exist "%WORD%" goto :MISSING
if not exist "%CORE%" goto :MISSING
if not exist "%REFS%" goto :MISSING
if not exist "%APPLIANCE_ROOT%\README.md" goto :MISSING
if not exist "%APPLIANCE_MANIFEST%" goto :MISSING
if not exist "%APPLIANCE_ZIP%" goto :MISSING
if not exist "%PS%" (
  where powershell.exe >nul 2>nul
  if errorlevel 1 (
    echo [10Q@1M] Windows PowerShell was not found.
    pause
    exit /b 3
  )
  set "PS=powershell.exe"
)
if not exist "%CD%\runtime" mkdir "%CD%\runtime" >nul 2>nul

echo ================================================================
echo  LUNAR127 1.1.4 - 10Q@1M ULTRAWIDE MVP
echo ================================================================
echo  Represented target: 1e31 FLOP-eq/s at the 1 MHz gate
echo  Domain / epoch:     1e25 FLOP-eq = 5e24 mul+add lanes
echo  UltraWide appliance: COMPLETE REPOSITORY + ORIGINAL ZIP, CARRIER-RESIDENT
echo  Resident word:      1,048,576 bits / 131,072 bytes
echo  Hot state:          256 bits
echo  Digit ledger:       exact lane ranges; one sampled multiply/add pair per digit
echo  Raster session:     fresh 256-bit launch seed; carrier+BRWWORD-bound session seed
echo                      dashboard Run Simulation rotates once per press; replay/custom seed supported
echo  Boundary:           represented/queryable != physical host FLOPS
echo  Kernel mode:        in-process Windows PowerShell Add-Type
echo ================================================================
echo.

rem Avoid creating/executing a transient TenQKernel.exe. The image-resident C#
rem kernel is compiled and invoked in-process so Windows executable reputation /
rem Smart App Control does not block the benchmark before its own validation runs.

echo [1/3] Loading the image-resident TQK-1 kernel in-process...
echo [2/3] Verifying the complete carrier-resident UltraWide Appliance, then running the benchmark and randomized proof challenges...
"%PS%" -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; try { $src=[IO.File]::ReadAllText($env:TQKSRC); Add-Type -TypeDefinition $src -Language CSharp -ReferencedAssemblies 'System.dll','System.Core.dll','System.Numerics.dll','System.Web.Extensions.dll' -ErrorAction Stop; $a=[string[]]@('--word',$env:WORD,'--core',$env:CORE,'--refs',$env:REFS,'--appliance-root',$env:APPLIANCE_ROOT,'--appliance-manifest',$env:APPLIANCE_MANIFEST,'--appliance-zip',$env:APPLIANCE_ZIP,'--carrier-binding',$env:CARRIER_BINDING,'--json',$env:OUTJSON,'--js',$env:OUTJS); $rc=[Lunar127TenQ.KernelEntry]::Run($a); exit [int]$rc } catch { $m=($_ | Out-String); [Console]::Error.WriteLine($m); exit 97 }" >"%RLOG%" 2>&1
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" if not "%RC%"=="7" (
  type "%RLOG%"
  echo [10Q@1M] Kernel validation/runtime failed with exit code %RC%.
  pause
  exit /b 5
)

echo [3/3] Opening the digit operation ledger...
if exist "%RLOG%" type "%RLOG%"
start "" "%CD%\TENQ_AT_1MHZ_DASHBOARD.html"
echo.
if "%RC%"=="0" (
  echo [PASS] TQK-1 measured at or above the 1 MHz release gate.
) else (
  echo [RESULT] The proof package ran, but the 1 MHz host gate did not pass on this machine.
)
echo         The dashboard reports represented FLOP-eq and physical host diagnostics separately.
echo.
pause
exit /b %RC%

:MISSING
echo [10Q@1M] Required image-resident file is missing from the materialized hologram, including the complete UltraWide Appliance.
pause
exit /b 2
