using System;
using System.Collections.Generic;
using System.Globalization;

namespace MineHeim.Core
{
    /// <summary>
    /// Which Valheim item is which Minecraft item, for items that cross between the two worlds. Crossing moves an item,
    /// it never copies one: what arrives on one side was taken away on the other. A pair has a ratio (Minecraft items per
    /// Valheim item), so 4 planks make 1 Wood and only multiples of 4 cross.
    ///
    /// Valheim -> Minecraft uses the pair's Minecraft item. Minecraft -> Valheim also knows aliases (any log is Wood, any
    /// stone-like block is Stone), so a round trip can come back as a different Minecraft item than went in.
    /// </summary>
    public sealed class ItemMap
    {
        private struct Pair
        {
            public string Valheim, Minecraft;
            public int Ratio;
        }

        private readonly Dictionary<string, Pair> _byValheim = new Dictionary<string, Pair>(StringComparer.Ordinal);
        private readonly Dictionary<string, Pair> _byMinecraft = new Dictionary<string, Pair>(StringComparer.Ordinal);

        /// <summary>Valheim prefab = Minecraft item (* Minecraft items per Valheim item). Two-way pairs first, then Minecraft-only aliases.</summary>
        public const string Defaults =
            "Wood=oak_log, FineWood=birch_log, RoundLog=spruce_log, ElderBark=dark_oak_log, Stone=cobblestone, Flint=flint, Resin=resin_clump, " +
            "LeatherScraps=leather, DeerHide=rabbit_hide, Feathers=feather, BoneFragments=bone, Coal=coal, CopperOre=raw_copper, Copper=copper_ingot, " +
            "IronScrap=raw_iron, Iron=iron_ingot, Obsidian=obsidian, Crystal=amethyst_shard, BlackMetalScrap=netherite_scrap, BlackMetal=netherite_ingot, " +
            "Raspberry=sweet_berries, Blueberries=glow_berries, Mushroom=red_mushroom, MushroomYellow=brown_mushroom, Carrot=carrot, Turnip=beetroot, " +
            "Barley=wheat, Honey=honey_bottle, Dandelion=dandelion, RawMeat=porkchop, DeerMeat=beef, CookedMeat=cooked_porkchop, " +
            "CookedDeerMeat=cooked_beef, FishRaw=cod, FishCooked=cooked_cod, Entrails=rotten_flesh, Bread=bread, Coins=gold_nugget, Ruby=emerald, " +
            "Guck=slime_ball, ArrowWood=arrow, SurtlingCore=fire_charge, GreydwarfEye=spider_eye, DragonEgg=dragon_egg, ChickenEgg=egg, LinenThread=string";

        /// <summary>Minecraft items that become a Valheim item but are not what that Valheim item becomes in Minecraft.</summary>
        public const string Aliases =
            "Wood=oak_wood, Wood=acacia_log, Wood=cherry_log, Wood=jungle_log, Wood=mangrove_log, Wood=pale_oak_log, " +
            "Wood=oak_planks*4, Wood=spruce_planks*4, Wood=birch_planks*4, Wood=jungle_planks*4, Wood=acacia_planks*4, Wood=dark_oak_planks*4, " +
            "Wood=mangrove_planks*4, Wood=cherry_planks*4, Wood=pale_oak_planks*4, Wood=bamboo_planks*4, " +
            "Stone=stone, Stone=cobbled_deepslate, Stone=deepslate, Stone=andesite, Stone=diorite, Stone=granite, Stone=tuff, " +
            "Coal=charcoal, Crystal=quartz, FishRaw=salmon, FishCooked=cooked_salmon";

        public ItemMap() : this(Defaults + ", " + Aliases)
        {
        }

        public ItemMap(string pairs)
        {
            Apply(pairs, null);
        }

        public int Count => _byValheim.Count;

        /// <summary>
        /// Add or replace pairs, "Valheim=minecraft_item[*ratio], ..." (a "minecraft:" prefix is optional). The first pair
        /// for a Valheim item sets its way into Minecraft; every pair is a way back. Bad entries go to `problems`.
        /// </summary>
        public void Apply(string pairs, List<string> problems)
        {
            if (string.IsNullOrEmpty(pairs))
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in pairs.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string entry = raw.Trim();
                int eq = entry.IndexOf('=');
                if (eq <= 0 || eq == entry.Length - 1)
                {
                    problems?.Add(entry);
                    continue;
                }

                string valheim = entry.Substring(0, eq).Trim();
                string minecraft = entry.Substring(eq + 1).Trim();
                int ratio = 1;
                int star = minecraft.IndexOf('*');
                if (star >= 0)
                {
                    if (!int.TryParse(minecraft.Substring(star + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ratio) || ratio < 1)
                    {
                        problems?.Add(entry);
                        continue;
                    }

                    minecraft = minecraft.Substring(0, star).Trim();
                }

                minecraft = Normalize(minecraft);
                var pair = new Pair { Valheim = valheim, Minecraft = minecraft, Ratio = ratio };
                if (seen.Add(valheim))
                {
                    _byValheim[valheim] = pair; // a later list (the player's config) replaces the earlier pair
                }

                _byMinecraft[minecraft] = pair;
            }
        }

        /// <summary>"oak_log" and "minecraft:oak_log" are the same item.</summary>
        public static string Normalize(string minecraftId)
        {
            string id = minecraftId.Trim().ToLowerInvariant();
            return id.IndexOf(':') >= 0 ? id : "minecraft:" + id;
        }

        /// <summary>Valheim's `count` of `valheimPrefab` as Minecraft items (count x ratio), if the item has a Minecraft twin.</summary>
        public bool ToMinecraft(string valheimPrefab, int count, out string minecraftId, out int minecraftCount)
        {
            Pair p;
            if (count > 0 && valheimPrefab != null && _byValheim.TryGetValue(valheimPrefab, out p))
            {
                minecraftId = p.Minecraft;
                minecraftCount = count * p.Ratio;
                return true;
            }

            minecraftId = null;
            minecraftCount = 0;
            return false;
        }

        /// <summary>
        /// A stack of `minecraftCount` Minecraft items as Valheim items: `used` of them cross (whole multiples of the ratio),
        /// the rest stay in Minecraft. False if nothing crosses.
        /// </summary>
        public bool ToValheim(string minecraftId, int minecraftCount, out string valheimPrefab, out int valheimCount, out int used)
        {
            Pair p;
            valheimPrefab = null;
            valheimCount = used = 0;
            if (minecraftCount <= 0 || minecraftId == null || !_byMinecraft.TryGetValue(Normalize(minecraftId), out p))
            {
                return false;
            }

            valheimCount = minecraftCount / p.Ratio;
            if (valheimCount == 0)
            {
                return false;
            }

            valheimPrefab = p.Valheim;
            used = valheimCount * p.Ratio;
            return true;
        }

        public IEnumerable<string> ValheimItems => _byValheim.Keys;
    }
}
