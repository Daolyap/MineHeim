using System.Collections.Generic;
using System.Text;
using MineHeim.Core;
using UnityEngine;

namespace MineHeim
{
    /// <summary>
    /// Steve's Minecraft weapons as real Valheim weapons (Core.WeaponMap): the borrowed weapon is read from the game's own
    /// ObjectDB, so vanilla balance and future patches carry over, and its hit is filled in the way Valheim's Attack fills
    /// one: the weapon's damage at its quality, times the Viking's random skill factor and the attack's multiplier, with its
    /// knockback, stagger, backstab bonus, tool tier and skill. Minecraft's recharge stays on top as an extra multiplier.
    /// </summary>
    internal static class SteveWeapons
    {
        internal sealed class Borrowed
        {
            public SteveWeapon Weapon;
            /// <summary>The prefab the game had (the ladder steps down when one is missing), or "unarmed".</summary>
            public string Prefab;
            public ItemDrop.ItemData Item;
            /// <summary>The arrow or bolt a shot adds (bows and crossbows), or null.</summary>
            public ItemDrop.ItemData Ammo;
            public Enchantments Enchantments;
            /// <summary>Tool tiers below the borrowed item's (WeaponMap.ToolTierPenalty: wood and gold).</summary>
            public int TierPenalty;

            public Skills.SkillType Skill => Item.m_shared.m_skillType;
            public Attack Attack => Item.m_shared.m_attack;
        }

        private static readonly Dictionary<string, Borrowed> Cache = new Dictionary<string, Borrowed>();
        private static int _cachedTier = -1;
        private static ObjectDB _cachedDb;
        private static Player _cachedPlayer;

        /// <summary>The highest tier the world has opened (the last boss beaten).</summary>
        public static int WorldTier()
        {
            ZoneSystem zones = ZoneSystem.instance;
            return zones == null ? 0 : WeaponMap.WorldTier(key => zones.GetGlobalKey(key));
        }

        /// <summary>The Valheim weapon a Minecraft item fights as, or null (off in the config, or the game is not ready: use the old way).</summary>
        public static Borrowed For(string item, string enchantments)
        {
            Player player = Player.m_localPlayer;
            if (!Plugin.SteveWeaponStats.Value || ObjectDB.instance == null || player == null)
            {
                return null;
            }

            int tier = WorldTier();
            if (tier != _cachedTier || ObjectDB.instance != _cachedDb || player != _cachedPlayer)
            {
                Cache.Clear();
                _cachedTier = tier;
                _cachedDb = ObjectDB.instance;
                _cachedPlayer = player;
                LogTable(tier);
            }

            var ench = new Enchantments(enchantments);
            string key = item + "|" + ench.Key;
            Borrowed found;
            if (Cache.TryGetValue(key, out found))
            {
                return found;
            }

            found = Borrow(player, WeaponMap.Resolve(item, tier), ench);
            if (found != null)
            {
                found.TierPenalty = WeaponMap.ToolTierPenalty(item);
            }

            Cache[key] = found;
            return found;
        }

        private static Borrowed Borrow(Player player, SteveWeapon w, Enchantments ench)
        {
            string prefab;
            ItemDrop.ItemData source;
            if (w.Kind == WeaponKind.Unarmed)
            {
                prefab = "unarmed";
                source = player.m_unarmedWeapon != null ? player.m_unarmedWeapon.m_itemData : null;
            }
            else
            {
                prefab = WeaponMap.Available(w.Kind, w.Tier, Exists);
                source = ItemOf(prefab);
            }

            if (source == null || source.m_shared == null)
            {
                return null;
            }

            ItemDrop.ItemData item = source.Clone();
            item.m_quality = WeaponMap.Quality(ench, source.m_shared.m_maxQuality);
            item.m_worldLevel = Game.m_worldLevel;
            ItemDrop.ItemData ammo = null;
            string ammoPrefab = WeaponMap.AvailableAmmo(w.Kind, w.Tier, Exists);
            if (ammoPrefab.Length > 0)
            {
                ammo = ItemOf(ammoPrefab)?.Clone();
                if (ammo != null)
                {
                    ammo.m_worldLevel = Game.m_worldLevel;
                }
            }

            return new Borrowed { Weapon = w, Prefab = prefab, Item = item, Ammo = ammo, Enchantments = ench };
        }

        private static bool Exists(string prefab) => ItemOf(prefab) != null;

        private static ItemDrop.ItemData ItemOf(string prefab)
        {
            if (string.IsNullOrEmpty(prefab) || ObjectDB.instance == null)
            {
                return null;
            }

            GameObject go = ObjectDB.instance.GetItemPrefab(prefab);
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            return drop != null ? drop.m_itemData : null;
        }

