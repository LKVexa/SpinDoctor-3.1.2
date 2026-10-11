param([Parameter(Mandatory=$true)][string]$SourceRoot,[string]$TestRoot=(Join-Path $PSScriptRoot 'folder-verification-results'),[switch]$SkipMaximum)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -ne 5){throw 'Run with Windows PowerShell 5.1.'}
$sourceNames='QuantumCore.cs','SpinDoctorTranches.cs','SpinDoctorTrancheTerminal.cs','SpinDoctorCircuits.cs','SpinDoctorInputs.cs','SpinDoctorReports.cs','SpinDoctorPdfInputs.cs','SpinDoctorCalculators.cs','SpinDoctorAdvanced.cs','SpinDoctorSyntax.cs','SpinDoctorSequences.cs','SpinDoctorCommandReports.cs','QuantumTerminal.cs'
$sourcePaths=@($sourceNames | ForEach-Object {Join-Path $SourceRoot ('quantum\'+$_)})
$sourcePaths+=Join-Path $PSScriptRoot 'newfolder_verification.cs'
Add-Type -Path $sourcePaths -ReferencedAssemblies 'System.dll','System.Core.dll','System.Web.Extensions.dll','System.Numerics.dll'
[NewFolderVerification]::Run((Join-Path $TestRoot ([Guid]::NewGuid().ToString('N'))),(-not $SkipMaximum))
