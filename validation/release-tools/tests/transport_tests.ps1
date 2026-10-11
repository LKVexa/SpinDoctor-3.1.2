param([Parameter(Mandatory=$true)][string]$SourceRoot,[Parameter(Mandatory=$true)][string]$RunRoot,[switch]$SkipHttp)
$ErrorActionPreference='Stop'
$names='QuantumCore.cs','SpinDoctorTranches.cs','SpinDoctorTrancheTerminal.cs','SpinDoctorCircuits.cs','SpinDoctorInputs.cs','SpinDoctorReports.cs','SpinDoctorPdfInputs.cs','SpinDoctorCalculators.cs','SpinDoctorAdvanced.cs','SpinDoctorSyntax.cs','SpinDoctorSequences.cs','SpinDoctorCommandReports.cs','QuantumTerminal.cs'
$sources=@($names|ForEach-Object{Join-Path $SourceRoot ('quantum\'+$_)})+@((Join-Path $PSScriptRoot 'transport_harness.cs'))
Add-Type -Path $sources -ReferencedAssemblies 'System.dll','System.Core.dll','System.Web.Extensions.dll','System.Numerics.dll'
New-Item -ItemType Directory -Path $RunRoot -Force|Out-Null
[TransportAuditHarness]::Run($RunRoot,(-not $SkipHttp))
