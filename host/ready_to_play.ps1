<#
.SYNOPSIS
  Test helper: make sure Valheim is being played before a test suite (no Valheim menu open, no Minecraft screen open,
  the clock running), optionally moving the Viking away from the start temple (a no-build area). Prints the state.
#>
param([string]$ValheimDir = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim', [double]$MoveX = 0)
$env:PATH += ";$env:USERPROFILE\.local\bin"
$cmdFile = "$ValheimDir\BepInEx\mineheim-cmd.txt"
$log = "$ValheimDir\BepInEx\LogOutput.log"
function Status5() {
    $b = (Select-String -Path $log -Pattern 'status5: ').Count
    Set-Content $cmdFile 'status5'
    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Milliseconds 500
        $m = Select-String -Path $log -Pattern 'status5: '
        if ($m.Count -gt $b) { $h = @{}; foreach ($x in [regex]::Matches($m[-1].Line, '(\w+)=(\S+)')) { $h[$x.Groups[1].Value] = $x.Groups[2].Value }; return $h }
    }
    @{}
}
for ($try = 0; $try -lt 3; $try++) {
    $s = Status5
    if ($s.screen -ne 'none') { um win drive --proc valheim 'focus' 'key 0x1B' 2>&1 | Out-Null; Start-Sleep 2; continue }
    if ($s.cursorLock -ne 'Locked') { um win drive --proc valheim 'focus' 'click 480 156' 2>&1 | Out-Null; Start-Sleep 2; continue }   # Valheim's menu: Continue
    break
}
if ($MoveX -ne 0) { Set-Content $cmdFile "move $MoveX 0"; Start-Sleep 4 }
$s = Status5
"ready: screen=$($s.screen) cursorLock=$($s.cursorLock) mcMode=$($s.mcMode) x=$($s.x) z=$($s.z)"
