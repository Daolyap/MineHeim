using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using MineHeim.Core;

namespace MineHeim
{
    /// <summary>
    /// Valheim half of the passthrough: real Minecraft runs next to Valheim and is drawn into its frame.
    /// This plugin reads Valheim's camera and world and talks to the Minecraft mod over 127.0.0.1.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dev.daolyap.mineheim";
        public const string Name = "MineHeim";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Port;
        internal static ConfigEntry<double> YOffset;
        internal static ConfigEntry<KeyCode> PillarKey;
        internal static ConfigEntry<KeyCode> ToggleKey;
        internal static ConfigEntry<bool> StartInMinecraftMode;
        internal static ConfigEntry<bool> HideViking, HideValheimHotbar, FreeLookOn;
        internal static ConfigEntry<float> ExplosionDamage, ExplosionPush, ExplosionReach, MeleeDamage, MeleeReach, ToolDamage, ArrowDamage;
        internal static ConfigEntry<bool> HurtPlayer;
        internal static ConfigEntry<SteveMode> GameMode;
        internal static ConfigEntry<float> FlySpeed;
        internal static ConfigEntry<bool> ShieldEnabled, ShieldStagger, ExplosionCraters, PickaxeDigs;
        internal static ConfigEntry<float> ShieldBlock, ShieldReflect, ExplosionStructureDamage, MinecraftDamageShare;

        internal enum SteveMode
        {
            Creative,
            Survival,
        }

        internal enum AvatarMode
        {
            Native,
            Minecraft,
        }

        internal enum SkinChoice
        {
            Steve,
            Account,
            File,
        }

        internal enum HerobrineMode
        {
            Always,
            ClassicSteveOnly,
            Off,
        }

        internal static ConfigEntry<HerobrineMode> HerobrineEyes;
        internal static ConfigEntry<AvatarMode> SteveAvatarMode;
        internal static ConfigEntry<SkinChoice> SteveSkin;
        internal static ConfigEntry<string> SteveSkinFile;
        internal static ConfigEntry<bool> SteveSkinSlim;
        internal static ConfigEntry<bool> GroundEnabled;
        internal static ConfigEntry<int> GroundRadius, GroundPerFrame, MaxColliders;
        internal static ConfigEntry<bool> BuildingsEnabled, ItemsCross, CreativeToValheim, TimeSync, FireLights, MinecraftLamps;
        internal static ConfigEntry<int> BuildingRadius, BuildingTestsPerFrame, BuildingMaxCells, MaxLamps;
        internal static ConfigEntry<float> LampIntensity;
        internal static ConfigEntry<string> ItemPairs, MobBodyPrefab;
        internal static ConfigEntry<bool> MobsEnabled, MobsHunt, AutoStart, HideMinecraftWindow, QuitWithValheim;
        internal static ConfigEntry<string> LaunchCommand;
        internal static ConfigEntry<KeyCode> GameModeKey, StatusKey, AvatarKey;
        internal static ConfigEntry<float> WeaponDamageScale, MobRadius, WeaponPointDamage;
        internal static ConfigEntry<bool> CreativeInvulnerable, WardsBlockExplosions, PredictMotion, SteveWeaponStats;
        internal static ConfigEntry<bool> NetherEnabled, NetherAshGround, NetherWeather, NetherMapPin;
        internal static ConfigEntry<float> NetherMaxRadius, NetherGrowPerMinute, NetherHealPerMinute;
        internal static ConfigEntry<int> NetherMobCap, NetherSpawnSeconds;
        internal static ConfigEntry<string> NetherEnvironment, EndEnvironment;
        internal static ConfigEntry<bool> NetherCreaturesBurn, NetherAnimalsFlee, EndEnabled, EndDragon, EndWeather, EndCreaturesWarp;
        internal static ConfigEntry<float> NetherBurnDamage;
        internal static ConfigEntry<float> MinecraftMusic, MinecraftVolume;
        internal static readonly NetherBridge Nether = new NetherBridge();
        internal static readonly MobBridge Mobs = new MobBridge();

