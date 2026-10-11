param([string]$Harness,[string]$Tiff,[string]$Gif,[switch]$Smoke)
$ErrorActionPreference='Stop'
Add-Type -TypeDefinition ([IO.File]::ReadAllText($Harness)) -Language CSharp -ReferencedAssemblies 'System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll'
foreach($carrier in @($Tiff,$Gif)) {
  $rc=[TIFFHolo.HarnessEntry]::Run([string[]]@('--verify',$carrier))
  if($rc -ne 0){throw "Carrier verification failed: $carrier ($rc)"}
  if($Smoke){
    $rc=[TIFFHolo.HarnessEntry]::Run([string[]]@('--smoke',$carrier))
    if($rc -ne 0){throw "Carrier smoke failed: $carrier ($rc)"}
  }
}
