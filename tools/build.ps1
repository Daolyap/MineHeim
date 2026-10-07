<#
.SYNOPSIS
  Builds MineHeim from source. Finds the tools itself and says exactly what to install when one is missing.

.DESCRIPTION
  Parts (all three by default):
    Valheim     the BepInEx plugin  -> valheim\MineHeim\bin\Release\net472\MineHeim.dll   needs: .NET SDK 8 or newer
                (compiles against YOUR Valheim install; the BepInEx pack is downloaded into third_party\ first)
    Minecraft   the Fabric mod      -> mc\build\libs\mineheim-<version>.jar                needs: a JDK 25
    Compositor  the ReShade add-on  -> compositor\build\MineHeim.addon64                   needs: MSVC (see compositor\build.ps1)
  -Test also runs the Unity-free unit tests (valheim\Core.Tests).
  Usually called through  .\Install-MineHeim.ps1 -Build , which builds and then installs.

.EXAMPLE
  .\tools\build.ps1
  .\tools\build.ps1 -Parts Valheim -ValheimDir 'D:\SteamLibrary\steamapps\common\Valheim'
#>
param(
    [ValidateSet('Valheim', 'Minecraft', 'Compositor')][string[]]$Parts = @('Valheim', 'Minecraft', 'Compositor'),
    [string]$ValheimDir,
    [switch]$Test
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

function Find-Dotnet {
    # a dotnet.exe on PATH can be a runtime without any SDK: look for a real SDK 8+
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    foreach ($exe in @($(if ($onPath) { $onPath.Source }), (Join-Path $env:USERPROFILE 'tools\dotnet\dotnet.exe'), "$env:ProgramFiles\dotnet\dotnet.exe")) {
        if (-not $exe -or -not (Test-Path $exe)) { continue }
        $sdks = & $exe --list-sdks 2>$null | Where-Object { $_ -match '^(\d+)\.' -and [int]$Matches[1] -ge 8 }
        if ($sdks) {
            $dir = Split-Path $exe
            $env:DOTNET_ROOT = $dir; $env:PATH = "$dir;$env:PATH"
            return
        }
    }
    throw 'No .NET SDK found. Install it with:  winget install Microsoft.DotNet.SDK.8   (any SDK 8 or newer works), then open a new terminal.'
}

function Get-JdkMajor($home_) {
    $release = Join-Path $home_ 'release'
    if (-not (Test-Path (Join-Path $home_ 'bin\javac.exe')) -or -not (Test-Path $release)) { return 0 }
    $m = [regex]::Match((Get-Content $release -Raw), 'JAVA_VERSION="(\d+)')
    if ($m.Success) { [int]$m.Groups[1].Value } else { 0 }
}

function Find-Jdk25 {
    $candidates = @($env:JAVA_HOME, (Join-Path $env:USERPROFILE 'tools\jdk-25'))
    $javac = Get-Command javac -ErrorAction SilentlyContinue
    if ($javac) { $candidates += (Split-Path (Split-Path $javac.Source)) }
    foreach ($base in "$env:ProgramFiles\Eclipse Adoptium", "$env:ProgramFiles\Java", "$env:ProgramFiles\Microsoft", "$env:ProgramFiles\Zulu") {
        if (Test-Path $base) { $candidates += (Get-ChildItem $base -Directory -Filter 'jdk*' | ForEach-Object FullName) }
    }
    foreach ($c in ($candidates | Where-Object { $_ } | Select-Object -Unique)) {
        if ((Get-JdkMajor $c) -ge 25) { return $c }
    }
    throw 'No JDK 25 found (the Minecraft mod targets Java 25). Install one with:  winget install EclipseAdoptium.Temurin.25.JDK   then open a new terminal.'
}

if ('Valheim' -in $Parts) {
    Find-Dotnet
    & (Join-Path $repo 'valheim\fetch_deps.ps1')
    $props = @()
    if ($ValheimDir) { $props += "-p:ValheimDir=$ValheimDir" }
    dotnet build (Join-Path $repo 'valheim\MineHeim\MineHeim.csproj') -c Release -v q --nologo @props
    if ($LASTEXITCODE) { throw 'Valheim plugin build failed (see above)' }
    if ($Test) {
        Push-Location (Join-Path $repo 'valheim\Core.Tests'); try { dotnet run -c Release; if ($LASTEXITCODE) { throw 'Core.Tests failed' } } finally { Pop-Location }
    }
    Write-Host 'built: valheim\MineHeim\bin\Release\net472\MineHeim.dll'
}

if ('Minecraft' -in $Parts) {
    $env:JAVA_HOME = Find-Jdk25
    Write-Host "JDK: $env:JAVA_HOME"
    Push-Location (Join-Path $repo 'mc')
    try { .\gradlew.bat --console=plain build; if ($LASTEXITCODE) { throw 'Minecraft mod build failed (see above)' } } finally { Pop-Location }
    Write-Host 'built: mc\build\libs\mineheim-*.jar'
}

if ('Compositor' -in $Parts) {
    & (Join-Path $repo 'compositor\build.ps1')
}
