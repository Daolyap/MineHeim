using System.Collections.Generic;
using UnityEngine;
using MineHeim.Core;

namespace MineHeim
{
    /// <summary>
    /// Valheim's buildings as whole Minecraft collision: every built piece near the player (walls, floors, roofs, stairs,
    /// doors) is voxelized into barrier cells, so Minecraft's mobs, items and arrows stop at walls and walk under roofs.
    ///
    /// A cell counts when the piece's own colliders reach into the box from its middle to the middle of the cell above
    /// (Minecraft y + 0.5 .. y + 1.5): a floor's barrier then tops out within half a block of the floor's surface, never
    /// more than half a block above or below it. Pieces are rescanned once a second; a piece whose colliders moved (a door
    /// opening) is voxelized again, a destroyed one lets go of its cells. Budgeted per frame, like the ground.
    /// </summary>
    internal sealed class PieceVoxels
    {
        private static readonly int SolidMask = LayerMask.GetMask("piece", "Default", "static_solid", "Default_small");

        private sealed class Record
        {
            public WearNTear Piece;
            public readonly List<long> Cells = new List<long>();
            public int Signature;
            public bool Queued;
            public Fireplace Fire;
            public Light Light;
        }

        private readonly BarrierCells _cells;
        private readonly Dictionary<int, Record> _records = new Dictionary<int, Record>();
        private readonly Queue<Record> _todo = new Queue<Record>();
        private readonly Collider[] _overlap = new Collider[32];
        private readonly HashSet<Collider> _own = new HashSet<Collider>();
        private readonly List<Collider> _colliders = new List<Collider>();
        private readonly List<int> _gone = new List<int>();
        private float _nextScan;
        private bool _warnedCap;

        public PieceVoxels(BarrierCells cells)
        {
            _cells = cells;
        }

        public int Pieces => _records.Count;
        public int Voxelized;
        public int CellCount;

        /// <summary>Burning fires and torches among the tracked pieces, for the light sync.</summary>
        public IEnumerable<KeyValuePair<Fireplace, Light>> Fires()
        {
            foreach (Record r in _records.Values)
            {
                if (r.Fire != null)
                {
                    yield return new KeyValuePair<Fireplace, Light>(r.Fire, r.Light);
                }
            }
        }

        /// <summary>The cells a piece was voxelized into (null if it is not tracked yet).</summary>
        public List<long> CellsOf(WearNTear piece)
        {
            Record r;
            return piece != null && _records.TryGetValue(piece.GetInstanceID(), out r) ? r.Cells : null;
        }

        /// <summary>A new Minecraft world or a new Valheim world: nothing is voxelized any more.</summary>
        public void Reset()
        {
            _records.Clear();
            _todo.Clear();
            CellCount = 0;
            _nextScan = 0f;
            _warnedCap = false;
        }

        public void Tick(Vector3 player, double yOffset, float radius, int budget, int maxCells)
        {
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 1f;
                Scan(player, radius);
            }

            while (budget > 0 && _todo.Count > 0)
            {
                Record r = _todo.Dequeue();
                r.Queued = false;
                if (r.Piece == null)
                {
                    continue; // destroyed while queued: the next scan lets go of its cells
                }

                budget -= Voxelize(r, yOffset, maxCells);
            }
        }

        private void Scan(Vector3 player, float radius)
        {
            float keep = radius + 8f; // a margin, so a piece on the edge does not flicker in and out
            foreach (WearNTear piece in WearNTear.GetAllInstances())
            {
                if (piece == null)
                {
                    continue;
                }

                Vector3 d = piece.transform.position - player;
                d.y *= 0.5f; // a tall tower still counts
                if (d.sqrMagnitude > radius * radius)
                {
                    continue;
                }

                int id = piece.GetInstanceID();
                Record r;
                if (!_records.TryGetValue(id, out r))
                {
                    r = new Record { Piece = piece, Signature = int.MinValue, Fire = piece.GetComponent<Fireplace>() };
                    if (r.Fire != null)
                    {
                        r.Light = piece.GetComponentInChildren<Light>(true);
                    }

                    _records[id] = r;
                }

                if (!r.Queued && Signature(piece) != r.Signature)
                {
                    r.Queued = true;
                    _todo.Enqueue(r);
                }
            }

            _gone.Clear();
            foreach (KeyValuePair<int, Record> kv in _records)
            {
                WearNTear piece = kv.Value.Piece;
                if (piece == null)
                {
                    _gone.Add(kv.Key);
                    continue;
                }

                Vector3 d = piece.transform.position - player;
                d.y *= 0.5f;
                if (d.sqrMagnitude > keep * keep)
                {
                    _gone.Add(kv.Key);
                }
            }

            foreach (int id in _gone)
            {
                Release(_records[id]);
                _records.Remove(id);
            }
        }

