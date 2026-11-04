using System.Collections;
using HaiBanTay.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    [Category("Screenshots"), Explicit]
    public class Release15Shots
    {
        [TearDown]
        public void TearDown()
        {
            ScreenshotCapture.RestoreUi();
            GameRoot.Instance?.UI.Settings.Close();
            GameSettings.ResetForTests();
            PlayTestUtil.ExitSim();
        }

        static IEnumerator Capture(int width, int height)
        {
            var root = PlayTestUtil.Root();
            SceneManager.LoadScene(SceneFlow.MenuScene);
            yield return PlayTestUtil.WaitUntil(() => root.UI.Menu.Visible, 20);
            var settings = GameSettings.Current.Clone();
            settings.UiTextSize = 2;
            settings.SubtitleScale = 1.4f;
            settings.SubtitleBackground = true;
            settings.SubtitleOpacity = 0.8f;
            settings.TouchToggle = true;
            GameSettings.Apply(settings);
            ScreenshotCapture.RedirectUi(width, height);
            root.UI.ApplySafeArea(new Rect(110, 24, width - 140, height - 24), width, height);
            yield return null; yield return null;
            yield return ScreenshotCapture.Capture($"release15_menu_{width}x{height}", width, height);
            root.UI.Menu.Hide();
            root.UI.Settings.Open(null);
            yield return null; yield return null;
            yield return ScreenshotCapture.Capture($"release15_settings_{width}x{height}", width, height);
            root.UI.Settings.Close();
            var line = root.Content.GetLine("mai_d1_1");
            root.Content.Subtitles.Speakers.TryGetValue(line.Speaker, out var speaker);
            root.UI.Subtitles.Show(speaker, line.Fr, line.Vi);
            yield return null; yield return null;
            yield return ScreenshotCapture.Capture($"release15_subtitles_{width}x{height}", width, height);
            Assert.AreEqual(0.8f, root.UI.Subtitles.Root.resolvedStyle.backgroundColor.a, 0.001f);
        }

        [UnityTest] public IEnumerator SettingsAndSubtitlesPhone() => Capture(2760, 1256);
        [UnityTest] public IEnumerator SettingsAndSubtitles1080p() => Capture(1920, 1080);
    }
}
