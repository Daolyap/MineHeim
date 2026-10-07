<#
.SYNOPSIS
  Builds MineHeim.addon64 (the ReShade add-on) with the portable MSVC from tools\get_msvc.py.
  Output: compositor\build\MineHeim.addon64
#>
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $repo 'tools\msvc_env.ps1')
$out = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Force $out | Out-Null
$inc = Join-Path $repo 'third_party\reshade'
if (-not (Test-Path (Join-Path $inc 'reshade.hpp'))) { throw 'Run python compositor\fetch_deps.py first.' }
Push-Location $out
try {
    # /MT: no dependency on a VC runtime that may not be installed. The add-on talks to ReShade through its API only.
    cl /nologo /std:c++17 /EHsc /MT /O2 /W3 /LD /DNOMINMAX /DWIN32_LEAN_AND_MEAN /I $inc `
        (Join-Path $PSScriptRoot 'mineheim_addon.cpp') /Fe:MineHeim.addon64 /link /DLL kernel32.lib user32.lib
    if ($LASTEXITCODE -ne 0) { throw "cl failed ($LASTEXITCODE)" }
    Get-Item .\MineHeim.addon64 | Select-Object Name, Length, LastWriteTime
} finally { Pop-Location }
