using System.Collections.Generic;
using UnityEngine;
using MineHeim.Core;

namespace MineHeim
{
    /// <summary>
    /// Valheim's ground as invisible barrier blocks in Minecraft. Every frame a batch of columns around the player is
    /// probed with a raycast straight down (against the terrain, rocks and trees' trunks: built pieces are not ground, they
    /// are voxelized whole by PieceVoxels, so the ground under a roof is still there) and the columns whose height
    /// changed go into the shared barrier cells. Minecraft's blocks, mobs and items then rest on Valheim's world.
    ///
    /// A column's barrier is the two cells just under the surface (Minecraft y = floor(surface + offset) - 1 and below):
    /// terrain heights are not whole numbers, so a block placed on it sits up to 1 m into the ground, never floating above it.
    /// </summary>
    internal sealed class GroundProbe
    {
        private const int InnerRadius = 8, InnerPerFrame = 64;
        internal static readonly int Mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "terrain");

        private readonly BarrierCells _cells;
        private readonly Dictionary<long, int> _sent = new Dictionary<long, int>(); // column -> barrier top cell sent
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private int _outerIndex, _innerIndex;

        public GroundProbe(BarrierCells cells)
        {
            _cells = cells;
        }

        public int ColumnsSent => _sent.Count;

        /// <summary>The cell a column's barrier tops out at, if it has been sent.</summary>
        public bool TryGetTop(int x, int z, out int top) => _sent.TryGetValue(Key(x, z), out top);

        /// <summary>Forget what Minecraft was told (a new Minecraft world started): everything is sent again.</summary>
        public void Reset()
        {
            _sent.Clear();
            _outerIndex = _innerIndex = 0;
        }

        public IEnumerable<KeyValuePair<long, int>> Sent => _sent;

        public static long Key(int x, int z) => ((long)x << 32) | (uint)z;

        public static void Unpack(long key, out int x, out int z)
        {
            x = (int)(key >> 32);
            z = (int)(key & 0xFFFFFFFFL);
        }

        public void Tick(Vector3 player, double yOffset, int radius, int perFrame)
        {
            int cx = Mathf.FloorToInt(player.x), cz = Mathf.FloorToInt(-player.z);
            ProbeBatch(cx, cz, player.y, yOffset, InnerPerFrame, InnerRadius, ref _innerIndex);
            ProbeBatch(cx, cz, player.y, yOffset, Mathf.Max(0, perFrame - InnerPerFrame), radius, ref _outerIndex);
            if (_sent.Count > 60000)
            {
                // far behind the player: forget those columns (left in Minecraft), they are probed again if he returns
                var far = new List<long>();
                foreach (KeyValuePair<long, int> kv in _sent)
                {
                    int x, z;
                    Unpack(kv.Key, out x, out z);
                    if (Mathf.Abs(x - cx) > radius + 16 || Mathf.Abs(z - cz) > radius + 16)
                    {
                        far.Add(kv.Key);
                    }
                }

                foreach (long key in far)
                {
                    int x, z;
                    Unpack(key, out x, out z);
                    int top = _sent[key];
                    _cells.Forget(x, top - 1, z);
                    _cells.Forget(x, top, z);
                    _sent.Remove(key);
                }
            }
        }

        private void ProbeBatch(int cx, int cz, float playerY, double yOffset, int count, int radius, ref int index)
        {
            int side = radius * 2 + 1, total = side * side;
            for (int k = 0; k < count; k++)
            {
                int i = index++ % total;
                int bx = cx + i % side - radius, bz = cz + i / side - radius;
                // from well above the player (a hillside can rise next to him) straight down
                var origin = new Vector3(bx + 0.5f, playerY + 80f, -(bz + 0.5f));
                int n = Physics.RaycastNonAlloc(origin, Vector3.down, _hits, 260f, Mask, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue, surface = 0f;
                bool found = false;
                for (int j = 0; j < n; j++)
                {
                    if (_hits[j].collider.name == BlockColliders.ObjectName)
                    {
                        continue; // Minecraft's own blocks are not the ground
                    }

                    if (_hits[j].distance < best)
                    {
                        best = _hits[j].distance;
                        surface = _hits[j].point.y;
                        found = true;
                    }
                }

                long key = Key(bx, bz);
                int old;
                bool had = _sent.TryGetValue(key, out old);
                if (!found)
                {
                    if (had)
                    {
                        _cells.Remove(bx, old - 1, bz);
                        _cells.Remove(bx, old, bz);
                        _sent.Remove(key);
                    }

                    continue;
                }

                int top = Mathf.FloorToInt((float)(surface + yOffset)) - 1;
                if (had && old == top)
                {
                    continue;
                }

                if (had)
                {
                    _cells.Remove(bx, old - 1, bz);
                    _cells.Remove(bx, old, bz);
                }

                _cells.Add(bx, top - 1, bz);
                _cells.Add(bx, top, bz);
                _sent[key] = top;
            }
        }
    }
}
