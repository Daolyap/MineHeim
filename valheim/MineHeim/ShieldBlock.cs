using HarmonyLib;
using UnityEngine;

namespace MineHeim
{
    /// <summary>
    /// A raised Minecraft shield is a very strong Valheim shield. While Minecraft reports the shield up ({"t":"shield","up":true}),
    /// the Viking counts as blocking, and a blockable hit from the front (the half the camera faces, as in Minecraft) is:
    /// stopped (Shield.BlockFraction, all of it by default) without stamina, always a perfect parry (the attacker staggers),
    /// and partly thrown back at the attacker (Shield.ReflectFraction). Hits from behind go through as if there were no shield.
    /// Valheim's own blocking path (Character.RPC_Damage -> IsBlocking -> BlockAttack) is used, so it covers everything
    /// Valheim lets a shield block: melee, arrows, and Minecraft mobs' hits forwarded as "hurt".
    /// </summary>
    internal static class ShieldBlock
    {
        public static bool Up;
        public static int Blocks;

        private static bool Applies(Character c)
        {
            return Up && Plugin.ShieldEnabled.Value && Link.Connected && c != null && c == Player.m_localPlayer;
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.IsBlocking))]
        private static class IsBlockingPatch
        {
            private static bool Prefix(Humanoid __instance, ref bool __result)
            {
                if (!Applies(__instance) || __instance.IsDead() || __instance.IsStaggering())
                {
                    return true;
                }

                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
        private static class BlockAttackPatch
        {
            private static bool Prefix(Humanoid __instance, HitData hit, Character attacker, ref bool __result)
            {
                if (!Applies(__instance))
                {
                    return true;
                }

                // Minecraft blocks what comes from the half the player faces: hit.m_dir points from the attacker to us
                Vector3 facing = GameCamera.instance != null ? GameCamera.instance.transform.forward : __instance.transform.forward;
                facing.y = 0f;
                Vector3 incoming = hit.m_dir;
                incoming.y = 0f;
                if (Vector3.Dot(incoming.normalized, facing.normalized) > 0f)
                {
                    __result = false; // from behind: no block, the hit lands in full
                    return false;
                }

                float blocked = hit.GetTotalBlockableDamage() * Mathf.Clamp01(Plugin.ShieldBlock.Value);
                hit.BlockDamage(blocked);
                hit.m_statusEffectHash = 0;
                hit.m_pushForce *= 1f - Mathf.Clamp01(Plugin.ShieldBlock.Value);
                Blocks++;
                DamageText.instance?.ShowText(DamageText.TextType.Blocked, hit.m_point + Vector3.up * 0.5f, blocked);
                __instance.m_perfectBlockEffect.Create(hit.m_point, Quaternion.identity, null, 1f, -1, __instance.GetZDOID());

                if (attacker != null && !attacker.IsDead())
                {
                    if (Plugin.ShieldStagger.Value)
                    {
                        attacker.Stagger(-hit.m_dir);
                    }

                    float reflect = blocked * Mathf.Max(0f, Plugin.ShieldReflect.Value);
                    if (reflect > 0f)
                    {
                        var back = new HitData();
                        back.m_point = attacker.GetCenterPoint();
                        back.m_dir = -hit.m_dir;
                        back.m_pushForce = 30f;
                        back.m_damage.m_blunt = reflect;
                        back.m_attacker = __instance.GetZDOID();
                        attacker.Damage(back);
                    }
                }

                Plugin.Log.LogInfo(string.Format("shield blocked {0:0.0} from {1}", blocked, attacker != null ? attacker.m_name : "a Minecraft mob"));
                __result = true;
                return false;
            }
        }
    }
}
