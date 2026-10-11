param([string]$SourceRoot=(Join-Path $PSScriptRoot '..\source\mvp'),[string]$InputRoot=(Join-Path $PSScriptRoot '..\model-sampling-inputs'),[string]$TestRoot=(Join-Path $PSScriptRoot 'model-sampling-folder-results'))
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -ne 5){throw 'Run with Windows PowerShell 5.1.'}
$names='QuantumCore.cs','SpinDoctorTranches.cs','SpinDoctorTrancheTerminal.cs','SpinDoctorCircuits.cs','SpinDoctorInputs.cs','SpinDoctorReports.cs','SpinDoctorPdfInputs.cs','SpinDoctorCalculators.cs','SpinDoctorAdvanced.cs','SpinDoctorSyntax.cs','SpinDoctorSequences.cs','SpinDoctorCommandReports.cs','QuantumTerminal.cs'
$sources=@($names|ForEach-Object{Join-Path $SourceRoot ('quantum\'+$_)})
$sources+=Join-Path $PSScriptRoot 'model_sampling_folder.cs'
Add-Type -Path $sources -ReferencedAssemblies 'System.dll','System.Core.dll','System.Web.Extensions.dll','System.Numerics.dll'
[ModelSamplingFolder]::Run([System.IO.Path]::GetFullPath($SourceRoot),[System.IO.Path]::GetFullPath($InputRoot),(Join-Path $TestRoot ([Guid]::NewGuid().ToString('N'))))
