# Configuration

MineHeim's settings are in `Valheim\BepInEx\config\dev.daolyap.mineheim.cfg`, written the first time Valheim runs with the mod.
Edit it with Valheim closed (or with a BepInEx configuration manager in game: most settings apply at once).
In this file a single `\` is an escape character: write paths with `/` or `\\`.

| section.key | default | what it does |
|---|---|---|
| **Minecraft** | | |
| `Minecraft.AutoStart` | on | start Minecraft when Valheim starts (the Prism Launcher instance from `Install.cmd`, or `LaunchCommand`) |
| `Minecraft.LaunchCommand` | empty | your own command, e.g. `"C:/Prism/prismlauncher.exe" --launch MineHeim`; empty = the installed Prism |
| `Minecraft.HideWindow` | on | keep Minecraft's own window behind Valheim and out of the taskbar and Alt-Tab while attached |
| `Minecraft.QuitWithValheim` | on | Minecraft saves and quits when Valheim closes |
| **Input** | | |
| `Input.ToggleKey` | F7 | Minecraft mode on/off |
| `Input.GameModeKey` | F8 | Steve: Creative <-> Survival |
| `Input.StartInMinecraftMode` | off | start every game in Minecraft mode |
| **Steve** | | |
| `Steve.GameMode` | Creative | Creative: builder's kit and flight; Survival: Minecraft's rules, no flight. Valheim owns health in both |
| `Steve.FlySpeed` | 11 | Creative flight speed in m/s (Run = 2.5x) |
| `Steve.HideViking` | on | hide the Viking's body (not his held items or lights) while Minecraft is connected |
| **Shield** | | |
| `Shield.Enabled` | on | a raised Minecraft shield blocks Valheim attacks |
| `Shield.BlockFraction` | 1 | share of a blockable hit from the front that it stops (1 = all, no stamina) |
| `Shield.ReflectFraction` | 0.5 | share of the blocked damage thrown back at the attacker |
| `Shield.StaggerAttacker` | on | every block is a perfect parry: the attacker staggers |
| **Events** (Minecraft acting on Valheim) | | |
| `Events.WeaponDamageScale` | 1 | Minecraft damage on Valheim creatures = Minecraft damage x (Viking's max health / 20) x this |
| `Events.MinecraftDamageShare` | 1 | a Minecraft mob's hit takes this times its share of Steve's 20 from the Viking's max health (0 = harmless) |
| `Events.ExplosionDamage`, `ExplosionPush`, `ExplosionReach` | 60, 60, 1.6 | a Minecraft explosion's damage and knockback on creatures, and metres of reach per block of radius |
| `Events.HurtPlayer` | on | Minecraft explosions hurt the Viking too |
| `Events.ExplosionStructureDamage` | 150 | explosion damage to Valheim buildings, trees and rocks (0 = off) |
| `Events.ExplosionCraters` | on | explosions dig craters (not inside wards or no-build areas) |
| `Events.PickaxeDigs` | on | a Minecraft pickaxe at bare ground digs like a Valheim pickaxe |
| `Events.MeleeReach`, `ToolDamage` | 3.6 m, 80 | reach of Minecraft swings; pickaxe/axe damage on rocks, ore and trees |
| `Events.SwordDamage`, `ArrowDamage` | 35, 30 | only used if the Minecraft mod does not report its own damage (an older mod) |
| **Mobs** | | |
| `Mobs.Enabled` | on | Minecraft mobs and Valheim creatures stand in each other's world and fight |
| `Mobs.MinecraftMonstersHunt` | on | idle Minecraft monsters go after Valheim creatures |
| `Mobs.Radius` | 40 | Valheim creatures this close (m) stand in Minecraft too |
| `Mobs.BodyPrefab` | Deer | the invisible Valheim creature a Minecraft mob's body is made from |
| **Items** | | |
| `Items.Cross` | on | thrown items cross into the other world when they have a twin |
| `Items.CreativeToValheim` | off | let Creative's free items cross into Valheim too |
| `Items.ExtraPairs` | empty | your own pairs: `ValheimPrefab=minecraft_item` or `...=item*ratio`, comma-separated, e.g. `Tin=iron_nugget*2, Wood=spruce_log` |
| **Buildings** | | |
| `Buildings.Enabled` | on | Valheim's buildings become Minecraft collision |
| `Buildings.Radius`, `TestsPerFrame`, `MaxCells` | 32, 600, 40000 | how far, how fast, how much |
| **Lighting** | | |
| `Lighting.TimeSync` | on | Minecraft's sun follows Valheim's day (off = always noon in Minecraft) |
| `Lighting.FireLights` | on | Valheim's fires light Minecraft's blocks |
| `Lighting.MinecraftLamps`, `MaxLamps`, `LampIntensity` | on, 12, 1.4 | Minecraft's lamps light Valheim (the nearest few) |
| **Ground, blocks, world** | | |
| `Ground.Enabled`, `Radius`, `ProbesPerFrame` | on, 40, 160 | Valheim's terrain as Minecraft barrier blocks |
| `Blocks.MaxColliders` | 3000 | at most this many Minecraft blocks become colliders in Valheim |
| `World.YOffset` | 70 | Minecraft y = Valheim y + this. Keep it fixed: Minecraft's world persists |
| `Link.Port` | 25599 | the local port of the link (the Minecraft side reads `-Dmineheim.port`) |
| `General.Enabled` | on | send Valheim's camera to Minecraft |
| `Debug.PillarTest` | F9 | alignment test pillar (developers) |

## Item pairs

Built in (Valheim = Minecraft; the first Minecraft item is what a Valheim item becomes, the others also cross back):

Wood = oak log (any log or wood, or 4 planks) · FineWood = birch log · RoundLog = spruce log · ElderBark = dark oak log ·
Stone = cobblestone (stone, deepslate, cobbled deepslate, andesite, diorite, granite, tuff) · Flint · Resin = resin clump ·
LeatherScraps = leather · DeerHide = rabbit hide · Feathers = feather · BoneFragments = bone · Coal (or charcoal) ·
CopperOre = raw copper · Copper = copper ingot · IronScrap = raw iron · Iron = iron ingot · Obsidian ·
Crystal = amethyst shard (or quartz) · BlackMetalScrap = netherite scrap · BlackMetal = netherite ingot ·
Raspberry = sweet berries · Blueberries = glow berries · Mushroom = red mushroom · MushroomYellow = brown mushroom · Carrot ·
Turnip = beetroot · Barley = wheat · Honey = honey bottle · Dandelion · RawMeat = porkchop · DeerMeat = beef ·
CookedMeat = cooked porkchop · CookedDeerMeat = cooked beef · FishRaw = cod (or salmon) · FishCooked = cooked cod (or cooked salmon) ·
Entrails = rotten flesh · Bread · Coins = gold nugget · Ruby = emerald · Guck = slime ball · ArrowWood = arrow ·
SurtlingCore = fire charge · GreydwarfEye = spider eye · DragonEgg = dragon egg · ChickenEgg = egg · LinenThread = string
