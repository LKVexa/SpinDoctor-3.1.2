param(
    [Parameter(Mandatory=$true)][string]$BaseDir,
    [Parameter(Mandatory=$true)][string]$Tree,
    [Parameter(Mandatory=$true)][string]$OutDir,
    [string]$Harness = '',
    [string]$BaseCarrierStem = 'Lunar127_10Q1M_FULL_ULTRAWIDE_PROCESSABLE'
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ne 5) { throw 'Use Windows PowerShell 5.1.' }
if (-not $Harness) { $Harness = Join-Path $Tree 'mvp\bootstrap\TIFFGifHoloHarness.cs' }
$immutable = Join-Path $Tree 'integrity\immutable.json'
foreach ($required in @($Harness, $immutable)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing build input: $required" }
}
$mine = [IO.File]::ReadAllLines((Join-Path $PSScriptRoot 'BuildCarrier.cs'))
$usings = ($mine | Where-Object { $_ -match '^using ' }) -join "`n"
$body = ($mine | Where-Object { $_ -notmatch '^using ' }) -join "`n"
$harnessSource = [IO.File]::ReadAllText($Harness)
$extra = @([Regex]::Matches($usings, 'using\s+[A-Za-z0-9_.]+;') | ForEach-Object { $_.Value } | Where-Object { -not $harnessSource.Contains($_) }) -join "`n"
$code = $extra + "`n" + $harnessSource + "`n" + $body
Add-Type -TypeDefinition $code -Language CSharp -ReferencedAssemblies 'System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$stem = 'Lunar127_10Q1M_FULL_ULTRAWIDE_PROCESSABLE'
if ($BaseCarrierStem -notmatch '^[A-Za-z0-9._-]+$') { throw 'Invalid baseline carrier basename.' }
$result = [TIFFHolo.ReleaseCarrierBuilder]::Run(
    (Join-Path $BaseDir "cartridge\$BaseCarrierStem.tiff"), (Join-Path $BaseDir "cartridge\$BaseCarrierStem.gif"),
    $Tree, $immutable, (Join-Path $OutDir "$stem.tiff"), (Join-Path $OutDir "$stem.gif"), 'MVP_DOCS')
$summaryText = ($result -split "`n", 2)[0]
$summary = $summaryText | ConvertFrom-Json
foreach ($flag in @('bothCarriersVerifiedWithHardenedReader','allPayloadBytesRoundTripIdentically','immutablePayloadBankVerified','checkpointHeaderUnchanged','tiffCoverDecodedPixelsUnchanged','gifCoverDecodedPixelsUnchanged','gifCoverPrefixBytesUnchanged')) {
    if ($summary.$flag -ne $true) { throw "Required carrier validation failed: $flag" }
}
[IO.File]::WriteAllText((Join-Path $OutDir 'build-validation.json'), $summaryText + "`n", (New-Object Text.UTF8Encoding($false)))
Write-Output $result
