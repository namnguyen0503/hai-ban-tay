using System;
using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Ending;
using HaiBanTay.Interaction;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace HaiBanTay.Tests
{
    public class MarseilleNpcPlayTests
    {
        static GameRoot Root => GameRoot.Instance;
        static PlayerRig Rig => PlayerRig.Current;

        [TearDown]
        public void TearDown()
        {
            if (Root != null)
            {
                Root.Input.SimMove = Root.Input.SimLook = Vector2.zero;
                Root.Input.SimUse = Root.Input.SimRaise = false;
            }
            Time.captureDeltaTime = 0f;
            PlayTestUtil.ExitSim();
        }

        static IEnumerator Load(float scale = 20f)
        {
            PlayTestUtil.Root().ResetProgress();
            PlayTestUtil.EnterSim(scale);
            Time.captureDeltaTime = 1f / 60f;
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[2]);
        }

        public static IEnumerator Until(Func<bool> done, string why, float seconds = 60f)
        {
            yield return PlayTestUtil.WaitUntil(done, seconds);
            if (!done())
            {
                var d = UnityEngine.Object.FindAnyObjectByType<EndingDirector>();
                if (d != null && d.visitingWomen != null)
                    foreach (var w in d.visitingWomen)
                        TestContext.WriteLine($"VISITOR {w.name} at {w.Position} walking={w.Walking} waiting={w.Waiting} blockedBy={w.BlockedBy} active={w.gameObject.activeInHierarchy}");
                if (PlayerRig.Current != null) TestContext.WriteLine($"PLAYER at {PlayerRig.Current.Position}");
            }
            Assert.IsTrue(done(), why + "; beat=" + HaiBanTay.Narrative.SceneDirector.Current?.Beat);
        }

        [UnityTest]
        public IEnumerator ClerkServesTwoThenPlayerAndTenCoinsRemainUntilClear()
        {
            yield return Load();
            var d = UnityEngine.Object.FindAnyObjectByType<EndingDirector>();
            yield return Until(() => Root.Events.HasFired(EventIds.S3PayCalled), "quản lương chưa gọi Ba");
            Assert.IsTrue(d.purser.PlayerCalled);
            Assert.AreEqual(0, d.purser.queue.Count);
            Assert.AreEqual(2, d.purser.Handovers, "Ba phải chờ đủ hai lượt");
            foreach (var envelope in d.purser.envelopes)
            {
                Assert.IsTrue(envelope.gameObject.activeInHierarchy);
                Assert.AreNotEqual(d.purser.transform.parent, envelope.parent);
            }
            Assert.Greater(d.purser.envelopes[0].localScale.y, d.purser.envelopes[1].localScale.y);
            yield return Until(() => Root.Events.HasFired(EventIds.S3PayReceived), "chưa nhận lương");
            Assert.AreEqual(10, d.coins.Dropped, "event chỉ phát sau đủ mười xu, trước Clear");
            int visibleCoins = 0;
            var coinPoints = new List<Vector3>();
            Assert.IsTrue(d.payFill.gameObject.activeInHierarchy);
            Assert.AreEqual(LightShadows.None, d.payFill.shadows);
            foreach (var t in Rig.hands.GetComponentsInChildren<Transform>())
                if (t.name.StartsWith("Coin_", StringComparison.Ordinal))
                {
                    visibleCoins++;
                    foreach (var point in coinPoints)
                        Assert.Greater(Vector3.Distance(point, t.position), 0.028f, "mặt hai xu chồng nhau");
                    coinPoints.Add(t.position);
                    var screen = Rig.cam.WorldToViewportPoint(t.position);
                    Assert.Greater(screen.z, 0f);
                    Assert.That(screen.x, Is.InRange(0.05f, 0.95f));
                    Assert.Greater(screen.y, 0.11f * Root.UI.Letterbox.Amount + 0.012f,
                        "dải đen che xu " + t.name);
                }
            Assert.AreEqual(10, visibleCoins);
            Assert.AreEqual(3, d.purser.Handovers);
            yield return d.purser.ServeQueue();
            yield return d.purser.HandToPlayer();
            Assert.AreEqual(3, d.purser.Handovers, "gọi lại không được trả lần hai");
            yield return Until(() => d.Beat == "E2c", "khung tay không kết thúc");
            Assert.AreEqual(0, d.coins.Dropped);
            Assert.IsFalse(d.payFill.gameObject.activeInHierarchy);
            Assert.IsTrue(Rig.ControlEnabled);
            Assert.IsFalse(Rig.motor.MovementLocked);
        }

        [UnityTest]
        public IEnumerator WaiterPlacesCupBeforeEventAndNotebookCanReopen()
        {
            yield return Load();
            var d = UnityEngine.Object.FindAnyObjectByType<EndingDirector>();
            yield return Until(() => Root.Events.HasFired(EventIds.S3CafeServed), "bồi bàn không phục vụ", 120f);
            Assert.IsTrue(d.MaiSeated, "ông Mai phải ngồi cùng bàn trước khi bồi phục vụ");
            Assert.AreEqual(TrangPhuc.Thuong, Rig.body.Outfit);
            Assert.IsTrue(d.waiter.CupPlaced);
            var hint = Root.UI.Root.Q<Label>(className: "hbt-hint");
            Assert.IsNotNull(hint);
            Assert.AreEqual(DisplayStyle.None, hint.style.display.value, "gợi ý mang cà phê còn hiện sau phục vụ");
            Assert.Greater(d.cup.Find("Cup").GetComponent<Renderer>().bounds.size.y, 0.07f, "tách bị ép phẳng");
            Assert.Greater(Vector3.Dot(d.waiter.tray.up, Vector3.up), 0.99f, "khay nghiêng khỏi mặt phẳng ngang");
            var animator = d.waiter.carryHand.GetComponentInParent<Animator>();
            Assert.Greater(d.waiter.tray.position.y - d.waiter.agent.Position.y, 1.05f,
                "khay nằm quanh hông; Animator=" + animator.enabled + "/" + animator.cullingMode +
                "; Animated=" + d.waiter.agent.Animated + "; State=" + d.waiter.agent.State +
                "; hand=" + d.waiter.carryHand.position + "; eye=" + Rig.cam.transform.position +
                "; facing=" + Rig.cam.transform.forward);
            Assert.AreEqual(d.table, d.cup.parent);
            Assert.Less(Vector3.Distance(d.waiterTablePoint.position, d.cup.position), 0.002f);
            // Ba ngồi bằng clip SitChair: gốc người đặt SeatForward trước tâm ghế, theo hướng nhìn vào bàn.
            var toTable = d.table.position - d.seatPoint.position;
            var sitAt = d.seatPoint.position + Quaternion.Euler(0f, Mathf.Atan2(toTable.x, toTable.z) * Mathf.Rad2Deg, 0f) * Vector3.forward * HaiBanTay.Player.SitController.SeatForward;
            Assert.Less(new Vector2(Rig.Position.x - sitAt.x, Rig.Position.z - sitAt.z).magnitude,
                0.12f, "collider ghế đẩy Ba khỏi chỗ ngồi");
            Assert.Less(Mathf.Abs(Rig.Position.y - d.seatPoint.position.y), 0.12f, "Ba sai độ cao khi ngồi");
            Assert.IsTrue(Physics.Raycast(d.cup.position + Vector3.up * 0.3f, Vector3.down, out var hit,
                0.5f, ~0, QueryTriggerInteraction.Ignore), "không có mặt bàn dưới tách");
            Assert.AreEqual("COL_CafeTable_0", hit.collider.name);
            foreach (var renderer in d.cup.GetComponentsInChildren<Renderer>())
                if (renderer.enabled)
                    Assert.GreaterOrEqual(renderer.bounds.min.y, hit.point.y - 0.001f, "tách chìm vào mặt bàn");
            var cupScreen = Rig.cam.WorldToViewportPoint(d.cup.position + Vector3.up * 0.04f);
            Assert.Greater(cupScreen.z, 0f);
            Assert.That(cupScreen.x, Is.InRange(0.05f, 0.95f));
            Assert.That(cupScreen.y, Is.InRange(0.05f, 0.95f), "tách ngoài khung nhìn người đang ngồi");
            Assert.AreEqual(1, d.waiter.Servings);
            yield return d.waiter.Serve();
            Assert.AreEqual(1, d.waiter.Servings);
            Sim.Enabled = false;
            Time.timeScale = 1f;
            yield return Until(() => d.Beat == "E5c" && Root.UI.Notebook.IsOpen, "sổ nối trang chưa mở");
            Root.UI.Notebook.Close();
            yield return null;
            yield return null;
            Assert.IsTrue(Root.UI.Notebook.IsOpen, "đóng sổ lúc ngồi không được gây khóa mềm");
            var link = Root.Content.Notebook.LinkTarget;
            Root.UI.Notebook.SimulateLink(link.A, link.B);
            Assert.IsTrue(d.Link.Linked);
        }

        [UnityTest]
        public IEnumerator WaiterYieldsWhenPlayerOccupiesServingPoint()
        {
            yield return Load(1f);
            Sim.Enabled = false;
            var d = UnityEngine.Object.FindAnyObjectByType<EndingDirector>();
            Rig.motor.Teleport(d.waiter.servePoint.position, 0f); // isolated obstruction fixture
            bool done = false;
            IEnumerator Serve()
            {
                yield return d.waiter.Serve();
                done = true;
            }
            d.StartCoroutine(Serve());
            yield return Until(() => done, "người chơi đứng ở đích bồi bàn gây khóa", 30f);
            Assert.IsTrue(d.waiter.CupPlaced);
            Assert.Less(Vector3.Distance(d.cup.position, d.waiterTablePoint.position), 0.002f);
            Assert.AreEqual(1, d.waiter.Servings);
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator FinalMealPorterWalksInsideTheGalley()
        {
            // Bot cả game 15/10: MealPots viết tuyến bưng nồi theo tọa độ thế giới của cảnh bếp (khối bếp ở gốc); ở Marseille tàu
            // cập chỗ khác và quay ngược nên phụ bếp đi ra ngoài tàu, kẹt ở (4,0; 1,2) rồi bị đặt thẳng vào chỗ.
            yield return Load(1f);
            Sim.Enabled = false;
            var d = UnityEngine.Object.FindAnyObjectByType<EndingDirector>();
            var meals = d.finalMeals;
            Assert.IsNotNull(meals);
            yield return Until(() => meals.CookingPots.Count > 0, "nồi cuối chưa lên bếp");
            d.StopAllCoroutines();
            var porter = meals.porter;
            var home = porter.Position;
            var galley = meals.transform;
            float outside = 0f, far = 0f;
            bool done = false;
            IEnumerator Serve()
            {
                yield return meals.Serve();
                done = true;
            }
            meals.StartCoroutine(Serve());
            for (float t = 0f; t < 90f && !(done && !porter.Walking && Vector3.Distance(porter.Position, home) < 0.6f); t += Time.deltaTime)
            {
                // Trong khối bếp (ShipInteriorLayout.GalleyRegion: x −5,05…7,0, z −5,65…4,33 theo hệ khối bếp).
                var local = galley.InverseTransformPoint(porter.Position);
                float outX = Mathf.Max(0f, -5.05f - local.x, local.x - 7.0f), outZ = Mathf.Max(0f, -5.65f - local.z, local.z - 4.33f);
                outside = Mathf.Max(outside, Mathf.Max(outX, outZ));
                far = Mathf.Max(far, Vector3.Distance(porter.Position, home));
                yield return null;
            }
            Debug.Log($"MARSEILLE_PORTER: xa chỗ đứng nhất {far:F1} m, ra ngoài khối bếp {outside:F2} m, quá hạn {meals.TimedOutWalks} đoạn, " +
                      $"nồi bẩn {meals.Pending}, đứng ở {galley.InverseTransformPoint(porter.Position)} (hệ khối bếp)");
            Assert.IsTrue(done, "dọn nồi cuối xong");
            Assert.AreEqual(0, meals.TimedOutWalks, "phụ bếp tự đi tới nồi, chồng nồi bẩn và về chỗ, không bị đặt thẳng vào chỗ");
            Assert.Less(outside, 0.05f, "phụ bếp đi trong khối bếp, không ra ngoài tàu");
            Assert.Greater(far, 3f, "phụ bếp thật sự đi bưng nồi (không đứng yên)");
            Assert.AreEqual(1, meals.Pending, "nồi vừa nấu nằm ở chồng nồi bẩn");
        }

        [UnityTest]
        public IEnumerator PlayerWalksE3ToCafeThroughRealTriggersAndInput()
        {
            yield return Load();
            var d = UnityEngine.Object.FindAnyObjectByType<EndingDirector>();
            yield return Until(() => d.Beat == "E3" && !d.gangwayBarrier.activeSelf, "dây chắn chưa mở", 120f);
            Sim.Enabled = false;
            Time.timeScale = 1f;
            // Sim is only setup for E1/E2. No teleport, Fire or direct Interact from here.
            yield return Walk(new Vector3(0f, 0f, -1.1f));
            yield return Walk(new Vector3(0f, 0f, -3.1f));
            yield return Walk(d.quayPoint.position);
            yield return Until(() => d.Beat == "E3b", "cầu thang không phát QuayStep");
            yield return Walk(new Vector3(-2.2f, -2.5f, -12.6f));
            yield return Until(() => d.Beat == "E4", "không quan sát được phu nghèo");
            yield return Walk(new Vector3(0f, -2.5f, -15f));
            yield return Until(() => d.tram.Done && d.Beat == "E4b", "tàu điện hoặc dây đường không mở");
            var stands = new[] { new Vector3(0f, -2.5f, -14.4f), new Vector3(-2.1f, -2.5f, -20.5f),
                new Vector3(-2f, -2.5f, -25.5f), new Vector3(-1.3f, -2.5f, -22f) };
            for (int i = 0; i < d.newDiscoveries.Length; i++)
            {
                int seen = i;
                yield return Walk(stands[i]);
                yield return Until(() => d.newDiscoveries[seen].available, "vật mới chưa sẵn sàng");
                yield return Press(d.newDiscoveries[i], () => d.NewThingsSeen > seen);
            }
            Assert.AreEqual(4, d.NewThingsSeen);
            yield return Until(() => d.chair.available, "ghế quán chưa mở sau quan sát");
            Assert.IsFalse(d.streetBarrier.activeSelf);
            yield return Walk(new Vector3(0.5f, -2.4f, -22.25f));
            yield return Press(d.chair, () => !Rig.ControlEnabled);
            yield return Until(() => Root.Events.HasFired(EventIds.S3CafeServed), "tới quán nhưng không được phục vụ");
            Assert.Less(Vector3.Distance(d.cup.position, d.waiterTablePoint.position), 0.002f);
            Assert.IsTrue(Root.Events.HasFired(EventIds.S3QuayObserved));
        }

        public static IEnumerator Walk(Vector3 goal)
        {
            float end = Time.realtimeSinceStartup + 45f;
            while ((new Vector2(Rig.Position.x - goal.x, Rig.Position.z - goal.z).magnitude > 0.22f ||
                    Mathf.Abs(Rig.Position.y - goal.y) > 0.65f) && Time.realtimeSinceStartup < end)
            {
                var delta = goal - Rig.Position;
                Rig.look.SetAngles(Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg, 0f);
                Root.Input.SimMove = new Vector2(0f, 1f);
                yield return null;
            }
            Root.Input.SimMove = Vector2.zero;
            Assert.Less(new Vector2(Rig.Position.x - goal.x, Rig.Position.z - goal.z).magnitude, 0.3f,
                "đi bộ mắc tại " + Rig.Position + ", đích " + goal);
            Assert.Less(Mathf.Abs(Rig.Position.y - goal.y), 0.65f, "sai mặt sàn tại " + goal);
        }

        public static void Aim(Vector3 point)
        {
            var delta = point - Rig.cam.transform.position;
            Rig.look.SetAngles(Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg,
                -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg);
        }

        public static IEnumerator Press(Component target, Func<bool> effect)
        {
            var collider = target.GetComponent<Collider>();
            Assert.IsNotNull(collider, target.name);
            Aim(collider.bounds.center);
            yield return Until(() => ReferenceEquals(Rig.interactor.Current, target), "không thấy prompt " + target.name, 5f);
            Root.Input.SimulateInteract();
            yield return Until(effect, "E không có tác dụng ở " + target.name, 10f);
        }
    }
}
