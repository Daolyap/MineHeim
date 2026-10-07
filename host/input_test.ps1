<#
.SYNOPSIS
  Step 7 oracle: with Minecraft mode on, a left click breaks the Minecraft block under the crosshair and Valheim does NOT
  swing; a right click places a block; number keys and the wheel move Minecraft's hotbar and not Valheim's. With the mode off the
  same inputs act on Valheim (it swings, its hotbar item is used). Counters come from the plugin's `status` test command
  (Harmony counters on Humanoid.StartAttack and Player.UseHotbarItem, Minecraft's "selected" slot event, the block collider count).
  Drives the mouse and keyboard through `um win drive`: only when nobody is at the machine. The player should stand in open ground.
#>
param([string]$ValheimDir = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim')
$env:PATH += ";$env:USERPROFILE\.local\bin"; $env:PYTHONUTF8 = 1
$repo = Split-Path $PSScriptRoot -Parent
$py = Join-Path $repo '.venv\Scripts\python.exe'
$cmdFile = "$ValheimDir\BepInEx\mineheim-cmd.txt"
$csv = "$ValheimDir\BepInEx\mineheim-player.csv"
$log = "$ValheimDir\BepInEx\LogOutput.log"
$fails = @()

function PluginCmd($c) { Set-Content $cmdFile $c; Start-Sleep -Milliseconds 1800 }
# `status`, waiting for its NEW line; returns a hashtable of its fields
function Status() {
    $before = (Select-String -Path $log -Pattern 'status: colliders=' | Measure-Object).Count
    Set-Content $cmdFile 'status'
    for ($i = 0; $i -lt 24; $i++) {
        Start-Sleep -Milliseconds 500
        $all = Select-String -Path $log -Pattern 'status: colliders='
        if (($all | Measure-Object).Count -gt $before) {
            $h = @{}
            foreach ($m in [regex]::Matches(($all | Select-Object -Last 1).Line, '(\w+)=([\w.-]+)')) { $h[$m.Groups[1].Value] = $m.Groups[2].Value }
            return $h
        }
    }
    throw 'no status line'
}
function Drive() { um win drive --proc valheim 'focus' @args 2>&1 | Out-Null; Start-Sleep 2 }
function Check($ok, $what) { if ($ok) { Write-Host "  PASS  $what" } else { Write-Host "  FAIL  $what"; $script:fails += $what } }
function PlaceAtCrosshair($block) {
    $cell = & $py (Join-Path $repo 'host\crosshair_cell.py') $csv 3
    & $py (Join-Path $repo 'host\cmd.py') "setblock $cell minecraft:$block" | Out-Null
    Write-Host "  placed $block at $cell"
    Start-Sleep 3
}

PluginCmd 'playerlog on'; Start-Sleep 2
PluginCmd 'inputmode on'
$s0 = Status
Write-Host "start: mode=$($s0.mcMode) colliders=$($s0.colliders) valheimAttacks=$($s0.valheimAttacks) hotbarUses=$($s0.hotbarUses) mcSlot=$($s0.mcSlot) camDistance=$($s0.camDistance)"

Write-Host 'A. Minecraft mode, left click on a block under the crosshair'
PlaceAtCrosshair 'stone'
$a1 = Status
Check ([int]$a1.colliders -eq [int]$s0.colliders + 1) "the stone became a collider ($($s0.colliders) -> $($a1.colliders))"
Drive 'mdown 480 270' 'mup 480 270'
Start-Sleep 2
$a2 = Status
Check ([int]$a2.colliders -eq [int]$s0.colliders) "the left click broke it ($($a1.colliders) -> $($a2.colliders))"
Check ([int]$a2.valheimAttacks -eq [int]$s0.valheimAttacks) "Valheim did not swing (attacks $($s0.valheimAttacks) -> $($a2.valheimAttacks))"

Write-Host 'B. Minecraft mode off, left click: Valheim swings'
PluginCmd 'inputmode off'
Drive 'mdown 480 270' 'mup 480 270'
Start-Sleep 2
$b = Status
Check ([int]$b.valheimAttacks -gt [int]$a2.valheimAttacks) "Valheim swung (attacks $($a2.valheimAttacks) -> $($b.valheimAttacks))"

Write-Host 'C. Minecraft mode, number key 7 and right click place a block'
PluginCmd 'inputmode on'
$c0 = Status
Drive 'hold 0x37 250'
Start-Sleep 2
$c1 = Status
Check ($c1.mcSlot -eq '6') "key 7 selected Minecraft's slot 6 (mcSlot=$($c1.mcSlot))"
Check ([int]$c1.hotbarUses -eq [int]$c0.hotbarUses) "Valheim's hotbar was not used (uses $($c0.hotbarUses) -> $($c1.hotbarUses))"
PlaceAtCrosshair 'stone'
$c2 = Status
Drive 'mdown 480 270 right' 'mup right'
Start-Sleep 3
$c3 = Status
Check ([int]$c3.colliders -eq [int]$c2.colliders + 1) "the right click placed a block ($($c2.colliders) -> $($c3.colliders))"
Drive 'mdown 480 270' 'mup 480 270'
Drive 'mdown 480 270' 'mup 480 270'
Start-Sleep 2
$c4 = Status
Check ([int]$c4.colliders -eq [int]$s0.colliders) "two left clicks broke both again (colliders back to $($c4.colliders), started at $($s0.colliders))"

Write-Host 'D. Minecraft mode off, number key 1: Valheim uses its hotbar'
PluginCmd 'inputmode off'
$d0 = Status
Drive 'hold 0x31 250'
Start-Sleep 2
$d1 = Status
Check ([int]$d1.hotbarUses -gt [int]$d0.hotbarUses) "Valheim's hotbar was used (uses $($d0.hotbarUses) -> $($d1.hotbarUses))"
Check ($d1.mcSlot -eq $c1.mcSlot) "Minecraft's slot did not change (mcSlot $($c1.mcSlot) -> $($d1.mcSlot))"

Write-Host 'E. Minecraft mode, mouse wheel: Minecraft hotbar scrolls, Valheim camera does not zoom'
PluginCmd 'inputmode on'
$e0 = Status
Drive 'wheel -120'
Start-Sleep 2
$e1 = Status
Check ($e1.mcSlot -ne $e0.mcSlot) "the wheel moved Minecraft's slot ($($e0.mcSlot) -> $($e1.mcSlot))"
Check ($e1.camDistance -eq $e0.camDistance) "Valheim's camera did not zoom ($($e0.camDistance) -> $($e1.camDistance))"

PluginCmd 'inputmode off'
PluginCmd 'playerlog off'
""
if ($fails.Count -eq 0) { 'RESULT: PASS' } else { "RESULT: FAIL ($($fails.Count)): " + ($fails -join '; ') }
