using System.Collections.Generic;
using UnityEngine;
using MineHeim.Core;

namespace MineHeim
{
    /// <summary>
    /// Step 8: what happens in Minecraft happens in Valheim.
    ///   explosion   TNT, creepers, ...: an area hit on every Valheim character in reach (damage and knockback fall off with distance)
    ///   melee       a sword swing hurts the creatures in front of the player; a pickaxe or axe damages the rock or tree in front of him
    ///   proj        arrows and firework rockets in flight are traced through Valheim's world: through a creature (damage, the arrow is
    ///               gone), into the ground or a rock (the arrow sticks), reported back as "projhit"
    ///   pteleport   an ender pearl moves the Viking
    /// Damage goes through Valheim's own HitData and IDestructible.Damage, so armour, resistances and AI reactions apply.
    /// </summary>
    internal sealed class WorldEvents
    {
        private static readonly int ProjectileMask = LayerMask.GetMask("character", "character_net", "Default", "static_solid", "Default_small", "piece", "terrain");
        private static readonly int ToolMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");

        private readonly Dictionary<int, Vector3> _arrows = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, float> _arrowSeen = new Dictionary<int, float>();
        private readonly RaycastHit[] _hits = new RaycastHit[24];
        private readonly List<int> _stale = new List<int>();

        public int Explosions, Swings, ProjectileHits, ToolHits, StructureHits, Craters, Digs, MobHits;

        private static readonly int StructureMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "vehicle");
        private static readonly int TerrainMask = LayerMask.GetMask("terrain");
        private readonly Collider[] _overlap = new Collider[256];
        private GameObject _digPrefab;
        private bool _digLooked;

        /// <summary>The terrain operation a Valheim pickaxe leaves where it hits the ground (it respects wards and no-build areas).</summary>
        private GameObject DigPrefab()
        {
            if (!_digLooked && ObjectDB.instance != null)
            {
                _digLooked = true;
                foreach (string name in new[] { "PickaxeIron", "PickaxeBronze", "PickaxeAntler" })
                {
                    GameObject item = ObjectDB.instance.GetItemPrefab(name);
                    ItemDrop drop = item != null ? item.GetComponent<ItemDrop>() : null;
                    if (drop != null && drop.m_itemData.m_shared.m_spawnOnHitTerrain != null)
                    {
                        _digPrefab = drop.m_itemData.m_shared.m_spawnOnHitTerrain;
                        Plugin.Log.LogInfo("terrain digging uses " + name + "'s " + _digPrefab.name);
                        break;
                    }
                }

                if (_digPrefab == null)
                {
                    Plugin.Log.LogWarning("no pickaxe terrain operation found: digging and craters are off");
                }
            }

            return _digPrefab;
        }

        private bool Dig(Vector3 point)
        {
            GameObject prefab = DigPrefab();
            Player player = Player.m_localPlayer;
            if (prefab == null || player == null)
            {
                return false;
            }

            if (Attack.SpawnOnHitTerrain(point, prefab, player, 0f, null, null, true) != null)
            {
                return true;
            }

            // say which of Valheim's own rules refused it (the same ones a Valheim pickaxe obeys)
            TerrainOp op = prefab.GetComponentInChildren<TerrainOp>();
            float radius = op != null ? op.GetRadius() : 0f;
            Plugin.Log.LogInfo(string.Format("dig refused at {0}: ward access {1}, no-build location {2}",
                point, PrivateArea.CheckAccess(point, radius, false) ? "ok" : "DENIED", Location.IsInsideNoBuildLocation(point) ? "YES" : "no"));
            return false;
        }

