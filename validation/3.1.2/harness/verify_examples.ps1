param([Parameter(Mandatory=$true)][string]$SourceRoot,[Parameter(Mandatory=$true)][string]$Work,[Parameter(Mandatory=$true)][string]$Examples,[Parameter(Mandatory=$true)][string]$Inputs,[Parameter(Mandatory=$true)][string]$Out)
# Runs every HANDGUIDE example command in order on a fresh scratch session of the given build.
$ErrorActionPreference='Stop'
if(Test-Path $Work){throw 'Use a fresh work directory.'}
$sess=Join-Path $Work 'session'; robocopy $SourceRoot $sess /E /XD workspace /NFL /NDL /NJH /NJS /NP | Out-Null
$names='QuantumCore.cs','SpinDoctorTranches.cs','SpinDoctorTrancheTerminal.cs','SpinDoctorCircuits.cs','SpinDoctorInputs.cs','SpinDoctorReports.cs','SpinDoctorPdfInputs.cs','SpinDoctorCalculators.cs','SpinDoctorAdvanced.cs','SpinDoctorSyntax.cs','SpinDoctorSequences.cs','SpinDoctorCommandReports.cs','QuantumTerminal.cs'
Add-Type -Path @($names | % { Join-Path $sess ('quantum\'+$_) }) -ReferencedAssemblies 'System.dll','System.Core.dll','System.Web.Extensions.dll','System.Numerics.dll'
Add-Type -AssemblyName System.Web.Extensions
$js=New-Object System.Web.Script.Serialization.JavaScriptSerializer; $js.MaxJsonLength=2147483647; $js.RecursionLimit=200
$s=New-Object QuettaQuantum.TerminalSession($sess,$true)
function Idle { $sw=[Diagnostics.Stopwatch]::StartNew(); do { $j=$s.GetStatus()['sequence']['job']; $seq=($j -ne $null -and $j['active']); $c=$s.GetStatus()['circuit']; $cj=$null; if($c -is [System.Collections.IDictionary] -and $c.ContainsKey('job')){$cj=$c['job']}; $cir=($cj -is [System.Collections.IDictionary] -and $cj['active']); if(-not ($seq -or $cir)){return}; Start-Sleep -Milliseconds 200 } while($sw.Elapsed.TotalSeconds -lt 180) }
$list=Get-Content $Examples -Raw | ConvertFrom-Json
$res=@()
foreach($cmd in $list){
  $run=$cmd; if($cmd -like 'q.folder run path=*'){ $run='q.folder run path="' + $Inputs.Replace('\','/') + '" downloads=false verify=false' }
  $sw=[Diagnostics.Stopwatch]::StartNew(); $e=[ordered]@{command=$cmd; executed=$run}
  try { $r=$s.Execute($run); $e.ok=[bool]$r['ok']; if(-not $r['ok']){$e.error=[string]$r['error']}; $e.reportId=[string]$r['reportId'] } catch { $e.ok=$false; $e.error='EXCEPTION '+$_.Exception.Message }
  Idle
  if($cmd -like 'q.folder run path=*'){ $j=$s.GetStatus()['sequence']['job']; $e.folderPhase=[string]$j['phase']; $e.folderError=[string]$j['error']; $e.folderFiles=@($j['files'] | % { [string]$_['filename'] + ':' + [string]$_['phase'] }); $e.skippedFiles=@($j['skippedFiles'] | % { [string]$_ }) }
  $e.seconds=[math]::Round($sw.Elapsed.TotalSeconds,2); $res+=$e
  "{0,-5} {1}  {2}" -f $e.ok,$cmd,$e.error
}
$summary=[ordered]@{productVersion=[string][QuettaQuantum.TerminalSession]::ProductVersion; ranUtc=(Get-Date).ToUniversalTime().ToString('o'); total=$res.Count; ok=@($res | ? { $_.ok }).Count; results=$res}
[IO.File]::WriteAllText($Out,($summary | ConvertTo-Json -Depth 6),(New-Object Text.UTF8Encoding($false))); "wrote $Out"
$s.Dispose()
