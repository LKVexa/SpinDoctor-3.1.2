param([Parameter(Mandatory=$true)][string]$SourceRoot,[Parameter(Mandatory=$true)][string]$OutDir)
# Compile the 13 production quantum sources with the .NET Framework csc the project uses (no installs); log warnings/errors.
$csc="$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$names='QuantumCore.cs','SpinDoctorTranches.cs','SpinDoctorTrancheTerminal.cs','SpinDoctorCircuits.cs','SpinDoctorInputs.cs','SpinDoctorReports.cs','SpinDoctorPdfInputs.cs','SpinDoctorCalculators.cs','SpinDoctorAdvanced.cs','SpinDoctorSyntax.cs','SpinDoctorSequences.cs','SpinDoctorCommandReports.cs','QuantumTerminal.cs'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$src=@($names | % { Join-Path $SourceRoot ('quantum\'+$_) })
& $csc /nologo /t:library /warn:4 /optimize+ "/out:$OutDir\SpinDoctorQuantum.dll" /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Numerics.dll $src 2>&1 | Out-File "$OutDir\csc.log" -Encoding utf8
"exit=$LASTEXITCODE errors=$(@(Select-String -Path "$OutDir\csc.log" -Pattern ': error ').Count) warnings=$(@(Select-String -Path "$OutDir\csc.log" -Pattern ': warning ').Count)"
Select-String -Path "$OutDir\csc.log" -Pattern ': (error|warning) ' | % { ($_.Line -replace '^.*\\quantum\\','') } | Group-Object { ($_ -split ': ')[1] + ' ' + (($_ -split ': ')[2] -split ' ')[0] } | % { "$($_.Count) x $($_.Name)" }
