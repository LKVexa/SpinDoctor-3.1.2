param([string]$Harness, [string]$Carrier, [string]$Scratch)
$ErrorActionPreference = 'Stop'
$code = [IO.File]::ReadAllText($Harness) + "`n" + [IO.File]::ReadAllText("$PSScriptRoot\carrier_harness_tests.cs")
Add-Type -TypeDefinition $code -Language CSharp -ReferencedAssemblies 'System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll'
[TIFFHolo.CarrierAuditTests]::Run($Carrier, $Scratch)
