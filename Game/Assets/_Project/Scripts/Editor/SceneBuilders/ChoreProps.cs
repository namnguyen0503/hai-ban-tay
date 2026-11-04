using System.Collections.Generic;
using HaiBanTay.Editor.Setup;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>Dụng cụ bếp sinh bằng editor; không ghi đè prefab art của nhóm.</summary>
    public static class ChoreProps
    {
        /// <summary>
        /// Vật chứa hở miệng thật (sọt, xô): thành ngoài, mép dày, thành trong và đáy trong, đáy ngoài. Khối trụ đặc làm tay
        /// cầm vành chui vào thân (bị che) và sọt rỗng trông như khối đặc. Gốc ở đáy; UV theo mét như SurfaceMesh.
        /// </summary>
        public static GameObject OpenVessel(Transform parent, string name, Vector3 position, float bottomRadius, float topRadius,
            float height, float wall, float floorThickness, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            float tile = Mathf.Max(0.01f, AssetFactory.SurfaceTile(mat));
            string key = $"Gen_Vessel2_{Mathf.RoundToInt(bottomRadius * 1000)}_{Mathf.RoundToInt(topRadius * 1000)}_{Mathf.RoundToInt(height * 1000)}_{Mathf.RoundToInt(wall * 1000)}_{Mathf.RoundToInt(floorThickness * 1000)}_t{Mathf.RoundToInt(tile * 100)}";
            string path = $"{SurfaceMesh.Folder}/{key}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                AssetFactory.EnsureFolder(SurfaceMesh.Folder);
                mesh = BuildVessel(bottomRadius, topRadius, height, wall, floorThickness, tile);
                mesh.name = key;
                AssetDatabase.CreateAsset(mesh, path);
            }
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>
        /// Lưỡi dao gọt (paring knife): gót ở x = 0,025 (chỗ khâu chặn), mũi ở x = −0,058; sống lưng thẳng ở +Z rồi cong xuống
        /// mũi, lưỡi cắt thẳng ở −Z rồi hơi vểnh lên mũi. Mặt cắt hình nêm: sống dày 1,6 mm, lưỡi mỏng như giấy, mỏng dần
        /// về mũi. Mặt trên, mặt dưới và sống tách đỉnh để vệt sáng thép gãy gọn ở mép.
        /// </summary>
        public static Mesh PeelingBlade()
        {
            const string key = "Gen_PeelingBlade_v1";
            string path = $"{SurfaceMesh.Folder}/{key}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null)
                return mesh;
            const int n = 18;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            void Column(int face, float s, out Vector3 spine, out Vector3 edge)
            {
                float x = 0.025f - 0.083f * s;
                float spineZ = s <= 0.5f ? 0.0095f : 0.0095f - 0.0155f * (1f - Mathf.Cos((s - 0.5f) / 0.5f * Mathf.PI * 0.5f));
                float edgeZ = s <= 0.75f ? -0.0085f : -0.0085f + 0.0025f * Mathf.Pow((s - 0.75f) / 0.25f, 2f);
                float hs = 0.0008f * (1f - 0.6f * s), he = 0.00008f;
                float sign = face == 1 ? -1f : 1f;
                spine = new Vector3(x, sign * hs, spineZ);
                edge = new Vector3(x, sign * he, edgeZ);
            }
            // Mặt trên (0) và mặt dưới (1): dải giữa sống và lưỡi; sống (2): dải giữa mép sống trên và dưới.
            for (int face = 0; face < 3; face++)
            {
                int start = v.Count;
                for (int i = 0; i <= n; i++)
                {
                    float s = i / (float)n;
                    if (face < 2)
                    {
                        Column(face, s, out var spine, out var edge);
                        v.Add(spine); v.Add(edge);
                    }
                    else
                    {
                        Column(0, s, out var top, out _);
                        Column(1, s, out var bottom, out _);
                        v.Add(top); v.Add(bottom);
                    }
                    uv.Add(new Vector2(s, 1f)); uv.Add(new Vector2(s, 0f));
                }
                for (int i = 0; i < n; i++)
                {
                    int a = start + i * 2, d = a + 1, b = a + 2, c = a + 3;
                    // Đỉnh: a/b ở sống (hoặc mép trên), d/c ở lưỡi (hoặc mép dưới); i+1 lệch về phía mũi (−X).
                    if (face == 0) { tri.Add(b); tri.Add(a); tri.Add(d); tri.Add(b); tri.Add(d); tri.Add(c); }
                    else if (face == 1) { tri.Add(b); tri.Add(d); tri.Add(a); tri.Add(b); tri.Add(c); tri.Add(d); }
                    else { tri.Add(a); tri.Add(b); tri.Add(c); tri.Add(a); tri.Add(c); tri.Add(d); }
                }
            }
            mesh = new Mesh { name = key };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            AssetFactory.EnsureFolder(SurfaceMesh.Folder);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>Vành tròn dẹt (rIn..rOut, cao h): lót quanh lỗ tròn trên mặt bếp, che góc ô khoét vuông bên dưới.</summary>
        public static GameObject Collar(Transform parent, string name, Vector3 position, float rIn, float rOut, float h, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            string key = $"Gen_Collar2_{Mathf.RoundToInt(rIn * 1000)}_{Mathf.RoundToInt(rOut * 1000)}_{Mathf.RoundToInt(h * 1000)}";
            string path = $"{SurfaceMesh.Folder}/{key}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                AssetFactory.EnsureFolder(SurfaceMesh.Folder);
                mesh = CollarMesh(rIn, rOut, h);
                mesh.name = key;
                AssetDatabase.CreateAsset(mesh, path);
            }
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>Số cạnh quanh trục của vành (CollarMesh) và vật chứa (BuildVessel).</summary>
        public const int CollarSegments = 40, VesselSegments = 28;

        /// <summary>Lưới vành tròn dẹt: mặt trên, thành trong, thành ngoài (gốc ở đáy, không có mặt dưới).</summary>
        public static Mesh CollarMesh(float rIn, float rOut, float h)
        {
            const int N = CollarSegments;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            void Strip(float r0, float y0, float r1, float y1, bool flip)
            {
                int start = v.Count;
                for (int i = 0; i <= N; i++)
                {
                    float a = i * Mathf.PI * 2f / N, c = Mathf.Cos(a), sn = Mathf.Sin(a);
                    v.Add(new Vector3(c * r0, y0, sn * r0)); uv.Add(new Vector2(c * r0, sn * r0));
                    v.Add(new Vector3(c * r1, y1, sn * r1)); uv.Add(new Vector2(c * r1, sn * r1));
                }
                for (int i = 0; i < N; i++)
                {
                    int a0 = start + i * 2, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
                    if (!flip) { tri.Add(a0); tri.Add(b0); tri.Add(a1); tri.Add(a1); tri.Add(b0); tri.Add(b1); }
                    else { tri.Add(a0); tri.Add(a1); tri.Add(b0); tri.Add(a1); tri.Add(b1); tri.Add(b0); }
                }
            }
            // Cùng quy ước mặt trước với BuildVessel (dải đi lên hướng ra, đi vào tâm hướng lên). Bản cũ lật mặt trên và thành ngoài:
            // vành lò nhìn từ trên thấy xuyên (SeeThroughSweep 15/10, StoveRing ~900 px).
            Strip(rOut, h, rIn, h, false);   // mặt trên hướng lên
            Strip(rIn, h, rIn, 0f, false);   // thành trong
            Strip(rOut, 0f, rOut, h, false); // thành ngoài
            var mesh = new Mesh { name = "Collar" };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh BuildVessel(float rb, float rt, float h, float wall, float floor, float tile)
        {
            const int N = VesselSegments;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            float riB = Mathf.Max(0.01f, rb - wall), riT = Mathf.Max(0.01f, rt - wall);
            // Mỗi dải có đỉnh riêng để cạnh cứng giữa thành ngoài, mép, thành trong. Mặt trước của dải là Cross(hướng dải r0→r1, chiều
            // tăng góc): dải đi lên ở mép ngoài thì hướng ra, dải đi xuống ở thành trong thì hướng vào, dải từ tâm ra thì hướng xuống. Bản cũ
            // lật thêm thành trong và đáy ngoài nên lòng sọt chỉ còn mặt sau, bị cull: nhìn vào sọt rỗng thấy xuyên ra sàn (ghi chú 15/10).
            void Strip(float r0, float y0, float r1, float y1, bool flip)
            {
                int start = v.Count;
                float len = Vector2.Distance(new Vector2(r0, y0), new Vector2(r1, y1));
                for (int i = 0; i <= N; i++)
                {
                    float a = i * Mathf.PI * 2f / N, c = Mathf.Cos(a), s = Mathf.Sin(a);
                    float u = a * (r0 + r1) * 0.5f / tile;
                    v.Add(new Vector3(c * r0, y0, s * r0)); uv.Add(new Vector2(u, y0 / tile));
                    v.Add(new Vector3(c * r1, y1, s * r1)); uv.Add(new Vector2(u, (y0 + len * Mathf.Sign(y1 - y0 + 1e-6f)) / tile));
                }
                for (int i = 0; i < N; i++)
                {
                    int a0 = start + i * 2, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
                    if (!flip) { tri.Add(a0); tri.Add(b0); tri.Add(a1); tri.Add(a1); tri.Add(b0); tri.Add(b1); }
                    else { tri.Add(a0); tri.Add(a1); tri.Add(b0); tri.Add(a1); tri.Add(b1); tri.Add(b0); }
                }
            }
            Strip(rb, 0f, rt, h, false);              // thành ngoài, hướng ra ngoài
            Strip(rt, h, riT, h, false);              // mép trên, hướng lên
            Strip(riT, h, riB, floor, false);         // thành trong, hướng vào trong
            Strip(riB, floor, 0f, floor, false);      // đáy trong, hướng lên
            Strip(0f, 0f, rb, 0f, false);             // đáy ngoài, hướng xuống
            var mesh = new Mesh { name = "Vessel" };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            Unwrapping.GenerateSecondaryUVSet(mesh);
            mesh.RecalculateTangents();
            return mesh;
        }

        public static GameObject Shovel(Transform parent, Vector3 position, Vector3 euler)
        {
            var root = BuilderUtils.Group("ArtSlot_Shovel", parent);
            root.transform.localPosition = position;
            root.transform.localRotation = Quaternion.Euler(euler);
            var wood = AssetFactory.Lit("ShovelWood", new Color(0.25f, 0.12f, 0.055f), 0.08f);
            var steel = AssetFactory.Lit("ShovelSteel", new Color(0.085f, 0.075f, 0.065f), 0.2f, 0.65f);
            var edge = AssetFactory.Lit("ShovelWornEdge", new Color(0.34f, 0.35f, 0.36f), 0.38f, 0.8f);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Handle", root.transform, new Vector3(0f, 0.08f, 0f),
                new Vector3(0.032f, 0.31f, 0.032f), wood, false, false);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Socket", root.transform, new Vector3(0f, 0.36f, 0.003f),
                new Vector3(0.046f, 0.085f, 0.046f), steel, false, false);
            for (int i = 0; i < 12; i++)
            {
                Vector3 Arc(int n) => new Vector3(0.075f * Mathf.Cos(n * Mathf.PI / 12f), -0.32f + 0.115f * Mathf.Sin(n * Mathf.PI / 12f), 0f);
                var a = Arc(i); var b = Arc(i + 1);
                BuilderUtils.Prim(PrimitiveType.Cylinder, "DFrame", root.transform, (a + b) * 0.5f,
                    new Vector3(0.014f, Vector3.Distance(a, b) * 0.5f, 0.014f), steel, false, false,
                    Quaternion.FromToRotation(Vector3.up, b - a).eulerAngles);
            }
            BuilderUtils.Prim(PrimitiveType.Cylinder, "DGrip", root.transform, new Vector3(0f, -0.32f, 0f),
                new Vector3(0.033f, 0.085f, 0.033f), wood, false, false, new Vector3(0f, 0f, 90f));
            var blade = BuilderUtils.Group("Blade", root.transform);
            blade.AddComponent<MeshFilter>().sharedMesh = ShovelBlade();
            blade.AddComponent<MeshRenderer>().sharedMaterial = steel;
            void Anchor(string name, Vector3 p)
            {
                var anchor = BuilderUtils.Group(name, root.transform).transform;
                anchor.localPosition = p;
            }
            Anchor("GripL", new Vector3(0f, 0.14f, 0f));
            Anchor("GripR", new Vector3(0f, -0.32f, 0f));
            Anchor("LoadSupport", new Vector3(0f, 0.52f, 0.006f));
            // Mép trước hở, không có thành chắn than; thép mòn sáng chỉ rộng 8 mm.
            BuilderUtils.Block("WornLip", root.transform, new Vector3(0f, 0.645f, -0.006f),
                new Vector3(0.275f, 0.008f, 0.005f), edge, false, false);
            BuilderUtils.ClearStatic(root);
            return root;
        }

        /// <summary>Kê từng cục than lên mặt cong của lưỡi, trong hệ trục xẻng; không suy từ bounds thế giới khi xẻng nghiêng.</summary>
        public static void SeatShovelLoad(Transform shovel, Transform load)
        {
            var support = shovel.Find("LoadSupport");
            load.SetParent(support, false);
            load.localPosition = Vector3.zero;
            foreach (var mf in load.GetComponentsInChildren<MeshFilter>())
            {
                var bounds = HaiBanTay.Editor.Import.ArtImporter.TransformBounds(mf.sharedMesh.bounds, shovel.worldToLocalMatrix * mf.transform.localToWorldMatrix);
                float v = Mathf.Clamp01((bounds.center.y - 0.37f) / 0.28f);
                float side = Mathf.Pow(Mathf.Abs(bounds.center.x) / (Mathf.Lerp(0.24f, 0.275f, v) * 0.5f), 6f);
                float surface = 0.006f * Mathf.Sin(v * Mathf.PI) - 0.018f * Mathf.Max(side * (1f - v), Mathf.Pow(1f - v, 6f));
                mf.transform.position += shovel.TransformVector(Vector3.forward * (surface - bounds.max.z));
            }
        }

        /// <summary>Bao đã cởi dây: vải có nếp, đáy kín và miệng mở để khoai thoát ra.</summary>
        public static GameObject OpenSack(Transform parent)
        {
            const string path = SurfaceMesh.Folder + "/Gen_OpenSack_v1.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                const int rows = 20, segments = 48;
                var v = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>();
                for (int side = 0; side < 2; side++)
                {
                    int offset = v.Count;
                    for (int row = 0; row <= rows; row++)
                    {
                        float t = row / (float)rows;
                        float rx = Mathf.Lerp(0.07f, 0.15f, t) + 0.1f * Mathf.Sin(t * Mathf.PI);
                        float rz = Mathf.Lerp(0.05f, 0.105f, t) + 0.075f * Mathf.Sin(t * Mathf.PI);
                        for (int i = 0; i <= segments; i++)
                        {
                            float a = i * Mathf.PI * 2f / segments;
                            float fold = 0.006f * Mathf.Sin(a * 12f + t * 7f) * Mathf.Sin(t * Mathf.PI) + (row == rows ? 0.004f : 0f);
                            v.Add(new Vector3(Mathf.Cos(a) * (rx + fold - side * 0.003f), -0.3f + t * 0.6f,
                                Mathf.Sin(a) * (rz + fold - side * 0.003f)));
                            uv.Add(new Vector2(i / (float)segments * 1.3f, t * 0.75f));
                        }
                    }
                    for (int row = 0; row < rows; row++)
                        for (int i = 0; i < segments; i++)
                        {
                            int a = offset + row * (segments + 1) + i, b = a + segments + 1;
                            tr.AddRange(side == 0 ? new[] { a, b, a + 1, a + 1, b, b + 1 } : new[] { a, a + 1, b, a + 1, b + 1, b });
                        }
                    int center = v.Count; v.Add(new Vector3(0f, -0.3f, 0f)); uv.Add(Vector2.zero);
                    for (int i = 0; i < segments; i++)
                        tr.AddRange(side == 0 ? new[] { center, offset + i, offset + i + 1 } : new[] { center, offset + i + 1, offset + i });
                }
                mesh = new Mesh { name = "OpenJuteSack", vertices = v.ToArray(), uv = uv.ToArray(), triangles = tr.ToArray() };
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
                AssetFactory.EnsureFolder(SurfaceMesh.Folder); AssetDatabase.CreateAsset(mesh, path);
            }
            var root = BuilderUtils.Group("OpenSack", parent);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterial = AssetFactory.Lit("Burlap", new Color(0.55f, 0.45f, 0.3f), 0.05f);
            root.SetActive(false);
            return root;
        }

        /// <summary>
        /// Bàn chải cọ nồi ngắn (Tool.Brush): bó lông ở đầu, đai gỗ, cán gỗ tròn. Hệ của bàn chải: gốc ở mũi lông, +Y dọc cán về đuôi,
        /// +Z hướng ngón tay khi cầm (BrushReach, BrushScrub.Place). Kích thước theo BrushShape.Short (dài 0,42 m, bó lông bán kính 3,5 cm).
        /// </summary>
        public static GameObject Brush(Transform parent)
        {
            var root = BuilderUtils.Group("PotBrush", parent);
            var shape = HaiBanTay.Tasks.BrushShape.Short;
            var wood = AssetFactory.Lit("BrushWood", new Color(0.32f, 0.18f, 0.08f), 0.1f);
            var fibre = AssetFactory.Lit("BrushFibre", new Color(0.39f, 0.29f, 0.13f), 0.05f);
            float r = shape.HeadRadius;
            // Bó lông: lõi đặc và một vòng chùm lông lởm chởm ở mép (mũi lông không phẳng như khối gỗ).
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Bristles", root.transform, new Vector3(0f, 0.04f, 0f), new Vector3(r * 1.7f, 0.035f, r * 1.7f), fibre, false, false);
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2f / 10f;
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Tuft", root.transform, new Vector3(Mathf.Cos(a) * r * 0.78f, 0.035f + (i % 2) * 0.004f, Mathf.Sin(a) * r * 0.78f),
                    new Vector3(0.014f, 0.035f, 0.014f), fibre, false, false);
            }
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Stock", root.transform, new Vector3(0f, 0.09f, 0f), new Vector3(r * 2.25f, 0.016f, r * 2.25f), wood, false, false);
            float from = 0.105f;
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Handle", root.transform, new Vector3(0f, (from + shape.Length) * 0.5f, 0f),
                new Vector3(shape.HandleRadius * 2f, (shape.Length - from) * 0.5f, shape.HandleRadius * 2f), wood, false, false);
            BuilderUtils.Prim(PrimitiveType.Sphere, "Knob", root.transform, new Vector3(0f, shape.Length, 0f), Vector3.one * 0.034f, wood, false, false);
            BuilderUtils.ClearStatic(root);
            return root;
        }

        /// <summary>
        /// Bàn chải cán dài (Tool.LongBrush): bó lông tre buộc đai ở đầu cán gỗ dài 1,9 m, như chổi cọ chảo lớn. Cùng hệ với Brush (gốc ở mũi
        /// lông, +Y dọc cán), kích thước theo BrushShape.Long.
        /// </summary>
        public static GameObject LongBrush(Transform parent)
        {
            var root = BuilderUtils.Group("LongBrush", parent);
            var shape = HaiBanTay.Tasks.BrushShape.Long;
            var wood = AssetFactory.Lit("BrushWood", new Color(0.32f, 0.18f, 0.08f), 0.1f);
            var fibre = AssetFactory.Lit("BrushFibre", new Color(0.39f, 0.29f, 0.13f), 0.05f);
            var cord = AssetFactory.Lit("BrushCord", new Color(0.24f, 0.2f, 0.15f), 0.05f);
            float r = shape.HeadRadius;
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Bristles", root.transform, new Vector3(0f, 0.085f, 0f), new Vector3(r * 1.75f, 0.08f, r * 1.75f), fibre, false, false);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Strip", root.transform, new Vector3(Mathf.Cos(a) * r * 0.8f, 0.08f + (i % 3) * 0.004f, Mathf.Sin(a) * r * 0.8f),
                    new Vector3(0.016f, 0.08f, 0.016f), fibre, false, false);
            }
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Binding", root.transform, new Vector3(0f, 0.175f, 0f), new Vector3(r * 1.5f, 0.02f, r * 1.5f), cord, false, false);
            float from = 0.19f;
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Handle", root.transform, new Vector3(0f, (from + shape.Length) * 0.5f, 0f),
                new Vector3(shape.HandleRadius * 2f, (shape.Length - from) * 0.5f, shape.HandleRadius * 2f), wood, false, false);
            BuilderUtils.ClearStatic(root);
            return root;
        }

        /// <summary>
        /// Chỗ đặt bàn chải trong nắm tay phải (khung cổ tay HandsRig): cán xuyên qua tâm nắm của mesh Grip theo trục X của bàn tay, mũi lông về
        /// phía ngón út (+X), đuôi cán về phía ngón cái. <paramref name="gripAt"/>: tâm nắm cách mũi lông (m).
        /// </summary>
        public static void SeatInRightFist(Transform brush, float gripAt)
        {
            brush.localPosition = new Vector3(gripAt, -HaiBanTay.Player.HandsRig.GripBelow, HaiBanTay.Player.HandsRig.GripForward);
            brush.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }

        /// <summary>
        /// Mesh tròn xoay quanh trục Y từ một đường biên (r, y), lưu asset theo khóa. Biên đi từ tâm đáy ra ngoài, lên, rồi vào trong thì mặt
        /// hướng ra ngoài/lên (cùng chiều quấn với <see cref="Pot"/>).
        /// </summary>
        public static Mesh Lathe(string key, Vector2[] profile, int segments = 40)
        {
            string path = SurfaceMesh.Folder + "/" + key + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            var v = new List<Vector3>();
            var tr = new List<int>();
            for (int row = 0; row < profile.Length; row++)
                for (int i = 0; i <= segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    v.Add(new Vector3(Mathf.Cos(a) * profile[row].x, profile[row].y, Mathf.Sin(a) * profile[row].x));
                }
            for (int row = 0; row < profile.Length - 1; row++)
                for (int i = 0; i < segments; i++)
                {
                    int a = row * (segments + 1) + i, b = a + segments + 1;
                    tr.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            mesh = new Mesh { name = key, vertices = v.ToArray(), triangles = tr.ToArray() };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetFactory.EnsureFolder(SurfaceMesh.Folder);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>Nồi rỗng có đáy, thành và vành; kích thước tính bằng mét.</summary>
        public static GameObject Pot(Transform parent, string name, float diameter, float height, Material metal)
        {
            var root = BuilderUtils.Group(name, parent);
            string key = $"Gen_ChorePot_{Mathf.RoundToInt(diameter * 100)}_{Mathf.RoundToInt(height * 100)}";
            string path = SurfaceMesh.Folder + "/" + key + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                var points = new[] { new Vector2(0f, 0f), new Vector2(diameter * 0.45f, 0f),
                    new Vector2(diameter * 0.5f, 0.035f), new Vector2(diameter * 0.5f, height),
                    new Vector2(diameter * 0.5f - 0.012f, height), new Vector2(diameter * 0.45f - 0.012f, 0.025f), new Vector2(0f, 0.025f) };
                var v = new List<Vector3>();
                var tr = new List<int>();
                const int segments = 32;
                for (int row = 0; row < points.Length; row++)
                    for (int i = 0; i <= segments; i++)
                    {
                        float a = i * Mathf.PI * 2f / segments;
                        v.Add(new Vector3(Mathf.Cos(a) * points[row].x, points[row].y, Mathf.Sin(a) * points[row].x));
                    }
                for (int row = 0; row < points.Length - 1; row++)
                    for (int i = 0; i < segments; i++)
                    {
                        int a = row * (segments + 1) + i, b = a + segments + 1;
                        tr.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                    }
                mesh = new Mesh { name = key, vertices = v.ToArray(), triangles = tr.ToArray() };
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                // Vật động không bake lightmap; tránh tách UV trên mặt lá hai phía.
                AssetFactory.EnsureFolder(SurfaceMesh.Folder);
                AssetDatabase.CreateAsset(mesh, path);
            }
            var body = BuilderUtils.Group("Body", root.transform);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            body.AddComponent<MeshRenderer>().sharedMaterial = metal;
            foreach (float sign in new[] { -1f, 1f })
                BuilderUtils.Block("Handle", root.transform, new Vector3(sign * (diameter * 0.5f + 0.045f), height * 0.75f, 0f),
                    new Vector3(0.09f, 0.02f, 0.09f), metal, false, false);
            BuilderUtils.ClearStatic(root);
            return root;
        }

        public static GameObject Leaf(Transform parent, string name, Material green)
        {
            const string path = SurfaceMesh.Folder + "/Gen_CabbageLeaf_v3.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                var vertices = new List<Vector3>(); var triangles = new List<int>();
                const int rows = 14, cols = 8, face = (rows + 1) * (cols + 1);
                float Height(float z, float x) => 0.035f * Mathf.Sin(z / 0.25f * Mathf.PI)
                    + 0.045f * Mathf.Pow(Mathf.Abs(x) / 0.065f, 2f)
                    + 0.008f * Mathf.Sin(z * 55f + x * 32f) * Mathf.Abs(x) / 0.065f;
                // Vỏ lá kín 1,5 mm, lòng võng và mép cuộn; pháp tuyến riêng cho hai mặt.
                for (int layer = 0; layer < 2; layer++)
                    for (int z = 0; z <= rows; z++) for (int x = 0; x <= cols; x++)
                    {
                        float t = z / (float)rows;
                        float w = 0.065f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI)), 0.7f)
                            * (1f + 0.08f * Mathf.Sin(t * 45f));
                        float u = (x / (float)cols * 2f - 1f) * w;
                        vertices.Add(new Vector3(u, Height(t * 0.25f, u) - layer * 0.0015f, t * 0.25f));
                        if (z < rows && x < cols)
                        {
                            int a = layer * face + z * (cols + 1) + x, b = a + cols + 1;
                            triangles.AddRange(layer == 0 ? new[] { a, b, a + 1, a + 1, b, b + 1 }
                                : new[] { a, a + 1, b, a + 1, b + 1, b });
                        }
                    }
                void Edge(int a, int b) => triangles.AddRange(new[] { a, b, b + face, a, b + face, a + face });
                for (int z = 0; z < rows; z++)
                { Edge(z * (cols + 1), (z + 1) * (cols + 1)); Edge((z + 1) * (cols + 1) + cols, z * (cols + 1) + cols); }
                for (int x = 0; x < cols; x++)
                { Edge(x + 1, x); Edge(rows * (cols + 1) + x, rows * (cols + 1) + x + 1); }
                void Rib(Vector3 from, Vector3 to, float radius)
                {
                    int first = vertices.Count;
                    var axis = (to - from).normalized;
                    var across = Vector3.Cross(axis, Vector3.up).normalized;
                    var up = Vector3.Cross(across, axis).normalized;
                    const int sides = 6, rings = 5;
                    for (int j = 0; j < rings; j++)
                    {
                        float t = j / (float)(rings - 1);
                        var center = Vector3.Lerp(from, to, t);
                        if (center.z > 0f) center.y = Height(center.z, center.x) + 0.002f;
                        for (int k = 0; k < sides; k++)
                        {
                            float angle = k * Mathf.PI * 2f / sides;
                            vertices.Add(center + (up * Mathf.Cos(angle) + across * Mathf.Sin(angle)) * radius * Mathf.Lerp(1f, 0.25f, t));
                        }
                    }
                    for (int j = 0; j < rings - 1; j++) for (int k = 0; k < sides; k++)
                    {
                        int a = first + j * sides + k, b = first + j * sides + (k + 1) % sides;
                        triangles.AddRange(new[] { a, b, a + sides, b, b + sides, a + sides });
                    }
                }
                Rib(new Vector3(0f, 0f, -0.06f), new Vector3(0f, 0f, 0.22f), 0.005f);
                for (int i = 1; i <= 3; i++) foreach (float sign in new[] { -1f, 1f })
                    Rib(new Vector3(0f, 0f, i * 0.05f), new Vector3(sign * 0.045f, 0f, i * 0.05f + 0.035f), 0.0016f);
                mesh = new Mesh { name = "CurledCabbageLeaf", vertices = vertices.ToArray(), triangles = triangles.ToArray() };
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                AssetFactory.EnsureFolder(SurfaceMesh.Folder); AssetDatabase.CreateAsset(mesh, path);
            }
            var root = BuilderUtils.Group(name, parent);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            green.enableInstancing = true;
            root.AddComponent<MeshRenderer>().sharedMaterial = green;
            return root;
        }

        public static GameObject Bunch(Transform parent, string name, Material green)
        {
            var root = BuilderUtils.Group(name, parent);
            for (int i = 0; i < 6; i++)
            {
                var leaf = Leaf(root.transform, "Leaf_" + i, green);
                leaf.transform.localPosition = new Vector3((i % 2 - 0.5f) * 0.035f, i * 0.006f, -0.05f);
                leaf.transform.localRotation = Quaternion.Euler(-8f + i * 4f, -40f + i * 16f, 0f);
            }
            return root;
        }

        public static Mesh ShovelBlade()
        {
            const string path = SurfaceMesh.Folder + "/Gen_ShallowShovel_v1.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            // Mặt -Z là lòng xẻng, độ võng 6 mm, gờ bên/sau cao tối đa 18 mm.
            const int rows = 8, cols = 8;
            for (int y = 0; y <= rows; y++)
                for (int x = 0; x <= cols; x++)
                {
                    float u = x / (float)cols, v = y / (float)rows;
                    float side = Mathf.Pow(Mathf.Abs(u * 2f - 1f), 6f);
                    float back = Mathf.Pow(1f - v, 6f);
                    float z = 0.006f * Mathf.Sin(v * Mathf.PI) - 0.018f * Mathf.Max(side * (1f - v), back);
                    vertices.Add(new Vector3((u - 0.5f) * Mathf.Lerp(0.24f, 0.275f, v), 0.37f + v * 0.28f, z));
                }
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                {
                    int a = y * (cols + 1) + x, b = a + cols + 1;
                    triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            // Mặt sau tấm thép: dày 3 mm, pháp tuyến ngược.
            int count = vertices.Count, indexCount = triangles.Count;
            for (int i = 0; i < count; i++) vertices.Add(vertices[i] + Vector3.forward * 0.003f);
            for (int i = 0; i < indexCount; i += 3)
                triangles.AddRange(new[] { triangles[i] + count, triangles[i + 2] + count, triangles[i + 1] + count });
            // Nối kín bốn mép tấm thép, winding ngược cạnh biên của mặt lòng xẻng.
            void Edge(int a, int b) => triangles.AddRange(new[] { b, a, a+count, b, a+count, b+count });
            for (int y = 0; y < rows; y++)
            {
                Edge(y * (cols+1), (y+1) * (cols+1));
                Edge((y+1) * (cols+1) + cols, y * (cols+1) + cols);
            }
            for (int x = 0; x < cols; x++)
            {
                Edge(x+1, x);
                Edge(rows * (cols+1) + x, rows * (cols+1) + x+1);
            }
            var mesh = existing != null ? existing : new Mesh { name = "ShallowCoalShovel" };
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            // Xẻng là vật động, không cần UV lightmap.
            AssetFactory.EnsureFolder(SurfaceMesh.Folder);
            if (existing == null) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
