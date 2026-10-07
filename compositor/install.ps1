<#
.SYNOPSIS
  Installs (or removes) ReShade 6.8.0 (add-on build) for Valheim, with either the DisplayDepth check or the MineHeim
  compositor, and records exactly what it added.

.DESCRIPTION
  ReShade goes in as dxgi.dll (Valheim uses Direct3D 11). Adds: dxgi.dll, ReShade.ini, ReShadePreset.ini,
  reshade-shaders\Shaders\*, and with -Effect MineHeim the add-on MineHeim.addon64. Every added path is listed in
  <ValheimDir>\mineheim-reshade-install.txt; -Remove deletes exactly those plus what ReShade generates at runtime
  (ReShade.log, ReShade screenshots folder). Stops instead of replacing a dxgi.dll / ReShade.ini that is not its own.
  Run  .\compositor\fetch_deps.ps1  first (or the one-step .\Install-MineHeim.ps1).

.EXAMPLE
  .\compositor\install.ps1 -Effect DisplayDepth     # step 5a: is depth visible?
  .\compositor\install.ps1 -Effect MineHeim     # the compositor (after build.ps1)
  .\compositor\install.ps1 -Remove
#>
param(
    [string]$ValheimDir = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim',
    [ValidateSet('DisplayDepth', 'MineHeim')][string]$Effect = 'DisplayDepth',
    [switch]$Remove,
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$third = Join-Path $root 'third_party'
$manifest = Join-Path $ValheimDir 'mineheim-reshade-install.txt'
if (-not (Test-Path (Join-Path $ValheimDir 'valheim.exe'))) { throw "valheim.exe not found in $ValheimDir" }
if (Get-Process valheim -ErrorAction SilentlyContinue) { throw 'Valheim is running: close it first.' }

function Read-Manifest { if (Test-Path $manifest) { Get-Content $manifest | Where-Object { $_ } } else { @() } }

if ($Remove) {
    $added = Read-Manifest
    if (-not $added) { Write-Host 'Nothing recorded as installed.'; return }
    $generated = @('ReShade.log', 'ReShade.ini', 'ReShadePreset.ini', 'reshade-screens', 'ReShade_shaders_cache', 'reshade-shaders\Shaders\ReShade.fxh.bak')
    foreach ($rel in ($added + $generated | Select-Object -Unique)) {
        $full = Join-Path $ValheimDir $rel
        if (Test-Path $full) { Remove-Item $full -Recurse -Force; Write-Host "removed $rel" }
    }
    foreach ($dir in 'reshade-shaders\Shaders', 'reshade-shaders\Textures', 'reshade-shaders') {
        $full = Join-Path $ValheimDir $dir
        if ((Test-Path $full) -and -not (Get-ChildItem $full -Force)) { Remove-Item $full -Force; Write-Host "removed empty $dir" }
    }
    Remove-Item $manifest -Force
    return
}

$runtime = Join-Path $third 'reshade-runtime\ReShade64.dll'
if (-not (Test-Path $runtime)) { throw 'Run .\compositor\fetch_deps.ps1 first.' }
$mine = Test-Path $manifest
foreach ($check in 'dxgi.dll', 'ReShade.ini', 'ReShadePreset.ini') {
    if ((Test-Path (Join-Path $ValheimDir $check)) -and -not $mine -and -not $Force) {
        throw "$check already exists in the Valheim folder and was not installed by this script. Not overwriting (use -Force)."
    }
}
$added = [System.Collections.Generic.List[string]]::new()
Read-Manifest | ForEach-Object { $added.Add($_) }
function Add-File($src, $rel) {
    $dst = Join-Path $ValheimDir $rel
    New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
    Copy-Item $src $dst -Force
    if (-not $added.Contains($rel)) { $added.Add($rel) }
}
function Add-Text($text, $rel) {
    $dst = Join-Path $ValheimDir $rel
    Set-Content -Path $dst -Value $text -NoNewline -Encoding ASCII
    if (-not $added.Contains($rel)) { $added.Add($rel) }
}

Add-File $runtime 'dxgi.dll'
foreach ($f in 'ReShade.fxh', 'ReShadeUI.fxh', 'DisplayDepth.fx', 'DisplayDepth_L10N.fxh') { Add-File (Join-Path $third "shaders\$f") "reshade-shaders\Shaders\$f" }
if ($Effect -eq 'MineHeim') {
    Add-File (Join-Path $PSScriptRoot 'MineHeim.fx') 'reshade-shaders\Shaders\MineHeim.fx'
    $addon = Join-Path $PSScriptRoot 'build\MineHeim.addon64'
    if (-not (Test-Path $addon)) { throw 'Build the add-on first: .\compositor\build.ps1' }
    Add-File $addon 'MineHeim.addon64'
}
New-Item -ItemType Directory -Force (Join-Path $ValheimDir 'reshade-shaders\Textures') | Out-Null

# Direct3D 11, reversed-Z depth (SystemInfo.usesReversedZBuffer = true in Valheim 1.0.17), and the depth buffer reaches
# ReShade upside down (seen with DisplayDepth.fx in gameplay). Generic Depth must copy the
# depth buffer before it is cleared, or the effect sees an empty one.
$ini = @"
[GENERAL]
EffectSearchPaths=.\reshade-shaders\Shaders\
TextureSearchPaths=.\reshade-shaders\Textures\
PresetPath=.\ReShadePreset.ini
PreprocessorDefinitions=RESHADE_DEPTH_INPUT_IS_REVERSED=1,RESHADE_DEPTH_INPUT_IS_UPSIDE_DOWN=1,RESHADE_DEPTH_INPUT_IS_LOGARITHMIC=0,RESHADE_DEPTH_LINEARIZATION_FAR_PLANE=1000
NoReloadOnInit=0

[ADDON]
AddonPath=.

[DEPTH]
DepthCopyBeforeClears=1
DepthCopyAtClearIndex=0
UseAspectRatioHeuristics=1

[OVERLAY]
TutorialProgress=4
ShowClock=0
ShowFPS=0

[SCREENSHOT]
SavePath=.\reshade-screens\
FileFormat=1
"@
Add-Text ($ini -replace "`r?`n", "`r`n") 'ReShade.ini'
if ($Effect -eq 'DisplayDepth') {
    Add-Text "Techniques=DisplayDepth@DisplayDepth.fx`r`nTechniqueSorting=DisplayDepth@DisplayDepth.fx`r`n" 'ReShadePreset.ini'
} else {
    Add-Text "Techniques=MineHeim@MineHeim.fx`r`nTechniqueSorting=MineHeim@MineHeim.fx`r`n" 'ReShadePreset.ini'
}
Set-Content $manifest ($added -join "`r`n")
Write-Host "Installed ReShade ($Effect): $($added.Count) path(s) recorded in $manifest"
Write-Host 'Undo with: .\compositor\install.ps1 -Remove'
