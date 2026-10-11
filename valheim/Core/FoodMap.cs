using System;
using System.Collections.Generic;

namespace MineHeim.Core
{
    /// <summary>
    /// Valheim's food is the one that keeps the Viking alive, so what Steve eats in Minecraft is eaten as a Valheim food of
    /// the same standing: cooked and prepared foods as their Valheim twins, golden ones as the best meals. Raw meat, rotten
    /// flesh and poisonous things feed nothing (Valheim does not let you eat raw meat either).
    /// Each Minecraft food lists Valheim prefabs, best first; the first one the game has is eaten.
    /// </summary>
    public static class FoodMap
    {
        private static readonly Dictionary<string, string[]> Foods = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // Meadows
            { "sweet_berries", new[] { "Raspberry" } },
            { "apple", new[] { "Raspberry" } },
            { "melon_slice", new[] { "Raspberry" } },
            { "carrot", new[] { "Raspberry" } },
            { "potato", new[] { "Raspberry" } },
            { "beetroot", new[] { "Raspberry" } },
            { "dried_kelp", new[] { "Raspberry" } },
            { "cookie", new[] { "Honey", "Raspberry" } },
            { "honey_bottle", new[] { "Honey" } },
            { "red_mushroom", new[] { "Mushroom" } },
            { "brown_mushroom", new[] { "MushroomYellow", "Mushroom" } },
            { "cooked_porkchop", new[] { "CookedMeat" } },
            { "cooked_beef", new[] { "CookedDeerMeat", "CookedMeat" } },
            { "cooked_cod", new[] { "FishCooked", "CookedMeat" } },
            { "cooked_salmon", new[] { "FishCooked", "CookedMeat" } },
            { "cooked_mutton", new[] { "NeckTailGrilled", "CookedMeat" } },
            // Black Forest
            { "glow_berries", new[] { "Blueberries", "Raspberry" } },
            { "chorus_fruit", new[] { "Blueberries", "Raspberry" } },
            { "mushroom_stew", new[] { "CarrotSoup", "Mushroom" } },
            { "rabbit_stew", new[] { "DeerStew", "CookedDeerMeat" } },
            { "cooked_rabbit", new[] { "BoarJerky", "CookedMeat" } },
            { "baked_potato", new[] { "QueensJam", "Raspberry" } },
            // Swamp
            { "beetroot_soup", new[] { "TurnipStew", "CarrotSoup" } },
            { "suspicious_stew", new[] { "Sausages", "TurnipStew", "CarrotSoup" } },
            // Plains
            { "bread", new[] { "Bread", "TurnipStew" } },
            { "pumpkin_pie", new[] { "LoxPie", "Bread", "TurnipStew" } },
            { "cooked_chicken", new[] { "CookedChickenMeat", "WolfMeatSkewer", "CookedMeat" } },
            // the best of Minecraft: the best of Valheim
            { "golden_carrot", new[] { "MeatPlatter", "YggdrasilPorridge", "MisthareSupreme", "LoxPie" } },
            { "golden_apple", new[] { "MisthareSupreme", "HoneyGlazedChicken", "MeatPlatter", "LoxPie" } },
            { "enchanted_golden_apple", new[] { "ScorchingMedley", "SparklingShroomshake", "MisthareSupreme", "HoneyGlazedChicken", "LoxPie" } },
        };

        /// <summary>The Valheim foods a Minecraft food can be, best first (empty: it feeds nothing).</summary>
        public static string[] Candidates(string item)
        {
            string id = item ?? "";
            if (id.StartsWith("minecraft:", StringComparison.Ordinal))
            {
                id = id.Substring("minecraft:".Length);
            }

            string[] list;
            return Foods.TryGetValue(id, out list) ? list : new string[0];
        }

        /// <summary>The first of a Minecraft food's Valheim foods the game has (`exists`), or "".</summary>
        public static string Resolve(string item, Func<string, bool> exists)
        {
            foreach (string prefab in Candidates(item))
            {
                if (exists(prefab))
                {
                    return prefab;
                }
            }

            return "";
        }
    }
}
