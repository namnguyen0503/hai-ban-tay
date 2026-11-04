using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Narrative;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace HaiBanTay.Tests
{
    public class Release15PlayTests
    {
        [TearDown]
        public void TearDown()
        {
            ScreenshotCapture.RestoreUi();
            Time.captureDeltaTime = 0;
            PlayTestUtil.ExitSim();
            SaveSystem.AllowInSim = false;
            GameRoot.Instance?.UI.Settings.Close();
            GameSettings.ResetForTests();
            PlayTestUtil.UseTempStorage();
        }

        public static IEnumerator SettingsFit(int width, int height)
        {
            var root = PlayTestUtil.Root();
            root.UI.ResetTransient();
            root.UI.SetFade(0);
            ScreenshotCapture.RedirectUi(width, height);
            var settings = GameSettings.Current.Clone();
            foreach (int size in new[] { 0, 1, 2 })
            {
                settings.UiTextSize = size;
                settings.SubtitleScale = 1.4f;
                GameSettings.Apply(settings.Clone());
                // Notch giả lập ở bên trái + mép dưới, kiểm đổi từ gốc trái dưới sang trái trên.
                root.UI.ApplySafeArea(new Rect(110, 24, width - 140, height - 24), width, height);
                root.UI.Settings.Open(null);
                yield return null; yield return null; yield return null;
                var bounds = root.UI.Root.worldBound;
                Assert.Greater(bounds.width, 100);
                var safe = new Rect(bounds.width * 110 / width, 0, bounds.width * (width - 140) / width, bounds.height * (height - 24) / height);
                int count = 0;
                foreach (var label in root.UI.Settings.Root.Query<TextElement>().ToList())
                {
                    if (string.IsNullOrEmpty(label.text)) continue;
                    var scroll = label.GetFirstAncestorOfType<ScrollView>();
                    scroll?.ScrollTo(label);
                    yield return null; yield return null;
                    var r = label.worldBound;
                    Assert.IsTrue(Contains(safe, r), $"{width}×{height}, cỡ {size}: {label.text} ngoài vùng notch: {r} / {safe}");
                    if (scroll != null)
                        Assert.IsTrue(Contains(scroll.contentViewport.worldBound, r), label.text + " bị che bởi vùng cuộn");
                    var c = label.contentRect;
                    var needed = label.MeasureTextSize(label.text, c.width, VisualElement.MeasureMode.AtMost, 0, VisualElement.MeasureMode.Undefined);
                    Assert.LessOrEqual(needed.x, c.width + 1.5f, label.text + " tràn ngang");
                    Assert.LessOrEqual(needed.y, c.height + 1.5f, label.text + " tràn dọc");
                    count++;
                }
                Assert.Greater(count, 40, "phải đo mọi chữ và nút trong Settings, kể cả ngoài khung cuộn ban đầu");
                root.UI.Settings.Close();
            }
            settings.SubtitleBackground = true;
            settings.SubtitleOpacity = 0.8f;
            GameSettings.Apply(settings);
            var line = root.Content.Subtitles.Lines[0];
            root.UI.Subtitles.Show("", line.Fr, line.Vi);
            yield return null; yield return null;
            Assert.AreEqual(0.8f, root.UI.Subtitles.Root.resolvedStyle.backgroundColor.a, 0.001f);
            Assert.IsTrue(Contains(root.UI.Root.worldBound, root.UI.Subtitles.Root.worldBound));
            settings.SubtitleBackground = false;
            GameSettings.Apply(settings);
            yield return null;
            Assert.AreEqual(0, root.UI.Subtitles.Root.resolvedStyle.backgroundColor.a, 0.001f);
        }

        static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - 1.5f && inner.xMax <= outer.xMax + 1.5f &&
            inner.yMin >= outer.yMin - 1.5f && inner.yMax <= outer.yMax + 1.5f;

        [UnityTest] public IEnumerator SettingsFitPhoneAtAllThreeTextSizes() => SettingsFit(2760, 1256);
        [UnityTest] public IEnumerator SettingsFit1080pAtAllThreeTextSizes() => SettingsFit(1920, 1080);

        [UnityTest, Timeout(120000)]
        public IEnumerator ContinueRestartsSavedDayWithoutMiddayWork()
        {
            var root = PlayTestUtil.Root();
            // Cảnh mở đầu ngày chờ người chơi; Sim (tốc độ thường) tự đi qua, ca vào pha đầu.
            PlayTestUtil.EnterSim(1f);
            SaveSystem.AllowInSim = true;
            yield return PlayTestUtil.WaitUntil(() => !root.Scenes.IsLoading, 30f);
            // Vào ngày 3 như chọn chương (sự kiện các ngày trước đã đánh dấu), không nạp cảnh trần thiếu trạng thái mở đầu.
            ChapterStart.PrepareDay(root, 3, 4, false);
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            // Ngày có cảnh mở đầu (thẻ, lời quản bếp) trước pha đầu: chờ tới khi ca thật sự vào pha đầu.
            yield return PlayTestUtil.WaitUntil(() => SceneDirector.Current is ShiftDirector shift && shift.PhaseIndex >= 0, 120);
            var first = SceneDirector.Current as ShiftDirector;
            Assert.IsNotNull(first);
            Assert.AreEqual(0, first.PhaseIndex, $"ca kẹt ở {first.Beat}");
            var checkpoint = SaveSystem.Read();
            Assert.IsNotNull(checkpoint);
            Assert.AreEqual(3, checkpoint.Day);
            root.State.Grime = 1;
            root.Flags.Set("release15_midday");
            root.State.Learn("release15_midday");
            SceneManager.LoadScene(SceneFlow.MenuScene);
            yield return PlayTestUtil.WaitUntil(() => root.UI.Menu.Visible, 20);
            Assert.AreEqual("Tiếp tục từ đầu ngày", root.Content.GetString("menu.continue"));
            root.UI.Menu.Click(0);
            yield return PlayTestUtil.WaitUntil(() => SceneDirector.Current is ShiftDirector shift && shift.Day.Number == 3 && shift.PhaseIndex >= 0, 120);
            var restored = SceneDirector.Current as ShiftDirector;
            Assert.IsNotNull(restored);
            Assert.AreEqual(3, restored.Day.Number);
            Assert.AreEqual(0, restored.PhaseIndex);
            Assert.AreEqual(checkpoint.Grime, root.State.Grime, 0.001f);
            Assert.IsFalse(root.Flags.Has("release15_midday"));
            Assert.IsFalse(root.State.Knows("release15_midday"));
            Assert.AreEqual(4, root.State.MaxDay);
        }

        [UnityTest]
        public IEnumerator TouchToggleClearsOnFocusLossAndSceneReset()
        {
            var root = PlayTestUtil.Root();
            var s = GameSettings.Current.Clone(); s.TouchToggle = true; GameSettings.Apply(s);
            var touch = root.GetComponent<HaiBanTay.UI.TouchControls>();
            bool created = touch == null;
            if (created)
            {
                touch = root.gameObject.AddComponent<HaiBanTay.UI.TouchControls>();
                touch.Initialize(root.Input, root.UI, root.Scenes, root.Content, null, () => false);
            }
            root.Input.Touch.UseHold.Press(true); root.Input.Touch.UseHold.Release();
            Assert.IsTrue(root.Input.Touch.UseHold.Held(true));
            touch.SendMessage("OnApplicationFocus", false);
            Assert.IsFalse(root.Input.Touch.UseHold.Held(true));
            root.Input.Touch.RaiseHold.Press(true);
            root.Input.ClearBlocks();
            Assert.IsFalse(root.Input.Touch.RaiseHold.Held(true));
            if (created) Object.Destroy(touch);
            yield return null;
        }
    }
}
