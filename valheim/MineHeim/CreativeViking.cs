using HarmonyLib;

namespace MineHeim
{
    /// <summary>
    /// Minecraft's Creative for the Viking: while he is Steve (Minecraft mode on) in Creative, nothing hurts him (creatures,
    /// explosions, falls, fire, drowning) and nothing uses his stamina (Steve.CreativeInvulnerable). Not Valheim's own god
    /// mode: that one marks every creature he hits as cheated (no drops).
    /// </summary>
    internal static class CreativeViking
    {
        public static int Blocked;
        public static bool TestOff; // test hook "creativegod off"

        public static bool On(Character c) =>
            c != null && c == Player.m_localPlayer && Plugin.CreativeInvulnerable.Value && !TestOff && InputBridge.McMode && Link.Connected &&
            Plugin.GameMode.Value == Plugin.SteveMode.Creative;

        /// <summary>Every kind of harm reaches the owner of a character through this RPC.</summary>
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class NoDamagePatch
        {
            private static bool Prefix(Character __instance)
            {
                if (!On(__instance))
                {
                    return true;
                }

                Blocked++;
                return false;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.UseStamina))]
        private static class NoStaminaPatch
        {
            private static bool Prefix(Player __instance) => !On(__instance);
        }

        [HarmonyPatch(typeof(Player), nameof(Player.HaveStamina))]
        private static class FullStaminaPatch
        {
            private static bool Prefix(Player __instance, ref bool __result)
            {
                if (!On(__instance))
                {
                    return true;
                }

                __result = true;
                return false;
            }
        }
    }
}
