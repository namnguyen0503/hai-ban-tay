using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace HaiBanTay.Editor.Setup
{
    /// <summary>
    /// Vật liệu và texture lửa sinh bằng code (không ray tracing, không shader riêng): URP Particles/Unlit cộng sáng (additive).
    /// Sprite sheet 4x4 ngọn lửa trong hốc, hơi nồi mềm và mesh than góc cạnh dùng chung.
    /// Đổi cách sinh thì tăng hậu tố phiên bản trong tên file để asset cũ được sinh lại.
    /// </summary>
    public static partial class AssetFactory
    {
        const string FlameSheetName = "Gen_FlameSheet_v4.png";
        const string FireRingName = "Gen_FireRing_v4.png";
        const string FireGlowName = "Gen_FireGlow_v4.png";

        /// <summary>Gang đúc có hạt rỗ nhẹ; texture chung 128 px, không thêm shader hoặc texture lớn.</summary>
        public static Material RangeIron()
        {
            var mat = Lit("RangeIron", new Color(0.19f, 0.185f, 0.175f), 0.16f, 0.65f);
            mat.SetTexture("_BaseMap", EnsureFireTexture("Gen_CastIron_v1.png", 128, false, (tex, size) =>
            {
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float grain = Mathf.PerlinNoise(x * 0.73f + 17f, y * 0.73f + 31f);
                        float mottling = Mathf.PerlinNoise(x * 0.08f + 4f, y * 0.08f + 9f);
                        float shade = 0.65f + 0.25f * grain + 0.1f * mottling;
                        tex.SetPixel(x, y, new Color(shade, shade, shade, 1f));
                    }
            }));
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Ngọn lửa hạt: sprite sheet 4x4, _BaseColor HDR để Bloom bắt phần lõi.</summary>
        public static Material FlameMaterial() =>
            Additive("FireFlame", EnsureFireTexture(FlameSheetName, 256, true, DrawFlameSheet), new Color(2.1f, 1.7f, 1.3f, 1f));

        /// <summary>Vòng lửa bò từ khe nắp ra quanh đáy nồi (quad phẳng trên mặt bếp).</summary>
        public static Material FireRingMaterial() =>
            Additive("FireRingAdd", EnsureFireTexture(FireRingName, 128, false, DrawFireRing), new Color(1.5f, 1.15f, 0.95f, 1f));

        /// <summary>Quầng sáng mềm hình tròn (hạt quầng, vệt sáng trên sàn, tia lửa).</summary>
        public static Material FireHaloMaterial() =>
            Additive("FireHaloAdd", EnsureFireTexture(FireGlowName, 64, false, DrawFireGlow), new Color(1.1f, 0.55f, 0.2f, 1f));

        /// <summary>Hơi nồi dùng alpha mềm, không cộng màu như lửa.</summary>
        public static Material SteamMaterial()
        {
            var mat = Additive("PotSteam", EnsureFireTexture(FireGlowName, 64, false, DrawFireGlow), Color.white);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            return mat;
        }

        /// <summary>Than vỡ góc cạnh: 24 đỉnh, 12 tam giác; sinh một mesh dùng chung, không dùng quả cầu.</summary>
        public static Mesh CoalLumpMesh()
        {
            string folder = Root + "/Art/Meshes";
            string path = folder + "/Gen_CoalLump_v1.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            EnsureFolder(folder);
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh = Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh);
            Object.DestroyImmediate(primitive);
            mesh.name = "CoalLump";
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i];
                // Các đỉnh trùng ở đường UV cùng biến dạng, không tạo khe mesh.
                vertices[i] = v * (0.65f + 0.28f * Mathf.PerlinNoise(v.x * 17f + v.z * 9f + 3f, v.y * 13f + 8f));
            }
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>Ụ than đơn vị: đáy tròn bán kính 1 ở y = 0, đỉnh cao 1, sườn gồ ghề theo nhiễu (dùng chung, scale theo đống).</summary>
        public static Mesh CoalMoundMesh()
        {
            string folder = Root + "/Art/Meshes";
            string path = folder + "/Gen_CoalMound_v1.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            EnsureFolder(folder);
            const int n = 28;
            var vertices = new Vector3[(n + 1) * (n + 1)];
            var uv = new Vector2[vertices.Length];
            var triangles = new System.Collections.Generic.List<int>();
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                {
                    float x = i / (float)n * 2f - 1f, z = j / (float)n * 2f - 1f;
                    // Lưới vuông ép vào đĩa tròn để mép đáy tròn đều.
                    float dx = x * Mathf.Sqrt(1f - z * z * 0.5f), dz = z * Mathf.Sqrt(1f - x * x * 0.5f);
                    vertices[j * (n + 1) + i] = new Vector3(dx, CoalMoundHeight(dx, dz), dz);
                    uv[j * (n + 1) + i] = new Vector2(dx * 0.5f + 0.5f, dz * 0.5f + 0.5f);
                    if (i < n && j < n)
                    {
                        int a = j * (n + 1) + i, b = a + n + 1;
                        triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                    }
                }
            mesh = new Mesh { name = "CoalMound" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>Độ cao ụ than đơn vị tại (x, z) trong đĩa bán kính 1.</summary>
        public static float CoalMoundHeight(float x, float z)
        {
            float r2 = Mathf.Clamp01(x * x + z * z);
            float dome = Mathf.Pow(1f - r2, 0.75f);
            float bumps = 0.16f * (Mathf.PerlinNoise(x * 3.1f + 7f, z * 3.1f + 3f) - 0.5f) + 0.07f * (Mathf.PerlinNoise(x * 9f + 1f, z * 9f + 5f) - 0.5f);
            return Mathf.Max(0f, dome * (1f + bumps * 2f));
        }

        static Material Additive(string name, Texture2D tex, Color color)
        {
            return GetMaterial(name, "Universal Render Pipeline/Particles/Unlit", m =>
            {
                m.SetTexture("_BaseMap", tex);
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 2f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.One);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_Cull", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetOverrideTag("RenderType", "Transparent");
            });
        }

        static Texture2D EnsureFireTexture(string file, int size, bool noMips, System.Action<Texture2D, int> draw)
        {
            EnsureFolder(TexturesFolder);
            string path = TexturesFolder + "/" + file;
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            draw(t, size);
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.maxTextureSize = size;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = !noMips;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Dải màu lửa theo độ đậm i (0–1): đỏ sậm, cam, vàng, lõi vàng trắng.</summary>
        static Color FlameRamp(float i)
        {
            var dark = new Color(0.6f, 0.08f, 0f);
            var orange = new Color(1f, 0.36f, 0.03f);
            var amber = new Color(1f, 0.64f, 0.13f);
            var yellow = new Color(1f, 0.86f, 0.42f);
            var white = new Color(1f, 0.97f, 0.82f);
            if (i < 0.18f) return Color.Lerp(dark, orange, i / 0.18f);
            if (i < 0.4f) return Color.Lerp(orange, amber, (i - 0.18f) / 0.22f);
            if (i < 0.7f) return Color.Lerp(amber, yellow, (i - 0.4f) / 0.3f);
            return Color.Lerp(yellow, white, Mathf.Clamp01((i - 0.7f) / 0.3f));
        }

        static float Smooth(float a, float b, float x)
        {
            float k = Mathf.Clamp01((x - a) / (b - a));
            return k * k * (3f - 2f * k);
        }

        /// <summary>16 khung ngọn lửa: hình giọt, mép mềm (Gauss), gốc rộng thon dần, lắc ngang và lưỡi lửa theo nhiễu Perlin.</summary>
        static void DrawFlameSheet(Texture2D tex, int size)
        {
            int cell = size / 4;
            for (int f = 0; f < 16; f++)
            {
                int cx = (f % 4) * cell, cy = (f / 4) * cell;
                float seed = f * 13.7f + 3.1f;
                for (int py = 0; py < cell; py++)
                    for (int px = 0; px < cell; px++)
                    {
                        float u = (px + 0.5f) / cell * 2f - 1f;
                        float v = (py + 0.5f) / cell;
                        float sway = (Mathf.PerlinNoise(seed, v * 1.5f) * 2f - 1f) * 0.32f * v
                                     + (Mathf.PerlinNoise(seed + 40f, v * 4f) * 2f - 1f) * 0.07f * v;
                        float width = 0.8f * Mathf.Pow(1f - v, 0.7f) * (0.55f + 0.45f * Smooth(0f, 0.25f, v));
                        float x = (u - sway) / Mathf.Max(width, 0.03f);
                        float body = Mathf.Exp(-x * x * 1.4f);
                        float tongues = Mathf.PerlinNoise(u * 2.6f + seed, v * 3.1f - seed * 0.31f);
                        float i = body * Mathf.Pow(1f - v, 0.6f) * (0.75f + 0.5f * tongues) * 1.45f;
                        i = Mathf.Clamp01(i);
                        int edge = Mathf.Min(Mathf.Min(px, cell - 1 - px), Mathf.Min(py, cell - 1 - py));
                        i *= Mathf.Clamp01(edge / 4f);
                        var c = FlameRamp(i);
                        tex.SetPixel(cx + px, cy + py, new Color(c.r, c.g, c.b, Smooth(0.0f, 0.95f, i)));
                    }
            }
        }

        /// <summary>Vòng sáng quanh tâm (đỉnh ở r ≈ 0,58), răng cưa lưỡi lửa theo góc; dùng làm lửa liếm ra từ khe nắp quanh đáy nồi.</summary>
        static void DrawFireRing(Texture2D tex, int size)
        {
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float spokes = Mathf.PerlinNoise(u * 5.5f + 7f, v * 5.5f + 2f);
                    float fine = Mathf.PerlinNoise(u * 13f + 1f, v * 13f + 9f);
                    float band = Mathf.Exp(-Mathf.Pow((r - 0.6f) / (0.14f + 0.1f * spokes), 2f));
                    float i = band * (0.55f + 0.7f * spokes) * (0.8f + 0.4f * fine);
                    i *= 1f - Smooth(0.82f, 1f, r);
                    // Rỗng ở giữa (dưới đáy nồi): không vẽ lên thân nồi chỗ quad cắt qua đáy cong.
                    i *= Smooth(0.46f, 0.6f, r);
                    i = Mathf.Clamp01(i);
                    var c = FlameRamp(i * 0.85f);
                    tex.SetPixel(x, y, new Color(c.r, c.g, c.b, Smooth(0.02f, 0.7f, i)));
                }
        }

        /// <summary>Quầng sáng mềm cam: alpha giảm dần theo bán kính; màu ấm, tâm hơi vàng.</summary>
        static void DrawFireGlow(Texture2D tex, int size)
        {
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Clamp01(Mathf.Sqrt(u * u + v * v));
                    float i = Mathf.Pow(1f - r, 2.2f);
                    var c = Color.Lerp(new Color(1f, 0.5f, 0.15f), new Color(1f, 0.82f, 0.5f), i * i);
                    tex.SetPixel(x, y, new Color(c.r, c.g, c.b, i));
                }
        }
    }
}
