using System;
using System.Collections.Generic;
using System.Globalization;

namespace MineHeim.Core
{
    /// <summary>What kind of Valheim weapon a Minecraft item fights as.</summary>
    public enum WeaponKind
    {
        Unarmed,
        Sword,
        Axe,
        Pickaxe,
        Mace,
        Spear,
        Bow,
        Crossbow,
    }

    /// <summary>A Minecraft item as Steve swings or shoots it in Valheim: the kind and the Valheim tier (biome) it borrows its stats from.</summary>
    public struct SteveWeapon
    {
        public WeaponKind Kind;
        /// <summary>0 Meadows, 1 Black Forest, 2 Swamp, 3 Mountains, 4 Plains, 5 Mistlands, 6 Ashlands.</summary>
        public int Tier;
        /// <summary>The Valheim weapon's prefab (empty for Unarmed: the Viking's own fists).</summary>
        public string Prefab;
        /// <summary>The Valheim ammunition a shot adds to the bow's damage (bows and crossbows only).</summary>
        public string Ammo;
    }

    /// <summary>
    /// Steve's weapons borrow a real Valheim weapon's stats: a Minecraft sword is a Valheim sword of its material's tier, and
    /// Valheim's own numbers (damage types, stamina, stagger, knockback, backstab, tool tier, skills) apply as if the Viking
    /// swung that weapon. Minecraft's materials follow the pairs Valheim's metals cross as (Copper = copper ingot, Iron = iron
    /// ingot, Silver = diamond, BlackMetal = netherite ingot); items without a material (mace, trident, bow, crossbow) and
    /// netherite, Minecraft's best, keep up with the bosses the world has beaten.
    /// </summary>
    public static class WeaponMap
    {
        public const int MaxTier = 6;

        public static readonly string[] Biomes = { "Meadows", "Black Forest", "Swamp", "Mountains", "Plains", "Mistlands", "Ashlands" };

        /// <summary>The boss whose defeat opens each tier (index = tier; Meadows needs none).</summary>
        public static readonly string[] BossKeys = { "", "defeated_eikthyr", "defeated_gdking", "defeated_bonemass", "defeated_dragon", "defeated_goblinking", "defeated_queen" };

        /// <summary>Per kind, the Valheim prefab for each tier (Meadows first). A prefab missing from the game falls back to the tier below.</summary>
        private static readonly Dictionary<WeaponKind, string[]> Ladders = new Dictionary<WeaponKind, string[]>
        {
            { WeaponKind.Sword, new[] { "KnifeFlint", "SwordBronze", "SwordIron", "SwordSilver", "SwordBlackmetal", "SwordMistwalker", "SwordNiedhogg" } },
            { WeaponKind.Axe, new[] { "AxeFlint", "AxeBronze", "AxeIron", "AxeIron", "AxeBlackMetal", "AxeJotunBane", "AxeBerzerkr" } },
            { WeaponKind.Pickaxe, new[] { "PickaxeAntler", "PickaxeBronze", "PickaxeIron", "PickaxeIron", "PickaxeBlackMetal", "PickaxeBlackMetal", "PickaxeBlackMetal" } },
            { WeaponKind.Mace, new[] { "Club", "MaceBronze", "MaceIron", "MaceSilver", "MaceNeedle", "MaceEldner", "MaceEldner" } },
            { WeaponKind.Spear, new[] { "SpearFlint", "SpearBronze", "SpearElderbark", "SpearWolfFang", "SpearWolfFang", "SpearCarapace", "SpearSplitner" } },
            { WeaponKind.Bow, new[] { "Bow", "BowFineWood", "BowHuntsman", "BowHuntsman", "BowDraugrFang", "BowSpineSnap", "BowAshlands" } },
            // Valheim has crossbows only from the Mistlands on: before that a crossbow shoots as the bow of its tier
            { WeaponKind.Crossbow, new[] { "Bow", "BowFineWood", "BowHuntsman", "BowHuntsman", "BowDraugrFang", "CrossbowArbalest", "CrossbowRipper" } },
        };

        private static readonly string[] Arrows = { "ArrowWood", "ArrowFlint", "ArrowBronze", "ArrowIron", "ArrowNeedle", "ArrowCarapace", "ArrowCharred" };
        private static readonly string[] Bolts = { "ArrowWood", "ArrowFlint", "ArrowBronze", "ArrowIron", "ArrowNeedle", "BoltCarapace", "BoltCharred" };

