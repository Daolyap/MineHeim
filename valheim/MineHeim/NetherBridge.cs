using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using MineHeim.Core;

namespace MineHeim
{
    /// <summary>
    /// Valheim's half of the Nether infection (mc/NetherInfection.java owns it: a lit Nether portal in Minecraft turns the
    /// land around it into a crust of Nether blocks and spawns Nether mobs there). Here the infected land also becomes
    /// Valheim's own hell, the Ashlands: its ground texture and its clutter, and its weather while the Viking stands in it.
    /// A pin marks the portal on the map. Valheim's wards go to Minecraft: nothing infects inside a ward.
    /// Nothing here is saved into the Valheim world: the ground's colour and the clutter are redrawn from the infection,
    /// and they go back to normal when it heals.
    /// </summary>
    internal sealed class NetherBridge
    {
        internal sealed class Zone
        {
            public int Id;
            public Vector3 Centre; // Valheim coordinates, the portal's ground
            public float Radius;
            public bool Lit;
            public int Columns, Cells;
            public Minimap.PinData Pin;
        }

        internal static readonly Dictionary<int, Zone> Zones = new Dictionary<int, Zone>();
        internal static int Mobs, Recolored, Reports;
        internal static string Env = "";
        internal static int PaintedNow => Originals.Count;
        private static readonly Color32 Ashlands = Heightmap.GetBiomeColor(Heightmap.Biome.AshLands);
        private static readonly AccessTools.FieldRef<Heightmap, Mesh> RenderMesh = AccessTools.FieldRefAccess<Heightmap, Mesh>("m_renderMesh");
        private static readonly AccessTools.FieldRef<EnvMan, string> ForcedEnv = AccessTools.FieldRefAccess<EnvMan, string>("m_forceEnv");
        private static readonly Dictionary<Heightmap, Saved> Originals = new Dictionary<Heightmap, Saved>();
        private static readonly Dictionary<int, float> _paintedRadius = new Dictionary<int, float>();
        private float _nextWards, _nextGrass;
        private string _lastWards = "";
        private bool _grassDirty;
        private string _envName;

        // ------------------------------------------------------------------ from Minecraft

        /// <summary>{"t":"infect","l":[[id,x,y,z,radius,lit,columns,cells,waiting],...],"mobs":n}.</summary>
        public void OnInfect(string json)
        {
            Reports++;
            Mobs = Messages.IntValue(json, "mobs", 0);
            List<double> n = Messages.Numbers(json, "l");
            var seen = new HashSet<int>();
            bool changed = false;
            for (int i = 0; i + 8 < n.Count; i += 9)
            {
                int id = (int)n[i];
                double ux, uy, uz;
                Mapping.FromMinecraft(n[i + 1], n[i + 2] + 1, n[i + 3], Plugin.YOffset.Value, out ux, out uy, out uz);
                seen.Add(id);
                Zone z;
                bool fresh = !Zones.TryGetValue(id, out z);
                if (fresh)
                {
                    z = new Zone { Id = id };
                    Zones[id] = z;
                }

                bool wasLit = z.Lit;
                z.Centre = new Vector3((float)ux, (float)uy, (float)uz);
                z.Radius = (float)n[i + 4];
                z.Lit = n[i + 5] > 0.5;
                z.Columns = (int)n[i + 6];
                z.Cells = (int)n[i + 7];
                if (fresh || wasLit != z.Lit)
                {
                    Announce(z, fresh);
                }

                float painted;
                if (!_paintedRadius.TryGetValue(id, out painted) || Mathf.Abs(painted - z.Radius) >= 0.75f)
                {
                    changed = true;
                }
            }

            foreach (int id in new List<int>(Zones.Keys))
            {
                if (!seen.Contains(id))
                {
                    Zone z = Zones[id];
                    if (z.Pin != null && Minimap.instance != null)
                    {
                        Minimap.instance.RemovePin(z.Pin);
                    }

                    Zones.Remove(id);
                    Plugin.Log.LogInfo("nether infection " + id + ": healed");
                    Message("The Nether's hold on the land is gone.");
                    changed = true;
                }
            }

            if (changed)
            {
                Repaint();
            }
        }

