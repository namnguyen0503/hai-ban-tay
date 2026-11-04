using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Narrative;
using HaiBanTay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>Ảnh nghiệm thu chỗ chiếu phim 1911 (phố Sài Gòn): góc chéo như ảnh tham chiếu, chính diện, cận máy chiếu.</summary>
    [Category("Screenshots"), Explicit]
    public sealed class ProjectionShots
    {
        static PlayerRig Rig => PlayerRig.Current;
        static GameRoot Root => GameRoot.Instance;
        [TearDown] public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            ScreenshotCapture.RestoreUi(); PlayTestUtil.ExitSim();
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator CinemaViews()
        {
            Root.ResetProgress();
            PlayTestUtil.EnterSim(1f);
            Time.captureDeltaTime = 1f / 60f;
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.PrologueScene);
            ScreenshotCapture.RedirectUi();
            var d = Object.FindAnyObjectByType<PrologueDirector>();
            // Đợi tới nhịp "quan sát": đèn phố và vùng sáng đã dựng xong như lúc chơi thật (không dừng Director giữa chừng).
            yield return PlayTestUtil.WaitUntil(() => d.discoveries[1].available, 240f);
            Sim.Enabled = false;
            var projection = Object.FindAnyObjectByType<ProjectionFlicker>();
            Assert.AreEqual(12, projection.limbs.Length);
            Assert.AreEqual(2, projection.reels.Length);
            yield return new WaitForSeconds(.3f);
            Root.UI.Shift.Clear(); Root.UI.Prompt.HideHint(); Root.UI.Toast.Hide(); Root.Subtitles.StopAll(); Root.UI.Card.Hide(); Root.UI.SetFade(0f);
            // Le và người qua đường không che chỗ chiếu trong ảnh nghiệm thu.
            foreach (var npc in Object.FindObjectsByType<HaiBanTay.Npc.NpcAgent>(FindObjectsSortMode.None))
                foreach (var r in npc.GetComponentsInChildren<Renderer>()) r.enabled = false;
            ChoreTestUtil.Look(new Vector3(-6f, .12f, -6.4f), new Vector3(-10.4f, 1.6f, -8.8f));
            yield return new WaitForSeconds(.4f);
            yield return ScreenshotCapture.Capture("projection_oblique");
            ChoreTestUtil.Look(new Vector3(-9.5f, .12f, -6.2f), new Vector3(-9.5f, 1.8f, -9.3f));
            yield return new WaitForSeconds(.3f);
            yield return ScreenshotCapture.Capture("projection_front");
            ChoreTestUtil.Look(new Vector3(-10.4f, .12f, -6.3f), new Vector3(-12f, 1.6f, -8.2f));
            yield return new WaitForSeconds(.3f);
            yield return ScreenshotCapture.Capture("projection_projector");
        }
    }
}
