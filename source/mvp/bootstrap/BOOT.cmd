@echo off
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0"
title SpinDoctor Quantum Virtual Terminal

set "FORMAT=tiff"
if /I "%~1"=="gif" set "FORMAT=gif"
if /I "%~1"=="tiff" set "FORMAT=tiff"
set "CARRIER=%CD%\cartridge\SpinDoctor-3.1.2.%FORMAT%"
set "HARNESSSRC=%CD%\harness\TIFFGifHoloHarness.cs"
set "RUNTIME=%CD%\workspace\bootstrap"
set "QUETTA_MOUNT_ROOT=%CD%\workspace\projection"
set "SPINDOCTOR_PACKAGE_ROOT=%CD%"
set "BUILDLOG=%RUNTIME%\harness-powershell.log"
set "PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"

if not exist "%CARRIER%" (
  echo [SpinDoctor] Requested %FORMAT% carrier is missing.
  pause
  exit /b 2
)
if not exist "%HARNESSSRC%" (
  echo [SpinDoctor] Generic C# harness source is missing.
  pause
  exit /b 2
)
if not exist "%RUNTIME%" mkdir "%RUNTIME%" >nul 2>nul
if not exist "%PS%" (
  where powershell.exe >nul 2>nul
  if errorlevel 1 (
    echo [SpinDoctor] Windows PowerShell was not found.
    pause
    exit /b 3
  )
  set "PS=powershell.exe"
)

rem IMPORTANT: Do not create or execute a transient harness EXE.
rem Some Windows Smart App Control / WDAC configurations reject freshly generated
rem executables in %%TEMP%% with "The system cannot execute the specified program."
rem The generic C# source is compiled and invoked in-process by Windows PowerShell.

echo ================================================================
echo  SPINDOCTOR - CARRIER-CONTAINED QUANTUM VIRTUAL TERMINAL
echo ================================================================
echo  External bootstrap: BOOT.cmd + generic C# harness source only
echo  Authoritative MVP: TIFF-GIF-HOLO/4 image-resident filesystem
echo  Model domain:      500 trillion procedural single-qubit state addresses
echo  Quantum program:   FP64 C# CPU and actual OpenCL GPU kernels in carrier
echo  Selected register: 1-29 coherent qubits with entangling gate operands
echo  Verification:      mathematical rule checks and independent sampled calculations
echo  Accounting:        represented gates and actual state calculations reported separately
echo  Carrier:           %CARRIER%
echo  Harness mode:      in-process Windows PowerShell Add-Type
echo ================================================================
echo.

echo [1/3] Loading generic carrier harness in-process...
call :PSRUN --verify
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
  echo.
  echo [SpinDoctor] Carrier verification failed. Nothing will be materialized.
  if exist "%BUILDLOG%" (
    echo [SpinDoctor] Diagnostic log:
    echo   "%BUILDLOG%"
    type "%BUILDLOG%"
  )
  pause
  exit /b 5
)

echo [2/3] Processable hologram and immutable MVP bank verified.
if /I "%~1"=="verify" goto :DONE
if /I "%~2"=="verify" goto :DONE
if /I "%~1"=="inspect" goto :INSPECT
if /I "%~2"=="inspect" goto :INSPECT

echo [3/3] Materializing the verified quantum terminal and launching it...
call :PSRUN --launch-mvp
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
  echo [SpinDoctor] Image-resident MVP launch failed.
  if exist "%BUILDLOG%" (
    echo [SpinDoctor] Diagnostic log:
    echo   "%BUILDLOG%"
    type "%BUILDLOG%"
  )
  pause
  exit /b 6
)

echo.
echo [PASS] The quantum entry point, C# CPU/OpenCL engine and interface were decoded from the verified hologram.
echo        Seeds and checkpoints are retained in workspace\projection\mvp.
echo        Represented gates and individually calculated states are reported separately.
echo.
goto :DONE

:INSPECT
call :PSRUN --inspect
set "RC=%ERRORLEVEL%"
goto :DONE_RC

:PSRUN
set "PSMODE=%~1"
"%PS%" -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; try { $src=[IO.File]::ReadAllText($env:HARNESSSRC); Add-Type -TypeDefinition $src -Language CSharp -ReferencedAssemblies 'System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll' -ErrorAction Stop; $a=[string[]]@($env:PSMODE,$env:CARRIER); $rc=[TIFFHolo.HarnessEntry]::Run($a); exit [int]$rc } catch { $m=($_ | Out-String); [IO.File]::WriteAllText($env:BUILDLOG,$m); [Console]::Error.WriteLine($m); exit 97 }"
exit /b %ERRORLEVEL%

:DONE
set "RC=0"
:DONE_RC
echo Press any key to close this bootstrap window.
pause >nul
exit /b %RC%
