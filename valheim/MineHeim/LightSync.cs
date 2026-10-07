using System.Collections.Generic;
using UnityEngine;
using MineHeim.Core;

namespace MineHeim
{
    /// <summary>
    /// One light for both worlds:
    ///   time    Minecraft's sun follows Valheim's day (its sky light, and so its blocks, dim at dusk and go dark at night)
    ///   fires   Valheim's burning fires, hearths and torches become Minecraft light blocks (invisible, they only give light)
    ///   lamps   Minecraft's torches, lanterns, glowstone and lava become Unity point lights that light Valheim's world
    /// </summary>
    internal sealed class LightSync
    {
        public const string ObjectName = "vc_light";
        private static readonly Color Warm = new Color(1f, 0.78f, 0.5f);

        private float _nextTime, _nextFires, _nextCull;
        private int _sentTicks = int.MinValue;

        // fires sent to Minecraft: cell -> level
        private readonly Dictionary<long, int> _fires = new Dictionary<long, int>();
        private readonly Dictionary<long, int> _wanted = new Dictionary<long, int>();
        private readonly List<int> _add = new List<int>(), _remove = new List<int>();

        // Minecraft's lamps: cell -> Unity light
        private readonly Dictionary<long, Light> _lamps = new Dictionary<long, Light>();
        private readonly List<KeyValuePair<float, Light>> _byDistance = new List<KeyValuePair<float, Light>>();
        private Transform _root;

        public int FiresSent => _fires.Count;
        public int Lamps => _lamps.Count;
        public int LampsLit;
        public int Ticks => _sentTicks;

        /// <summary>A new Minecraft world: it holds none of our light blocks and none of its lamps are known yet.</summary>
        public void Reset()
        {
            _fires.Clear();
            _sentTicks = int.MinValue;
            ClearLamps();
        }

        public void ClearLamps()
        {
            foreach (Light l in _lamps.Values)
            {
                if (l != null)
                {
                    Object.Destroy(l.gameObject);
                }
            }

            _lamps.Clear();
            LampsLit = 0;
        }

        public void Tick(Player player, double yOffset)
        {
            float now = Time.unscaledTime;
            if (player != null && Link.Connected && now >= _nextTime)
            {
                _nextTime = now + 2f;
                SyncTime();
            }

            if (player != null && Link.Connected && now >= _nextFires)
            {
                _nextFires = now + 2f;
                SyncFires(yOffset);
            }

            if (now >= _nextCull)
            {
                _nextCull = now + 0.5f;
                CullLamps(player);
            }
        }

        // ------------------------------------------------------------------ time of day

        private void SyncTime()
        {
            int ticks;
            if (Plugin.TimeSync.Value && EnvMan.instance != null)
            {
                ticks = Messages.DayTicks(EnvMan.instance.GetDayFraction());
            }
            else
            {
                ticks = 6000; // always noon: Minecraft's blocks stay fully lit
            }

            int diff = Mathf.Abs(ticks - _sentTicks);
            if (_sentTicks == int.MinValue || Mathf.Min(diff, 24000 - diff) >= 20)
            {
                Link.Send(Messages.DayTime(ticks));
                _sentTicks = ticks;
            }
        }

        // ------------------------------------------------------------------ Valheim's fires -> Minecraft light blocks