        public bool Handle(string type, string json)
        {
            switch (type)
            {
                case "explosion":
                    OnExplosion(json);
                    return true;
                case "melee":
                    OnMelee(Messages.StringValue(json, "item", "diamond_sword"), Messages.DoubleValue(json, "dmg", -1));
                    return true;
                case "chit":
                    Plugin.Mobs.OnCreatureHit(json);
                    return true;
                case "mobs":
                    Plugin.Mobs.OnMobs(json, Plugin.YOffset.Value);
                    return true;
                case "proj":
                    OnProjectiles(json);
                    return true;
                case "pteleport":
                    OnTeleport(json);
                    return true;
                case "shield":
                    ShieldBlock.Up = Messages.BoolValue(json, "up", false);
                    return true;
                case "hurt":
                    OnHurt(json);
                    return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ Minecraft mobs hurting Steve (Valheim owns health)

        private void OnHurt(string json)
        {
            Player player = Player.m_localPlayer;
            double x, y, z;
            if (player == null || player.IsDead())
            {
                return;
            }

            // dynamic: the same share of the Viking's max health as of Steve's 20 in Minecraft, so food and progression matter
            float amount = (float)Messages.DoubleValue(json, "amount", 0) / 20f * player.GetMaxHealth() * Plugin.MinecraftDamageShare.Value;
            if (amount <= 0f)
            {
                return;
            }

            Vector3 dir = -player.transform.forward;
            if (Messages.Pos(json, out x, out y, out z))
            {
                double ux, uy, uz;
                Mapping.FromMinecraft(x, y, z, Plugin.YOffset.Value, out ux, out uy, out uz);
                dir = player.GetCenterPoint() - new Vector3((float)ux, (float)uy, (float)uz);
            }

            HitData hit = MakeHit(player.GetCenterPoint(), dir, 15f);
            hit.m_damage.m_blunt = amount;
            hit.m_blockable = true; // a raised shield (Minecraft's, or the Viking's own) can stop it
            hit.m_dodgeable = true;
            player.Damage(hit);
            MobHits++;
            Plugin.Log.LogInfo(string.Format("{0} hit Steve: {1:0.0} Valheim damage", Messages.StringValue(json, "src", "a Minecraft mob"), amount));
        }

        // ------------------------------------------------------------------ explosions

        private void OnExplosion(string json)
        {
            double x, y, z, radius;
            string source;
            if (!Messages.Explosion(json, out x, out y, out z, out radius, out source))
            {
                return;
            }

            double ux, uy, uz;
            Mapping.FromMinecraft(x, y, z, Plugin.YOffset.Value, out ux, out uy, out uz);
            Explode(new Vector3((float)ux, (float)uy, (float)uz), (float)radius, 1f, source);
        }

        /// <summary>An area hit: full strength at the centre, none at radius x ExplosionReach metres.</summary>
        public void Explode(Vector3 center, float radiusBlocks, float strength, string source)
        {
            Explosions++;
            float reach = Mathf.Max(1f, radiusBlocks) * Plugin.ExplosionReach.Value;
            int hurt = 0;
            foreach (Character c in new List<Character>(Character.GetAllCharacters()))
            {
                if (c == null || c.IsDead() || (c.IsPlayer() && !Plugin.HurtPlayer.Value) || MobBridge.IsBody(c))
                {
                    continue; // (a Minecraft mob's body: Minecraft's own blast already hit the mob)
                }

                Vector3 target = c.m_eye != null ? c.m_eye.position : c.transform.position + Vector3.up;
                float d = Vector3.Distance(center, target);
                if (d > reach)
                {
                    continue;
                }

                float falloff = 1f - d / reach;
                Vector3 away = target - center;
                away.y = Mathf.Max(away.y, 0.25f * away.magnitude); // lifted a little, as a blast does
                HitData hit = MakeHit(target, away, Plugin.ExplosionPush.Value * falloff);
                hit.m_damage.m_blunt = Plugin.ExplosionDamage.Value * falloff * strength;
                hit.m_staggerMultiplier = 2f;
                c.Damage(hit);
                hurt++;
            }

            int structures = ExplodeStructures(center, reach, strength);
            int craters = ExplodeTerrain(center, reach, radiusBlocks);
            Plugin.Log.LogInfo(string.Format("explosion ({0}, radius {1:0.0}) at {2}: {3} character(s), {4} building piece(s)/tree(s)/rock(s), {5} terrain dig(s) in reach {6:0.0} m",
                source, radiusBlocks, center, hurt, structures, craters, reach));
        }

        /// <summary>Buildings take blunt, trees chop, rocks and ore pickaxe damage: each through its own Damage, so materials, wards' rules and drops apply.</summary>
        private int ExplodeStructures(Vector3 center, float reach, float strength)
        {
            float damage = Plugin.ExplosionStructureDamage.Value * strength;
            if (damage <= 0f)
            {
                return 0;
            }

            int n = Physics.OverlapSphereNonAlloc(center, reach, _overlap, StructureMask, QueryTriggerInteraction.Ignore);
            var seen = new HashSet<IDestructible>();
            Player player = Player.m_localPlayer;
            for (int i = 0; i < n; i++)
            {
                Collider col = _overlap[i];
                if (col == null || col.name == BlockColliders.ObjectName)
                {
                    continue;
                }

                IDestructible target = col.GetComponentInParent<IDestructible>();
                if (target == null || target is Character || !seen.Add(target))
                {
                    continue;
                }

                Vector3 at = col.bounds.ClosestPoint(center);
                float falloff = 1f - Vector3.Distance(center, at) / reach;
                if (falloff <= 0f)
                {
                    continue;
                }

                HitData hit = MakeHit(at, at - center, 0f);
                hit.m_toolTier = 4;
                float d = damage * falloff;
                if (target is WearNTear)
                {
                    hit.m_damage.m_blunt = d;
                }
                else if (target is TreeBase || target is TreeLog || target.GetDestructibleType() == DestructibleType.Tree)
                {
                    hit.m_damage.m_chop = d;
                }
                else
                {
                    hit.m_damage.m_pickaxe = d; // rocks, ore, MineRock5; Destructible applies its own resistances
                    hit.m_damage.m_blunt = d;
                }

                if (player != null)
                {
                    hit.m_attacker = player.GetZDOID(); // Steve's TNT: the drops and the credit are the player's
                }

                target.Damage(hit);
                StructureHits++;
            }

            return seen.Count;
        }

        /// <summary>A crater where the blast meets Valheim's ground: the pickaxe's dig at the centre (twice, it is deepest there) and in a ring.</summary>
        private int ExplodeTerrain(Vector3 center, float reach, float radiusBlocks)
        {
            RaycastHit ground;
            if (!Plugin.ExplosionCraters.Value || !Physics.Raycast(center + Vector3.up * 2f, Vector3.down, out ground, 2f + reach * 0.5f, TerrainMask))
            {
                return 0;
            }

            int dug = 0;
            float ring = Mathf.Clamp(radiusBlocks * 0.4f, 0.8f, 3f);
            if (Dig(ground.point))
            {
                dug++;
                dug += Dig(ground.point) ? 1 : 0;
            }

            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3f;
                Vector3 p = ground.point + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ring;
                RaycastHit there;
                if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out there, 6f, TerrainMask) && Dig(there.point))
                {
                    dug++;
                }
            }

