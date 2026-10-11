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
| FD1 | Survival, F7 on, with an empty food slot: eat cooked beef (or bread) as Steve | The Viking gets Cooked deer meat (Bread) in a food slot: its icon, more max health and stamina; `LogOutput.log`: `steve ate ...: the Viking ate ...` |
| FD2 | Eat the same food again while it is still in a slot | Nothing eaten in Valheim, the item is back in Steve's inventory |
| FD3 | Fill all three slots | Minecraft's hunger bar is full and Steve cannot start eating (like Valheim) |
| FD4 | Eat raw beef or rotten flesh | Nothing in Valheim (the item is gone, as in Minecraft) |
| FD5 | Creative: eat something | A message: Creative food stays in Minecraft; no Valheim food |
| R1 | Look at Steve's face (F7 on; a mirror view: walk toward the camera with Alt + wheel zoomed out) | White eyes, no pupils |
| R2 | At night or in a dark crypt | The eyes glow (bright while the rest of him is dark) |
| R3 | F11 (Minecraft draws Steve) | His eyes are white there too (lit like the rest of him, no glow) |
| R4 | `Steve.HerobrineEyes = Off`, then back to `Always` | Steve's own eyes come back, then Herobrine's |
| R5 | `Steve.Skin = Account` with `HerobrineEyes = ClassicSteveOnly` | The account's own eyes; `Always` whitens them |
| G12b | Valheim draws Steve (F11 until it says Valheim): hold a diamond sword, a pickaxe, a block, a torch, a bow | Each is in his right hand as Minecraft holds it (compare with F11); F10 shows `skin ..., <item>` |
| A14b | Shield in the off hand, raise it (right button) | The shield model is on his left arm and comes up with the block pose |
| G13 | Change slots quickly with the wheel | The item in his hand follows within a tick; no leftovers in the hand |
| G14 | Hold a dyed or enchanted item, a spawn egg, a potion | Its colours as in Minecraft (no enchantment glint) |
| G15 | Wear iron armour, then leather dyed red, then a turtle helmet | Each piece on him like in Minecraft (leather red, with its overlay); taking it off removes it |
| G16 | Slim skin (`Steve.Skin = File` with `SkinSlimArms = true`) | Held items still sit in the hand |
| U1 | F7 on, Tab | Minecraft's inventory with "Viking's bag" beside it: the Viking's items with their Valheim icons and counts, equipped ones outlined green |
| U2 | Click the hammer in the bag | A hammer stand-in (Valheim icon) appears on Steve's selected or first free hotbar slot; the hammer is still in the bag |
| U3 | Select that slot, close the inventory | The Viking holds the hammer; left click places, right button opens the build menu, the wheel turns the piece; 1-9 picks a Minecraft slot again |
| U4 | Stand-in for a Valheim sword or axe, attack a Greydwarf | Valheim's own attack (stamina, damage), Steve's arm swings |
| U5 | Stand-in for cooked meat or a mead, right button | The Viking eats or drinks it |
| U6 | Throw the stand-in (Q), or put it down on the bag | It disappears; the real item stays in the bag |
| U7 | Drop the real item from the Viking's bag (F7 off) | The stand-in turns reddish (the Viking no longer has it) and does nothing when selected |
| U8 | Survival: shift-click Wood in the bag | It moves into Minecraft as oak logs (into Steve's inventory) |
| U9 | Survival: pick up Minecraft cobblestone in the inventory, click it down on the bag | It moves into Valheim as Stone (the Viking picks it up) |
| U10 | Click [open] on the bag | Minecraft's screen closes and the Viking's Valheim inventory opens |
| U11 | As Steve: E on a Valheim door, chest, workbench, bed, portal | Each works as for the Viking |
| G17 | Look in `latest.log` (Minecraft) after changing items | `held item for the host: main hand ... (N quads)` lines; tell me if one says 0 quads for an item you could see |
