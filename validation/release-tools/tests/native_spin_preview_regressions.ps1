param([Parameter(Mandatory=$true)][string]$SourceRoot,[Parameter(Mandatory=$true)][string]$OutputRoot,[switch]$ReplayPdf,[string]$PdfOutputRoot)
$ErrorActionPreference='Stop'
$names='QuantumCore.cs','SpinDoctorTranches.cs','SpinDoctorTrancheTerminal.cs','SpinDoctorCircuits.cs','SpinDoctorInputs.cs','SpinDoctorReports.cs','SpinDoctorPdfInputs.cs','SpinDoctorCalculators.cs','SpinDoctorAdvanced.cs','SpinDoctorSyntax.cs','SpinDoctorSequences.cs','SpinDoctorCommandReports.cs','QuantumTerminal.cs'
Add-Type -Path (@($names|ForEach-Object{Join-Path $SourceRoot ('quantum\'+$_)})+(Join-Path $PSScriptRoot 'native_spin_preview_regressions.cs')) -ReferencedAssemblies 'System.dll','System.Core.dll','System.Web.Extensions.dll','System.Numerics.dll'
if($ReplayPdf){if($PdfOutputRoot){[NativeSpinPreviewRegressions]::ReplayPdf($SourceRoot,$OutputRoot,$PdfOutputRoot)}else{[NativeSpinPreviewRegressions]::ReplayPdf($SourceRoot,$OutputRoot)}}else{[NativeSpinPreviewRegressions]::Run($SourceRoot,$OutputRoot)}
