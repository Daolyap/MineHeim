<#
.SYNOPSIS
  Downloads the BepInEx pack for Valheim from Thunderstore into third_party\ (never committed) and checks its hash.
#>
param(
    [string]$Version = '5.4.2351',
    # SHA-256 of the 5.4.2351 zip as downloaded on 2026-10-06; update it when you change $Version.
    [string]$Sha256 = 'BCE631497976A93977CEB08E166712E6C31D15244956F89F17DF092A9B62E29F'
)
$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..\third_party'
New-Item -ItemType Directory -Force $root | Out-Null
$zip = Join-Path $root "BepInExPack_Valheim-$Version.zip"
if (-not (Test-Path $zip)) {
    Invoke-WebRequest -UseBasicParsing "https://thunderstore.io/package/download/denikson/BepInExPack_Valheim/$Version/" `
        -OutFile $zip -Headers @{ 'User-Agent' = 'mineheim-fetch/0.1' }
}
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
if ($hash -ne $Sha256) { throw "Unexpected hash for $zip : $hash (expected $Sha256). Delete it, check the version, and update the hash only if you trust the new file." }
Expand-Archive $zip (Join-Path $root 'BepInExPack_Valheim') -Force
Write-Host "BepInEx pack $Version ready in $root\BepInExPack_Valheim"
