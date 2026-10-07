<#
.SYNOPSIS
  Installs (or removes) BepInEx 5 + the MineHeim plugin in a Valheim folder, and records exactly what it added.

.DESCRIPTION
  Adds: winhttp.dll + doorstop_config.ini (Unity Doorstop 4, loads BepInEx), BepInEx\core\*, BepInEx\config\BepInEx.cfg
  (console turned off), BepInEx\plugins\MineHeim\MineHeim.dll. Every added path is listed in
  <ValheimDir>\mineheim-install.txt; -Remove deletes exactly those, plus the files BepInEx generates at runtime.
  It stops instead of overwriting a winhttp.dll / BepInEx folder that is not its own (-Force overrides).

  The BepInEx pack is the official BepInExPack_Valheim from Thunderstore, unpacked into third_party\ (not committed).
  Run  .\valheim\fetch_deps.ps1  first to download it.

.EXAMPLE
  .\valheim\install.ps1                  # install BepInEx + plugin
  .\valheim\install.ps1 -PluginOnly      # just refresh the plugin DLL after a rebuild
  .\valheim\install.ps1 -Remove          # take everything out again
#>
param(
    [string]$ValheimDir = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim',
    [string]$PluginDll = (Join-Path $PSScriptRoot 'MineHeim\bin\Release\net472\MineHeim.dll'),
    [string]$Pack = (Join-Path $PSScriptRoot '..\third_party\BepInExPack_Valheim\BepInExPack_Valheim'),
    [switch]$PluginOnly,
    [switch]$Remove,
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$manifestPath = Join-Path $ValheimDir 'mineheim-install.txt'
$pluginRel = 'BepInEx\plugins\MineHeim\MineHeim.dll'

if (-not (Test-Path (Join-Path $ValheimDir 'valheim.exe'))) { throw "valheim.exe not found in $ValheimDir" }
if (Get-Process valheim -ErrorAction SilentlyContinue) { throw 'Valheim is running: close it first.' }

function Read-Manifest { if (Test-Path $manifestPath) { Get-Content $manifestPath | Where-Object { $_ } } else { @() } }

if ($Remove) {
    $added = Read-Manifest
    if (-not $added) { Write-Host 'Nothing recorded as installed (no manifest).'; return }
    # what BepInEx and the plugin write at runtime (its config, test-hook files), under today's name and the old one (Valheimcraft)
    $generated = @('BepInEx\LogOutput.log', 'BepInEx\cache', 'BepInEx\config\BepInEx.cfg')
    foreach ($name in 'mineheim', 'valheimcraft') {
        $generated += @("BepInEx\config\dev.daolyap.$name.cfg", "BepInEx\$name-cmd.txt", "BepInEx\$name-player.csv", "BepInEx\$name-proj.csv")
    }
    foreach ($rel in ($added + $generated | Select-Object -Unique)) {
        $full = Join-Path $ValheimDir $rel
        if (Test-Path $full) { Remove-Item $full -Recurse -Force; Write-Host "removed $rel" }
    }
    # tidy empty folders that we created (never touch a folder that still holds something else)
    foreach ($dir in 'BepInEx\plugins\MineHeim', 'BepInEx\plugins\Valheimcraft', 'BepInEx\plugins', 'BepInEx\core', 'BepInEx\config', 'BepInEx') {
        $full = Join-Path $ValheimDir $dir
        if ((Test-Path $full) -and -not (Get-ChildItem $full -Force)) { Remove-Item $full -Force; Write-Host "removed empty $dir" }
    }
    $left = Join-Path $ValheimDir 'BepInEx'
    if (Test-Path $left) { Write-Warning "BepInEx\ still holds files that are not ours; left in place: $((Get-ChildItem $left -Recurse -File | Measure-Object).Count) file(s)" }
    Remove-Item $manifestPath -Force
    return
}

if (-not (Test-Path $PluginDll)) { throw "Plugin DLL not built: $PluginDll  (dotnet build -c Release in valheim\MineHeim)" }
$added = [System.Collections.Generic.List[string]]::new()
Read-Manifest | ForEach-Object { $added.Add($_) }

function Add-File($src, $rel) {
    $dst = Join-Path $ValheimDir $rel
    New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
    Copy-Item $src $dst -Force
    if (-not $added.Contains($rel)) { $added.Add($rel) }
}

if (-not $PluginOnly) {
    if (-not (Test-Path (Join-Path $Pack 'winhttp.dll'))) { throw "BepInEx pack not found at $Pack (run valheim\fetch_deps.ps1)" }
    $mine = Test-Path $manifestPath
    foreach ($check in 'winhttp.dll', 'doorstop_config.ini', 'BepInEx') {
        $full = Join-Path $ValheimDir $check
        # a BepInEx folder holding only empty folders (left by an earlier removal) is nobody's
        $taken = (Test-Path $full) -and (-not (Test-Path $full -PathType Container) -or (Get-ChildItem $full -Recurse -File -Force | Select-Object -First 1))
        if ($taken -and -not $mine -and -not $Force) {
            throw "$check already exists in the Valheim folder and was not installed by this script. Not overwriting (use -Force)."
        }
    }
    Add-File (Join-Path $Pack 'winhttp.dll') 'winhttp.dll'
    Add-File (Join-Path $Pack 'doorstop_config.ini') 'doorstop_config.ini'
    if (Test-Path (Join-Path $Pack '.doorstop_version')) { Add-File (Join-Path $Pack '.doorstop_version') '.doorstop_version' }
    Get-ChildItem (Join-Path $Pack 'BepInEx\core') -File | ForEach-Object { Add-File $_.FullName "BepInEx\core\$($_.Name)" }
    # the pack's cfg ships with a console window that never closes; logs go to LogOutput.log instead
    $cfgRel = 'BepInEx\config\BepInEx.cfg'
    Add-File (Join-Path $Pack 'BepInEx\config\BepInEx.cfg') $cfgRel
    $cfgPath = Join-Path $ValheimDir $cfgRel
    $cfg = Get-Content $cfgPath -Raw
    $cfg = [regex]::Replace($cfg, '(?s)(\[Logging\.Console\].*?\nEnabled\s*=\s*)true', '${1}false')
    $cfg = [regex]::Replace($cfg, '(?s)(\[Logging\.Console\].*?\nPreventClose\s*=\s*)true', '${1}false')
    Set-Content $cfgPath $cfg -NoNewline
}

Add-File $PluginDll $pluginRel
$pdb = [System.IO.Path]::ChangeExtension($PluginDll, '.pdb')
if (Test-Path $pdb) { Add-File $pdb 'BepInEx\plugins\MineHeim\MineHeim.pdb' }
Set-Content $manifestPath ($added -join "`r`n")
Write-Host "Installed. $($added.Count) path(s) recorded in $manifestPath"
Write-Host "Log after launching: $ValheimDir\BepInEx\LogOutput.log"
Write-Host 'Undo with: .\valheim\install.ps1 -Remove'
