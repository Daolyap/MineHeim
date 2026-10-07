@echo off
rem Removes exactly what Install.cmd added to the Valheim folder (and, if you say so, its Minecraft). Close Valheim and Minecraft first.
rem a PowerShell 7 window passes its own module path to Windows PowerShell, which then misses its cmdlets
set PSModulePath=
set MH_MC=
set /p MH_MC=Also remove MineHeim's Minecraft (Prism Launcher, the MineHeim instance and its world)? [y/N] 
if /i "%MH_MC%"=="y" (set MH_EXTRA=-IncludeMinecraft) else (set MH_EXTRA=)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-MineHeim.ps1" -Remove %MH_EXTRA% %*
pause
