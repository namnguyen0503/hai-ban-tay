using System.Collections;
using System.Linq;
using HaiBanTay.Core;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>Ảnh duyệt ngày 1 (W2, 14/10): thẻ tua nhanh bữa trưa và bữa tối, bữa nhân viên ngồi ăn với ông Mai có thoại,
    /// buổi tối ông Mai ngồi bàn bài còn Ba tự chép chữ. Chạy đúng luồng của ShiftDirector (Sim tua tới hết lượt rửa nồi đầu).</summary>
    [Category("Screenshots"), Explicit]
    public sealed class Day1Shots
    {
        static GameRoot Root => GameRoot.Instance;
        static PlayerRig Rig => PlayerRig.Current;

        [TearDown]
        public void TearDown()
        {
            if (Root != null) { Root.Input.SimUse = false; Root.Input.SimLook = Vector2.zero; Root.Subtitles.StopAll(); }
            ScreenshotCapture.RestoreUi();
            PlayTestUtil.ExitSim();
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator MontageMealWithMaiAndEveningStudy()
        {
            PlayTestUtil.ExitSim();
            PlayTestUtil.Root().ResetProgress();
            SceneManager.LoadScene(SceneFlow.GameScenes[1]);
            ShiftDirector d = null;
            yield return PlayTestUtil.WaitUntil(() => (d = Object.FindAnyObjectByType<ShiftDirector>()) != null && d.Beat == "D1.sweep", 60f);
            Assert.AreEqual("D1.sweep", d.Beat);
            PlayTestUtil.EnterSim(4f);
            // Hết lượt rửa nồi đầu thì tắt Sim: thẻ tua nhanh bữa trưa hiện như lúc chơi thật.
            yield return PlayTestUtil.WaitUntil(() => d.Beat == "D1.dishes" && d.Board.AllDone, 300f);
            Assert.AreEqual("D1.dishes", d.Beat);
            PlayTestUtil.ExitSim();

            yield return PlayTestUtil.WaitUntil(() => d.Beat == "D1.lunch" && Root.UI.Card.Visible, 30f);
            yield return new WaitForSeconds(1.5f);
            yield return ScreenshotCapture.Capture("day1_01_montage_lunch");
            yield return PlayTestUtil.WaitUntil(() => d.Beat == "D1.dinner" && Root.UI.Card.Visible, 30f);
            yield return new WaitForSeconds(1.5f);
            yield return ScreenshotCapture.Capture("day1_02_montage_dinner");

            // Bữa nhân viên: ngồi vào bàn, ông Mai mời ăn và nói chuyện.
            yield return PlayTestUtil.WaitUntil(() => d.Beat == "D1.crew_meal" && !Root.UI.Card.Visible && Rig.ControlEnabled, 60f);
            var soup = d.storyStations.OfType<VoyageEncounter>().First(x => x.Kind == OrderKinds.Soup);
            yield return PlayTestUtil.WaitUntil(() => soup.Pending > 0, 30f);
            ChoreTestUtil.Look(soup.stand, soup.target.transform.position);
            yield return new WaitForSeconds(0.4f);
            Root.Subtitles.StopAll();
            yield return ScreenshotCapture.Capture("day1_03_meal_table_before_sitting");
            soup.OnInteract(soup.target, Rig.interactor);
            // Ngồi xuống ghế đối diện ông Mai: góc nhìn của người ngồi, ông Mai mời ăn.
            yield return PlayTestUtil.WaitUntil(() => Root.Subtitles.CurrentLineId == "mai_meal_1", 10f);
            yield return new WaitForSeconds(1.2f);
            Assert.IsTrue(soup.Busy, "ông Mai đang nói");
            yield return ScreenshotCapture.Capture("day1_04_meal_seated_mai_invites");
            yield return PlayTestUtil.WaitUntil(() => soup.Stage == 1 && !soup.Busy, 60f);
            soup.OnInteract(soup.target, Rig.interactor);
            // Câu ông nhắc lời bàn tán hôm Ba lên xin việc (S16).
            yield return PlayTestUtil.WaitUntil(() => Root.Subtitles.CurrentLineId == "mai_meal_4", 40f);
            LookAt(soup.speaker.Position + Vector3.up * 0.95f);
            yield return new WaitForSeconds(1.2f);
            yield return ScreenshotCapture.Capture("day1_05_meal_mai_recalls");
            yield return PlayTestUtil.WaitUntil(() => soup.Pending == 0 && !soup.Busy, 40f);

            // Buổi tối: ông Mai ngồi bàn bài, Ba tự chép chữ nghe được trong ngày.
            yield return PlayTestUtil.WaitUntil(() => d.Beat == "D1.evening", 90f);
            yield return new WaitForSeconds(1.5f);
            var cards = d.cast.cardMai != null ? d.cast.cardMai.transform.position : new Vector3(2.2f, 0.95f, 0.2f);
            ChoreTestUtil.Look(new Vector3(2.95f, 0f, -0.35f), cards + Vector3.up * 0.9f);
            yield return new WaitForSeconds(0.5f);
            yield return ScreenshotCapture.Capture("day1_06_evening_mai_at_cards");
            var nb = Root.UI.Notebook;
            yield return PlayTestUtil.WaitUntil(() => nb.InStudy, 90f);
            yield return new WaitForSeconds(1f);
            yield return ScreenshotCapture.Capture("day1_07_self_study_trace");
            d.study.ForceComplete();
            yield return PlayTestUtil.WaitUntil(() => Root.Subtitles.CurrentLineId == "mai_cards_1", 30f);
            yield return new WaitForSeconds(1f);
            yield return ScreenshotCapture.Capture("day1_08_mai_asks_from_cards");
        }

        /// <summary>Quay mắt (không dời chỗ: đang ngồi ghế) về một điểm.</summary>
        static void LookAt(Vector3 point)
        {
            var d = point - Rig.cam.transform.position;
            Rig.look.SetAngles(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
        }
    }
}
