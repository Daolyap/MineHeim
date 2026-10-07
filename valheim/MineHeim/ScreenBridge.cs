using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace MineHeim
{
    /// <summary>
    /// Minecraft's own screens, worked from Valheim's window: its inventory and creative tabs (Tab in Minecraft mode), its
    /// chat and commands (T and /), and whatever Minecraft opens itself (a chest, a crafting table, a furnace). Minecraft
    /// draws the screen into the overlay layer that is composited over Valheim anyway; while one is open ({"t":"screen"}
    /// from Minecraft) Valheim frees the cursor and hands Minecraft the cursor position, the mouse buttons, the wheel,
    /// the keys and the typed text. Valheim's own buttons, keys, camera and menu wait meanwhile; Escape closes the screen.
    /// </summary>
    internal static class ScreenBridge
    {
        /// <summary>A Minecraft screen is open (Minecraft said so).</summary>
        public static bool Open { get; private set; }
        public static string Name = "";
        public static int Opened, Keys, Chars, Clicks;

        private static Vector2 _lastPos = new Vector2(-1f, -1f);
        private static readonly Queue<char> Typed = new Queue<char>();
        private static bool _textHooked;

        public static void OnScreen(string json)
        {
            bool open = Core.Messages.BoolValue(json, "open", false);
            Name = Core.Messages.StringValue(json, "name", "");
            if (open && !Open)
            {
                Opened++;
                _lastPos = new Vector2(-1f, -1f);
                Typed.Clear();
            }

            Open = open && Link.Connected;
            Plugin.Log.LogInfo("minecraft screen " + (Open ? "open: " + Name : string.Format("closed (forwarded {0} key(s), {1} character(s), {2} click(s))", Keys, Chars, Clicks)));
            if (Open)
            {
                Keys = Chars = Clicks = 0;
            }
        }

        public static void Disconnected()
        {
            Open = false;
        }

        /// <summary>Every frame, main thread: forward input to the open screen.</summary>
        public static void Poll()
        {
            if (!_textHooked && Keyboard.current != null)
            {
                _textHooked = true;
                Keyboard.current.onTextInput += c =>
                {
                    if (Open && !char.IsControl(c))
                    {
                        Typed.Enqueue(c);
                    }
                };
            }

            if (!Open || !Link.Connected)
            {
                Open = Open && Link.Connected;
                return;
            }

            // Minecraft draws no cursor of its own into its picture (SDL shows the system's): Valheim's stands in
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;
            int mods = Mods(keyboard);
            if (mouse != null)
            {
                // Unity counts from the bottom left of the game's picture, Minecraft from the top left of its window (same size)
                Vector2 p = mouse.position.ReadValue();
                var at = new Vector2(p.x, Screen.height - p.y);
                if ((at - _lastPos).sqrMagnitude > 0.01f)
                {
                    _lastPos = at;
                    Link.Send(string.Format(CultureInfo.InvariantCulture, "{{\"t\":\"mmove\",\"x\":{0:0.#},\"y\":{1:0.#}}}", at.x, at.y));
                }

                Button(mouse.leftButton, 1, mods); // SDL3 buttons: 1 left, 2 middle, 3 right
                Button(mouse.rightButton, 3, mods);
                Button(mouse.middleButton, 2, mods);
                float wheel = mouse.scroll.ReadValue().y;
                if (wheel > 0.5f || wheel < -0.5f)
                {
                    Link.Send("{\"t\":\"mscroll\",\"d\":" + (wheel > 0f ? "1" : "-1") + "}");
                }
            }

            // text typed this frame goes before this frame's keys: "...16" then Enter must not run the command without the "16"
            while (Typed.Count > 0)
            {
                Chars++;
                Link.Send("{\"t\":\"char\",\"c\":" + (int)Typed.Dequeue() + "}");
            }

            if (keyboard != null)
            {
                foreach (KeyControl key in keyboard.allKeys)
                {
                    if (key == null)
                    {
                        continue;
                    }

                    bool down = key.wasPressedThisFrame, up = key.wasReleasedThisFrame;
                    if (!down && !up)
                    {
                        continue;
                    }

                    // Tab opened the inventory, so Tab closes it too (Minecraft closes it on its inventory key, E = scancode 8)
                    int code = key.keyCode == Key.Tab && Name.Contains("Inventory") ? 8 : Scancode(key.keyCode);
                    if (code > 0)
                    {
                        Keys++;
                        Link.Send("{\"t\":\"kpress\",\"k\":" + code + ",\"down\":" + (down ? "true" : "false") + ",\"mods\":" + mods + "}");
                    }
                }
            }
        }

        private static void Button(ButtonControl button, int index, int mods)
        {
            if (button.wasPressedThisFrame)
            {
                Clicks++;
                Link.Send("{\"t\":\"mbtn\",\"b\":" + index + ",\"down\":true,\"mods\":" + mods + "}");
            }

            if (button.wasReleasedThisFrame)
            {
                Link.Send("{\"t\":\"mbtn\",\"b\":" + index + ",\"down\":false,\"mods\":" + mods + "}");
            }
        }

        /// <summary>SDL3's modifier bits, which Minecraft 26 reads off its input events: Shift 0x1, Ctrl 0x40, Alt 0x100.</summary>
        private static int Mods(Keyboard k)
        {
            if (k == null)
            {
                return 0;
            }

            return (k.shiftKey.isPressed ? 0x1 : 0) | (k.ctrlKey.isPressed ? 0x40 : 0) | (k.altKey.isPressed ? 0x100 : 0);
        }

        /// <summary>Unity's key to the SDL3 scancode Minecraft 26 uses for its keys (USB HID usage ids); 0 = not forwarded.</summary>
        internal static int Scancode(Key k)
        {
            if (k >= Key.A && k <= Key.Z)
            {
                return 4 + (k - Key.A);
            }

            if (k >= Key.Digit1 && k <= Key.Digit9)
            {
                return 30 + (k - Key.Digit1);
            }

            if (k >= Key.F1 && k <= Key.F12)
            {
                return 58 + (k - Key.F1);
            }

            switch (k)
            {
                case Key.Digit0: return 39;
                case Key.Enter: return 40;
                case Key.Escape: return 41;
                case Key.Backspace: return 42;
                case Key.Tab: return 43;
                case Key.Space: return 44;
                case Key.Minus: return 45;
                case Key.Equals: return 46;
                case Key.LeftBracket: return 47;
                case Key.RightBracket: return 48;
                case Key.Backslash: return 49;
                case Key.Semicolon: return 51;
                case Key.Quote: return 52;
                case Key.Backquote: return 53;
                case Key.Comma: return 54;
                case Key.Period: return 55;
                case Key.Slash: return 56;
                case Key.Insert: return 73;
                case Key.Home: return 74;
                case Key.PageUp: return 75;
                case Key.Delete: return 76;
                case Key.End: return 77;
                case Key.PageDown: return 78;
                case Key.RightArrow: return 79;
                case Key.LeftArrow: return 80;
                case Key.DownArrow: return 81;
                case Key.UpArrow: return 82;
                case Key.NumpadEnter: return 88;
                case Key.LeftCtrl: return 224;
                case Key.LeftShift: return 225;
                case Key.LeftAlt: return 226;
                case Key.RightCtrl: return 228;
                case Key.RightShift: return 229;
                case Key.RightAlt: return 230;
                default: return 0;
            }
        }
        // ------------------------------------------------------------------ Valheim waits while a Minecraft screen is open

        /// <summary>The cursor stays free (Valheim would capture it again every frame).</summary>
        [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
        private static class CursorPatch
        {
            private static bool Prefix()
            {
                if (!Open)
                {
                    return true;
                }

                ZCursor.LockState = CursorLockMode.None;
                ZCursor.Show();
                return false;
            }
        }

        /// <summary>The camera does not turn with the cursor.</summary>
        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseDelta))]
        private static class MouseDeltaPatch
        {
            private static bool Prefix(ref Vector2 __result)
            {
                if (!Open)
                {
                    return true;
                }

                __result = Vector2.zero;
                return false;
            }
        }

        /// <summary>Escape and the other keys go to Minecraft's screen, not to Valheim's menu.</summary>
        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetKeyDown), typeof(KeyCode), typeof(bool))]
        private static class KeyDownPatch
        {
            private static bool Prefix(ref bool __result)
            {
                if (!Open)
                {
                    return true;
                }

                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetKey), typeof(KeyCode), typeof(bool))]
        private static class KeyHeldPatch
        {
            private static bool Prefix(ref bool __result)
            {
                if (!Open)
                {
                    return true;
                }

                __result = false;
                return false;
            }
        }
    }
}
