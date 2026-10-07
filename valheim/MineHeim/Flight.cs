using HarmonyLib;
using UnityEngine;

namespace MineHeim
{
    /// <summary>
    /// Creative flight, Minecraft style: double-tap Jump to fly or land, hold Jump to rise and Left Ctrl to sink, Run for speed.
    /// The Viking flies with the movement of Valheim's own debug fly (Character.UpdateDebugFly), but NOT through debug fly
    /// itself: that one makes the player invulnerable and marks every creature he hits as "cheated" (no drops). Here the
    /// Viking can still be hurt, and creatures he kills drop their loot. No fall damage after landing from a flight (Creative).
    /// </summary>
    internal static class Flight
    {
        private const float DoubleTap = 0.45f; // Minecraft uses 0.35 s; a little more for low frame rates

        private static readonly AccessTools.FieldRef<Character, float> MaxAirAltitude = AccessTools.FieldRefAccess<Character, float>("m_maxAirAltitude");
        private static readonly AccessTools.FieldRef<Character, bool> Sliding = AccessTools.FieldRefAccess<Character, bool>("m_sliding");
        private static readonly AccessTools.FieldRef<Character, bool> WallRunning = AccessTools.FieldRefAccess<Character, bool>("m_wallRunning");
        private static readonly AccessTools.FieldRef<Character, bool> Running = AccessTools.FieldRefAccess<Character, bool>("m_running");
        private static readonly AccessTools.FieldRef<Character, bool> Walking = AccessTools.FieldRefAccess<Character, bool>("m_walking");
        private static readonly System.Action<Character, float> FlyStep = AccessTools.MethodDelegate<System.Action<Character, float>>(AccessTools.Method(typeof(Character), "UpdateDebugFly"));
        private static readonly System.Action<Character> BodyFriction = AccessTools.MethodDelegate<System.Action<Character>>(AccessTools.Method(typeof(Character), "UpdateBodyFriction"));

        public static bool Active { get; private set; }

        /// <summary>After a flight ends: no fall damage until the Viking touches the ground again.</summary>
        private static bool _landing;
        private static float _landingSince, _lastY;
        private static float _lastJump = -1f;
        private static int _savedSpeed = -1;

        public static void Tick(Player player)
        {
            if (player == null)
            {
                Active = _landing = false;
                return;
            }

            bool allowed = Link.Connected && Plugin.GameMode.Value == Plugin.SteveMode.Creative && !player.IsDead();
            if (!allowed && Active)
            {
                Stop(Link.Connected ? "not allowed now" : "Minecraft disconnected");
            }

            // double-tap Jump, only while the game has the mouse (no menu, inventory, chat or console open)
            if (allowed && Cursor.lockState == CursorLockMode.Locked && ZInput.GetButtonDown("Jump"))
            {
                float now = Time.unscaledTime;
                Plugin.Log.LogInfo(string.Format("flight: Jump tap ({0:0.00} s after the last)", _lastJump < 0f ? -1f : now - _lastJump));
                if (now - _lastJump < DoubleTap)
                {
                    _lastJump = -1f;
                    if (Active)
                    {
                        Stop("double-tap");
                    }
                    else
                    {
                        Start();
                    }
                }
                else
                {
                    _lastJump = now;
                }
            }

            // sinking onto the ground lands, as in Minecraft (IsOnGround() is no use while flying: flight keeps it true)
            if (Active && ZInput.GetKey(KeyCode.LeftControl) && Physics.Raycast(player.transform.position + Vector3.up * 0.2f, Vector3.down, 0.45f, GroundMask))
            {
                Stop("landed");
            }

            if (_landing)
            {
                // the same goes for the end of a flight: wait for a real touchdown (on the ground, a moment after the flight,
                // no longer falling) before Valheim's fall damage counts again
                float y = player.transform.position.y;
                MaxAirAltitude(player) = y;
                bool settled = Mathf.Abs(y - _lastY) < 0.02f && Time.time - _landingSince > 0.6f;
                if ((player.IsOnGround() && settled) || player.InWater())
                {
                    _landing = false;
                }

                _lastY = y;
            }
        }

        private static readonly int GroundMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");

        public static void Start()
        {
            Active = true;
            _savedSpeed = Character.m_debugFlySpeed;
            Character.m_debugFlySpeed = Mathf.Max(1, Mathf.RoundToInt(Plugin.FlySpeed.Value));
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, "Flying (double-tap Jump to stop)");
            Plugin.Log.LogInfo("flight on");
        }

        public static void Stop(string why)
        {
            if (!Active)
            {
                return;
            }

            Active = false;
            _landing = true;
            _landingSince = Time.time;
            _lastY = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position.y : 0f;
            if (_savedSpeed >= 0)
            {
                Character.m_debugFlySpeed = _savedSpeed;
            }

            Plugin.Log.LogInfo("flight off (" + why + ")");
        }

        /// <summary>Instead of walking, swimming or falling: fly (the same steps UpdateMotion takes for debug fly).</summary>
        [HarmonyPatch(typeof(Character), "UpdateMotion")]
        private static class UpdateMotionPatch
        {
            private static bool Prefix(Character __instance, float dt)
            {
                if (!Active || __instance != Player.m_localPlayer || __instance.IsDead())
                {
                    return true;
                }

                BodyFriction(__instance);
                Sliding(__instance) = false;
                WallRunning(__instance) = false;
                Running(__instance) = false;
                Walking(__instance) = false;
                FlyStep(__instance, dt);
                return false;
            }
        }
    }
}
