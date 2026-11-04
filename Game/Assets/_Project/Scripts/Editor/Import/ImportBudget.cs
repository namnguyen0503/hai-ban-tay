using System;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.Import
{
    /// <summary>
    /// Ép import settings: texture 2K (4K cho đôi tay, chảo/nồi đồng), có nén. Tầng Laptop hạ một mức mip
    /// (QualitySettings.globalTextureMipmapLimit = 1) nên vẫn đúng ngân sách 1K/2K của PLAN 3.3;
    /// âm thanh Vorbis, nhạc và ambience dạng streaming. Chỉ hạ giới hạn, không nâng cấu hình nhóm đã đặt thấp hơn.
    /// </summary>
    public sealed class ImportBudget : AssetPostprocessor
    {
        public const string ArtRoot = "Assets/_Project/Art/";
        public const string PrefabArtRoot = "Assets/_Project/Prefabs/Art/";
        public const string AudioRoot = "Assets/_Project/Audio/";
        public const string LutFolder = "Assets/_Project/Art/LUT/";

        public const int DefaultMaxSize = 2048;
        public const int HeroMaxSize = 4096;
        public const float VorbisQuality = 0.7f;
        public const float AndroidVorbisQuality = 0.5f;

        public static int AndroidTextureBudget(string path) => TextureBudget(path) / 2;

        /// <summary>Áp cả asset đã import từ trước. Không sửa .meta bằng tay, không cần đổi build target.</summary>
        [MenuItem("HaiBanTay/Release/Apply Android Import Budget")]
        public static void ApplyAndroidBudget()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtRoot.TrimEnd('/'), PrefabArtRoot.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!InTextureScope(path) || !(AssetImporter.GetAtPath(path) is TextureImporter ti))
                    continue;
                string before = EditorJsonUtility.ToJson(ti);
                ConfigureTexture(ti, path);
                if (before != EditorJsonUtility.ToJson(ti))
                    ti.SaveAndReimport();
            }
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is AudioImporter ai))
                    continue;
                string before = EditorJsonUtility.ToJson(ai);
                ConfigureAudio(ai, path);
                if (before != EditorJsonUtility.ToJson(ai))
                    ai.SaveAndReimport();
            }
        }

        // Vật chính được 4K ở tầng PC, 2K ở tầng Laptop (PLAN 3.3, mục 12).
        static readonly string[] HeroKeywords = { "hand", "copperpot", "copper_pot", "copper" };

        public static bool InTextureScope(string path) =>
            (path.StartsWith(ArtRoot) || path.StartsWith(PrefabArtRoot)) && !path.StartsWith(LutFolder);

        public static bool InAudioScope(string path) => path.StartsWith(AudioRoot);

        public static int TextureBudget(string path)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            // Model nhập từ Staging giữ tên gốc (brass_pot_01...): xét cả tên thư mục slot.
            string folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path) ?? "").ToLowerInvariant();
            if (folder == "textures")
                folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(path)) ?? "").ToLowerInvariant();
            foreach (var k in HeroKeywords)
                if (name.Contains(k) || folder.Contains(k))
                    return HeroMaxSize;
            return DefaultMaxSize;
        }

        public static bool IsStreamed(string path) =>
            path.StartsWith(AudioRoot + "Music/") || path.StartsWith(AudioRoot + "Ambience/");

        public static AudioClipLoadType ExpectedLoadType(string path)
        {
            if (IsStreamed(path))
                return AudioClipLoadType.Streaming;
            if (path.StartsWith(AudioRoot + "VO/"))
                return AudioClipLoadType.CompressedInMemory;
            return AudioClipLoadType.DecompressOnLoad;
        }

        void OnPreprocessTexture()
        {
            if (!InTextureScope(assetPath))
                return;
            ConfigureTexture((TextureImporter)assetImporter, assetPath);
        }

        static void ConfigureTexture(TextureImporter importer, string path)
        {
            int budget = TextureBudget(path);
            if (importer.maxTextureSize > budget)
                importer.maxTextureSize = budget;
            if (importer.textureCompression == TextureImporterCompression.Uncompressed)
                importer.textureCompression = TextureImporterCompression.Compressed;

            var standalone = importer.GetPlatformTextureSettings("Standalone");
            if (standalone.overridden && standalone.maxTextureSize > budget)
            {
                standalone.maxTextureSize = budget;
                importer.SetPlatformTextureSettings(standalone);
            }
            var android = importer.GetPlatformTextureSettings("Android");
            int previousMax = android.overridden ? Math.Min(android.maxTextureSize, importer.maxTextureSize) : importer.maxTextureSize;
            android.name = "Android";
            android.overridden = true;
            android.maxTextureSize = Math.Min(previousMax, AndroidTextureBudget(path));
            android.format = TextureImporterFormat.ASTC_6x6;
            android.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(android);
        }

        void OnPreprocessAudio()
        {
            if (!InAudioScope(assetPath))
                return;
            ConfigureAudio((AudioImporter)assetImporter, assetPath);
        }

        static void ConfigureAudio(AudioImporter importer, string path)
        {
            var s = importer.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = VorbisQuality;
            s.loadType = ExpectedLoadType(path);
            s.preloadAudioData = s.loadType != AudioClipLoadType.Streaming;
            importer.defaultSampleSettings = s;
            importer.loadInBackground = IsStreamed(path);
            importer.ClearSampleSettingOverride("Standalone");
            s.quality = AndroidVorbisQuality;
            importer.SetOverrideSampleSettings("Android", s);
        }
    }
}
