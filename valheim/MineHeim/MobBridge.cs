using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using MineHeim.Core;

namespace MineHeim
{
    /// <summary>
    /// Minecraft's mobs and Valheim's creatures in one fight:
    ///   creatures  Valheim's creatures near the player go to Minecraft ~10 times a second ({"t":"creatures"}); they stand
    ///              there invisibly, Minecraft's monsters hunt them, and those hits come back as {"t":"chit"}
    ///   mobs       Minecraft's mobs near the player ({"t":"mobs"}) get an invisible body in Valheim (a creature prefab with
    ///              its looks, AI, drops and sounds taken out): Valheim's creatures see it as an enemy and attack it, and the
    ///              Viking can hit it with his own weapons. Its hits go back to Minecraft ({"t":"mhit"}); the body never
    ///              loses health in Valheim, Minecraft owns the mob's health and the body only shows it.
    /// Damage crosses at one exchange rate (Messages.HealthPerPoint): a Minecraft point is the Viking's max health / 20.
    /// </summary>
    internal sealed class MobBridge
    {
        public const string ObjectName = "vc_mob";
        private static readonly HashSet<Character> Bodies = new HashSet<Character>();
        private static readonly AccessTools.FieldRef<Character, float> BackstabTime = AccessTools.FieldRefAccess<Character, float>("m_backstabTime");

        private sealed class Body
        {
            public Character Character;
            public Vector3 Target;
            public string Type;
        }

        private readonly Dictionary<int, Body> _bodies = new Dictionary<int, Body>();
        private readonly List<Messages.Creature> _creatures = new List<Messages.Creature>();
        private readonly List<int> _gone = new List<int>();
        private float _nextCreatures;
        private bool _sentNone = true;
        private GameObject _prefab;
        private bool _prefabLooked;

        public int CreaturesSent, BodiesMade, HitsToMinecraft, HitsFromMinecraft;
        public int BodyCount => _bodies.Count;

        /// <summary>One of Minecraft's mobs standing in Valheim (not a creature of Valheim's own).</summary>
        public static bool IsBody(Character c) => c != null && Bodies.Contains(c);

        public static double Rate()
        {
            Player p = Player.m_localPlayer;
            return Messages.HealthPerPoint(p != null ? p.GetMaxHealth() : 25f);
        }

        // ------------------------------------------------------------------ Valheim's creatures -> Minecraft

        public void Tick(Player player, double yOffset)
        {
            if (player == null || !Link.Connected || !Plugin.MobsEnabled.Value)
            {
                if (_bodies.Count > 0)
                {
                    Clear();
                }

                return;
            }

            float now = Time.unscaledTime;
            if (now >= _nextCreatures)
            {
                _nextCreatures = now + 0.1f;
                SendCreatures(player, yOffset);
            }

            // bodies glide to where Minecraft last put them (5 updates a second)
            float k = Mathf.Clamp01(Time.deltaTime * 12f);
            foreach (Body b in _bodies.Values)
            {
                if (b.Character != null)
                {
                    Transform t = b.Character.transform;
                    t.position = Vector3.Lerp(t.position, b.Target, k);
                }
            }
        }

        private void SendCreatures(Player player, double yOffset)
        {
            _creatures.Clear();
            Vector3 at = player.transform.position;
            float r2 = Plugin.MobRadius.Value * Plugin.MobRadius.Value;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c == player || c.IsPlayer() || c.IsDead() || IsBody(c) || (c.transform.position - at).sqrMagnitude > r2)
                {
                    continue;
                }

                // its size from its capsule (scaled creatures, e.g. two-star ones, are bigger)
                float width = 0.8f, height = 1.8f;
                CapsuleCollider capsule = c.GetComponent<CapsuleCollider>();
                if (capsule != null)
                {
                    Vector3 s = capsule.transform.lossyScale;
                    width = capsule.radius * 2f * Mathf.Max(s.x, s.z);
                    height = Mathf.Max(capsule.height * s.y, width);
                }

                Vector3 feet = c.transform.position;
                double mx, my, mz;
                Mapping.Position(feet.x, feet.y, feet.z, yOffset, out mx, out my, out mz);
                _creatures.Add(new Messages.Creature { Id = c.GetInstanceID(), Name = Utils.GetPrefabName(c.gameObject), X = mx, Y = my, Z = mz, Width = width, Height = height });
            }

            if (_creatures.Count == 0 && _sentNone)
            {
                return;
            }