        internal static readonly BarrierCells Cells = new BarrierCells();
        internal static readonly GroundProbe Ground = new GroundProbe(Cells);
        internal static readonly PieceVoxels Pieces = new PieceVoxels(Cells);
        internal static readonly BlockColliders Blocks = new BlockColliders();
        internal static readonly LightSync Lights = new LightSync();
        internal static readonly ItemBridge Items = new ItemBridge();
        internal static readonly TestHooks Hooks = new TestHooks();
        internal static readonly WorldEvents Events = new WorldEvents();

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, "Send Valheim's camera to Minecraft.");
            Port = Config.Bind("Link", "Port", 25599, "Port of the Minecraft mod's WebSocket link (127.0.0.1 only).");
            PredictMotion = Config.Bind("Link", "PredictMotion", true,
                "Minecraft's picture arrives a frame or two after Valheim's camera has moved on: send Minecraft where the camera and the Viking will be by " +
                "the time its picture is shown, so Steve keeps up with the Viking while walking and running.");
            YOffset = Config.Bind("World", "YOffset", 70.0,
                "Minecraft y = Valheim y + this. 70 puts Valheim's sea level (30) at Minecraft y=100. Keep it fixed: Minecraft's world persists, " +
                "so changing it moves everything you built against Valheim's terrain.");
            ToggleKey = Config.Bind("Input", "ToggleKey", KeyCode.F7,
                "Minecraft mode: on, you are Steve: he stands where the Viking stands, Minecraft's hotbar shows, and the mouse buttons, the wheel and the number keys 1-9 " +
                "go to Minecraft (break, place, pick, hotbar). Off, you are the Viking again: Valheim's own body, hotbar and controls (Minecraft's blocks, mobs and items stay in the world).");
            HideViking = Config.Bind("Steve", "HideViking", true,
                "In Minecraft mode, hide the Viking's body (not his held items or lights): Steve stands where he stands.");
            HideValheimHotbar = Config.Bind("Steve", "HideValheimHotbar", true,
                "In Minecraft mode, hide Valheim's hotbar (Minecraft's is the one in use). Health, stamina and food stay.");
            FreeLookOn = Config.Bind("Steve", "FreeLook", true,
                "Hold Alt to look around: the mouse swings the camera around Steve (or the Viking) while he keeps facing where he was. " +
                "Let go and the camera comes back behind him. Alt + wheel still zooms.");
            SteveAvatarMode = Config.Bind("Steve", "Avatar", AvatarMode.Native,
                "Who draws Steve. Native: Valheim, on the Viking in the same frame, so he never trails behind or doubles up when you run or turn " +
                "(his held items and armour are Minecraft's own models, sent over). Minecraft: Minecraft's own picture of him, a frame or two late.");
            SteveSkin = Config.Bind("Steve", "Skin", SkinChoice.Steve,
                "Steve's skin. Steve: classic Steve. Account: the skin of the Minecraft account you play on (fetched from Mojang when Minecraft starts). " +
                "File: a skin PNG of your own (Steve.SkinFile).");
            SteveSkinFile = Config.Bind("Steve", "SkinFile", "",
                "With Skin = File: the path of a Minecraft skin PNG (64x64 or old 64x32), e.g. C:/Users/you/Downloads/skin.png. Write paths with /.");
            SteveSkinSlim = Config.Bind("Steve", "SkinSlimArms", false, "With Skin = File: the skin is for the slim (3-pixel) arms model (\"Alex\").");
            SteveAvatarMode.SettingChanged += (_, __) => InputBridge.SendSteve();
            HerobrineEyes = Config.Bind("Steve", "HerobrineEyes", HerobrineMode.Always,
                "Steve infects every game he touches: was it Herobrine all along? His eyes are white and glow in the dark. Always: on any skin " +
                "(at the classic skin's eye pixels). ClassicSteveOnly: only on classic Steve, not on an account or file skin. Off: Steve's own eyes.");
            HerobrineEyes.SettingChanged += (_, __) => SteveAvatar.RequestSkin();
            SteveSkin.SettingChanged += (_, __) => SteveAvatar.RequestSkin();
            SteveSkinFile.SettingChanged += (_, __) => SteveAvatar.RequestSkin();
            SteveSkinSlim.SettingChanged += (_, __) => SteveAvatar.RequestSkin();
            CreativeInvulnerable = Config.Bind("Steve", "CreativeInvulnerable", true,
                "In Creative, while you are Steve (Minecraft mode on), the Viking takes no damage and uses no stamina, as in Minecraft's Creative.");
            StartInMinecraftMode = Config.Bind("Input", "StartInMinecraftMode", false, "Start every game in Minecraft mode.");
            GameModeKey = Config.Bind("Input", "GameModeKey", KeyCode.F8, "Switch Steve between Creative and Survival.");
            StatusKey = Config.Bind("Input", "StatusKey", KeyCode.F10,
                "Show MineHeim's status on screen: Minecraft attached or not, frame rate and Minecraft's delay, who draws Steve, the Nether and End portals near you. " +
                "While Minecraft is not attached (it crashed, or you closed it) it also starts it again.");
            AvatarKey = Config.Bind("Input", "AvatarKey", KeyCode.F11,
                "Switch who draws Steve (Steve.Avatar): Valheim (no trailing when you run or turn) or Minecraft (a frame or two late).");
            AutoStart = Config.Bind("Minecraft", "AutoStart", true,
                "Start Minecraft when Valheim starts (if none is running): the MineHeim instance of the Prism Launcher that Install.cmd set up, or Minecraft.LaunchCommand.");
            LaunchCommand = Config.Bind("Minecraft", "LaunchCommand", "",
                "Your own command to start Minecraft with MineHeim, e.g. \"C:/Prism/prismlauncher.exe\" --launch MineHeim. Write paths with / (or \\\\): a single \\ is an escape character in this file. Empty = the Prism Launcher from Install.cmd.");
            HideMinecraftWindow = Config.Bind("Minecraft", "HideWindow", true, "Hide Minecraft's own window while it is attached (its picture is in Valheim's). It comes back when Valheim goes.");
            QuitWithValheim = Config.Bind("Minecraft", "QuitWithValheim", true, "Minecraft saves and quits when Valheim closes.");
            GameMode = Config.Bind("Steve", "GameMode", SteveMode.Creative,
                "Minecraft's game mode for Steve. Creative: the builder's kit (refilled when you switch to it) and flight (double-tap Jump). " +
                "Survival: Minecraft's own rules for breaking, placing and items, no flight. Valheim owns health and death in both.");
            FlySpeed = Config.Bind("Steve", "FlySpeed", 11f, "Creative flight speed in m/s (Minecraft flies at ~11; hold Run for 2.5 times that). Jump rises, Left Ctrl sinks.");
            ShieldEnabled = Config.Bind("Shield", "Enabled", true, "A raised Minecraft shield (off hand, hold the right button in Minecraft mode) blocks Valheim attacks.");
            ShieldBlock = Config.Bind("Shield", "BlockFraction", 1f, "Share of a blockable hit from the front that the shield stops (1 = all of it, no stamina used).");
            ShieldReflect = Config.Bind("Shield", "ReflectFraction", 0.5f, "Share of the blocked damage thrown back at the attacker as blunt damage.");
            ShieldStagger = Config.Bind("Shield", "StaggerAttacker", true, "Every block counts as a perfect parry: the attacker staggers.");
            ExplosionStructureDamage = Config.Bind("Events", "ExplosionStructureDamage", 150f,
                "Damage of a Minecraft explosion at its centre to Valheim buildings (blunt), trees (chop) and rocks and ore (pickaxe); falls off to 0 at the reach. 0 = off.");
            ExplosionCraters = Config.Bind("Events", "ExplosionCraters", true, "Minecraft explosions dig a crater into Valheim's terrain (not inside wards or dungeons).");
            PickaxeDigs = Config.Bind("Events", "PickaxeDigs", true, "A Minecraft pickaxe swing at bare ground digs Valheim's terrain like a Valheim pickaxe.");
            MinecraftDamageShare = Config.Bind("Events", "MinecraftDamageShare", 1f,
                "A Minecraft mob's hit on Steve takes this times the share of the Viking's max health it would take of Steve's 20 " +
                "(a zombie's 3 is 15%: 3.75 of a 25-health Viking, 15 of a well-fed 100). 0 = Minecraft mobs cannot hurt the Viking.");
            GameMode.SettingChanged += (_, __) => SendGameMode();
            MinecraftMusic = Config.Bind("Audio", "MinecraftMusic", 0f,
                new ConfigDescription("Volume of Minecraft's music (0-1). Minecraft's own window is hidden, so this is where you turn it up.", new AcceptableValueRange<float>(0f, 1f)));
            MinecraftVolume = Config.Bind("Audio", "MinecraftVolume", 1f,
                new ConfigDescription("Master volume of everything Minecraft plays (0-1).", new AcceptableValueRange<float>(0f, 1f)));
            MinecraftMusic.SettingChanged += (_, __) => SendAudio();
            MinecraftVolume.SettingChanged += (_, __) => SendAudio();
            ExplosionDamage = Config.Bind("Events", "ExplosionDamage", 60f, "Blunt damage of a Minecraft explosion at its centre (it falls off to 0 at the reach).");
            ExplosionPush = Config.Bind("Events", "ExplosionPush", 60f, "Knockback force of a Minecraft explosion at its centre.");
            ExplosionReach = Config.Bind("Events", "ExplosionReach", 1.6f, "Metres of reach per block of explosion radius (TNT has radius 4).");
            HurtPlayer = Config.Bind("Events", "HurtPlayer", true, "Minecraft explosions hurt the Viking too.");
            SteveWeaponStats = Config.Bind("Events", "SteveWeaponStats", true,
                "Steve's weapons fight as real Valheim weapons of their tier: wooden/stone/golden = Meadows, copper = Black Forest, iron = Swamp, " +
                "diamond = Mountains, netherite = Plains or the world's tier if higher; mace, trident, bow and crossbow keep up with the bosses beaten. " +
                "Valheim's damage types, skills, stamina, stagger and knockback apply; Minecraft's recharge scales each swing, and a fist is the Viking's fist. " +
                "Enchantments: Sharpness/Power raise the weapon's quality, Fire Aspect/Flame add fire, Smite spirit, Bane of Arthropods poison. " +
                "Off: the old way (Minecraft damage times WeaponPointDamage).");
            WeaponPointDamage = Config.Bind("Events", "WeaponPointDamage", 6f,
                "Valheim damage per point of Minecraft weapon damage, at least: the rate is the Viking's max health / 20 when that is higher (it grows with food " +
                "and progression). A fully charged diamond sword (7) deals 42, a fist (1) 6.");
            WeaponDamageScale = Config.Bind("Events", "WeaponDamageScale", 1f,
                "Minecraft damage on Valheim creatures (Steve's hits and arrows, Minecraft mobs' hits) is its Minecraft damage times the rate above, times this. " +
                "Valheim's hits on Minecraft mobs cross at the same rate the other way.");
            WardsBlockExplosions = Config.Bind("Events", "WardsBlockExplosions", true,
                "Minecraft explosions do not damage buildings or dig craters inside an active ward (creatures are still hurt).");
            MeleeDamage = Config.Bind("Events", "SwordDamage", 35f, "Slash damage of a Minecraft sword swing on Valheim creatures, only if Minecraft does not report the swing's damage (an older mod).");
            MeleeReach = Config.Bind("Events", "MeleeReach", 3.6f, "Reach of a Minecraft sword, pickaxe or axe swing, metres from the Viking's eyes.");
            ToolDamage = Config.Bind("Events", "ToolDamage", 80f, "Pickaxe or chop damage of a Minecraft pickaxe or axe swing on a Valheim rock, ore or tree.");
            ArrowDamage = Config.Bind("Events", "ArrowDamage", 30f, "Pierce damage of a Minecraft arrow that hits a Valheim creature, only if Minecraft does not report the arrow's damage.");
            MobsEnabled = Config.Bind("Mobs", "Enabled", true,
                "Minecraft's mobs and Valheim's creatures fight: Minecraft's monsters hunt Valheim's creatures, and Minecraft's mobs get an invisible body in Valheim " +
                "that Valheim's creatures attack and the Viking can hit with Valheim weapons.");
            MobsHunt = Config.Bind("Mobs", "MinecraftMonstersHunt", true, "Minecraft's monsters go after Valheim's creatures when they have nothing else to do.");
            MobsHunt.SettingChanged += (_, __) => Link.Send("{\"t\":\"mobfight\",\"on\":" + (MobsHunt.Value ? "true" : "false") + "}");
            MobRadius = Config.Bind("Mobs", "Radius", 40f, "Valheim creatures this close to the player (metres) stand in Minecraft too.");
            MobBodyPrefab = Config.Bind("Mobs", "BodyPrefab", "Deer",
                "The Valheim creature whose invisible body a Minecraft mob wears in Valheim (resized to the mob; its looks, AI, drops and sounds are taken out).");
            GroundEnabled = Config.Bind("Ground", "Enabled", true, "Send Valheim's ground to Minecraft as invisible barrier blocks.");
            GroundRadius = Config.Bind("Ground", "Radius", 40, "How far around the player (blocks) the ground is probed.");
            GroundPerFrame = Config.Bind("Ground", "ProbesPerFrame", 160, "Raycasts per frame for the ground (64 of them go to the 17x17 area around the player).");
            MaxColliders = Config.Bind("Blocks", "MaxColliders", 3000, "At most this many Minecraft blocks become colliders in Valheim.");
            BuildingsEnabled = Config.Bind("Buildings", "Enabled", true,
                "Valheim's buildings (walls, floors, roofs, doors) become whole invisible collision in Minecraft: its mobs and items stop at walls and walk under roofs.");
            BuildingRadius = Config.Bind("Buildings", "Radius", 32, "How far around the player (metres) building pieces are turned into Minecraft collision.");
            BuildingTestsPerFrame = Config.Bind("Buildings", "TestsPerFrame", 600, "Cell tests per frame for the building collision (a big house takes a few frames).");
            BuildingMaxCells = Config.Bind("Buildings", "MaxCells", 40000, "At most this many Minecraft cells of building collision.");
            ItemsCross = Config.Bind("Items", "Cross", true,
                "Items cross between the worlds by being thrown, moved and never copied: a Valheim item the Viking drops becomes its Minecraft twin, a Minecraft item " +
                "Steve throws (Q) becomes its Valheim twin. Items without a twin stay where they are.");
            CreativeToValheim = Config.Bind("Items", "CreativeToValheim", false,
                "Let Minecraft items cross into Valheim in Creative too. Creative's items are free, so this hands out free Valheim resources.");
            ItemPairs = Config.Bind("Items", "ExtraPairs", "",
                "More or different pairs, \"ValheimPrefab=minecraft_item\" or \"ValheimPrefab=minecraft_item*ratio\" (Minecraft items per Valheim item), comma-separated. " +
                "They replace the built-in pair for the same Valheim item. Example: Tin=iron_nugget*2, Wood=spruce_log");
            ItemPairs.SettingChanged += (_, __) => ItemBridge.Configure(ItemPairs.Value);
            ItemBridge.Configure(ItemPairs.Value);
            TimeSync = Config.Bind("Lighting", "TimeSync", true, "Minecraft's sun follows Valheim's day: its blocks dim at dusk and are dark at night. Off = always noon in Minecraft.");
            FireLights = Config.Bind("Lighting", "FireLights", true, "Valheim's burning fires, hearths and torches light Minecraft's blocks (as invisible Minecraft light blocks).");
            MinecraftLamps = Config.Bind("Lighting", "MinecraftLamps", true, "Minecraft's torches, lanterns, glowstone and lava light Valheim's world (as Unity point lights).");
            MaxLamps = Config.Bind("Lighting", "MaxLamps", 12, "At most this many of Minecraft's lamps, the nearest, light Valheim at once (each costs frame time).");
            LampIntensity = Config.Bind("Lighting", "LampIntensity", 1.4f, "Brightness of a Minecraft lamp's light in Valheim.");
            NetherEnabled = Config.Bind("Nether", "Enabled", true,
                "A lit Nether portal in Minecraft infects the land around it: the ground turns to a crust of Nether blocks, fire, roots and fungi, and the " +
                "Nether's mobs spawn there. Nobody goes to the Nether: it comes to Valheim. Break the portal and the land heals. Off: portals stay just portals.");
            NetherMaxRadius = Config.Bind("Nether", "MaxRadius", 48f, "How far from its portal an infection spreads at most (metres, up to 96).");
            NetherGrowPerMinute = Config.Bind("Nether", "GrowPerMinute", 8f, "How fast it spreads while the portal burns (metres a minute).");
            NetherHealPerMinute = Config.Bind("Nether", "HealPerMinute", 16f, "How fast the land heals once the portal is broken (metres a minute).");
            NetherMobCap = Config.Bind("Nether", "MobCap", 10, "At most this many Nether mobs per infection at once (a small infection has fewer). 0 = none.");
            NetherSpawnSeconds = Config.Bind("Nether", "SpawnSeconds", 8, "Seconds between Nether mob spawns while the Viking is near an infection.");
            NetherAshGround = Config.Bind("Nether", "AshlandsGround", true,
                "Infected land also wears the Ashlands' ground and clutter in Valheim (drawn only: nothing is saved to the Valheim world).");
            NetherWeather = Config.Bind("Nether", "AshlandsWeather", true, "Standing deep inside an infection brings the Ashlands' sky and weather.");
            NetherEnvironment = Config.Bind("Nether", "Environment", "auto", "Which of Valheim's environments that is; auto = the first whose name has Ashlands in it.");
            NetherMapPin = Config.Bind("Nether", "MapPin", true, "Mark an infection's portal on the map (not saved).");
            NetherCreaturesBurn = Config.Bind("Nether", "CreaturesBurn", true, "Valheim's creatures on Nether ground burn (not tamed ones, bosses or the fireproof).");
            NetherBurnDamage = Config.Bind("Nether", "BurnDamage", 2f, "Fire damage every 2 seconds to a creature on Nether ground (it also catches fire).");
            NetherAnimalsFlee = Config.Bind("Nether", "AnimalsFlee", true, "Animals (deer, boars, necks...) run from a lit portal's land, Nether or End.");
            EndEnabled = Config.Bind("End", "Enabled", true,
                "A filled End portal (twelve frames with eyes) infects the land like a Nether portal: end stone, chorus, obsidian spikes with end crystals, " +
                "endermen and shulkers, and the Mistlands' look in Valheim. Break a frame or kill the dragon and it heals. Uses the Nether section's reach and speeds.");
            EndDragon = Config.Bind("End", "Dragon", true, "Once an End infection has spread to three quarters of its reach, the Ender Dragon comes and circles its portal.");
            EndWeather = Config.Bind("End", "MistlandsWeather", true, "Standing deep inside an End infection brings the Mistlands' sky and mist.");
            EndEnvironment = Config.Bind("End", "Environment", "auto", "Which of Valheim's environments that is; auto = the first whose name has Mistlands in it.");
            EndCreaturesWarp = Config.Bind("End", "CreaturesWarp", true, "Now and then the End warps a Valheim creature on its land a few metres, as it warps endermen.");
            Config.SettingChanged += (_, args) =>
            {
                if (args.ChangedSetting.Definition.Section == "Nether" || args.ChangedSetting.Definition.Section == "End")
                {
                    NetherBridge.SendConfig();
                }
            };

            PillarKey = Config.Bind("Debug", "PillarTest", KeyCode.F9,
                "Alignment test: puts a Minecraft gold pillar 6 m ahead and logs Unity's own projection of it (BepInEx/mineheim-proj.csv).");

            // Step 3 oracle: this line in BepInEx/LogOutput.log proves the plugin loaded in the real game.
            Logger.LogInfo($"{Name} {Version} loaded");

            // Recon (handover section 5): which graphics API does Valheim 1.0 actually use?
            Logger.LogInfo($"unity {Application.unityVersion}, platform {Application.platform}");
            Logger.LogInfo($"graphics: {SystemInfo.graphicsDeviceType} | {SystemInfo.graphicsDeviceName} | {SystemInfo.graphicsDeviceVersion} | {SystemInfo.graphicsMemorySize} MB");
            Logger.LogInfo($"depth: reversedZ={SystemInfo.usesReversedZBuffer} copyTextureSupport={SystemInfo.copyTextureSupport}");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();

            InputBridge.McMode = StartInMinecraftMode.Value;
            HostPoseShm.Init();
            Link.Start("127.0.0.1", Port.Value, message => Logger.LogInfo("link: " + message));
            gameObject.AddComponent<PoseSender>();
        }

        /// <summary>Ask Minecraft for every solid block around the player (its world persists; Valheim's colliders do not).</summary>
        internal static void RequestBlockSync()
        {
            Link.Send("{\"t\":\"blocksync\",\"r\":48}");
        }

        /// <summary>A Minecraft world started (or the player entered a Valheim world): start the ground and the colliders afresh.</summary>
        internal static void ResetWorldState(string why)
        {
            Log.LogInfo("world state reset: " + why);
            if (ZNet.instance != null && Player.m_localPlayer != null)
            {
                // each Valheim world has its own Minecraft world (Minecraft switches to it if another is open)
                Link.Send(Messages.World(ZNet.instance.GetWorldName(), ZNet.instance.GetWorldUID()));
            }

            Cells.Reset();
            Ground.Reset();
            Pieces.Reset();
            Lights.Reset();
            Mobs.Clear();
            VikingStats.Reset();
            Blocks.Clear();
            Nether.Reset();
            RequestBlockSync();
            SendAudio();
            SendGameMode();
            InputBridge.SendSteve();
            SteveAvatar.RequestSkin();
            SteveGear.Request();
            VikingBag.Reset();
            Link.Send("{\"t\":\"mobfight\",\"on\":" + (MobsHunt.Value ? "true" : "false") + "}");
        }

        internal static void SendAudio()
        {
            Link.Send("{\"t\":\"audio\",\"music\":" + MinecraftMusic.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                      ",\"master\":" + MinecraftVolume.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "}");
        }

        internal static void SendGameMode()
        {
            Link.Send(Messages.GameMode(GameMode.Value == SteveMode.Survival));
            if (GameMode.Value != SteveMode.Creative)
            {
                Flight.Stop("not in Creative");
            }
        }

        private void OnApplicationQuit()
        {
            MinecraftLauncher.OnQuit();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            Link.Stop();
        }
    }
}
