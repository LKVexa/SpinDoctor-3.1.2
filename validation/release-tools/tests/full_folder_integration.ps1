param([Parameter(Mandatory=$true)][string]$SourceRoot,[Parameter(Mandatory=$true)][string]$TestRoot,[Parameter(Mandatory=$true)][string]$InputRoot,[Parameter(Mandatory=$true)][string]$SpecPath)
$ErrorActionPreference='Stop'
$names='QuantumCore.cs','SpinDoctorTranches.cs','SpinDoctorTrancheTerminal.cs','SpinDoctorCircuits.cs','SpinDoctorInputs.cs','SpinDoctorReports.cs','SpinDoctorPdfInputs.cs','SpinDoctorCalculators.cs','SpinDoctorAdvanced.cs','SpinDoctorSyntax.cs','SpinDoctorSequences.cs','SpinDoctorCommandReports.cs','QuantumTerminal.cs'
$sources=@($names|ForEach-Object{Join-Path $SourceRoot ('quantum\'+$_)})+@((Join-Path $PSScriptRoot 'full_folder_integration.cs'))
Add-Type -Path $sources -ReferencedAssemblies 'System.dll','System.Core.dll','System.Web.Extensions.dll','System.Numerics.dll'
$runRoot=Join-Path $TestRoot ([Guid]::NewGuid().ToString('N'))
[FullFolderIntegration]::Run($SourceRoot,$runRoot,$InputRoot,$SpecPath)

