param([Parameter(Mandatory=$true)][string]$SourceRoot,[Parameter(Mandatory=$true)][string]$Tests,[Parameter(Mandatory=$true)][string]$Scratch,[Parameter(Mandatory=$true)][string]$Out,[string]$Fixtures='',[string]$Only='')
# Runs the candidate's own source tests one by one (fresh scratch per test), records exit code + tail.
$ErrorActionPreference='Continue'
$ps="$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$T=@(
 @('engine_regressions',@('-SourceRoot',$SourceRoot,'-TestRoot','{S}')),
 @('command_receipt_regressions',@('-SourceRoot',$SourceRoot,'-TestRoot','{S}')),
 @('preset_shot_regressions',@('-SourceRoot',$SourceRoot,'-TestRoot','{S}')),
 @('direct_preset_regressions',@('-SourceRoot',$SourceRoot,'-OutputRoot','{S}')),
 @('circuit_receipt_transition_regressions',@('-SourceRoot',$SourceRoot,'-OutputRoot','{S}')),
 @('manual_receipt_guard_regressions',@('-SourceRoot',$SourceRoot,'-TestRoot','{S}')),
 @('csv_report_regressions',@('-SourceRoot',$SourceRoot,'-TestRoot','{S}')),
 @('csv_gate_regressions',@('-SourceRoot',$SourceRoot,'-TestRoot','{S}','-FixturesRoot',$Fixtures)),
 @('csv_input_regressions',@('-SourceRoot',$SourceRoot,'-TestRoot','{S}','-FixturesRoot',$Fixtures)),
 @('csv_workflow_example',@('-SourceRoot',$SourceRoot,'-OutputRoot','{S}','-FixturesRoot',$Fixtures)),
 @('csv_http_offline',@('-SourceRoot',$SourceRoot,'-RunRoot','{S}')),
 @('detailed_report_regressions',@('-SourceRoot',$SourceRoot,'-TestRoot','{S}','-PdfChecks')),
 @('newfolder_verification',@('-SourceRoot',$SourceRoot,'-TestRoot','{S}','-SkipMaximum')),
 @('native_verification_preview_regressions',@('-SourceRoot',$SourceRoot,'-TestRoot','{S}')),
 @('native_spin_preview_regressions',@('-SourceRoot',$SourceRoot,'-OutputRoot','{S}')),
 @('native_report_example',@('-SourceRoot',$SourceRoot,'-OutputDirectory','{S}')),
 @('transport_tests',@('-SourceRoot',$SourceRoot,'-RunRoot','{S}','-SkipHttp')),
 @('v312_regressions',@('-SourceRoot',$SourceRoot,'-TestRoot','{S}'))
)
$res=@()
foreach($t in $T){
  $n=$t[0]; if($Only -and -not ($Only.Split(',') -contains $n)){continue}
  $s=Join-Path $Scratch $n; if(Test-Path $s){Remove-Item $s -Recurse -Force}; New-Item -ItemType Directory $s | Out-Null
  $a=@('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $Tests "$n.ps1")) + @($t[1] | % { $_.Replace('{S}',(Join-Path $s 'run')) })
  $sw=[Diagnostics.Stopwatch]::StartNew(); $log=Join-Path $Scratch "$n.log"
  & $ps @a *> $log; $code=$LASTEXITCODE
  $tail=(Get-Content $log -Tail 6 -ErrorAction SilentlyContinue) -join ' | '
  $res+=[ordered]@{test=$n; exit=$code; seconds=[math]::Round($sw.Elapsed.TotalSeconds,1); tail=$tail.Substring(0,[math]::Min(600,$tail.Length))}
  "{0,-42} exit={1} {2}s" -f $n,$code,[math]::Round($sw.Elapsed.TotalSeconds,1)
  ($res | ConvertTo-Json -Depth 4) | Out-File $Out -Encoding utf8
}
