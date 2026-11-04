using System.Linq;
using System.Collections.Generic;
using HaiBanTay.Editor.SceneBuilders;
using HaiBanTay.Editor.Setup;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Hồi quy nhìn xuyên (ghi chú 15/10 trên điện thoại: sọt rỗng, bát gọt khoai, vành lò thấy xuyên ra sau): mặt trước của mọi tam giác
    /// sọt/xô/bát (ChoreProps.BuildVessel) và vành tròn (ChoreProps.CollarMesh) phải quay ra khỏi khối vật liệu (Unity vẽ mặt có pháp
    /// tuyến Cross(b − a, c − a), cull mặt kia); vật liệu vật mỏng (tóc, vải nhân vật, vỏ tàu, quần áo người chơi) phải vẽ hai mặt.
    /// </summary>
    public class SeeThroughGeometryTests
    {
        const float Eps = 0.0003f;

        // Hàn đỉnh trùng vị trí: primitive tách đỉnh theo pháp tuyến/UV nhưng khối vật liệu vẫn phải kín, winding liền mạch.
        [TestCase("Gen_WholeFish_v1")]
        [TestCase("Gen_ShallowShovel_v1")]
        [TestCase("Gen_Cube_15x16x600_t100")]
        public void GeneratedFishShovelAndBeamHaveClosedConsistentSurfaces(string name)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(SurfaceMesh.Folder + "/" + name + ".asset");
            Assert.IsNotNull(mesh, name + ": cần rebuild scene trước khi đo mesh thật");
            var ids = new Dictionary<Vector3Int, int>();
            var vertices = mesh.vertices;
            var weld = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var p = vertices[i] * 1000000f;
                var key = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
                if (!ids.TryGetValue(key, out int id)) ids[key] = id = ids.Count;
                weld[i] = id;
            }
            var edges = new Dictionary<(int, int), (int count, int direction)>();
            var t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
                for (int j = 0; j < 3; j++)
                {
                    int a = weld[t[i + j]], b = weld[t[i + (j + 1) % 3]];
                    if (a == b) continue;
                    var key = a < b ? (a, b) : (b, a);
                    edges.TryGetValue(key, out var e);
                    edges[key] = (e.count + 1, e.direction + (a < b ? 1 : -1));
                }
            Assert.Greater(edges.Count, 12, name);
            Assert.AreEqual(0, edges.Count(e => e.Value.count != 2 || e.Value.direction != 0),
                name + ": mép hở hoặc hai tam giác cùng chiều — mặt sau có thể lộ qua khối vật liệu");
        }

        /// <summary>Bán kính quy về đa giác N cạnh (đỉnh ở góc i·2π/N): điểm trên cạnh đa giác bán kính R cho đúng R.</summary>
        static float PolygonRadius(Vector3 p, int n)
        {
            float r = new Vector2(p.x, p.z).magnitude;
            float step = Mathf.PI * 2f / n;
            float phi = Mathf.Repeat(Mathf.Atan2(p.z, p.x), step) - step * 0.5f;
            return r * Mathf.Cos(phi) / Mathf.Cos(step * 0.5f);
        }

        static void AssertFacesOut(Mesh mesh, System.Func<Vector3, bool> solid, string label)
        {
            var v = mesh.vertices;
            var t = mesh.triangles;
            int bad = 0, count = 0;
            string first = null;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                var n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-14f)
                    continue; // tam giác suy biến ở tâm đáy
                n.Normalize();
                var m = (a + b + c) / 3f;
                count++;
                if (solid(m + n * Eps) || !solid(m - n * Eps))
                {
                    bad++;
                    first ??= $"tam giác {i / 3}, tâm {m.ToString("F3")}, pháp tuyến {n.ToString("F2")}";
                }
            }
            Assert.Greater(count, 20, label);
            Assert.AreEqual(0, bad, $"{label}: {bad}/{count} tam giác quay mặt trước vào trong vật liệu (bị cull, nhìn xuyên), ví dụ {first}");
        }

        // Sọt than, xô khoai, bát gọt, chậu: các kích thước đang dùng trong builder.
        [TestCase(0.16f, 0.21f, 0.34f, 0.012f, 0.02f)]
        [TestCase(0.10f, 0.13f, 0.08f, 0.006f, 0.008f)]
        [TestCase(0.20f, 0.26f, 0.30f, 0.015f, 0.02f)]
        public void VesselFacesPointOutOfTheWall(float rb, float rt, float h, float wall, float floor)
        {
            var mesh = ChoreProps.BuildVessel(rb, rt, h, wall, floor, 0.5f);
            float riB = Mathf.Max(0.01f, rb - wall), riT = Mathf.Max(0.01f, rt - wall);
            bool Solid(Vector3 p)
            {
                if (p.y < 0f || p.y > h)
                    return false;
                float r = PolygonRadius(p, ChoreProps.VesselSegments);
                if (r > Mathf.Lerp(rb, rt, p.y / h))
                    return false;
                return !(p.y > floor && r < Mathf.Lerp(riB, riT, (p.y - floor) / (h - floor)));
            }
            AssertFacesOut(mesh, Solid, $"vật chứa {rb}/{rt}/{h}");
            Object.DestroyImmediate(mesh);
        }

        // Vành lò (StoveRing), đai xô (BucketHoop).
        [TestCase(0.215f, 0.31f, 0.0045f)]
        [TestCase(0.149f, 0.154f, 0.014f)]
        public void CollarFacesPointOutOfTheRing(float rIn, float rOut, float h)
        {
            var mesh = ChoreProps.CollarMesh(rIn, rOut, h);
            bool Solid(Vector3 p)
            {
                if (p.y < 0f || p.y > h)
                    return false;
                float r = PolygonRadius(p, ChoreProps.CollarSegments);
                return r >= rIn && r <= rOut;
            }
            AssertFacesOut(mesh, Solid, $"vành {rIn}/{rOut}/{h}");
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void ThinMaterialsDrawBothSides()
        {
            var single = TwoSidedMaterials.SingleSided();
            Assert.IsEmpty(single, "vật liệu vật mỏng còn cull mặt sau (chạy Tools/two_sided.sh):\n" + string.Join("\n", single.Take(20)));
        }
    }
}
