using System.Collections;
using System.Collections.Generic;
using HaiBanTay.Core;
using HaiBanTay.Ending;
using HaiBanTay.Narrative;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    public class EndingTests
    {
        [TearDown]
        public void TearDown()
        {
            PlayTestUtil.ExitSim();
            Time.captureDeltaTime = 0f;
        }

        static readonly string[] EndingOrder =
        {
            EventIds.S3Dawn, EventIds.S3FinalCleanDone, EventIds.S3PayQueueEntered, EventIds.S3PayCalled,
            EventIds.S3PayReceived, EventIds.S3CrewFarewell, EventIds.S3QuayStep, EventIds.S3QuayObserved,
            EventIds.S3TramPass, EventIds.S3CafeEntered, EventIds.S3CafeServed, EventIds.S3PagesLinked, EventIds.S3End
        };

        static void AssertOrder(List<string> fired, string[] order)
        {
            int last = -1;
            foreach (var id in order)
            {
                int i = fired.IndexOf(id);
                Assert.GreaterOrEqual(i, 0, "chưa phát sự kiện " + id);
                Assert.Greater(i, last, "sai thứ tự sự kiện " + id);
                last = i;
            }
        }

        [UnityTest]
        public IEnumerator EndingRunsEveryBeatAtNormalSpeed()
        {
            var root = PlayTestUtil.Root();
            root.ResetProgress();
            var fired = PlayTestUtil.Record(root.Events);
            PlayTestUtil.EnterSim(1f);
            Time.captureDeltaTime = 1f / 60f;
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[2]);
            yield return PlayTestUtil.WaitUntil(() => root.Events.HasFired(EventIds.S3End), 240f);
            Assert.IsTrue(root.Events.HasFired(EventIds.S3End), "Sim ×1 dừng ở " + SceneDirector.Current?.Beat);
            AssertOrder(fired, EndingOrder);
            foreach (var id in EndingOrder) Assert.AreEqual(1, fired.FindAll(x => x == id).Count, id);
            var director = Object.FindAnyObjectByType<EndingDirector>();
            Assert.AreEqual(6, director.Farewells);
            Assert.AreEqual(1, director.waiter.Servings);
            Assert.IsTrue(root.Notebook.IsUnlocked("p_mrs_last_shift"));
            Assert.IsTrue(root.Notebook.IsUnlocked("p_mrs_quay"));
        }

        [UnityTest]
        public IEnumerator EndingRunsE1ToE6AtTwentyTimes()
        {
            var root = PlayTestUtil.Root();
            root.ResetProgress();
            var fired = PlayTestUtil.Record(root.Events);
            PlayTestUtil.EnterSim(20f);
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[2]);
            float start = Time.realtimeSinceStartup;
            yield return PlayTestUtil.WaitUntil(() => root.Events.HasFired(EventIds.S3End), 90f);
            var director = Object.FindAnyObjectByType<EndingDirector>();
            Assert.IsTrue(root.Events.HasFired(EventIds.S3End), $"ending dừng ở beat {(director != null ? director.Beat : "?")}");
            AssertOrder(fired, EndingOrder);
            Assert.IsTrue(root.Notebook.IsUnlocked("p_monsieur"));
            Assert.IsTrue(root.Notebook.IsUnlocked("p_pay"));
            Debug.Log($"Ending ×20 xong sau {Time.realtimeSinceStartup - start:0.0} s thật");
        }

        [UnityTest, Timeout(1200000)]
        public IEnumerator FullChainBootToEndInSim()
        {
            var root = PlayTestUtil.Root();
            var fired = PlayTestUtil.Record(root.Events);
            Time.captureDeltaTime = 0f;
            float start = Time.realtimeSinceStartup;
            SceneManager.LoadScene(SceneFlow.BootScene);
            yield return null;
            PlayTestUtil.EnterSim(20f);
            // Thao tác Sim ×20 vẫn chờ được fade đầu cảnh (thời gian thật), tránh gửi lệnh chuyển cảnh quá sớm.
            Sim.StepSeconds = (SceneFlow.FadeSeconds + 0.1f) * 20f;
            yield return PlayTestUtil.WaitUntil(() => SceneManager.GetActiveScene().name == SceneFlow.GameScenes[1], 90f);
            Assert.AreEqual(SceneFlow.GameScenes[1], SceneManager.GetActiveScene().name,
                $"không tới được cảnh 02: beat={SceneDirector.Current?.Beat}, loading={root.Scenes.IsLoading}, Sim={Sim.Enabled}, scale={Time.timeScale}");
            float galley = Time.realtimeSinceStartup;
            // Sáu ngày trên tàu (M11), mỗi ngày có chuyển cảnh bản đồ chặng.
            yield return PlayTestUtil.WaitUntil(() => SceneManager.GetActiveScene().name == SceneFlow.GameScenes[2], 720f);
            Assert.AreEqual(SceneFlow.GameScenes[2], SceneManager.GetActiveScene().name, "không tới được cảnh 03");
            float marseille = Time.realtimeSinceStartup;
            yield return PlayTestUtil.WaitUntil(() => root.Events.HasFired(EventIds.S3End), 120f);
            Assert.IsTrue(root.Events.HasFired(EventIds.S3End), "không tới được cuối ending");
            Debug.Log($"FullChain: menu+01 {galley - start:0.0} s · các ngày trên tàu {marseille - galley:0.0} s · " +
                $"03 {Time.realtimeSinceStartup - marseille:0.0} s (thời gian thật, ×20)");

            AssertOrder(fired, new[]
            {
                EventIds.S1HandsRaised, EventIds.S1NotebookIntro, EventIds.S1Depart,
                // Tối ngày 1 Ba đã tự chép chữ (S16: tối nào cũng đọc, viết), trước khi tàu tới Singapore.
                // Trang sổ "Chảo đồng, nồi cao" mở ở lần cọ nồi cao đầu tiên: ngày 1, lượt tallpot sau bữa trưa (sau Mai ghé ở lượt dishes).
                EventIds.S2CoalDone, EventIds.S2SackDone, EventIds.S2MaiTalk, EventIds.S2ScrubDone, EventIds.S2WordsDone,
                EventIds.S2PortSingapore, EventIds.S2SoldiersHelp,
                EventIds.S2StormStart, EventIds.S2SecureDone, EventIds.S2StormWave, EventIds.S2HaulDone,
                EventIds.S2SailorHelp, EventIds.S2PileHigh, EventIds.S2BookLent,
                EventIds.S3Dawn, EventIds.S3PayReceived, EventIds.S3PagesLinked, EventIds.S3End
            });
            foreach (var page in root.Content.Notebook.Pages)
                Assert.IsTrue(root.Notebook.IsUnlocked(page.Id), "trang chưa mở khóa: " + page.Id);
        }
    }
}
