using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using MineHeim.Core;

namespace MineHeim
{
    /// <summary>
    /// Items cross between the worlds by being thrown, and are moved, never copied:
    ///   Valheim -> Minecraft   an item the Viking drops (inventory: drag it out, or the drop key) that has a Minecraft twin
    ///                          (ItemMap) leaves Valheim and lands in Minecraft as that item, where Steve picks it up.
    ///   Minecraft -> Valheim   an item Steve throws (Q) that has a Valheim twin: Minecraft offers it ("itemout"), Valheim
    ///                          asks for the part that crosses ("itemtake"), Minecraft takes it away and confirms
    ///                          ("itemtaken"), and only then does it land in Valheim. An item picked up again in between
    ///                          never crosses, so nothing is doubled.
    /// In Creative, Minecraft's items are free, so by default they do not cross into Valheim (Items.CreativeToValheim).
    /// </summary>
    internal sealed class ItemBridge
    {
        public static ItemMap Map = new ItemMap();
        public int ToMinecraft, ToValheim, Offers;

        /// <summary>The player's config pairs on top of the defaults; unknown Valheim items are reported once a world is up.</summary>
        public static void Configure(string extra)
        {
            var map = new ItemMap();
            var problems = new List<string>();
            map.Apply(extra, problems);
            foreach (string p in problems)
            {
                Plugin.Log.LogWarning("Items.ExtraPairs: cannot read \"" + p + "\" (expected Valheim=minecraft_item or Valheim=minecraft_item*ratio)");
            }

            Map = map;
            _checked = false;
        }

        private static bool _checked;

        private static void CheckNames()
        {
            if (_checked || ObjectDB.instance == null || ObjectDB.instance.m_items.Count == 0)
            {
                return;
            }

            _checked = true;
            var unknown = new List<string>();
            foreach (string name in Map.ValheimItems)
            {
                if (ObjectDB.instance.GetItemPrefab(name) == null)
                {
                    unknown.Add(name);
                }
            }

            Plugin.Log.LogInfo("item map: " + Map.Count + " Valheim items" + (unknown.Count > 0 ? "; not in this Valheim (ignored): " + string.Join(", ", unknown.ToArray()) : ""));
        }

        // ------------------------------------------------------------------ Valheim -> Minecraft

        private static bool _localDropping;
        private static ItemDrop _dropped;

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropItem))]
        private static class HumanoidDropPatch
        {
            private static void Prefix(Humanoid __instance)
            {
                _localDropping = __instance != null && __instance == Player.m_localPlayer;
                _dropped = null;
            }

            private static void Postfix(Humanoid __instance, bool __result)
            {
                ItemDrop drop = _dropped;
                _localDropping = false;
                _dropped = null;
                if (__result && drop != null && __instance == Player.m_localPlayer)
                {
                    Plugin.Items.Cross(drop, (Player)__instance); // after DropItem finished with it
                }
            }
        }

        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.DropItem))]
        private static class ItemDropPatch
        {
            private static void Postfix(ItemDrop __result)
            {
                if (_localDropping)
                {
                    _dropped = __result;
                }
            }
        }

        private void Cross(ItemDrop drop, Player player)
        {
            CheckNames();
            if (!Plugin.ItemsCross.Value || !Link.Connected || drop == null || drop.m_itemData == null || drop.m_itemData.m_dropPrefab == null)
            {
                return;
            }

            string prefab = drop.m_itemData.m_dropPrefab.name;
            int stack = drop.m_itemData.m_stack;
            string mcId;
            int mcCount;
            if (drop.m_itemData.m_quality > 1 || !Map.ToMinecraft(prefab, stack, out mcId, out mcCount))
            {
                return; // no twin: it stays a Valheim item on Valheim's ground
            }

            // Steve catches it where the Viking threw it: a Minecraft item, tossed the same way
            Vector3 at = drop.transform.position;
            Vector3 toss = (player.transform.forward + Vector3.up) * 0.25f; // ~5 m/s, as Valheim throws, in blocks per tick
            double mx, my, mz;
            Mapping.Position(at.x, at.y, at.z, Plugin.YOffset.Value, out mx, out my, out mz);
            Link.Send(Messages.ItemIn(mcId, mcCount, mx, my, mz, toss.x, toss.y, -toss.z));
            ZNetView view = drop.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && ZNetScene.instance != null)
            {
                ZNetScene.instance.Destroy(drop.gameObject);
            }
            else
            {
                Object.Destroy(drop.gameObject);
            }

            ToMinecraft++;
            Plugin.Log.LogInfo(string.Format("item to Minecraft: {0} {1} -> {2} {3}", stack, prefab, mcCount, mcId));
        }

        // ------------------------------------------------------------------ Minecraft -> Valheim

        /// <summary>{"t":"itemout","id":..,"item":..,"n":..,"pos":[..]}: Steve threw this; ask for the part that crosses.</summary>
        public void OnOffer(string json)
        {
            CheckNames();
            Offers++;
            int id = Messages.IntValue(json, "id", -1);
            string item = Messages.StringValue(json, "item", null);
            int n = Messages.IntValue(json, "n", 0);
            string prefab;
            int count, used;
            if (id < 0 || !Plugin.ItemsCross.Value || Player.m_localPlayer == null || !Map.ToValheim(item, n, out prefab, out count, out used))
            {
                return;
            }

            if (Plugin.GameMode.Value == Plugin.SteveMode.Creative && !Plugin.CreativeToValheim.Value)
            {
                if (!_toldCreative)
                {
                    _toldCreative = true;
                    Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, "Creative items stay in Minecraft (Survival lets them cross)");
                }

                return;
            }

            if (ObjectDB.instance == null || ObjectDB.instance.GetItemPrefab(prefab) == null)
            {
                return;
            }

            Link.Send(Messages.ItemTake(id, used));
        }

        private bool _toldCreative;

        /// <summary>{"t":"itemtaken","item":..,"n":..,"pos":[..]}: Minecraft took the items away; they land in Valheim.</summary>
        public void OnTaken(string json)
        {
            string item = Messages.StringValue(json, "item", null);
            int n = Messages.IntValue(json, "n", 0);
            string prefab;
            int count, used;
            double x, y, z;
            GameObject itemPrefab;
            if (!Map.ToValheim(item, n, out prefab, out count, out used) || !Messages.Pos(json, out x, out y, out z) || ObjectDB.instance == null ||
                (itemPrefab = ObjectDB.instance.GetItemPrefab(prefab)) == null)
            {
                Plugin.Log.LogWarning("itemtaken that cannot land in Valheim (lost): " + json);
                return;
            }

            double ux, uy, uz;
            Mapping.FromMinecraft(x, y, z, Plugin.YOffset.Value, out ux, out uy, out uz);
            var at = new Vector3((float)ux, (float)uy + 0.3f, (float)uz);
            int left = count;
            while (left > 0)
            {
                GameObject go = Object.Instantiate(itemPrefab, at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                ItemDrop drop = go.GetComponent<ItemDrop>();
                int stack = Mathf.Min(left, Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize));
                drop.SetStack(stack);
                left -= stack;
                at += Vector3.up * 0.2f;
            }

            ToValheim++;
            Plugin.Log.LogInfo(string.Format("item to Valheim: {0} {1} -> {2} {3}", n, item, count, prefab));
        }
    }
}
