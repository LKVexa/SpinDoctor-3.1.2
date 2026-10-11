@echo off
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0"
title SpinDoctor Quantum Virtual Terminal
set "QUETTA_QUANTUM_ROOT=%CD%"
set "PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
echo Starting SpinDoctor, the carrier-contained quantum terminal on 127.0.0.1.
echo Keep this console open. Ctrl+C stops the local service.
"%PS%" -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; try { $r=$env:QUETTA_QUANTUM_ROOT; $names='QuantumCore.cs','SpinDoctorTranches.cs','SpinDoctorTrancheTerminal.cs','SpinDoctorCircuits.cs','SpinDoctorInputs.cs','SpinDoctorReports.cs','SpinDoctorPdfInputs.cs','SpinDoctorCalculators.cs','SpinDoctorAdvanced.cs','SpinDoctorSyntax.cs','SpinDoctorSequences.cs','SpinDoctorCommandReports.cs','QuantumTerminal.cs'; Add-Type -Path @($names|ForEach-Object{Join-Path $r ('quantum\'+$_)}) -ReferencedAssemblies 'System.dll','System.Core.dll','System.Web.Extensions.dll','System.Numerics.dll'; [QuettaQuantum.TerminalHost]::Run($r,$true) } catch { [Console]::Error.WriteLine(($_ | Out-String)); exit 1 }"
if errorlevel 1 pause