        private static void Announce(Zone z, bool fresh)
        {
            Plugin.Log.LogInfo(string.Format("nether infection {0}: {1} at {2}, radius {3:0.0}", z.Id, z.Lit ? (fresh ? "open" : "lit again") : "portal gone, healing", z.Centre, z.Radius));
            if (z.Lit && (!fresh || z.Radius < 4f))
            {
                // (an infection already grown is one from before: the world was loaded, not a portal lit just now)
                Message(fresh ? "A Nether portal has opened. The land around it is turning to Nether." : "The Nether portal burns again.");
            }
            else if (!fresh)
            {
                Message("The Nether portal is broken. The land begins to heal.");
            }

            if (z.Pin == null && Minimap.instance != null && Plugin.NetherMapPin.Value)
            {
                z.Pin = Minimap.instance.AddPin(z.Centre, Minimap.PinType.Icon4, "Nether portal", false, false);
            }
        }

        private static void Message(string text)
        {
            if (Player.m_localPlayer != null && MessageHud.instance != null)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, text);
            }
        }

        /// <summary>A Minecraft world started: what it says about its infections comes again; forget ours.</summary>
        public void Reset()
        {
            foreach (Zone z in Zones.Values)
            {
                if (z.Pin != null && Minimap.instance != null)
                {
                    Minimap.instance.RemovePin(z.Pin);
                }
            }

            Zones.Clear();
            _lastWards = "";
            Repaint();
            SendConfig();
        }

        internal static void SendConfig()
        {
            Link.Send(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{{\"t\":\"nethercfg\",\"on\":{0},\"max\":{1:0.##},\"grow\":{2:0.##},\"heal\":{3:0.##},\"mobs\":{4},\"every\":{5}}}",
                Plugin.NetherEnabled.Value ? "true" : "false", Plugin.NetherMaxRadius.Value, Plugin.NetherGrowPerMinute.Value, Plugin.NetherHealPerMinute.Value,
                Plugin.NetherMobCap.Value, Plugin.NetherSpawnSeconds.Value));
        }

        // ------------------------------------------------------------------ every frame

        public void Tick(Player player)
        {
            if (player == null)
            {
                return;
            }

            if (Time.unscaledTime >= _nextWards)
            {
                _nextWards = Time.unscaledTime + 2f;
                SendWards(player.transform.position);
            }

            if (_grassDirty && Time.unscaledTime >= _nextGrass && ClutterSystem.instance != null)
            {
                _grassDirty = false;
                _nextGrass = Time.unscaledTime + 4f;
                foreach (Zone z in Zones.Values)
                {
                    ClutterSystem.instance.ResetGrass(z.Centre, z.Radius + 6f);
                }
            }

            UpdateWeather(player.transform.position);
        }

        /// <summary>{"t":"wards","w":[x,z,radius,...]}: active wards within 200 m, in Minecraft coordinates, when they change.</summary>
        private void SendWards(Vector3 around)
        {
            var b = new System.Text.StringBuilder("{\"t\":\"wards\",\"w\":[");
            int n = 0;
            foreach (PrivateArea ward in WorldEvents.ActiveWards())
            {
                Vector3 p = ward.transform.position;
                if (Utils.DistanceXZ(p, around) > 200f)
                {
                    continue;
                }

                b.Append(n++ == 0 ? "" : ",").Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.##},{1:0.##},{2:0.##}", p.x, -p.z, ward.m_radius));
            }

            string message = b.Append("]}").ToString();
            if (message != _lastWards)
            {
                _lastWards = message;
                Link.Send(message);
            }
        }

        /// <summary>Inside a lit infection (well past its edge) the sky is the Ashlands', unless something else (a dungeon) already set one.</summary>
        private void UpdateWeather(Vector3 at)
        {
            EnvMan env = EnvMan.instance;
            if (env == null)
            {
                return;
            }

            bool inside = false;
            if (Plugin.NetherWeather.Value)
            {
                foreach (Zone z in Zones.Values)
                {
                    inside |= z.Lit && z.Radius >= 10f && Utils.DistanceXZ(at, z.Centre) < z.Radius - 4f;
                }
            }

            string forced = ForcedEnv(env) ?? "";
            string want = inside ? EnvName(env) : "";
            if (inside && want.Length > 0 && forced.Length == 0)
            {
                env.SetForceEnvironment(want);
                Env = want;
            }
            else if (!inside && Env.Length > 0 && forced == Env)
            {
                env.SetForceEnvironment("");
                Env = "";
            }
        }

        /// <summary>The configured environment, or the first of Valheim's whose name says Ashlands.</summary>
        private string EnvName(EnvMan env)
        {
            if (_envName != null)
            {
                return _envName;
            }

            string configured = Plugin.NetherEnvironment.Value.Trim();
            _envName = "";
            foreach (EnvSetup e in env.m_environments)
            {
                if (configured.Length > 0 && configured != "auto" ? e.m_name == configured : e.m_name.IndexOf("ashlands", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _envName = e.m_name;
                    break;
                }
            }

            Plugin.Log.LogInfo("nether weather: environment '" + _envName + "' (" + env.m_environments.Count + " known)");
            return _envName;
        }

        /// <summary>Every environment's name (test hook "envs").</summary>
        internal static string EnvNames()
        {
            var names = new List<string>();
            if (EnvMan.instance != null)
            {
                foreach (EnvSetup e in EnvMan.instance.m_environments)
                {
                    names.Add(e.m_name);
                }
            }

            return string.Join(", ", names.ToArray());
        }

        // ------------------------------------------------------------------ the ground's colour (Valheim's terrain shader picks textures by vertex colour)

        /// <summary>0..1: how far this point is into an infection's land (fading over the last 3 m of its edge).</summary>
        internal static float Weight(Vector3 p)
        {
            float w = 0f;
            foreach (Zone z in Zones.Values)
            {
                if (z.Radius > 0.5f)
                {
                    w = Mathf.Max(w, Mathf.Clamp01((z.Radius - Utils.DistanceXZ(p, z.Centre)) / 3f));
                }
            }

            return w;
        }

        private static void Repaint()
        {
            _paintedRadius.Clear();
            foreach (Zone z in Zones.Values)
            {
                _paintedRadius[z.Id] = z.Radius;
            }

            foreach (Heightmap hm in Heightmap.GetAllHeightmaps())
            {
                if (hm != null && (Originals.ContainsKey(hm) || Overlaps(hm)))
                {
                    Paint(hm);
                }
            }

            if (Plugin.Nether != null)
            {
                Plugin.Nether._grassDirty = true;
            }
        }

        private static bool Overlaps(Heightmap hm)
        {
            float half = hm.m_width * hm.m_scale * 0.5f + 1f;
            Vector3 c = hm.transform.position;
            foreach (Zone z in Zones.Values)
            {
                if (Mathf.Abs(z.Centre.x - c.x) < half + z.Radius && Mathf.Abs(z.Centre.z - c.z) < half + z.Radius)
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class Saved
        {
            public Color32[] Colours;
            public Color[] Mask;
        }

        /// <summary>
        /// Paint one heightmap: the Ashlands' colour blended into its vertex colours by Weight, and its paint mask's
        /// vegetation channel (which the Ashlands' shader shows as lava where it is high) set low for ash, high along thin
        /// cracks. What was there is kept and put back where no infection reaches any more.
        /// </summary>
        private static void Paint(Heightmap hm)
        {
            Mesh mesh = RenderMesh(hm);
            Texture2D mask = PaintMask(hm);
            Saved saved;
            bool had = Originals.TryGetValue(hm, out saved);
            if (mesh == null || mask == null || !Plugin.NetherAshGround.Value && !had)
            {
                return;
            }

            if (!had)
            {
                saved = new Saved { Colours = mesh.colors32, Mask = mask.GetPixels() };
                if (saved.Colours == null || saved.Colours.Length == 0)
                {
                    return;
                }
            }

            Vector3[] vertices = mesh.vertices;
            if (vertices.Length != saved.Colours.Length || saved.Mask.Length != mask.width * mask.height)
            {
                Originals.Remove(hm);
                return;
            }

            bool on = Plugin.NetherAshGround.Value;
            var colours = new Color32[saved.Colours.Length];
            bool any = false;
            Vector3 origin = hm.transform.position;
            for (int i = 0; i < vertices.Length; i++)
            {
                float w = on ? Weight(origin + vertices[i]) : 0f;
                any |= w > 0f;
                colours[i] = w > 0f ? Color32.Lerp(saved.Colours[i], Ashlands, w) : saved.Colours[i];
            }

            var pixels = (Color[])saved.Mask.Clone();
            for (int y = 0; y < mask.height; y++)
            {
                for (int x = 0; x < mask.width; x++)
                {
                    Vector3 p = hm.VertexMaskToWorld(x, y);
                    float w = on ? Weight(p) : 0f;
                    if (w > 0f)
                    {
                        Color c = pixels[y * mask.width + x];
                        c.a = Mathf.Lerp(c.a, Lava(p), w);
                        pixels[y * mask.width + x] = c;
                    }
                }
            }

            mesh.colors32 = colours;
            mask.SetPixels(pixels);
            mask.Apply();
            if (any)
            {
                if (!had)
                {
                    Originals[hm] = saved;
                    Recolored++;
                }
            }
            else
            {
                Originals.Remove(hm);
            }
        }

        /// <summary>The paint mask's vegetation value for infected ground: ash (low) with thin lava cracks (high), more lava at the portal.</summary>
        private static float Lava(Vector3 p)
        {
            float n = Mathf.PerlinNoise(p.x * 0.09f + 311f, p.z * 0.09f + 97f);
            float crack = 1f - Mathf.Clamp01(Mathf.Abs(n - 0.5f) / 0.035f); // a thin band through the noise's middle
            foreach (Zone z in Zones.Values)
            {
                if (z.Lit)
                {
                    crack = Mathf.Max(crack, Mathf.Clamp01(1f - Utils.DistanceXZ(p, z.Centre) / 3.5f));
                }
            }

            return Mathf.Lerp(0.08f, 0.95f, crack);
        }

        private static readonly AccessTools.FieldRef<Heightmap, Texture2D> PaintMaskRef = AccessTools.FieldRefAccess<Heightmap, Texture2D>("m_paintMask");

        private static Texture2D PaintMask(Heightmap hm) => PaintMaskRef(hm);

        /// <summary>Valheim regenerated a heightmap (it streamed in, the ground was dug or painted): colours and mask are fresh, paint again.</summary>
        [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.Regenerate))]
        private static class RegeneratePatch
        {
            private static readonly System.Reflection.FieldInfo Request = AccessTools.Field(typeof(Heightmap), "m_regenRequest");

            // a full rebuild makes fresh vertex colours too; a paint-only one (a hoe, a path) only a fresh mask
            private static void Prefix(Heightmap __instance, out bool __state)
            {
                __state = Request == null || Request.GetValue(__instance).ToString() == "Full";
            }

            private static void Postfix(Heightmap __instance, bool __state)
            {
                Saved saved;
                if (Originals.TryGetValue(__instance, out saved))
                {
                    if (__state)
                    {
                        Originals.Remove(__instance);
                    }
                    else if (PaintMask(__instance) != null)
                    {
                        saved.Mask = PaintMask(__instance).GetPixels();
                    }
                }

                if (Zones.Count > 0 && Overlaps(__instance))
                {
                    Paint(__instance);
                }
            }
        }

        [HarmonyPatch(typeof(Heightmap), "OnDestroy")]
        private static class ForgetPatch
        {
            private static void Prefix(Heightmap __instance)
            {
                Originals.Remove(__instance);
            }
        }

        // ------------------------------------------------------------------ clutter: the Ashlands' instead of the meadow's grass

        private static bool Infected(Vector3 p)
        {
            return Zones.Count > 0 && Plugin.NetherAshGround.Value && Weight(p) >= 0.5f;
        }

        [HarmonyPatch(typeof(ClutterSystem), nameof(ClutterSystem.GetGroundInfo))]
        private static class GroundInfoPatch
        {
            private static void Postfix(bool __result, ref Vector3 point, ref Heightmap.Biome biome)
            {
                if (__result && Infected(point))
                {
                    biome = Heightmap.Biome.AshLands;
                }
            }
        }

        [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.FindBiomeClutter))]
        private static class PatchBiomePatch
        {
            private static void Postfix(Vector3 point, ref Heightmap.Biome __result)
            {
                // a patch's biomes are its corners' together: a corner on infected land lets the Ashlands' clutter in
                if (__result != Heightmap.Biome.None && Infected(point))
                {
                    __result = Heightmap.Biome.AshLands;
                }
            }
        }
    }
}
