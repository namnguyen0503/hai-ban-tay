using System.Collections;
using System.Collections.Generic;
using System.IO;
using HaiBanTay.Core;
using HaiBanTay.Ending;
using HaiBanTay.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HaiBanTay.Tests
{
    public static class PlayTestUtil
    {
        /// <summary>GameRoot cho test, với bản lưu và cài đặt ở thư mục tạm: Editor và bản build dùng chung
        /// persistentDataPath, test không được ghi đè bản lưu thật của người chơi.</summary>
        public static GameRoot Root()
        {
            UseTempStorage();
            var root = GameRoot.EnsureExists();
            GameSettings.ResetForTests();
            return root;
        }

        public static void UseTempStorage()
        {
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Logs/test-storage"));
            SaveSystem.Folder = folder;
            GameSettings.Folder = folder;
            // Mỗi test bắt đầu không có bản lưu (cảnh 02/03 tự lưu khi không ở chế độ Sim).
            SaveSystem.Delete();
        }

        public static void EnterSim(float timeScale)
        {
            Sim.Enabled = true;
            Sim.StepSeconds = 0.5f;
            EndingDirector.TimeScaleForTests = timeScale;
            Time.timeScale = timeScale;
        }

        public static void ExitSim()
        {
            Sim.Enabled = false;
            EndingDirector.TimeScaleForTests = 1f;
            Time.timeScale = 1f;
            PauseController.QuitOnEscape = false;
            var root = GameRoot.Instance;
            if (root != null)
            {
                root.Input.ClearBlocks();
                root.UI.ResetTransient();
                root.Subtitles.StopAll();
                root.UI.SetFade(0f);
            }
        }

        public static IEnumerator LoadAndSettle(string scene)
        {
            SceneManager.LoadScene(scene);
            yield return null;
            yield return null;
        }

        /// <summary>Chờ tới khi điều kiện đúng hoặc hết thời gian thật (giây).</summary>
        public static IEnumerator WaitUntil(System.Func<bool> done, float realSeconds)
        {
            float end = Time.realtimeSinceStartup + realSeconds;
            while (!done() && Time.realtimeSinceStartup < end)
                yield return null;
        }

        public static List<string> Record(GameEvents events)
        {
            var list = new List<string>();
            events.AnyRaised += id => list.Add(id);
            return list;
        }
    }
}
