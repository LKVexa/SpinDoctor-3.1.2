param([string]$Tests="D:\AGENT_WORK\spindoctor304\chug_tests", [string]$Out="D:\AGENT_WORK\spindoctor304\chug_smoke_results.json", [string]$Only='', [int]$Timeout=240, [string]$SourceRoot='', [string]$Root='', [string]$FolderArgs='downloads=false')
# Runs q.folder against a SCRATCH copy only (default 3.0.4; -SourceRoot for others) (compiled in a throwaway root). Never point this at the original release.
$ErrorActionPreference='Stop'
$scr="D:\AGENT_WORK\spindoctor304"; $src="$scr\SpinDoctor 3.0.4\source\mvp"; if($SourceRoot){$src=$SourceRoot}
$sessRoot=$(if($Root){$Root}else{"$scr\chug_session_root"}); if(Test-Path $sessRoot){Remove-Item $sessRoot -Recurse -Force}; robocopy $src $sessRoot /E /XD workspace /NFL /NDL /NJH /NJS /NP | Out-Null
$names='QuantumCore.cs','SpinDoctorTranches.cs','SpinDoctorTrancheTerminal.cs','SpinDoctorCircuits.cs','SpinDoctorInputs.cs','SpinDoctorReports.cs','SpinDoctorPdfInputs.cs','SpinDoctorCalculators.cs','SpinDoctorAdvanced.cs','SpinDoctorSyntax.cs','SpinDoctorSequences.cs','SpinDoctorCommandReports.cs','QuantumTerminal.cs'
Add-Type -Path @($names | % { Join-Path $sessRoot ('quantum\'+$_) }) -ReferencedAssemblies 'System.dll','System.Core.dll','System.Web.Extensions.dll','System.Numerics.dll'
Add-Type -AssemblyName System.Web.Extensions; $js=New-Object System.Web.Script.Serialization.JavaScriptSerializer; $js.MaxJsonLength=2147483647; $js.RecursionLimit=200
$s=New-Object QuettaQuantum.TerminalSession($sessRoot,$true)
$cap=$s.Execute('q.circuit capacity 1024')  # tiers create many uniquely named circuits in one session
$res=New-Object System.Collections.Generic.List[object]
$folders = Get-ChildItem $Tests -Directory | Sort-Object Name | ? { $n=$_.Name; $Only -eq '' -or @($Only.Split(',') | ? { $n -like "$_*" }).Count -gt 0 }
foreach($d in $folders){
  $p=$d.FullName.Replace('\','/'); $t=[Diagnostics.Stopwatch]::StartNew()
  $r=$s.Execute('q.folder run path="' + $p + '" ' + $FolderArgs)
  $row=[ordered]@{folder=[string]$d.Name; runOk=[bool]$r['ok']; runError=[string]$r['error']; phase=''; error=''; files=@(); seconds=0; cancelled=$false}
  if($r['ok']){
    $cancelSent=$false
    do { Start-Sleep -Milliseconds 500; $job=$s.GetStatus()['sequence']['job']
         if($d.Name -like '08*' -and -not $cancelSent -and $t.Elapsed.TotalSeconds -gt 8){ $c=$s.Execute('q.folder cancel'); $cancelSent=$true; $row.cancelled=$true; $row.cancelReply=[string]$c['ok'] + ' ' + [string]$c['error'] }
         if(-not $cancelSent -and $t.Elapsed.TotalSeconds -gt $Timeout){ $s.Execute('q.folder cancel') | Out-Null; $cancelSent=$true; $row.timeoutCancel=$true }
    } while($job['active'])
    $s.Execute('q.folder status') | Out-Null  # one recorded status report per folder (polling uses GetStatus, which writes no report)
    $row.phase=[string]$job['phase']; $row.error=[string]$job['error']
    $row.files=@($job['files'] | % { [ordered]@{index=[int]$_['index']; filename=[string]$_['filename']; phase=[string]$_['phase']; format=[string]$_['sourceFormat']; error=[string]$_['error']; done=[string]$_['completedCommands']; total=[string]$_['totalCommands']} })
  }
  $row.seconds=[math]::Round($t.Elapsed.TotalSeconds,1); $res.Add($row); "{0,-32} run={1} phase={2} files={3} {4}s {5}{6}" -f $d.Name,$row.runOk,$row.phase,$row.files.Count,$row.seconds,$row.runError,$row.error
}
$o=[ordered]@{product=[string][QuettaQuantum.TerminalSession]::ProductVersion; results=$res}
[IO.File]::WriteAllText($Out,($o | ConvertTo-Json -Depth 8),(New-Object System.Text.UTF8Encoding($false)))
if ($s -is [IDisposable]) { $s.Dispose() }
