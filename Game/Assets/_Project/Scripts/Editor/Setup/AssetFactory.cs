using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HaiBanTay.Audio;
using HaiBanTay.Core;
using HaiBanTay.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using FontAsset = UnityEngine.TextCore.Text.FontAsset;
using UnityEngine.UIElements;

namespace HaiBanTay.Editor.Setup
{
    /// <summary>Sinh asset dùng chung bằng code: GameConfig, vật liệu khối xám, volume hậu kỳ, profile ánh sáng,
    /// AudioLibrary, credits. Asset nhóm có thể chỉnh (profile, HandsShotPose) chỉ tạo khi chưa có.</summary>
    public static partial class AssetFactory
    {
        public const string Root = ProjectSetup.Root;
        public const string ConfigFolder = Root + "/Config";
        public const string GameConfigPath = Root + "/Resources/GameConfig.asset";
        public const string InputPath = Root + "/Input/HaiBanTay.inputactions";
        public const string HandsShotPath = ConfigFolder + "/HandsShotPose.asset";
        public const string AudioLibraryPath = ConfigFolder + "/AudioLibrary.asset";
        public const string CreditsPath = ConfigFolder + "/Credits.generated.txt";
        public const string MaterialsFolder = Root + "/Art/Materials";
        public const string TexturesFolder = Root + "/Art/Textures";
        public const string SurfacesFolder = Root + "/Art/Surfaces";
        public const string VolumesFolder = Root + "/Rendering/Volumes";
        public const string AudioFolder = Root + "/Audio";

        public static void RunAll()
        {
            EnsureFolder(ConfigFolder);
            EnsureFolder(Root + "/Resources");
            EnsureVolumes();
            EnsureLightingProfiles();
            SceneBuilders.WorldBuilder.EnsureMaterials();
            var shot = ConfigureHandsShot();
            var audio = RebuildAudioLibrary();
            var credits = RebuildCredits();

            var config = LoadOrCreate<GameConfig>(GameConfigPath);
            config.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(ProjectSetup.PanelSettingsPath);
            config.input = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath);
            config.serifFont = AssetDatabase.LoadAssetAtPath<FontAsset>(ProjectSetup.FontsFolder + "/NotoSerif-Regular.asset");
            config.audio = audio;
            config.handsShot = shot;
            config.credits = credits;
            if (config.input == null)
                Debug.LogError("AssetFactory: không nạp được " + InputPath);
            EditorUtility.SetDirty(config);
        }

        // ---------- Tiện ích ----------

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        public static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        /// <summary>Khung chung B1.2/P3/E2; cấu hình luôn sinh từ code.</summary>
        public static HandsShotPose ConfigureHandsShot()
        {
            var shot = LoadOrCreate<HandsShotPose>(HandsShotPath);
            shot.pitch = 8f;
            // Hạ tay 1,2 cm (y −0,08/−0,06 → −0,092/−0,072): cánh tay dài hơn 9% (PlayerBody.ArmGain) đưa đỉnh bàn tay lên 0,522–0,527 khung
            // hình, quá mép trên 0,52 của HandsShotPlayTests.AssertPalms (tay phải ở nửa dưới khung, trên phụ đề).
            shot.leftPos = new Vector3(-0.15f, -0.092f, 0.58f);
            shot.rightPos = new Vector3(0.15f, -0.072f, 0.58f);
            shot.leftEuler = new Vector3(-35f, 8f, 180f);
            shot.rightEuler = new Vector3(-35f, -8f, 180f);
            shot.prologuePitch = shot.pitch;
            shot.prologueHandOffset = Vector3.zero;
            EditorUtility.SetDirty(shot);
            return shot;
        }

        // ---------- Vật liệu ----------

        static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>();

        public static Material Lit(string name, Color color, float smoothness = 0.2f, float metallic = 0f)
        {
            return GetMaterial(name, "Universal Render Pipeline/Lit", m =>
            {
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Smoothness", smoothness);
                m.SetFloat("_Metallic", metallic);
                ApplySurface(m, name == "PropWood" ? "Wood" : name, metallic);
            });
        }

        /// <summary>
        /// Vật liệu cho model có UV sẵn (không qua SurfaceMesh): cùng bề mặt PBR với khối xám, tỉ lệ texture đổi theo số mét
        /// mỗi đơn vị UV của model (tàu: 1 UV = 2 m).
        /// </summary>
        public static Material LitModel(string name, string surface, Color color, float metresPerUv, float smoothness = 0.2f,
            float metallic = 0f)
        {
            return GetMaterial(name, "Universal Render Pipeline/Lit", m =>
            {
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Smoothness", smoothness);
                m.SetFloat("_Metallic", metallic);
                ApplySurface(m, surface, metallic);
                float s = metresPerUv / (SurfaceTiles.TryGetValue(surface, out var t) ? t : 2f);
                m.SetTextureScale("_BaseMap", new Vector2(s, s));
            });
        }

        // ---------- Bề mặt PBR (Art/Surfaces/{tên vật liệu}/) ----------

        /// <summary>Cỡ một ô texture (mét) theo vật liệu; mặc định 2 m.</summary>
        static readonly Dictionary<string, float> SurfaceTiles = new Dictionary<string, float>
        {
            { "DeckPlanks", 2f }, { "GalleyFloor", 1.5f }, { "Floor", 1.5f }, { "GalleyWall", 2.5f }, { "GalleyCeiling", 2.5f },
            { "ColdWall", 2.5f }, { "Hull", 4f }, { "ShipWhite", 3f }, { "Rail", 1f }, { "Funnel", 3f }, { "Iron", 1f },
            { "Copper", 0.6f }, { "Brass", 0.6f }, { "Wood", 1f }, { "PropWood", 0.25f }, { "Door", 1f }, { "Burlap", 0.8f }, { "Coal", 1f },
            { "Cobble", 2f }, { "QuayStone", 3f }, { "Wall", 4f }, { "NhaRongWall", 4f }, { "NhaRongRoof", 2f },
            { "Shore", 6f }, { "Hill", 12f }, { "Marble", 1.5f }, { "Basilica", 4f }, { "Awning", 1.5f }, { "Shutter", 1f },
            { "Paper", 0.3f }, { "Porcelain", 0.3f },
        };

