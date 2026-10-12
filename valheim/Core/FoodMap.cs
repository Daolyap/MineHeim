using System;
using System.Collections.Generic;

namespace MineHeim.Core
{
    /// <summary>
    /// Valheim's food is the one that keeps the Viking alive, so what Steve eats in Minecraft is eaten as a Valheim food of
    /// the same standing: cooked and prepared foods as their Valheim twins, golden ones as the best meals. Each Minecraft food
    /// lists Valheim prefabs, best first; the first one the game has is eaten. Any other food (raw meat, rotten flesh, a
    /// food from a newer Minecraft) is its closest relative by how much it feeds in Minecraft (FoodMap.ByNutrition).
    /// </summary>
    public static class FoodMap
    {
        private static readonly Dictionary<string, string[]> Foods = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // poisonous in Minecraft: Valheim's own berries that make you throw up
            { "spider_eye", new[] { "Pukeberries", "Raspberry" } },
            { "poisonous_potato", new[] { "Pukeberries", "Raspberry" } },
            { "pufferfish", new[] { "Pukeberries", "Raspberry" } },
            { "rotten_flesh", new[] { "Pukeberries", "NeckTailGrilled" } },
            // Meadows
            { "sweet_berries", new[] { "Raspberry" } },
            { "apple", new[] { "Raspberry" } },
            { "melon_slice", new[] { "Raspberry" } },
            { "carrot", new[] { "Carrot", "Raspberry" } },
            { "potato", new[] { "Onion", "Raspberry" } },
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

        /// <summary>
        /// Valheim foods for a Minecraft food that has no twin above, by its nutrition (Minecraft's hunger points): a berry for
        /// a snack (raw chicken 2), plain grilled meat for raw meat and rotten flesh (3-4), cooked meat for a meal (5-8), a
        /// stew or bread past that. Best first; empty for nothing (0).
        /// </summary>
        public static string[] ByNutrition(int nutrition)
        {
            if (nutrition <= 0)
            {
                return new string[0];
            }

            if (nutrition <= 2)
            {
                return new[] { "Raspberry", "Blueberries", "Mushroom" };
            }

            if (nutrition <= 4)
            {
                return new[] { "NeckTailGrilled", "CookedMeat", "Raspberry" };
            }

            if (nutrition <= 6)
            {
                return new[] { "CookedMeat", "NeckTailGrilled", "Raspberry" };
            }

            if (nutrition <= 8)
            {
                return new[] { "CookedDeerMeat", "CookedMeat", "Raspberry" };
            }

            return new[] { "TurnipStew", "Bread", "CookedDeerMeat", "CookedMeat" };
        }

        /// <summary>The first of a Minecraft food's Valheim foods the game has (`exists`), or "".</summary>
        public static string Resolve(string item, Func<string, bool> exists)
        {
            return Resolve(item, 0, exists);
        }

        /// <summary>
        /// The first of a Minecraft food's Valheim foods the game has (`exists`); a food with no twin is its closest relative
        /// by `nutrition`; "" if neither gives one.
        /// </summary>
        public static string Resolve(string item, int nutrition, Func<string, bool> exists)
        {
            string[] twins = Candidates(item);
            foreach (string prefab in twins.Length > 0 ? twins : ByNutrition(nutrition))
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
