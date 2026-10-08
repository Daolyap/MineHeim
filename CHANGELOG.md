# Changelog

## 1.0.0 (in progress, branch `v1/nether-infection`)

- **The Nether comes to Valheim.** Light a Nether portal in Minecraft (an obsidian frame and flint and steel; in Survival, throw
  Valheim's Obsidian, Flint and Iron across for them) and nobody goes through it: instead the land around it turns to Nether. Minecraft lays a crust of netherrack, crimson and warped nylium, soul soil, magma
  and Nether ores over Valheim's ground, with fire, roots and fungi on top, spreading out from the portal (8 m a minute, up to 48 m).
  In Valheim the infected ground becomes the Ashlands' (ash with glowing lava cracks, the Ashlands' clutter instead of grass), and
  standing deep inside it brings the Ashlands' sky and ash rain. Ash, spores and embers drift in the air.
- **Nether mobs spawn on infected land:** zombified piglins, piglins and magma cubes; hoglins, blazes and wither skeletons as it
  grows; the odd ghast once it is big (its fireballs blow craters into Valheim). They fight Valheim's creatures like any Minecraft mob.
- **Fire, lava and magma blocks burn the Viking** in Survival (Valheim fire damage: he catches fire, fire resistance helps).
- **Break the portal and the land heals**, outside in, until it is as it was. A **ward** keeps an infection out of its area, and placing
  one on infected land cleans it. The portal gets a map pin. Infections are saved with the Minecraft world.
- Nothing is saved into the Valheim world: the Ashlands look is drawn, not painted into the terrain. Settings: `Nether.*`.

## 0.9.1 — fixes from the first real play test

- **F7 now switches who you are.** Off: you are the Viking (his own body, hotbar and controls; Minecraft's blocks, mobs and items stay
  in the world). On: you are Steve (Minecraft's hotbar, hand and controls; flight and the shield). The game starts as the Viking.
- **Creative protects the Viking** while you are Steve: no damage, no stamina used (`Steve.CreativeInvulnerable`). Not Valheim's own
  god mode, so drops still count.
- **Hit with anything:** every swing deals the held item's Minecraft damage (fist 1, diamond sword 7, ...), scaled by the recharge
  exactly as Minecraft scales it, so rapid clicking is weak. One point is at least 6 Valheim damage (`Events.WeaponPointDamage`), more
  for a well-fed Viking. The recharge shows next to Minecraft's hotbar.
- **Wards stop explosion damage** to buildings and the ground (`Events.WardsBlockExplosions`).
- **Steve keeps up when you walk or run:** the Viking's position is sent ahead by the measured delay of Minecraft's picture.
- **Valheim's menus are never drawn under Minecraft:** while one is open (pause, inventory, map, ...) Minecraft's picture steps aside.
- **No frozen Steve:** if Minecraft closes or hangs, its picture goes within 1.5 s.
- **Minecraft follows Valheim out:** it saves and quits when Valheim quits, crashes or is ended in Task Manager; if anything keeps it
  alive, it ends itself a minute later. (A Java process used to stay behind.)
- **Minecraft's screens:** a cursor is shown; Backspace, Delete, the arrows and Ctrl+A/C/V work in its chat and text boxes.
- **E as the Viking opens a Minecraft chest**, crafting table or furnace under the crosshair.
- Minecraft's mobs and items stand on Valheim's real ground (not up to a block inside it); Steve stays visible while swimming;
  middle-click no longer picks the invisible barrier ground; distant blocks no longer look see-through.
- `Install-MineHeim.ps1 -Only Valheim,Compositor` works through `Install.cmd` too (the list arrived as one word).
- **Building from a clone just works:** `Install-MineHeim.ps1` builds whatever is not built yet before installing; `-Build` rebuilds,
  `-Only Valheim|Compositor|Minecraft` limits it to the parts you name. `tools\build.ps1` finds the .NET SDK, a JDK 25, MSVC and
  your Valheim install (any Steam library) itself, and names the install command for anything missing. A plain `dotnet build`
  now finds Valheim through Steam's registry entry and fails with one clear message instead of 195 errors.

## 0.9.0 (release candidate) — MineHeim

The first public build, under its new name.

- **One-click Minecraft setup:** `Install.cmd` also installs Prism Launcher (portable, hash-checked) with a ready MineHeim instance;
  sign in once and Valheim starts Minecraft by itself from then on.
- **Minecraft comes and goes with Valheim:** auto-start, its window kept behind Valheim and out of the taskbar and Alt-Tab, and it saves
  and quits when Valheim closes.
- **Minecraft's own screens over Valheim:** Tab opens Minecraft's inventory (creative tabs in Creative), T its chat, / a command line;
  mouse, keys and typed text are forwarded.
- **F8** switches Steve between Creative and Survival.
- Upgrading from a Valheimcraft build removes the old install first.
- Renamed everywhere: mod id `mineheim`, plugin `dev.daolyap.mineheim`, `MineHeim.dll`, `MineHeim.addon64`, `MineHeim.fx`.

## 0.4.0

- Minecraft mobs and Valheim creatures fight each other: invisible stand-ins in both worlds, hits converted at one exchange rate
  (a Minecraft point = the Viking's max health / 20). Steve's sword and arrows deal their real Minecraft damage.
- Survival: Minecraft's hearts and hunger show the Viking's health and food.

## 0.3.0

- Items cross between the worlds when thrown, moved and never copied (about 45 pairs, plus your own).
- Valheim's buildings become whole Minecraft collision (doors included).
- One light: Minecraft's sun follows Valheim's day; Valheim's fires light Minecraft; Minecraft's lamps light Valheim.

## 0.2.0

- Explosions damage Valheim's buildings, trees and rocks and dig craters; a Minecraft pickaxe digs terrain; diamond axe.
- Creative flight (double-tap Jump), an OP Minecraft shield, Creative/Survival game mode; Valheim owns Steve's health.

## 0.1.0

- Real Minecraft composited into Valheim, depth-correct: shared camera, terrain as barrier blocks, Minecraft blocks as Valheim colliders,
  Minecraft mode (F7), TNT, swords, arrows and tools acting on Valheim; one-step installer.
