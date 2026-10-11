param([Parameter(Mandatory=$true)][string]$SourceRoot,[Parameter(Mandatory=$true)][string]$OutputRoot,[Parameter(Mandatory=$true)][string]$FixturesRoot)
$ErrorActionPreference='Stop'
$names='QuantumCore.cs','SpinDoctorTranches.cs','SpinDoctorTrancheTerminal.cs','SpinDoctorCircuits.cs','SpinDoctorInputs.cs','SpinDoctorReports.cs','SpinDoctorPdfInputs.cs','SpinDoctorCalculators.cs','SpinDoctorAdvanced.cs','SpinDoctorSyntax.cs','SpinDoctorSequences.cs','SpinDoctorCommandReports.cs','QuantumTerminal.cs'
Add-Type -Path (@($names|ForEach-Object{Join-Path $SourceRoot ('quantum\'+$_)})+(Join-Path $PSScriptRoot 'csv_workflow_example.cs')) -ReferencedAssemblies 'System.dll','System.Core.dll','System.Web.Extensions.dll','System.Numerics.dll'
try { [CsvWorkflowExample]::Run($SourceRoot,$OutputRoot,$FixturesRoot) }
catch { if ($_.Exception.InnerException) { [Console]::Error.WriteLine($_.Exception.InnerException.ToString()) }; throw }
