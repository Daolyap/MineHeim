<#
.SYNOPSIS
  One-step install (or removal) of MineHeim: BepInEx + the plugin and ReShade + the compositor add-on in Valheim, and
  Prism Launcher with a ready Minecraft instance.

.DESCRIPTION
  1. Finds Valheim (Steam libraries) unless -ValheimDir is given.
  2. Downloads the BepInEx pack (Thunderstore) and ReShade 6.8.0 (reshade.me) into third_party\, checking SHA-256 hashes.
  3. Runs valheim\install.ps1 and compositor\install.ps1 -Effect MineHeim. Each writes a list of exactly what it added
     into the Valheim folder (mineheim-install.txt, mineheim-reshade-install.txt); -Remove takes exactly those out.
  4. Runs prism\install.ps1: Prism Launcher (portable) with a ready "MineHeim" Minecraft instance in
     %LOCALAPPDATA%\MineHeim\PrismLauncher; you sign in once with your own Microsoft account. -NoMinecraft skips it
     (then set up a Fabric profile yourself, see README.md).
  An install from before the rename (Valheimcraft) is removed first. Valheim must be closed. -Remove takes out the
  Valheim side; add -IncludeMinecraft to remove the Prism Launcher and its MineHeim instance (and that instance's world) too.

  Release zip or source? The release zip is prebuilt: nothing to build. In a copy of the source (git clone) whatever is
  not built yet, or was edited since its last build, is built first (tools\build.ps1: .NET SDK, JDK 25 and MSVC, each found automatically; it tells you what
  to install if one is missing). -Build rebuilds even what is already built: use it after changing code.
  -Only installs (and builds) just the parts you name: Valheim (BepInEx + plugin), Compositor (ReShade + add-on),
  Minecraft (Prism + the instance's mod jars).

.EXAMPLE
  .\Install-MineHeim.ps1
  .\Install-MineHeim.ps1 -ValheimDir 'D:\SteamLibrary\steamapps\common\Valheim'
  .\Install-MineHeim.ps1 -Build                       # source: rebuild everything, install everything
  .\Install-MineHeim.ps1 -Build -Only Valheim         # source: changed the plugin? rebuild and reinstall just that
  .\Install-MineHeim.ps1 -Build -Only Minecraft       # source: changed the Fabric mod? rebuild and update Prism's instance
  .\Install-MineHeim.ps1 -Remove
#>
param([string]$ValheimDir, [switch]$Remove, [switch]$Force, [switch]$NoMinecraft, [switch]$IncludeMinecraft,
      [switch]$Build, [string[]]$Only)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$source = Test-Path (Join-Path $here 'valheim\MineHeim\MineHeim.csproj')
# "-Only Valheim,Minecraft" arrives as one string through Install.cmd (powershell -File): split it here
$Only = @($Only | ForEach-Object { $_ -split '[,; ]+' } | Where-Object { $_ })
foreach ($p in $Only) { if ($p -notin 'Valheim', 'Compositor', 'Minecraft') { throw "-Only takes Valheim, Compositor and/or Minecraft, not '$p'." } }
$parts = if ($Only) { $Only } elseif ($NoMinecraft) { @('Valheim', 'Compositor') } else { @('Valheim', 'Compositor', 'Minecraft') }

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
if (($Remove -or 'Valheim' -in $parts -or 'Compositor' -in $parts) -and (Get-Process valheim -ErrorAction SilentlyContinue)) { throw 'Valheim is running: close it first.' }
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

# a copy of the source: build what is missing (or everything asked for, with -Build)
if ($source) {
    # built = the output exists and is newer than every source file of that part (an edit since means: build again)
    function Test-Fresh($output, $sources) {
        $out = Get-ChildItem $output -ErrorAction SilentlyContinue | Where-Object Name -notmatch 'sources' | Sort-Object LastWriteTime | Select-Object -Last 1
        if (-not $out) { return $false }
        $newest = Get-ChildItem $sources -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\(bin|obj|build)\\' } | Sort-Object LastWriteTime | Select-Object -Last 1
        return -not $newest -or $newest.LastWriteTime -le $out.LastWriteTime
    }
    $built = @{
        Valheim    = { Test-Fresh (Join-Path $here 'valheim\MineHeim\bin\Release\net472\MineHeim.dll') @((Join-Path $here 'valheim\MineHeim'), (Join-Path $here 'valheim\Core')) }
        Compositor = { Test-Fresh (Join-Path $here 'compositor\build\MineHeim.addon64') @((Join-Path $here 'compositor\mineheim_addon.cpp')) }
        Minecraft  = { Test-Fresh (Join-Path $here 'mc\build\libs\mineheim-*.jar') @((Join-Path $here 'mc\src'), (Join-Path $here 'mc\gradle.properties'), (Join-Path $here 'mc\build.gradle')) }
    }
    $toBuild = @($parts | Where-Object { $Build -or -not (& $built[$_]) })
    if ($toBuild) {
        Write-Host "Building from source: $($toBuild -join ', ')"
        & (Join-Path $here 'tools\build.ps1') -Parts $toBuild -ValheimDir $ValheimDir
    }
} elseif ($Build) {
    throw 'This is a release package: it is already built. -Build is for a copy of the source code (git clone).'
}

if ('Minecraft' -in $parts) {
    $mc = Get-CimInstance Win32_Process -Filter "Name like 'java%'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -like '*MineHeim*PrismLauncher*' }
    if ($mc) { throw "Minecraft (the MineHeim instance) is still running (PID $($mc.ProcessId -join ', ')): close it first." }
}
if ('Valheim' -in $parts) {
    & (Join-Path $here 'valheim\fetch_deps.ps1')
    & (Join-Path $here 'valheim\install.ps1') -ValheimDir $ValheimDir -Force:$Force
}
if ('Compositor' -in $parts) {
    & (Join-Path $here 'compositor\fetch_deps.ps1')
    & (Join-Path $here 'compositor\install.ps1') -ValheimDir $ValheimDir -Effect MineHeim -Force:$Force
}

Write-Host ''
if ($Only) {
    if ('Minecraft' -in $parts) { & (Join-Path $here 'prism\install.ps1') -NoOpen }
    Write-Host "Installed: $($parts -join ', ')."
} elseif ($NoMinecraft) {
    $jars = if (Test-Path (Join-Path $here 'minecraft')) { Join-Path $here 'minecraft' } else { Join-Path $here 'mc\build\libs' }
    Write-Host 'Valheim side installed. Minecraft (once, your own way): a Fabric 0.19.5 profile for Minecraft 26.3 with its own'
    Write-Host "game directory, and the two jars from $jars in its mods folder. Start it, then Valheim."
} else {
    & (Join-Path $here 'prism\install.ps1')
    Write-Host ''
    Write-Host 'Done. Start Valheim: Minecraft starts by itself in the background. In a world, F7 = Minecraft mode.'
}
Write-Host 'Remove it all again with Uninstall.cmd.'
