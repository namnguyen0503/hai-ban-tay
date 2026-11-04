using System.Collections.Generic;
using HaiBanTay.Editor.Import;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>Import settings đúng ngân sách texture (tầng Laptop hạ một mức mip, PLAN 3.3).</summary>
    public class ImportBudgetTests
    {
        static IEnumerable<string> PathsOf(string filter, params string[] folders)
        {
            var existing = new List<string>();
            foreach (var f in folders)
                if (AssetDatabase.IsValidFolder(f.TrimEnd('/')))
                    existing.Add(f.TrimEnd('/'));
            if (existing.Count == 0)
                yield break;
            foreach (var guid in AssetDatabase.FindAssets(filter, existing.ToArray()))
                yield return AssetDatabase.GUIDToAssetPath(guid);
        }

        [Test]
        public void HeroTexturesGetDoubleBudget()
        {
            Assert.AreEqual(ImportBudget.HeroMaxSize, ImportBudget.TextureBudget("Assets/_Project/Art/Textures/Hands_Albedo.png"));
            Assert.AreEqual(ImportBudget.HeroMaxSize, ImportBudget.TextureBudget("Assets/_Project/Art/Textures/CopperPot_BaseColor.png"));
            Assert.AreEqual(ImportBudget.DefaultMaxSize, ImportBudget.TextureBudget("Assets/_Project/Art/Textures/Stove_BaseColor.png"));
            Assert.AreEqual(2 * ImportBudget.DefaultMaxSize, ImportBudget.HeroMaxSize);
        }

        [Test]
        public void MusicAndAmbienceStream()
        {
            Assert.AreEqual(AudioClipLoadType.Streaming, ImportBudget.ExpectedLoadType("Assets/_Project/Audio/Music/motif.ogg"));
            Assert.AreEqual(AudioClipLoadType.Streaming, ImportBudget.ExpectedLoadType("Assets/_Project/Audio/Ambience/sea.ogg"));
            Assert.AreEqual(AudioClipLoadType.CompressedInMemory, ImportBudget.ExpectedLoadType("Assets/_Project/Audio/VO/chef_01.wav"));
            Assert.AreEqual(AudioClipLoadType.DecompressOnLoad, ImportBudget.ExpectedLoadType("Assets/_Project/Audio/SFX/coin.wav"));
        }

        [Test]
        public void ProjectTexturesWithinBudget()
        {
            var bad = new List<string>();
            foreach (var path in PathsOf("t:Texture2D", ImportBudget.ArtRoot, ImportBudget.PrefabArtRoot))
            {
                if (!ImportBudget.InTextureScope(path) || !(AssetImporter.GetAtPath(path) is TextureImporter ti))
                    continue;
                int budget = ImportBudget.TextureBudget(path);
                if (ti.maxTextureSize > budget)
                    bad.Add($"{path}: max {ti.maxTextureSize} > {budget}");
                if (ti.textureCompression == TextureImporterCompression.Uncompressed)
                    bad.Add(path + ": chưa nén");
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void ProjectAudioIsVorbisWithRightLoadType()
        {
            var bad = new List<string>();
            foreach (var path in PathsOf("t:AudioClip", ImportBudget.AudioRoot))
            {
                if (!(AssetImporter.GetAtPath(path) is AudioImporter ai))
                    continue;
                var s = ai.defaultSampleSettings;
                if (s.compressionFormat != AudioCompressionFormat.Vorbis)
                    bad.Add(path + ": không phải Vorbis");
                if (s.loadType != ImportBudget.ExpectedLoadType(path))
                    bad.Add($"{path}: loadType {s.loadType}");
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }
    }
}
