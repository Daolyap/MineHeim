<#
.SYNOPSIS
  Test helper: launch Valheim windowed at 960x540, skip the intro, click through the menus to the saved test
  character/world, and wait until the plugin reports the player in the world. Drives the mouse and keyboard through
  `um win drive`: only run it when nobody is using the machine (check `um win drive --proc valheim idle` first).

  Assumes the test character (VCTest) is the one selected on the character screen and the test world (VCTestWorld) is the
  first row of the world list. Click positions are for a 960x540 client area at 100 % display scale.
  Prints "valheim PID <n>" so the caller can stop that exact process.
#>
param(
    [string]$ValheimDir = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim',
    [int]$Width = 960, [int]$Height = 540,
    [int]$TimeoutMinutes = 8
)
$ErrorActionPreference = 'Stop'
$env:PATH += ";$env:USERPROFILE\.local\bin"; $env:PYTHONUTF8 = 1
$log = Join-Path $ValheimDir 'BepInEx\LogOutput.log'
function Drive { um win drive --proc valheim @args 2>&1 | Out-Null }

$start = Get-Date
$p = Start-Process -FilePath (Join-Path $ValheimDir 'valheim.exe') -WorkingDirectory $ValheimDir `
    -ArgumentList '-screen-fullscreen', '0', '-screen-width', $Width, '-screen-height', $Height -PassThru
"valheim PID $($p.Id)"

# the opening cinematic: Space/Esc skip it; keep tapping until the main menu has arrived
Start-Sleep 35
foreach ($i in 1..4) { Drive 'focus' 'key 0x20'; Start-Sleep 2 }
Drive 'focus' 'key 0x1B'
Start-Sleep 6
Drive 'focus' 'click 480 389'   # START GAME
Start-Sleep 5
Drive 'focus' 'click 480 518'   # character screen: Start
Start-Sleep 5
Drive 'focus' 'click 309 408'   # world screen: Start

$deadline = $start.AddMinutes($TimeoutMinutes)
while ((Get-Date) -lt $deadline -and -not $p.HasExited) {
    if ((Test-Path $log) -and (Select-String -Path $log -Pattern 'MineHeim\] perf:' -Quiet)) { break }
    # the new-character intro and the loading screen both accept Space
    Start-Sleep 5
}
if ($p.HasExited) { throw 'Valheim exited' }
"in world after $([math]::Round(((Get-Date) - $start).TotalSeconds)) s"
