using System.Collections;
using HaiBanTay.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    public class BootTests
    {
        [UnityTest]
        public IEnumerator BootLoadsFirstSceneWithoutErrors()
        {
            PlayTestUtil.UseTempStorage();
            SceneManager.LoadScene(SceneFlow.BootScene);
            yield return null;
            Assert.IsNotNull(GameRoot.Instance, "GameRoot không được tạo");
            Assert.IsTrue(GameRoot.Instance.Ready);

            float timeout = 10f;
            while (SceneManager.GetActiveScene().name != SceneFlow.MenuScene && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.AreEqual(SceneFlow.MenuScene, SceneManager.GetActiveScene().name);
            yield return null;
            Assert.IsTrue(GameRoot.Instance.UI.Menu.Visible, "menu chính phải hiện");
            GameRoot.Instance.UI.Menu.Hide();
            GameRoot.Instance.Input.ClearBlocks();
        }
    }
}
