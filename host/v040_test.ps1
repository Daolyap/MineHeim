<#
.SYNOPSIS
  0.3.0 + 0.4.0 oracle, in the real games (no mouse or keyboard needed: plugin test hooks and messages to the mod):
    1. Buildings: a Valheim wall becomes whole barrier collision in Minecraft (every voxel of it is a barrier there).
    2. Time: Minecraft's clock follows Valheim's day (noon -> 6000, midnight -> 18000, and Minecraft's sky darkens at night).
    3. Fires: a burning Valheim fire becomes a Minecraft light block.
    4. Lamps: a Minecraft glowstone block becomes a Unity point light in Valheim, and goes when the block goes.
    5. Items Valheim -> Minecraft: the Viking drops 5 Wood; it leaves Valheim and arrives as 5 oak logs in Minecraft.
    6. Items Minecraft -> Valheim: in Creative a thrown cobblestone stays in Minecraft; in Survival it becomes Valheim Stone
       (taken away in Minecraft first) and the Viking picks it up.
    7. Mobs: a Minecraft zombie gets a body in Valheim; the zombie hunts a Valheim Greydwarf and its hits hurt it.
    8. Mobs: the Viking's hit on the zombie's body hurts the zombie in Minecraft (its health, shown on the body, drops).
    9. Dynamic damage: Steve's sword swing (as Minecraft reports it) deals 7 x the exchange rate to a creature.
   10. Mobs: a Greydwarf with its own AI attacks a Minecraft zombie's body, at a Greydwarf's strength.
   11. Survival: Minecraft's hearts show the Viking's health.
  Run with Valheim in the test world and Minecraft attached. Stand in open, flat-ish ground outside the start temple.
#>
param([string]$ValheimDir = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim', [string]$Out = (Join-Path (Split-Path $PSScriptRoot -Parent) 'test-output\v040'))
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
function Status($which = 'status3') {
    $b = Count "${which}: "
    Set-Content $cmdFile $which
    $l = Wait-Log "${which}: " $b
    $h = @{}
    foreach ($m in [regex]::Matches($l, '(\w+)=([\w.-]+)')) { $h[$m.Groups[1].Value] = $m.Groups[2].Value }
    $h
}
function Status2() {
    $b = Count 'status2: '
    Set-Content $cmdFile 'status'
    $l = Wait-Log 'status2: ' $b
    $h = @{}
    foreach ($m in [regex]::Matches($l, '(\w+)=([\w.-]+)')) { $h[$m.Groups[1].Value] = $m.Groups[2].Value }
    $h
}
function Query() {
    $b = Count 'minecraft query: '
    Set-Content $cmdFile 'query'
    $l = Wait-Log 'minecraft query: ' $b
    ($l -replace '^.*minecraft query: ', '') | ConvertFrom-Json
}
function ItemCount($name) {
    $b = Count "count: $name x"
    Set-Content $cmdFile "count $name"
    $l = Wait-Log "count: $name x" $b
    [int]([regex]::Match($l, 'x(\d+)').Groups[1].Value)
}
function Mc($c) { & $py "$repo\host\cmd.py" $c }
function Send($json) { & $py "$repo\host\send.py" $json }
function Shot($name) { um win shot --exe valheim.exe (Join-Path $Out "$name.png") 2>&1 | Out-Null }
function Check($ok, $what) { if ($ok) { Write-Host "  PASS  $what" } else { Write-Host "  FAIL  $what"; $script:fails += $what } }

PluginCmd 'nodeath on'                   # tests only: whatever hits the Viking leaves him at 1 health or more
PluginCmd 'cleanup'
PluginCmd 'gamemode creative'
PluginCmd 'inputmode off'
PluginCmd 'time 0.45'
PluginCmd 'heal'
PluginCmd 'pitch 8'
Mc 'kill @e[type=minecraft:item]'

Write-Host '1. A Valheim wall is whole collision in Minecraft'
PluginCmd 'spawn woodwall 5'
Start-Sleep 3
$b = Count 'voxelcheck: \d+/'
PluginCmd 'voxelcheck'
$l = Wait-Log 'voxelcheck: \d+/' $b
Write-Host "  $l"
$m = [regex]::Match("$l", '(\d+)/(\d+) cells')
Check ($m.Success -and [int]$m.Groups[2].Value -ge 4 -and $m.Groups[1].Value -eq $m.Groups[2].Value) "every voxel of the wall is a barrier in Minecraft ($($m.Value))"
$s = Status
Write-Host "  pieces=$($s.pieces) pieceCells=$($s.pieceCells) barrierCells=$($s.barrierCells)"
Shot 'wall'
PluginCmd 'cleanup'
Start-Sleep 3
$s2 = Status
Check ([int]$s2.pieceCells -lt [int]$s.pieceCells) "a destroyed wall lets go of its cells (pieceCells $($s.pieceCells) -> $($s2.pieceCells))"

Write-Host '2. Minecraft time follows Valheim day'
PluginCmd 'time 0.5'
Start-Sleep 4
$q = Query
Check ([math]::Abs([int]$q.time - 6000) -le 150) "Valheim noon -> Minecraft $($q.time) (6000), sky darken $($q.darken)"
PluginCmd 'time 0.0'
Start-Sleep 4
$q2 = Query
Check ([math]::Abs([int]$q2.time - 18000) -le 150 -and [int]$q2.darken -gt [int]$q.darken) "Valheim midnight -> Minecraft $($q2.time) (18000), sky darken $($q.darken) -> $($q2.darken)"
Shot 'night'
PluginCmd 'time 0.45'

Write-Host '3. A Valheim fire lights Minecraft'
PluginCmd 'spawn fire_pit 3'
Start-Sleep 5
$s = Status
$q = Query
Check ([int]$s.fires -ge 1 -and [int]$q.fires -ge 1) "burning fire -> light block (sent $($s.fires), placed in Minecraft $($q.fires))"
PluginCmd 'cleanup'
Start-Sleep 4
$q = Query
Check ([int]$q.fires -eq 0) "the fire gone -> its light block gone (placed $($q.fires))"

Write-Host '4. A Minecraft lamp lights Valheim'
$s0 = Status
Mc 'execute at @p run setblock ~2 ~1 ~ minecraft:glowstone'
Start-Sleep 2
$s1 = Status
Check ([int]$s1.lamps -gt [int]$s0.lamps -and [int]$s1.lampsLit -ge 1) "glowstone -> Unity light (lamps $($s0.lamps) -> $($s1.lamps), lit $($s1.lampsLit))"
Shot 'lamp'
Mc 'execute at @p run setblock ~2 ~1 ~ minecraft:air'
Start-Sleep 2
$s2 = Status
Check ([int]$s2.lamps -lt [int]$s1.lamps) "glowstone gone -> light gone (lamps $($s1.lamps) -> $($s2.lamps))"

Write-Host '5. Valheim items cross into Minecraft'
$w0 = ItemCount 'Wood'
$b = Count 'item to Minecraft'
PluginCmd 'drop Wood 5'
$l = Wait-Log 'item to Minecraft' $b
Start-Sleep 2
$w1 = ItemCount 'Wood'
$q = Query
$logs = @($q.items | Where-Object { $_[0] -eq 'minecraft:oak_log' } | ForEach-Object { [int]$_[1] }) + @($q.inv | Where-Object { $_[0] -eq 'minecraft:oak_log' } | ForEach-Object { [int]$_[1] })
$logCount = ($logs | Measure-Object -Sum).Sum
Write-Host "  $l"
Check ($l -ne $null -and $w1 -eq $w0 -and $logCount -ge 5) "5 Wood left Valheim (inventory $w0 -> $w1 after a give+drop) and 5 oak logs are in Minecraft ($logCount)"

Write-Host '6. Minecraft items cross into Valheim'
Send '{"t":"slot","n":6}'                 # the cobblestone
$st0 = ItemCount 'Stone'
$b = Count 'item to Valheim'
Send '{"t":"key","k":"drop","down":true,"stack":false}'
Start-Sleep 3
Check ((Count 'item to Valheim') -eq $b) 'in Creative a thrown cobblestone stays in Minecraft'
PluginCmd 'gamemode survival'
Start-Sleep 2
Send '{"t":"key","k":"drop","down":true,"stack":false}'
$l = Wait-Log 'item to Valheim' $b 10
Start-Sleep 3
$st1 = ItemCount 'Stone'
Write-Host "  $l"
Check ($l -match '1 minecraft:cobblestone -> 1 Stone' -and $st1 -eq $st0 + 1) "in Survival it became Valheim Stone and the Viking picked it up (Stone $st0 -> $st1)"
PluginCmd 'gamemode creative'
Start-Sleep 2

Write-Host '7. A Minecraft zombie hunts a Valheim Greydwarf'
PluginCmd 'heal'
PluginCmd 'spawn Greydwarf 7'
Start-Sleep 2
$b = Count 'hit Greydwarf'
PluginCmd 'mcmob zombie 3'
$l = Wait-Log 'minecraft zombie hit .*Greydwarf' $b 30
$s = Status 'status4'
Write-Host "  $l"
Check ([int]$s.bodiesMade -ge 1) "the zombie got a body in Valheim (bodies $($s.bodies))"
Check ($l -ne $null -and [double]$s.creatureHp -lt 40) "the zombie hit the Greydwarf (creature health $($s.creatureHp))"
Shot 'zombie_vs_greydwarf'

Write-Host '8. The Viking hits the zombie''s body'
$s0 = Status 'status4'
PluginCmd 'hitbody 20'
Start-Sleep 2
$s1 = Status 'status4'
Check ([int]$s1.hitsToMinecraft -gt [int]$s0.hitsToMinecraft -and [double]$s1.bodyHp -lt [double]$s0.bodyHp) "the hit reached the zombie in Minecraft (body health $($s0.bodyHp) -> $($s1.bodyHp), rate $($s1.rate))"

Write-Host '9. Dynamic sword damage'
$b = Count 'sword swing: '
Send '{"t":"slot","n":1}'
Start-Sleep 1
Send '{"t":"key","k":"attack","down":true}' '{"t":"key","k":"attack","down":false}'
$l = Wait-Log 'sword swing: ' $b
Write-Host "  $l"
$m = [regex]::Match("$l", 'for ([\d.]+) \(([\d.]+) in Minecraft\)')
$rate = [double]$s1.rate
Check ($m.Success -and [math]::Abs([double]$m.Groups[1].Value - [double]$m.Groups[2].Value * $rate) -lt 0.2) "the swing dealt Minecraft damage x rate ($($m.Groups[2].Value) x $rate = $($m.Groups[1].Value))"

Mc 'kill @e[type=minecraft:zombie]'
PluginCmd 'cleanup'

Write-Host '10. A Greydwarf with its own AI attacks a Minecraft zombie'
Mc 'kill @e[type=minecraft:zombie]'
PluginCmd 'heal'
$b = Count "Greydwarf hit Minecraft's Zombie"
PluginCmd 'mcmob zombie 5'
Start-Sleep 2
PluginCmd 'spawn Greydwarf 9 ai'
$l = Wait-Log "Greydwarf hit Minecraft's Zombie" $b 40
Write-Host "  $l"
$m = [regex]::Match("$l", ': ([\d.]+) Valheim damage')
Check ($m.Success -and [double]$m.Groups[1].Value -gt 0 -and [double]$m.Groups[1].Value -le 30) "the Greydwarf's own attack reached the zombie, at a Greydwarf's strength (no backstab, no chop/pickaxe parts)"
Shot 'greydwarf_vs_zombie'
Mc 'kill @e[type=minecraft:zombie]'
PluginCmd 'cleanup'

Write-Host '11. Survival: Minecraft''s hearts show the Viking''s health'
PluginCmd 'gamemode survival'
PluginCmd 'heal'
PluginCmd 'hurt 6'
Start-Sleep 2
$h = Status2
$q = Query
$viking = [double]$h.health / ($rate * 20)
$steve = [double]$q.hp / [double]$q.maxHp
Check ($viking -lt 0.99 -and [math]::Abs($viking - $steve) -lt 0.03) ("Steve's hearts {0:P0} = the Viking's health {1:P0}" -f $steve, $viking)
PluginCmd 'gamemode creative'
PluginCmd 'heal'
PluginCmd 'time -1'
PluginCmd 'nodeath off'

""
if ($fails.Count -eq 0) { 'RESULT: PASS' } else { "RESULT: FAIL ($($fails.Count)): " + ($fails -join '; ') }
