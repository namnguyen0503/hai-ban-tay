using System.IO;
using HaiBanTay.Editor.Setup;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>
    /// Mặt tiền quán cà phê Pháp ở phố Sài Gòn (00_Prologue). Model giao tấm "CafeGlass" là bốn ô phẳng một màu sáng; ở đây tấm
    /// kính đổi sang vật liệu có hình nội thất quán nhìn mờ qua kính (đèn treo, kệ chai, quầy, bàn ghế và bóng người ngồi), nhẹ
    /// phát sáng để đọc được ban đêm, và thêm hai cánh cửa kính có khung gỗ, ô kính chia nan, tay nắm đồng ở chỗ cửa
    /// (anchor cafe_door). Chỉ là hình: ánh sáng do PrologueSceneBuilder đặt.
    /// </summary>
    public static class SaigonStorefronts
    {
        public const string TexturePath = "Assets/_Project/Art/Generated/Prologue/CafeInterior.png";
        const string MaterialPath = "Assets/_Project/Art/Materials/Gen_CafeInterior.mat";
        /// <summary>Mặt trước tấm kính của model (hướng +Z, ra phố).</summary>
        const float GlassFaceZ = -9.84f;
        const int Size = 256;

        public static void Dress(Transform root, GameObject model)
        {
            var interior = InteriorMaterial();
            int replaced = 0;
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.name.StartsWith("Shop_", System.StringComparison.Ordinal))
                    continue;
                var mats = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].name == "Gen_Env_CafeGlass")
                    {
                        mats[i] = interior;
                        changed = true;
                        replaced++;
                    }
                if (changed)
                    renderer.sharedMaterials = mats;
            }
            if (replaced == 0)
                throw new System.InvalidOperationException("SaigonStorefronts: không thấy tấm CafeGlass trong model phố.");
            BuildCafeDoors(root);
        }

        static Material InteriorMaterial()
        {
            AssetFactory.EnsureFolder("Assets/_Project/Art/Materials");
            var tex = EnsureTexture();
            var m = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, MaterialPath);
            }
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseMap", tex);
            // Một ô kính của model cao/rộng ~2,65 UV: mỗi ô hiện trọn một bức nội thất.
            m.SetTextureScale("_BaseMap", new Vector2(1f / 2.65f, 1f / 2.65f));
            m.SetFloat("_Smoothness", 0.85f);
            m.SetFloat("_Metallic", 0f);
            m.SetTexture("_EmissionMap", tex);
            m.SetTextureScale("_EmissionMap", new Vector2(1f / 2.65f, 1f / 2.65f));
            m.SetColor("_EmissionColor", new Color(0.8f, 0.7f, 0.58f));
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------- Cửa kính có khung ----------

        static void BuildCafeDoors(Transform root)
        {
            var door = BuilderUtils.Group("CafeDoors", root).transform;
            var wood = AssetFactory.Lit("Wood", new Color(0.4f, 0.27f, 0.16f), 0.15f);
            var brass = AssetFactory.Lit("Brass", new Color(0.7f, 0.58f, 0.3f), 0.55f, 0.8f);
            float z = GlassFaceZ + 0.07f;
            const float cx = 12f, half = 1.15f, bottom = 0.14f, top = 2.82f;
            // Khung: hai trụ, thanh ngang trên, bậc cửa, ngưỡng.
            foreach (float x in new[] { cx - half, cx + half })
                BuilderUtils.Block("Jamb", door, new Vector3(x, (bottom + top) * 0.5f, z), new Vector3(0.12f, top - bottom, 0.12f), wood, true, false);
            BuilderUtils.Block("Header", door, new Vector3(cx, top, z), new Vector3(half * 2f + 0.12f, 0.14f, 0.14f), wood, true, false);
            BuilderUtils.Block("Threshold", door, new Vector3(cx, bottom + 0.02f, z + 0.05f), new Vector3(half * 2f, 0.05f, 0.3f), wood, true, false);
            // Hai cánh: viền gỗ, chân cánh bịt ván cao 0,7 m, nan chia kính 2 cột × 4 hàng, tay nắm đồng ở giữa.
            foreach (float side in new[] { -1f, 1f })
            {
                float leafCenter = cx + side * half * 0.5f;
                float w = half - 0.1f;
                BuilderUtils.Block("LeafStile", door, new Vector3(cx + side * 0.04f, (bottom + top) * 0.5f - 0.05f, z + 0.02f),
                    new Vector3(0.07f, top - bottom - 0.2f, 0.08f), wood, true, false);
                BuilderUtils.Block("LeafPanel", door, new Vector3(leafCenter, bottom + 0.36f, z + 0.025f), new Vector3(w, 0.62f, 0.05f), wood, true, false);
                BuilderUtils.Block("LeafRailTop", door, new Vector3(leafCenter, top - 0.16f, z + 0.025f), new Vector3(w, 0.07f, 0.06f), wood, true, false);
                BuilderUtils.Block("LeafRailMid", door, new Vector3(leafCenter, bottom + 0.76f, z + 0.025f), new Vector3(w, 0.06f, 0.06f), wood, true, false);
                for (int c = 1; c < 2; c++)
                    BuilderUtils.Block("Muntin", door, new Vector3(leafCenter - side * 0.0f, (bottom + 0.76f + top - 0.16f) * 0.5f, z + 0.025f),
                        new Vector3(0.035f, top - 0.16f - bottom - 0.76f, 0.04f), wood, true, false);
                for (int r = 1; r < 4; r++)
                {
                    float y = Mathf.Lerp(bottom + 0.76f, top - 0.16f, r / 4f);
                    BuilderUtils.Block("Muntin", door, new Vector3(leafCenter, y, z + 0.025f), new Vector3(w, 0.03f, 0.035f), wood, true, false);
                }
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Handle", door, new Vector3(cx + side * 0.2f, 1.1f, z + 0.1f), new Vector3(0.025f, 0.22f, 0.025f),
                    brass, true, false);
            }
        }

        // ---------- Hình nội thất mờ ----------

        static Texture2D EnsureTexture()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (existing != null)
                return existing;
            AssetFactory.EnsureFolder("Assets/_Project/Art/Generated/Prologue");
            var px = new Color[Size * Size];
            Paint(px);
            Blur(px, 2);
            Blur(px, 1);
            // Mặt panel của model có v hướng xuống: lật dọc để đèn treo từ trần.
            for (int y = 0; y < Size / 2; y++)
                for (int x = 0; x < Size; x++)
                {
                    var t = px[y * Size + x];
                    px[y * Size + x] = px[(Size - 1 - y) * Size + x];
                    px[(Size - 1 - y) * Size + x] = t;
                }
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), TexturePath), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(TexturePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        }

        static float Hash(int a, int b)
        {
            unchecked
            {
                uint h = (uint)a * 2654435761u ^ (uint)b * 40503u + 0x9E3779B9u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        static void Add(Color[] px, int x, int y, Color c, float w)
        {
            if (x < 0 || y < 0 || x >= Size || y >= Size || w <= 0f)
                return;
            ref var p = ref px[y * Size + x];
            p = new Color(p.r + c.r * w, p.g + c.g * w, p.b + c.b * w, 1f);
        }

        static void Mix(Color[] px, int x, int y, Color c, float w)
        {
            if (x < 0 || y < 0 || x >= Size || y >= Size)
                return;
            ref var p = ref px[y * Size + x];
            p = Color.Lerp(p, c, Mathf.Clamp01(w));
            p.a = 1f;
        }

        static void Rect(Color[] px, float u0, float v0, float u1, float v1, Color c, float alpha = 1f)
        {
            for (int y = Mathf.FloorToInt(v0 * Size); y < Mathf.CeilToInt(v1 * Size); y++)
                for (int x = Mathf.FloorToInt(u0 * Size); x < Mathf.CeilToInt(u1 * Size); x++)
                    Mix(px, x, y, c, alpha);
        }

        static void Disc(Color[] px, float u, float v, float ru, float rv, Color c, float alpha = 1f)
        {
            for (int y = Mathf.FloorToInt((v - rv) * Size); y <= Mathf.CeilToInt((v + rv) * Size); y++)
                for (int x = Mathf.FloorToInt((u - ru) * Size); x <= Mathf.CeilToInt((u + ru) * Size); x++)
                {
                    float du = (x / (float)Size - u) / ru, dv = (y / (float)Size - v) / rv;
                    if (du * du + dv * dv <= 1f)
                        Mix(px, x, y, c, alpha);
                }
        }

        static void Paint(Color[] px)
        {
            var dark = new Color(0.04f, 0.028f, 0.02f);
            // Tường nền: vàng ấm sáng ở giữa, tối dần xuống chân và hai bên; sọc giấy dán tường mảnh.
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    float glow = Mathf.Clamp01(1f - Mathf.Abs(u - 0.5f) * 1.3f) * Mathf.Clamp01((v - 0.1f) * 1.4f);
                    var wall = Color.Lerp(new Color(0.16f, 0.1f, 0.06f), new Color(0.62f, 0.38f, 0.18f), glow * 0.85f);
                    wall *= 0.95f + 0.06f * Mathf.Sin(u * 90f);
                    var floor = Color.Lerp(new Color(0.05f, 0.03f, 0.02f), new Color(0.2f, 0.12f, 0.06f), Mathf.Clamp01(v * 3f) * glow);
                    px[y * Size + x] = v < 0.27f ? floor : wall;
                    px[y * Size + x].a = 1f;
                }
            // Kệ chai sau quầy: hai tầng, chai cao thấp đủ màu hổ phách và xanh sẫm.
            for (int tier = 0; tier < 2; tier++)
            {
                float vs = 0.6f + tier * 0.17f;
                Rect(px, 0.1f, vs - 0.012f, 0.9f, vs, new Color(0.1f, 0.06f, 0.03f));
                for (int i = 0; i < 22; i++)
                {
                    float u = 0.12f + i * 0.0355f;
                    float h = 0.07f + 0.05f * Hash(i, tier);
                    var c = Hash(i, tier + 7) < 0.5f ? new Color(0.5f, 0.28f, 0.08f) : new Color(0.1f, 0.22f, 0.12f);
                    Rect(px, u, vs, u + 0.02f, vs + h, c * (0.7f + 0.5f * Hash(i, 3)));
                    Rect(px, u + 0.004f, vs + h * 0.2f, u + 0.008f, vs + h * 0.85f, new Color(1f, 0.8f, 0.5f), 0.35f);
                }
            }
            // Quầy bên phải.
            Rect(px, 0.52f, 0.27f, 1.0f, 0.5f, new Color(0.13f, 0.075f, 0.04f));
            Rect(px, 0.52f, 0.49f, 1.0f, 0.515f, new Color(0.6f, 0.4f, 0.2f), 0.8f);
            // Bàn tròn và ghế (bóng sẫm).
            foreach (var (u, scale) in new[] { (0.2f, 1f), (0.38f, 0.85f) })
            {
                Disc(px, u, 0.4f, 0.09f * scale, 0.018f * scale, new Color(0.45f, 0.36f, 0.26f));
                Rect(px, u - 0.008f, 0.27f, u + 0.008f, 0.4f, dark);
                Rect(px, u - 0.07f * scale, 0.27f, u - 0.05f * scale, 0.5f, dark);
                Rect(px, u + 0.05f * scale, 0.27f, u + 0.07f * scale, 0.5f, dark);
            }
            // Bóng người ngồi uống cà phê (đầu, thân), rìa sáng ấm.
            foreach (var u in new[] { 0.14f, 0.47f, 0.7f })
            {
                float v = u > 0.6f ? 0.53f : 0.5f;
                Disc(px, u, v + 0.17f, 0.032f, 0.045f, new Color(0.06f, 0.04f, 0.03f));
                Rect(px, u - 0.05f, v - 0.12f, u + 0.05f, v + 0.12f, new Color(0.05f, 0.035f, 0.03f));
                Rect(px, u - 0.05f, v + 0.1f, u - 0.044f, v + 0.12f, new Color(1f, 0.7f, 0.35f), 0.4f);
            }
            // Ba đèn treo: dây, chao và quầng sáng ấm.
            foreach (var u in new[] { 0.22f, 0.5f, 0.78f })
            {
                Rect(px, u - 0.002f, 0.87f, u + 0.002f, 1f, dark);
                Rect(px, u - 0.035f, 0.83f, u + 0.035f, 0.87f, new Color(0.35f, 0.25f, 0.1f));
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        float du = x / (float)Size - u, dv = (y / (float)Size - 0.82f) * 1.2f;
                        float d = Mathf.Sqrt(du * du + dv * dv);
                        Add(px, x, y, new Color(1f, 0.74f, 0.38f), Mathf.Pow(Mathf.Clamp01(1f - d / 0.3f), 2.2f) * 0.55f);
                    }
                Disc(px, u, 0.81f, 0.02f, 0.014f, new Color(1f, 0.92f, 0.65f));
            }
            // Vệt phản chiếu đèn trên sàn bóng.
            foreach (var u in new[] { 0.22f, 0.5f, 0.78f })
                for (int y = 0; y < Mathf.RoundToInt(0.26f * Size); y++)
                    for (int x = 0; x < Size; x++)
                    {
                        float du = x / (float)Size - u;
                        float fall = 1f - y / (0.26f * Size);
                        Add(px, x, y, new Color(0.8f, 0.5f, 0.2f), Mathf.Exp(-du * du * 900f) * fall * 0.35f);
                    }
            // Tối dần ở rìa (mờ khung).
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size - 0.5f, v = y / (float)Size - 0.5f;
                    float vig = 1f - Mathf.Clamp01((u * u + v * v) * 1.5f);
                    ref var p = ref px[y * Size + x];
                    p = new Color(p.r * (0.55f + 0.45f * vig), p.g * (0.55f + 0.45f * vig), p.b * (0.55f + 0.45f * vig), 1f);
                }
        }

        static void Blur(Color[] px, int radius)
        {
            var tmp = new Color[px.Length];
            for (int pass = 0; pass < 2; pass++)
            {
                var src = pass == 0 ? px : tmp;
                var dst = pass == 0 ? tmp : px;
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        Color sum = Color.clear; int n = 0;
                        for (int k = -radius; k <= radius; k++)
                        {
                            int xx = pass == 0 ? Mathf.Clamp(x + k, 0, Size - 1) : x;
                            int yy = pass == 0 ? y : Mathf.Clamp(y + k, 0, Size - 1);
                            sum += src[yy * Size + xx]; n++;
                        }
                        dst[y * Size + x] = sum / n;
                    }
            }
        }
    }
}
