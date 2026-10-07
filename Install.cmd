@echo off
rem Installs MineHeim: the Valheim half (BepInEx + plugin, ReShade + compositor) and the Minecraft half (Prism Launcher + a MineHeim instance). Close Valheim first.
rem Optional: Install.cmd -ValheimDir "D:\SteamLibrary\steamapps\common\Valheim"
rem a PowerShell 7 window passes its own module path to Windows PowerShell, which then misses its cmdlets
set PSModulePath=
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-MineHeim.ps1" %*
pause
