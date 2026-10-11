# Play test round 3: new and repeated checks

Carry these over to the test sheet (https://claude.ai/artifact/3ZujziDppsXU6ATWHUJJhy). Still open from round 2: G9, G10, A9, A10, K6,
L6, and B, F, M, I, W, N, E, P (not reached).

## Re-test (failed in round 2)

| Check | Do | Pass when |
|---|---|---|
| L3 | Quit Valheim from its menu | Within ~10 s no `javaw` / "Java(TM) Platform SE binary" is left in Task Manager |
| L4 | End Valheim in Task Manager | Minecraft saves and is gone within ~35 s |
| L5, L6 | As in round 2 (F10 restarts Minecraft after closing it) | As in round 2 |
| L7 | If L3 or L4 still fails: open Minecraft's `logs/latest.log` | Send the line `... ending the process. Non-daemon threads still alive: ...` (it names what kept Minecraft alive) |

## New

| Check | Do | Pass when |
|---|---|---|
| V1 | Start fresh with the default config | No Minecraft music plays; Minecraft's other sounds (blocks, mobs) do |
| V2 | Set `Audio.MinecraftMusic = 0.5` (config manager, or edit the file with Valheim closed) | Minecraft music plays; back at 0 it stops |
| V3 | Set `Audio.MinecraftVolume = 0` | All Minecraft sound is silent; 1 brings it back |
| H1 | F7 on (Minecraft mode) | Valheim's hotbar is gone; health, stamina and food icons stay |
| H2 | F7 off | Valheim's hotbar is back, with the right items and selection |
| H3 | Set `Steve.HideValheimHotbar = false`, F7 on | Valheim's hotbar stays |
| X1 | Creative (F8), F7 off: spawn a zombie near you (or meet one) | It comes for the Viking and its hits hurt him (blockable with his shield; armour reduces it) |
| X2 | Creative, F7 off: hit a Minecraft mob with a Valheim weapon | It fights back |
| X3 | Creative, F7 on: let a zombie reach Steve | It ignores Steve (Creative is untouchable) |
| X4 | Survival, F7 off and on | Mobs hunt and hurt you in both |
| X5 | Creative, F7 on -> off -> on | The Creative kit and flight are still there (switching does not wipe or refill the inventory) |