        /// <summary>Vật liệu có texture bề mặt: khối dùng nó cần mesh UV theo mét (SurfaceMesh).</summary>
        public static bool IsSurface(Material m) => m != null && m.name.StartsWith("Gen_") && m.shader != null
                                                    && m.shader.name == "Universal Render Pipeline/Lit"
                                                    && m.GetTexture("_BaseMap") != null
                                                    // Than hồng (Embers): texture phủ trọn mặt khối, không lát theo mét (cục than 0,1 m chỉ thấy 5% texture).
                                                    && m.GetTexture("_EmissionMap") == null
                                                    // Vệt bẩn sàn: ảnh phủ trọn quad (UV 0..1, Clamp); UV theo mét chỉ lấy viền trong suốt nên vệt không hiện.
                                                    && m.name != "Gen_FloorGrime" && m.name != "Gen_RangeGrate";

        public static float SurfaceTile(Material m)
        {
            string key = m.name.StartsWith("Gen_") ? m.name.Substring(4) : m.name;
            return SurfaceTiles.TryGetValue(key, out var t) ? t : 2f;
        }

        static readonly string[] ColorKeys = { "_color", "_diff", "_albedo", "_basecolor", "_base_color", "_col" };
        static readonly string[] NormalKeys = { "_normalgl", "_nor_gl", "_normal", "_nor" };
        static readonly string[] RoughKeys = { "_roughness", "_rough" };

