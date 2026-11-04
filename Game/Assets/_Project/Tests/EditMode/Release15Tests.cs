using System;
using System.Globalization;
using System.IO;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Performance;
using HaiBanTay.Player;
using HaiBanTay.Editor.Import;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;

namespace HaiBanTay.Tests
{
    public class Release15Tests
    {
        [Test]
        public void AndroidDefaultsToLaptop()
        {
            Assert.AreEqual(QualityTier.Laptop, QualityTierManager.Resolve(RuntimePlatform.Android, Array.Empty<string>()));
        }

        [TestCase(60)]
        [TestCase(120)]
        public void MobileAutomaticCapsAtThirty(int refresh)
        {
            var plan = FrameCap.Resolve(QualityTier.Laptop, false, false, false, Array.Empty<string>(), null, 0, refresh);
            Assert.AreEqual(30, plan.TargetFps);
            Assert.AreEqual(0, plan.VSync);
        }

        [Test]
        public void SettingsContainPersistentAccessibilityControls()
        {
            var json = JObject.FromObject(new GameSettings());
            Assert.IsNotNull(json["TouchToggle"], "thiếu chế độ chạm bật/tắt");
            Assert.IsNotNull(json["UiTextSize"], "thiếu ba cỡ chữ UI");
            Assert.AreEqual(true, (bool?)json["SubtitleBackground"]);
            Assert.AreEqual(0.7f, (float?)json["SubtitleOpacity"]);
        }

        [Test]
        public void ContinueLabelExplainsDayCheckpoint()
        {
            Assert.AreEqual("Tiếp tục từ đầu ngày", ContentDatabase.LoadFromResources().GetString("menu.continue"));
        }

        [Test]
        public void PerfCsvDefinesBeatLowAndRenderCounters()
        {
            var header = typeof(PerfRecorder).GetField("CsvHeader");
            Assert.IsNotNull(header, "F8 cần định dạng CSV được kiểm tra");
            var columns = ((string)header.GetRawConstantValue()).Split(',');
            foreach (var name in new[] { "beat", "fps_avg", "fps_1pct_low", "draw_calls_avg", "setpass_avg", "build_id" })
                CollectionAssert.Contains(columns, name);
        }

        [Test]
        public void AccessibilitySettingsSurviveSaveAndOldFilesKeepDefaults()
        {
            string oldFolder = GameSettings.Folder;
            bool oldSim = Sim.Enabled;
            string folder = Path.Combine(Application.dataPath, "../../Logs/release15-settings-test");
            try
            {
                GameSettings.Folder = folder;
                Sim.Enabled = false;
                var s = new GameSettings { UiTextSize = 2, SubtitleBackground = false, SubtitleOpacity = 0.4f,
                    TouchToggle = true, CameraShake = 0, HeadBob = 0, FpsLimit = 60 };
                GameSettings.Apply(s);
                GameSettings.ResetForTests();
                GameSettings.Load();
                Assert.AreEqual(JsonConvert.SerializeObject(s), JsonConvert.SerializeObject(GameSettings.Current));
                File.WriteAllText(Path.Combine(folder, "settings.json"), "{\"FpsLimit\":120}");
                GameSettings.Load();
                Assert.AreEqual(120, GameSettings.Current.FpsLimit, "không ghi đè lựa chọn FPS có sẵn");
                Assert.AreEqual(Application.platform == RuntimePlatform.Android, GameSettings.Current.TouchToggle);
                Assert.AreEqual(1, GameSettings.Current.UiTextSize);
                Assert.IsTrue(GameSettings.Current.SubtitleBackground);
                var invalid = new GameSettings { UiTextSize = 100, SubtitleOpacity = -1, CameraShake = -1, HeadBob = -1 };
                invalid.Clamp();
                Assert.AreEqual(2, invalid.UiTextSize);
                Assert.AreEqual(0, invalid.SubtitleOpacity);
                Assert.AreEqual(0, invalid.CameraShake);
                Assert.AreEqual(0, invalid.HeadBob);
            }
            finally
            {
                GameSettings.Folder = oldFolder;
                Sim.Enabled = oldSim;
                GameSettings.ResetForTests();
            }
        }

