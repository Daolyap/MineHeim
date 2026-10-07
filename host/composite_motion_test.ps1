<#
.SYNOPSIS
  Step 5 latency oracle: take ReShade screenshots WHILE the player strafes, and compare where the pillar landed with
  Unity's own projection for the pose that frame was composited with (host/composite_oracle.py). Run it with the
  compositor's "Reproject" on and off to see what the re-projection buys.
  Needs Valheim in the world, the F9 pillar visible, neutral look on (compositor/neutral_look.py on).
#>
param([int]$Shots = 6, [string]$Tag = 'motion', [string]$ValheimDir = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim')
$env:PATH += ";$env:USERPROFILE\.local\bin"; $env:PYTHONUTF8 = 1
$repo = Split-Path $PSScriptRoot -Parent
$py = Join-Path $repo '.venv\Scripts\python.exe'
$out = Join-Path $repo "test-output\composite_motion"
New-Item -ItemType Directory -Force $out | Out-Null
$results = @()
for ($i = 1; $i -le $Shots; $i++) {
    $key = if ($i % 2) { '0x44' } else { '0x41' }   # D then A: strafe right, then left
    $before = (Get-ChildItem "$ValheimDir\reshade-screens" -Filter '*.png' | Sort-Object LastWriteTime -Descending | Select-Object -First 1).Name
    um win drive --proc valheim 'focus' "key $key down" 'hold 0x2C 250' "key $key up" 2>&1 | Out-Null
    Start-Sleep 4
    $new = Get-ChildItem "$ValheimDir\reshade-screens" -Filter '*.png' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($new.Name -eq $before) { "shot ${i}: no new screenshot"; continue }
    $dst = Join-Path $out "${Tag}_$i.png"
    Copy-Item $new.FullName $dst -Force; Copy-Item ($new.FullName + '.txt') ($dst + '.txt') -Force
    $res = & $py (Join-Path $repo 'host\composite_oracle.py') $dst 2>&1
    "shot ${i}: " + (($res | Select-String 'delta px|NO ROW|not found|off screen|behind') -join ' | ')
    $results += $res
}
