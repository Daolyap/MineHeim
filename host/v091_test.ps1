<#
.SYNOPSIS
  0.9.1 oracle, in the real games (Valheim in a world, Minecraft attached and following it). Drives the keyboard
  through `um win drive`: only when nobody is using the machine.
    1. F7 off = the Viking (his body shows), on = Steve (the Viking's body hidden).
    2. Creative as Steve: no damage, no stamina used; with the test override, or as the Viking, a hit lands.
    3. A swing deals the held item's Minecraft damage at the recharge: rested 42 (diamond sword), rapid clicks much less.
    4. An explosion inside an active ward digs no crater (outside it does).
    5. Valheim's pause menu: the compositor steps aside (no Minecraft over the menu), and comes back after it.
    6. Minecraft's inventory over Valheim shows a cursor.
    7. Backspace works in Minecraft's chat (typed "xyz" deleted, the command still runs).
    8. E as the Viking on a Minecraft chest opens it.
    9. Walking: Steve stays closer to the Viking with motion prediction than without.
   10. (-KillValheim) Valheim killed by its PID: Minecraft saves and quits by itself.
  Positions assume a 960x540 Valheim window. Moves the Viking 30 m along +x first (open ground in the test world).
#>
param([string]$ValheimDir = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim', [int]$ValheimPid = 0, [switch]$KillValheim)
$env:PATH += ";$env:USERPROFILE\.local\bin"; $env:PYTHONUTF8 = 1
$repo = Split-Path $PSScriptRoot -Parent
$py = Join-Path $repo '.venv\Scripts\python.exe'
$cmdFile = "$ValheimDir\BepInEx\mineheim-cmd.txt"
$log = "$ValheimDir\BepInEx\LogOutput.log"
$reshadeLog = "$ValheimDir\ReShade.log"
$fails = @()

function Count($file, $pattern) { (Select-String -Path $file -Pattern $pattern | Measure-Object).Count }
function Wait-Log($file, $pattern, $before, $seconds = 10) {
    for ($i = 0; $i -lt ($seconds * 2); $i++) {
        $m = Select-String -Path $file -Pattern $pattern
        if (($m | Measure-Object).Count -gt $before) { return ($m | Select-Object -Last 1).Line }
        Start-Sleep -Milliseconds 500
    }
    $null
}
function PluginCmd($c) { Set-Content $cmdFile $c; Start-Sleep -Milliseconds 1500 }
function Drive() { um win drive --proc valheim 'focus' @args 2>&1 | Out-Null; Start-Sleep -Milliseconds 800 }
function Status5() {
    $b = Count $log 'status5: '
    Set-Content $cmdFile 'status5'
    $l = Wait-Log $log 'status5: ' $b
    $h = @{}
    foreach ($m in [regex]::Matches($l, '(\w+)=(\S+)')) { $h[$m.Groups[1].Value] = $m.Groups[2].Value }
    $h
}
function NextPerf() { $l = Wait-Log $log 'perf: ' (Count $log 'perf: ') 12; [double]([regex]::Match($l, 'steve offset ([\d.]+) cm').Groups[1].Value) }
function NextCompositor() { Wait-Log $reshadeLog 'MineHeim: mappings' (Count $reshadeLog 'MineHeim: mappings') 12 }
function Query() {
    $b = Count $log 'minecraft query: '
    Set-Content $cmdFile 'query'
    ((Wait-Log $log 'minecraft query: ' $b) -replace '^.*minecraft query: ', '') | ConvertFrom-Json
}
function Torches() { (@((Query).inv) | Where-Object { $_[0] -eq 'minecraft:torch' } | ForEach-Object { [int]$_[1] } | Measure-Object -Sum).Sum }
function Swings($skip) { @(Select-String -Path $log -Pattern 'swing: \d+ creature\(s\) hit for ([\d.]+)' | Select-Object -Skip $skip | ForEach-Object { [double]$_.Matches[0].Groups[1].Value }) }
function PerfOffsets($skip) { @(Select-String -Path $log -Pattern 'steve offset ([\d.]+) cm' | Select-Object -Skip $skip | ForEach-Object { [double]$_.Matches[0].Groups[1].Value }) }
function Check($ok, $what) { if ($ok) { Write-Host "  PASS  $what" } else { Write-Host "  FAIL  $what"; $script:fails += $what } }

PluginCmd 'nodeath on'
PluginCmd 'cleanup'
PluginCmd 'gamemode creative'
PluginCmd 'creativegod on'
PluginCmd 'predict on'
PluginCmd 'time 0.5'
PluginCmd 'move 30 0'
Start-Sleep 3
PluginCmd 'heal'
PluginCmd 'pitch 5'

Write-Host '1. F7: the Viking or Steve'
PluginCmd 'inputmode off'
Start-Sleep 1
$v = Status5
PluginCmd 'inputmode on'
Start-Sleep 1
$s = Status5
Check ($v.mcMode -eq 'off' -and [int]$v.vikingHidden -eq 0 -and $s.mcMode -eq 'on' -and [int]$s.vikingHidden -gt 0) "Viking shown in Viking mode (hidden $($v.vikingHidden)), hidden as Steve (hidden $($s.vikingHidden))"

Write-Host '2. Creative protects Steve'
$a = Status5
PluginCmd 'hurt 4'
PluginCmd 'stamina 30'
$b = Status5
Check ([double]$b.health -ge [double]$a.health -and [double]$b.stamina -ge [double]$a.stamina - 0.1 -and [int]$b.creativeBlocked -gt [int]$a.creativeBlocked) "no damage, no stamina used (health $($a.health) -> $($b.health), stamina $($a.stamina) -> $($b.stamina))"
PluginCmd 'creativegod off'
PluginCmd 'hurt 4'
$c = Status5
PluginCmd 'creativegod on'
PluginCmd 'heal'
PluginCmd 'inputmode off'
PluginCmd 'hurt 4'
$d = Status5
PluginCmd 'heal'
Check ([double]$c.health -lt [double]$b.health - 1 -and [double]$d.health -lt 24) "a hit lands with the override (health $($c.health)) and as the Viking (health $($d.health))"

Write-Host '3. Swings at the recharge'
PluginCmd 'inputmode on'
Drive 'key 0x32'                          # the diamond sword
PluginCmd 'spawn Troll 2.6'
Start-Sleep 3
$n0 = Count $log 'swing: \d+ creature'
PluginCmd 'mcattack 3 200'
Start-Sleep 3
$rapid = Swings $n0
$n1 = Count $log 'swing: \d+ creature'
PluginCmd 'mcattack 1 0'
Start-Sleep 2
$rested = Swings $n1
PluginCmd 'cleanup'
Write-Host "  rapid clicks: $($rapid -join ', ')   after a rest: $($rested -join ', ')"
# (two clicks can land in one Minecraft tick on a slow PC: count what arrived, not what was sent)
Check ($rapid.Count -ge 2 -and $rapid[0] -ge 40 -and ($rapid | Select-Object -Skip 1 | Where-Object { $_ -ge 30 }).Count -eq 0 -and $rested.Count -ge 1 -and $rested[0] -ge 40) 'rested swings deal 42, rapid clicks far less'

Write-Host '4. A ward stops the crater'
PluginCmd 'spawn guard_stone 2'
PluginCmd 'wardon'
$b = Count $log 'explosion \('
PluginCmd 'boom 6'
$in = Wait-Log $log 'explosion \(' $b 15
PluginCmd 'cleanup'
$b = Count $log 'explosion \('
PluginCmd 'boom 6'
$out = Wait-Log $log 'explosion \(' $b 15
$digIn = [int][regex]::Match($in, '(\d+) terrain dig').Groups[1].Value
$digOut = [int][regex]::Match($out, '(\d+) terrain dig').Groups[1].Value
Check ($in -match 'inside a ward' -and $digIn -eq 0 -and $digOut -gt 0) "inside: $digIn dig(s), outside: $digOut"

Write-Host '5. Valheim''s menu covers Minecraft'
Drive 'key 0x1B'
$l = NextCompositor
$l = NextCompositor
$menu = $l -match 'active=0' -and $l -match 'flags=1[3-5]'
Drive 'key 0x1B'
$l = NextCompositor
$l = NextCompositor
Check ($menu -and $l -match 'active=1') "hidden behind the pause menu ($menu), back after it ($([regex]::Match($l, 'active=\d').Value))"

Write-Host '6. A cursor over Minecraft''s inventory'
Drive 'key 0x09'
Start-Sleep 2
$s = Status5
Drive 'key 0x09'
Check ($s.screen -match 'Inventory' -and $s.cursorVisible -eq 'yes') "screen $($s.screen), cursor visible $($s.cursorVisible)"

Write-Host '7. Backspace in Minecraft''s chat'
$t0 = Torches
Drive 'key 0x54'
Start-Sleep 1
um win drive --proc valheim 'focus' 'type /give @s minecraft:torch 16xyz' 2>&1 | Out-Null
Start-Sleep 1
Drive 'key 0x08' 'key 0x08' 'key 0x08'
Drive 'key 0x0D'
Start-Sleep 2
$t1 = Torches
Check ($t1 -eq $t0 + 16) "the command ran after three Backspaces (torches $t0 -> $t1)"

Write-Host '8. E opens a Minecraft chest for the Viking'
PluginCmd 'inputmode off'
PluginCmd 'mccmd execute at @p run setblock ^ ^ ^2 minecraft:chest'
PluginCmd 'pitch 46'
Start-Sleep 2
$b = Count $log 'minecraft screen open'
Drive 'hold 0x45 300'                     # held: a tap shorter than a frame never reaches Valheim's buttons on a slow PC
$l = Wait-Log $log 'minecraft screen open' $b
if ($l) { Drive 'key 0x1B' }
PluginCmd 'mccmd execute at @p run setblock ^ ^ ^2 minecraft:air'
PluginCmd 'pitch 5'
Check ($l -match 'ContainerScreen') "opened: $l"

Write-Host '9. Steve keeps up with the walking Viking'
PluginCmd 'inputmode on'
PluginCmd 'move 0 30'                     # fresh ground, away from what earlier steps built
Start-Sleep 3
function Walk($mode) {
    PluginCmd "predict $mode"
    $a = Status5
    $p0 = Count $log 'steve offset [\d.]+ cm'
    Drive 'hold 0x57 12000'
    $during = PerfOffsets $p0           # the perf lines written while walking (one every 5 s)
    $cm = if ($during.Count) { [math]::Round(($during | Measure-Object -Maximum).Maximum, 1) } else { -1 }
    $b = Status5
    $m = [math]::Sqrt([math]::Pow([double]$b.x - [double]$a.x, 2) + [math]::Pow([double]$b.z - [double]$a.z, 2))
    PluginCmd 'turn 180'
    @($cm, $m)
}
$off = Walk 'off'
$on = Walk 'on'
PluginCmd 'move 0 -30'
Check ($off[1] -gt 5 -and $on[1] -gt 5 -and $on[0] -lt $off[0] * 0.7) ("Steve's distance from the Viking: {0} cm without prediction, {1} cm with (walked {2:0.0} m and {3:0.0} m)" -f $off[0], $on[0], $off[1], $on[1])

PluginCmd 'move -30 0'
PluginCmd 'inputmode off'

if ($KillValheim -and $ValheimPid -gt 0) {
    Write-Host '10. Valheim goes: Minecraft follows'
    $mc = Get-CimInstance Win32_Process -Filter "Name like 'java%'" | Where-Object { $_.CommandLine -match 'fabric.dli.config|MineHeim' -and $_.CommandLine -notmatch 'GradleDaemon|gradlew' }
    Stop-Process -Id $ValheimPid
    $t0 = Get-Date
    while ($mc -and (Get-Process -Id $mc.ProcessId -ErrorAction SilentlyContinue) -and ((Get-Date) - $t0).TotalSeconds -lt 60) { Start-Sleep -Milliseconds 500 }
    $gone = -not ($mc -and (Get-Process -Id $mc.ProcessId -ErrorAction SilentlyContinue))
    Check $gone ("Minecraft (pid {0}) quit {1:0.0} s after Valheim was stopped" -f $mc.ProcessId, ((Get-Date) - $t0).TotalSeconds)
}

""
if ($fails.Count -eq 0) { 'RESULT: PASS' } else { "RESULT: FAIL ($($fails.Count)): " + ($fails -join '; ') }
