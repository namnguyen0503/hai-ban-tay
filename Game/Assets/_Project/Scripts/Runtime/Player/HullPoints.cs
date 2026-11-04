using System.Collections.Generic;
using UnityEngine;

namespace HaiBanTay.Player
{
    /// <summary>Rút gọn một đám điểm thành các điểm cực theo 26 hướng (trục, cạnh, góc hộp): đủ ôm hình lồi để giữ đồ cầm
    /// ngoài thân người chơi, mà mỗi khung chỉ phải biến đổi vài chục điểm.</summary>
    public static class HullPoints
    {
        static readonly Vector3[] Directions = MakeDirections();

        static Vector3[] MakeDirections()
        {
            var result = new List<Vector3>();
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                if (x != 0 || y != 0 || z != 0) result.Add(new Vector3(x, y, z).normalized);
            return result.ToArray();
        }

        public static Vector3[] Extremes(IReadOnlyList<Vector3> points)
        {
            if (points == null || points.Count == 0) return new Vector3[0];
            var picked = new HashSet<int>();
            foreach (var d in Directions)
            {
                int best = 0;
                float max = float.NegativeInfinity;
                for (int i = 0; i < points.Count; i++)
                {
                    float v = Vector3.Dot(points[i], d);
                    if (v > max) { max = v; best = i; }
                }
                picked.Add(best);
            }
            var result = new Vector3[picked.Count];
            int k = 0;
            foreach (int i in picked) result[k++] = points[i];
            return result;
        }

        static readonly Dictionary<Mesh, Vector3[]> MeshCache = new Dictionary<Mesh, Vector3[]>();

        /// <summary>Điểm cực của một mesh (hệ của mesh); mesh không đọc được thì lấy 8 góc hộp bao.</summary>
        public static Vector3[] Of(Mesh mesh)
        {
            if (mesh == null) return new Vector3[0];
            if (MeshCache.TryGetValue(mesh, out var cached)) return cached;
            Vector3[] result;
            if (mesh.isReadable)
                result = Extremes(mesh.vertices);
            else
            {
                var b = mesh.bounds;
                result = new Vector3[8];
                for (int i = 0; i < 8; i++)
                    result[i] = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            }
            MeshCache[mesh] = result;
            return result;
        }
    }
}
