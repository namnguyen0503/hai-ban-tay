using System.Collections.Generic;
using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>
    /// Lưới băm theo ô trên mặt phẳng XZ để tìm láng giềng của một NPC mà không xét mọi cặp. Lớp thuần: chỉ giữ vị trí,
    /// chỉ số trả về là thứ tự Add. Dựng lại vài lần mỗi giây (CrowdSpace), không cấp phát sau lần dựng đầu.
    /// </summary>
    public sealed class CrowdGrid
    {
        readonly float _inverseCell;
        readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        readonly List<List<int>> _used = new List<List<int>>();
        readonly Stack<List<int>> _pool = new Stack<List<int>>();
        readonly List<Vector2> _points = new List<Vector2>();

        public CrowdGrid(float cellSize)
        {
            CellSize = Mathf.Max(0.1f, cellSize);
            _inverseCell = 1f / CellSize;
        }

        public float CellSize { get; }
        public int Count => _points.Count;

        public void Clear()
        {
            foreach (var list in _used)
            {
                list.Clear();
                _pool.Push(list);
            }
            _used.Clear();
            _cells.Clear();
            _points.Clear();
        }

        /// <summary>Thêm một điểm, trả về chỉ số của nó.</summary>
        public int Add(Vector3 position)
        {
            int index = _points.Count;
            _points.Add(new Vector2(position.x, position.z));
            long key = Key(Cell(position.x), Cell(position.z));
            if (!_cells.TryGetValue(key, out var list))
            {
                list = _pool.Count > 0 ? _pool.Pop() : new List<int>(4);
                _cells[key] = list;
                _used.Add(list);
            }
            list.Add(index);
            return index;
        }

        /// <summary>Chỉ số các điểm cách position không quá radius (trên mặt phẳng XZ). Trả về số điểm tìm được.</summary>
        public int Query(Vector3 position, float radius, List<int> results)
        {
            results.Clear();
            float r2 = radius * radius;
            int x0 = Cell(position.x - radius), x1 = Cell(position.x + radius);
            int z0 = Cell(position.z - radius), z1 = Cell(position.z + radius);
            for (int cx = x0; cx <= x1; cx++)
            {
                for (int cz = z0; cz <= z1; cz++)
                {
                    if (!_cells.TryGetValue(Key(cx, cz), out var list))
                        continue;
                    foreach (int i in list)
                    {
                        float dx = _points[i].x - position.x, dz = _points[i].y - position.z;
                        if (dx * dx + dz * dz <= r2)
                            results.Add(i);
                    }
                }
            }
            return results.Count;
        }

        int Cell(float v) => Mathf.FloorToInt(v * _inverseCell);

        static long Key(int cx, int cz) => ((long)cx << 32) | (uint)cz;
    }
}
