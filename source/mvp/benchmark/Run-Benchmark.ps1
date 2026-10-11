param([ValidateRange(1,25)][int]$Iterations = 5)
$ErrorActionPreference = 'Stop'
try {
    $candidates = @()
    if ($env:SPINDOCTOR_PACKAGE_ROOT) { $candidates += $env:SPINDOCTOR_PACKAGE_ROOT }
    $candidate = Split-Path -Parent $PSScriptRoot
    for ($depth = 0; $candidate -and $depth -lt 12; $depth++) {
        $candidates += $candidate
        $candidate = Split-Path -Parent $candidate
    }
    $packageRoot = $null
    foreach ($candidate in $candidates) {
        if ((Test-Path -LiteralPath (Join-Path $candidate 'harness\TIFFGifHoloHarness.cs') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $candidate 'cartridge') -PathType Container)) { $packageRoot = $candidate; break }
    }
    if (-not $packageRoot) { throw 'The containing SpinDoctor package was not found. Set SPINDOCTOR_PACKAGE_ROOT to its root folder.' }
    Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $packageRoot 'harness\TIFFGifHoloHarness.cs'))) -Language CSharp -ReferencedAssemblies 'System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll'
    $static = [Reflection.BindingFlags]'NonPublic,Static'
    $instance = [Reflection.BindingFlags]'NonPublic,Instance'
    $codec = [TIFFHolo.HarnessEntry].Assembly.GetType('TIFFHolo.CarrierCodec', $true)
    $load = $codec.GetMethod('Load', $static)
    $buildArchive = $codec.GetMethod('BuildArchive', $static)
    $bundle = [ordered]@{schema='SPINDOCTOR/CARRIER-BENCHMARK/2'; source='Current carrier verified in-process with the bundled harness'; generatedUtc=[DateTime]::UtcNow.ToString('o')}
    foreach ($format in @('gif','tiff')) {
        [string]$path = Join-Path $packageRoot ('cartridge\Lunar127_10Q1M_FULL_ULTRAWIDE_PROCESSABLE.' + $format)
        $carrier = $load.Invoke($null, [object[]]@($path, $true))
        $carrierType = $carrier.GetType()
        $dataPages = $carrierType.GetField('DataPageCount', $instance).GetValue($carrier)
        $archiveBytes = $buildArchive.Invoke($null, [object[]]@($carrier.PSObject.BaseObject)).Length
        $fileBytes = (Get-Item -LiteralPath $path).Length
        $times = @()
        for ($index = 0; $index -lt $Iterations; $index++) {
            $timer = [Diagnostics.Stopwatch]::StartNew()
            $carrier = $load.Invoke($null, [object[]]@($path, $true))
            $timer.Stop()
            $times += $timer.Elapsed.TotalMilliseconds
        }
        $smoke = @()
        for ($index = 0; $index -lt [Math]::Min(3,$Iterations); $index++) {
            $timer = [Diagnostics.Stopwatch]::StartNew()
            $rc = [TIFFHolo.HarnessEntry]::Run([string[]]@('--smoke', $path))
            $timer.Stop()
            if ($rc -ne 0) { throw "MVP smoke validation failed: $format" }
            $smoke += $timer.Elapsed.TotalMilliseconds
        }
        $mean = ($times | Measure-Object -Average).Average
        $sorted = @($times | Sort-Object)
        $p95 = $sorted[[Math]::Ceiling(.95 * $sorted.Count) - 1]
        $sha = ([BitConverter]::ToString($codec.GetMethod('FileHash',$static).Invoke($null,[object[]]@($path)))).Replace('-','').ToLowerInvariant()
        $bundle[$format] = [ordered]@{
            carrier=$format.ToUpperInvariant(); fileBytes=[int64]$fileBytes; dataPages=$dataPages; archiveBytes=$archiveBytes; sha256=$sha
            liveVerifyMeanMs=$mean; liveVerifyP95Ms=$p95; liveVerifyPagesPerSec=$dataPages*1000.0/$mean
            liveVerifyFileMBps=($fileBytes/1048576.0)/($mean/1000.0); liveVerifyArchiveMBps=($archiveBytes/1048576.0)/($mean/1000.0)
            liveSmokeMeanMs=($smoke|Measure-Object -Average).Average; iterations=$Iterations
            referenceTransactionMs=$null; referenceTransactionRatePerSec=$null; referenceVmInstructions=$null
            measurementNote='Machine-local verified carrier decode/hash timings after one warm-up; PowerShell compilation/startup is excluded. Smoke validates native payload presence without running a simulation. No MHz optical, physical quantum, or old VM transaction claim.'
        }
    }
    $json = $bundle | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'latest_metrics.json'), $json + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'latest_metrics.js'), 'window.LUNAR127_BENCHMARK = ' + $json + ';' + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
    Write-Host 'PASS: current TIFF/GIF carrier verify and MVP smoke benchmark completed.'
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 2
}
