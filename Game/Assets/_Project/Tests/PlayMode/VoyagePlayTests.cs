using System.Collections;
using System.Collections.Generic;
using HaiBanTay.Core;
using HaiBanTay.Notebook;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Ship;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>M11: sáu ngày trên tàu chạy hết trong Sim; sóng phủ boong ngày bão; NPC của ngày; học buổi tối trong sổ;
    /// cảng Singapore và đám đông trên boong.</summary>
    public class VoyagePlayTests
    {
        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            var root = GameRoot.Instance;
            if (root != null)
            {
                root.Input.SimUse = false;
                root.Input.SimMove = Vector2.zero;
            }
            PlayTestUtil.ExitSim();
        }

        static readonly int[] Days = { 1, 2, 3, 4, 5, 6 };

        static IEnumerator LoadDay(GameRoot root, int day, System.Action<ShiftDirector> found)
        {
            ChapterStart.PrepareDay(root, day, day, false);
            var before = SceneManager.GetActiveScene();
            SceneManager.LoadScene(SceneFlow.GameScenes[1]);
            ShiftDirector d = null;
            yield return PlayTestUtil.WaitUntil(() => (d = Object.FindAnyObjectByType<ShiftDirector>()) != null && d.gameObject.scene != before
                                                      && d.Day != null, 30f);
            Assert.IsNotNull(d, "không nạp được cảnh 02");
            Assert.AreEqual(day, d.Day.Number);
            found(d);
        }

        /// <summary>Chờ chuyển cảnh của ngày trước xong hẳn (bản đồ chặng, nạp cảnh) để test sau không bị nạp chồng.</summary>
        static IEnumerator SettleTransition(GameRoot root, ShiftDirector old)
        {
            yield return PlayTestUtil.WaitUntil(() => !root.Scenes.IsLoading && (old == null || Object.FindAnyObjectByType<ShiftDirector>() != old), 60f);
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator EachDayRunsToItsEndInSim([ValueSource(nameof(Days))] int day)
        {
            var root = PlayTestUtil.Root();
            var fired = PlayTestUtil.Record(root.Events);
            PlayTestUtil.EnterSim(20f);
            ShiftDirector d = null;
            yield return LoadDay(root, day, x => d = x);
            var data = d.Day;
            yield return PlayTestUtil.WaitUntil(() => d == null || d.Finished, 150f);
            Assert.IsTrue(d != null && d.Finished, $"Ngày {day} dừng ở beat {(d != null ? d.Beat : "?")}");
            foreach (var e in ChapterStart.EventsOf(data))
                Assert.IsTrue(fired.Contains(e), $"Ngày {day}: chưa phát sự kiện {e}");
            foreach (var page in root.Content.Pages)
                if (ChapterStart.EventsOf(data).Contains(page.UnlockEvent))
                    Assert.IsTrue(root.Notebook.IsUnlocked(page.Id), $"Ngày {day}: trang {page.Id} chưa mở");
            foreach (var w in ChapterStart.WordsOf(root.Content, data))
                Assert.IsTrue(root.State.Knows(w), $"Ngày {day}: chưa học từ {w}");
            Assert.IsEmpty(root.State.PendingWords, "đồng hồ chạy theo việc, tan ca đúng 9 giờ nên không nợ từ");
            if (day < 6)
                Assert.AreEqual(day + 1, root.State.Day, "sang ngày sau");
            yield return SettleTransition(root, d);
            if (day == 6)
            {
                yield return PlayTestUtil.WaitUntil(() => SceneManager.GetActiveScene().name == SceneFlow.GameScenes[2], 60f);
                Assert.AreEqual(SceneFlow.GameScenes[2], SceneManager.GetActiveScene().name, "Ngày 6 xong thì tới Marseille");
            }
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator StormWaveOnSecondTripLosesBasketThenReplacementArrives()
        {
            var root = PlayTestUtil.Root();
            PlayTestUtil.EnterSim(20f);
            // Buộc xong nồi (hết lượt bão) thì tắt Sim ngay trong khung đó, để lượt kéo rổ chạy như người chơi thật.
            bool armed = true;
            System.Action<string> stopSim = id =>
            {
                if (armed && id == EventIds.S2SecureDone)
                {
                    armed = false;
                    Sim.Enabled = false;
                    Time.timeScale = 1f;
                    Time.captureDeltaTime = 1f / 60f;
                }
            };
            root.Events.AnyRaised += stopSim;
            ShiftDirector d = null;
            yield return LoadDay(root, 4, x => d = x);
            yield return PlayTestUtil.WaitUntil(() => d.Beat == "D4.haul", 120f);
            root.Events.AnyRaised -= stopSim;
            Assert.AreEqual("D4.haul", d.Beat);
            Assert.IsFalse(armed, "lượt bão đã xong");
            PlayTestUtil.ExitSim();
            Time.captureDeltaTime = 1f / 60f;
            var haul = d.haul;
            var rig = PlayerRig.Current;
            yield return PlayTestUtil.WaitUntil(() => haul.Pending > 0, 5f);
            Assert.IsTrue(haul.basket.gameObject.activeSelf, "rổ rau chờ ở hầm");
            Assert.IsTrue(haul.waveEnabled);

            // Buộc rổ, giữ chuột, đi trước rổ dọc dây sắt.
            rig.motor.Teleport(haul.Logic.PointAt(haul.Logic.Basket + 0.6f), 90f);
            haul.OnInteract(haul.basketTarget, rig.interactor);
            Assert.IsTrue(haul.Logic.Tied);
            root.Input.SimUse = true;
            yield return Follow(haul, rig, () => haul.Pending == 1);
            Assert.AreEqual(1, haul.DeliveredTrips, "chuyến thứ nhất về bếp bình thường");
            Assert.IsFalse(haul.WaveDone, "không đánh sóng ở chuyến thứ nhất");
            Assert.IsFalse(root.Events.HasFired(EventIds.S2StormWave));
            root.Input.SimUse = false;
            rig.motor.Teleport(haul.Logic.PointAt(0.6f), 90f);
            haul.OnInteract(haul.basketTarget, rig.interactor);
            var lost = haul.basket;
            root.Input.SimUse = true;
            yield return Follow(haul, rig, () => haul.WaveDone);
            Assert.IsTrue(haul.WaveDone, "sóng phải đánh khi rổ lên boong");
            // Im lặng thứ hai (DIRECTOR §5): sóng đánh sau 0,5 s im lặng ngoài Sim, sự kiện nổ cùng lúc người bị xô tới cột buồm.
            yield return PlayTestUtil.WaitUntil(() => root.Events.HasFired(EventIds.S2StormWave), 5f);
            Assert.IsTrue(root.Events.HasFired(EventIds.S2StormWave));
            yield return null;
            Assert.Less(Vector3.Distance(rig.Position, haul.mastSpot.position), 1.2f, "sóng cuốn tới cột buồm");
            Assert.Greater(root.UI.Tint.SplashLevel, 0.2f, "nước tóe phủ màn hình");
            Assert.IsFalse(rig.ControlEnabled, "bị sóng xô: tạm mất điều khiển");
            Assert.AreEqual(1, haul.LostBaskets, "rổ chuyến hai mất xuống biển");
            Assert.AreEqual(1, haul.Pending, "mất rổ chưa hoàn tất công việc");
            Assert.AreNotSame(lost, haul.basket, "rổ thay thế có danh tính khác");
            yield return PlayTestUtil.WaitUntil(() => rig.ControlEnabled, 10f);
            Assert.IsTrue(rig.ControlEnabled);
            Assert.AreEqual("D4.haul", d.Beat, "không có thất bại, vẫn lượt kéo rổ");

            Assert.IsTrue(lost == null, "rổ bị cuốn đã mất hẳn");
            // Lấy rổ khác từ hầm rồi kéo chuyến thứ hai về bếp.
            root.Input.SimUse = false;
            rig.motor.Teleport(haul.Logic.PointAt(0.6f), 90f);
            haul.OnInteract(haul.basketTarget, rig.interactor);
            Assert.IsTrue(haul.Logic.Tied);
            root.Input.SimUse = true;
            yield return Follow(haul, rig, () => haul.Pending == 0);
            root.Input.SimUse = false;
            Assert.AreEqual(0, haul.Pending, "rổ về tới bếp");
            Assert.AreEqual(2, haul.DeliveredTrips);
            Assert.AreEqual(1, haul.LostBaskets);
            yield return PlayTestUtil.WaitUntil(() => d.Beat != "D4.haul", 10f);
            Assert.IsTrue(root.Events.HasFired(EventIds.S2HaulDone));
        }

        /// <summary>Người chơi đi trước rổ một đoạn dây (dịch từng khung dọc đường) tới khi điều kiện đúng.</summary>
        internal static IEnumerator Follow(HaulStation haul, PlayerRig rig, System.Func<bool> until)
        {
            float s = haul.Logic.Basket + 0.6f;
            for (int frame = 0; frame < 6000 && !until(); frame++)
            {
                float basket = haul.Logic.Basket;
                s = Mathf.Min(haul.Logic.Length, Mathf.Min(Mathf.Max(s, basket + 0.6f) + 0.02f, basket + haul.Logic.Rope + 0.3f));
                var dir = haul.Logic.DirectionAt(s);
                rig.motor.Teleport(haul.Logic.PointAt(s), Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
                yield return null;
            }
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator SoldiersComeToSortGreensOnDayTwo()
        {
            var root = PlayTestUtil.Root();
            PlayTestUtil.EnterSim(20f);
            ShiftDirector d = null;
            yield return LoadDay(root, 2, x => d = x);
            var scenery = Object.FindAnyObjectByType<DayScenery>();
            Assert.IsTrue(scenery.Docked, "Ngày 2 tàu đỗ");
            Assert.IsTrue(GameObject.Find("Port_Singapore") != null, "thấy cảng Singapore");
            Assert.AreEqual(0f, d.ship.Intensity, 1e-3f, "tàu đỗ không lắc");
            yield return PlayTestUtil.WaitUntil(() => d.Beat == "D2.greens", 90f);
            yield return PlayTestUtil.WaitUntil(() => root.Events.HasFired(EventIds.S2SoldiersHelp), 10f);
            var cast = d.cast;
            Assert.IsTrue(cast.soldierA.gameObject.activeSelf && cast.soldierB.gameObject.activeSelf, "hai lính vào bếp");
            Assert.Less(Vector3.Distance(cast.soldierA.Position, cast.greensSpots[0].position), 0.3f, "lính đứng ở bàn rau");
            Assert.Less(Vector3.Distance(cast.soldierB.Position, cast.greensSpots[1].position), 0.3f, "lính thứ hai cũng ở bàn rau");
            foreach (var r in cast.soldierB.GetComponentsInChildren<Renderer>())
                Assert.IsTrue(r.enabled && r.gameObject.activeInHierarchy, "thấy được lính thứ hai: " + r.name);
            Debug.Log($"Greens: A {cast.soldierA.Position} B {cast.soldierB.Position} · renderers B " +
                cast.soldierB.GetComponentsInChildren<Renderer>().Length);
            Assert.IsTrue(root.Notebook.IsUnlocked("p_french_sailor"));
            yield return PlayTestUtil.WaitUntil(() => d.Beat != "D2.greens", 30f);
            yield return null;
            Assert.IsFalse(cast.soldierA.gameObject.activeSelf, "xong lượt nhặt rau thì lính về");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator DeckCrowdStandsOnTheDeckAndStaysInsideOnStormDay()
        {
            var root = PlayTestUtil.Root();
            ShiftDirector d = null;
            yield return LoadDay(root, 1, x => d = x);
            var crowd = GameObject.Find("DeckCrowd");
            Assert.IsNotNull(crowd, "Ngày 1 có người trên boong");
            var agents = crowd.GetComponentsInChildren<NpcAgent>(true);
            Assert.GreaterOrEqual(agents.Length, 5);
            foreach (var a in agents)
            {
                var p = a.transform.position;
                Assert.IsTrue(Physics.Raycast(p + Vector3.up * 1.0f, Vector3.down, out var hit, 1.6f, ~0, QueryTriggerInteraction.Ignore),
                    a.name + " không đứng trên sàn");
                Assert.AreEqual(0f, hit.point.y, 0.25f, a.name + " phải đứng trên boong giếng");
            }
            var cast = d.cast;
            yield return PlayTestUtil.WaitUntil(() => cast.soldierA.gameObject.activeSelf, 15f);
            Assert.IsTrue(cast.soldierA.gameObject.activeSelf, "Ngày 1 hai lính đứng ở boong giếng");
            Assert.IsFalse(cast.IsLitAsInterior(cast.soldierA), "đứng boong thì nhận ánh mặt trời");
            PlayTestUtil.EnterSim(20f);
            yield return LoadDay(root, 4, x => d = x);
            Assert.IsNull(GameObject.Find("DeckCrowd"), "ngày bão không ai đứng boong");
            Assert.IsNull(GameObject.Find("Port_Singapore"), "biển khơi, không có cảng");
        }

        static readonly object[] Evenings =
        {
            new object[] { 2, NotebookMode.Trace },
            new object[] { 3, NotebookMode.Match },
            new object[] { 5, NotebookMode.Fill },
            new object[] { 6, NotebookMode.Sentence },
        };

        [UnityTest, Timeout(240000)]
        public IEnumerator EveningStudyOpensInTheNotebook([ValueSource(nameof(Evenings))] object[] args)
        {
            int day = (int)args[0];
            var mode = (NotebookMode)args[1];
            var root = PlayTestUtil.Root();
            PlayTestUtil.EnterSim(20f);
            ShiftDirector d = null;
            yield return LoadDay(root, day, x => d = x);
            yield return PlayTestUtil.WaitUntil(() => d.Beat == $"D{day}.evening", 120f);
            if (day == 3)
            {
                yield return PlayTestUtil.WaitUntil(() => root.Events.HasFired(EventIds.S2CoffeeDone), 30f);
                Assert.IsTrue(root.Events.HasFired(EventIds.S2CoffeeDone), "mang cà phê trước học");
            }
            PlayTestUtil.ExitSim();
            Time.timeScale = 4f;
            var nb = root.UI.Notebook;
            yield return PlayTestUtil.WaitUntil(() => nb.InStudy && nb.Mode == mode, 60f);
            Assert.AreEqual(mode, nb.Mode, $"Ngày {day}: kiểu học");
            Assert.IsNotNull(d.StudyPlan);
            Assert.Greater(d.StudyPlan.MainCount, 0, "tối nào cũng học ít nhất một mục");
            Assert.IsEmpty(d.StudyPlan.Deferred, "tan ca đúng 9 giờ thì học đủ");
            if (day >= 2)
            {
                var teacher = day == 4 ? d.cast.jacques : d.cast.soldierA;
                Assert.IsTrue(teacher.gameObject.activeSelf, "người dạy ngồi ở góc đèn dầu");
                Debug.Log($"Evening D{day}: hông lệch gốc {d.cast.StoolOffset:0.00} m");
            }
            // Ngày 5, 6 có bài kể chuyện (quốc ngữ, bức thư) và lời thoại trước bài chính: 1000 khung không đủ tới lúc bài chính xong.
            for (int guard = 0; guard < 20000 && d.Beat != $"D{day}.rest"; guard++)
            {
                if (!nb.InStudy) { yield return null; continue; }
                switch (nb.Mode)
                {
                    case NotebookMode.Trace: nb.SimulateTraceLetter(); break;
                    case NotebookMode.Fill: nb.SimulateFill(); break;
                    case NotebookMode.Sentence: nb.SimulateSentence(); break;
                    case NotebookMode.Match:
                        for (int i = 0; i < nb.Match.Count; i++)
                            nb.Match.TryMatch(i, i);
                        nb.FinishMatch();
                        break;
                }
                yield return null;
            }
            Assert.IsFalse(nb.InStudy);
            yield return PlayTestUtil.WaitUntil(() => d.Beat == $"D{day}.rest", 60f);
            Assert.AreEqual($"D{day}.rest", d.Beat, "bài chính phải xong");
            Assert.IsTrue(root.Events.HasFired(EventIds.S2WordsDone));
            foreach (var w in ChapterStart.WordsOf(root.Content, d.Day))
                Assert.IsTrue(root.State.Knows(w), "đã học " + w + "; kế hoạch=" + string.Join(",", d.StudyPlan.Taught()) + "; hoãn=" + string.Join(",", d.StudyPlan.Deferred) + "; ngân sách=" + d.StudyPlan.Budget + "; nợ=" + string.Join(",", root.State.PendingWords));
            if (day == 6)
                Assert.IsTrue(root.Events.HasFired(EventIds.S2BookLent), "lính cho mượn sách");
        }
    }
}