        /// <summary>The Viking's stamina for one swing of this weapon (Valheim's own: less with skill).</summary>
        public static float StaminaCost(Player player, Borrowed b)
        {
            Attack attack = b.Attack;
            return attack == null ? 0f : (float)WeaponMap.StaminaCost(attack.m_attackStamina, player.GetSkillFactor(b.Skill));
        }

        /// <summary>
        /// Fill `hit` as Valheim's Attack would for this weapon, times `factor` (Minecraft's recharge or draw, a tired swing,
        /// the split between several targets, Events.WeaponDamageScale). `creature`: chop and pickaxe damage are left out.
        /// </summary>
        public static void Fill(HitData hit, Borrowed b, Player player, double factor, bool creature, bool burning = false)
        {
            HitData.DamageTypes d = b.Item.GetDamage();
            if (b.Ammo != null)
            {
                d.Add(b.Ammo.GetDamage(), 1);
            }

            Attack attack = b.Attack;
            d.Modify((float)factor * player.GetRandomSkillFactor(b.Skill) * (attack != null ? attack.m_damageMultiplier : 1f));
            float physical = d.m_blunt + d.m_slash + d.m_pierce;
            Enchantments e = b.Enchantments;
            d.m_fire += physical * (float)WeaponMap.ElementShare(System.Math.Max(e.Get("fire_aspect"), System.Math.Max(e.Get("flame"), burning ? 1 : 0)));
            d.m_spirit += physical * (float)WeaponMap.ElementShare(e.Get("smite"));
            d.m_poison += physical * (float)WeaponMap.ElementShare(e.Get("bane_of_arthropods"));
            if (creature)
            {
                d.m_chop = 0f;
                d.m_pickaxe = 0f;
            }

            ItemDrop.ItemData.SharedData shared = b.Item.m_shared;
            hit.m_damage = d;
            hit.m_pushForce = shared.m_attackForce * (attack != null ? attack.m_forceMultiplier : 1f) * (float)WeaponMap.PushFactor(e);
            hit.m_staggerMultiplier = attack != null ? attack.m_staggerMultiplier : 1f;
            hit.m_backstabBonus = shared.m_backstabBonus;
            hit.m_toolTier = (short)System.Math.Max(0, shared.m_toolTier - b.TierPenalty);
            hit.m_skill = shared.m_skillType;
            hit.m_skillLevel = player.GetSkillLevel(shared.m_skillType);
            hit.m_itemLevel = (short)b.Item.m_quality;
            hit.m_itemWorldLevel = (byte)Game.m_worldLevel;
            hit.m_blockable = shared.m_blockable;
            hit.m_dodgeable = shared.m_dodgeable;
            hit.m_attacker = player.GetZDOID();
        }

        /// <summary>
        /// One swing may hit everything in its arc, or only the nearest target. As in Minecraft only a sword sweeps (where its
        /// Valheim twin does too); an axe, a club or a pickaxe hits the one creature it is swung at.
        /// </summary>
        public static bool MultiHit(Borrowed b) => b.Weapon.Kind == WeaponKind.Sword && b.Attack != null && b.Attack.m_multiHit;

        /// <summary>Half the arc of the weapon's attack, degrees (Minecraft's own reach stays the reach).</summary>
        public static float HalfArc(Borrowed b) => b.Attack != null && b.Attack.m_attackAngle > 1f ? Mathf.Min(b.Attack.m_attackAngle, 180f) * 0.5f : 30f;

        /// <summary>BepInEx/LogOutput.log: what each Minecraft weapon is in this world (which prefabs the game had).</summary>
        private static void LogTable(int tier)
        {
            var b = new StringBuilder("steve weapons (world at " + WeaponMap.Biomes[tier] + "):");
            foreach (string item in new[] { "wooden_sword", "copper_sword", "iron_sword", "diamond_sword", "netherite_sword", "iron_axe", "diamond_pickaxe", "mace", "trident", "bow", "crossbow" })
            {
                SteveWeapon w = WeaponMap.Resolve(item, tier);
                string prefab = WeaponMap.Available(w.Kind, w.Tier, Exists);
                b.Append(' ').Append(item).Append('=').Append(prefab.Length > 0 ? prefab : "(none)");
                if (prefab != w.Prefab)
                {
                    b.Append("[wanted ").Append(w.Prefab).Append(']');
                }
            }

            Plugin.Log.LogInfo(b.ToString());
        }
    }
}
