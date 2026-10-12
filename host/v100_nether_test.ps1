<#
.SYNOPSIS
  1.0 Nether infection oracle, in the real games (Valheim in a world, Minecraft attached and following it). Uses the
  plugin's test hooks; only the screenshots press a key (ReShade's), so run it only when nobody is using the machine.
    1. A Nether portal built and lit in Minecraft (obsidian frame + fire, the "portal" test action) starts an infection; Valheim sees it.
    2. It grows: the radius rises and Minecraft lays crust on the columns inside it.
    3. Valheim paints the infected ground with the Ashlands' texture (heightmaps recoloured).
    4. Standing deep inside it brings the Ashlands' weather.
    5. Nether mobs spawn on it.
    6. A ward placed on infected land cleans its area (crust cells go down).
    7. Breaking the portal: it heals back to nothing; the ground's colour and the weather go back.
    8. Fire where Steve stands burns the Viking in Survival (Valheim fire damage).
   5b. (inside 5) Heat at the camera reaches the compositor; the Nether ambience plays; a deer flees, a Greydwarf burns.
   E1-E5. The End: an End portal infects (Mistlands ground and sky, the End's light in the compositor), endermen come,
       creatures warp, the dragon comes at full reach and its death puts the portal out; a broken frame does too.
  Screenshots (ReShade) land in test-output/nether/.
#>
param([string]$ValheimDir = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim', [switch]$NoShots)
$env:PATH += ";$env:USERPROFILE\.local\bin"; $env:PYTHONUTF8 = 1
$repo = Split-Path $PSScriptRoot -Parent
$cmdFile = "$ValheimDir\BepInEx\mineheim-cmd.txt"
$log = "$ValheimDir\BepInEx\LogOutput.log"
$reshadeLog = "$ValheimDir\ReShade.log"
$mcLog = "$env:LOCALAPPDATA\MineHeim\PrismLauncher\instances\MineHeim\.minecraft\logs\latest.log"   # Install.cmd's Prism instance
if (-not (Test-Path $mcLog)) { $mcLog = "$env:LOCALAPPDATA\Temp\vc_runclient.log" }                  # the dev client's (start_devclient.cmd)
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
function Nether() {
    $b = Count $log 'nether: zones='
    Set-Content $cmdFile 'nether'
    $l = Wait-Log $log 'nether: zones=' $b
    $h = @{ line = $l }
    foreach ($m in [regex]::Matches($l, '(\w+)=(\S+)')) { $h[$m.Groups[1].Value] = $m.Groups[2].Value }
    # list = id:radius:lit|healing:columns:distance,...
    $z = if ($h.list -and $h.list -ne '-') { $h.list.Split(',')[0].Split(':') } else { @() }
    $h.radius = if ($z.Count -ge 5) { [double]$z[1] } else { 0 }
    $h.lit = $z.Count -ge 5 -and $z[2] -eq 'lit'
    $h.columns = if ($z.Count -ge 5) { [int]$z[3] } else { 0 }
    $h.dist = if ($z.Count -ge 5) { [double]$z[4] } else { -1 }
    $h.kind = if ($z.Count -ge 7) { $z[5] } else { '' }
    $h.dragon = $z.Count -ge 7 -and $z[6] -eq 'dragon'
    $h
}
function Shot($name) {
    if ($NoShots) { return }
    um win drive --proc valheim 'focus' 'hold 0x2C 250' 2>&1 | Out-Null
    Start-Sleep 5
    $new = Get-ChildItem "$ValheimDir\reshade-screens" -Filter '*.png' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($new) {
        $dst = Join-Path $repo 'test-output\nether'
        New-Item -ItemType Directory -Force $dst | Out-Null
        Copy-Item $new.FullName (Join-Path $dst "$name.png") -Force
    }
}
function Check($ok, $what) { if ($ok) { Write-Host "  PASS  $what" } else { Write-Host "  FAIL  $what"; $script:fails += $what } }

PluginCmd 'nodeath on'
PluginCmd 'cleanup'
PluginCmd 'nethercmd clear'
PluginCmd 'gamemode creative'
PluginCmd 'creativegod on'
PluginCmd 'inputmode off'
PluginCmd 'time 0.5'
PluginCmd 'move 30 0'
Start-Sleep 4
PluginCmd 'heal'
PluginCmd 'pitch 15'
PluginCmd 'mccmd difficulty normal'
# fast for the test: 60 blocks a minute out to 24, healing at 120, a mob every 2 s
PluginCmd 'mcraw {"t":"nethercfg","on":true,"end":true,"dragon":true,"max":24,"grow":60,"heal":120,"mobs":6,"every":2}'

Write-Host '1. A lit portal starts an infection'
$b = Count $log 'nether infection \d+ \(nether\): open'
# a 4x5 obsidian frame 6 blocks along +x of the player (Minecraft x = Valheim x), standing on the ground, lit by a fire inside
PluginCmd 'nethercmd portal'
$open = Wait-Log $log 'nether infection \d+ \(nether\): open' $b 15
$n = Nether
Check ($open -and [int]$n.zones -ge 1 -and $n.lit) "infection open ($open) zones=$($n.zones) lit=$($n.lit)"

Write-Host '2. It grows and lays crust'
Start-Sleep 20
$n2 = Nether
Check ($n2.radius -ge 12 -and $n2.columns -ge 100) "radius $($n.radius) -> $($n2.radius), $($n2.columns) columns crusted"

Write-Host '3. Ashlands ground'
Check ([int]$n2.recolored -ge 1 -and [int]$n2.painted -ge 1 -and [double]$n2.weightHere -gt 0.5) "heightmaps painted $($n2.painted) (ever $($n2.recolored)), weight where the Viking stands $($n2.weightHere)"
PluginCmd 'turn 90'
Shot 'infected_1'
PluginCmd 'turn 180'
Shot 'infected_2'

Write-Host '4. Ashlands weather inside'
Start-Sleep 3
$n3 = Nether
Check ($n3.env -ne '-') "environment forced: $($n3.env) (Viking $($n3.dist) m from the portal, radius $($n3.radius))"

Write-Host '5. Nether mobs'
Start-Sleep 12
$n4 = Nether
Check ([int]$n4.mobs -ge 1) "mobs on the infection: $($n4.mobs)"
Shot 'mobs'

Write-Host '5b. Heat, sound, creatures'
$b = Count $log 'burned='
# a deer on burning ground with Nether mobs about lives seconds: look right after it arrives (up to three deer)
$fled0 = [int](Nether).fled
foreach ($k in 1..3) {
    PluginCmd 'spawn Deer 6 ai'
    $f = Nether
    if ([int]$f.fled -gt $fled0) { break }
}
PluginCmd 'spawn Greydwarf 5'
Start-Sleep 8
$c = Nether
$rs = Wait-Log $reshadeLog 'MineHeim: mappings.*heat=' (Count $reshadeLog 'MineHeim: mappings.*heat=') 12
$rsHeat = [double]([regex]::Match("$rs", 'heat=([\d.]+)').Groups[1].Value)
$amb = (Test-Path $mcLog) -and (Select-String -Path $mcLog -Pattern 'nether air: nether ambience started' -Quiet)
Check ([double]$c.heat -gt 0.3 -and $rsHeat -gt 0.3) "heat at the camera $($c.heat), compositor heat $rsHeat"
Check ($amb) "Nether ambience started in Minecraft ($amb)"
Check ([int]$c.burned -ge 1 -and [int]$f.fled -gt $fled0) "creatures burned $($c.burned) time(s), animals fled $([int]$f.fled - $fled0) time(s)"
Shot 'heat_haze'
PluginCmd 'cleanup'

Write-Host '6. A ward cleans its area'
PluginCmd 'spawn guard_stone 3'
PluginCmd 'wardon'
Start-Sleep 6
$n5 = Nether
$cellsBefore = [int]$n4.cells
$cellsAfter = [int]$n5.cells
Check ($cellsAfter -lt $cellsBefore) "crust cells $cellsBefore -> $cellsAfter after the ward"
PluginCmd 'cleanup'

Write-Host '7. Break the portal: it heals'
$b = Count $log 'nether infection \d+ \(nether\): portal gone'
PluginCmd 'nethercmd unportal'
$healing = Wait-Log $log 'nether infection \d+ \(nether\): portal gone' $b 15
Start-Sleep 25
$n6 = Nether
Check ($healing -and [int]$n6.zones -eq 0 -and [int]$n6.painted -eq 0 -and $n6.env -eq '-') "healing seen ($([bool]$healing)), zones $($n6.zones), painted heightmaps $($n6.painted), env $($n6.env)"
Shot 'healed'

Write-Host '8. Fire under Steve burns the Viking (Survival)'
$b = Count $log 'fire hit Steve'
PluginCmd 'heal'
PluginCmd 'gamemode survival'
PluginCmd 'mccmd execute at @p run setblock ~ ~ ~ minecraft:fire'
$burn = Wait-Log $log 'fire hit Steve' $b 10
PluginCmd 'mccmd execute at @p run fill ~-1 ~ ~-1 ~1 ~1 ~1 minecraft:air replace minecraft:fire'
PluginCmd 'gamemode creative'
PluginCmd 'heal'
Check ($burn) "burned: $burn"

Write-Host 'E1. An End portal starts an End infection'
$dragonsBefore = Count $log 'the Ender Dragon is here'   # (it comes by itself once the End has spread to 3/4 of its reach)
PluginCmd 'nethercmd clear'
PluginCmd 'move 0 40'
Start-Sleep 3
$b = Count $log 'nether infection \d+ \(end\): open'
PluginCmd 'nethercmd endportal'
$open = Wait-Log $log 'nether infection \d+ \(end\): open' $b 15
$e = Nether
Check ($open -and $e.kind -eq 'end' -and $e.lit) "End infection open ($([bool]$open)), kind $($e.kind), lit $($e.lit)"

Write-Host 'E2. It grows: Mistlands ground and sky, the End light'
Start-Sleep 22
$e2 = Nether
$rs = Wait-Log $reshadeLog 'MineHeim: mappings.*end=' (Count $reshadeLog 'MineHeim: mappings.*end=') 12
$rsEnd = [double]([regex]::Match("$rs", ' end=([\d.]+)').Groups[1].Value)
Check ($e2.radius -ge 12 -and $e2.columns -ge 100 -and [int]$e2.painted -ge 1) "radius $($e2.radius), $($e2.columns) columns, painted heightmaps $($e2.painted)"
Check ($e2.env -match 'Mistlands' -and [double]$e2.endHere -gt 0.3 -and $rsEnd -gt 0.3) "environment $($e2.env), End at the camera $($e2.endHere), compositor $rsEnd"
PluginCmd 'turn 90'
Shot 'end_1'

Write-Host 'E3. Endermen; creatures warp'
PluginCmd 'spawn Greydwarf 5'
PluginCmd 'spawn Greydwarf 6'
PluginCmd 'spawn Greydwarf 7'
Start-Sleep 30
$e3 = Nether
$amb = (Test-Path $mcLog) -and (Select-String -Path $mcLog -Pattern 'nether air: end ambience started' -Quiet)
Check ([int]$e3.mobs -ge 1) "End mobs: $($e3.mobs)"
Check ([int]$e3.warped -ge 1) "creatures warped: $($e3.warped)"
Check ($amb) "End ambience started in Minecraft ($amb)"
PluginCmd 'cleanup'

Write-Host 'E4. The dragon comes; killing it puts the portal out'
PluginCmd 'nethercmd dragon'
$came = Wait-Log $log 'the Ender Dragon is here' $dragonsBefore 15
Shot 'dragon'
$b = Count $log 'nether infection \d+ \(end\): portal gone'
PluginCmd 'nethercmd killdragon'
$out = Wait-Log $log 'nether infection \d+ \(end\): portal gone' $b 15
Check ($came -and $out) "dragon came ($([bool]$came)), portal out after its death ($([bool]$out))"

Write-Host 'E5. A broken frame puts a new End portal out'
PluginCmd 'nethercmd clear'
PluginCmd 'nethercmd endportal'
Start-Sleep 4
$b = Count $log 'nether infection \d+ \(end\): portal gone'
PluginCmd 'nethercmd breakframe'
$out = Wait-Log $log 'nether infection \d+ \(end\): portal gone' $b 15
Check ($out) "portal out after a frame was broken ($([bool]$out))"
PluginCmd 'nethercmd unportal'
PluginCmd 'move 0 -40'

PluginCmd 'nethercmd clear'
PluginCmd ('mcraw {"t":"nethercfg","on":true,"end":true,"dragon":true,"max":48,"grow":8,"heal":16,"mobs":10,"every":8}')
if ($fails.Count) { "RESULT: FAIL ($($fails.Count)): " + ($fails -join '; ') } else { 'RESULT: PASS' }
