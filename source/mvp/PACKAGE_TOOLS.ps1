param(
    [ValidateSet('Verify', 'Inspect', 'Smoke', 'Diagnose', 'Build', 'Reset')]
    [string]$Mode = 'Verify',
    [string]$Target = ''
)
$ErrorActionPreference = 'Stop'
try {
    # The bootstrap supplies the root. Ancestor discovery supports a later launch
    # of these tools from the saved projection without relying on the current cwd.
    $candidates = @()
    if ($env:SPINDOCTOR_PACKAGE_ROOT) { $candidates += $env:SPINDOCTOR_PACKAGE_ROOT }
    $candidate = $PSScriptRoot
    for ($depth = 0; $candidate -and $depth -lt 12; $depth++) {
        $candidates += $candidate
        $candidate = Split-Path -Parent $candidate
    }
    $packageRoot = $null
    foreach ($candidate in $candidates) {
        if ((Test-Path -LiteralPath (Join-Path $candidate 'BOOT.cmd') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $candidate 'harness\TIFFGifHoloHarness.cs') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $candidate 'cartridge') -PathType Container)) {
            $packageRoot = [IO.Path]::GetFullPath($candidate)
            break
        }
    }
    if (-not $packageRoot) { throw 'The containing SpinDoctor package was not found. Run the package BOOT.cmd first, or set SPINDOCTOR_PACKAGE_ROOT to that package folder.' }
    if ($Mode -eq 'Reset') {
        Write-Host 'RESET_CURRENT does not erase seeds, checkpoints, or saved reports.'
        Write-Host 'Use q.reset in the running terminal to reset the active model.'
        Write-Host 'For a clean separate workspace, copy this versioned package to a new folder.'
        exit 0
    }
    $source = Join-Path $packageRoot 'harness\TIFFGifHoloHarness.cs'
    Add-Type -TypeDefinition ([IO.File]::ReadAllText($source)) -Language CSharp -ReferencedAssemblies 'System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll'
    if ($Mode -eq 'Build') {
        Write-Host 'PASS: generic carrier harness compiled in-process. No transient executable is required.'
        exit 0
    }
    $carrierStem = Join-Path $packageRoot 'cartridge\Lunar127_10Q1M_FULL_ULTRAWIDE_PROCESSABLE'
    $targets = @()
    if (-not $Target) {
        if ($Mode -eq 'Inspect') { $targets = @($carrierStem + '.tiff') }
        else { $targets = @(($carrierStem + '.tiff'), ($carrierStem + '.gif')) }
    } elseif ($Target -in @('tiff', 'gif')) {
        $targets = @($carrierStem + '.' + $Target.ToLowerInvariant())
    } else {
        if (-not [IO.Path]::IsPathRooted($Target)) { $Target = Join-Path $PSScriptRoot $Target }
        $targets = @([IO.Path]::GetFullPath($Target))
    }
    foreach ($carrier in $targets) {
        if (-not (Test-Path -LiteralPath $carrier -PathType Leaf)) { throw "Carrier missing: $carrier" }
        $argument = if ($Mode -eq 'Inspect') { '--inspect' } elseif ($Mode -eq 'Smoke') { '--smoke' } else { '--verify' }
        $rc = [TIFFHolo.HarnessEntry]::Run([string[]]@($argument, $carrier))
        if ($rc -ne 0) { exit [int]$rc }
    }
    if ($Mode -eq 'Diagnose') {
        $flags = [Reflection.BindingFlags]'NonPublic,Static'
        $instance = [Reflection.BindingFlags]'NonPublic,Instance'
        $codec = [TIFFHolo.HarnessEntry].Assembly.GetType('TIFFHolo.CarrierCodec', $true)
        $carrier = $codec.GetMethod('Load', $flags).Invoke($null, [object[]]@($targets[0], $true))
        $entries = $carrier.GetType().GetField('Entries', $instance).GetValue($carrier)
        $nativeSources = @('QuantumCore.cs','SpinDoctorTranches.cs','SpinDoctorTrancheTerminal.cs','SpinDoctorCircuits.cs','SpinDoctorInputs.cs','SpinDoctorReports.cs','SpinDoctorPdfInputs.cs','SpinDoctorCalculators.cs','SpinDoctorAdvanced.cs','SpinDoctorSyntax.cs','SpinDoctorSequences.cs','SpinDoctorCommandReports.cs','QuantumTerminal.cs')
        $names = @($nativeSources | ForEach-Object { 'mvp/quantum/' + $_ }) + @('mvp/SpinDoctor.html','mvp/QUANTUM_TERMINAL.html','mvp/RUN_SPINDOCTOR.cmd','mvp/RUN_QUANTUM_XPU.cmd')
        $hash = [Security.Cryptography.SHA256]::Create()
        try {
            foreach ($name in $names) {
                $entry = $entries[$name]
                if (-not $entry) { throw "Required native payload missing: $name" }
                $data = $entry.GetType().GetField('Data', $instance).GetValue($entry)
                if ($data.Length -eq 0) { throw "Required native payload is empty: $name" }
                $expected = ([BitConverter]::ToString($hash.ComputeHash($data))).Replace('-', '').ToLowerInvariant()
                Write-Host "$name sha256=$expected"
                $projected = Join-Path (Join-Path $packageRoot 'workspace\projection') ($name.Replace('/', '\'))
                if (Test-Path -LiteralPath $projected -PathType Leaf) {
                    $actual = ([BitConverter]::ToString($hash.ComputeHash([IO.File]::ReadAllBytes($projected)))).Replace('-', '').ToLowerInvariant()
                    if ($actual -ne $expected) { throw "Saved projection differs from verified carrier: $projected. Run BOOT.cmd to refresh the code." }
                }
            }
        } finally { $hash.Dispose() }
        Write-Host 'PASS: carrier integrity, required native sources, launcher payloads, and any saved projection source hashes.'
    }
    exit 0
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 2
}
