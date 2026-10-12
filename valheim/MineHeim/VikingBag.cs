using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using MineHeim.Core;
using UnityEngine;

namespace MineHeim
{
    /// <summary>
    /// The Viking's bag in Minecraft's inventory screen (Minecraft's ValheimBag draws it): the bag goes to Minecraft whenever
    /// it changes ({"t":"vinv"}), each item's icon once ({"t":"vicon"}), and Minecraft asks for an item to be moved across
    /// ({"t":"vcross","x":..,"y":..}: dropped the way the Viking drops it, so it crosses as its twin).
    /// </summary>
    internal static class VikingBag
    {
        private static readonly MethodInfo EncodeToPng = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")
            ?.GetMethod("EncodeToPNG", new[] { typeof(Texture2D) });

        private static readonly HashSet<string> IconsSent = new HashSet<string>();
        private static string _sent = "";
        private static float _next;

        /// <summary>A new connection or world: send everything again.</summary>
        internal static void Reset()
        {
            IconsSent.Clear();
            _sent = "";
        }

        /// <summary>Every frame (cheap: looks twice a second).</summary>
        internal static void Tick(Player player)
        {
            if (player == null || !Link.Connected || Time.unscaledTime < _next)
            {
                return;
            }

            _next = Time.unscaledTime + 0.5f;
            Inventory inv = player.GetInventory();
            var b = new StringBuilder("{\"t\":\"vinv\",\"cols\":").Append(inv.GetWidth()).Append(",\"rows\":").Append(inv.GetHeight()).Append(",\"items\":[");
            bool first = true;
            int icons = 0;
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
            {
                string prefab = item.m_dropPrefab != null ? item.m_dropPrefab.name : "";
                if (prefab.Length == 0)
                {
                    continue;
                }

                string twin, unused;
                int count;
                twin = item.m_quality <= 1 && ItemBridge.Map.ToMinecraft(prefab, item.m_stack, out unused, out count) ? unused : "";
                b.Append(first ? "" : ",").Append('[').Append(item.m_gridPos.x).Append(',').Append(item.m_gridPos.y).Append(",\"").Append(Json(prefab)).Append("\",\"")
                    .Append(Json(Name(item))).Append("\",").Append(item.m_stack).Append(',').Append(item.m_quality).Append(',').Append(item.m_equipped ? 1 : 0)
                    .Append(",\"").Append(Json(twin)).Append("\"]");
                first = false;
                if (!IconsSent.Contains(prefab) && icons < 4)
                {
                    icons++; // a few a tick: a full bag's icons arrive within a few seconds without a hitch
                    SendIcon(prefab, item);
                }
            }

            string message = b.Append("]}").ToString();
            if (message != _sent)
            {
                _sent = message;
                Link.Send(message);
            }
        }

        /// <summary>{"t":"vcross","x":..,"y":..}: Minecraft wants this item: it leaves the Viking's bag and goes straight into
        /// Steve's inventory as its twin (no throw: nothing to pick up, nothing lost on the ground).</summary>
        internal static void OnCross(string json)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }

            ItemDrop.ItemData item = player.GetInventory().GetItemAt(Messages.IntValue(json, "x", -1), Messages.IntValue(json, "y", -1));
            if (item == null || item.m_equipped)
            {
                return;
            }

            string mcId;
            int mcCount;
            if (item.m_dropPrefab == null || item.m_quality > 1 || !ItemBridge.Map.ToMinecraft(item.m_dropPrefab.name, item.m_stack, out mcId, out mcCount) || mcCount <= 0)
            {
                player.Message(MessageHud.MessageType.TopLeft, Name(item) + " has no Minecraft twin");
                return;
            }

