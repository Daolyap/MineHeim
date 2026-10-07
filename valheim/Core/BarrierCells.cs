using System.Collections.Generic;

namespace MineHeim.Core
{
    /// <summary>
    /// The barrier cells Minecraft should hold, counted per owner: the ground probe and the building voxels can both want
    /// the same cell (a floor resting on the ground), and one of them letting go must not clear it under the other.
    /// Only a cell's first owner sends it to Minecraft and only its last one clears it. Drain turns the changes since the
    /// last drain into "ground" message columns {x, z, yBottom, yTop}, vertical runs merged.
    /// </summary>
    public sealed class BarrierCells
    {
        private readonly Dictionary<long, int> _refs = new Dictionary<long, int>();
        private readonly HashSet<long> _toSet = new HashSet<long>(), _toClear = new HashSet<long>();

        public int Count => _refs.Count;
        public int PendingCount => _toSet.Count + _toClear.Count;

        public static long Key(int x, int y, int z) => ((long)(x & 0x3FFFFFF) << 38) | ((long)(z & 0x3FFFFFF) << 12) | (long)((y + 512) & 0xFFF);

        public static void Unpack(long key, out int x, out int y, out int z)
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

        public bool Has(int x, int y, int z) => _refs.ContainsKey(Key(x, y, z));

        public void Add(int x, int y, int z)
        {
            long k = Key(x, y, z);
            int n;
            _refs.TryGetValue(k, out n);
            _refs[k] = n + 1;
            if (n == 0 && !_toClear.Remove(k))
            {
                _toSet.Add(k);
            }
        }

        public void Remove(int x, int y, int z)
        {
            long k = Key(x, y, z);
            int n;
            if (!_refs.TryGetValue(k, out n))
            {
                return;
            }

            if (n > 1)
            {
                _refs[k] = n - 1;
                return;
            }

            _refs.Remove(k);
            if (!_toSet.Remove(k))
            {
                _toClear.Add(k);
            }
        }

        /// <summary>Let go of a cell without clearing it in Minecraft (far behind the player: left there, not worth a message).</summary>
        public void Forget(int x, int y, int z)
        {
            long k = Key(x, y, z);
            int n;
            if (!_refs.TryGetValue(k, out n))
            {
                return;
            }

            if (n > 1)
            {
                _refs[k] = n - 1;
            }
            else
            {
                _refs.Remove(k);
                _toSet.Remove(k);
            }
        }

        /// <summary>A new Minecraft world: it holds none of our cells, and every owner sends its cells again.</summary>
        public void Reset()
        {
            _refs.Clear();
            _toSet.Clear();
            _toClear.Clear();
        }

        /// <summary>The changes since the last drain as columns {x, z, yBottom, yTop, ...}; false if there were none.</summary>
        public bool Drain(List<int> add, List<int> remove)
        {
            if (_toSet.Count + _toClear.Count == 0)
            {
                return false;
            }

            Columns(_toSet, add);
            Columns(_toClear, remove);
            _toSet.Clear();
            _toClear.Clear();
            return true;
        }

        private static void Columns(HashSet<long> cells, List<int> output)
        {
            if (cells.Count == 0)
            {
                return;
            }

            // the key orders by x, then z, then y: sorted, a column's cells are adjacent and in rising y
            var keys = new List<long>(cells);
            keys.Sort();
            int i = 0;
            while (i < keys.Count)
            {
                int x, y, z;
                Unpack(keys[i], out x, out y, out z);
                int top = y;
                int j = i + 1;
                while (j < keys.Count)
                {
                    int x2, y2, z2;
                    Unpack(keys[j], out x2, out y2, out z2);
                    if (x2 != x || z2 != z || y2 != top + 1)
                    {
                        break;
                    }

                    top = y2;
                    j++;
                }

                output.Add(x);
                output.Add(z);
                output.Add(y);
                output.Add(top);
                i = j;
            }
        }
    }
}
