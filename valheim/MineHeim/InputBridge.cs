using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace MineHeim
{
    /// <summary>
    /// Step 7: who has the mouse. In "Minecraft mode" (toggled with F7, config Input.ToggleKey) the left, right and middle
    /// mouse buttons, the wheel, the number keys 1-9 and Q go to Minecraft (break, place/use, pick, hotbar scroll, hotbar slot,
    /// throw the held item) instead of Valheim, whose own attack, block, secondary attack, hotbar, auto-run and camera zoom
    /// are switched off (InputPatches).
    /// Only while the game is being played: with a menu, the inventory, the chat or the console open the cursor is not locked
    /// and nothing is forwarded or suppressed. Everything else (walking, jumping, E to interact, the camera) stays Valheim's.
    /// </summary>
    internal static class InputBridge
    {
        public static bool McMode;
        public static int ValheimAttacks, HotbarUses, McSlot = -1;

        private static bool _attackDown, _useDown, _pickDown;
        private static int _activeFrame = -1;
        private static bool _active;

        /// <summary>Minecraft has the mouse right now (cached per frame: Valheim asks for its buttons many times a frame).</summary>
        public static bool Active
        {
            get
            {
                if (_activeFrame != Time.frameCount)
                {
                    _activeFrame = Time.frameCount;
                    _active = McMode && Player.m_localPlayer != null && Cursor.lockState == CursorLockMode.Locked;
                }

                return _active;
            }
        }

        /// <summary>The names of Valheim's buttons that Minecraft takes over while it has the mouse.</summary>
        public static bool Takes(string button)
        {
            if (ScreenBridge.Open)
            {
                return true; // a Minecraft screen has the keyboard and mouse
            }

            return Active && (button == "Attack" || button == "SecondaryAttack" || button == "Block" || button == "AutoRun" || button == "Inventory" ||
                              button == "OpenEmote" || button.StartsWith("Hotbar"));
        }

        public static void SetMode(bool on, string why)
        {
            if (McMode == on)
            {
                return;
            }

            McMode = on;
            _activeFrame = -1;
            ReleaseAll();
            SendSteve();
            Plugin.Log.LogInfo("minecraft mode " + (on ? "ON" : "off") + " (" + why + ")");
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, on ? "Minecraft mode on: you are Steve" : "Minecraft mode off: you are the Viking");
        }

        /// <summary>
        /// Minecraft shows Steve and its hotbar only in Minecraft mode (sent on every change and every new connection); his
        /// body not at all when Valheim draws him (Steve.Avatar = Native).
        /// </summary>
        public static void SendSteve()
        {
            Link.Send("{\"t\":\"steve\",\"on\":" + (McMode ? "true" : "false") + ",\"body\":" + (SteveAvatar.Native ? "false" : "true") + "}");
        }

        /// <summary>Every frame, main thread.</summary>
        public static void Poll()
        {
            if (ZInput.GetKeyDown(Plugin.ToggleKey.Value, false))
            {
                SetMode(!McMode, "key");
            }

            // F8: Creative <-> Survival, in game (no menu open)
            if (Player.m_localPlayer != null && Cursor.lockState == CursorLockMode.Locked && ZInput.GetKeyDown(Plugin.GameModeKey.Value, false))
            {
                Plugin.GameMode.Value = Plugin.GameMode.Value == Plugin.SteveMode.Creative ? Plugin.SteveMode.Survival : Plugin.SteveMode.Creative;
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Steve: " + Plugin.GameMode.Value);
            }

            if (Player.m_localPlayer != null && Cursor.lockState == CursorLockMode.Locked)
            {
                if (ZInput.GetKeyDown(Plugin.StatusKey.Value, false))
                {
                    ShowStatus(Player.m_localPlayer);
                }

                if (ZInput.GetKeyDown(Plugin.AvatarKey.Value, false))
                {
                    Plugin.SteveAvatarMode.Value = SteveAvatar.Native ? Plugin.AvatarMode.Minecraft : Plugin.AvatarMode.Native;
                    Player.m_localPlayer.Message(MessageHud.MessageType.Center, SteveAvatar.Native ? "Steve: drawn by Valheim" : "Steve: drawn by Minecraft (with his held item)");
                }
            }

            // the Viking's E on a Minecraft chest, crafting table or furnace (nothing of Valheim's under the crosshair) opens it
            Player local = Player.m_localPlayer;
            if (!McMode && local != null && Link.Connected && !ScreenBridge.Open && Cursor.lockState == CursorLockMode.Locked &&
                ZInput.GetButtonDown("Use"))
            {
                // Valheim reports whatever collider is in reach (the terrain, our own colliders for Minecraft's blocks): only
                // something Valheim can interact with keeps the key for Valheim
                GameObject hover = local.GetHoverObject();
                if (hover != null && hover.GetComponentInParent<Interactable>() == null)
                {
                    hover = null;
                }

                if (hover == null)
                {
                    Link.Send("{\"t\":\"interact\"}");
                }

                Plugin.Log.LogInfo("use key as the Viking: " + (hover == null ? "nothing of Valheim's to use under the crosshair, asked Minecraft to open what is there" : "Valheim's " + hover.name));
            }

            if (!Active)
            {
                ReleaseAll(); // a menu opened or the mode ended: never leave Minecraft holding a button
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                Forward(mouse.leftButton, "attack", ref _attackDown);
                Forward(mouse.rightButton, "use", ref _useDown);
                Forward(mouse.middleButton, "pick", ref _pickDown);
                float wheel = mouse.scroll.ReadValue().y;
                if ((wheel > 0.5f || wheel < -0.5f) && !AltHeld)
                {
                    int notches = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(wheel) / 120f));
                    for (int i = 0; i < notches; i++)
                    {
                        Link.Send("{\"t\":\"scroll\",\"d\":" + (wheel > 0f ? 1 : -1) + "}");
                    }
                }
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                for (int i = 0; i < 9; i++)
                {
                    if (keyboard[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame)
                    {
                        Link.Send("{\"t\":\"slot\",\"n\":" + i + "}");
                    }
                }

                // Tab opens Minecraft's inventory (creative tabs in Creative), T its chat, / a command line
                if (keyboard.tabKey.wasPressedThisFrame)
                {
                    Link.Send("{\"t\":\"screenopen\",\"k\":\"inventory\"}");
                }
                else if (keyboard.tKey.wasPressedThisFrame)
                {
                    Link.Send("{\"t\":\"screenopen\",\"k\":\"chat\"}");
                }
                else if (keyboard.slashKey.wasPressedThisFrame)
                {
                    Link.Send("{\"t\":\"screenopen\",\"k\":\"command\"}");
                }

                // Q throws the held Minecraft item (Ctrl+Q the whole stack), as in Minecraft; Valheim's auto-run waits meanwhile
                if (keyboard.qKey.wasPressedThisFrame)
                {
                    bool stack = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
                    Link.Send("{\"t\":\"key\",\"k\":\"drop\",\"down\":true,\"stack\":" + (stack ? "true" : "false") + "}");
                }
            }
        }

        /// <summary>Alt held: the wheel zooms Valheim's camera even in Minecraft mode (it otherwise turns Minecraft's hotbar).</summary>
        public static bool AltHeld => Keyboard.current != null && Keyboard.current.altKey.isPressed;

        /// <summary>The status key: what MineHeim is doing, top left; starts Minecraft again if it is not attached.</summary>
        private static void ShowStatus(Player player)
        {
            var lines = new System.Collections.Generic.List<string>();
            if (Link.Connected)
            {
                lines.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "MineHeim: Minecraft attached, {0:0} fps, its picture {1:0.0} frame(s) behind",
                    PoseSender.Fps, MotionPrediction.LagFrames));
            }
            else
            {
                lines.Add("MineHeim: Minecraft not attached: " + MinecraftLauncher.StartAgain());
            }

            lines.Add((McMode ? "You are Steve (" + Plugin.GameMode.Value + ")" : "You are the Viking (F7: Steve)") + ", Steve drawn by " +
                      (SteveAvatar.Native ? "Valheim, skin " + SteveAvatar.SkinSource : "Minecraft"));
            int shown = 0;
            foreach (NetherBridge.Zone z in NetherBridge.Zones.Values)
            {
                if (shown++ == 3)
                {
                    break;
                }

                lines.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} portal {1:0} m away: {2}, {3:0} m across{4}",
                    z.Kind == 1 ? "End" : "Nether", Utils.DistanceXZ(player.transform.position, z.Centre), z.Lit ? "open" : "out, healing", z.Radius * 2f,
                    z.Dragon ? ", the dragon is here" : ""));
            }

            if (NetherBridge.Zones.Count == 0)
            {
                lines.Add("No Nether or End portal burning");
            }

            foreach (string line in lines)
            {
                player.Message(MessageHud.MessageType.TopLeft, line);
                Plugin.Log.LogInfo("status: " + line);
            }
        }

        private static void Forward(ButtonControl button, string name, ref bool held)
        {
            // a click shorter than a frame (press and release in the same one) must still arrive, in that order
            if (button.wasPressedThisFrame)
            {
                Link.Send("{\"t\":\"key\",\"k\":\"" + name + "\",\"down\":true}");
                held = true;
            }

            if (held && (button.wasReleasedThisFrame || !button.isPressed))
            {
                Link.Send("{\"t\":\"key\",\"k\":\"" + name + "\",\"down\":false}");
                held = false;
            }
        }

        private static void ReleaseAll()
        {
            Release("attack", ref _attackDown);
            Release("use", ref _useDown);
            Release("pick", ref _pickDown);
        }

        private static void Release(string name, ref bool held)
        {
            if (held)
            {
                Link.Send("{\"t\":\"key\",\"k\":\"" + name + "\",\"down\":false}");
                held = false;
            }
        }
    }

    // ---------------------------------------------------------------------- Valheim's own input, switched off while Minecraft has it

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown), typeof(string))]
    internal static class ButtonDownPatch
    {
        private static bool Prefix(string name, ref bool __result)
        {
            if (!InputBridge.Takes(name))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButton), typeof(string))]
    internal static class ButtonHeldPatch
    {
        private static bool Prefix(string name, ref bool __result)
        {
            if (!InputBridge.Takes(name))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonUp), typeof(string))]
    internal static class ButtonUpPatch
    {
        private static bool Prefix(string name, ref bool __result)
        {
            if (!InputBridge.Takes(name))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    /// <summary>The wheel moves Minecraft's hotbar, not Valheim's camera zoom (Alt + wheel still zooms).</summary>
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class ScrollPatch
    {
        private static bool Prefix(ref float __result)
        {
            if (!InputBridge.Active || InputBridge.AltHeld)
            {
                return true; // Alt + wheel: Valheim's camera zoom, also in Minecraft mode
            }

            __result = 0f;
            return false;
        }
    }

    // ---------------------------------------------------------------------- counters for the oracle (they change nothing)

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class AttackCounterPatch
    {
        private static void Postfix(Humanoid __instance, bool __result)
        {
            if (__result && __instance == Player.m_localPlayer)
            {
                InputBridge.ValheimAttacks++;
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.UseHotbarItem))]
    internal static class HotbarCounterPatch
    {
        private static void Prefix()
        {
            InputBridge.HotbarUses++;
        }
    }
}