        [Test]
        public void TouchToggleStaysHeldUntilNextTapAndClearReleasesIt()
        {
            var touch = new TouchState();
            var b = touch.UseHold;
            b.Press(true);
            b.Press(true);
            b.Release(); b.Release();
            Assert.IsTrue(b.Held(true), "hai ngón cùng chạm không đảo chốt hai lần");
            b.Press(true); b.Release();
            Assert.IsFalse(b.Held(true));
            b.Press(false);
            Assert.IsTrue(b.Held(false));
            b.Release();
            Assert.IsFalse(b.Held(false), "chế độ giữ vẫn nhả khi nhấc ngón");
            b.Press(true); b.Release();
            touch.RaiseHold.Press(true);
            touch.Clear();
            Assert.IsFalse(b.Held(true));
            Assert.IsFalse(touch.RaiseHold.Held(true));
        }

        [Test]
        public void OnePercentLowUsesSlowestFramesAndMissingCountersStayUnknown()
        {
            var samples = new PerfBeatSamples();
            for (int i = 0; i < 198; i++) samples.Add(0.01f, 20, 10);
            samples.Add(0.1f, 40, 20); samples.Add(0.2f, 40, 20);
            Assert.AreEqual(200 / 2.28, samples.AverageFps, 0.001);
            Assert.AreEqual(2 / 0.3, samples.OnePercentLow(), 0.001);
            Assert.AreEqual(5, samples.MinFps, 0.001);
            Assert.AreEqual(20.2, samples.DrawCallsAverage, 0.001);
            Assert.AreEqual(10.1, samples.SetPassAverage, 0.001);
            var missing = new PerfBeatSamples();
            missing.Add(0, 0, 0); missing.Add(float.NaN, 0, 0); missing.Add(0.1f, -1, -1);
            Assert.AreEqual(1, missing.Count);
            Assert.AreEqual(-1, missing.DrawCallsAverage);
            Assert.AreEqual(-1, missing.SetPassMax);
        }

        [Test]
        public void CsvSplitsBeatsAndTiersAndFlushesFinalPartialBeatInInvariantCulture()
        {
            var go = new GameObject("Release15PerfTest");
            var recorder = go.AddComponent<PerfRecorder>();
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Logs/release15-perf-format.csv"));
            var oldCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
                recorder.Begin(path);
                recorder.RecordFrame("Galley,\"test\"", "D1.dawn", QualityTier.Laptop, 0.1f, 50, 20);
                recorder.RecordFrame("Galley,\"test\"", "D1.dawn", QualityTier.Laptop, 0.1f, 70, 30);
                recorder.RecordFrame("Galley", "D1.ash", QualityTier.Laptop, 0.2f, -1, -1);
                recorder.RecordFrame("Galley", "D1.ash", QualityTier.PC, 0.1f, 80, 40);
                recorder.Stop();
                var rows = File.ReadAllLines(path);
                Assert.AreEqual(4, rows.Length, "chốt lượt cuối dù ngắn hơn một giây");
                Assert.AreEqual(PerfRecorder.CsvHeader, rows[0]);
                StringAssert.Contains("\"Galley,\"\"test\"\"\",\"D1.dawn\",10.000,10.000,100.000,Laptop,0.200,2,10.000,60.000,25.000,70,30,", rows[1]);
                StringAssert.Contains(",\"D1.ash\",5.000,5.000,200.000,Laptop,0.200,1,5.000,-1.000,-1.000,-1,-1,", rows[2]);
                StringAssert.Contains(",PC,0.100,1,", rows[3]);
                Assert.AreEqual("\"a,\"\"b\"\"\n\"", PerfRecorder.CsvField("a,\"b\"\n"));
            }
            finally
            {
                recorder.Stop();
                UnityEngine.Object.DestroyImmediate(go);
                CultureInfo.CurrentCulture = oldCulture;
            }
        }

        [Test]
        public void AndroidImportsHaveExplicitMobileBudgets()
        {
            var bad = new System.Collections.Generic.List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/_Project/Art" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!ImportBudget.InTextureScope(path) || !(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                var a = ti.GetPlatformTextureSettings("Android");
                if (!a.overridden || a.maxTextureSize > ImportBudget.AndroidTextureBudget(path) || a.format != TextureImporterFormat.ASTC_6x6)
                    bad.Add(path);
            }
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/_Project/Audio" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ai = (AudioImporter)AssetImporter.GetAtPath(path);
                var a = ai.GetOverrideSampleSettings("Android");
                if (!ai.ContainsSampleSettingsOverride("Android") || a.compressionFormat != AudioCompressionFormat.Vorbis ||
                    a.quality != ImportBudget.AndroidVorbisQuality || a.loadType != ImportBudget.ExpectedLoadType(path))
                    bad.Add(path);
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }
    }
}
