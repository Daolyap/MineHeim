using System.Collections.Generic;
using UnityEngine;
using MineHeim.Core;

namespace MineHeim
{
    /// <summary>
    /// Minecraft's solid blocks as invisible colliders in Valheim, so the Viking and Valheim's creatures stop at what is built
    /// and can walk through what is broken. They are plain local GameObjects with a BoxCollider on Valheim's "static_solid"
    /// layer (rocks and trees are there): no ZNetView, so nothing is saved into the world and nothing is synchronised.
    /// Changes arrive from Minecraft's "blocks" messages and are applied in budgeted batches so a TNT crater is no hitch.
    /// </summary>
    internal sealed class BlockColliders
    {
        public const string ObjectName = "vc_block";
        private const int BudgetPerFrame = 300;
        private static readonly int Layer = LayerMask.NameToLayer("static_solid");

        private readonly Dictionary<long, GameObject> _blocks = new Dictionary<long, GameObject>();
        private readonly Queue<long> _pendingSet = new Queue<long>(), _pendingClear = new Queue<long>();
        private Transform _root;
        private bool _warnedCap;

        public int Count => _blocks.Count;
        public int Pending => _pendingSet.Count + _pendingClear.Count;

        public static long Key(int x, int y, int z) => ((long)(x & 0x3FFFFFF) << 38) | ((long)(z & 0x3FFFFFF) << 12) | (long)((y + 512) & 0xFFF);

        private static void Unpack(long key, out int x, out int y, out int z)
        {
            x = (int)((key >> 38) & 0x3FFFFFF);
            if (x >= 0x2000000)
            {
                x -= 0x4000000;
            }

            z = (int)((key >> 12) & 0x3FFFFFF);
            if (z >= 0x2000000)
            {
                z -= 0x4000000;
            }

            y = (int)(key & 0xFFF) - 512;
        }

        /// <summary>A "blocks" message: {"set":[x,y,z,...],"clear":[x,y,z,...]}.</summary>
        public void Enqueue(string json)
        {
            List<int> set = Messages.IntArray(json, "set"), clear = Messages.IntArray(json, "clear");
            for (int i = 0; i + 2 < clear.Count; i += 3)
            {
                _pendingClear.Enqueue(Key(clear[i], clear[i + 1], clear[i + 2]));
            }

            for (int i = 0; i + 2 < set.Count; i += 3)
            {
                _pendingSet.Enqueue(Key(set[i], set[i + 1], set[i + 2]));
            }
        }

        /// <summary>Apply up to a frame's budget of queued changes (main thread).</summary>
        public void Tick(double yOffset, int maxColliders)
        {
            int budget = BudgetPerFrame;
            while (budget > 0 && _pendingClear.Count > 0)
            {
                budget--;
                long key = _pendingClear.Dequeue();
                GameObject go;
                if (_blocks.TryGetValue(key, out go))
                {
                    Object.Destroy(go);
                    _blocks.Remove(key);
                }
            }

            while (budget > 0 && _pendingSet.Count > 0)
            {
                budget--;
                long key = _pendingSet.Dequeue();
                if (_blocks.ContainsKey(key))
                {
                    continue;
                }

                if (_blocks.Count >= maxColliders)
                {
                    if (!_warnedCap)
                    {
                        _warnedCap = true;
                        Plugin.Log.LogWarning("block colliders: cap of " + maxColliders + " reached, further blocks have no collision");
                    }

                    continue;
                }

                if (_root == null)
                {
                    _root = new GameObject("mineheim_blocks").transform;
                }

                int x, y, z;
                Unpack(key, out x, out y, out z);
                double ux, uy, uz;
                Mapping.FromMinecraft(x + 0.5, y + 0.5, z + 0.5, yOffset, out ux, out uy, out uz);
                var go = new GameObject(ObjectName) { layer = Layer };
                go.transform.SetParent(_root, false);
                go.transform.position = new Vector3((float)ux, (float)uy, (float)uz);
                go.AddComponent<BoxCollider>().size = Vector3.one;
                _blocks[key] = go;
            }
        }

        /// <summary>Remove every collider (leaving the world, or a new Minecraft world).</summary>
        public void Clear()
        {
            foreach (GameObject go in _blocks.Values)
            {
                if (go != null)
                {
                    Object.Destroy(go);
                }
            }

            _blocks.Clear();
            _pendingSet.Clear();
            _pendingClear.Clear();
            _warnedCap = false;
        }
    }
}