            _sentNone = _creatures.Count == 0;
            Link.Send(Messages.Creatures(_creatures));
            CreaturesSent = _creatures.Count;
        }

        /// <summary>{"t":"chit"}: a Minecraft mob hit one of Valheim's creatures (its proxy there).</summary>
        public void OnCreatureHit(string json)
        {
            int id = Messages.IntValue(json, "id", 0);
            Character victim = null;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c != null && c.GetInstanceID() == id)
                {
                    victim = c;
                    break;
                }
            }

            if (victim == null || victim.IsDead())
            {
                return;
            }

            float amount = (float)(Messages.DoubleValue(json, "amount", 0) * Rate() * Plugin.WeaponDamageScale.Value);
            Vector3 dir = victim.transform.forward * -1f;
            double x, y, z;
            if (Messages.Pos(json, out x, out y, out z))
            {
                double ux, uy, uz;
                Mapping.FromMinecraft(x, y, z, Plugin.YOffset.Value, out ux, out uy, out uz);
                dir = victim.transform.position - new Vector3((float)ux, (float)uy, (float)uz);
            }

            var hit = new HitData { m_point = victim.GetCenterPoint(), m_dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.up, m_pushForce = 20f };
            if (Messages.BoolValue(json, "ranged", false))
            {
                hit.m_damage.m_pierce = amount;
            }
            else
            {
                hit.m_damage.m_slash = amount;
            }

            // the attacker is the mob's body here, if it has one: the creature turns on it
            Body body;
            if (_bodies.TryGetValue(Messages.IntValue(json, "by", -1), out body) && body.Character != null)
            {
                hit.m_attacker = body.Character.GetZDOID();
            }

            victim.Damage(hit);
            HitsFromMinecraft++;
            Plugin.Log.LogInfo(string.Format("minecraft {0} hit {1}: {2:0.0} Valheim damage", Messages.StringValue(json, "src", "mob"), Name(victim), amount));
        }

        // ------------------------------------------------------------------ Minecraft's mobs -> bodies in Valheim

        private GameObject Prefab()
        {
            if (!_prefabLooked && ZNetScene.instance != null)
            {
                _prefabLooked = true;
                _prefab = ZNetScene.instance.GetPrefab(Plugin.MobBodyPrefab.Value);
                if (_prefab == null || _prefab.GetComponent<Character>() == null)
                {
                    Plugin.Log.LogWarning("Mobs.BodyPrefab: no creature prefab named " + Plugin.MobBodyPrefab.Value + "; trying Deer");
                    _prefab = ZNetScene.instance.GetPrefab("Deer");
                }
            }

            return _prefab;
        }

        /// <summary>{"t":"mobs"}: Minecraft's mobs near the player. New ones get a body, gone ones lose it, the rest move.</summary>
        public void OnMobs(string json, double yOffset)
        {
            if (!Plugin.MobsEnabled.Value || Player.m_localPlayer == null)
            {
                return;
            }

            double rate = Rate();
            var seen = new HashSet<int>();
            foreach (Messages.Mob m in Messages.Mobs(json))
            {
                seen.Add(m.Id);
                double ux, uy, uz;
                Mapping.FromMinecraft(m.X, m.Y, m.Z, yOffset, out ux, out uy, out uz);
                var at = new Vector3((float)ux, (float)uy, (float)uz);
                Body b;
                if (!_bodies.TryGetValue(m.Id, out b) || b.Character == null)
                {
                    b = Make(m, at);
                    if (b == null)
                    {
                        continue;
                    }

                    _bodies[m.Id] = b;
                }

                b.Target = at;
                // the body shows the mob's health in Valheim's terms (its health bar, when the Viking hits it)
                b.Character.SetMaxHealth((float)(m.MaxHealth * rate));
                b.Character.SetHealth(Mathf.Max(0.1f, (float)(m.Health * rate)));
            }

            _gone.Clear();
            foreach (KeyValuePair<int, Body> kv in _bodies)
            {
                if (!seen.Contains(kv.Key) || kv.Value.Character == null)
                {
                    _gone.Add(kv.Key);
                }
            }

            foreach (int id in _gone)
            {
                Destroy(_bodies[id]);
                _bodies.Remove(id);
            }
        }

        private Body Make(Messages.Mob m, Vector3 at)
        {
            GameObject prefab = Prefab();
            if (prefab == null)
            {
                return null;
            }

            // a body that is never saved: the prefab's ZNetView is made non-persistent for this one instantiation
            ZNetView view = prefab.GetComponent<ZNetView>();
            bool persistent = view != null && view.m_persistent;
            if (view != null)
            {
                view.m_persistent = false;
            }

            GameObject go;
            try
            {
                go = Object.Instantiate(prefab, at, Quaternion.identity);
            }
            finally
            {
                if (view != null)
                {
                    view.m_persistent = persistent;
                }
            }

            go.name = ObjectName;
            Character c = go.GetComponent<Character>();
            Bodies.Add(c);
            c.m_faction = Character.Faction.PlayerSpawned; // every Valheim creature's enemy, as the player is
            c.m_name = Pretty(m.Type);
            c.m_boss = false;
            c.m_damageModifiers = new HitData.DamageModifiers(); // none of the prefab's resistances: Minecraft has the mob's own
            BackstabTime(c) = float.MaxValue / 2f; // never "caught unaware": no backstab bonus on a body
            foreach (BaseAI ai in go.GetComponentsInChildren<BaseAI>())
            {
                ai.enabled = false;
            }

            foreach (CharacterDrop drop in go.GetComponentsInChildren<CharacterDrop>())
            {
                Object.Destroy(drop); // killing a body yields nothing in Valheim: the mob drops its loot in Minecraft
            }

            foreach (FootStep step in go.GetComponentsInChildren<FootStep>())
            {
                Object.Destroy(step);
            }

            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = false; // Minecraft draws the mob
            }

            foreach (Animator a in go.GetComponentsInChildren<Animator>())
            {
                a.enabled = false;
            }

            Rigidbody body = go.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = true;
                body.useGravity = false;
            }

            // the mob's size: its capsule (Minecraft's box, as a capsule) and its eyes near the top
            CapsuleCollider capsule = go.GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                Vector3 s = go.transform.lossyScale;
                float sx = Mathf.Max(0.01f, Mathf.Max(s.x, s.z)), sy = Mathf.Max(0.01f, s.y);
                float h = Mathf.Max((float)m.Height, (float)m.Width);
                capsule.direction = 1;
                capsule.radius = (float)m.Width * 0.5f / sx;
                capsule.height = h / sy;
                capsule.center = new Vector3(0f, h * 0.5f / sy, 0f);
            }

            if (c.m_eye != null)
            {
                c.m_eye.position = at + Vector3.up * (float)m.Height * 0.85f;
            }

            BodiesMade++;
            Plugin.Log.LogInfo(string.Format("minecraft {0} stands in Valheim at {1} ({2:0.0} x {3:0.0} m)", m.Type, at, m.Width, m.Height));
            return new Body { Character = c, Target = at, Type = m.Type };
        }

        /// <summary>"$enemy_greydwarf" -> "Greydwarf", for the log.</summary>
        private static string Name(Character c) => Pretty(c.m_name.StartsWith("$enemy_") ? c.m_name.Substring(7) : c.m_name);

        private static string Pretty(string type)
        {
            string s = type.Replace('_', ' ');
            return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s.Substring(1) : s;
        }

        private static void Destroy(Body b)
        {
            if (b.Character == null)
            {
                return;
            }

            Bodies.Remove(b.Character);
            ZNetView view = b.Character.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && ZNetScene.instance != null)
            {
                ZNetScene.instance.Destroy(b.Character.gameObject);
            }
            else
            {
                Object.Destroy(b.Character.gameObject);
            }
        }

        /// <summary>Minecraft went away or the world ended: every body goes.</summary>
        public void Clear()
        {
            foreach (Body b in _bodies.Values)
            {
                Destroy(b);
            }

            _bodies.Clear();
            Bodies.Clear();
            _sentNone = false; // tell a returning Minecraft afresh
        }

        private int MinecraftIdOf(Character c)
        {
            foreach (KeyValuePair<int, Body> kv in _bodies)
            {
                if (kv.Value.Character == c)
                {
                    return kv.Key;
                }
            }

            return -1;
        }

        // ------------------------------------------------------------------ the bodies' own behaviour

        /// <summary>A body has no physics, motion or death of its own in Valheim: it is placed where Minecraft says.</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.CustomFixedUpdate))]
        private static class NoMotionPatch
        {
            private static bool Prefix(Character __instance) => !IsBody(__instance);
        }

        /// <summary>A hit on a body (from a creature, or from the Viking's own weapon) goes to the Minecraft mob instead.</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
        private static class HitPatch
        {
            private static bool Prefix(Character __instance, HitData hit)
            {
                if (!IsBody(__instance))
                {
                    return true;
                }

                // what would hurt a creature: not the chop and pickaxe parts (those are for trees, rocks and buildings)
                Character attacker = hit.GetAttacker();
                float total = hit.GetTotalDamage() - hit.m_damage.m_chop - hit.m_damage.m_pickaxe;
                if (attacker == null || total <= 0f || IsBody(attacker))
                {
                    return false; // the world's own harm (falls, fire, water): the mob takes that in Minecraft, if anywhere
                }

                Plugin.Mobs.HitMob(__instance, attacker, total, hit.m_point);
                return false;
            }
        }

        private void HitMob(Character body, Character attacker, float total, Vector3 point)
        {
            int id = MinecraftIdOf(body);
            if (id < 0 || !Link.Connected)
            {
                return;
            }

            bool byPlayer = attacker == Player.m_localPlayer;
            double amount = total / Rate();
            Link.Send(Messages.MobHit(id, amount, byPlayer ? -1 : attacker.GetInstanceID(), byPlayer));
            HitsToMinecraft++;
            if (DamageText.instance != null)
            {
                DamageText.instance.ShowText(HitData.DamageModifier.Normal, point, total, byPlayer);
            }

            Plugin.Log.LogInfo(string.Format("{0} hit Minecraft's {1}: {2:0.0} Valheim damage = {3:0.0} Minecraft", byPlayer ? "the Viking" : Name(attacker), body.m_name, total, amount));
        }
    }
}
