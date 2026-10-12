using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MineHeim
{
    /// <summary>
    /// Free look (Steve.FreeLook): hold Alt and the mouse swings Valheim's camera around the player instead of turning him,
    /// so you can see Steve's face (and his Herobrine eyes) or take a picture from the front. Valheim's camera is always the
    /// player's look direction (behind his eye along its rotation), so the mouse's turn is kept here as an orbit and the camera
    /// placed by it; let go of Alt and it eases back behind him. Minecraft's camera follows Valheim's as always.
    /// </summary>
    internal static class FreeLook
    {
        /// <summary>The orbit around the player's own look, degrees.</summary>
        internal static float Yaw, Pitch;

        /// <summary>Tests and takes only (the "orbit" test command): the orbit is held without Alt.</summary>
        internal static bool Scripted;

        private const float ReturnSpeed = 540f; // degrees a second back behind him once Alt is let go
        private static readonly System.Reflection.MethodInfo CollideRay2 = AccessTools.Method(typeof(GameCamera), "CollideRay2");
        private static readonly System.Reflection.MethodInfo OffsetedEyePos = AccessTools.Method(typeof(GameCamera), "GetOffsetedEyePos");
        private static readonly AccessTools.FieldRef<GameCamera, float> Distance = AccessTools.FieldRefAccess<GameCamera, float>("m_distance");
        private static readonly object[] Args = new object[3];

        /// <summary>Alt held in play (no menu, no Minecraft screen, the mouse captured).</summary>
        internal static bool Held => Scripted ||
            Plugin.FreeLookOn.Value && Keyboard.current != null && Keyboard.current.altKey.isPressed && Cursor.lockState == CursorLockMode.Locked &&
            !ScreenBridge.Open && Player.m_localPlayer != null;

        internal static bool Orbiting => Yaw != 0f || Pitch != 0f;

        private static void Ease(float dt)
        {
            if (!Held && Orbiting)
            {
                Yaw = Mathf.MoveTowards(Mathf.DeltaAngle(0f, Yaw), 0f, ReturnSpeed * dt);
                Pitch = Mathf.MoveTowards(Pitch, 0f, ReturnSpeed * dt);
            }
        }

        /// <summary>The mouse turns the orbit, not the player, while Alt is held.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.SetMouseLook))]
        private static class LookPatch
        {
            private static bool Prefix(Player __instance, Vector2 mouseLook)
            {
                if (__instance != Player.m_localPlayer || !Held)
                {
                    return true;
                }

                Yaw += mouseLook.x;
                Pitch = Mathf.Clamp(Pitch - mouseLook.y, -80f, 80f);
                return false;
            }
        }

        /// <summary>The camera around the player's head by the orbit, kept out of walls and the ground as Valheim keeps it.</summary>
        [HarmonyPatch(typeof(GameCamera), "GetCameraPosition")]
        private static class CameraPatch
        {
            private static void Postfix(GameCamera __instance, float dt, ref Vector3 pos, ref Quaternion rot)
            {
                Ease(dt);
                Player player = Player.m_localPlayer;
                if (!Orbiting || player == null || player.m_eye == null || OffsetedEyePos == null || CollideRay2 == null)
                {
                    return;
                }

                Vector3 pivot = (Vector3)OffsetedEyePos.Invoke(__instance, null);
                Vector3 e = rot.eulerAngles;
                float pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, e.x) + Pitch, -85f, 85f);
                rot = Quaternion.Euler(pitch, e.y + Yaw, 0f);
                Vector3 end = pivot - rot * Vector3.forward * Distance(__instance);
                Args[0] = player.m_eye.position;
                Args[1] = pivot;
                Args[2] = end;
                CollideRay2.Invoke(__instance, Args);
                pos = (Vector3)Args[2];
            }
        }
    }
}
