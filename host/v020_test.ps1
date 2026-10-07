<#
.SYNOPSIS
  0.2.0 oracle, in the real games:
    1. Minecraft TNT next to a Valheim wall, rock and tree: they take damage, and Valheim's terrain under the blast drops (crater).
    2. A Minecraft pickaxe swing at bare ground digs Valheim's terrain.
    3. Creative flight: double-tap Jump flies, holding Jump rises; landing after it costs no health.
    4. The shield (as if Minecraft reported it raised): a forwarded mob hit from the front is blocked in full; without it, it lands.
    5. The real Minecraft shield: sword in hand, right button held -> Minecraft reports it up; a Greydwarf with its AI on attacks and is blocked.
    6. Survival: a Minecraft zombie attacks Steve -> the Viking loses health (Valheim owns health).
  Drives mouse and keyboard through `um win drive`: only when nobody is at the machine. Stand in open, flat-ish ground.
#>
param([string]$ValheimDir = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim', [string]$Out = (Join-Path (Split-Path $PSScriptRoot -Parent) 'test-output\v020'))
$env:PATH += ";$env:USERPROFILE\.local\bin"; $env:PYTHONUTF8 = 1
$repo = Split-Path $PSScriptRoot -Parent
$py = Join-Path $repo '.venv\Scripts\python.exe'
$cmdFile = "$ValheimDir\BepInEx\mineheim-cmd.txt"
$log = "$ValheimDir\BepInEx\LogOutput.log"
New-Item -ItemType Directory -Force $Out | Out-Null
$fails = @()

function Count($pattern) { (Select-String -Path $log -Pattern $pattern | Measure-Object).Count }
function Wait-Log($pattern, $before, $seconds = 12) {
    for ($i = 0; $i -lt ($seconds * 2); $i++) {
        $m = Select-String -Path $log -Pattern $pattern
        if (($m | Measure-Object).Count -gt $before) { return ($m | Select-Object -Last 1).Line }
        Start-Sleep -Milliseconds 500
    }
    $null
}
function PluginCmd($c) { Set-Content $cmdFile $c; Start-Sleep -Milliseconds 1500 }
function Status() {
    $b = Count 'status2: '
    Set-Content $cmdFile 'status'
    $l = Wait-Log 'status2: ' $b
    $h = @{}
    foreach ($m in [regex]::Matches($l, '(\w+)=([\w.-]+)')) { $h[$m.Groups[1].Value] = $m.Groups[2].Value }
    $h
}
function Terrain($ahead) {
    $b = Count 'terrain: y='
    Set-Content $cmdFile "terrain $ahead"
    $l = Wait-Log 'terrain: y=' $b
    [double]([regex]::Match($l, 'y=([-\d.]+)').Groups[1].Value)
}
function Drive() { um win drive --proc valheim 'focus' @args 2>&1 | Out-Null; Start-Sleep 1 }
function Shot($name) { um win shot --exe valheim.exe (Join-Path $Out "$name.png") 2>&1 | Out-Null }
function Check($ok, $what) { if ($ok) { Write-Host "  PASS  $what" } else { Write-Host "  FAIL  $what"; $script:fails += $what } }

PluginCmd 'nodeath on'                   # tests only: whatever hits the Viking (natural spawns too) leaves him at 1 health or more
PluginCmd 'cleanup'
PluginCmd 'gamemode creative'
PluginCmd 'inputmode off'
PluginCmd 'time 0.45'
PluginCmd 'heal'
PluginCmd 'pitch 8'

Write-Host '1. TNT next to a wall, a rock and a tree'
PluginCmd 'spawn woodwall 7.5'
PluginCmd 'spawn Rock_4 4.6'
PluginCmd 'spawn Beech1 8.5'
Start-Sleep 2
$y0 = Terrain 6
$s0 = Status
Shot 'tnt_before'
$b = Count 'explosion \('
PluginCmd 'boom 6'
$l = Wait-Log 'explosion \(' $b 15
Start-Sleep 3
Shot 'tnt_after'
$s1 = Status
$y1 = Terrain 6
Write-Host "  $l"
Write-Host ("  terrain at the blast: {0:N3} -> {1:N3}" -f $y0, $y1)
Check ([int]$s1.structureHits -ge [int]$s0.structureHits + 2) "the blast hit Valheim objects (structureHits $($s0.structureHits) -> $($s1.structureHits))"
Check ([int]$s1.craters -gt [int]$s0.craters) "a crater was dug (craters $($s0.craters) -> $($s1.craters))"
Check ($y1 -lt $y0 - 0.05) ("the ground under the blast went down ({0:N3} m)" -f ($y1 - $y0))
PluginCmd 'props'
PluginCmd 'cleanup'

Write-Host '2. A Minecraft pickaxe digs the ground'
PluginCmd 'heal'
PluginCmd 'inputmode on'
PluginCmd 'pitch 40'
Drive 'hold 0x31 250'                     # key 1: the pickaxe
$s0 = Status
$b = Count 'diamond_pickaxe swing'
Drive 'mdown 480 270' 'hold 0x87 150' 'mup 480 270'
$l = Wait-Log 'diamond_pickaxe swing' $b
$s1 = Status
Write-Host "  $l"
Check ([int]$s1.digs -gt [int]$s0.digs) "the pickaxe dug Valheim's terrain (digs $($s0.digs) -> $($s1.digs))"
Shot 'pickaxe_dig'

Write-Host '3. Creative flight'
PluginCmd 'inputmode off'
PluginCmd 'pitch 8'
$s0 = Status
Drive 'hold 0x20 120' 'hold 0x87 110' 'hold 0x20 120'      # double-tap Space
$s1 = Status
Check ($s1.flying -eq 'on') "double-tap Jump starts flying (flying=$($s1.flying))"
if ($s1.flying -ne 'on') { PluginCmd 'fly on' }
Drive 'hold 0x20 2000'                                      # hold Jump: rise
$s2 = Status
Shot 'flying'
Check ([double]$s2.y -gt [double]$s0.y + 3) ("holding Jump rises (y {0} -> {1})" -f $s0.y, $s2.y)
$hp = [double]$s2.health
PluginCmd 'fly off'
Start-Sleep 5
$s3 = Status
Check ([double]$s3.y -lt [double]$s2.y - 2) ("after the flight the Viking comes down (y {0} -> {1})" -f $s2.y, $s3.y)
Check ([double]$s3.health -ge $hp - 0.1) ("no fall damage after a flight (health {0} -> {1})" -f $hp, $s3.health)

Write-Host '4. The shield against a forwarded mob hit'
PluginCmd 'heal'
PluginCmd 'shield on'
$s0 = Status
PluginCmd 'hurt 5'
$s1 = Status
Check ([int]$s1.shieldBlocks -gt [int]$s0.shieldBlocks -and [double]$s1.health -ge [double]$s0.health - 0.1) ("shield up: blocked, health {0} -> {1}" -f $s0.health, $s1.health)
PluginCmd 'shield off'
PluginCmd 'hurt 5'
$s2 = Status
Check ([double]$s2.health -lt [double]$s1.health - 1) ("shield down: the hit lands, health {0} -> {1}" -f $s1.health, $s2.health)
PluginCmd 'heal'

Write-Host '5. The real Minecraft shield against a Greydwarf that fights'
PluginCmd 'inputmode on'
Drive 'hold 0x32 250'                                       # key 2: the sword (shield in the off hand)
um win drive --proc valheim 'focus' 'mdown 480 270 right' 2>&1 | Out-Null
Start-Sleep 1.5
$s0 = Status
Check ($s0.shieldUp -eq 'yes') "Minecraft reports the shield raised (shieldUp=$($s0.shieldUp))"
$b = Count 'shield blocked'
PluginCmd 'spawn Greydwarf 2.2 ai'
$l = Wait-Log 'shield blocked [\d.]+ from \$enemy' $b 20
Shot 'shield_block'
PluginCmd 'cleanup'                       # the Greydwarf goes before the shield comes down
um win drive --proc valheim 'mup right' 2>&1 | Out-Null
Write-Host "  $l"
Check ($l -ne $null) 'a real Greydwarf attack was blocked by the Minecraft shield'
PluginCmd 'heal'

Write-Host '6. Survival: a Minecraft zombie attacks Steve'
PluginCmd 'inputmode off'
PluginCmd 'gamemode survival'
Start-Sleep 2
$s0 = Status
& $py "$repo\host\cmd.py" 'execute at @p run summon minecraft:zombie ~2 ~ ~1'
$b = Count 'hit Steve:'
$l = Wait-Log 'zombie hit Steve:' $b 20
$s1 = Status
Shot 'zombie'
Write-Host "  $l"
Check ([int]$s1.mobHits -gt [int]$s0.mobHits -and [double]$s1.health -lt [double]$s0.health) ("the zombie's hit became Valheim damage (mobHits {0} -> {1}, health {2} -> {3})" -f $s0.mobHits, $s1.mobHits, $s0.health, $s1.health)
& $py "$repo\host\cmd.py" 'kill @e[type=minecraft:zombie]'
PluginCmd 'gamemode creative'
PluginCmd 'heal'
PluginCmd 'time -1'
PluginCmd 'pitch 6'
PluginCmd 'nodeath off'

""
if ($fails.Count -eq 0) { 'RESULT: PASS' } else { "RESULT: FAIL ($($fails.Count)): " + ($fails -join '; ') }
