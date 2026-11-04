using System.Collections;
using System.Linq;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Rendering.Universal;

namespace HaiBanTay.Tests
{
    /// <summary>Ảnh story11b: đạo cụ, thao tác và người trên tàu qua camera/UI gameplay.</summary>
    [Category("Screenshots"), Explicit]
    public sealed class VoyageStoryShots
    {
        static GameRoot Root => GameRoot.Instance;
        static PlayerRig Rig => PlayerRig.Current;
        static ShiftDirector Director => Object.FindAnyObjectByType<ShiftDirector>();
        [TearDown] public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            if (Root != null) { Root.Input.SimUse = false; Root.Input.SimLook = Vector2.zero; Root.Subtitles.StopAll(); }
            ScreenshotCapture.RestoreUi(); PlayTestUtil.ExitSim();
        }
        static IEnumerator Capture(string name, bool keepSubtitles = false)
        {
            Root.UI.Shift.Clear(); Root.UI.Prompt.HideHint(); Root.UI.Toast.Hide();
            if (!keepSubtitles) Root.Subtitles.StopAll();
            Root.UI.Card.Hide(); Root.UI.SetFade(0f);
            yield return ScreenshotCapture.Capture(name);
        }
        static IEnumerator Shot(string id, Vector3 feet, Vector3 point)
        {
            ChoreTestUtil.Look(feet, point);
            yield return new WaitForSeconds(0.4f);
            yield return Capture("02story_" + id);
        }
        static IEnumerator Framed(string name, Vector3 eye, Vector3 point)
        {
            var go = new GameObject("StoryDetailCamera"); var cam = go.AddComponent<Camera>();
            cam.CopyFrom(Rig.cam); cam.enabled = false;
            var source = Rig.cam.GetUniversalAdditionalCameraData();
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = source.renderPostProcessing;
            data.volumeLayerMask = source.volumeLayerMask; data.volumeTrigger = Rig.cam.transform;
            go.transform.position = eye; go.transform.LookAt(point);
            yield return ScreenshotCapture.CaptureCamera(cam, "02story_" + name);
            Object.Destroy(go);
        }
        /// <summary>Giữ tia nhìn vào thớt: thao tác chỉ khóa chuột khi đang nhìn trúng; nếu một khung bị nhả (người đi ngang)
        /// thì SimLook kéo mắt trôi mất, nên mỗi khung chưa khóa thì ngắm lại.</summary>
        static IEnumerator Aimed(PeelStation task, System.Func<bool> until, float seconds)
        {
            for (float t = 0f; t < seconds && !until(); t += Time.deltaTime)
            {
                if (!Rig.look.Locked) ChoreTestUtil.Look(task.stand, task.board.transform.position);
                yield return null;
            }
        }
        [UnityTest, Timeout(240000)]
        public IEnumerator WaterMeatFishIceAndFrenchCooks()
        {
            yield return ChoreTestUtil.Galley(); var d = Director;
            foreach (var kind in new[] { OrderKinds.Water, OrderKinds.Meat, OrderKinds.Fish, OrderKinds.Ice })
            {
                var task = d.storyStations.OfType<VoyageDelivery>().First(x => x.Kind == kind);
                task.OnOrder(1);
                yield return Shot(kind + "_source", task.pickupStand, task.load.transform.position + Vector3.up * 0.25f);
                task.load.Interact(Rig.interactor);
                yield return Shot(kind + "_held", new Vector3(-1.5f, 0f, 0.8f), new Vector3(-2.8f, 1f, 2.3f));
                Assert.AreSame(task.load, Rig.interactor.Holding);
                if (kind == OrderKinds.Meat) Assert.IsTrue(task.load.ShoulderCarried);
                task.OnInteract(task.destination, Rig.interactor);
                if (kind == OrderKinds.Water)
                {
                    ChoreTestUtil.Look(task.deliveryStand, task.destination.transform.position);
                    yield return new WaitForSeconds(0.3f);
                    Root.Input.SimUse = true;
                    yield return PlayTestUtil.WaitUntil(() => task.Progress > 0.4f, 5f);
                    Assert.Greater(task.Progress, 0.4f);
                    yield return Capture("02story_water_pouring");
                    yield return PlayTestUtil.WaitUntil(() => task.Pending == 0, 5f);
                    Root.Input.SimUse = false;
                }
                Assert.AreEqual(0, task.Pending);
                task.load.gameObject.SetActive(false);
            }
            d.people.Phase(new PhaseData { Orders = { new OrderData { Task = OrderKinds.Serve, Count = 1 } } });
            var serve = d.storyStations.OfType<VoyageDelivery>().First(x => x.Kind == OrderKinds.Serve); serve.OnOrder(1);
            serve.load.Interact(Rig.interactor); serve.OnInteract(serve.destination, Rig.interactor);
            Assert.IsTrue(serve.servedTable.activeInHierarchy);
            yield return Shot("serve_cooks_seated_ba_standing", serve.deliveryStand, new Vector3(2.6f, 1.1f, -2.5f));
            Assert.AreEqual(0, serve.Pending);
            Assert.IsFalse(Rig.GetComponent<SitController>().StudySeated);
        }
        [UnityTest, Timeout(240000)]
        public IEnumerator PeelingLessonAndAsparagusCorrection()
        {
            yield return ChoreTestUtil.Galley(); var d = Director;
            foreach (var task in d.storyStations.OfType<PeelStation>())
            {
                task.OnOrder(task.asparagus ? 1 : 2);
                ChoreTestUtil.Look(task.stand, task.board.transform.position);
                yield return new WaitForSeconds(0.3f);
                task.OnInteract(task.board, Rig.interactor);
                if (!task.asparagus)
                {
                    yield return PlayTestUtil.WaitUntil(() => task.ShowingDemo, 40f);
                    Assert.IsTrue(task.ShowingDemo);
                    yield return Shot("mai_demonstrates", task.stand, (task.board.transform.position + task.maiSpot.position + Vector3.up * 1.4f) * 0.5f);
                    ChoreTestUtil.Look(task.stand, task.board.transform.position);
                }
                yield return PlayTestUtil.WaitUntil(() => task.Ready, 40f);
                Assert.IsTrue(task.Ready);
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (!task.Ready) task.OnInteract(task.board, Rig.interactor);
                    // Xong một củ sẽ mở khóa camera; ngắm lại bàn trước khi rê dao trên củ tiếp theo.
                    ChoreTestUtil.Look(task.stand, task.board.transform.position);
                    yield return new WaitForSeconds(0.3f);
                    Assert.IsTrue(Rig.interactor.IsLookingAt(task.board), "Phải ngắm trúng bàn trước khi gọt.");
                    Root.Input.SimUse = true; Root.Input.SimLook = new Vector2(0f, 15f);
                    yield return Aimed(task, () => task.Progress > 0.4f, 5f);
                    Assert.Greater(task.Progress, 0.4f);
                    if (task.asparagus && attempt == 1) Assert.IsTrue(task.CorrectMethod);
                    yield return Capture(task.asparagus ? task.CorrectMethod ? "02story_asparagus_correct_base" : "02story_asparagus_old_method" : attempt == 0 ? "02story_potato_peel_ribbon" : "02story_turnip_peel_ribbon");
                    int pending = task.Pending;
                    yield return Aimed(task, () => task.Pending < pending || task.Demonstrating, 5f);
                    Root.Input.SimUse = false; Root.Input.SimLook = Vector2.zero;
                    if (task.Demonstrating)
                    {
                        // Chọn chỗ xem trước khi ông Mai tới lan can; LookLocked của thao tác phải được nhả.
                        // Đứng lệch khỏi lối đi của ông Mai (nón chắn 0,8 m), nếu không ông chờ mãi.
                        ChoreTestUtil.Look(new Vector3(-4.4f, 0f, 3.5f), new Vector3(-5.9f, 1.05f, 5.5f));
                        yield return PlayTestUtil.WaitUntil(() => d.cast.mai.Position.x < -5.1f && d.cast.mai.Position.z > 5.9f && !task.Discarded, 40f);
                        Assert.Less(d.cast.mai.Position.x, -5.1f);
                        Assert.Greater(d.cast.mai.Position.z, 5.9f, "Ông Mai phải tới lan can trước khi bỏ măng.");
                        Assert.IsTrue(Physics.Raycast(d.cast.mai.Position + Vector3.up * 0.5f, Vector3.down, out var deck, 1.5f, ~0, QueryTriggerInteraction.Ignore), "Ông Mai phải đứng trên boong, không lơ lửng ngoài mạn.");
                        Assert.AreEqual(0.5f, deck.distance, 0.15f, "Chân ông Mai phải chạm mặt boong.");
                        Assert.IsFalse(task.Discarded, "Phải chụp lúc đang bỏ măng, không phải lúc ông Mai đã quay về.");
                        yield return Framed("mai_discards_stripped_asparagus", new Vector3(-4.3f, 1.5f, 4.5f), d.cast.mai.Position + Vector3.up * .9f);
                        ChoreTestUtil.Look(task.stand, task.board.transform.position);
                        yield return PlayTestUtil.WaitUntil(() => task.Ready && !task.Demonstrating, 40f);
                        Assert.IsTrue(task.Discarded); Assert.IsTrue(task.CorrectMethod);
                    }
                }
                Assert.AreEqual(0, task.Pending);
            }
        }
        [UnityTest, Timeout(240000)]
        public IEnumerator SoupCoffeeFirstClassSeasickAndCards()
        {
            yield return ChoreTestUtil.Galley(); var d = Director;
            d.people.crewMeal.SetActive(true);
            var soup = d.storyStations.OfType<VoyageEncounter>().First(x => x.Kind == OrderKinds.Soup); soup.OnOrder(1);
            yield return Shot("bread_butter_soup_fork", soup.stand, soup.target.transform.position);
            soup.OnInteract(soup.target, Rig.interactor);
            yield return PlayTestUtil.WaitUntil(() => soup.Stage == 1 && !soup.Busy, 20f);
            Assert.IsTrue(soup.spoon.activeSelf); Assert.IsFalse(soup.fork.activeSelf);
            yield return Capture("02story_soup_spoon_guidance");
            soup.OnInteract(soup.target, Rig.interactor);
            yield return PlayTestUtil.WaitUntil(() => soup.Pending == 0, 5f); Assert.AreEqual(0, soup.Pending);
            d.people.crewMeal.SetActive(false); d.people.firstClass.SetActive(true);
            var chieu = d.storyStations.OfType<VoyageEncounter>().First(x => x.Kind == OrderKinds.Chieu); chieu.OnOrder(1);
            yield return Shot("chieu_family_first_class", new Vector3(-4.65f, 2.85f, 5.45f), new Vector3(-2.6f, 4.15f, 4.8f));
            ChoreTestUtil.Look(chieu.stand, chieu.target.transform.position);
            yield return new WaitForSeconds(0.3f);
            chieu.OnInteract(chieu.target, Rig.interactor);
            yield return new WaitForSeconds(0.4f);
            yield return Capture("02story_chieu_exact_question", keepSubtitles: true);
            yield return PlayTestUtil.WaitUntil(() => chieu.Pending == 0, 40f); Assert.AreEqual(2, chieu.Stage);
            d.people.firstClass.SetActive(false); d.people.seasick.SetActive(true);
            yield return Shot("seasick_shipmates", new Vector3(-16f, 0f, 4.6f), new Vector3(-15f, 0.8f, 5.8f));
            d.people.seasick.SetActive(false); d.cast.PrepareCoffee();
            var coffee = d.storyStations.OfType<VoyageDelivery>().First(x => x.Kind == OrderKinds.Coffee); coffee.Activate(); coffee.OnOrder(1);
            coffee.load.Interact(Rig.interactor);
            yield return Shot("secret_coffee_to_soldiers", coffee.deliveryStand, new Vector3(-7.25f, 1.1f, 1.7f));
            coffee.OnInteract(coffee.destination, Rig.interactor); Assert.AreEqual(0, coffee.Pending);
            d.people.Evening(5);
            foreach (var go in d.eveningOnly) if (go != null) go.SetActive(true);
            d.lights.SetInterior(d.coal.fireLight, d.eveningLamp);
            yield return Shot("cards_by_evening_lamp", new Vector3(2.95f, 0f, -0.35f), new Vector3(2.2f, 0.95f, 0.2f));
            d.Clock.Minutes = 23 * 60; d.wallClock.Set(d.Clock.Minutes);
            Root.UI.Card.Hide(); Root.UI.SetFade(0f);
            ChoreTestUtil.Look(new Vector3(-2.75f, 0f, 0.1f), d.study.studyLookAt.position);
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(Root.UI.Card.Visible);
            Assert.IsTrue(d.eveningLamp.enabled); Assert.Greater(d.eveningLamp.intensity, 0f);
            yield return Capture("02story_study_late_2300");
        }
        [UnityTest, Timeout(240000)]
        public IEnumerator StormSecondBasketLostAndReplacement()
        {
            // Follow giới hạn theo frame: batchmode cần bước thời gian cố định để kéo đủ quãng đường thật.
            Time.captureDeltaTime = 1f / 60f;
            PlayTestUtil.ExitSim(); var root = PlayTestUtil.Root(); root.ResetProgress();
            ChapterStart.PrepareDay(root, 4, 4, false);
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneFlow.GameScenes[1]);
            yield return PlayTestUtil.WaitUntil(() => Director != null && Director.Day != null && Director.Day.Number == 4 && Director.Beat == "D4.sweep", 40f);
            var d = Director; Assert.AreEqual("D4.sweep", d.Beat);
            d.StopAllCoroutines(); d.Clock.Running = false; Root.Subtitles.StopAll();
            d.storm.StartStorm(); d.people.seasick.SetActive(true);
            var haul = d.haul; haul.waveEnabled = true; haul.OnOrder(2);
            Rig.motor.Teleport(haul.Logic.PointAt(0.6f), 90f); haul.OnInteract(haul.basketTarget, Rig.interactor);
            Root.Input.SimUse = true;
            yield return VoyagePlayTests.Follow(haul, Rig, () => haul.Logic.Basket >= haul.Logic.Length - 0.8f);
            Root.Input.SimUse = false;
            yield return Framed("storm_first_trip_at_galley", new Vector3(-1.4f, 1.5f, 0.2f), haul.BasketPosition + Vector3.up * 0.2f);
            Root.Input.SimUse = true;
            yield return VoyagePlayTests.Follow(haul, Rig, () => haul.Pending == 1);
            Root.Input.SimUse = false;
            Assert.AreEqual(1, haul.DeliveredTrips); Assert.IsFalse(haul.WaveDone);
            Rig.motor.Teleport(haul.Logic.PointAt(0.6f), 90f); haul.OnInteract(haul.basketTarget, Rig.interactor);
            var lost = haul.basket; Root.Input.SimUse = true;
            yield return VoyagePlayTests.Follow(haul, Rig, () => haul.WaveDone);
            Root.Input.SimUse = false;
            Assert.AreEqual(1, haul.LostBaskets); Assert.AreEqual(1, haul.Pending);
            yield return PlayTestUtil.WaitUntil(() => Root.UI.Tint.SplashLevel < 0.22f && !Rig.ControlEnabled, 5f);
            Assert.Less(Root.UI.Tint.SplashLevel, 0.22f);
            Assert.IsFalse(Rig.ControlEnabled, "Chụp lúc còn bám cột buồm, sau khi nước tóe đã tan.");
            // Camera game bị khóa trong beat sóng; góc chứng cứ thấy rõ cột và xích, không mang lớp UI nước tóe.
            yield return Framed("storm_second_wave_mast_chain", new Vector3(-20f, 1.45f, -3.2f), new Vector3(-21.5f, .9f, -1.6f));
            yield return PlayTestUtil.WaitUntil(() => Rig.ControlEnabled, 10f);
            Assert.IsTrue(lost == null); Assert.AreNotSame(lost, haul.basket);
            yield return Shot("storm_replacement_basket", haul.Logic.PointAt(0.8f), haul.BasketPosition + Vector3.up * 0.2f);
            haul.OnInteract(haul.basketTarget, Rig.interactor); Root.Input.SimUse = true;
            yield return VoyagePlayTests.Follow(haul, Rig, () => haul.Pending == 0);
            Root.Input.SimUse = false;
            Assert.AreEqual(2, haul.DeliveredTrips); Assert.AreEqual(1, haul.LostBaskets);
        }
        [UnityTest, Timeout(240000)]
        public IEnumerator QuocNguAndLetterInDiningRoom()
        {
            yield return ChoreTestUtil.Galley(); var d = Director;
            foreach (var go in d.eveningOnly) if (go != null) go.SetActive(true);
            d.lights.SetInterior(d.coal.fireLight, d.eveningLamp);
            d.cast.Begin(Root.Content.GetDay(5)); d.cast.BeginEvening(); d.people.Evening(5);
            foreach (var id in new[] { "l_quoc_ngu", "l_letter" })
            {
                var lesson = Root.Content.GetLesson(id); var plan = new StudyPlanner.Plan { Kind = lesson.Kind, Heading = lesson.Heading };
                plan.Items.AddRange(lesson.Items);
                if (id == "l_letter") { d.people.Evening(6); d.study.chair = d.letterChair; d.study.studyLookAt = d.letterLook; }
                d.study.Begin(plan); d.study.Activate();
                yield return PlayTestUtil.WaitUntil(() => Root.UI.Notebook.InStudy, 10f);
                Assert.IsTrue(Rig.GetComponent<SitController>().StudySeated);
                yield return Capture(id == "l_letter" ? "02story_letter_ordered_phrases" : "02story_ba_teaches_accents");
                d.study.ForceComplete(); yield return new WaitForSeconds(0.4f);
                if (id == "l_letter") { yield return Shot("letter_friend_dining_room", new Vector3(-2.4f, 0f, -5.1f), new Vector3(-4.3f, 0.8f, -4.3f));
                    yield return Framed("letter_friend_seat_side", new Vector3(-3.7f, 1.05f, -5.6f), new Vector3(-4.3f, 0.65f, -4.3f)); }
            }
        }
    }
}
