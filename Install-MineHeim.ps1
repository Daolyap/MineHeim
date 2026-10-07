<#
.SYNOPSIS
  One-step install (or removal) of the Valheim half of MineHeim: BepInEx + the plugin, and ReShade + the compositor add-on.

.DESCRIPTION
  1. Finds Valheim (Steam libraries) unless -ValheimDir is given.
  2. Downloads the BepInEx pack (Thunderstore) and ReShade 6.8.0 (reshade.me) into third_party\, checking SHA-256 hashes.
  3. Runs valheim\install.ps1 and compositor\install.ps1 -Effect MineHeim. Each writes a list of exactly what it added
     into the Valheim folder (mineheim-install.txt, mineheim-reshade-install.txt); -Remove takes exactly those out.
  4. Runs prism\install.ps1: Prism Launcher (portable) with a ready "MineHeim" Minecraft instance in
     %LOCALAPPDATA%\MineHeim\PrismLauncher; you sign in once with your own Microsoft account. -NoMinecraft skips it
     (then set up a Fabric profile yourself, see README.md).
  An install from before the rename (Valheimcraft) is removed first. Works from the repo (after building) and from the
  release zip (prebuilt). Valheim must be closed. -Remove takes out the Valheim side; add -IncludeMinecraft to remove
  the Prism Launcher and its MineHeim instance (and that instance's world) too.

.EXAMPLE
  .\Install-MineHeim.ps1
  .\Install-MineHeim.ps1 -ValheimDir 'D:\SteamLibrary\steamapps\common\Valheim'
  .\Install-MineHeim.ps1 -Remove
#>
param([string]$ValheimDir, [switch]$Remove, [switch]$Force, [switch]$NoMinecraft, [switch]$IncludeMinecraft)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot

function Find-Valheim {
    $roots = @()
    try { $roots += (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath } catch { }
    $roots += "${env:ProgramFiles(x86)}\Steam"
    foreach ($steam in ($roots | Where-Object { $_ } | Select-Object -Unique)) {
        $libs = @($steam)
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) { $libs += $m.Groups[1].Value -replace '\\\\', '\' }
        }
        foreach ($lib in $libs) {
            $dir = Join-Path $lib 'steamapps\common\Valheim'
            if (Test-Path (Join-Path $dir 'valheim.exe')) { return (Resolve-Path $dir).Path }
        }
    }
    return $null
}

if (-not $ValheimDir) { $ValheimDir = Find-Valheim }
if (-not $ValheimDir -or -not (Test-Path (Join-Path $ValheimDir 'valheim.exe'))) {
    throw 'Valheim not found. Pass its folder: -ValheimDir "<...>\steamapps\common\Valheim"'
}
if (Get-Process valheim -ErrorAction SilentlyContinue) { throw 'Valheim is running: close it first.' }
Write-Host "Valheim: $ValheimDir"

if ($Remove) {
    & (Join-Path $here 'compositor\install.ps1') -ValheimDir $ValheimDir -Remove
    & (Join-Path $here 'valheim\install.ps1') -ValheimDir $ValheimDir -Remove
    if ($IncludeMinecraft) { & (Join-Path $here 'prism\install.ps1') -Remove }
    else { Write-Host 'The Minecraft side (Prism Launcher + the MineHeim instance) stays; remove it too with: Install-MineHeim.ps1 -Remove -IncludeMinecraft' }
    Write-Host 'Removed. Your Valheim saves and settings were not touched.'
    return
}

# an install from before the rename (Valheimcraft): take it out first, so the two never run side by side
$legacy = @{ 'valheimcraft-install.txt' = 'mineheim-install.txt'; 'valheimcraft-reshade-install.txt' = 'mineheim-reshade-install.txt' }
$found = $legacy.Keys | Where-Object { Test-Path (Join-Path $ValheimDir $_) }
if ($found) {
    Write-Host 'Removing an earlier Valheimcraft install first.'
    foreach ($old in $found) { Move-Item (Join-Path $ValheimDir $old) (Join-Path $ValheimDir $legacy[$old]) -Force }
    if (Test-Path (Join-Path $ValheimDir 'mineheim-reshade-install.txt')) { & (Join-Path $here 'compositor\install.ps1') -ValheimDir $ValheimDir -Remove }
    if (Test-Path (Join-Path $ValheimDir 'mineheim-install.txt')) { & (Join-Path $here 'valheim\install.ps1') -ValheimDir $ValheimDir -Remove }
}

& (Join-Path $here 'valheim\fetch_deps.ps1')
& (Join-Path $here 'compositor\fetch_deps.ps1')
& (Join-Path $here 'valheim\install.ps1') -ValheimDir $ValheimDir -Force:$Force
& (Join-Path $here 'compositor\install.ps1') -ValheimDir $ValheimDir -Effect MineHeim -Force:$Force

Write-Host ''
if ($NoMinecraft) {
    $jars = if (Test-Path (Join-Path $here 'minecraft')) { Join-Path $here 'minecraft' } else { Join-Path $here 'mc\build\libs' }
    Write-Host 'Valheim side installed. Minecraft (once, your own way): a Fabric 0.19.5 profile for Minecraft 26.3 with its own'
    Write-Host "game directory, and the two jars from $jars in its mods folder. Start it, then Valheim."
} else {
    & (Join-Path $here 'prism\install.ps1')
    Write-Host ''
    Write-Host 'Done. Start Valheim: Minecraft starts by itself in the background. In a world, F7 = Minecraft mode.'
}
Write-Host 'Remove it all again with Uninstall.cmd.'
