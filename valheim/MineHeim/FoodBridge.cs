using MineHeim.Core;
using UnityEngine;

namespace MineHeim
{
    /// <summary>
    /// Steve eating is the Viking eating: Minecraft's food becomes a Valheim food of the same standing (Core.FoodMap) and
    /// fills one of the Viking's three food slots, with Valheim's own health, stamina, regeneration and duration. Minecraft's
    /// hunger bar only shows those slots. In Creative its free food stays Minecraft's, as its items do (Items.CreativeToValheim).
    /// A food the Viking cannot eat now (that one is still in a slot, or all three are full) goes back into Steve's inventory.
    /// </summary>
    internal static class FoodBridge
    {
        private static bool _toldCreative;

        /// <summary>{"t":"ate","item":"bread","n":nutrition}</summary>
        public static void OnAte(string json)
        {
            Player player = Player.m_localPlayer;
            string item = Messages.StringValue(json, "item", "");
            if (player == null || player.IsDead() || item.Length == 0)
            {
                return;
            }

            if (Plugin.GameMode.Value == Plugin.SteveMode.Creative && !Plugin.CreativeToValheim.Value)
            {
                if (!_toldCreative)
                {
                    _toldCreative = true;
                    player.Message(MessageHud.MessageType.TopLeft, "Creative food stays in Minecraft (Survival feeds the Viking)");
                }

                return;
            }

            // its Valheim twin, or a food of the same standing (raw meat, rotten flesh, a newer Minecraft's food)
            string prefab = FoodMap.Resolve(item, Messages.IntValue(json, "n", 0), p => FoodOf(p) != null);
            if (prefab.Length == 0)
            {
                Plugin.Log.LogInfo("steve ate " + item + ": no Valheim food");
                return;
            }

            ItemDrop.ItemData food = FoodOf(prefab).Clone();
            food.m_dropPrefab = ObjectDB.instance.GetItemPrefab(prefab); // Valheim's food slot is keyed by it (unset on the prefab's own data)
            if (player.CanEat(food, true) && player.EatFood(food))
            {
                Plugin.Log.LogInfo("steve ate " + item + ": the Viking ate " + prefab);
                return;
            }

            // nothing eaten in Valheim: Minecraft took the item, so give it back
            Link.Send(Messages.Command("give @a minecraft:" + item + " 1"));
            Plugin.Log.LogInfo("steve ate " + item + ": the Viking cannot eat " + prefab + " now, given back");
        }

        private static ItemDrop.ItemData FoodOf(string prefab)
        {
            GameObject go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            return drop != null && drop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && drop.m_itemData.m_shared.m_food > 0f
                ? drop.m_itemData : null;
        }
    }
}
