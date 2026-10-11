param([Parameter(Mandatory=$true)][string]$SourceRoot,[string]$TestRoot=(Join-Path $PSScriptRoot 'folder-results'),[Parameter(Mandatory=$true)][string]$TestPack,[ValidateSet('all','paths','async','details','byte-budget','http-status','serialized-status')][string]$TestCase='all')
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -ne 5){throw 'Run this harness with Windows PowerShell 5.1.'}
$names='QuantumCore.cs','SpinDoctorTranches.cs','SpinDoctorTrancheTerminal.cs','SpinDoctorCircuits.cs','SpinDoctorInputs.cs','SpinDoctorReports.cs','SpinDoctorPdfInputs.cs','SpinDoctorCalculators.cs','SpinDoctorAdvanced.cs','SpinDoctorSyntax.cs','SpinDoctorSequences.cs','SpinDoctorCommandReports.cs','QuantumTerminal.cs'
$sources=@($names | ForEach-Object {Join-Path $SourceRoot ('quantum\'+$_)})
$sources+=Join-Path $PSScriptRoot 'folder_regressions.cs'
Add-Type -Path $sources -ReferencedAssemblies 'System.dll','System.Core.dll','System.Web.Extensions.dll','System.Numerics.dll'
$runRoot=Join-Path $TestRoot ([Guid]::NewGuid().ToString('N'))
if($TestCase -eq 'paths'){[FolderRegressions]::QuotedFolderFields($runRoot)}elseif($TestCase -eq 'async'){[FolderRegressions]::AsyncIdentityFailure($runRoot)}elseif($TestCase -eq 'details'){[FolderRegressions]::DetailedTextReports($runRoot)}elseif($TestCase -eq 'byte-budget'){[FolderRegressions]::ReportByteBudget($runRoot)}elseif($TestCase -eq 'http-status'){[FolderRegressions]::LongHttpStatus($runRoot)}elseif($TestCase -eq 'serialized-status'){[FolderRegressions]::LongSerializedStatus($runRoot)}else{[FolderRegressions]::Run($runRoot,$TestPack)}
