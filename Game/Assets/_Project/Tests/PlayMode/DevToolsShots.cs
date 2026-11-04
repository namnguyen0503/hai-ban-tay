using System.Collections;
using System.IO;
using HaiBanTay.DevTools;
using HaiBanTay.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>Nút cảm ứng (bản điện thoại) và ghi chú debug: ảnh bố cục + đủ tệp khi lưu.</summary>
    [Category("Screenshots"), Explicit]
    public sealed class DevToolsShots
    {
        string _dir;

        [TearDown]
        public void Done()
        {
            System.Environment.SetEnvironmentVariable("HBT_NOTES_DIR", null);
            PlayTestUtil.ExitSim();
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator TouchLayoutAndNoteSaves()
        {
            yield return ChoreTestUtil.Galley();
            var root = PlayTestUtil.Root();
            var notes = root.GetComponent<DebugNotes>();
            Assert.IsNotNull(notes, "GameRoot phải có DebugNotes.");
            var touch = root.GetComponent<TouchControls>() ?? root.gameObject.AddComponent<TouchControls>();
            touch.Initialize(root.Input, root.UI, root.Scenes, root.Content, notes.Open, () => DebugNotes.AnyOpen);
            yield return null;
            yield return ScreenshotCapture.Capture("devtools_touch_hud");

            root.Input.Touch.Press(HaiBanTay.Player.TouchButton.Sit);
            yield return null;
            yield return null;
            Assert.IsFalse(root.Input.Touch.Pressed(HaiBanTay.Player.TouchButton.Sit), "Nút chạm chỉ đúng một khung.");

            _dir = Path.Combine(Application.temporaryCachePath, "notes_test");
            System.Environment.SetEnvironmentVariable("HBT_NOTES_DIR", _dir);
            notes.Open();
            yield return PlayTestUtil.WaitUntil(() => notes.IsOpen, 5f);
            Assert.IsTrue(notes.IsOpen);
            Assert.IsFalse(root.Input.GameplayEnabled, "Ghi chú chặn điều khiển.");
            Assert.AreEqual(0f, Time.timeScale);
            notes.Draft = "Thử ghi chú: nồi bị xuyên tay";
            yield return null;
            yield return ScreenshotCapture.Capture("devtools_note_open");
            notes.Submit();
            Assert.IsFalse(notes.IsOpen);
            Assert.IsTrue(root.Input.GameplayEnabled);
            Assert.IsNotNull(notes.LastSaved);
            foreach (var f in new[] { "note.txt", "state.json", "log.txt" })
                Assert.IsTrue(File.Exists(Path.Combine(notes.LastSaved, f)), f);
            string state = File.ReadAllText(Path.Combine(notes.LastSaved, "state.json"));
            StringAssert.Contains("02_Galley", state);
            StringAssert.Contains("nồi bị xuyên tay", state);
            Debug.Log("DevToolsShots: state.json\n" + state.Substring(0, Mathf.Min(4000, state.Length)));
            Object.Destroy(touch);
        }
    }
}
