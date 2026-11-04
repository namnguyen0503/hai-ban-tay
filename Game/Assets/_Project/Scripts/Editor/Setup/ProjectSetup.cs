using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace HaiBanTay.Editor.Setup
{
    /// <summary>Cấu hình project theo PLAN mục 3 và 9.6. Chạy lại bao nhiêu lần cũng được.</summary>
    public static class ProjectSetup
    {
        public const string Root = "Assets/_Project";
        public const string RenderingFolder = Root + "/Rendering";
        public const string LaptopAssetPath = RenderingFolder + "/URP_Laptop.asset";
        public const string PcAssetPath = RenderingFolder + "/URP_PC.asset";
        public const string FontsFolder = Root + "/Fonts";
        public const string PanelSettingsPath = Root + "/UI/PanelSettings.asset";
        public const string PanelTextSettingsPath = Root + "/UI/PanelTextSettings.asset";
        public const string ThemePath = Root + "/UI/USS/theme.tss";

        public static readonly string[] CustomTags = { "Interactable", "Holdable", "NPC" };
        public static readonly string[] CustomLayers = { "Interactable", "Holdable", "NPC" };

        [MenuItem("HaiBanTay/Setup/Run All")]
        public static void RunAll()
        {
            try
            {
                ApplyProjectSettings();
                ApplyPlayerSettings();
                ApplyTagsAndLayers();
                var laptop = EnsureUrpAsset(LaptopAssetPath, "URP_Laptop_Renderer", laptop: true);
                var pc = EnsureUrpAsset(PcAssetPath, "URP_PC_Renderer", laptop: false);
                ApplyQualityLevels(laptop, pc);
                EnsureFontAndPanelSettings();
                AssetFactory.RunAll();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("ProjectSetup: xong.");
            }
            catch (Exception e)
            {
                Debug.LogError("ProjectSetup thất bại: " + e);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
                throw;
            }
        }

        // ---- Project / Player ----

        public static void ApplyProjectSettings()
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            UnityEditor.VersionControlSettings.mode = "Visible Meta Files";
            PlayerSettings.colorSpace = ColorSpace.Linear;
        }

        /// <summary>Player Settings Windows theo PLAN mục 9.6 — gọi cả từ BuildScript.</summary>
        public static void ApplyPlayerSettings()
        {
            PlayerSettings.productName = "Hai Bàn Tay";
            PlayerSettings.companyName = "HaiBanTay";   // CSV F8: %USERPROFILE%\AppData\LocalLow\HaiBanTay\Hai Bàn Tay
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.runInBackground = true;
            ApplyAndroidSettings();
        }

        /// <summary>Bản Android (chơi thử trên điện thoại): ARM64 IL2CPP, Vulkan, ngang màn hình, độ phân giải gốc,
        /// vẽ cả vùng tai thỏ. Chỉ ảnh hưởng target Android.</summary>
        static void ApplyAndroidSettings()
        {
            var android = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, "com.haibantay.haibantay");
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Low);
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.Android.renderOutsideSafeArea = true;
            PlayerSettings.Android.optimizedFramePacing = true;
            PlayerSettings.Android.startInFullscreen = true;
            PlayerSettings.Android.bundleVersionCode = Math.Max(1, PlayerSettings.Android.bundleVersionCode);
        }

        // ---- Tag / Layer ----

        static void ApplyTagsAndLayers()
        {
            foreach (var tag in CustomTags)
                UnityEditorInternal.InternalEditorUtility.AddTag(tag);

            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            foreach (var layer in CustomLayers)
            {
                bool exists = false;
                for (int i = 0; i < layers.arraySize; i++)
                    if (layers.GetArrayElementAtIndex(i).stringValue == layer) { exists = true; break; }
                if (exists)
                    continue;
                for (int i = 8; i < layers.arraySize; i++) // 0–7 là layer built-in
                {
                    var slot = layers.GetArrayElementAtIndex(i);
                    if (string.IsNullOrEmpty(slot.stringValue))
                    {
                        slot.stringValue = layer;
                        break;
                    }
                }
            }
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---- URP ----

        static UniversalRenderPipelineAsset EnsureUrpAsset(string path, string rendererName, bool laptop)
        {
            Directory.CreateDirectory(RenderingFolder);
            var existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (existing == null)
            {
                var template = FindTemplateAsset();
                if (template == null)
                    throw new InvalidOperationException("Không tìm thấy URP asset của template trong Assets/Settings.");
                if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(template), path))
                    throw new InvalidOperationException("Không nhân bản được URP asset tới " + path);
                existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                CloneRenderer(existing, RenderingFolder + "/" + rendererName + ".asset");
            }
            ConfigureUrp(existing, laptop);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static UniversalRenderPipelineAsset FindTemplateAsset()
        {
            return AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets/Settings" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p.Contains("PC") ? 0 : 1)
                .Select(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>)
                .FirstOrDefault();
        }

        static void CloneRenderer(UniversalRenderPipelineAsset asset, string rendererPath)
        {
            var so = new SerializedObject(asset);
            var list = so.FindProperty("m_RendererDataList");
            var original = list.GetArrayElementAtIndex(0).objectReferenceValue;
            if (original == null || !AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original), rendererPath))
                throw new InvalidOperationException("Không nhân bản được Renderer Data tới " + rendererPath);
            list.GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<ScriptableObject>(rendererPath);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Dùng SerializedObject để không phụ thuộc setter công khai thay đổi giữa các bản URP.
        static void ConfigureUrp(UniversalRenderPipelineAsset asset, bool laptop)
        {
            // Giữ layer Default (bit 0) để nội dung cũ không bị đổi mask.
            EnsureRenderingLayer(1, "Interior");
            EnsureRenderingLayer(2, "Exterior");
            var so = new SerializedObject(asset);
            SetFloat(so, "m_RenderScale", laptop ? 0.7f : 1.0f);
            SetInt(so, "m_UpscalingFilter", laptop ? 3 : 0);          // 3 = FSR 1.0, 0 = Auto
            SetFloat(so, "m_ShadowDistance", laptop ? 15f : 40f);
            SetInt(so, "m_ShadowCascadeCount", laptop ? 1 : 2);
            SetInt(so, "m_MainLightShadowmapResolution", laptop ? 1024 : 2048);
            SetBool(so, "m_SoftShadowsSupported", !laptop);
            SetInt(so, "m_MSAA", 1);                                   // khử răng cưa FXAA đặt ở camera
            SetBool(so, "m_SupportsLightLayers", true);
            so.ApplyModifiedPropertiesWithoutUndo();
            ApplyRendererFeatures(asset, laptop);
        }

        /// <summary>SSAO chỉ bật ở tầng PC (PLAN 3.3: tầng Laptop tắt SSAO).</summary>
        public static void ApplyRendererFeatures(UniversalRenderPipelineAsset asset, bool laptop)
        {
            var list = new SerializedObject(asset).FindProperty("m_RendererDataList");
            for (int i = 0; i < list.arraySize; i++)
            {
                if (!(list.GetArrayElementAtIndex(i).objectReferenceValue is ScriptableRendererData data))
                    continue;
                foreach (var feature in data.rendererFeatures)
                {
                    if (feature == null || feature.GetType().Name != "ScreenSpaceAmbientOcclusion" || feature.isActive == !laptop)
                        continue;
                    feature.SetActive(!laptop);
                    EditorUtility.SetDirty(feature);
                    EditorUtility.SetDirty(data);
                }
            }
        }

        /// <summary>Áp lại cấu hình renderer cho hai tầng (gọi trước khi build).</summary>
        public static void ApplyRenderTiers()
        {
            var laptop = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(LaptopAssetPath);
            var pc = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcAssetPath);
            if (laptop != null)
                ConfigureUrp(laptop, true);
            if (pc != null)
                ConfigureUrp(pc, false);
            AssetDatabase.SaveAssets();
        }

        static void EnsureRenderingLayer(int index, string name)
        {
            while (RenderingLayerMask.GetDefinedRenderingLayerCount() <= index)
                if (!UnityEditor.Rendering.RenderPipelineEditorUtility.TryAddRenderingLayerName("WorldLayer" + RenderingLayerMask.GetDefinedRenderingLayerCount()))
                    throw new InvalidOperationException("Không thêm được Rendering Layer " + name);
            string existing = RenderingLayerMask.RenderingLayerToName(index);
            if (existing == name)
                return;
            if (!string.IsNullOrEmpty(existing) && !existing.StartsWith("Light Layer") && !existing.StartsWith("WorldLayer"))
                throw new InvalidOperationException($"Rendering Layer {index} đã dùng cho {existing}; không ghi đè.");
            if (!UnityEditor.Rendering.RenderPipelineEditorUtility.TrySetRenderingLayerName(index, name))
                throw new InvalidOperationException("Không đặt được Rendering Layer " + name);
        }

        static void SetFloat(SerializedObject so, string name, float v) { var p = Prop(so, name); if (p != null) p.floatValue = v; }
        static void SetInt(SerializedObject so, string name, int v) { var p = Prop(so, name); if (p != null) p.intValue = v; }
        static void SetBool(SerializedObject so, string name, bool v) { var p = Prop(so, name); if (p != null) p.boolValue = v; }

        static SerializedProperty Prop(SerializedObject so, string name)
        {
            var p = so.FindProperty(name);
            if (p == null)
                Debug.LogWarning($"ProjectSetup: URP asset không có thuộc tính {name}; bỏ qua (kiểm tra tay).");
            return p;
        }

        // ---- Quality ----

        static void ApplyQualityLevels(RenderPipelineAsset laptop, RenderPipelineAsset pc)
        {
            var qs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var levels = qs.FindProperty("m_QualitySettings");
            if (levels.arraySize < 2)
                throw new InvalidOperationException("QualitySettings có ít hơn 2 level để làm mẫu.");
            levels.arraySize = 2;
            Configure(levels.GetArrayElementAtIndex(0), "Laptop", laptop, vSync: 2, mipLimit: 1);
            Configure(levels.GetArrayElementAtIndex(1), "PC", pc, vSync: 1, mipLimit: 0);
            qs.ApplyModifiedPropertiesWithoutUndo();
        }

        // mipLimit 1: tầng Laptop dùng texture một nửa độ phân giải (2K → 1K) để giữ ngân sách bộ nhớ PLAN 3.3.
        static void Configure(SerializedProperty level, string name, RenderPipelineAsset urp, int vSync, int mipLimit)
        {
            level.FindPropertyRelative("name").stringValue = name;
            level.FindPropertyRelative("customRenderPipeline").objectReferenceValue = urp;
            level.FindPropertyRelative("vSyncCount").intValue = vSync;
            level.FindPropertyRelative("antiAliasing").intValue = 0;
            var mip = level.FindPropertyRelative("globalTextureMipmapLimit") ?? level.FindPropertyRelative("textureQuality");
            if (mip != null)
                mip.intValue = mipLimit;
            else
                Debug.LogWarning("ProjectSetup: QualitySettings không có globalTextureMipmapLimit; bỏ qua.");
            var excluded = level.FindPropertyRelative("excludedTargetPlatforms");
            if (excluded != null)
                excluded.arraySize = 0;
        }

        // ---- Font + PanelSettings ----

        static void EnsureFontAndPanelSettings()
        {
            var fontAsset = EnsureFontAsset(FontsFolder + "/BeVietnamPro-Regular.ttf", FontsFolder + "/BeVietnamPro-Regular.asset");
            // Noto Serif cho sổ quan sát (M2); tạo sẵn nếu có file.
            EnsureFontAsset(FontsFolder + "/NotoSerif-Regular.ttf", FontsFolder + "/NotoSerif-Regular.asset");

            var textSettings = LoadOrCreate<PanelTextSettings>(PanelTextSettingsPath);
            if (fontAsset != null)
                textSettings.defaultFontAsset = fontAsset;
            EditorUtility.SetDirty(textSettings);

            var panel = LoadOrCreate<PanelSettings>(PanelSettingsPath);
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            panel.textSettings = textSettings;
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme != null)
                panel.themeStyleSheet = theme;
            else
                Debug.LogWarning("ProjectSetup: chưa import được " + ThemePath + "; PanelSettings dùng theme mặc định.");
            EditorUtility.SetDirty(panel);
        }

        static FontAsset EnsureFontAsset(string ttfPath, string assetPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<FontAsset>(assetPath);
            if (existing != null)
                return existing;
            var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (font == null)
            {
                Debug.LogWarning($"ProjectSetup: chưa có {ttfPath} (M0 bước 6). Bỏ qua FontAsset.");
                return null;
            }
            var fa = FontAsset.CreateFontAsset(font);
            AssetDatabase.CreateAsset(fa, assetPath);
            if (fa.atlasTexture != null)
                AssetDatabase.AddObjectToAsset(fa.atlasTexture, fa);
            if (fa.material != null)
                AssetDatabase.AddObjectToAsset(fa.material, fa);
            EditorUtility.SetDirty(fa);
            return fa;
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
