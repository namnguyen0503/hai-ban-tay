using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.Setup
{
    /// <summary>
    /// Vật liệu, texture và mesh cho cảnh chiếu phim 1911 ở phố Sài Gòn: tấm vải có vùng sáng bo góc, chùm sáng máy chiếu,
    /// vệt ánh hắt trên nền, cuộn phim có nan. Đổi cách sinh thì tăng hậu tố phiên bản trong tên file.
    /// </summary>
    public static partial class AssetFactory
    {
        public const float ScreenWidth = 3f, ScreenHeight = 1.85f;
        // Vùng sáng chiếu trên vải (mét, nửa kích thước quanh tâm) và bán kính bo góc.
        public const float LitHalfWidth = 1.38f, LitHalfHeight = 0.83f, LitCorner = 0.2f;

        /// <summary>Vải màn: emission map là hình chữ nhật sáng bo góc mép mềm, ngoài vùng sáng chỉ còn ánh hắt xám ấm.</summary>
        public static Material ProjectionScreen()
        {
            var map = EnsureTexture("Gen_ProjectionScreen_v2.png", 512, 320, false, TextureWrapMode.Clamp, DrawProjectionScreen);
            return GetMaterial("ProjectionScreen", "Universal Render Pipeline/Lit", m =>
            {
                m.SetColor("_BaseColor", new Color(0.62f, 0.58f, 0.5f));
                m.SetFloat("_Smoothness", 0.05f);
                m.SetFloat("_Metallic", 0f);
                m.SetTexture("_EmissionMap", map);
                m.SetColor("_EmissionColor", new Color(0.75f, 0.65f, 0.5f));
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            });
        }

        /// <summary>Chùm sáng máy chiếu: cộng sáng, nhạt dần về phía màn, có vệt bụi dọc chùm. UV: u ngang chùm, v từ ống kính (0) tới màn (1).</summary>
        public static Material ProjectorBeam()
        {
            var tex = EnsureTexture("Gen_ProjectorBeam_v6.png", 64, 256, true, TextureWrapMode.Clamp, DrawProjectorBeam);
            return Additive("ProjectorBeam", tex, new Color(1.25f, 0.95f, 0.6f, 0.31f));
        }

        /// <summary>Quầng ánh hắt trên nền phố trước màn (quad nằm ngang, mép mờ dần).</summary>
        public static Material ProjectorSpill() =>
            Additive("ProjectorSpill", EnsureFireTexture(FireGlowName, 64, false, DrawFireGlow), new Color(1f, 0.8f, 0.5f, 0.4f));

        /// <summary>Kính thấu kính phát sáng ấm (điểm sáng đầu chùm).</summary>
        public static Material ProjectorLens() =>
            Emissive("ProjectorLens", new Color(0.8f, 0.72f, 0.55f), new Color(3.2f, 2.6f, 1.7f), bakeGlow: false, smoothness: 0.9f);

        /// <summary>Cuộn phim hở nan, trục theo Y đơn vị: vành dày, 6 nan, ống giữa. Một mesh dùng chung cho cả hai cuộn.</summary>
        public static Mesh FilmReelMesh()
        {
            string folder = Root + "/Art/Meshes";
            string path = folder + "/Gen_FilmReel_v1.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            EnsureFolder(folder);
            var v = new List<Vector3>();
            var t = new List<int>();
            // Mỗi mặt có đỉnh riêng (bóng phẳng); thứ tự đỉnh tự đảo để pháp tuyến quay ra ngoài theo hướng gợi ý.
            void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) { var s = b; b = c; c = s; }
                int i = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward) { Tri(a, b, c, outward); Tri(a, c, d, outward); }
            Vector3 P(float r, float angle, float y) => new Vector3(Mathf.Cos(angle) * r, y, Mathf.Sin(angle) * r);
            void Tube(float rIn, float rOut, float half, int n)
            {
                for (int k = 0; k < n; k++)
                {
                    float a0 = k * Mathf.PI * 2f / n, a1 = (k + 1) * Mathf.PI * 2f / n, mid = (a0 + a1) * 0.5f;
                    var radial = P(1f, mid, 0f);
                    Quad(P(rOut, a0, half), P(rOut, a1, half), P(rOut, a1, -half), P(rOut, a0, -half), radial);
                    if (rIn > 0f)
                    {
                        Quad(P(rIn, a0, half), P(rIn, a1, half), P(rIn, a1, -half), P(rIn, a0, -half), -radial);
                        Quad(P(rIn, a0, half), P(rOut, a0, half), P(rOut, a1, half), P(rIn, a1, half), Vector3.up);
                        Quad(P(rIn, a0, -half), P(rOut, a0, -half), P(rOut, a1, -half), P(rIn, a1, -half), Vector3.down);
                    }
                    else
                    {
                        Tri(new Vector3(0f, half, 0f), P(rOut, a0, half), P(rOut, a1, half), Vector3.up);
                        Tri(new Vector3(0f, -half, 0f), P(rOut, a0, -half), P(rOut, a1, -half), Vector3.down);
                    }
                }
            }
            Tube(0.86f, 1f, 0.07f, 40);
            Tube(0f, 0.2f, 0.15f, 20);
            const int spokes = 6;
            for (int s = 0; s < spokes; s++)
            {
                float phi = s * Mathf.PI * 2f / spokes;
                var d = P(1f, phi, 0f);
                var p = new Vector3(-Mathf.Sin(phi), 0f, Mathf.Cos(phi));
                const float r0 = 0.17f, r1 = 0.9f, hw = 0.04f, hy = 0.045f;
                Vector3 C(float r, float side, float y) => d * r + p * (side * hw) + Vector3.up * y;
                Quad(C(r0, -1, hy), C(r1, -1, hy), C(r1, 1, hy), C(r0, 1, hy), Vector3.up);
                Quad(C(r0, -1, -hy), C(r1, -1, -hy), C(r1, 1, -hy), C(r0, 1, -hy), Vector3.down);
                Quad(C(r0, -1, hy), C(r1, -1, hy), C(r1, -1, -hy), C(r0, -1, -hy), -p);
                Quad(C(r0, 1, hy), C(r1, 1, hy), C(r1, 1, -hy), C(r0, 1, -hy), p);
            }
            var mesh = new Mesh { name = "FilmReel" };
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Texture2D EnsureTexture(string file, int width, int height, bool mips, TextureWrapMode wrap,
            System.Action<Texture2D, int, int> draw)
        {
            EnsureFolder(TexturesFolder);
            string path = TexturesFolder + "/" + file;
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;
            var t = new Texture2D(width, height, TextureFormat.RGBA32, false);
            draw(t, width, height);
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.maxTextureSize = Mathf.Max(width, height);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = mips;
            importer.wrapMode = wrap;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void DrawProjectionScreen(Texture2D tex, int w, int h)
        {
            var white = new Color(1f, 0.98f, 0.93f);
            var edge = new Color(1f, 0.87f, 0.68f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    float px = (u - 0.5f) * ScreenWidth, py = (v - 0.5f) * ScreenHeight;
                    // Khoảng cách có dấu tới hình chữ nhật bo góc (âm = bên trong).
                    float qx = Mathf.Abs(px) - (LitHalfWidth - LitCorner), qy = Mathf.Abs(py) - (LitHalfHeight - LitCorner);
                    float d = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f))
                              + Mathf.Min(Mathf.Max(qx, qy), 0f) - LitCorner;
                    float lit = 1f - Smooth(-0.07f, 0.09f, d);
                    float inner = Mathf.Clamp01((px * px) / (LitHalfWidth * LitHalfWidth) * 0.55f + (py * py) / (LitHalfHeight * LitHalfHeight) * 0.45f);
                    // Vải: thớ mịn và vài nếp võng ngang làm sáng không phẳng tuyệt đối.
                    float cloth = 1f + 0.035f * (Mathf.PerlinNoise(x * 0.9f, y * 0.9f) - 0.5f) * 2f
                                  + 0.025f * Mathf.Sin(v * 17f + Mathf.PerlinNoise(u * 3f, 4f) * 6f);
                    float spill = 0.3f;
                    float value = (spill * (0.9f + 0.2f * Mathf.PerlinNoise(u * 2.2f + 5f, v * 2.2f)) + (1f - spill) * lit * (1f - 0.2f * inner)) * cloth;
                    var tint = Color.Lerp(white, edge, Mathf.Clamp01(inner * 1.1f));
                    tex.SetPixel(x, y, new Color(tint.r * value, tint.g * value, tint.b * value, 1f));
                }
        }

        static void DrawProjectorBeam(Texture2D tex, int w, int h)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    float side = Smooth(0f, 0.5f, u) * Smooth(0f, 0.5f, 1f - u);
                    // Nhạt dần rồi tắt hẳn trước màn: chùm không đè lên bóng người trên vải.
                    float along = Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(v / 0.75f)) * Smooth(0f, 0.05f, v) * (1f - Smooth(0.16f, 0.4f, v));
                    float dust = 0.65f + 0.7f * Mathf.PerlinNoise(u * 9f + 3f, v * 2.4f + 1f);
                    float a = Mathf.Clamp01(side * along * dust);
                    tex.SetPixel(x, y, new Color(1f, 0.93f, 0.78f, a));
                }
        }
    }
}
