<#
.SYNOPSIS
  Downloads what compositor\install.ps1 needs into third_party\ (never committed, never redistributed by this repo):
    third_party\reshade-runtime\ReShade64.dll   ReShade 6.8.0 with add-on support, taken out of the official installer
                                                ReShade_Setup_6.8.0_Addon.exe from reshade.me (the installer is never run:
                                                it carries its DLLs as a zip appended to the exe)
    third_party\shaders\*                       ReShade.fxh, ReShadeUI.fxh, DisplayDepth.fx, DisplayDepth_L10N.fxh
                                                (crosire/reshade-shaders, pinned to the commit we tested)
  With -Headers also (only needed to build the add-on):
    third_party\reshade\*.hpp                   ReShade's add-on API headers (crosire/reshade, tag v6.8.0)
  Every file is checked against the SHA-256 of the copy that was tested. No Python needed.
#>
param([switch]$Headers)
$ErrorActionPreference = 'Stop'
$root = Join-Path (Split-Path $PSScriptRoot -Parent) 'third_party'
$ua = @{ 'User-Agent' = 'mineheim-fetch/0.1' }

$installerSha = 'AFE4C8F13048306307983B8B3D41D5BF00A86820440B0E57DEA10950E1176445'
$dllSha = '0CEE63F9C9F13F3AC909C5B4903F4DBB4B719A7AB3B4F13B0DEAF83C814B94F7'
$shadersCommit = 'fd0022170615ce0d8162d219bff07232fa6dd84f'
$shaders = [ordered]@{
    'ReShade.fxh'           = '6DABFBBAF968C3871905D2EA17F96572FF7B1CEC01310B5D0E5252B66B30174F'
    'ReShadeUI.fxh'         = '78ADF672DF47460297EB9FE6DD238D2AAFA24510B52B84FEB1A745DFF70EB901'
    'DisplayDepth.fx'       = 'B67944BB8A5C6C8127813D332F1FB271222F7AA2C365927B4CBAC42BEBF749C9'
    'DisplayDepth_L10N.fxh' = 'F7D896885ECDB3F4953E069A09C16381CEB0C3A87D12025C3496D6986E67507D'
}

function Assert-Hash($path, $expected) {
    $h = (Get-FileHash $path -Algorithm SHA256).Hash
    if ($h -ne $expected) { throw "Unexpected SHA-256 for $path : $h (expected $expected). Delete it and try again; do not install it." }
}

$rt = Join-Path $root 'reshade-runtime'
$sh = Join-Path $root 'shaders'
New-Item -ItemType Directory -Force $rt, $sh | Out-Null

$exe = Join-Path $rt 'ReShade_Setup_6.8.0_Addon.exe'
if (-not (Test-Path $exe)) {
    Invoke-WebRequest -UseBasicParsing 'https://reshade.me/downloads/ReShade_Setup_6.8.0_Addon.exe' -OutFile $exe `
        -Headers ($ua + @{ 'Referer' = 'https://reshade.me/' })
}
Assert-Hash $exe $installerSha

# the zip is appended to the exe and its offsets count from the zip's own start: find that start from the end record
$bytes = [IO.File]::ReadAllBytes($exe)
$eocd = -1
for ($i = $bytes.Length - 22; $i -ge [Math]::Max(0, $bytes.Length - 65557); $i--) {
    if ($bytes[$i] -eq 0x50 -and $bytes[$i + 1] -eq 0x4B -and $bytes[$i + 2] -eq 5 -and $bytes[$i + 3] -eq 6) { $eocd = $i; break }
}
if ($eocd -lt 0) { throw "No zip found inside $exe" }
$start = $eocd - [BitConverter]::ToUInt32($bytes, $eocd + 12) - [BitConverter]::ToUInt32($bytes, $eocd + 16)
$zipPath = Join-Path $rt 'ReShade_Setup_6.8.0_Addon.zip'
$fs = [IO.File]::Create($zipPath)
try { $fs.Write($bytes, $start, $bytes.Length - $start) } finally { $fs.Dispose() }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entry = $zip.GetEntry('ReShade64.dll')
    if (-not $entry) { throw 'ReShade64.dll not in the installer' }
    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $rt 'ReShade64.dll'), $true)
} finally { $zip.Dispose() }
Assert-Hash (Join-Path $rt 'ReShade64.dll') $dllSha

foreach ($f in $shaders.Keys) {
    $dst = Join-Path $sh $f
    if (-not (Test-Path $dst) -or (Get-FileHash $dst).Hash -ne $shaders[$f]) {
        Invoke-WebRequest -UseBasicParsing "https://raw.githubusercontent.com/crosire/reshade-shaders/$shadersCommit/Shaders/$f" -OutFile $dst -Headers $ua
    }
    Assert-Hash $dst $shaders[$f]
}
Write-Host "ReShade 6.8.0 runtime and shaders ready in $root (hashes checked)"

if ($Headers) {
    $api = [ordered]@{
        'reshade.hpp'              = 'CF3CD150920EA310B4B1E2E4F11AB8D7221A9FC18755F4891A026563311B524C'
        'reshade_api.hpp'          = '8D83EC8A620C48DA7A20D975BD27B3345B24D3F0D5B78AE4C4FE633D61B804EB'
        'reshade_api_device.hpp'   = 'A716C44B6FBB064DBBB0826687728F21AA71ED45E13564E4DC1CDF36DA5BDFA0'
        'reshade_api_format.hpp'   = '68CAA17FEF712043E810AB88770C15AF009AA3C7693E5C115DDC324F2D0809A7'
        'reshade_api_pipeline.hpp' = '5BDA8E07260C2C60F0FD18A78B11782AED8F69E5D24B5757874614CAAFDF416F'
        'reshade_api_resource.hpp' = '32CDAFE6D783813F3AC72AD43E426638D8D0FEF56BCB558FAD218A14CFB75EE3'
        'reshade_events.hpp'       = '2717F56E2BD89AF6D708C9C316353D6581C5A41D4526629FADBA51D13BE57A89'
        'reshade_overlay.hpp'      = 'ED914B12FBC41ED038287B0163C0BC934772AFCFF2C22392044C6008ADC5AE79'
    }
    $inc = Join-Path $root 'reshade'
    New-Item -ItemType Directory -Force $inc | Out-Null
    foreach ($f in $api.Keys) {
        $dst = Join-Path $inc $f
        if (-not (Test-Path $dst) -or (Get-FileHash $dst).Hash -ne $api[$f]) {
            Invoke-WebRequest -UseBasicParsing "https://raw.githubusercontent.com/crosire/reshade/v6.8.0/include/$f" -OutFile $dst -Headers $ua
        }
        Assert-Hash $dst $api[$f]
    }
    Write-Host "ReShade 6.8.0 add-on API headers ready in $inc (hashes checked)"
}
