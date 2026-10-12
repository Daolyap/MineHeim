# Play test round 4

Round 3 ran an old Valheim plugin: the build had gone to `bin\x64\Release` and the installer copied an older DLL from
before round 3. Every check that depends on the Valheim side (the bag's items, held items and armour, Herobrine's eyes,
weapons, food, `[open]`) has to be seen again. Install as before:

```powershell
git fetch; git switch v1/round3-seamless; git pull
.\Install-MineHeim.ps1 -Build -Only Valheim,Compositor,Minecraft
```

In `BepInEx\LogOutput.log` the line `steve weapons (world at ...)` appears at the first swing, and `steve skin: ..., Herobrine's
eyes` when Steve is dressed: if either is missing, the old plugin is still installed.

## Checked overnight from the logs (both games, the test world, no screen)

| Check | Result |
|---|---|
| FD1, FD2 | Cooked beef as Steve: the Viking ate Cooked deer meat; again while it is in a slot: given back. Bread likewise |
| D9 | A full-draw arrow hit a Greydwarf for 14.1 (Valheim's Bow + wood arrow) |
| D8 | Copper deposit: a wooden pickaxe is too weak, a stone one mines it |
| D10 | Fire Aspect II adds fire damage (7.7 on a diamond sword), Smite V spirit |
| D6 | Only swords sweep several creatures |
| X2 | Three zombies on the Viking: about one hit a second (Minecraft's pace), not every swing |
| B5, "TNT doesn't blow up stone" | A TNT blast hit 50 parts of a big rock (it used to hit one, and Valheim ignored that) |
| "TNT doesn't do enough damage" | One TNT at 5 m killed a Greydwarf |
| Mobs vs creatures | Zombies landed 8 hits on a Greydwarf and killed it; piglins attacked one; a ghast's fireballs took a Gjall from 1500 to 1324 |
| U1 (data) | The bag reaches Minecraft (items and icons) |
| U3, U4 (data) | A hammer and a bronze sword stand-in each equip the real item |
| G15 (data) | Iron helmet and red leather chestplate are sent to Valheim's Steve |
| I2 | Oak logs thrown in Survival arrive in the Viking's bag as Wood |
| Worlds | Each Valheim world gets its own Minecraft world; logging out and into another world switches Minecraft's (CTestWorld keeps your builds) |

## Needs your eyes

| Check | Do | Pass when |
|---|---|---|
| U1 | F7, Tab (Creative and Survival) | The Viking's bag sits beside the inventory without overlapping it (smaller at a large GUI scale), with Valheim's icons and counts |
| U2-U10 | As in round 3 | As in round 3 |
| G12b-G16, A14b | As in round 3 (F11 until Valheim draws Steve) | Items in his hand, armour on him, the shield on his arm |
| R1-R5 | Hold **Alt** and move the mouse to swing the camera round to Steve's face (new: free look) | White eyes; they glow at night |
| FL1 | Hold Alt, move the mouse, let go | The camera circles Steve while he keeps facing the same way; it comes back behind him |
| FL2 | Alt + wheel | Still zooms |
| D2-D5, D7, D11, D12 | As in round 3 | As in round 3 |
| B3 | Click fast with a pickaxe on bare ground | Only full swings dig |
| B4 | Diamond axe on a beech | Several chops, not one |
| W1 | Two Valheim worlds, build something in one | It is not in the other; back in the first it is still there |
| P4 | F10 | Shows Valheim's and Minecraft's frame rates; tell me both if Minecraft feels slow (and where you are: Nether/End land, the dragon) |
| W2 | Minecraft wall at night next to a campfire | Note whether blocks still look see-through (it may be the relighting borrowing Valheim's picture behind the block) |