            Craters += dug > 0 ? 1 : 0;
            return dug;
        }

        // ------------------------------------------------------------------ swings

        /// <summary>
        /// What a Minecraft hit of `minecraftDamage` points deals in Valheim: the exchange rate (the Viking's max health / 20)
        /// times Events.WeaponDamageScale; `fallback` (the old fixed config value) if Minecraft did not report its damage.
        /// </summary>
        private static float Dynamic(double minecraftDamage, float fallback)
        {
            return minecraftDamage < 0 ? fallback : (float)(minecraftDamage * MobBridge.Rate() * Plugin.WeaponDamageScale.Value);
        }

        private void OnMelee(string item, double minecraftDamage)
        {
            Player player = Player.m_localPlayer;
            if (player == null || GameCamera.instance == null)
            {
                return;
            }

            Swings++;
            Vector3 eye = player.m_eye.position;
            Vector3 aim = GameCamera.instance.transform.forward;
            if (item.EndsWith("_sword"))
            {
                int hurt = 0;
                float damage = Dynamic(minecraftDamage, Plugin.MeleeDamage.Value);
                foreach (Character c in new List<Character>(Character.GetAllCharacters()))
                {
                    if (c == null || c == player || c.IsDead() || MobBridge.IsBody(c))
                    {
                        continue; // (a Minecraft mob: Steve's sword hits it in Minecraft itself)
                    }

                    // the nearest point of the creature, not its eyes: a Troll's head is 3 m above the Viking's
                    Vector3 target;
                    float d = DistanceTo(c, eye, out target);
                    Vector3 to = target - eye;
                    if (d > Plugin.MeleeReach.Value || Vector3.Dot(to / Mathf.Max(d, 0.01f), aim) < 0.3f)
                    {
                        continue;
                    }

                    HitData hit = MakeHit(target, new Vector3(aim.x, 0.1f, aim.z), 25f);
                    hit.m_damage.m_slash = damage;
                    hit.m_attacker = player.GetZDOID();
                    c.Damage(hit);
                    hurt++;
                }

                Plugin.Log.LogInfo(string.Format("sword swing: {0} creature(s) hit for {1:0.0} ({2:0.0} in Minecraft)", hurt, damage, minecraftDamage));
                return;
            }

            // a pickaxe or an axe: the rock, ore or tree in front of the player
            // along the aim and angled down toward the feet (rocks and stumps are low), with a fat ray
            float best = float.MaxValue;
            IDestructible target2 = null;
            Vector3 point = Vector3.zero;
            Vector3 right = Vector3.Cross(Vector3.up, aim).normalized;
            foreach (float downDegrees in new[] { 0f, 20f, 40f })
            {
                Vector3 dir = Quaternion.AngleAxis(downDegrees, right) * aim;
                int n = Physics.SphereCastNonAlloc(eye, 0.5f, dir, _hits, Plugin.MeleeReach.Value + 0.6f, ToolMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    if (_hits[i].collider.name == BlockColliders.ObjectName || _hits[i].distance >= best)
                    {
                        continue;
                    }

                    IDestructible found = _hits[i].collider.GetComponentInParent<IDestructible>();
                    if (found == null)
                    {
                        continue; // plain terrain and rocks without a health: keep looking
                    }

                    best = _hits[i].distance;
                    target2 = found;
                    point = _hits[i].distance <= 0f ? eye + dir : _hits[i].point;
                }
            }

            if (target2 == null)
            {
                // nothing to chop or mine: a pickaxe digs the ground it points at, like a Valheim pickaxe
                if (item.EndsWith("_pickaxe") && Plugin.PickaxeDigs.Value)
                {
                    foreach (float downDegrees in new[] { 0f, 20f, 40f })
                    {
                        Vector3 dir = Quaternion.AngleAxis(downDegrees, right) * aim;
                        RaycastHit ground;
                        if (Physics.Raycast(eye, dir, out ground, Plugin.MeleeReach.Value + 0.6f, TerrainMask) && ground.collider.GetComponent<Heightmap>() != null)
                        {
                            bool dug = Dig(ground.point);
                            Digs += dug ? 1 : 0;
                            Plugin.Log.LogInfo(item + " swing: " + (dug ? "dug the ground at " + ground.distance.ToString("0.0") + " m" : "the ground here cannot be dug (ward or no-build area)"));
                            return;
                        }
                    }
                }

                Plugin.Log.LogInfo(item + " swing: nothing destructible in reach");
                return;
            }

            var tool = MakeHit(point, aim, 0f);
            tool.m_toolTier = 4; // a diamond tool: nothing in the world is too hard for it
            if (item.EndsWith("_pickaxe"))
            {
                tool.m_damage.m_pickaxe = Plugin.ToolDamage.Value;
            }
            else
            {
                tool.m_damage.m_chop = Plugin.ToolDamage.Value;
            }

            tool.m_attacker = player.GetZDOID();
            target2.Damage(tool);
            ToolHits++;
            Plugin.Log.LogInfo(item + " swing hit a " + target2.GetDestructibleType() + " at " + best.ToString("0.0") + " m");
        }

        // ------------------------------------------------------------------ projectiles

        private void OnProjectiles(string json)
        {
            float now = Time.unscaledTime;
            foreach (Messages.Projectile p in Messages.Projectiles(json))
            {
                double ux, uy, uz;
                Mapping.FromMinecraft(p.X, p.Y, p.Z, Plugin.YOffset.Value, out ux, out uy, out uz);
                var at = new Vector3((float)ux, (float)uy, (float)uz);
                _arrowSeen[p.Id] = now;
                Vector3 before;
                if (!_arrows.TryGetValue(p.Id, out before))
                {
                    _arrows[p.Id] = at;
                    continue;
                }

                _arrows[p.Id] = at;
                Vector3 seg = at - before;
                float len = seg.magnitude;
                if (len < 0.01f)
                {
                    continue;
                }

                int n = Physics.SphereCastNonAlloc(before, 0.15f, seg / len, _hits, len, ProjectileMask, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                RaycastHit chosen = default(RaycastHit);
                bool found = false;
                for (int i = 0; i < n; i++)
                {
                    Collider col = _hits[i].collider;
                    if (col.name == BlockColliders.ObjectName || _hits[i].distance >= best)
                    {
                        continue;
                    }

                    Character owner = col.GetComponentInParent<Character>();
                    if (owner != null && (owner == Player.m_localPlayer || MobBridge.IsBody(owner)))
                    {
                        continue; // not the Viking's own body, nor a Minecraft mob's (the arrow meets that mob in Minecraft)
                    }

                    best = _hits[i].distance;
                    chosen = _hits[i];
                    found = true;
                }

                if (!found)
                {
                    continue;
                }

                Character victim = chosen.collider.GetComponentInParent<Character>();
                Vector3 hitPoint = chosen.distance <= 0f ? before : chosen.point;
                if (victim != null && !victim.IsDead())
                {
                    HitData hit = MakeHit(hitPoint, seg, 12f);
                    float damage = Dynamic(p.Damage, Plugin.ArrowDamage.Value);
                    hit.m_damage.m_pierce = damage;
                    if (Player.m_localPlayer != null)
                    {
                        hit.m_attacker = Player.m_localPlayer.GetZDOID();
                    }

                    victim.Damage(hit);
                    Plugin.Log.LogInfo(string.Format("arrow hit {0} for {1:0.0} ({2:0.0} in Minecraft)", victim.m_name, damage, p.Damage));
                }
                else if (p.Kind == "firework")
                {
                    Explode(hitPoint, 3f, 0.5f, "firework");
                }

                ProjectileHits++;
                double mx, my, mz;
                Mapping.Position(hitPoint.x, hitPoint.y, hitPoint.z, Plugin.YOffset.Value, out mx, out my, out mz);
                Link.Send(Messages.ProjectileHit(p.Id, mx, my, mz, victim == null));
                _arrows.Remove(p.Id);
            }

            // projectiles Minecraft stopped reporting (landed, despawned): forget them
            _stale.Clear();
            foreach (KeyValuePair<int, float> kv in _arrowSeen)
            {
                if (now - kv.Value > 2f)
                {
                    _stale.Add(kv.Key);
                }
            }

            foreach (int id in _stale)
            {
                _arrowSeen.Remove(id);
                _arrows.Remove(id);
            }
        }

        // ------------------------------------------------------------------ ender pearls

        private void OnTeleport(string json)
        {
            double x, y, z;
            Player player = Player.m_localPlayer;
            if (player == null || !Messages.Pos(json, out x, out y, out z))
            {
                return;
            }

            double ux, uy, uz;
            Mapping.FromMinecraft(x, y, z, Plugin.YOffset.Value, out ux, out uy, out uz);
            player.TeleportTo(new Vector3((float)ux, (float)uy + 0.2f, (float)uz), player.transform.rotation, false);
            Plugin.Log.LogInfo("ender pearl: teleported to " + new Vector3((float)ux, (float)uy, (float)uz));
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Distance from `from` to the nearest point of the character's colliders (axis-aligned bounds), and that point.</summary>
        private static float DistanceTo(Character c, Vector3 from, out Vector3 closest)
        {
            float best = float.MaxValue;
            closest = c.transform.position + Vector3.up;
            foreach (Collider col in c.GetComponentsInChildren<Collider>())
            {
                if (!col.enabled || col.isTrigger)
                {
                    continue;
                }

                Vector3 p = col.bounds.ClosestPoint(from);
                float d = Vector3.Distance(p, from);
                if (d < best)
                {
                    best = d;
                    closest = p;
                }
            }

            return best == float.MaxValue ? Vector3.Distance(closest, from) : best;
        }

        private static HitData MakeHit(Vector3 point, Vector3 dir, float push)
        {
            var hit = new HitData();
            hit.m_point = point;
            hit.m_dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.up;
            hit.m_pushForce = push;
            return hit;
        }
    }
}
