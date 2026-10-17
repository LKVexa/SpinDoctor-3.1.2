$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ne 5) {
    throw 'Run this check with Windows PowerShell 5.1 (powershell.exe).'
}
$root = [IO.Path]::GetFullPath($PSScriptRoot) + [IO.Path]::DirectorySeparatorChar
$count = 0
foreach ($line in Get-Content -LiteralPath (Join-Path $root 'SHA256SUMS.txt')) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    if ($line -notmatch '^([a-fA-F0-9]{64})\s+\*?(.+)$') { throw "Invalid manifest line: $line" }
    $expected = $Matches[1]
    $relative = $Matches[2]
    $path = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Manifest path outside package: $relative" }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing package file: $relative" }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expected) { throw "Package checksum mismatch: $relative" }
    $count++
}
if ($count -ne 614) { throw "Expected 614 manifest entries, found $count" }
Write-Host "PASS: all $count original package file checksums match."
& (Join-Path $root 'validation\release-tools\tests\verify_carriers.ps1') `
    -Harness (Join-Path $root 'SpinDoctor\harness\TIFFGifHoloHarness.cs') `
    -Tiff (Join-Path $root 'SpinDoctor\cartridge\SpinDoctor-3.1.2.tiff') `
    -Gif (Join-Path $root 'SpinDoctor\cartridge\SpinDoctor-3.1.2.gif')
Write-Host 'PASS: both delivered carrier images verify.'