        static void ApplySurface(Material m, string name, float metallic)
        {
            string folder = $"{SurfacesFolder}/{name}";
            if (!AssetDatabase.IsValidFolder(folder))
                return;
            var textures = AssetDatabase.FindAssets("t:Texture2D", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => !Path.GetFileName(p).StartsWith("Gen_")).ToArray();
            string Pick(string[] keys) => keys.Select(k => textures.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).ToLowerInvariant().Contains(k)))
                .FirstOrDefault(p => p != null);

            string color = Pick(ColorKeys);
            if (color == null)
                return;
            // Màu khối xám cũ thành sắc thái nhẹ để các vật cùng loại vẫn phân biệt được; texture mang màu chính.
            var tint = Color.Lerp(Color.white, m.GetColor("_BaseColor"), 0.15f);
            tint.a = 1f;
            m.SetColor("_BaseColor", tint);
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(color));

            string normal = Pick(NormalKeys);
            if (normal != null)
            {
                m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
                m.SetFloat("_BumpScale", 1f);
                m.EnableKeyword("_NORMALMAP");
            }

            string rough = Pick(RoughKeys);
            if (rough != null)
            {
                var gloss = EnsureGlossMap(folder, rough, metallic);
                if (gloss != null)
                {
                    m.SetTexture("_MetallicGlossMap", gloss);
                    m.SetFloat("_Smoothness", 1f);
                    m.SetFloat("_SmoothnessTextureChannel", 0f);
                    m.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
            }
        }

        /// <summary>URP Lit đọc độ bóng ở kênh alpha của metallic map: sinh Gen_Gloss.png (R = metallic, A = 1 − roughness).</summary>
        static Texture2D EnsureGlossMap(string folder, string roughPath, float metallic)
        {
            string path = $"{folder}/Gen_Gloss.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;
            byte[] bytes = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), roughPath));
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!src.LoadImage(bytes))
            {
                Object.DestroyImmediate(src);
                return null;
            }
            var px = src.GetPixels32();
            byte metal = (byte)Mathf.RoundToInt(Mathf.Clamp01(metallic) * 255f);
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32(metal, metal, metal, (byte)(255 - px[i].r));
            var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, true);
            dst.SetPixels32(px);
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), path), dst.EncodeToPNG());
            Object.DestroyImmediate(src);
            Object.DestroyImmediate(dst);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>bakeGlow = false: chỉ sáng khi render (bảng phấn), không hắt sáng vào lightmap.</summary>
        public static Material Emissive(string name, Color baseColor, Color emission, bool bakeGlow = true, float smoothness = 0.5f)
        {
            return GetMaterial(name, "Universal Render Pipeline/Lit", m =>
            {
                m.SetColor("_BaseColor", baseColor);
                m.SetFloat("_Smoothness", smoothness);
                m.SetColor("_EmissionColor", emission);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = bakeGlow ? MaterialGlobalIlluminationFlags.BakedEmissive
                    : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            });
        }

        /// <summary>
        /// Kính đèn: URP Lit Transparent kiểu Premultiply (emission và phản chiếu giữ nguyên độ sáng, nền xuyên qua theo 1 - alpha),
        /// bóng loáng, không ghi sâu, không đổ bóng. tint nên rất tối: Light đặt ngay trong kính (cách vài cm) sẽ làm kính cháy
        /// trắng nếu albedo sáng; màu hổ phách đến từ emission. emission đen thì tắt hẳn phát sáng (kính đèn tắt: thấy xuyên vào trong).
        /// </summary>
        public static Material Glass(string name, Color tint, Color emission, float smoothness = 0.92f)
        {
            return GetMaterial(name, "Universal Render Pipeline/Lit", m =>
            {
                m.SetColor("_BaseColor", tint);
                m.SetFloat("_Smoothness", smoothness);
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 1f);
                m.SetFloat("_AlphaClip", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                m.DisableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetShaderPassEnabled("ShadowCaster", false);
                bool glows = Mathf.Max(emission.r, Mathf.Max(emission.g, emission.b)) > 0.001f;
                m.SetColor("_EmissionColor", glows ? emission : Color.black);
                if (glows)
                    m.EnableKeyword("_EMISSION");
                else
                    m.DisableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            });
        }

        /// <summary>
        /// Than hồng (cửa lò, hố tro): URP Lit, texture nhiễu sinh bằng code làm cả màu nền (tối, vỏ than) lẫn _EmissionMap
        /// (đốm cam đỏ, lõi vàng cam hiếm), nên không còn mặt phẳng phát sáng đều. emission là hệ số nhân màu emission
        /// (CoalStation đổi _EmissionColor lúc chạy qua MaterialPropertyBlock theo mức lửa).
        /// </summary>
        public static Material Embers(string name, Color emission, float albedo = 0.07f, float minHeat = 0f)
        {
            var tex = EnsureEmberTexture(minHeat);
            return GetMaterial(name, "Universal Render Pipeline/Lit", m =>
            {
                // Albedo thấp: FireLight đặt cách mặt cửa lò vài chục cm, albedo sáng sẽ cháy trắng thành mảng xám.
                m.SetColor("_BaseColor", new Color(albedo, albedo * 0.85f, albedo * 0.85f));
                m.SetTexture("_BaseMap", tex);
                // Không phản chiếu/specular: F0 0,04 nhân với irradiance của FireLight sát mặt cũng đủ cháy thành mảng xám.
                m.SetFloat("_Smoothness", 0f);
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_SpecularHighlights", 0f);
                m.SetFloat("_EnvironmentReflections", 0f);
                m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                m.SetTexture("_EmissionMap", tex);
                m.SetColor("_EmissionColor", emission);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            });
        }

        /// <summary>Khoảng cách tới đường biên Voronoi (F2 - F1) trên lưới n×n lặp được (mép texture khớp nhau).</summary>
        static void VoronoiEdge(float px, float py, int n, out float edge)
        {
            int fx = Mathf.FloorToInt(px), fy = Mathf.FloorToInt(py);
            float f1 = 9f, f2 = 9f;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ci = ((fx + dx) % n + n) % n, cj = ((fy + dy) % n + n) % n;
                    float a = Mathf.Sin(ci * 127.1f + cj * 311.7f) * 43758.5453f;
                    float b = Mathf.Sin(ci * 269.5f + cj * 183.3f) * 43758.5453f;
                    a -= Mathf.Floor(a);
                    b -= Mathf.Floor(b);
                    float ddx = fx + dx + a - px, ddy = fy + dy + b - py;
                    float d = Mathf.Sqrt(ddx * ddx + ddy * ddy);
                    if (d < f1) { f2 = f1; f1 = d; }
                    else if (d < f2) f2 = d;
                }
            edge = f2 - f1;
        }

        /// <summary>minHeat &gt; 0 nâng nền: không còn vùng vỏ than đen kịt (cục than, nền cửa lò vẫn đỏ rực giữa các vết nứt).</summary>
        static Texture2D EnsureEmberTexture(float minHeat)
        {
            EnsureFolder(TexturesFolder);
            string path = minHeat <= 0f ? TexturesFolder + "/Gen_EmberNoise.png"
                : TexturesFolder + $"/Gen_EmberCracks9_m{Mathf.RoundToInt(minHeat * 100f)}.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
                return tex;
            const int size = 128;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float n = 0.55f * Mathf.PerlinNoise(u * 6f + 2f, v * 6f + 9f) + 0.45f * Mathf.PerlinNoise(u * 15f + 5f, v * 15f + 1f);
                    // Vết nứt tối giữa các cục than: nhiễu mịn kéo nhiệt xuống.
                    float crack = Mathf.PerlinNoise(u * 28f + 13f, v * 28f + 3f);
                    float heat = Mathf.Clamp01((n - 0.38f) / 0.4f);
                    heat = heat * heat * (3f - 2f * heat) * Mathf.Lerp(0.55f, 1f, crack);
                    if (minHeat > 0f)
                    {
                        // Than nứt (Voronoi): vỏ tối đỏ sẫm ở giữa mỗi mảng, đường nứt sáng cam, vài điểm nóng vàng; không còn mảng đỏ đều.
                        float wu = u + 0.05f * (Mathf.PerlinNoise(u * 9f + 4f, v * 9f + 8f) - 0.5f);
                        float wv = v + 0.05f * (Mathf.PerlinNoise(u * 9f + 21f, v * 9f + 2f) - 0.5f);
                        // Nền than (minHeat nhỏ): ô nhỏ hơn, nền cháy đỏ cam đều, đường nứt sáng vàng; cục than (minHeat lớn): vỏ tối, nứt rõ.
                        bool bed = minHeat < 0.3f;
                        int cells = bed ? 12 : 8;
                        VoronoiEdge(wu * cells, wv * cells, cells, out float edge);
                        float glow = 1f - Mathf.Clamp01(edge / (bed ? 0.35f : 0.3f));
                        glow = glow * glow * (3f - 2f * glow);
                        float hot = Mathf.Clamp01((Mathf.PerlinNoise(u * 4f + 30f, v * 4f + 12f) - 0.35f) / 0.4f);
                        if (bed)
                            heat = Mathf.Lerp(0.42f, 1f, glow * (0.55f + 0.45f * hot)) * (0.88f + 0.24f * n);
                        else
                        {
                            heat = Mathf.Clamp01(glow * (0.55f + 0.45f * hot) + 0.22f * n * (1f - glow));
                            heat = Mathf.Lerp(minHeat * 0.6f, 1f, heat);
                        }
                        heat = Mathf.Clamp01(heat);
                    }
                    else
                        heat = Mathf.Lerp(minHeat, 1f, heat);
                    var crust = new Color(0.06f, 0.02f, 0.015f);
                    var ember = new Color(1f, 0.32f, 0.06f);
                    var core = new Color(1f, 0.62f, 0.2f);
                    var c = Color.Lerp(crust, ember, heat);
                    c = Color.Lerp(c, core, Mathf.Clamp01((heat - 0.8f) * 4f) * 0.7f);
                    t.SetPixel(x, y, new Color(c.r, c.g, c.b, 1f));
                }
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.maxTextureSize = 128;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Unlit trong suốt (lớp sương, kính).</summary>
        public static Material Transparent(string name, Color color)
        {
            return GetMaterial(name, "Universal Render Pipeline/Unlit", m =>
            {
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetOverrideTag("RenderType", "Transparent");
            });
        }

        /// <summary>Lớp bẩn cọ nồi (D9): URP Unlit + Alpha Clipping trên texture nhiễu; tăng _Cutoff để vết bẩn mất từng mảng.</summary>
        public static Material Dirt()
        {
            var noise = EnsureNoiseTexture();
            return GetMaterial("Dirt", "Universal Render Pipeline/Unlit", m =>
            {
                m.SetColor("_BaseColor", new Color(0.12f, 0.1f, 0.08f, 1f));
                m.SetTexture("_BaseMap", noise);
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.02f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            });
        }

        /// <summary>
        /// Lớp bẩn có mặt nạ cọ (brush17): cùng màu và nhiễu với Dirt, alpha nhân với mặt nạ của BrushScrub (_MaskTex, R = còn bẩn), nên
        /// vết bẩn chỉ mất ở chỗ bàn chải đã chà. Lưới phủ dựng lúc chạy (DirtOverlayMesh), uv là uv mặt nạ.
        /// </summary>
        public static Material DirtMasked()
        {
            var noise = EnsureNoiseTexture();
            return GetMaterial("DirtMasked", "HaiBanTay/DirtOverlay", m =>
            {
                m.SetColor("_BaseColor", new Color(0.12f, 0.1f, 0.08f, 1f));
                m.SetTexture("_BaseMap", noise);
                m.SetTextureScale("_BaseMap", new Vector2(1.6f, 1.6f));
                m.SetFloat("_Cutoff", 0.08f);
                m.renderQueue = (int)RenderQueue.AlphaTest;
            });
        }

        /// <summary>
        /// Vệt bẩn sàn bếp (vụn than, bụi, nước đọng khô): mảng loang mép mờ, lốm đốm, có vài chấm văng; Lit trong suốt để nhận
        /// đèn như sàn. Alpha cắt (_Cutoff) vẫn dùng được cho lúc quét: phần nhạt biến trước, lõi đậm biến sau cùng.
        /// </summary>
        public static Material FloorGrime()
        {
            var tex = EnsureFloorGrimeTexture();
            return GetMaterial("FloorGrime", "Universal Render Pipeline/Lit", m =>
            {
                m.SetTexture("_BaseMap", tex);
                m.SetColor("_BaseColor", new Color(0.13f, 0.1f, 0.075f, 0.85f));
                m.SetFloat("_Smoothness", 0.05f);
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.02f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.EnableKeyword("_ALPHATEST_ON");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = (int)RenderQueue.Transparent - 10;
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetShaderPassEnabled("ShadowCaster", false);
            });
        }

        /// <summary>Vỏ khoai tây: nâu đất lốm đốm, chấm bì sậm và vài mắt mầm; ảnh lặp hai lần quanh củ (UV của PotatoMesh).</summary>
        public static Material PotatoSkin()
        {
            var tex = EnsurePotatoSkinTexture();
            return GetMaterial("PotatoSkin", "Universal Render Pipeline/Lit", m =>
            {
                m.SetTexture("_BaseMap", tex);
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Smoothness", 0.16f);
                m.SetFloat("_Metallic", 0f);
            });
        }

        /// <summary>Vỏ củ cải (navet): trắng ngà ở rễ, ngả tím loang không đều về phía cuống. Ảnh trải dọc củ một lần (V kẹp).</summary>
        public static Material TurnipSkin()
        {
            var tex = EnsureTurnipSkinTexture();
            return GetMaterial("TurnipSkin", "Universal Render Pipeline/Lit", m =>
            {
                m.SetTexture("_BaseMap", tex);
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Smoothness", 0.32f);
                m.SetFloat("_Metallic", 0f);
            });
        }

        static Texture2D EnsureTurnipSkinTexture()
        {
            EnsureFolder(TexturesFolder);
            string path = TexturesFolder + "/Gen_TurnipSkin.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
                return tex;
            const int w = 128, h = 256;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var white = new Color(0.9f, 0.88f, 0.8f);
            var purple = new Color(0.47f, 0.16f, 0.4f);
            var root = new Color(0.76f, 0.68f, 0.55f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // u quanh củ (lặp liền mép), v dọc củ: 0 = chóp rễ, 1 = cuống.
                    float u = x / (float)w, v = y / (float)h;
                    float cu = Mathf.Cos(u * Mathf.PI * 2f), su = Mathf.Sin(u * Mathf.PI * 2f);
                    // Ranh tím chảy thành vệt dọc xuống thân, cao thấp không đều quanh củ.
                    float streak = Mathf.PerlinNoise(4f + cu * 2.2f, 6f + su * 2.2f) * 0.7f + Mathf.PerlinNoise(9f + cu * 7f, 2f + su * 7f) * 0.3f;
                    float edge = Mathf.Lerp(0.5f, 0.74f, streak);
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((v - edge + 0.07f) / 0.16f));
                    var c = Color.Lerp(white, purple, k);
                    // Tím đậm dần lên sát cuống.
                    c = Color.Lerp(c, purple * 0.78f, Mathf.Clamp01((v - 0.86f) / 0.14f) * 0.7f);
                    // Vân ngang mảnh như vòng rễ, và loang nhẹ.
                    float ring = Mathf.PerlinNoise(13f + cu * 1.5f, v * 46f);
                    c *= Mathf.Lerp(0.93f, 1.04f, ring) * Mathf.Lerp(0.96f, 1.03f, Mathf.PerlinNoise(21f + cu * 5f, 3f + su * 5f + v * 8f));
                    // Chóp rễ hơi ngả nâu đất.
                    c = Color.Lerp(c, root, Mathf.Clamp01((0.12f - v) / 0.12f) * 0.6f);
                    c.a = 1f;
                    t.SetPixel(x, y, c);
                }
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.maxTextureSize = 256;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Lit trong suốt (mặt nước trong nồi, chậu): nhận đèn như vật xung quanh, thấy đồ chìm bên dưới.</summary>
        public static Material LitTransparent(string name, Color color, float smoothness)
        {
            return GetMaterial(name, "Universal Render Pipeline/Lit", m =>
            {
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Smoothness", smoothness);
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetShaderPassEnabled("ShadowCaster", false);
            });
        }

        static Texture2D EnsurePotatoSkinTexture()
        {
            EnsureFolder(TexturesFolder);
            string path = TexturesFolder + "/Gen_PotatoSkin2.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
                return tex;
            const int size = 256;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var rng = new System.Random(1911);
            // Chấm bì (lenticel) nhỏ sậm và mắt mầm: lõm tối, viền hơi sáng.
            var dots = new Vector3[260];
            for (int i = 0; i < dots.Length; i++)
                dots[i] = new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), 0.004f + 0.006f * (float)rng.NextDouble());
            var eyes = new Vector3[9];
            for (int i = 0; i < eyes.Length; i++)
                eyes[i] = new Vector3((float)rng.NextDouble(), 0.12f + 0.76f * (float)rng.NextDouble(), 0.016f + 0.012f * (float)rng.NextDouble());
            var baseColor = new Color(0.46f, 0.32f, 0.19f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    // Nhiễu lặp liền mép (ảnh lặp quanh củ): lấy mẫu trên vòng tròn theo u.
                    float cu = Mathf.Cos(u * Mathf.PI * 2f), su = Mathf.Sin(u * Mathf.PI * 2f);
                    float mottle = 0.55f * Mathf.PerlinNoise(3f + cu * 1.6f, 7f + su * 1.6f + v * 3f)
                                   + 0.3f * Mathf.PerlinNoise(11f + cu * 5f, 2f + su * 5f + v * 9f)
                                   + 0.15f * Mathf.PerlinNoise(5f + cu * 14f, 9f + su * 14f + v * 25f);
                    float tone = Mathf.Lerp(0.78f, 1.14f, mottle);
                    var c = baseColor * tone;
                    // Mảng đất bám nhạt.
                    float soil = Mathf.Clamp01((Mathf.PerlinNoise(21f + cu * 2.4f, 4f + su * 2.4f + v * 4f) - 0.62f) * 4f);
                    c = Color.Lerp(c, new Color(0.38f, 0.3f, 0.22f), soil * 0.6f);
                    foreach (var d in dots)
                    {
                        float du = Mathf.Abs(u - d.x); du = Mathf.Min(du, 1f - du);
                        float dist = new Vector2(du, v - d.y).magnitude;
                        if (dist < d.z) c = Color.Lerp(c, c * 0.62f, 1f - dist / d.z);
                    }
                    foreach (var e in eyes)
                    {
                        float du = Mathf.Abs(u - e.x); du = Mathf.Min(du, 1f - du);
                        float dist = new Vector2(du, v - e.y).magnitude;
                        if (dist < e.z * 0.55f) c = Color.Lerp(c, new Color(0.22f, 0.15f, 0.09f), 1f - dist / (e.z * 0.55f));
                        else if (dist < e.z) c = Color.Lerp(c, c * 1.12f, 1f - (dist - e.z * 0.55f) / (e.z * 0.45f));
                    }
                    c.a = 1f;
                    t.SetPixel(x, y, c);
                }
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = 256;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// Lưới nướng thép đan thoi trên mặt bếp lò than: dây thép và vành viền đặc, ô lưới trong suốt (alpha cắt) để thấy lửa và
        /// than hồng bên dưới. Ảnh phủ trọn mặt đĩa (UV 0..1), nên không dùng UV theo mét.
        /// </summary>
        public static Material RangeGrate()
        {
            var tex = EnsureRangeGrateTexture();
            return GetMaterial("RangeGrate", "Universal Render Pipeline/Lit", m =>
            {
                m.SetTexture("_BaseMap", tex);
                m.SetColor("_BaseColor", new Color(0.66f, 0.66f, 0.64f, 1f));
                m.SetFloat("_Smoothness", 0.55f);
                m.SetFloat("_Metallic", 0.85f);
                m.SetFloat("_Surface", 0f);
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.5f);
                m.SetFloat("_Cull", 0f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            });
        }

        static Texture2D EnsureRangeGrateTexture()
        {
            EnsureFolder(TexturesFolder);
            string path = TexturesFolder + "/Gen_RangeGrate.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
                return tex;
            const int size = 512;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            const float cells = 13f, wire = 0.085f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float dx = u - 0.5f, dy = v - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = 0f;
                    if (r <= 0.5f)
                    {
                        // Hai họ dây chéo cắt nhau tạo ô thoi; dây gợn nhẹ như lưới đan.
                        float wave = 0.012f * Mathf.Sin((u - v) * 40f);
                        float d1 = Mathf.Abs(Mathf.Repeat((u + v) * cells * 0.5f + wave * cells, 1f) - 0.5f);
                        float d2 = Mathf.Abs(Mathf.Repeat((u - v) * cells * 0.5f - wave * cells, 1f) - 0.5f);
                        bool mesh = d1 > 0.5f - wire * cells * 0.5f || d2 > 0.5f - wire * cells * 0.5f;
                        bool rim = r > 0.455f;
                        a = mesh || rim ? 1f : 0f;
                    }
                    float tone = 0.8f + 0.2f * Mathf.PerlinNoise(u * 22f, v * 22f);
                    t.SetPixel(x, y, new Color(tone, tone, tone, a));
                }
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 512;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Texture2D EnsureFloorGrimeTexture()
        {
            EnsureFolder(TexturesFolder);
            string path = TexturesFolder + "/Gen_FloorGrime.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
                return tex;
            const int size = 256;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var rng = new System.Random(1911);
            // Chấm văng quanh mảng chính.
            var specks = new Vector3[38];
            for (int i = 0; i < specks.Length; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = 0.25f + 0.22f * (float)rng.NextDouble();
                specks[i] = new Vector3(0.5f + Mathf.Cos(a) * r, 0.5f + Mathf.Sin(a) * r, 0.006f + 0.016f * (float)rng.NextDouble());
            }
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    // Mép loang: bán kính dao động theo góc và nhiễu, không thành hình elip trơn.
                    float dx = u - 0.5f, dy = v - 0.5f;
                    float ang = Mathf.Atan2(dy, dx);
                    float edge = 0.3f + 0.06f * Mathf.Sin(ang * 3f + 0.7f) + 0.04f * Mathf.Sin(ang * 7f + 2.1f)
                                 + 0.08f * (Mathf.PerlinNoise(u * 5f + 13f, v * 5f + 2f) - 0.5f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float body = 1f - Mathf.SmoothStep(edge * 0.45f, edge, dist);
                    float grain = 0.45f * Mathf.PerlinNoise(u * 14f, v * 14f) + 0.35f * Mathf.PerlinNoise(u * 37f + 5f, v * 37f + 9f)
                                  + 0.2f * Mathf.PerlinNoise(u * 90f + 3f, v * 90f + 1f);
                    float a = body * Mathf.Clamp01(0.25f + grain * 0.95f);
                    foreach (var sp in specks)
                    {
                        float d = Vector2.Distance(new Vector2(u, v), new Vector2(sp.x, sp.y));
                        if (d < sp.z) a = Mathf.Max(a, 0.75f * (1f - d / sp.z));
                    }
                    // Lõi đậm hơi lệch tâm: quét thì phần nhạt bên ngoài mất trước.
                    float tone = Mathf.Lerp(0.75f, 1.15f, grain);
                    t.SetPixel(x, y, new Color(tone, tone * 0.95f, tone * 0.9f, Mathf.Clamp01(a)));
                }
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 256;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Khung cảnh biển sau ô cửa sổ (Unlit, không nhận đèn): ban ngày trời chiều mây xám, ban đêm tối xanh.</summary>
        public static Material PortholeView(bool night)
        {
            var tex = EnsurePortholeTexture();
            return GetMaterial(night ? "PortholeNight" : "PortholeDay", "Universal Render Pipeline/Unlit", m =>
            {
                m.SetTexture("_BaseMap", tex);
                m.SetColor("_BaseColor", night ? new Color(0.16f, 0.19f, 0.27f) : new Color(1.15f, 1.1f, 1.05f));
            });
        }

        static Texture2D EnsurePortholeTexture()
        {
            EnsureFolder(TexturesFolder);
            string path = TexturesFolder + "/Gen_PortholeView.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
                return tex;
            const int size = 512;
            const float horizon = 0.5f;
            var t = new Texture2D(size, size, TextureFormat.RGB24, false);
            var zenith = new Color(0.42f, 0.5f, 0.6f);
            var skyLow = new Color(0.78f, 0.78f, 0.76f);
            var seaFar = new Color(0.33f, 0.4f, 0.45f);
            var seaNear = new Color(0.07f, 0.14f, 0.19f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    Color c;
                    if (v >= horizon)
                    {
                        float k = (v - horizon) / (1f - horizon);
                        c = Color.Lerp(skyLow, zenith, Mathf.Pow(k, 0.7f));
                        float cloud = Mathf.PerlinNoise(u * 3f + 5f, v * 9f) * 0.6f + Mathf.PerlinNoise(u * 9f, v * 22f) * 0.4f;
                        c = Color.Lerp(c, new Color(0.62f, 0.62f, 0.64f), Mathf.SmoothStep(0.45f, 0.75f, cloud) * 0.7f);
                    }
                    else
                    {
                        float k = (horizon - v) / horizon;
                        c = Color.Lerp(seaFar, seaNear, Mathf.Pow(k, 0.6f));
                        // Vệt sóng: nhiễu kéo dài theo phương ngang, dày dần về gần.
                        float freq = Mathf.Lerp(90f, 18f, k);
                        float wave = Mathf.PerlinNoise(u * 5f + 13f, v * freq);
                        float crest = Mathf.SmoothStep(0.68f, 0.82f, wave) * Mathf.Lerp(0.25f, 0.6f, k);
                        c = Color.Lerp(c, new Color(0.7f, 0.75f, 0.76f), crest);
                        c *= 0.85f + 0.3f * Mathf.PerlinNoise(u * 14f, v * freq * 0.5f + 3f);
                    }
                    // Vệt sáng mờ ở đường chân trời.
                    c = Color.Lerp(c, new Color(0.88f, 0.86f, 0.8f), Mathf.Exp(-Mathf.Abs(v - horizon) * 90f) * 0.6f);
                    t.SetPixel(x, y, c);
                }
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Shader URP Lit vẽ hai mặt, mặt sau lật pháp tuyến (Rendering/LitTwoSided.shader).</summary>
        public const string TwoSidedShader = "HaiBanTay/LitTwoSided";

        /// <summary>
        /// Vật liệu của vật mỏng hay hở mà người chơi nhìn được cả mặt trong (bộ dò SeeThroughSweep, 15/10): vỏ, boong, lan can, ống khói
        /// tàu (đứng trên boong nhìn mặt trong mạn tàu thấy xuyên ra biển), vành bếp, miệng thông gió, bóng phim, áo quần và ống tay người
        /// chơi (cúi nhìn lòng áo, ống tay), da tay góc nhìn thứ nhất (cổ tay hở), bao tải.
        /// </summary>
        public static readonly HashSet<string> TwoSidedNames = new HashSet<string>
        {
            "ShipModel_Hull", "ShipModel_White", "ShipModel_Deck", "ShipModel_Rail", "ShipModel_Iron", "ShipModel_Funnel",
            "RangeWarmLid", "ShipVentInside", "StoryFilmShadow", "BaShirt", "BaThuongShirt", "BaTrousers", "FP_Skin", "Burlap",
        };

        /// <summary>Đổi sang shader hai mặt, giữ thuộc tính (cùng bộ thuộc tính URP Lit), tắt cull.</summary>
        public static void MakeTwoSided(Material m)
        {
            var shader = Shader.Find(TwoSidedShader);
            if (shader != null && m.shader != shader)
                m.shader = shader;
            if (m.HasProperty("_Cull"))
                m.SetFloat("_Cull", 0f);
            m.doubleSidedGI = true;
        }

        /// <summary>Quầng sáng cộng (additive) quanh đèn: màu HDR, power càng cao quầng càng hẹp (shader HaiBanTay/LampHalo).</summary>
        public static Material LampHalo(string name, Color color, float power)
        {
            return GetMaterial(name, "HaiBanTay/LampHalo", m =>
            {
                m.SetColor("_Color", color);
                m.SetFloat("_Power", power);
            });
        }

        static Material GetMaterial(string name, string shaderName, System.Action<Material> setup)
        {
            if (MaterialCache.TryGetValue(name, out var cached) && cached != null)
                return cached;
            EnsureFolder(MaterialsFolder);
            string path = $"{MaterialsFolder}/Gen_{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null)
                    throw new System.InvalidOperationException("Không tìm thấy shader " + shaderName);
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            setup(m);
            if (TwoSidedNames.Contains(name) && shaderName == "Universal Render Pipeline/Lit")
                MakeTwoSided(m);
            EditorUtility.SetDirty(m);
            MaterialCache[name] = m;
            return m;
        }

        static Texture2D EnsureNoiseTexture()
        {
            EnsureFolder(TexturesFolder);
            string path = TexturesFolder + "/Gen_DirtNoise.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
                return tex;
            const int size = 256;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float n = 0.55f * Mathf.PerlinNoise(u * 6f, v * 6f) + 0.3f * Mathf.PerlinNoise(u * 17f + 3f, v * 17f + 7f)
                              + 0.15f * Mathf.PerlinNoise(u * 41f + 11f, v * 41f + 5f);
                    // Giá trị alpha thấp nhất vẫn > 0,02 để đầu tác vụ nồi bẩn kín.
                    float a = Mathf.Lerp(0.05f, 1f, Mathf.Clamp01((n - 0.2f) / 0.6f));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 256;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------- Hậu kỳ ----------

        public static VolumeProfile VolumeFor(string name) => AssetDatabase.LoadAssetAtPath<VolumeProfile>($"{VolumesFolder}/{name}.asset");

        static void EnsureVolumes()
        {
            BuildVolume("Post_Laptop", new Color(1f, 0.92f, 0.8f), -35f, bloom: 0.25f, grain: 0.2f);
            BuildVolume("Post_PC", new Color(1f, 0.92f, 0.8f), -35f, bloom: 0.4f, grain: 0.25f);
            BuildVolume("Post_Ending", new Color(1f, 0.9f, 0.76f), -22f, bloom: 0.35f, grain: 0.2f);
            // Hoàng hôn Sài Gòn (prologue): bloom ngưỡng thấp để đèn phố và kính đèn tỏa quầng ấm. Độ bão hòa giảm như các cảnh khác:
            // bão hòa dương cộng đèn vàng làm cả phố ám cam, đồ trắng (cốc giấy, áo) mất hết kênh xanh (người dùng 17/10).
            BuildVolume("Post_Dusk_Laptop", new Color(1f, 0.97f, 0.95f), -10f, bloom: 2.0f, grain: 0.1f, threshold: 0.75f, scatter: 0.85f,
                contrast: 12f, exposure: -0.1f, vignette: 0.3f, bloomTint: new Color(1f, 0.86f, 0.7f));
            BuildVolume("Post_Dusk_PC", new Color(1f, 0.97f, 0.95f), -10f, bloom: 2.6f, grain: 0.12f, threshold: 0.75f, scatter: 0.88f,
                contrast: 12f, exposure: -0.1f, vignette: 0.3f, bloomTint: new Color(1f, 0.86f, 0.7f));
        }

        // Sepia nhẹ, vignette, film grain, bloom thấp (PLAN mục 3.3). Không SSAO/motion blur/DOF trong volume.
        static void BuildVolume(string name, Color filter, float saturation, float bloom, float grain, float threshold = 1.1f,
            float scatter = 0.7f, float contrast = 8f, float exposure = 0f, float vignette = 0.32f, Color? bloomTint = null)
        {
            EnsureFolder(VolumesFolder);
            string path = $"{VolumesFolder}/{name}.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            var color = Ensure<ColorAdjustments>(profile);
            color.colorFilter.Override(filter);
            color.saturation.Override(saturation);
            color.contrast.Override(contrast);
            color.postExposure.Override(exposure);
            var vig = Ensure<Vignette>(profile);
            vig.intensity.Override(vignette);
            vig.smoothness.Override(0.45f);
            var film = Ensure<FilmGrain>(profile);
            film.type.Override(FilmGrainLookup.Thin1);
            film.intensity.Override(grain);
            var bl = Ensure<Bloom>(profile);
            bl.intensity.Override(bloom);
            bl.threshold.Override(threshold);
            bl.scatter.Override(scatter);
            bl.tint.Override(bloomTint ?? Color.white);
            var tone = Ensure<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.Neutral);
            EditorUtility.SetDirty(profile);
        }

        static T Ensure<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var c))
                return c;
            c = profile.Add<T>(true);
            c.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }

        // ---------- Ánh sáng ----------

        public static SceneLightingProfile LightingProfile(string sceneKey)
        {
            return AssetDatabase.LoadAssetAtPath<SceneLightingProfile>($"{ConfigFolder}/SceneLightingProfile_{sceneKey}.asset");
        }

        static void EnsureLightingProfiles()
        {
            Profile("NhaRong", p =>
            {
                p.ambientSky = new Color(0.62f, 0.66f, 0.7f);
                p.ambientEquator = new Color(0.5f, 0.47f, 0.42f);
                p.ambientGround = new Color(0.25f, 0.22f, 0.18f);
                p.sunColor = new Color(1f, 0.96f, 0.88f);
                p.sunIntensity = 1.4f;
                p.sunEuler = new Vector3(70f, -20f, 0f);
                p.fogColor = new Color(0.78f, 0.8f, 0.8f);
                p.fogDensity = 0.008f;
                p.background = new Color(0.68f, 0.76f, 0.84f);
            });
            Profile("Galley", p =>
            {
                p.ambientSky = new Color(0.16f, 0.14f, 0.13f);
                p.ambientEquator = new Color(0.12f, 0.1f, 0.09f);
                p.ambientGround = new Color(0.06f, 0.05f, 0.05f);
                p.sun = false;
                p.fog = false;
                p.background = new Color(0.02f, 0.03f, 0.05f);
            });
            Profile("Marseille", p =>
            {
                p.ambientSky = new Color(0.62f, 0.56f, 0.55f);
                p.ambientEquator = new Color(0.52f, 0.44f, 0.38f);
                p.ambientGround = new Color(0.22f, 0.19f, 0.17f);
                p.sunColor = new Color(1f, 0.78f, 0.55f);
                p.sunIntensity = 1.1f;
                p.sunEuler = new Vector3(12f, 110f, 0f);
                p.fogColor = new Color(0.86f, 0.78f, 0.72f);
                p.fogDensity = 0.06f;
                p.background = new Color(0.86f, 0.74f, 0.64f);
            });
            // Sài Gòn buổi tối (prologue): ánh sáng thật do WorldRoot (TimeOfDay 19:30) ghi đè lúc dựng, hồ sơ chỉ giữ màu nền.
            Profile("Prologue", p =>
            {
                p.ambientSky = new Color(0.1f, 0.13f, 0.22f);
                p.ambientEquator = new Color(0.09f, 0.09f, 0.12f);
                p.ambientGround = new Color(0.05f, 0.04f, 0.05f);
                p.sun = false;
                p.fogColor = new Color(0.1f, 0.12f, 0.2f);
                p.fogDensity = 0.004f;
                p.background = new Color(0.03f, 0.05f, 0.1f);
            });
            Profile("Sandbox", p => { });
        }

        static void Profile(string key, System.Action<SceneLightingProfile> defaults)
        {
            string path = $"{ConfigFolder}/SceneLightingProfile_{key}.asset";
            if (AssetDatabase.LoadAssetAtPath<SceneLightingProfile>(path) != null)
                return;
            var p = ScriptableObject.CreateInstance<SceneLightingProfile>();
            defaults(p);
            AssetDatabase.CreateAsset(p, path);
        }

        // ---------- Âm thanh ----------

        static AudioLibrary RebuildAudioLibrary()
        {
            var lib = LoadOrCreate<AudioLibrary>(AudioLibraryPath);
            lib.entries.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var rel = path.Substring(AudioFolder.Length + 1);
                var key = Path.ChangeExtension(rel, null).Replace('\\', '/');
                lib.entries.Add(new AudioLibrary.Entry { key = key, clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path) });
            }
            lib.entries.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
            EditorUtility.SetDirty(lib);
            return lib;
        }

        // ---------- Credits ----------

        /// <summary>Đọc bảng trong Docs/CREDITS.md → mỗi dòng "Asset — Tác giả — Giấy phép" (CC BY kèm nguồn).</summary>
        public static TextAsset RebuildCredits()
        {
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string md = Path.Combine(repo, "Docs", "CREDITS.md");
            var lines = new List<string>();
            if (File.Exists(md))
            {
                foreach (var raw in File.ReadAllLines(md, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (!line.StartsWith("|") || line.StartsWith("|---") || line.StartsWith("| Asset"))
                        continue;
                    var cells = line.Trim('|').Split('|').Select(c => c.Trim().Replace("`", "")).ToArray();
                    if (cells.Length < 4 || string.IsNullOrEmpty(cells[0]))
                        continue;
                    // CC BY: ghi công phải kèm nguồn.
                    bool by = cells[3].Contains("BY");
                    lines.Add(by ? $"{cells[0]} — {cells[1]} — {cells[2]} — {cells[3]}" : $"{cells[0]} — {cells[1]} — {cells[3]}");
                }
            }
            EnsureFolder(ConfigFolder);
            var full = Path.Combine(Path.GetDirectoryName(Application.dataPath), CreditsPath);
            var text = string.Join("\n", lines) + "\n";
            if (!File.Exists(full) || File.ReadAllText(full, Encoding.UTF8) != text)
            {
                File.WriteAllText(full, text, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(CreditsPath);
            }
            return AssetDatabase.LoadAssetAtPath<TextAsset>(CreditsPath);
        }
    }
}
