<#
.SYNOPSIS
  Builds everything and assembles the release package dist\MineHeim-<version>.zip.

.DESCRIPTION
  The package holds our own build output and scripts only:
    Install.cmd, Uninstall.cmd, Install-MineHeim.ps1, README.md, LICENSE, THIRD-PARTY.md
    valheim\    install.ps1, fetch_deps.ps1, MineHeim\bin\Release\net472\MineHeim.dll (+ .pdb)
    compositor\ install.ps1, fetch_deps.ps1, MineHeim.fx, build\MineHeim.addon64
  prism\      install.ps1 (downloads Prism Launcher, hash-checked, and writes the MineHeim instance)
    minecraft\  mineheim-<version>.jar and Fabric API (Apache-2.0) for the Fabric profile
  The paths match the repo, so the same install scripts work in both. BepInEx, ReShade and Prism Launcher are NOT in the package:
  Install.cmd downloads them from their official sources and checks their hashes. No Minecraft or Valheim file is in it.

.EXAMPLE
  .\tools\make_release.ps1            # build + package
  .\tools\make_release.ps1 -NoBuild   # package what is already built
#>
param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$props = Get-Content (Join-Path $repo 'mc\gradle.properties') -Raw
$version = [regex]::Match($props, '(?m)^version=(.+)$').Groups[1].Value.Trim()
$fabricApi = [regex]::Match($props, '(?m)^fabric_api_version=(.+)$').Groups[1].Value.Trim()
$pluginVersion = [regex]::Match((Get-Content (Join-Path $repo 'valheim\MineHeim\Plugin.cs') -Raw), 'Version = "([^"]+)"').Groups[1].Value
if ($pluginVersion -ne $version) { throw "Version mismatch: mod $version, plugin $pluginVersion" }

if (-not $NoBuild) { & (Join-Path $repo 'tools\build.ps1') -Test }

$name = "MineHeim-$version"
$out = Join-Path $repo "dist\$name"
if (Test-Path $out) { throw "$out exists: delete it (or bump the version) first" }
New-Item -ItemType Directory -Force $out | Out-Null
function Put($src, $rel) {
    $s = Join-Path $repo $src
    if (-not (Test-Path $s)) { throw "missing: $s" }
    $d = Join-Path $out $rel
    New-Item -ItemType Directory -Force (Split-Path $d) | Out-Null
    Copy-Item $s $d
}
foreach ($f in 'Install.cmd', 'Uninstall.cmd', 'Install-MineHeim.ps1', 'README.md', 'LICENSE', 'THIRD-PARTY.md') { Put $f $f }
foreach ($f in 'valheim\install.ps1', 'valheim\fetch_deps.ps1', 'valheim\MineHeim\bin\Release\net472\MineHeim.dll', 'valheim\MineHeim\bin\Release\net472\MineHeim.pdb',
               'compositor\install.ps1', 'compositor\fetch_deps.ps1', 'compositor\MineHeim.fx', 'compositor\build\MineHeim.addon64', 'prism\install.ps1') { Put $f $f }
Put "mc\build\libs\mineheim-$version.jar" "minecraft\mineheim-$version.jar"
$api = Get-ChildItem "$env:USERPROFILE\.gradle\caches\modules-2\files-2.1\net.fabricmc.fabric-api\fabric-api\$fabricApi" -Recurse -Filter "fabric-api-$fabricApi.jar" | Select-Object -First 1
if (-not $api) { throw "Fabric API $fabricApi jar not in the Gradle cache (build the mod first)" }
Copy-Item $api.FullName (Join-Path $out "minecraft\fabric-api-$fabricApi.jar")

$zip = Join-Path $repo "dist\$name.zip"
Compress-Archive -Path "$out\*" -DestinationPath $zip -Force
Get-ChildItem $out -Recurse -File | ForEach-Object { '{0,10}  {1}' -f $_.Length, $_.FullName.Substring($out.Length + 1) }
"{0}  {1:N1} MB  SHA-256 {2}" -f $zip, ((Get-Item $zip).Length / 1MB), (Get-FileHash $zip).Hash
