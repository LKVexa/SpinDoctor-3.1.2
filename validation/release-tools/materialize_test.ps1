param(
    [Parameter(Mandatory=$true)][string]$Tree,
    [Parameter(Mandatory=$true)][string]$Carrier,
    [Parameter(Mandatory=$true)][string]$Scratch
)
$ErrorActionPreference = 'Stop'
function Get-Sha256File([string]$Path) {
    $fileStream = [IO.File]::OpenRead($Path)
    $hashAlgorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hashAlgorithm.ComputeHash($fileStream))).Replace('-', '') }
    finally { $fileStream.Dispose(); $hashAlgorithm.Dispose() }
}
if ($PSVersionTable.PSVersion.Major -ne 5) { throw 'Use Windows PowerShell 5.1.' }
if (Test-Path -LiteralPath $Scratch) { throw 'Use a new scratch directory for the materialization test.' }
$harness = Join-Path $Tree 'mvp\bootstrap\TIFFGifHoloHarness.cs'
Add-Type -TypeDefinition ([IO.File]::ReadAllText($harness)) -Language CSharp -ReferencedAssemblies 'System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll'
$mount = Join-Path $Scratch 'projection'
$rc = [TIFFHolo.HarnessEntry]::Run([string[]]@('--materialize-mvp',$Carrier,$mount))
if ($rc -ne 0) { throw "New carrier materialization failed: $rc" }
$manifest = Get-Content -LiteralPath (Join-Path $Tree 'integrity\immutable.json') -Raw | ConvertFrom-Json
$count = 0
foreach ($entry in $manifest.sha256.PSObject.Properties) {
    $path = Join-Path $mount ($entry.Name.Replace('/', '\'))
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Materialized source missing: $($entry.Name)" }
    if ((Get-Sha256File $path).ToLowerInvariant() -ne $entry.Value) { throw "Materialized source mismatch: $($entry.Name)" }
    $count++
}
# Synthetic mutable sentinels are inert and never opened as native sessions.
$mutable = Join-Path $mount 'mvp\quantum-session-state.json'
[IO.File]::WriteAllText($mutable,'synthetic mutable preservation sentinel')
$reports = Join-Path $mount 'mvp\sequence-reports'
New-Item -ItemType Directory -Force -Path $reports | Out-Null
$report = Join-Path $reports 'synthetic-preservation.txt'
[IO.File]::WriteAllText($report,'synthetic saved report preservation sentinel')
$mutableHash = Get-Sha256File $mutable
$reportHash = Get-Sha256File $report
$rc = [TIFFHolo.HarnessEntry]::Run([string[]]@('--materialize-mvp',$Carrier,$mount))
if ($rc -ne 0) { throw "Repeat materialization failed: $rc" }
if ((Get-Sha256File $mutable) -ne $mutableHash -or (Get-Sha256File $report) -ne $reportHash) { throw 'Materialization changed mutable session/report sentinels.' }
$summary = [ordered]@{status='PASS'; materializedSourceFiles=$count; immutableProjectionHashMatch=$true; repeatedBootPreservesUnlistedMutableFiles=$true; carrier=$Carrier; scratch=$Scratch}
$summary | ConvertTo-Json -Depth 3
