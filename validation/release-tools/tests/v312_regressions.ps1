param([Parameter(Mandatory=$true)][string]$SourceRoot,[Parameter(Mandatory=$true)][string]$TestRoot)
# SpinDoctor 3.1.2 regression tests for the audit fixes. Uses a fresh scratch root; never a live workspace.
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -ne 5){throw 'Run this harness with Windows PowerShell 5.1.'}
if(Test-Path $TestRoot){throw 'Use a fresh 3.1.2 regression directory.'}
$names='QuantumCore.cs','SpinDoctorTranches.cs','SpinDoctorTrancheTerminal.cs','SpinDoctorCircuits.cs','SpinDoctorInputs.cs','SpinDoctorReports.cs','SpinDoctorPdfInputs.cs','SpinDoctorCalculators.cs','SpinDoctorAdvanced.cs','SpinDoctorSyntax.cs','SpinDoctorSequences.cs','SpinDoctorCommandReports.cs','QuantumTerminal.cs'
Add-Type -Path @($names | % { Join-Path $SourceRoot ('quantum\'+$_) }) -ReferencedAssemblies 'System.dll','System.Core.dll','System.Web.Extensions.dll','System.Numerics.dll'
$utf8=New-Object System.Text.UTF8Encoding($false)
$script:n=0
function Check($cond,$label){ if(-not $cond){ throw "FAIL: $label" }; $script:n++; "PASS $label" }
function W($dir,$name,$text){ New-Item -ItemType Directory -Force $dir | Out-Null; [IO.File]::WriteAllText((Join-Path $dir $name),$text,$utf8) }
function RunFolder($s,$dir){
  $r=$s.Execute('q.folder run path="' + $dir.Replace('\','/') + '" downloads=false verify=false')
  if(-not $r['ok']){ return @{ok=$false; error=[string]$r['error']; run=$r} }
  $sw=[Diagnostics.Stopwatch]::StartNew()
  do { Start-Sleep -Milliseconds 200; $job=$s.GetStatus()['sequence']['job'] } while($job['active'] -and $sw.Elapsed.TotalSeconds -lt 300)
  return @{ok=$true; job=$job; run=$r}
}
New-Item -ItemType Directory $TestRoot | Out-Null
$root=Join-Path $TestRoot 'session'; $in=Join-Path $TestRoot 'inputs'
$s=New-Object QuettaQuantum.TerminalSession($root,$true)
try {
  Check ([QuettaQuantum.TerminalSession]::ProductVersion -eq '3.1.2') 'ProductVersion is 3.1.2'
  # F-B: unsupported files are reported; .qasm2 and .circuit are accepted
  $d=Join-Path $in 'mixed'
  W $d 'a_cmds.txt' "# heading`nq.math norm amplitudes=[1,0]`n"
  W $d 'b_prog.qasm2' "OPENQASM 2.0;`ninclude `"qelib1.inc`";`nqreg q[1];`ncreg c[1];`nh q[0];`nmeasure q -> c;`n"
  W $d 'c_text.circuit' "name v312circ`nqubits 1`nshots 16`nh 0`n"
  W $d 'notes.md' "# not read`n"
  W $d 'image.png' "decoy"
  $x=RunFolder $s $d
  Check ($x.ok -and $x.job['phase'] -eq 'completed') ('mixed folder completes: ' + $x.error + ' ' + $x.job['error'])
  Check (@($x.job['files']).Count -eq 3) '.txt, .qasm2 and .circuit are all enumerated'
  Check ([int]$x.job['skippedFileCount'] -eq 2) 'skippedFileCount reports the two unsupported files'
  Check ((@($x.job['skippedFiles']) -join ',') -eq 'image.png,notes.md') 'skippedFiles lists unsupported names in ordinal order'
  Check ([int]$x.run['result']['job']['skippedFileCount'] -eq 2) 'q.folder run response already reports skipped files'
  $d2=Join-Path $in 'only_md'; W $d2 'a.md' "q.math norm amplitudes=[1,0]`n"
  $y=RunFolder $s $d2
  Check ((-not $y.ok) -and $y.error -like 'No supported*1 unsupported file(s) skipped, e.g. a.md*') ('md-only folder names the skipped file: ' + $y.error)
  # F-C: empty and prose-only text inputs explain themselves
  $d3=Join-Path $in 'empty'; W $d3 'empty.txt' ''
  $z=RunFolder $s $d3
  Check ($z.ok -and $z.job['phase'] -eq 'failed' -and ([string]$z.job['error']) -like 'Text input is empty*') ('empty .txt message: ' + $z.job['error'])
  $d4=Join-Path $in 'prose'; W $d4 'prose.txt' "# Notes`nThis is a sentence, not a command.`n"
  $p=RunFolder $s $d4
  Check ($p.ok -and $p.job['phase'] -eq 'failed' -and ([string]$p.job['error']) -like 'Text input line 2 is neither a q.* command nor a known gate*') ('prose .txt message: ' + $p.job['error'])
  $d5=Join-Path $in 'circuit_ok'; W $d5 'c.txt' "qubits 1`nh 0`n"
  $c=RunFolder $s $d5
  Check ($c.ok -and $c.job['phase'] -eq 'completed') 'genuine text circuit still compiles'
  $d6=Join-Path $in 'undeclared'; W $d6 'u.txt' "h 0`n"
  $u=RunFolder $s $d6
  Check ($u.ok -and ([string]$u.job['error']) -eq 'Text circuit requires qubits N or q.circuit new NAME N.') 'gate lines without qubits keep the original message'
  # F-A: a 128-command file completes (combined report no longer capped at 32 MiB)
  $d7=Join-Path $in 'c128'; W $d7 'c128.txt' ((0..127 | % { "q.math norm amplitudes=[1,$_]" }) -join "`n")
  $q=RunFolder $s $d7
  Check ($q.ok -and $q.job['phase'] -eq 'completed' -and [int]$q.job['completedCommands'] -eq 128) ('128-command file completes: ' + $q.job['error'])
  # F-D: q.folder status archives a compact polling report; caller still receives full status
  $st=$s.Execute('q.folder status')
  Check ($st['ok'] -and $st['status'] -ne $null -and $st['status'].ContainsKey('sequence')) 'q.folder status response still carries the full live status'
  $rep=Get-Item (Join-Path (Join-Path $root 'quantum-command-reports') ([string]$st['reportId'] + '.json'))
  $txt=[IO.File]::ReadAllText($rep.FullName)
  Check ($txt.Contains('archiveCompacted')) 'archived q.folder status report uses the compact status form'
  Check ($rep.Length -lt 262144) ('compact q.folder status archive is small: ' + $rep.Length + ' bytes')
  $full=$s.Execute('q.status'); Check ($full['ok']) 'q.status still returns the full snapshot'
  "PASS 3.1.2 regressions: $script:n assertions"
} finally { $s.Dispose() }
