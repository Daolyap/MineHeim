# Changelog

## 1.0.0

### The Nether and the End come to Valheim

- **Light a Nether portal** in Minecraft (an obsidian frame and flint and steel; in Survival, throw Valheim's Obsidian, Flint and
  Iron across for them) and nobody goes through it: instead the land around it turns to Nether. Minecraft lays a crust of
  netherrack, crimson and warped nylium, soul soil, magma and Nether ores over Valheim's ground, with fire, roots and fungi on top,
  spreading out from the portal (8 m a minute, up to 48 m). In Valheim the infected ground becomes the Ashlands' (ash with glowing
  lava cracks, the Ashlands' clutter instead of grass), and standing deep inside it brings the Ashlands' sky and ash rain.
- **Nether mobs spawn on infected land:** zombified piglins, piglins and magma cubes; hoglins, blazes and wither skeletons as it
  grows; the odd ghast once it is big (its fireballs blow craters into Valheim).
- **Fire, lava and magma blocks burn the Viking** in Survival (Valheim fire damage: he catches fire, fire resistance helps).
- **Break the portal and the land heals**, outside in, until it is as it was. A **ward** keeps an infection out of its area, and
  placing one on infected land cleans it. The portal gets a map pin. Infections are saved with the Minecraft world.
- **You hear it and feel the heat.** The Nether wastes' drone plays over infected land; the air shimmers with heat and the light
  turns warm (`HeatHaze` in ReShade's settings scales it).
- **Valheim's creatures react.** Animals run from a lit portal's land; creatures standing on Nether ground burn, unless they are
  fireproof, tamed or a boss (`Nether.CreaturesBurn`, `BurnDamage`, `AnimalsFlee`).
- **The End comes too.** Fill an End portal's twelve frames with eyes (Creative) and the land fades into the End: end stone,
  purpur ruins and end rods, chorus flowers, obsidian spikes crowned with end crystals. In Valheim it becomes the Mistlands, with
  its mist and sky deep inside and a cold violet light. Endermen, endermites and shulkers walk it, and the End now and then warps
  a Valheim creature a few metres. Once it has spread far enough the **Ender Dragon** comes and circles the portal. Kill it, or
  break one of the frames, and the portal goes out and the land heals; fill it again and a new dragon can come. Settings: `End.*`.
- Nothing is saved into the Valheim world: the Ashlands and Mistlands looks are drawn, not painted into the terrain.

### One inventory, one Steve

- **One inventory.** Tab in Minecraft mode shows the Viking's bag beside Minecraft's inventory, with Valheim's own icons. Click a
  Valheim item to put a stand-in for it on Steve's hotbar: selected, the Viking holds the real item and the mouse is Valheim's, so
  the hammer and hoe build, Valheim weapons attack and food is eaten, without leaving Minecraft mode. Items with a twin move across
  with a shift-click (or by putting them down on the bag). **[open]** shows the Viking's own inventory.
- **Valheim draws Steve itself**, on the Viking in the same frame, so he no longer trails or doubles up when you run or turn. He
  walks, crouches, sits, turns his head and swings his arm when Minecraft does (`Steve.Avatar = Minecraft` brings back the old way).
- **Steve holds and wears what he has:** held items in both hands and armour, exactly as Minecraft draws them; leather keeps its dye.
- **Skins:** `Steve.Skin` = Steve (the default), Account (your Minecraft account's skin, slim arms included) or File (any skin PNG,
  `Steve.SkinFile`). Old 64x32 skins work too.
- **Was it Herobrine all along?** Steve has white, glowing eyes (`Steve.HerobrineEyes`).
- **Free look:** hold Alt and the mouse swings the camera around Steve, so you can see his face (`Steve.FreeLook`).

### Fighting

- **Weapons are Valheim weapons.** Steve's weapons fight as a Valheim weapon of their tier (wood, stone and gold: Meadows; copper:
  Black Forest; iron: Swamp; diamond: Mountains; netherite: Plains or later), with Valheim's damage types, skills, stamina, stagger
  and knockback. Spam-clicking runs out of stamina, a fist is a fist, and bosses take a proper fight. Enchantments count
  (Sharpness: quality, Fire Aspect: fire, Smite: spirit). `Events.SteveWeaponStats`.
- **Steve hits what the crosshair is on**; only swords sweep (an axe, a club or a pickaxe hits the one creature it is swung at).
- **Tools:** wood and gold pickaxes mine only stone, as in Minecraft (ore is too hard). A pickaxe digs Valheim's ground only on a
  full swing, and works on big rocks part by part.
- **TNT breaks rocks and ore** and hurts creatures with Minecraft's own blast formula at the exchange rate: TNT that kills a zombie
  kills a Greydwarf.
- **Minecraft's mobs and Valheim's creatures really fight.** Monsters hunt creatures as far as they would hunt you, piglins and
  hoglins fight back, and a ghast's or blaze's fireball that passes close to a creature bursts on it (a Gjall drifting out of
  the way is still hit). Zombified piglins and endermen only fight back, as in Minecraft.
- **Minecraft's mobs fight the Viking too**, at Minecraft's pace (after a hit only a harder one counts for half a second).
  Creative only protects Steve: as the Viking (F7 off), Minecraft's monsters hunt you.

### Food

- **Eating as Steve feeds the Viking** (Survival): a Minecraft food fills one of the Viking's food slots as its Valheim twin, or as
  the Valheim food closest to how much it feeds in Minecraft (raw meat, rotten flesh, anything else).

### Worlds, picture and controls

- **Each Valheim world has its own Minecraft world.** What Steve builds in one world stays there; switching worlds switches
  Minecraft's too (saved first). The first world you play takes over the old shared Minecraft world, so nothing is lost.
- **Valheim's HUD stays on top:** creature names and health bars, damage numbers and messages are no longer hidden by Minecraft's
  blocks (with the matching compositor add-on).
- **Valheim's hotbar hides in Minecraft mode** (health, stamina and food stay). `Steve.HideValheimHotbar`.
- **Minecraft's music is off** by default; `Audio.MinecraftMusic` and `Audio.MinecraftVolume` set it from Valheim.
- **New keys:** F10 shows MineHeim's status with both games' frame rates (and starts Minecraft again if it crashed or was closed),
  F11 switches who draws Steve, Alt + wheel zooms Valheim's camera in Minecraft mode.
- **Minecraft always closes with Valheim:** after a quit, the process ends a few seconds after the world is saved, or after 30 s
  at most.
- Building from source: the plugin always builds where the installer looks, and the build stops if that DLL is out of date.

## 0.9.1

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

## 0.9.0 — MineHeim

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