        /// <summary>Material prefix -> tier (netherite: at least Plains, more with the world).</summary>
        private static readonly KeyValuePair<string, int>[] Materials =
        {
            new KeyValuePair<string, int>("wooden_", 0),
            new KeyValuePair<string, int>("golden_", 0),
            new KeyValuePair<string, int>("stone_", 0),
            new KeyValuePair<string, int>("copper_", 1),
            new KeyValuePair<string, int>("iron_", 2),
            new KeyValuePair<string, int>("diamond_", 3),
            new KeyValuePair<string, int>("netherite_", 4),
        };

        /// <summary>The highest tier the world has opened: the last boss beaten in Valheim's order.</summary>
        public static int WorldTier(Func<string, bool> hasGlobalKey)
        {
            int tier = 0;
            for (int t = 1; t <= MaxTier; t++)
            {
                if (hasGlobalKey(BossKeys[t]))
                {
                    tier = t;
                }
            }

            return tier;
        }

        public static string[] Ladder(WeaponKind kind)
        {
            string[] ladder;
            return Ladders.TryGetValue(kind, out ladder) ? ladder : null;
        }

        /// <summary>A Minecraft item id ("diamond_sword", "minecraft:mace") as one of Steve's weapons in a world at `worldTier`.</summary>
        public static SteveWeapon Resolve(string item, int worldTier)
        {
            string id = item ?? "";
            if (id.StartsWith("minecraft:", StringComparison.Ordinal))
            {
                id = id.Substring("minecraft:".Length);
            }

            worldTier = Math.Max(0, Math.Min(MaxTier, worldTier));
            WeaponKind kind;
            int tier;
            if (id == "mace")
            {
                kind = WeaponKind.Mace;
                tier = worldTier;
            }
            else if (id == "trident")
            {
                kind = WeaponKind.Spear;
                tier = worldTier;
            }
            else if (id == "bow")
            {
                kind = WeaponKind.Bow;
                tier = worldTier;
            }
            else if (id == "crossbow")
            {
                kind = WeaponKind.Crossbow;
                tier = worldTier;
            }
            else
            {
                kind = id.EndsWith("_sword", StringComparison.Ordinal) ? WeaponKind.Sword
                    : id.EndsWith("_pickaxe", StringComparison.Ordinal) ? WeaponKind.Pickaxe
                    : id.EndsWith("_axe", StringComparison.Ordinal) ? WeaponKind.Axe
                    : id.EndsWith("_spear", StringComparison.Ordinal) ? WeaponKind.Spear
                    : WeaponKind.Unarmed; // a fist, a block, a shovel, a hoe, a stick: the Viking's own fists
                tier = -1;
                foreach (KeyValuePair<string, int> m in Materials)
                {
                    if (id.StartsWith(m.Key, StringComparison.Ordinal))
                    {
                        tier = m.Key == "netherite_" ? Math.Max(m.Value, worldTier) : m.Value;
                        break;
                    }
                }

                if (tier < 0)
                {
                    kind = WeaponKind.Unarmed;
                    tier = 0;
                }
            }

            var w = new SteveWeapon { Kind = kind, Tier = kind == WeaponKind.Unarmed ? 0 : tier, Prefab = "", Ammo = "" };
            string[] ladder = Ladder(kind);
            if (ladder != null)
            {
                w.Prefab = ladder[w.Tier];
            }

            if (kind == WeaponKind.Bow)
            {
                w.Ammo = Arrows[w.Tier];
            }
            else if (kind == WeaponKind.Crossbow)
            {
                w.Ammo = Bolts[w.Tier];
            }

            return w;
        }

        /// <summary>
        /// The prefab to use when `wanted` is missing from the game: the same kind's ladder walked down from `tier`; `exists`
        /// says what the game has. Empty if nothing on the ladder exists.
        /// </summary>
        public static string Available(WeaponKind kind, int tier, Func<string, bool> exists)
        {
            string[] ladder = Ladder(kind);
            if (ladder == null)
            {
                return "";
            }

            for (int t = Math.Min(tier, ladder.Length - 1); t >= 0; t--)
            {
                if (exists(ladder[t]))
                {
                    return ladder[t];
                }
            }

            return "";
        }

        public static string AvailableAmmo(WeaponKind kind, int tier, Func<string, bool> exists)
        {
            string[] ladder = kind == WeaponKind.Crossbow ? Bolts : kind == WeaponKind.Bow ? Arrows : null;
            if (ladder == null)
            {
                return "";
            }

            for (int t = Math.Min(tier, ladder.Length - 1); t >= 0; t--)
            {
                if (exists(ladder[t]))
                {
                    return ladder[t];
                }
            }

            return "";
        }

