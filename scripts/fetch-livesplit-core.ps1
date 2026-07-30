param([string]$Version = "1.8.37")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$zip = Join-Path $root "lib\LiveSplit_$Version.zip"
$url = "https://github.com/LiveSplit/LiveSplit/releases/download/$Version/LiveSplit_$Version.zip"
Invoke-WebRequest -Uri $url -OutFile $zip
$extract = Join-Path $root "lib\_ls_extract"
Expand-Archive -Path $zip -DestinationPath $extract -Force
foreach ($dll in "LiveSplit.Core.dll", "UpdateManager.dll") {
  Copy-Item (Get-ChildItem -Recurse $extract -Filter $dll | Select-Object -First 1).FullName (Join-Path $root "lib\$dll") -Force
}
Remove-Item $extract -Recurse -Force
Remove-Item $zip -Force
Write-Host "Fetched LiveSplit.Core.dll + UpdateManager.dll ($Version) into lib\"
