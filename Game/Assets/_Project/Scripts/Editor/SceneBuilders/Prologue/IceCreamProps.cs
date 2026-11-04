using System;
using System.Collections.Generic;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Narrative;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>
    /// Cốc kem của prologue theo ảnh tham chiếu: cốc giấy trắng loe miệng có viền cuộn, trong lòng cũng trắng (vệt vàng trong ảnh
    /// tham chiếu là lỗi ảnh sinh, người dùng xác nhận 16/10); viên kem trắng gồ ghề có váy xếp nếp ở chân; thìa nhựa xanh dương cỡ
    /// thìa thường (dài ~15,5 cm, lòng 5 cm). Hình học là mesh sinh bằng code, lưu ở Art/Meshes (cập nhật tại chỗ, không đổi GUID).
    /// Kích thước dùng chung với <see cref="IceEatMotion"/>.
    /// </summary>
    public static class IceCreamProps
    {
        const int Sides = 36;

        /// <summary>Cốc (gốc ở tâm đáy) có viên kem đầy ở miệng và thìa cắm trong kem.</summary>
        public static Transform BuildCup(Transform parent, string name, float scale)
        {
            var cup = BuilderUtils.Group(name, parent).transform;
            cup.localScale = Vector3.one * scale;
            var paper = Pale(AssetFactory.Lit("StoryPaperCup", new Color(0.78f, 0.84f, 1f), 0.16f), new Color(0.34f, 0.34f, 0.35f));
            var inner = Pale(AssetFactory.Lit("StoryPaperCupInner", new Color(0.85f, 0.88f, 1f), 0.12f), new Color(0.34f, 0.34f, 0.35f));
            inner.SetTexture("_BaseMap", null);
            EditorUtility.SetDirty(inner);
            MeshChild(cup, "PaperCup", CupMesh(), paper, inner);

            var cream = MeshChild(cup, "IceCream", ScoopMesh("IceScoop", IceEatMotion.ScoopRadius, IceEatMotion.ScoopCenter, 0.0042f, 1f, 1f), CreamMaterial());
            cream.localPosition = Vector3.up * IceEatMotion.CupHeight;
            // Thìa cắm nghiêng trong kem: lòng thìa chìm ở đỉnh viên kem, cán chĩa lên.
            Spoon(cup, "Spoon").SetLocalPositionAndRotation(new Vector3(0.027f, IceEatMotion.CupHeight + 0.069f, 0f), Quaternion.Euler(0f, 0f, 160f));
            return cup;
        }

        /// <summary>Thìa nhựa xanh: gốc ở điểm cầm, +Y về phía lòng thìa, lòng thìa lõm về +Z. Có sẵn miếng kem trên thìa (SpoonScoop, mặc định tắt).</summary>
        public static Transform Spoon(Transform parent, string name)
        {
            var spoon = BuilderUtils.Group(name, parent).transform;
            // Xanh dương đậm như ảnh tham chiếu: nền tối, tự sáng xanh để đèn vàng của quán không làm thìa bạc màu xám.
            var plastic = Pale(AssetFactory.Lit("StoryPlasticSpoon", new Color(0.05f, 0.12f, 0.5f), 0.3f), new Color(0.025f, 0.06f, 0.2f));
            MeshChild(spoon, "SpoonBody", SpoonMesh(), plastic);
            var bite = MeshChild(spoon, "SpoonScoop", ScoopMesh("IceBite", IceEatMotion.BiteRadius, IceEatMotion.BiteCenter, 0.0022f, 1.2f, 0.8f), CreamMaterial());
            bite.SetLocalPositionAndRotation(new Vector3(0f, IceEatMotion.SpoonBowlY, IceEatMotion.SpoonBowlZ), Quaternion.Euler(90f, 0f, 0f));
            bite.gameObject.SetActive(false);
            return spoon;
        }

        // Kem trắng ngà, mờ (ít bóng): tự sáng rất nhẹ cho khỏi chìm trong bóng cốc; sáng hơn nữa thì cháy trắng như đèn (ảnh 17/10).
        static Material CreamMaterial() => Pale(AssetFactory.Lit("StoryIceCream", new Color(0.80f, 0.78f, 0.72f), 0.15f), new Color(0.03f, 0.03f, 0.03f));

        /// <summary>Phát sáng nhẹ để cốc/kem không ngả cam đậm dưới ánh hoàng hôn của phố (ảnh tham chiếu: trắng ngà, kem vàng nhạt).</summary>
        static Material Pale(Material material, Color emission)
        {
            material.SetColor("_EmissionColor", emission);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Transform MeshChild(Transform parent, string name, Mesh mesh, params Material[] materials)
        {
            var go = BuilderUtils.Group(name, parent);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            return go.transform;
        }

        // ---------- Cốc giấy ----------

        static Mesh CupMesh()
        {
            const float h = IceEatMotion.CupHeight;
            const float bottom = IceEatMotion.CupBottomRadius, top = IceEatMotion.CupTopRadius;
            var profile = new List<Vector2> { new Vector2(0f, 0f), new Vector2(bottom - 0.002f, 0f), new Vector2(bottom, 0.0012f), new Vector2(top - 0.0032f, h - 0.0015f) };
            // Viền cuộn quanh tâm (0,0412; h−0,0015), bán kính 1,3 mm, đi từ mép ngoài qua đỉnh vào mép trong.
            var roll = new Vector2(top - 0.0045f, h - 0.0015f);
            int rimTop = 0;
            for (int i = 1; i <= 6; i++)
            {
                float a = i * 30f * Mathf.Deg2Rad;
                profile.Add(roll + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.0013f);
                if (i == 3) rimTop = profile.Count - 1;
            }
            profile.Add(new Vector2(bottom - 0.0057f, 0.0048f));
            profile.Add(new Vector2(bottom - 0.0070f, 0.0038f));
            profile.Add(new Vector2(0f, 0.0036f));
            var length = new float[profile.Count];
            for (int i = 1; i < profile.Count; i++) length[i] = length[i - 1] + Vector2.Distance(profile[i], profile[i - 1]);
            var mesh = Revolve("PaperCup", profile.Count, (r, k) =>
            {
                float a = k * Mathf.PI * 2f / Sides;
                return new Vector3(profile[r].x * Mathf.Cos(a), profile[r].y, profile[r].x * Mathf.Sin(a));
            }, r => length[r] / length[length.Length - 1] * 2f, 3f, rimTop);
            return mesh;
        }

        // ---------- Viên kem ----------

        /// <summary>
        /// Viên kem tròn gồ ghề: cầu bán kính radius tâm ở độ cao center (gốc mesh ở mặt phẳng miệng cốc), cắt phẳng đáy ở 150°, chân loe
        /// lởm chởm theo nhiễu. Lưới cầu-từ-khối (6 mặt lưới vuông, không có cực) nên không có vệt chụm về đỉnh như lưới tròn xoay; mặt sần
        /// bằng nhiễu 3D (tổng Perlin ba mặt phẳng): cục ~2 cm cao vài mm + nhám mịn, thêm hai gờ cong của vệt muỗng nạo trên nửa trên.
        /// squashXZ/squashY bóp méo nhẹ (miếng kem trên thìa dẹt hơn).
        /// </summary>
        static Mesh ScoopMesh(string name, float radius, float center, float bumps, float stretchXZ, float squashY)
        {
            int n = radius > .03f ? 22 : 14;
            float cut = Mathf.Cos(Mathf.PI * 5f / 6f);
            float Noise3(Vector3 q) => (Mathf.PerlinNoise(q.x, q.y) + Mathf.PerlinNoise(q.y, q.z) + Mathf.PerlinNoise(q.z, q.x)) / 3f - .5f;
            // Gờ vệt muỗng: (pháp tuyến mặt cắt, độ lệch, trục cửa sổ để gờ chỉ chạy một đoạn cung).
            var ridges = new[]
            {
                (new Vector3(.55f, .8f, -.25f).normalized, .42f, new Vector3(-.3f, .2f, .93f).normalized),
                (new Vector3(-.35f, .75f, .55f).normalized, .18f, new Vector3(.85f, .3f, .2f).normalized),
            };
            Vector3 Position(Vector3 d)
            {
                float theta = Mathf.Acos(Mathf.Clamp(d.y, -1f, 1f));
                float a = Mathf.Atan2(d.z, d.x);
                var flat = new Vector3(d.x, 0f, d.z);
                float horizontal = flat.magnitude;
                flat = horizontal > 1e-6f ? flat / horizontal : Vector3.right;
                float y = Mathf.Max(d.y, cut);
                float skirt = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Mathf.PI * 0.5f, Mathf.PI * 0.78f, Mathf.Min(theta, Mathf.PI * 5f / 6f)));
                float flare = 1f + skirt * (0.03f + 0.05f * Mathf.PerlinNoise(Mathf.Cos(a) * 2.2f + 7.3f, Mathf.Sin(a) * 2.2f + 1.9f));
                var p = new Vector3(flat.x * radius * horizontal * flare * stretchXZ, center + radius * y * squashY, flat.z * radius * horizontal * flare);
                if (d.y < cut)
                    return p;
                float lumps = Noise3(d * 1.8f + new Vector3(3.1f, 7.7f, 1.3f)) * 2.6f;
                float fine = Noise3(d * 11f + new Vector3(9.7f, 2.3f, 5.9f)) * 2.6f;
                float ridge = 0f;
                foreach (var (normal, offset, window) in ridges)
                {
                    float s = Vector3.Dot(d, normal) - offset;
                    float along = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.35f, .25f, Vector3.Dot(d, window)));
                    // Mép cuộn của lớp kem nạo: gờ nổi ở mép, phía trong thấp hơn một bậc.
                    ridge += along * (Mathf.Exp(-(s / .07f) * (s / .07f)) - .45f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.02f, -.12f, s)));
                }
                float upper = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.2f, .2f, d.y));
                var outward = new Vector3(p.x, p.y - center, p.z).normalized;
                return p + outward * (bumps * (lumps + .25f * fine + .7f * ridge * upper));
            }
            var index = new Dictionary<Vector3Int, int>();
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            var grid = new int[n + 1, n + 1];
            foreach (var face in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
            {
                var u = Mathf.Abs(face.y) > .5f ? Vector3.right : Vector3.up;
                var w = Vector3.Cross(face, u);
                for (int i = 0; i <= n; i++)
                    for (int j = 0; j <= n; j++)
                    {
                        // Lưới đều theo góc (tan) để ô gần cạnh khối không to hơn ô giữa mặt.
                        float x = Mathf.Tan((i / (float)n * 2f - 1f) * Mathf.PI / 4f), z = Mathf.Tan((j / (float)n * 2f - 1f) * Mathf.PI / 4f);
                        var d = (face + u * x + w * z).normalized;
                        var key = Vector3Int.RoundToInt(d * 100000f);
                        if (!index.TryGetValue(key, out int id))
                        {
                            id = vertices.Count;
                            index.Add(key, id);
                            vertices.Add(Position(d));
                            uvs.Add(new Vector2(Mathf.Atan2(d.z, d.x) / (Mathf.PI * 2f) + .5f, d.y * .5f + .5f));
                        }
                        grid[i, j] = id;
                    }
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                    {
                        int a = grid[i, j], b = grid[i + 1, j], c = grid[i, j + 1], d = grid[i + 1, j + 1];
                        triangles.AddRange(new[] { a, b, c, b, d, c });
                    }
            }
            var mesh = LoadMesh(name);
            mesh.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // ---------- Thìa nhựa ----------

        static Mesh SpoonMesh()
        {
            // (y, nửa bề rộng): đuôi tròn hơi loe, cán dẹt ~9 mm thon dần về cổ, cổ thắt, lòng thìa bầu 28 × 48 mm, đầu tròn.
            var keys = new[]
            {
                new Vector2(IceEatMotion.SpoonTailY, 0f), new Vector2(IceEatMotion.SpoonTailY + 0.00135f, 0.0038f),
                new Vector2(IceEatMotion.SpoonTailY + 0.0063f, 0.0047f), new Vector2(-0.036f, 0.0044f), new Vector2(0f, 0.0038f),
                new Vector2(0.0162f, 0.0031f), new Vector2(0.0234f, 0.0032f), new Vector2(0.0306f, 0.0072f), new Vector2(0.0414f, 0.0126f),
                new Vector2(0.0522f, 0.0142f), new Vector2(0.0612f, 0.0126f), new Vector2(0.0675f, 0.0086f), new Vector2(0.0716f, 0.0038f),
                new Vector2(IceEatMotion.SpoonTipY, 0f)
            };
            float HalfWidth(float y)
            {
                for (int i = 1; i < keys.Length; i++)
                    if (y <= keys[i].x)
                        return Mathf.Lerp(keys[i - 1].y, keys[i].y, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(keys[i - 1].x, keys[i].x, y)));
                return 0f;
            }
            const int rows = 56, cols = 10;
            const float thickness = 0.0018f, depth = 0.0068f, bowlStart = 0.0252f;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i <= rows; i++)
            {
                float y = Mathf.Lerp(IceEatMotion.SpoonTailY, IceEatMotion.SpoonTipY, i / (float)rows);
                float s = Mathf.Clamp01((y - bowlStart) / (IceEatMotion.SpoonTipY - bowlStart));
                float dish = depth * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(s * Mathf.PI)), 0.75f);
                for (int j = 0; j <= cols; j++)
                {
                    float t = j / (float)cols * 2f - 1f;
                    // Cán hơi cong lòng máng; lòng thìa lõm về +Z.
                    float z = -dish * (1f - t * t) + 0.0007f * t * t * (1f - s);
                    vertices.Add(new Vector3(t * HalfWidth(y), y, z));
                }
            }
            int stride = cols + 1;
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                {
                    int a = i * stride + j, b = a + stride, c = a + 1, d = b + 1;
                    triangles.AddRange(new[] { a, c, b, c, d, b });
                }
            // Mặt sau: đẩy xuống −Z một độ dày nhựa, pháp tuyến ngược.
            int count = vertices.Count, indexCount = triangles.Count;
            for (int i = 0; i < count; i++) vertices.Add(vertices[i] + Vector3.back * thickness);
            for (int i = 0; i < indexCount; i += 3)
                triangles.AddRange(new[] { triangles[i] + count, triangles[i + 2] + count, triangles[i + 1] + count });
            // Hai mép bên.
            for (int i = 0; i < rows; i++)
            {
                int l0 = i * stride, l1 = (i + 1) * stride, r0 = l0 + cols, r1 = l1 + cols;
                triangles.AddRange(new[] { l0, l1, l0 + count, l1, l1 + count, l0 + count });
                triangles.AddRange(new[] { r0, r0 + count, r1, r1, r0 + count, r1 + count });
            }
            var mesh = LoadMesh("PlasticSpoon");
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // ---------- Mesh dùng chung ----------

        static Mesh LoadMesh(string name)
        {
            string path = SurfaceMesh.Folder + "/Gen_" + name + "_v1.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = name };
                AssetFactory.EnsureFolder(SurfaceMesh.Folder);
                AssetDatabase.CreateAsset(mesh, path);
            }
            mesh.Clear();
            return mesh;
        }

        /// <summary>
        /// Mặt tròn xoay: hàng 0 → rings−1 đi dọc đường sinh, mặt thấy được nằm bên PHẢI hướng đi (cốc: từ đáy lên viền rồi xuống lòng cốc;
        /// kem: từ đáy lên đỉnh). splitRing: các ô sau hàng này vào submesh thứ hai (≥ rings: chỉ một submesh).
        /// Cột đầu và cột cuối trùng vị trí (cho UV liền), pháp tuyến đường nối được gộp để không lộ vệt.
        /// </summary>
        static Mesh Revolve(string name, int rings, Func<int, int, Vector3> position, Func<int, float> v, float uTiles, int splitRing)
        {
            int stride = Sides + 1;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (int r = 0; r < rings; r++)
                for (int k = 0; k <= Sides; k++)
                {
                    vertices.Add(position(r, k % Sides));
                    uvs.Add(new Vector2(uTiles * k / Sides, v(r)));
                }
            var first = new List<int>();
            var second = new List<int>();
            for (int r = 0; r < rings - 1; r++)
                for (int k = 0; k < Sides; k++)
                {
                    int a = r * stride + k, b = a + stride, c = a + 1, d = b + 1;
                    var list = r + 1 <= splitRing ? first : second;
                    list.AddRange(new[] { a, b, c, c, b, d });
                }
            var mesh = LoadMesh(name);
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = second.Count > 0 ? 2 : 1;
            mesh.SetTriangles(first, 0);
            if (second.Count > 0) mesh.SetTriangles(second, 1);
            mesh.RecalculateNormals();
            var normals = new List<Vector3>();
            mesh.GetNormals(normals);
            for (int r = 0; r < rings; r++)
            {
                var seam = (normals[r * stride] + normals[r * stride + Sides]).normalized;
                normals[r * stride] = seam;
                normals[r * stride + Sides] = seam;
                // Vòng thu về một điểm (đỉnh/đáy viên kem, tâm đáy cốc): mọi đỉnh trùng nhau phải chung pháp tuyến, không thì đỉnh viên kem
                // lộ hình sao nhiều cánh như nếp bánh bao.
                if ((vertices[r * stride] - vertices[r * stride + Sides / 2]).sqrMagnitude < 1e-10f)
                {
                    var pole = Vector3.zero;
                    for (int k = 0; k <= Sides; k++) pole += normals[r * stride + k];
                    pole.Normalize();
                    for (int k = 0; k <= Sides; k++) normals[r * stride + k] = pole;
                }
            }
            mesh.SetNormals(normals);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
