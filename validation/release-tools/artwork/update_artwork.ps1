param([Parameter(Mandatory=$true)][string]$Harness,[Parameter(Mandatory=$true)][string]$InputCartridge,[Parameter(Mandatory=$true)][string]$OutputCartridge,[Parameter(Mandatory=$true)][string]$TitleVersion)
$ErrorActionPreference = 'Stop'
$taskSource = [IO.File]::ReadAllText($Harness) + "`n" + [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'UpdateCarrierArtwork.cs'))
Add-Type -TypeDefinition $taskSource -Language CSharp -ReferencedAssemblies 'System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll'
$taskResult = [TIFFHolo.CarrierArtworkUpdate]::Run($InputCartridge, $OutputCartridge, $TitleVersion)
[IO.File]::WriteAllText((Join-Path $OutputCartridge 'artwork-validation.json'), $taskResult + "`n", (New-Object Text.UTF8Encoding($false)))
Write-Output $taskResult
