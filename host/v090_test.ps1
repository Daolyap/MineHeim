<#
.SYNOPSIS
  0.9.0 oracle, in the real games (Valheim in a world, Minecraft attached). Drives the keyboard and mouse through
  `um win drive`: only when nobody is using the machine.
    1. Minecraft's window is parked off-screen and still renders (its frames keep coming).
    2. Minecraft mode, Tab: Minecraft's creative inventory opens over Valheim.
    3. Clicks in it reach Minecraft: an oak log stack picked from the Building Blocks tab lands in hotbar slot 1.
    4. Escape closes it (and Valheim's own menu does not open).
    5. T opens Minecraft's chat; typed text and Enter run a command (/give @s minecraft:torch 16).
    6. F8 switches Steve between Creative and Survival.
  Click positions are for a 960x540 Valheim window and Minecraft's creative screen at GUI scale auto.
#>
param([string]$ValheimDir = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim', [string]$McLog = "$env:TEMP\vc_runclient.log")
$env:PATH += ";$env:USERPROFILE\.local\bin"; $env:PYTHONUTF8 = 1
$repo = Split-Path $PSScriptRoot -Parent
$py = Join-Path $repo '.venv\Scripts\python.exe'
$cmdFile = "$ValheimDir\BepInEx\mineheim-cmd.txt"
$log = "$ValheimDir\BepInEx\LogOutput.log"
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
function Query() {
    $b = Count $log 'minecraft query: '
    Set-Content $cmdFile 'query'
    $l = Wait-Log $log 'minecraft query: ' $b
    ($l -replace '^.*minecraft query: ', '') | ConvertFrom-Json
}
function Check($ok, $what) { if ($ok) { Write-Host "  PASS  $what" } else { Write-Host "  FAIL  $what"; $script:fails += $what } }

Write-Host '1. Minecraft parked off-screen, still rendering'
$frames = & $py -c "import sys,time; sys.path.insert(0, r'$repo\host'); from mcframe import MCFrames; r=MCFrames(); a=r.latest().mc_frame; time.sleep(2); print(a, r.latest().mc_frame)" 2>&1
$f = "$frames" -split ' '
Check ((Count $McLog 'minecraft window parked') -ge 1 -and [int]$f[1] -gt [int]$f[0] + 10) "parked, and frames keep coming ($frames)"

Write-Host '2. Tab opens Minecraft''s inventory'
PluginCmd 'gamemode creative'
PluginCmd 'inputmode on'
$b = Count $log 'minecraft screen open'
Drive 'key 0x09'
$l = Wait-Log $log 'minecraft screen open' $b
Check ($l -match 'CreativeModeInventoryScreen') "creative inventory open ($l)"
um win shot --exe valheim.exe (Join-Path $env:TEMP 'v090_inventory.png') 2>&1 | Out-Null

Write-Host '3. Clicks reach it'
function Logs() { (@((Query).inv) | Where-Object { $_[0] -eq 'minecraft:oak_log' } | ForEach-Object { [int]$_[1] } | Measure-Object -Sum).Sum }
$l0 = Logs
Drive 'click 309 110'                     # the Building Blocks tab (the screen remembers the last tab)
Drive 'click 318 185'
Drive 'click 318 375'
$l1 = Logs
Check ($l1 -gt $l0) "an oak log taken from the creative tab is in the hotbar (oak logs $l0 -> $l1)"

Write-Host '4. Escape closes it'
$b = Count $log 'minecraft screen closed'
Drive 'key 0x1B'
$l = Wait-Log $log 'minecraft screen closed' $b
Check ($l -ne $null) 'closed'

Write-Host '5. T: chat and a command'
$t0 = (@((Query).inv) | Where-Object { $_[0] -eq 'minecraft:torch' } | ForEach-Object { [int]$_[1] } | Measure-Object -Sum).Sum
Drive 'key 0x54'
Start-Sleep 1
um win drive --proc valheim 'focus' 'type /give @s minecraft:torch 16' 2>&1 | Out-Null
Start-Sleep 1
Drive 'key 0x0D'
Start-Sleep 2
$t1 = (@((Query).inv) | Where-Object { $_[0] -eq 'minecraft:torch' } | ForEach-Object { [int]$_[1] } | Measure-Object -Sum).Sum
$closed = (Select-String -Path $log -Pattern 'minecraft screen closed' | Select-Object -Last 1).Line
Check ($t1 -eq $t0 + 16) "the typed /give ran: torches $t0 -> $t1 ($closed)"

Write-Host '6. F8 switches the game mode'
$b = Count $log 'game mode|gameMode'
Drive 'key 0x77'
Start-Sleep 2
$b = Count $log 'status2: '
Set-Content $cmdFile 'status'
$s = Wait-Log $log 'status2: ' $b
Check ($s -match 'gameMode=Survival') "after F8: $([regex]::Match($s, 'gameMode=\w+').Value)"
Drive 'key 0x77'
PluginCmd 'inputmode off'

""
if ($fails.Count -eq 0) { 'RESULT: PASS' } else { "RESULT: FAIL ($($fails.Count)): " + ($fails -join '; ') }
