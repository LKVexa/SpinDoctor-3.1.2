param([string]$IdList, [Parameter(Mandatory=$true)][string]$OutDir, [int]$TimeoutSec = 900, [Parameter(Mandatory=$true)][string]$Root)
$ErrorActionPreference = 'Stop'
$root = $Root; if (-not $IdList) { $IdList = ((1..241) | % { 'qc-{0:D3}' -f $_ }) -join ',' }
$names='QuantumCore.cs','SpinDoctorTranches.cs','SpinDoctorTrancheTerminal.cs','SpinDoctorCircuits.cs','SpinDoctorInputs.cs','SpinDoctorReports.cs','SpinDoctorPdfInputs.cs','SpinDoctorCalculators.cs','SpinDoctorAdvanced.cs','SpinDoctorSyntax.cs','SpinDoctorSequences.cs','SpinDoctorCommandReports.cs','QuantumTerminal.cs'
Add-Type -Path @($names | % { Join-Path $root ('quantum\'+$_) }) -ReferencedAssemblies 'System.dll','System.Core.dll','System.Web.Extensions.dll','System.Numerics.dll'
Add-Type -AssemblyName System.Web.Extensions
$js = New-Object System.Web.Script.Serialization.JavaScriptSerializer; $js.MaxJsonLength = 2147483647; $js.RecursionLimit = 200
New-Item -ItemType Directory -Force $OutDir | Out-Null
$log = Join-Path $OutDir 'run_log.jsonl'
$Ids = $IdList -split ','
$session = New-Object QuettaQuantum.TerminalSession($root, $true)
$utf8 = New-Object System.Text.UTF8Encoding($false)
function SeqJobById($id) { if (-not $id) { return $null }; $sj = $js.DeserializeObject($session.SequenceJson($id, $null, 'latest')); if ($sj.ContainsKey('job')) { $sj['job'] } else { $sj } }
function SeqJob { $st = $session.GetStatus(); $st['sequence']['job'] }
function MakePdf($rec, $path) {
  $req = $js.DeserializeObject($js.Serialize(@{ kind='selected'; view='summary'; records=@($rec); status=@{} }))
  $bytes = $session.ReportPdf($req); [IO.File]::WriteAllBytes($path, $bytes); $bytes.Length
}
try {
foreach ($id in $Ids) {
  $cmd = "q.preset run $id"; $t0 = [Diagnostics.Stopwatch]::StartNew(); $entry = [ordered]@{ preset=$id; command=$cmd; startedLocal=(Get-Date).ToString('o') }
  try {
    $resp = $session.Execute($cmd)
    $entry.ok = [bool]$resp['ok']; $entry.reportId = $resp['reportId']; if (-not $resp['ok']) { $entry.error = $resp['error'] }
    $job = $null; if ($resp['result'] -is [System.Collections.IDictionary]) { $job = $resp['result']['job']; if ($job -eq $null -and $resp['result'].ContainsKey('id')) { $job = $resp['result'] } }
    $jobId = if ($job) { $job['id'] } else { $null }; $entry.jobId = $jobId
    $timedOut = $false
    if ($jobId) {
      while ($true) { $j = SeqJob; if ($j -eq $null -or $j['id'] -ne $jobId -or -not $j['active']) { break }; if ($t0.Elapsed.TotalSeconds -gt $TimeoutSec) { $timedOut = $true; $session.Execute('q.sequence cancel') | Out-Null; Start-Sleep 2; break }; Start-Sleep -Milliseconds 200 }
      $j = SeqJob; $entry.jobPhase = $j['phase']; $entry.jobError = $j['error']
    }
    $entry.timedOut = $timedOut
    $null = $session.GetStatus()  # triggers CompleteReports
    $t0.Stop(); $entry.durationSec = [Math]::Round($t0.Elapsed.TotalSeconds,3)
    $rid = $entry.reportId; $hist = $session.CommandHistoryPage(0,256)
    $stage = 'accepted'; try { $txt = $session.CommandPrintout($rid,'completed'); if ($jobId) { $stage='completed' } } catch { $txt = $session.CommandPrintout($rid,'accepted') }
    $base = Join-Path $OutDir $id
    [IO.File]::WriteAllText("$base.txt", $txt, $utf8)
    if ($stage -eq 'completed') { [IO.File]::WriteAllText("$base.accepted.txt", $session.CommandPrintout($rid,'accepted'), $utf8) }
    $rec = @{ id="return-$id"; command=$cmd; kind='sequence'; receivedUtc=(Get-Date).ToUniversalTime().ToString('o'); jobId=$jobId; jobType='sequence'; nativeArchive=@{ type='command'; id=$rid; stage=$stage } }
    $entry.pdfBytes = MakePdf $rec "$base.pdf"
    $entry.stage = $stage; try { $jo = (SeqJobById $jobId); if ($jo) { $entry.pruned = @($jo['prunedPresetCircuits']) } } catch {}
    # per-step reports
    $steps = @()
    if ($jobId) {
      $sj = $js.DeserializeObject($session.SequenceJson($jobId, $null, 'latest'))
      $recs = $null; foreach ($k in 'records','job') { if ($sj.ContainsKey($k)) { $recs = $sj[$k] } }
      if ($sj['records']) { $recs = $sj['records'] } elseif ($sj['job'] -and $sj['job']['records']) { $recs = $sj['job']['records'] }
      $entry.stepCount = @($recs).Count
      $stepDir = Join-Path $OutDir ($id + '_steps'); New-Item -ItemType Directory -Force $stepDir | Out-Null
      foreach ($r in @($recs)) {
        $ix = [int]$r['index']; $sc = $r['command']; $sst = if ($r['jobId']) { 'completed' } else { 'accepted' }
        $s = [ordered]@{ index=$ix; command=$sc; ok=$null; stage=$sst }
        try {
          if ($r['response']) { $s.ok = [bool]$r['response']['ok']; if (-not $r['response']['ok']) { $s.error = $r['response']['error'] } }
          $sb = Join-Path $stepDir ('{0:D2}' -f $ix)
          try { $stxt = $session.SequencePrintout($jobId, $ix, $sst) } catch { $sst='accepted'; $s.stage=$sst; $stxt = $session.SequencePrintout($jobId, $ix, 'accepted') }
          [IO.File]::WriteAllText("$sb.txt", $stxt, $utf8)
          $srec = @{ id="return-$id-$ix"; command=$sc; kind='step'; receivedUtc=(Get-Date).ToUniversalTime().ToString('o'); jobId=$r['jobId']; jobType='step'; nativeArchive=@{ type='sequence'; id=$jobId; index=$ix; stage=$sst } }
          $s.pdfBytes = MakePdf $srec "$sb.pdf"
        } catch { $s.reportError = $_.Exception.Message }
        $steps += $s
      }
    }
    $entry.steps = $steps
  } catch { $entry.exception = $_.Exception.ToString(); $t0.Stop(); $entry.durationSec = [Math]::Round($t0.Elapsed.TotalSeconds,3) }
  Add-Content -LiteralPath $log -Value ($entry | ConvertTo-Json -Depth 6 -Compress) -Encoding UTF8
  Write-Output ("{0} ok={1} phase={2} dur={3}s steps={4}" -f $id, $entry.ok, $entry.jobPhase, $entry.durationSec, $entry.stepCount)
}
} finally { $session.Dispose() }


