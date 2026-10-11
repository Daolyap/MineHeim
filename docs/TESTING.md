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
| D1 | Load a world, look in `LogOutput.log` | A line `steve weapons (world at ...)` lists a prefab for every item; send it if any says `(none)` or `[wanted ...]` |
| D2 | Survival, punch a Greyling or a deer as Steve | Small damage (the Viking's fist), not 6+; it does not grow after eating |
| D3 | Timed full swings vs. spam-clicking with a sword on a Greydwarf | Spam drains the stamina bar within seconds and then barely hurts; timed swings kill faster |
| D4 | An iron sword vs. a Valheim iron sword on the same creature (Viking mode) | About the same damage per full swing |
| D5 | Mace on a blob (Swamp) vs. a sword | The mace does far more (blobs are weak to blunt, resist slash) |
| D6 | Hit a group of 3 creatures with a sword, then with an axe | Each weapon hits as its Valheim twin (a sword's horizontal swing reaches several; check the log line `... creature(s) hit`) |
| D7 | Several swings | The Viking's Swords (or Axes, Clubs, Spears, Unarmed) skill rises |
| D8 | Stone pickaxe on copper ore, then iron pickaxe on silver | The stone (antler-tier) pickaxe mines copper; silver needs the iron pickaxe (or better) |
| D9 | Bow at full draw vs. a quick tap | The full draw hurts far more; Bows skill rises on hits |
| D10 | Fire Aspect / Smite sword on a Draugr or Skeleton (if you can get one: Creative inventory) | Fire sets it burning; Smite hurts undead much more |
| D11 | Set `Events.SteveWeaponStats = false` | The old damage comes back |
| E1 | Survival, F7 on, with an empty food slot: eat cooked beef (or bread) as Steve | The Viking gets Cooked deer meat (Bread) in a food slot: its icon, more max health and stamina; `LogOutput.log`: `steve ate ...: the Viking ate ...` |
| E2 | Eat the same food again while it is still in a slot | Nothing eaten in Valheim, the item is back in Steve's inventory |
| E3 | Fill all three slots | Minecraft's hunger bar is full and Steve cannot start eating (like Valheim) |
| E4 | Eat raw beef or rotten flesh | Nothing in Valheim (the item is gone, as in Minecraft) |
| E5 | Creative: eat something | A message: Creative food stays in Minecraft; no Valheim food |
| R1 | Look at Steve's face (F7 on; a mirror view: walk toward the camera with Alt + wheel zoomed out) | White eyes, no pupils |
| R2 | At night or in a dark crypt | The eyes glow (bright while the rest of him is dark) |
| R3 | F11 (Minecraft draws Steve) | His eyes are white there too (lit like the rest of him, no glow) |
| R4 | `Steve.HerobrineEyes = Off`, then back to `Always` | Steve's own eyes come back, then Herobrine's |
| R5 | `Steve.Skin = Account` with `HerobrineEyes = ClassicSteveOnly` | The account's own eyes; `Always` whitens them |