            string prefab = item.m_dropPrefab.name;
            int stack = item.m_stack;
            player.GetInventory().RemoveItem(item);
            Link.Send(Messages.Command("give @a " + ItemMap.Normalize(mcId) + " " + mcCount));
            Plugin.Log.LogInfo(string.Format("bag to Minecraft: {0} {1} -> {2} {3}", stack, prefab, mcCount, mcId));
            _next = 0f;
        }

        private static string Name(ItemDrop.ItemData item)
        {
            string token = item.m_shared.m_name;
            return Localization.instance != null ? Localization.instance.Localize(token) : token;
        }

        /// <summary>The item's icon (from its sprite, wherever that sits in an atlas) as a 64x64 PNG.</summary>
        private static void SendIcon(string prefab, ItemDrop.ItemData item)
        {
            IconsSent.Add(prefab);
            Sprite sprite = item.GetIcon();
            if (sprite == null || sprite.texture == null || EncodeToPng == null)
            {
                return;
            }

            const int size = 64;
            RenderTexture rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            RenderTexture before = RenderTexture.active;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try
            {
                Rect r = sprite.textureRect;
                Vector2 full = new Vector2(sprite.texture.width, sprite.texture.height);
                Graphics.Blit(sprite.texture, rt, new Vector2(r.width / full.x, r.height / full.y), new Vector2(r.x / full.x, r.y / full.y));
                RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                texture.Apply(false);
                byte[] png = (byte[])EncodeToPng.Invoke(null, new object[] { texture });
                Link.Send("{\"t\":\"vicon\",\"prefab\":\"" + Json(prefab) + "\",\"png\":\"" + Convert.ToBase64String(png) + "\"}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("bag icon for " + prefab + ": " + e.Message);
            }
            finally
            {
                RenderTexture.active = before;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(texture);
            }
        }

        private static string Json(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// A Valheim item's stand-in on Steve's Minecraft hotbar is selected ({"t":"selected","valheim":prefab}): the Viking
    /// equips the real item (or, for food and meads, has it at hand) and Valheim gets the mouse (InputBridge): the hammer and
    /// the hoe build, a Valheim weapon attacks, the right button eats or drinks. Selecting anything else puts away what was
    /// equipped this way.
    /// </summary>
    internal static class StandIns
    {
        /// <summary>The prefab of the selected stand-in ("" = a Minecraft slot).</summary>
        internal static string Prefab = "";
        private static ItemDrop.ItemData _equippedByUs;

        private static int _holdingFrame = -1;
        private static bool _holding;

        /// <summary>Valheim has Steve's mouse: Minecraft mode, a stand-in selected, and the Viking still has that item (cached per frame).</summary>
        internal static bool Holding
        {
            get
            {
                if (_holdingFrame != Time.frameCount)
                {
                    _holdingFrame = Time.frameCount;
                    _holding = InputBridge.McMode && Prefab.Length > 0 && Item() != null;
                }

                return _holding;
            }
        }

        internal static void Select(string prefab)
        {
            Prefab = prefab ?? "";
            _holdingFrame = -1;
            Apply();
        }

        /// <summary>Equip what the selection asks for (also when Minecraft mode comes back on).</summary>
        internal static void Apply()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }

            ItemDrop.ItemData wanted = InputBridge.McMode ? Item() : null;
            if (_equippedByUs != null && _equippedByUs != wanted && player.IsItemEquiped(_equippedByUs))
            {
                player.UnequipItem(_equippedByUs, true);
            }

            _equippedByUs = null;
            if (wanted != null && wanted.IsEquipable() && !player.IsItemEquiped(wanted))
            {
                player.EquipItem(wanted, true);
                _equippedByUs = wanted;
            }

            if (Prefab.Length > 0 && InputBridge.McMode)
            {
                Plugin.Log.LogInfo("stand-in selected: " + Prefab + (wanted == null ? " (the Viking no longer has it)" : wanted.IsEquipable() ? ", equipped" : ", at hand"));
            }
        }

        /// <summary>The right button with food or a mead at hand: eat or drink it, as from Valheim's hotbar.</summary>
        internal static void Use()
        {
            Player player = Player.m_localPlayer;
            ItemDrop.ItemData item = Item();
            if (player != null && item != null && !item.IsEquipable())
            {
                player.UseItem(player.GetInventory(), item, false);
            }
        }

        /// <summary>The Viking's item the stand-in stands for: the equipped one of that kind if there is one, else the first.</summary>
        private static ItemDrop.ItemData Item()
        {
            Player player = Player.m_localPlayer;
            if (player == null || Prefab.Length == 0)
            {
                return null;
            }

            ItemDrop.ItemData found = null;
            foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
            {
                if (item.m_dropPrefab != null && item.m_dropPrefab.name == Prefab)
                {
                    if (item.m_equipped)
                    {
                        return item;
                    }

                    found = found ?? item;
                }
            }

            return found;
        }
    }
}
