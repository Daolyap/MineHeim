<#
.SYNOPSIS
  Builds MineHeim.addon64 (the ReShade add-on). Output: compositor\build\MineHeim.addon64

.DESCRIPTION
  Needs MSVC (not MinGW: ReShade's add-on API is C++ classes whose layout depends on the compiler). It uses, in order:
  the portable MSVC from  python tools\get_msvc.py  (%USERPROFILE%\tools\msvc), a cl.exe already on PATH, or a Visual Studio /
  Build Tools install with the "Desktop development with C++" workload (found with vswhere). Fetches ReShade's API headers
  itself (compositor\fetch_deps.ps1 -Headers).
#>
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

function Use-Msvc {
    if (Test-Path (Join-Path $env:USERPROFILE 'tools\msvc\msvc-paths.json')) { . (Join-Path $repo 'tools\msvc_env.ps1'); return }
    if (Get-Command cl.exe -ErrorAction SilentlyContinue) { Write-Host 'cl.exe already on PATH'; return }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        $bat = if ($vs) { Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat' }
        if ($bat -and (Test-Path $bat)) {
            # take over the environment vcvars64.bat sets up
            cmd /c "`"$bat`" >nul && set" | ForEach-Object { if ($_ -match '^([^=]+)=(.*)$') { Set-Item "env:$($Matches[1])" $Matches[2] } }
            Write-Host "MSVC from $vs"
            return
        }
    }
    throw ("No MSVC found to build the compositor. Either run  python tools\get_msvc.py  (portable, no admin, ~1 GB)," +
           " or install Visual Studio Build Tools with 'Desktop development with C++'.")
}

$inc = Join-Path $repo 'third_party\reshade'
if (-not (Test-Path (Join-Path $inc 'reshade.hpp'))) { & (Join-Path $PSScriptRoot 'fetch_deps.ps1') -Headers }
Use-Msvc
$out = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Force $out | Out-Null
Push-Location $out
try {
    # /MT: no dependency on a VC runtime that may not be installed. The add-on talks to ReShade through its API only.
    cl /nologo /std:c++17 /EHsc /MT /O2 /W3 /LD /DNOMINMAX /DWIN32_LEAN_AND_MEAN /I $inc `
        (Join-Path $PSScriptRoot 'mineheim_addon.cpp') /Fe:MineHeim.addon64 /link /DLL kernel32.lib user32.lib
    if ($LASTEXITCODE -ne 0) { throw "cl failed ($LASTEXITCODE)" }
    Get-Item .\MineHeim.addon64 | Select-Object Name, Length, LastWriteTime
} finally { Pop-Location }