        private void Release(Record r)
        {
            foreach (long key in r.Cells)
            {
                int x, y, z;
                BarrierCells.Unpack(key, out x, out y, out z);
                _cells.Remove(x, y, z);
            }

            CellCount -= r.Cells.Count;
            r.Cells.Clear();
        }

        private void Colliders(WearNTear piece)
        {
            _colliders.Clear();
            foreach (Collider c in piece.GetComponentsInChildren<Collider>())
            {
                if (c.enabled && !c.isTrigger && ((1 << c.gameObject.layer) & SolidMask) != 0)
                {
                    _colliders.Add(c);
                }
            }
        }

        /// <summary>Where the piece's colliders are, to 5 cm: changes when a door swings or the piece is moved.</summary>
        private int Signature(WearNTear piece)
        {
            Colliders(piece);
            unchecked
            {
                int h = 17 + _colliders.Count;
                foreach (Collider c in _colliders)
                {
                    Bounds b = c.bounds;
                    h = h * 31 + Mathf.RoundToInt(b.center.x * 20f);
                    h = h * 31 + Mathf.RoundToInt(b.center.y * 20f);
                    h = h * 31 + Mathf.RoundToInt(b.center.z * 20f);
                    h = h * 31 + Mathf.RoundToInt(b.size.x * 20f) + Mathf.RoundToInt(b.size.y * 20f) * 7 + Mathf.RoundToInt(b.size.z * 20f) * 13;
                }

                return h;
            }
        }

        /// <summary>Voxelize one piece afresh; returns the number of overlap tests it cost.</summary>
        private int Voxelize(Record r, double yOffset, int maxCells)
        {
            r.Signature = Signature(r.Piece);
            _own.Clear();
            bool any = false;
            var bounds = new Bounds();
            foreach (Collider c in _colliders)
            {
                _own.Add(c);
                if (any)
                {
                    bounds.Encapsulate(c.bounds);
                }
                else
                {
                    bounds = c.bounds;
                    any = true;
                }
            }

            var fresh = new List<long>();
            int tests = 0;
            if (any)
            {
                // Minecraft cells: x = floor(ux), z = floor(-uz), and the cell y whose test box (y+0.5 .. y+1.5) meets the bounds
                int x0 = Mathf.FloorToInt(bounds.min.x), x1 = Mathf.FloorToInt(bounds.max.x);
                int z0 = Mathf.FloorToInt(-bounds.max.z), z1 = Mathf.FloorToInt(-bounds.min.z);
                int y0 = Mathf.FloorToInt((float)(bounds.min.y + yOffset) - 1.5f), y1 = Mathf.FloorToInt((float)(bounds.max.y + yOffset) - 0.5f);
                var half = new Vector3(0.49f, 0.49f, 0.49f);
                for (int x = x0; x <= x1; x++)
                {
                    for (int z = z0; z <= z1; z++)
                    {
                        for (int y = y0; y <= y1; y++)
                        {
                            tests++;
                            var center = new Vector3(x + 0.5f, (float)(y + 1.0 - yOffset), -(z + 0.5f));
                            int n = Physics.OverlapBoxNonAlloc(center, half, _overlap, Quaternion.identity, SolidMask, QueryTriggerInteraction.Ignore);
                            for (int i = 0; i < n; i++)
                            {
                                if (_own.Contains(_overlap[i]))
                                {
                                    fresh.Add(BarrierCells.Key(x, y, z));
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            // the new cells first, then let go of the old ones: a cell in both is never cleared in between
            int room = maxCells - CellCount + r.Cells.Count;
            if (fresh.Count > room)
            {
                if (!_warnedCap)
                {
                    _warnedCap = true;
                    Plugin.Log.LogWarning("building collision: cap of " + maxCells + " cells reached, further pieces have no collision in Minecraft");
                }

                fresh.RemoveRange(System.Math.Max(0, room), fresh.Count - System.Math.Max(0, room));
            }

            foreach (long key in fresh)
            {
                int x, y, z;
                BarrierCells.Unpack(key, out x, out y, out z);
                _cells.Add(x, y, z);
            }

            Release(r);
            r.Cells.AddRange(fresh);
            CellCount += fresh.Count;
            Voxelized++;
            return Mathf.Max(1, tests);
        }
    }
}
