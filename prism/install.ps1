<#
.SYNOPSIS
  The Minecraft half in one step: Prism Launcher (portable, open source, GPL-3.0) with a ready "MineHeim" instance
  (Minecraft 26.3 + Fabric Loader 0.19.5 + the MineHeim mod + Fabric API), so Valheim can start Minecraft by itself.

.DESCRIPTION
  - Downloads Prism Launcher 11.1.1 (MinGW-w64 portable build, needs no Visual C++ runtime) from its GitHub release,
    checks its SHA-256, and unpacks it into %LOCALAPPDATA%\MineHeim\PrismLauncher (portable: all its data stays there).
  - Writes the instance (instances\MineHeim): instance.cfg, mmc-pack.json and the two jars in .minecraft\mods.
    Prism downloads Minecraft and Fabric itself on the first start, from Mojang's and Fabric's servers.
  - You sign in once in Prism with YOUR Microsoft account that owns Minecraft (Accounts > Manage Accounts). Nothing
    here gets around the ownership check.
  - -Remove deletes %LOCALAPPDATA%\MineHeim\PrismLauncher, including the instance's world: back it up first if you care.
  Re-running updates the mod jars in place and keeps your account and world.
#>
param([switch]$Remove, [switch]$NoOpen, [string]$Jars)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path $here -Parent
$version = '11.1.1'
$zipName = "PrismLauncher-Windows-MinGW-w64-Portable-$version.zip"
$url = "https://github.com/PrismLauncher/PrismLauncher/releases/download/$version/$zipName"
$sha256 = '05841D0B3BFC0A8212658457CC6035E1C29CD7FFAE919464E874E384CBE8D062'
$target = Join-Path $env:LOCALAPPDATA 'MineHeim\PrismLauncher'
$marker = Join-Path $target 'mineheim-prism.txt'

if ($Remove) {
    if (-not (Test-Path $target)) { Write-Host 'Prism Launcher (MineHeim): not installed.'; return }
    if (-not (Test-Path $marker)) { throw "$target was not made by this installer; not removing it." }
    if (Get-Process prismlauncher -ErrorAction SilentlyContinue) { throw 'Prism Launcher is running: close it (and Minecraft) first.' }
    Remove-Item $target -Recurse -Force
    Write-Host "removed $target"
    return
}

if ((Test-Path $target) -and -not (Test-Path $marker)) { throw "$target exists but was not made by this installer; move it away first." }

# 1. Prism Launcher itself (cached in third_party\, hash-checked)
$cache = Join-Path $root 'third_party'
New-Item -ItemType Directory -Force $cache | Out-Null
$zip = Join-Path $cache $zipName
if (-not (Test-Path $zip) -or (Get-FileHash $zip -Algorithm SHA256).Hash -ne $sha256) {
    Write-Host "downloading $zipName (44 MB) from github.com/PrismLauncher"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing $url -OutFile $zip
}
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
if ($hash -ne $sha256) { Remove-Item $zip -Force; throw "Prism Launcher download has the wrong SHA-256 ($hash): not installed." }

if (-not (Test-Path (Join-Path $target 'prismlauncher.exe'))) {
    New-Item -ItemType Directory -Force $target | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $tmp = Join-Path $env:TEMP "mineheim-prism-$([guid]::NewGuid().ToString('N'))"
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $tmp)
    $inner = Get-ChildItem $tmp -Recurse -Filter prismlauncher.exe | Select-Object -First 1
    if (-not $inner) { throw 'prismlauncher.exe not found in the download.' }
    Copy-Item (Join-Path $inner.DirectoryName '*') $target -Recurse -Force
    Remove-Item $tmp -Recurse -Force
    # portable mode: Prism keeps its accounts, settings and instances next to the exe
    if (-not (Test-Path (Join-Path $target 'portable.txt'))) { Set-Content (Join-Path $target 'portable.txt') '' }
    Set-Content $marker "Prism Launcher $version, installed by MineHeim's installer. Remove with Uninstall.cmd or prism\install.ps1 -Remove."
    Write-Host "Prism Launcher $version -> $target"
}

# 2. The MineHeim instance
if (-not $Jars) { $Jars = if (Test-Path (Join-Path $root 'minecraft')) { Join-Path $root 'minecraft' } else { Join-Path $root 'mc\build\libs' } }
$mod = Get-ChildItem $Jars -Filter 'mineheim-*.jar' | Where-Object { $_.Name -notmatch 'sources' } | Sort-Object LastWriteTime | Select-Object -Last 1
$api = Get-ChildItem $Jars -Filter 'fabric-api-*.jar' | Select-Object -First 1
if (-not $api) {
    $props = Get-Content (Join-Path $root 'mc\gradle.properties') -Raw -ErrorAction SilentlyContinue
    $apiVersion = [regex]::Match("$props", '(?m)^fabric_api_version=(.+)$').Groups[1].Value.Trim()
    if ($apiVersion) { $api = Get-ChildItem "$env:USERPROFILE\.gradle\caches\modules-2\files-2.1\net.fabricmc.fabric-api\fabric-api\$apiVersion" -Recurse -Filter "fabric-api-$apiVersion.jar" -ErrorAction SilentlyContinue | Select-Object -First 1 }
}
if (-not $mod -or -not $api) { throw "The MineHeim mod jar and Fabric API jar were not found in $Jars." }

$inst = Join-Path $target 'instances\MineHeim'
$mods = Join-Path $inst '.minecraft\mods'
New-Item -ItemType Directory -Force $mods | Out-Null
Get-ChildItem $mods -Filter 'mineheim-*.jar' | Remove-Item -Force
Get-ChildItem $mods -Filter 'fabric-api-*.jar' | Remove-Item -Force
Copy-Item $mod.FullName, $api.FullName $mods
@'
[General]
ConfigVersion=1.2
InstanceType=OneSix
name=MineHeim
iconKey=grass
notes=Minecraft for MineHeim (Valheim starts it by itself). Keep it on Minecraft 26.3 with Fabric.
OverrideMemory=true
MinMemAlloc=512
MaxMemAlloc=3072
OverrideMiscellaneous=true
CloseAfterLaunch=true
QuitAfterGameStop=true
'@ | Set-Content (Join-Path $inst 'instance.cfg') -Encoding ascii
@'
{
    "components": [
        { "cachedName": "Minecraft", "important": true, "uid": "net.minecraft", "version": "26.3" },
        { "cachedName": "Fabric Loader", "uid": "net.fabricmc.fabric-loader", "version": "0.19.5" }
    ],
    "formatVersion": 1
}
'@ | Set-Content (Join-Path $inst 'mmc-pack.json') -Encoding ascii
Write-Host "Instance MineHeim: $($mod.Name), $($api.Name)"

$accounts = Join-Path $target 'accounts.json'
$signedIn = (Test-Path $accounts) -and ((Get-Content $accounts -Raw) -match '"type"\s*:\s*"MSA"')
if (-not $signedIn) {
    Write-Host ''
    Write-Host 'Last step, once: sign in to Prism Launcher with the Microsoft account that owns Minecraft'
    Write-Host '  (Accounts > Manage Accounts > Add Microsoft), then close Prism. Valheim starts Minecraft from then on.'
    if (-not $NoOpen) { Start-Process (Join-Path $target 'prismlauncher.exe') -WorkingDirectory $target }
}
