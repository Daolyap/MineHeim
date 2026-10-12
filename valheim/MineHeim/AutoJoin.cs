using System.IO;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace MineHeim
{
    /// <summary>
    /// Tests only: BepInEx/mineheim-autojoin.txt holding "character|world" (e.g. "ctest|TESTWORLD2") starts that character
    /// in that world from the main menu, through the menu's own buttons' methods, so a test run can restart Valheim with
    /// nobody at the keyboard. The file is deleted when it is used; without it nothing changes.
    /// </summary>
    internal static class AutoJoin
    {
        private static readonly string FilePath = Path.Combine(Paths.BepInExRootPath, "mineheim-autojoin.txt");
        private static float _menuSince = -1f;

        [HarmonyPatch(typeof(FejdStartup), "Update")]
        private static class MenuPatch
        {
            private static void Postfix(FejdStartup __instance)
            {
                if (_menuSince < 0f)
                {
                    _menuSince = Time.realtimeSinceStartup;
                }

                // a few seconds for the menu to settle (profiles and worlds listed)
                if (Time.realtimeSinceStartup - _menuSince < 5f || !File.Exists(FilePath))
                {
                    return;
                }

                string[] parts;
                try
                {
                    parts = File.ReadAllText(FilePath).Trim().Split('|');
                    File.Delete(FilePath);
                }
                catch (IOException)
                {
                    return;
                }

                if (parts.Length < 2)
                {
                    Plugin.Log.LogWarning("autojoin: want \"character|world\"");
                    return;
                }

                Traverse t = Traverse.Create(__instance);
                t.Method("SetSelectedProfile", parts[0].Trim()).GetValue();
                t.Method("OnCharacterStart").GetValue();
                var worlds = t.Field("m_worlds").GetValue<System.Collections.Generic.List<World>>();
                int index = worlds == null ? -1 : worlds.FindIndex(w => w.m_name == parts[1].Trim());
                if (index < 0)
                {
                    Plugin.Log.LogWarning("autojoin: no world " + parts[1]);
                    return;
                }

                t.Method("SetSelectedWorld", index, false).GetValue();
                t.Method("OnWorldStart").GetValue();
                Plugin.Log.LogInfo("autojoin: " + parts[0] + " in " + parts[1]);
            }
        }
    }
}
