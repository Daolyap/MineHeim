<#
.SYNOPSIS
  Step 6 oracle: blocks placed in Minecraft stop the Valheim player, and walk-through is possible again once they are broken.
  1. Builds a ring of Minecraft stone (walls 3 blocks from the player, 4 high) with server commands.
  2. Holds W for 2.5 s: the player must stop at the wall.
  3. Removes the ring and holds W again: the player must walk freely.
  Uses the plugin's test hooks (BepInEx/mineheim-cmd.txt, mineheim-player.csv). Drives the keyboard through
  `um win drive`: only when nobody is at the machine.
#>
param([string]$ValheimDir = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim', [int]$Half = 4, [int]$WalkMs = 4000)
$env:PATH += ";$env:USERPROFILE\.local\bin"; $env:PYTHONUTF8 = 1
$repo = Split-Path $PSScriptRoot -Parent
$py = Join-Path $repo '.venv\Scripts\python.exe'
$cmdFile = "$ValheimDir\BepInEx\mineheim-cmd.txt"
$csv = "$ValheimDir\BepInEx\mineheim-player.csv"
$log = "$ValheimDir\BepInEx\LogOutput.log"

function PluginCmd($c) { Set-Content $cmdFile $c; Start-Sleep -Milliseconds 1800 }
# run `status` and wait for its NEW line in the log (a stale read gave a wrong count once)
function StatusColliders() {
    $before = (Select-String -Path $log -Pattern 'status: colliders=' | Measure-Object).Count
    Set-Content $cmdFile 'status'
    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Milliseconds 500
        $all = Select-String -Path $log -Pattern 'status: colliders=(\d+)'
        if (($all | Measure-Object).Count -gt $before) { return [int]($all | Select-Object -Last 1).Matches[0].Groups[1].Value }
    }
    -1
}
function LastRow() { $l = Get-Content $csv -Tail 1; $v = $l.Split(','); [pscustomobject]@{ t = [double]$v[0]; x = [double]$v[1]; y = [double]$v[2]; z = [double]$v[3]; mx = [double]$v[4]; my = [double]$v[5]; mz = [double]$v[6] } }
function Colliders() { $s = Select-String -Path $log -Pattern 'status: colliders=(\d+)' | Select-Object -Last 1; if ($s) { [int]$s.Matches[0].Groups[1].Value } else { -1 } }
# ReShade's own screenshot (window capture goes stale under ReShade), copied to the evidence folder
function Shot($name) {
    um win drive --proc valheim 'focus' 'hold 0x2C 250' 2>&1 | Out-Null
    Start-Sleep 5
    $new = Get-ChildItem "$ValheimDir\reshade-screens" -Filter '*.png' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $dst = Join-Path $repo (Join-Path "test-output" "collision")
    New-Item -ItemType Directory -Force $dst | Out-Null
    Copy-Item $new.FullName (Join-Path $dst "$name.png") -Force
}
function Walk($label) {
    $start = LastRow
    um win drive --proc valheim 'focus' "hold 0x57 $WalkMs" 2>&1 | Out-Null
    Start-Sleep 1.5
    $rows = Get-Content $csv | Select-Object -Skip 1 | ForEach-Object { $v = $_.Split(','); [pscustomobject]@{ t = [double]$v[0]; x = [double]$v[1]; z = [double]$v[3] } } | Where-Object { $_.t -ge $start.t }
    $far = ($rows | ForEach-Object { [math]::Sqrt(($_.x - $start.x) * ($_.x - $start.x) + ($_.z - $start.z) * ($_.z - $start.z)) } | Measure-Object -Maximum).Maximum
    # distance over time counted from the moment the player starts to move (the key press arrives a few seconds after the sample)
    $dist = $rows | ForEach-Object { [pscustomobject]@{ t = $_.t; d = [math]::Sqrt(($_.x - $start.x) * ($_.x - $start.x) + ($_.z - $start.z) * ($_.z - $start.z)) } }
    $t0 = ($dist | Where-Object { $_.d -gt 0.05 } | Select-Object -First 1).t
    $series = foreach ($sec in 0.5, 1, 1.5, 2, 2.5, 3, 3.5, 4) {
        $r = $dist | Where-Object { $_.t -le $t0 + $sec } | Select-Object -Last 1
        if ($r) { '{0:N1}' -f $r.d }
    }
    Write-Host ("{0}: moved up to {1:N2} m from where it started; distance over time (m): {2}" -f $label, $far, ($series -join ' '))
    [pscustomobject]@{ label = $label; far = $far }
}

PluginCmd 'playerlog on'
Start-Sleep 2
$p = LastRow
$cx = [int][math]::Floor($p.mx); $cz = [int][math]::Floor($p.mz); $y0 = [int][math]::Floor($p.my); $y1 = $y0 + 3
Write-Host ("player at Minecraft ({0:N2}, {1:N2}, {2:N2}); ring cells x {3}..{4}, z {5}..{6}, y {7}..{8}" -f $p.mx, $p.my, $p.mz, ($cx - $Half), ($cx + $Half), ($cz - $Half), ($cz + $Half), $y0, $y1)
$walls = @(
    "fill $($cx - $Half) $y0 $($cz - $Half) $($cx - $Half) $y1 $($cz + $Half)",
    "fill $($cx + $Half) $y0 $($cz - $Half) $($cx + $Half) $y1 $($cz + $Half)",
    "fill $($cx - $Half + 1) $y0 $($cz - $Half) $($cx + $Half - 1) $y1 $($cz - $Half)",
    "fill $($cx - $Half + 1) $y0 $($cz + $Half) $($cx + $Half - 1) $y1 $($cz + $Half)")

$base = StatusColliders
Write-Host "colliders before the test: $base"
# 1. build the ring
& $py (Join-Path $repo 'host\cmd.py') @($walls | ForEach-Object { "$_ minecraft:stone" }) | Out-Null
Start-Sleep 4
$built = StatusColliders
Write-Host "after building: $built block colliders in Valheim"
Shot 'ring_built'
$blocked = Walk 'with the ring'

# 2. break the ring
& $py (Join-Path $repo 'host\cmd.py') @($walls | ForEach-Object { "$_ minecraft:air" }) | Out-Null
Start-Sleep 4
$after = StatusColliders
Write-Host "after breaking: $after block colliders in Valheim"
Shot 'ring_broken'
$free = Walk 'after breaking the ring'

PluginCmd 'playerlog off'
""
"RESULT: blocked walk {0:N2} m (limit 5.0), free walk {1:N2} m (needs > 5.5); colliders before {2}, with the ring {3}, after breaking {4}" -f $blocked.far, $free.far, $base, $built, $after
if ($blocked.far -lt 5.0 -and $free.far -gt 5.5 -and ($built - $base) -gt 50 -and ($after - $base) -lt 5) { 'PASS' } else { 'FAIL' }