        // ------------------------------------------------------------------ Valheim's attack maths, with Minecraft's recharge

        /// <summary>Minecraft's own scaling of a swing by how far it had recharged (0-1): a spammed swing is a fifth of a full one.</summary>
        public static double Recharge(double charge)
        {
            charge = Math.Max(0.0, Math.Min(1.0, charge));
            return 0.2 + 0.8 * charge * charge;
        }

        /// <summary>A swing with too little stamina for it still lands, this weakly.</summary>
        public const double TiredFactor = 0.2;

        /// <summary>Valheim's stamina for one swing: the attack's stamina, less a third of it at full skill.</summary>
        public static double StaminaCost(double attackStamina, double skillFactor)
        {
            return Math.Max(0.0, attackStamina * (1.0 - 0.33 * Math.Max(0.0, Math.Min(1.0, skillFactor))));
        }

        /// <summary>Valheim's share for each target when one swing hits several (`lowerDamagePerHit`): 1 / (targets x 0.75).</summary>
        public static double Split(int targets, bool lowerDamagePerHit)
        {
            return lowerDamagePerHit && targets > 1 ? 1.0 / (targets * 0.75) : 1.0;
        }

        /// <summary>A Minecraft arrow's damage against a full-draw plain arrow (6): how hard the bow was drawn, Power included.</summary>
        public static double Draw(double minecraftArrowDamage)
        {
            return minecraftArrowDamage < 0 ? 1.0 : Math.Max(0.1, Math.Min(1.5, minecraftArrowDamage / 6.0));
        }

        /// <summary>
        /// Enchantments as Valheim upgrades: Sharpness, Power, Impaling, Density and Breach raise the borrowed weapon's quality
        /// (levels 1-2: 2, 3-4: 3, 5: 4), capped at what the weapon allows.
        /// </summary>
        public static int Quality(Enchantments e, int maxQuality)
        {
            int level = Math.Max(Math.Max(e.Get("sharpness"), e.Get("power")), Math.Max(e.Get("impaling"), Math.Max(e.Get("density"), e.Get("breach"))));
            int q = level <= 0 ? 1 : 1 + (level + 1) / 2;
            return Math.Max(1, Math.Min(Math.Max(1, maxQuality), q));
        }

        /// <summary>Elemental damage an enchantment adds, as a share of the hit's physical damage: a fifth per level.</summary>
        public static double ElementShare(int level)
        {
            return 0.2 * Math.Max(0, level);
        }

        /// <summary>Knockback and Punch: push this many times harder.</summary>
        public static double PushFactor(Enchantments e)
        {
            return 1.0 + 0.5 * Math.Max(e.Get("knockback"), e.Get("punch"));
        }
    }

    /// <summary>An item's enchantments as Minecraft sends them: "sharpness:5,fire_aspect:2" (a "minecraft:" prefix is optional).</summary>
    public sealed class Enchantments
    {
        private readonly Dictionary<string, int> _levels = new Dictionary<string, int>(StringComparer.Ordinal);

        public static readonly Enchantments None = new Enchantments("");

        public Enchantments(string list)
        {
            foreach (string part in (list ?? "").Split(','))
            {
                string p = part.Trim();
                int colon = p.LastIndexOf(':');
                int level;
                if (colon <= 0 || !int.TryParse(p.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out level))
                {
                    continue;
                }

                string name = p.Substring(0, colon);
                if (name.StartsWith("minecraft:", StringComparison.Ordinal))
                {
                    name = name.Substring("minecraft:".Length);
                }

                _levels[name] = level;
            }
        }

        public int Get(string name)
        {
            int level;
            return _levels.TryGetValue(name, out level) ? level : 0;
        }

        public bool Any => _levels.Count > 0;

        /// <summary>A stable key for caching what these enchantments make of a weapon.</summary>
        public string Key
        {
            get
            {
                var names = new List<string>(_levels.Keys);
                names.Sort(StringComparer.Ordinal);
                var b = new System.Text.StringBuilder();
                foreach (string n in names)
                {
                    b.Append(n).Append(':').Append(_levels[n].ToString(CultureInfo.InvariantCulture)).Append(',');
                }

                return b.ToString();
            }
        }
    }
}
