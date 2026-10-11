param([Parameter(Mandatory=$true)][string]$SourceRoot,[Parameter(Mandatory=$true)][string]$RunRoot)
$ErrorActionPreference='Stop'
$names='QuantumCore.cs','SpinDoctorTranches.cs','SpinDoctorTrancheTerminal.cs','SpinDoctorCircuits.cs','SpinDoctorInputs.cs','SpinDoctorReports.cs','SpinDoctorPdfInputs.cs','SpinDoctorCalculators.cs','SpinDoctorAdvanced.cs','SpinDoctorSyntax.cs','SpinDoctorSequences.cs','SpinDoctorCommandReports.cs','QuantumTerminal.cs'
function SourceSha([string]$Path) {
    $hasher=[Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hasher.ComputeHash([IO.File]::ReadAllBytes($Path))).Replace('-','').ToLowerInvariant() }
    finally { $hasher.Dispose() }
}
$initialHashes=@{}
foreach($name in $names){$initialHashes[$name]=SourceSha (Join-Path $SourceRoot ('quantum\'+$name))}
$initialUiHash=SourceSha (Join-Path $SourceRoot 'SpinDoctor.html')
New-Item -ItemType Directory -Path $RunRoot -Force|Out-Null
$adapted=Join-Path $RunRoot 'QuantumTerminal-memory-transport.cs'
$native=Join-Path $SourceRoot 'quantum\QuantumTerminal.cs'
$text=[IO.File]::ReadAllText($native)
$count=([regex]::Matches($text,'NetworkStream stream')).Count
if($count -ne 6){throw 'Unexpected protocol stream signature count; test adapter must be reviewed.'}
# Only parameter types change. The production parser, route dispatch, guards,
# content types and response writer bodies remain byte-for-byte source text.
$text=$text.Replace('NetworkStream stream','Stream stream')
[IO.File]::WriteAllText($adapted,$text,[Text.UTF8Encoding]::new($false))
$sources=@($names|Where-Object{$_ -ne 'QuantumTerminal.cs'}|ForEach-Object{Join-Path $SourceRoot ('quantum\'+$_)})+@($adapted,(Join-Path $PSScriptRoot 'csv_http_offline.cs'))
Add-Type -Path $sources -ReferencedAssemblies 'System.dll','System.Core.dll','System.Web.Extensions.dll','System.Numerics.dll'
$checks=[CsvHttpOfflineAudit]::Run((Join-Path $RunRoot 'native-session'))
$hashes=@{}
foreach($name in $names){$hashes[$name]=SourceSha (Join-Path $SourceRoot ('quantum\'+$name))}
foreach($name in $names){if($initialHashes[$name] -ne $hashes[$name]){throw ('Native source changed during protocol checks: '+$name)}}
$currentUiHash=SourceSha (Join-Path $SourceRoot 'SpinDoctor.html')
if($initialUiHash -ne $currentUiHash){throw 'UI source changed during protocol checks.'}
$proof=@{passed=$true;checks=$checks;scope='Production CSV/equation HTTP parsing/guards/dispatch exercised through a test-only duplex memory stream; no socket listeners, network, GPU, shots or stress workloads.';testOnlyStreamParameterSubstitutions=$count;sourceUnchangedDuringChecks=$true;sourceHashes=$hashes;uiHtmlSha256=$currentUiHash}
$proof|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $RunRoot 'csv-http-offline-proof.json') -Encoding UTF8
Write-Output "PASS $checks CSV/equation offline HTTP protocol checks."
