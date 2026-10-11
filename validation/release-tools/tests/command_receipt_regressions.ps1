param([Parameter(Mandatory=$true)][string]$SourceRoot,[Parameter(Mandatory=$true)][string]$TestRoot)
$ErrorActionPreference='Stop'
$names='QuantumCore.cs','SpinDoctorTranches.cs','SpinDoctorTrancheTerminal.cs','SpinDoctorCircuits.cs','SpinDoctorInputs.cs','SpinDoctorReports.cs','SpinDoctorPdfInputs.cs','SpinDoctorCalculators.cs','SpinDoctorAdvanced.cs','SpinDoctorSyntax.cs','SpinDoctorSequences.cs','SpinDoctorCommandReports.cs','QuantumTerminal.cs'
Add-Type -Path (@($names|ForEach-Object{Join-Path $SourceRoot ('quantum\'+$_)})+(Join-Path $PSScriptRoot 'command_receipt_regressions.cs')) -ReferencedAssemblies 'System.dll','System.Core.dll','System.Web.Extensions.dll','System.Numerics.dll'
[CommandReceiptRegressions]::Run((Join-Path $TestRoot ([Guid]::NewGuid().ToString('N'))))
