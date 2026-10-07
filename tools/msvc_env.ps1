# Dot-source this to get cl.exe / link.exe / rc.exe on PATH for the portable toolchain from tools/get_msvc.py:
#   . .\tools\msvc_env.ps1
param([string]$Root = (Join-Path $env:USERPROFILE 'tools\msvc'))
$info = Get-Content (Join-Path $Root 'msvc-paths.json') -Raw | ConvertFrom-Json
$msvc = Join-Path $Root "VC\Tools\MSVC\$($info.msvc)"
$sdk = $info.sdk_root
$sv = $info.sdk
$env:INCLUDE = @("$msvc\include", "$sdk\Include\$sv\ucrt", "$sdk\Include\$sv\shared", "$sdk\Include\$sv\um", "$sdk\Include\$sv\winrt") -join ';'
$env:LIB = @("$msvc\lib\x64", "$sdk\Lib\$sv\ucrt\x64", "$sdk\Lib\$sv\um\x64") -join ';'
$env:PATH = "$msvc\bin\Hostx64\x64;$sdk\bin\$sv\x64;$env:PATH"
Write-Host "MSVC $($info.msvc), Windows SDK $sv on PATH (x64)"
