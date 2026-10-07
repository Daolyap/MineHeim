# Changelog

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
