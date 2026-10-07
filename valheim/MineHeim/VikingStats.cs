using System.Globalization;
using UnityEngine;

namespace MineHeim
{
    /// <summary>
    /// Minecraft's hearts and hunger bar show the Viking's (Survival only: Creative has neither): health as a share of his
    /// max health, food as how many of his three food slots are filled. Valheim's numbers are the real ones; Minecraft only
    /// shows them. Sent twice a second when they change ({"t":"vstats","hp":0..1,"food":0..20}).
    /// </summary>
    internal static class VikingStats
    {
        private static float _next;
        private static string _sent;

        public static void Tick(Player player)
        {
            if (player == null || !Link.Connected || Time.unscaledTime < _next)
            {
                if (!Link.Connected)
                {
                    _sent = null;
                }

                return;
            }

            _next = Time.unscaledTime + 0.5f;
            float hp = player.GetMaxHealth() > 0f ? Mathf.Clamp01(player.GetHealth() / player.GetMaxHealth()) : 1f;
            int food = Mathf.Clamp(Mathf.RoundToInt(player.GetFoods().Count / 3f * 20f), 0, 20);
            string message = string.Format(CultureInfo.InvariantCulture, "{{\"t\":\"vstats\",\"hp\":{0:0.###},\"food\":{1}}}", hp, food);
            if (message != _sent)
            {
                Link.Send(message);
                _sent = message;
            }
        }

        /// <summary>A new Minecraft world: send again.</summary>
        public static void Reset() => _sent = null;
    }
}