        private void SyncFires(double yOffset)
        {
            _wanted.Clear();
            if (Plugin.FireLights.Value)
            {
                foreach (KeyValuePair<Fireplace, Light> f in Plugin.Pieces.Fires())
                {
                    if (f.Key == null || !f.Key.IsBurning())
                    {
                        continue;
                    }

                    // the flame's light if the fire has one (a torch's sits at its top), else a metre above the fire
                    Vector3 at = f.Value != null ? f.Value.transform.position : f.Key.transform.position + Vector3.up;
                    int level = f.Value != null ? Mathf.Clamp(Mathf.RoundToInt(f.Value.range * 1.5f), 12, 15) : 15;
                    double mx, my, mz;
                    Mapping.Position(at.x, at.y, at.z, yOffset, out mx, out my, out mz);
                    _wanted[BarrierCells.Key((int)System.Math.Floor(mx), (int)System.Math.Floor(my), (int)System.Math.Floor(mz))] = level;
                }
            }

            _add.Clear();
            _remove.Clear();
            foreach (KeyValuePair<long, int> kv in _wanted)
            {
                int old;
                if (!_fires.TryGetValue(kv.Key, out old) || old != kv.Value)
                {
                    int x, y, z;
                    BarrierCells.Unpack(kv.Key, out x, out y, out z);
                    _add.AddRange(new[] { x, y, z, kv.Value });
                }
            }

            foreach (long key in _fires.Keys)
            {
                if (!_wanted.ContainsKey(key))
                {
                    int x, y, z;
                    BarrierCells.Unpack(key, out x, out y, out z);
                    _remove.AddRange(new[] { x, y, z });
                }
            }

            if (_add.Count + _remove.Count > 0)
            {
                Link.Send(Messages.ValheimLights(_add, _remove));
                _fires.Clear();
                foreach (KeyValuePair<long, int> kv in _wanted)
                {
                    _fires[kv.Key] = kv.Value;
                }

                Plugin.Log.LogInfo(string.Format("fire lights: {0} lit in Minecraft ({1} new, {2} out)", _fires.Count, _add.Count / 4, _remove.Count / 3));
            }
        }

        // ------------------------------------------------------------------ Minecraft's lamps -> Unity lights

        /// <summary>A "mclights" message: {"set":[x,y,z,level,...],"clear":[x,y,z,...]}.</summary>
        public void Enqueue(string json, double yOffset)
        {
            List<int> set = Messages.IntArray(json, "set"), clear = Messages.IntArray(json, "clear");
            for (int i = 0; i + 2 < clear.Count; i += 3)
            {
                long key = BarrierCells.Key(clear[i], clear[i + 1], clear[i + 2]);
                Light l;
                if (_lamps.TryGetValue(key, out l))
                {
                    if (l != null)
                    {
                        Object.Destroy(l.gameObject);
                    }

                    _lamps.Remove(key);
                }
            }

            for (int i = 0; i + 3 < set.Count; i += 4)
            {
                long key = BarrierCells.Key(set[i], set[i + 1], set[i + 2]);
                int level = Mathf.Clamp(set[i + 3], 1, 15);
                Light l;
                if (!_lamps.TryGetValue(key, out l) || l == null)
                {
                    if (_root == null)
                    {
                        _root = new GameObject("mineheim_lights").transform;
                    }

                    double ux, uy, uz;
                    Mapping.FromMinecraft(set[i] + 0.5, set[i + 1] + 0.5, set[i + 2] + 0.5, yOffset, out ux, out uy, out uz);
                    var go = new GameObject(ObjectName);
                    go.transform.SetParent(_root, false);
                    go.transform.position = new Vector3((float)ux, (float)uy, (float)uz);
                    l = go.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = Warm;
                    l.shadows = LightShadows.None;
                    l.enabled = false; // the cull turns on the nearest ones
                    _lamps[key] = l;
                }

                // a Minecraft light level falls by one per block: level 14 (a torch) still lights ~7 m clearly
                l.range = level * 0.6f + 1f;
                l.intensity = Plugin.LampIntensity.Value * (0.5f + level / 30f);
            }
        }

        /// <summary>Unity lights cost per pixel: only the nearest few are on.</summary>
        private void CullLamps(Player player)
        {
            LampsLit = 0;
            if (_lamps.Count == 0)
            {
                return;
            }

            int max = Plugin.MaxLamps.Value;
            _byDistance.Clear();
            Vector3 at = player != null ? player.transform.position : Vector3.zero;
            foreach (Light l in _lamps.Values)
            {
                if (l != null)
                {
                    _byDistance.Add(new KeyValuePair<float, Light>(player != null ? (l.transform.position - at).sqrMagnitude : 0f, l));
                }
            }

            _byDistance.Sort((a, b) => a.Key.CompareTo(b.Key));
            for (int i = 0; i < _byDistance.Count; i++)
            {
                bool on = player != null && Link.Connected && i < max && _byDistance[i].Key < 60f * 60f;
                _byDistance[i].Value.enabled = on;
                LampsLit += on ? 1 : 0;
            }
        }
    }
}
