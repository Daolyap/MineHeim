<#
.SYNOPSIS
  Records the showcase clip (about 55 s): Steve builds a cobblestone wall on Valheim's ground, TNT hits a Greydwarf behind it,
  a sword fight, an arrow. Everything is real input and real events; the plugin's test hooks only spawn the Greydwarfs, light the TNT,
  freeze the sky at daytime and set the look pitch (mouse counts are ~0.05 degrees: too coarse to aim by hand).
  Drives mouse and keyboard through `um win drive` and records the game window with `um win record`: only when nobody is at the machine.
  Needs Valheim in a world, Minecraft connected, open ground around the Viking.
  Output: <OutDir>\showcase.mkv + audio + json (mux with `um video mux`).
#>
param([string]$ValheimDir = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim', [string]$OutDir = (Join-Path (Split-Path $PSScriptRoot -Parent) 'test-output\takes'), [int]$Seconds = 110)
$env:PATH += ";$env:USERPROFILE\.local\bin"; $env:PYTHONUTF8 = 1
$repo = Split-Path $PSScriptRoot -Parent
$cmdFile = "$ValheimDir\BepInEx\mineheim-cmd.txt"
New-Item -ItemType Directory -Force $OutDir | Out-Null
$base = Join-Path $OutDir 'showcase'
function PluginCmd($c) { Set-Content $cmdFile $c; Start-Sleep -Milliseconds 1300 }
# one drive call = one process: commands inside it run back to back; `Wait` is a held, unbound key (F24)
function Wait($ms) { "hold 0x87 $ms" }
function Drive($cmds) { um win drive --proc valheim 'focus' @cmds 2>&1 | Out-Null }
function Click($right) { $b = if ($right) { ' right' } else { '' }; @("mdown 480 270$b", (Wait 150), "mup$(if ($right) { ' right' } else { ' 480 270' })") }

# --- set the stage (not recorded)
PluginCmd 'cleanup'
& "$repo\.venv\Scripts\python.exe" "$repo\host\cmd.py" 'execute at @p run fill ~-14 ~-6 ~-14 ~14 ~6 ~14 air replace cobblestone' 'execute at @p run fill ~-14 ~-6 ~-14 ~14 ~6 ~14 air replace gold_block'
Start-Sleep 2
PluginCmd 'time 0.45'
PluginCmd 'inputmode on'
PluginCmd 'pitch 40'
Drive @('hold 0x37 250')                                  # key 7: cobblestone

$job = Start-Job -ArgumentList $base, $Seconds -ScriptBlock { param($b, $s) $env:PATH += ";$env:USERPROFILE\.local\bin"; $env:PYTHONUTF8 = 1; um win record --exe valheim.exe --out $b --seconds $s 2>&1 }
$clock = [Diagnostics.Stopwatch]::StartNew()
function Mark($what) { Write-Host ('[{0,6:N1} s] {1}' -f $clock.Elapsed.TotalSeconds, $what) }   # seconds since the recorder was started (about: it needs ~1 s to open)
Start-Sleep 4

# --- 1. build: seven spots round the Viking, two blocks high, turning ~22 degrees between them
Mark 'build starts'
$build = @()
foreach ($i in 1..7) { $build += (Click $true); $build += (Wait 350); $build += (Click $true); $build += @((Wait 350), 'rel 450 0', (Wait 300)) }
Drive $build
Mark 'build done'
PluginCmd 'pitch 6'
Drive @('rel -1575 0', (Wait 800))                        # sweep back to the middle of the wall
Mark 'swept back to the wall'

# --- 2. TNT: a Greydwarf behind the wall
PluginCmd 'spawn Greydwarf 9'
Drive @((Wait 1200))
Mark 'TNT lit'
PluginCmd 'tnt 2'
Start-Sleep 4
Mark 'TNT done'

# --- 3. a sword fight at arm's length, in open ground behind the Viking
PluginCmd 'cleanup'
Drive @('rel 3600 0', (Wait 500))
Mark 'turned round'
PluginCmd 'spawn Greydwarf 2.6'
Drive @('hold 0x32 250', (Wait 600), (Click $false), (Wait 900), (Click $false), (Wait 900), (Click $false))
Start-Sleep 2
Mark 'sword done'

# --- 4. an arrow
PluginCmd 'cleanup'
PluginCmd 'spawn Greydwarf 11'
Drive @('hold 0x33 250', (Wait 500))
Mark 'bow drawn'
um win drive --proc valheim 'focus' 'mdown 480 270 right' 2>&1 | Out-Null
Start-Sleep 1.5
um win drive --proc valheim 'mup right' 2>&1 | Out-Null
Start-Sleep 3
Mark 'arrow done'
PluginCmd 'cleanup'
PluginCmd 'inputmode off'
PluginCmd 'time -1'

$job | Wait-Job -Timeout ($Seconds + 30) | Out-Null
Receive-Job $job | Select-Object -Last 4
Remove-Job $job -Force
& "$repo\.venv\Scripts\python.exe" "$repo\host\cmd.py" 'execute at @p run fill ~-14 ~-6 ~-14 ~14 ~6 ~14 air replace cobblestone'
Get-ChildItem "$OutDir\showcase*" | Select-Object Name, Length
