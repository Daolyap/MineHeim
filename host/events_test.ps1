<#
.SYNOPSIS
  Step 8 oracle: what happens in Minecraft hurts things in Valheim.
    1. TNT in Minecraft 4 m from a stationary Greydwarf: it loses health and is thrown back.
    2. A Minecraft sword swing (key 2, left click) on a Troll in reach: it loses health.
    3. A Minecraft arrow (key 3, hold the right button, release) at a Troll: it loses health, and the arrow is reported back.
    4. A Minecraft pickaxe swing (key 1, left click) at a rock in reach: Valheim's rock takes the hit.
  The creatures are spawned by the plugin's test hooks with their AI off, and cleaned up afterwards.
  Drives mouse and keyboard through `um win drive`: only when nobody is at the machine. Stand in open ground.
#>
param([string]$ValheimDir = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim')
$env:PATH += ";$env:USERPROFILE\.local\bin"; $env:PYTHONUTF8 = 1
$repo = Split-Path $PSScriptRoot -Parent
$cmdFile = "$ValheimDir\BepInEx\mineheim-cmd.txt"
$log = "$ValheimDir\BepInEx\LogOutput.log"
$fails = @()

function PluginCmd($c) { Set-Content $cmdFile $c; Start-Sleep -Milliseconds 1800 }
function Wait-Log($pattern, $before, $seconds = 12) {
    for ($i = 0; $i -lt ($seconds * 2); $i++) {
        $m = Select-String -Path $log -Pattern $pattern
        if (($m | Measure-Object).Count -gt $before) { return ($m | Select-Object -Last 1).Line }
        Start-Sleep -Milliseconds 500
    }
    $null
}
function Count($pattern) { (Select-String -Path $log -Pattern $pattern | Measure-Object).Count }
# `creatures`: hp and position of the first spawned creature
function Creature() {
    $b = Count 'creature 0 '
    Set-Content $cmdFile 'creatures'
    $l = Wait-Log 'creature 0 ' $b
    if (-not $l) { throw 'no creature report' }
    $m = [regex]::Match($l, 'hp ([\d.]+)/([\d.]+) dead=(\w+) pos=([-\d.]+),([-\d.]+),([-\d.]+)')
    [pscustomobject]@{ hp = [double]$m.Groups[1].Value; max = [double]$m.Groups[2].Value; dead = $m.Groups[3].Value; x = [double]$m.Groups[4].Value; y = [double]$m.Groups[5].Value; z = [double]$m.Groups[6].Value }
}
function Status() {
    $b = Count 'status: colliders='
    Set-Content $cmdFile 'status'
    $l = Wait-Log 'status: colliders=' $b
    $h = @{}
    foreach ($m in [regex]::Matches($l, '(\w+)=([\w.-]+)')) { $h[$m.Groups[1].Value] = $m.Groups[2].Value }
    $h
}
function Drive() { um win drive --proc valheim 'focus' @args 2>&1 | Out-Null; Start-Sleep 2 }
function Check($ok, $what) { if ($ok) { Write-Host "  PASS  $what" } else { Write-Host "  FAIL  $what"; $script:fails += $what } }

PluginCmd 'cleanup'
PluginCmd 'playerlog on'

Write-Host '1. TNT 4 m from a Greydwarf'
PluginCmd 'spawn Greydwarf 7'
Start-Sleep 3
$c0 = Creature
Write-Host ("  before: hp {0}/{1}, at {2:N2},{3:N2},{4:N2}" -f $c0.hp, $c0.max, $c0.x, $c0.y, $c0.z)
$s0 = Status
PluginCmd 'tnt 4'
Start-Sleep 4
$c1 = Creature
$s1 = Status
$moved = [math]::Sqrt(($c1.x - $c0.x) * ($c1.x - $c0.x) + ($c1.z - $c0.z) * ($c1.z - $c0.z))
Write-Host ("  after:  hp {0}/{1}, moved {2:N2} m, explosions {3} -> {4}" -f $c1.hp, $c1.max, $moved, $s0.explosions, $s1.explosions)
Check ([int]$s1.explosions -gt [int]$s0.explosions) 'the plugin received the explosion'
Check ($c1.hp -lt $c0.hp) "the Greydwarf lost health ($($c0.hp) -> $($c1.hp))"
Check ($moved -gt 0.3 -or $c1.dead -eq 'True') "the blast threw it back ($('{0:N2}' -f $moved) m)"
PluginCmd 'cleanup'

Write-Host '2. Minecraft sword swing at a Troll in reach'
PluginCmd 'inputmode on'
PluginCmd 'spawn Troll 2.6'
Start-Sleep 3
$t0 = Creature
$s0 = Status
Drive 'hold 0x32 250'                       # key 2: the diamond sword
Drive 'mdown 480 270' 'mup 480 270'
Start-Sleep 2
$t1 = Creature
$s1 = Status
Check ([int]$s1.swings -gt [int]$s0.swings) 'the plugin received the swing'
Check ($t1.hp -lt $t0.hp) "the Troll lost health ($($t0.hp) -> $($t1.hp))"
PluginCmd 'cleanup'

Write-Host '3. Minecraft arrow at a Troll'
PluginCmd 'spawn Troll 10'
Start-Sleep 3
$a0 = Creature
$s0 = Status
Drive 'hold 0x33 250'                       # key 3: the bow
um win drive --proc valheim 'focus' 'mdown 480 270 right' 2>&1 | Out-Null
Start-Sleep 1.4                              # draw the bow
um win drive --proc valheim 'mup right' 2>&1 | Out-Null
Start-Sleep 4
$a1 = Creature
$s1 = Status
Check ([int]$s1.projHits -gt [int]$s0.projHits) "the arrow was traced into Valheim's world (projHits $($s0.projHits) -> $($s1.projHits))"
Check ($a1.hp -lt $a0.hp) "the Troll lost health ($($a0.hp) -> $($a1.hp))"
PluginCmd 'cleanup'

Write-Host '4. Minecraft pickaxe swing at a rock'
PluginCmd 'spawn Rock_4 2.4'
Start-Sleep 3
$s0 = Status
Drive 'hold 0x31 250'                       # key 1: the diamond pickaxe
Drive 'mdown 480 270' 'mup 480 270'
Start-Sleep 2
$s1 = Status
Check ([int]$s1.toolHits -gt [int]$s0.toolHits) "the rock took a pickaxe hit (toolHits $($s0.toolHits) -> $($s1.toolHits))"
PluginCmd 'cleanup'

PluginCmd 'inputmode off'
PluginCmd 'playerlog off'
""
if ($fails.Count -eq 0) { 'RESULT: PASS' } else { "RESULT: FAIL ($($fails.Count)): " + ($fails -join '; ') }
