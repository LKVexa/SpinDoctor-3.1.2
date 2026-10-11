param([Parameter(Mandatory=$true)][string]$SourceRoot,[Parameter(Mandatory=$true)][string]$TestRoot)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -ne 5){throw 'Run this harness with Windows PowerShell 5.1.'}
$names='QuantumCore.cs','SpinDoctorTranches.cs','SpinDoctorTrancheTerminal.cs','SpinDoctorCircuits.cs','SpinDoctorInputs.cs','SpinDoctorReports.cs','SpinDoctorPdfInputs.cs','SpinDoctorCalculators.cs','SpinDoctorAdvanced.cs','SpinDoctorSyntax.cs','SpinDoctorSequences.cs','SpinDoctorCommandReports.cs','QuantumTerminal.cs'
$sources=@($names | ForEach-Object {Join-Path $SourceRoot ('quantum\'+$_)})
$sources+=Join-Path $PSScriptRoot 'engine_regressions.cs'
Add-Type -Path $sources -ReferencedAssemblies 'System.dll','System.Core.dll','System.Web.Extensions.dll','System.Numerics.dll'
$runRoot=Join-Path $TestRoot ([Guid]::NewGuid().ToString('N'))
[EngineRegressions]::Run($runRoot,(Join-Path $SourceRoot 'SPINDOCTOR_PRESETS.json'))
