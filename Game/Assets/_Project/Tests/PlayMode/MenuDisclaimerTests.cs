using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Narrative;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Disclaimer của menu chính: "Chơi mới" hiện màn chữ rồi vào prologue 00_Prologue (rồi 01); "Tiếp tục" và "Chọn chương" vào thẳng.
    /// Nút trong MainMenuView: 0 Tiếp tục · 1 Chơi mới · 2 Chọn chương.
    /// </summary>
    public class MenuDisclaimerTests
    {
        const int Continue = 0, NewGame = 1, Chapters = 2;

        GameRoot _root;

        [TearDown]
        public void TearDown()
        {
            PlayTestUtil.ExitSim();
            PlayTestUtil.UseTempStorage();
        }

        IEnumerator OpenMenu()
        {
            _root = PlayTestUtil.Root();
            _root.ResetProgress();
            SceneManager.LoadScene(SceneFlow.MenuScene);
            yield return PlayTestUtil.WaitUntil(() => SceneManager.GetActiveScene().name == SceneFlow.MenuScene
                                                     && _root.UI.Menu.Visible, 10f);
            Assert.IsTrue(_root.UI.Menu.Visible, "menu chính phải hiện");
        }

        IEnumerator WaitForScene(string scene)
        {
            yield return PlayTestUtil.WaitUntil(() => SceneManager.GetActiveScene().name == scene
                                                     && !_root.Scenes.IsLoading, 40f);
            Assert.AreEqual(scene, SceneManager.GetActiveScene().name, $"phải vào cảnh {scene}");
        }

        [UnityTest]
        public IEnumerator NewGameShowsDisclaimerThenEntersPrologue()
        {
            yield return OpenMenu();
            var director = Object.FindAnyObjectByType<MenuDirector>();
            Assert.IsNotNull(director, "thiếu MenuDirector");
            // Không bấm phím: màn tự qua sau disclaimerMaxSeconds (rút ngắn để test nhanh).
            director.disclaimerMaxSeconds = 1.5f;
            string body = _root.Content.GetString("disclaimer.body");

            _root.UI.Menu.Click(NewGame);
            yield return PlayTestUtil.WaitUntil(() => _root.UI.Card.Visible && _root.UI.Card.CurrentText == body, 3f);
            Assert.AreEqual(body, _root.UI.Card.CurrentText, "phải hiện nguyên văn disclaimer");
            Assert.IsFalse(_root.UI.Menu.Visible, "nút menu phải ẩn khi đọc disclaimer");
            Assert.AreEqual(SceneFlow.MenuScene, SceneManager.GetActiveScene().name, "chưa được vào game khi disclaimer còn hiện");

            yield return WaitForScene(SceneFlow.PrologueScene);
        }

        [UnityTest]
        public IEnumerator ChapterListFirstEntryEntersPrologueWithoutDisclaimer()
        {
            yield return OpenMenu();
            string body = _root.Content.GetString("disclaimer.body");

            _root.UI.Menu.Click(Chapters);
            yield return null;
            _root.UI.Menu.Click(0);
            float end = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < end)
            {
                Assert.AreNotEqual(body, _root.UI.Card.CurrentText, "Chọn chương không hiện disclaimer");
                yield return null;
            }
            yield return WaitForScene(SceneFlow.PrologueScene);
        }

        [UnityTest]
        public IEnumerator ChapterListSecondEntryEntersNhaRongWithoutDisclaimer()
        {
            yield return OpenMenu();
            string body = _root.Content.GetString("disclaimer.body");

            _root.UI.Menu.Click(Chapters);
            yield return null;
            _root.UI.Menu.Click(1);
            // Trong lúc chuyển cảnh không được có màn disclaimer.
            float end = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < end)
            {
                Assert.AreNotEqual(body, _root.UI.Card.CurrentText, "Chọn chương không hiện disclaimer");
                yield return null;
            }
            yield return WaitForScene(SceneFlow.GameScenes[0]);
        }

        [UnityTest]
        public IEnumerator ContinueEntersSavedSceneWithoutDisclaimer()
        {
            _root = PlayTestUtil.Root();
            _root.ResetProgress();
            SaveSystem.Write(SaveSystem.Capture(SceneFlow.GameScenes[1], _root.State, _root.Flags, _root.Notebook, _root.Events));
            SceneManager.LoadScene(SceneFlow.MenuScene);
            yield return PlayTestUtil.WaitUntil(() => SceneManager.GetActiveScene().name == SceneFlow.MenuScene
                                                     && _root.UI.Menu.Visible, 10f);
            string body = _root.Content.GetString("disclaimer.body");

            _root.UI.Menu.Click(Continue);
            float end = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < end)
            {
                Assert.AreNotEqual(body, _root.UI.Card.CurrentText, "Tiếp tục không hiện disclaimer");
                yield return null;
            }
            yield return WaitForScene(SceneFlow.GameScenes[1]);
        }
    }
}
