using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Narrative;
using HaiBanTay.Notebook;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Ship;
using HaiBanTay.UI;
using UnityEngine;

namespace HaiBanTay.Ending
{
    /// <summary>Cảnh 03: việc cuối ca, lĩnh lương, chia tay, cảng, tàu điện và quán. Chữ/timing từ JSON.</summary>
    public sealed class EndingDirector : SceneDirector
    {
        public static float TimeScaleForTests = 1f;
        const float MonsieurPause = 1.4f;
        public ShipMotion ship;
        public EngineHum engine;
        public FogLift fog;
        public CoinDrop coins;
        [Tooltip("Đèn fill tay E2 = PlayerRig.handsFill (builder gán); PlayerRig bật/tắt cùng khung hai bàn tay.")]
        public Light payFill;
        public SweepStation sweep;
        public WashStation wash;
        public MealPots finalMeals;
        public PayClerkRoutine purser;
        public MarseilleLedger ledger;
        public Transform payPoint;
        public NpcAgent[] crew = new NpcAgent[0];
        public InteractTarget[] farewellTargets = new InteractTarget[0];
        public NpcAgent captain;
        public NpcAgent sailor;
        /// <summary>Cai phu ở cầu cảng và người lính trẻ xin việc bị gạt (DIRECTOR §6); lính đi khỏi theo quaySoldierExit.</summary>
        public NpcAgent foreman, quaySoldier;
        public Transform quaySoldierExit;
        public GameObject deckCast;
        public GameObject gangwayBarrier;
        public GameObject streetBarrier;
        public BeatTrigger quayTrigger;
        public Transform quayPoint;
        public BeatTrigger observeTrigger;
        public BeatTrigger streetTrigger;
        public TramPass tram;
        public InteractTarget chair;
        public Transform seatPoint;
        [Tooltip("Clip ngồi ghế SitChair cho Ba ở quán cà phê.")]
        public AnimationClip chairClip;
        public WaiterRoutine waiter;
        public Transform waiterTablePoint;
        public Transform cup;
        public Transform table;
        public bool skipTramAndCafe;
        public NpcAgent[] visitingWomen;
        public Transform[] visitorPath;
        public Transform[] maiShorePath;
        public Transform maiCafeSeat;
        public GameObject borrowedClothes;
        [Tooltip("Quyển sách người lính trẻ cho Ba mượn ngày 6 (DIRECTOR §6): hiện ở tay anh lúc Ba trả, ẩn khi anh đưa lại.")]
        public GameObject soldierBook;
        public InteractTarget[] newDiscoveries;
        public int NewThingsSeen { get; private set; }
        public bool WomenAboard { get; private set; }
        public bool MaiSeated { get; private set; }

        /// <summary>
        /// Ông Mai (crew[3]) không chào từ biệt trên boong: ông cho Ba mượn áo và cùng xuống phố. Ba nói chuyện với ông sau khi chào
        /// hết những người ở lại; lời dặn của ông (FarewellLines[3]) nói lúc hai người vừa xuống cầu cảng.
        /// </summary>
        public const int MaiIndex = 3;

        bool _swept, _washed, _sat;
        int _talkTo = -1;
        public int Farewells { get; private set; }
        public PageLinkController Link { get; private set; }

        /// <summary>Mục tiêu hiện tại: dòng chỉ dẫn trên cùng màn hình, mốc chỉ chỗ (nếu có).</summary>
        void Objective(string key, System.Func<Transform> target = null) => Guide(key, target);

        Transform FirstAvailable(InteractTarget[] targets)
        {
            foreach (var t in targets)
                if (t != null && t.available)
                    return t.transform;
            return null;
        }
        bool Near(Transform point, float radius)
        {
            if (Rig == null || point == null)
                return false;
            var d = Rig.Position - point.position;
            return Mathf.Abs(d.y) < 1.2f && d.x * d.x + d.z * d.z < radius * radius;
        }

        IEnumerator Start()
        {
            if (!Mathf.Approximately(TimeScaleForTests, 1f))
                Time.timeScale = TimeScaleForTests;
            var data = Root.Content.Ending;
            // Người nói theo câu chào từ biệt của từng người (quản bếp, phụ bếp, Jacques, ông Mai, hai lính).
            for (int i = 0; i < crew.Length && i < data.FarewellLines.Count; i++)
                TalkGaze.BindLine(data.FarewellLines[i], crew[i]);
            TalkGaze.Bind("captain", captain);
            Rig.SetOutfit(TrangPhuc.PhuBep);
            if (finalMeals != null)
            {
                finalMeals.coal?.Activate();
                finalMeals.coal?.ashStation?.Activate();
                finalMeals.BeginMeal(data.FinalMeal);
            }
            SetGrime(0.8f, instant: true);
            Root.State.ReachedMarseille = true;
            SaveSystem.Write(SaveSystem.Capture(SceneFlow.GameScenes[2], Root.State, Root.Flags, Root.Notebook, Root.Events));
            if (ship != null)
                ship.Intensity = 0f;
            foreach (var npc in crew) npc.Play("IdleLook");
            captain.Play("IdleLook");
            sailor.Play("IdleLook");
            purser.agent.Play("Documents");
            waiter.agent.Play("IdleLook");
            chair.available = false;
            chair.Interacted += _ => { if (chair.available) _sat = true; };
            sweep.Worked += (_, units) => { _swept = true; sweep.Close(); };
            wash.Worked += (_, units) => { if (wash.Logic.Pending == 0 && !wash.Logic.InBasin) { _washed = true; wash.Close(); } };
            for (int i = 0; i < farewellTargets.Length; i++)
            {
                int index = i;
                farewellTargets[i].available = false;
                farewellTargets[i].Interacted += _ =>
                {
                    if (Beat == "E2c" && _talkTo < 0 && farewellTargets[index].available)
                    {
                        _talkTo = index;
                        farewellTargets[index].available = false;
                    }
                };
            }

            Beat = "E1a";
            Objective("mrs.objective.arrival");
            Root.Events.Raise(EventIds.S3Dawn);
            Root.Audio.StopMusic();
            Root.Audio.PlaySfx("SFX/ship_horn", 0.45f);
            Root.Audio.PlaySfx("SFX/whistle_bosun", 0.35f);
            if (engine != null)
                StartCoroutine(engine.FadeOut(data.Timing("engineFade", 1.5f)));
            yield return Root.Subtitles.PlayAndWait("mrs_arrival");
            float dawnStart = Time.time;
            if (fog != null)
                yield return fog.Run(data.Timing("fogLift", 12f));
            // Có thể ngắm cảng hoặc đi về cửa bếp; không khóa di chuyển trong bình minh.
            yield return WaitFor(() => Time.time - dawnStart >= data.Timing("dawn", 40f) || Near(sweep.hook.transform, 2.5f));

            Beat = "E1b";
            Objective("mrs.objective.sweep");
            sweep.Activate();
            sweep.OnOrder(1);
            GuideTask(sweep);
            yield return Root.Subtitles.PlayAndWait("mrs_last_clean");
            yield return WaitFor(() => _swept, () => sweep.SimulateWork());
            Objective("mrs.objective.wash");
            if (finalMeals != null) yield return finalMeals.Serve();
            else wash.AddDirty(1);
            // Chỉ mở thao tác rửa sau khi NPC đã mang nồi vừa nấu tới bồn.
            wash.Activate();
            GuideTask(wash);
            yield return WaitFor(() => _washed, () => wash.SimulateWork());
            SetGrime(0.8f, instant: true);
            Root.Events.Raise(EventIds.S3FinalCleanDone);

            Beat = "E2a";
            Objective("mrs.objective.queue", () => payPoint);
            ledger.available = true;
            yield return Root.Subtitles.PlayAndWait("mrs_pay_table");
            yield return WaitFor(() => Near(payPoint, 3.5f));
            Root.Events.Raise(EventIds.S3PayQueueEntered);
            yield return Root.Subtitles.PlayAndWait("mrs_pay_others");
            yield return purser.ServeQueue();
            Root.Events.Raise(EventIds.S3PayCalled);
            yield return Root.Subtitles.PlayAndWait("mrs_pay_called");
            Objective("mrs.objective.called", () => payPoint);
            yield return WaitFor(() => Near(payPoint, 1.5f));

            Beat = "E2b";
            Rig?.SetMovement(false);
            ledger.available = false;
            ClearGuide();
            yield return WaitForRaise(1.5f, "prompt.raise_hands", 0f);
            yield return PayScene(data);

            Beat = "E2c";
            Objective("mrs.objective.farewell", () => FirstAvailable(farewellTargets));
            for (int i = 0; i < farewellTargets.Length; i++)
                farewellTargets[i].available = i != MaiIndex;
            yield return captain.Do("Wave", 0.8f);
            yield return Root.Subtitles.PlayAndWait("mrs_captain");
            captain.Play("IdleLook");
            while (Farewells < farewellTargets.Length)
            {
                yield return WaitFor(() => _talkTo >= 0, () =>
                {
                    for (int i = 0; i < farewellTargets.Length; i++)
                        if (farewellTargets[i].available)
                        {
                            _talkTo = i;
                            farewellTargets[i].available = false;
                            break;
                        }
                });
                int next = _talkTo;
                var npc = crew[next];
                if (Rig != null)
                {
                    var dir = Rig.Position - npc.Position;
                    yield return npc.Face(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
                }
                if (next != MaiIndex)
                {
                    npc.Play("Wave");
                    yield return Root.Subtitles.PlayAndWait(data.FarewellLines[next]);
                    yield return Root.Subtitles.PlayAndWait(data.FarewellReplies[next]);
                    // Người lính trẻ cho mượn sách ở ngày 6 ("trả ở Marseille"): Ba trả, anh đẩy lại (DIRECTOR §6).
                    if (data.FarewellLines[next] == "mrs_soldier_a_bye")
                    {
                        if (soldierBook != null) soldierBook.SetActive(true);
                        npc.Play("HoldCarry");
                        yield return Root.Subtitles.PlayAndWait("mrs_ba_book");
                        npc.Play("HandOver");
                        yield return Root.Subtitles.PlayAndWait("mrs_soldier_keep");
                        if (soldierBook != null) soldierBook.SetActive(false);
                        npc.Play("IdleLook");
                    }
                }
                else
                {
                    Beat = "E2context";
                    BeginCutscene();
                    Rig.SetControl(false);
                    var toGangway = visitorPath[1].position + Vector3.up * .7f - Rig.cam.transform.position;
                    yield return Rig.MoveTo(Rig.Position, Mathf.Atan2(toGangway.x, toGangway.z) * Mathf.Rad2Deg,
                        -Mathf.Atan2(toGangway.y, new Vector2(toGangway.x, toGangway.z).magnitude) * Mathf.Rad2Deg, .6f);
                    // Bối cảnh kín đáo theo S16: váy kín, lời mời ngắn, không cử chỉ tình dục.
                    gangwayBarrier.SetActive(false);
                    yield return VisitorsBoard();
                    gangwayBarrier.SetActive(true);
                    var toMai = npc.Position + Vector3.up * 1.2f - Rig.cam.transform.position;
                    yield return Rig.MoveTo(Rig.Position, Mathf.Atan2(toMai.x, toMai.z) * Mathf.Rad2Deg, 0f, .6f);
                    yield return Root.Subtitles.PlayAndWait("mrs_mai_context");
                    yield return Root.Subtitles.PlayAndWait("mrs_ba_why1");
                    yield return Root.Subtitles.PlayAndWait("mrs_ba_why2");
                    Root.Events.Raise(EventIds.S3ContextSeen);
                    EndCutscene();
                    Rig.SetControl(true);
                    Beat = "E2c";
                }
                npc.Play("IdleLook");
                Farewells++;
                _talkTo = -1;
                int staying = farewellTargets.Length - 1;
                if (Farewells < staying)
                    GuideText(string.Format(S("mrs.objective.farewell_progress"), Farewells, staying), () => FirstAvailable(farewellTargets));
                else if (Farewells == staying)
                {
                    farewellTargets[MaiIndex].promptKey = "mrs.prompt.mai";
                    farewellTargets[MaiIndex].available = true;
                    Objective("mrs.objective.mai", () => FirstAvailable(farewellTargets));
                }
            }
            Root.Events.Raise(EventIds.S3CrewFarewell);

            Beat = "E2clothes";
            // Ông Mai đưa áo và nói thẳng với Ba (DIRECTOR §6, dựa S16: cho mượn áo quần, ra tiệm cà phê đường Canơbia).
            crew[MaiIndex].Play("HandOver");
            borrowedClothes.SetActive(true);
            yield return Root.Subtitles.PlayAndWait("mrs_mai_clothes");
            yield return Root.Subtitles.PlayAndWait("mrs_mai_clothes2");
            Rig.SetControl(false);
            yield return Root.Scenes.Fade(0f, 1f, 0.6f);
            Root.UI.Card.Blackout();
            Root.UI.SetFade(0f);
            yield return Root.UI.Card.Show(S("card.mrs.evening"), 3f);
            Rig.SetOutfit(TrangPhuc.Thuong);
            SetGrime(0f, instant: true);
            borrowedClothes.SetActive(false);
            Root.Events.Raise(EventIds.S3ClothesBorrowed);
            Root.UI.Card.Hide();
            Rig.SetControl(true);
            // Ông Mai tiếp tục đi cùng; DeckCast tắt sau khi Ba xuống cảng.
            crew[MaiIndex].transform.SetParent(maiCafeSeat.parent, true);

            Beat = "E3";
            Objective("mrs.objective.gangway", () => quayPoint);
            // Thủy thủ gác cầu thang chỉ ra hiệu, không nói: Jacques đã chào và chỉ cầu thang lúc từ biệt (câu mrs_sailor_open bỏ).
            yield return sailor.Do("HandOver", 0.8f);
            gangwayBarrier.SetActive(false);
            quayTrigger.GetComponent<Collider>().enabled = true;
            sailor.Play("IdleLook");
            StartCoroutine(MaiWalksToCafe());
            yield return WaitFor(() => quayTrigger.HasFired, () =>
            {
                Rig.motor.Teleport(quayPoint.position, quayPoint.eulerAngles.y);
                quayTrigger.Fire();
            });
            Root.Events.Raise(EventIds.S3QuayStep);
            deckCast.SetActive(false);
            Root.Audio.SetLowPass(Audio.AudioService.FullCutoff);
            // Vừa đặt chân lên đất Pháp: ông Mai đi trước dặn Ba (không phải lời từ biệt trên boong).
            yield return Root.Subtitles.PlayAndWait(data.FarewellLines[MaiIndex]);
            yield return Root.Subtitles.PlayAndWait(data.FarewellReplies[MaiIndex]);

            Beat = "E3b";
            Objective("mrs.objective.quay", () => observeTrigger.transform);
            observeTrigger.GetComponent<Collider>().enabled = true;
            yield return WaitFor(() => observeTrigger.HasFired, () => observeTrigger.Fire());
            yield return Root.Subtitles.PlayAndWait("mrs_docker");
            // Cảnh xin việc ở Nhà Rồng lật ngược: người bị gạt giờ là một người Pháp. Người lính không thấy Ba.
            if (foreman != null && quaySoldier != null)
            {
                quaySoldier.Play("Talk");
                yield return new WaitForSeconds(Sim.Enabled ? 0f : 1.2f);
                foreman.Play("Talk");
                yield return Root.Subtitles.PlayAndWait("mrs_foreman_no");
                foreman.Play("Idle");
                if (quaySoldierExit != null)
                    StartCoroutine(SoldierWalksOff());
                if (!Sim.Enabled) yield return new WaitForSeconds(1.5f);
            }
            yield return Root.Subtitles.PlayAndWait("mrs_ba_poor");
            Root.Events.Raise(EventIds.S3QuayObserved);

            Beat = "E4";
            Objective("mrs.objective.tram", () => streetTrigger.HasFired ? null : streetTrigger.transform);
            yield return WaitFor(() => streetTrigger.HasFired, () => streetTrigger.Fire());
            bool passed = false;
            tram.PassedWatcher += () => passed = true;
            StartCoroutine(tram.Run(Rig != null ? Rig.transform : null));
            yield return Root.Subtitles.PlayAndWait("mrs_ba_tram");
            yield return WaitFor(() => passed);
            Root.Events.Raise(EventIds.S3TramPass);
            yield return WaitFor(() => tram.Done);
            streetBarrier.SetActive(false);
            Beat = "E4b";
            Objective("mrs.objective.new", () => FirstAvailable(newDiscoveries));
            var discoveryLines = new[] { "mrs_ba_first", "mrs_ba_car", "mrs_ba_glass", "mrs_ba_paper" };
            for (int i = 0; i < newDiscoveries.Length; i++)
            {
                var target = newDiscoveries[i];
                bool seen = false;
                target.Interacted += _ => seen = true;
                target.available = true;
                yield return WaitFor(() => seen, () => seen = true);
                target.available = false;
                NewThingsSeen++;
                yield return Root.Subtitles.PlayAndWait(discoveryLines[i]);
            }

            Beat = "E5a";
            Objective("mrs.objective.cafe", () => chair.available ? chair.transform : null);
            chair.available = true;
            yield return Root.Subtitles.PlayAndWait("mrs_waiter_seat");
            yield return WaitFor(() => _sat, () => _sat = true);
            chair.available = false;
            Root.Events.Raise(EventIds.S3CafeEntered);
            yield return Cafe(data);

            Beat = "E6";
            ClearGuide();
            BeginCutscene();
            Root.Subtitles.StopAll();
            Root.Audio.PlayMusic("Music/ending", loop: true, volume: 0.75f);
            yield return Root.Scenes.Fade(0f, 1f, data.Timing("fadeToBlack", 2f));
            Root.UI.Card.Blackout();
            Root.UI.Letterbox.Set(0f);
            Root.UI.SetFade(0f);
            yield return EndCards.Play(Root);
            PauseController.QuitOnEscape = true;
            Root.Events.Raise(EventIds.S3End);
            Beat = "End";
        }

        IEnumerator VisitorsBoard()
        {
            for (int visitor = 0; visitor < visitingWomen.Length; visitor++)
            {
                var actor = visitingWomen[visitor];
                actor.gameObject.SetActive(true);
                actor.followSlope = true;
                // Điểm cuối của tuyến là chỗ thủy thủ đứng: đi tới đó thì chị vòng tránh anh, lấn ra mép boong (đo 22/10, chồng Edges).
                // Chị đi thẳng tới chỗ đứng bên trái anh, không dịch tức thời sau lời mời. Người sau đứng chéo sau người đầu (phía cầu thang):
                // x −0,5 ở hàng z −2,5 là chỗ hai phụ bếp đứng sau khi nhận lương (PayExitB), chị đứng chờ mãi (test E3 22/10).
                var sailorAt = visitorPath[visitorPath.Length - 1].position;
                var stand = sailorAt + (visitor == 0 ? new Vector3(-1.1f, 0f, 0f) : new Vector3(-0.9f, 0f, 0.75f));
                for (int i = 0; i < visitorPath.Length - 1; i++) yield return actor.WalkTo(visitorPath[i]);
                yield return actor.WalkTo(stand);
                actor.followSlope = false;
                var toSailor = sailorAt - stand;
                yield return actor.Face(Mathf.Atan2(toSailor.x, toSailor.z) * Mathf.Rad2Deg);
                // Chỉ người đầu nói lời mời; người sau đi theo, không lặp lại cùng câu.
                if (visitor == 0)
                {
                    actor.Play("Talk");
                    yield return Root.Subtitles.PlayAndWait("mrs_woman_invite");
                }
                else
                    yield return new WaitForSeconds(0.6f);
            }
            WomenAboard = true;
        }

        IEnumerator SoldierWalksOff()
        {
            quaySoldier.Play("Idle");
            yield return quaySoldier.WalkTo(quaySoldierExit.position);
            quaySoldier.SetBodyActive(false);
        }

        IEnumerator MaiWalksToCafe()
        {
            var mai = crew[3];
            mai.followSlope = true;
            foreach (var point in maiShorePath) yield return mai.WalkTo(point);
            mai.followSlope = false;
            yield return WaitFor(() => tram.Done);
            yield return mai.WalkTo(new Vector3(1.5f, -2.4f, -22f));
            yield return mai.WalkTo(maiCafeSeat);
            yield return mai.Face(maiCafeSeat.eulerAngles.y);
            mai.Play("SitChair");
            MaiSeated = true;
        }

        IEnumerator PayScene(Content.EndingData data)
        {
            var rig = Rig;
            if (rig != null)
            {
                var facing = purser.agent.Position - rig.Position;
                rig.look.SetAngles(Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg, rig.look.Pitch);
                yield return rig.EnterHandsShot(Root.Config.handsShot);
            }
            // Giữ nguyên HandsShotPose chung; dải đen mỏng để không che lòng tay và mười xu.
            StartCoroutine(Root.UI.Letterbox.Slide(0.35f, data.Timing("letterboxIn", 1f)));
            yield return Root.Subtitles.PlayAndWait("mrs_pay_here");
            yield return purser.HandToPlayer();
            yield return coins.Drop(new[] { rig.hands.left, rig.hands.right }, Vector3.down, data.Coins["player"]);
            // Đủ mười xu mới phát event; không phủ thẻ tư liệu lên khung tay.
            Root.Events.Raise(EventIds.S3PayReceived);
            yield return new WaitForSeconds(data.Timing("payHold", 8f));
            coins.Clear();
            rig.ExitHandsShot();
            Root.UI.Letterbox.Set(0f);
            ledger.available = true;
            yield return Root.Subtitles.PlayAndWait("mrs_pay_ba");
        }

        IEnumerator Cafe(Content.EndingData data)
        {
            Beat = "E5b";
            Objective("mrs.objective.service");
            var rig = Rig;
            Root.UI.Notebook.Close();
            Root.UI.Notebook.AllowToggle = false;
            rig.SetControl(false);
            var to = table.position - seatPoint.position;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            // Bước tới chỗ ngồi với tầm mắt đứng rồi mới ngồi ghế bằng clip SitChair (gốc trước tâm ghế SeatForward): bot 23/10 hạ đầu
            // 0,45 m từ lúc còn đi và gốc ở tâm ghế nên bụng, bắp chân xuyên CafeChair tới 14 cm.
            var seated = seatPoint.position + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * SitController.SeatForward + Vector3.up * SitController.SeatLift;
            // Đứng lại ngoài ghế (0,45 m theo hướng đi tới) rồi mới ngồi: đi đứng tới tận điểm ngồi thì thân đứng lướt qua ghế đã tắt
            // collider, bắp chân lún CafeChair 13 cm (bot 23/10 032931Z).
            var back = rig.Position - seated;
            back.y = 0f;
            if (back.sqrMagnitude < 1e-4f) back = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
            yield return rig.MoveTo(seated + back.normalized * 0.45f, yaw, 5f, 1f);
            rig.GetComponent<SitController>().SitForStudy(seatPoint, seated + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 2f + Vector3.up * 0.6f, chairClip);
            rig.hands.SetOverride(new HandPoseTarget(HandPose.Lowered,
                new Vector3(-0.12f, -0.36f, 0.5f), new Vector3(35f, 10f, 0f),
                new Vector3(0.24f, -0.55f, 0.3f), new Vector3(20f, 0f, -10f)));
            while (!MaiSeated) yield return null; // Cả Sim cũng phải đợi ông Mai ngồi thật.
            yield return waiter.Serve();
            ClearGuide();
            Root.Events.Raise(EventIds.S3CafeServed);
            // Im lặng thứ ba (DIRECTOR §5): quán im đi một nhịp khi bồi bàn gọi Ba là "ông".
            Root.Audio.Hush(0.15f, 0.6f);
            yield return Root.Subtitles.PlayAndWait("mrs_waiter_voila");
            yield return Root.Subtitles.PlayAndWait("mrs_ba_thanks");
            // Im một nhịp (DIRECTOR §5.3): Mai nhìn Ba, chưa nói gì.
            if (!Sim.Enabled) yield return new WaitForSeconds(MonsieurPause);
            yield return Root.Subtitles.PlayAndWait("mrs_mai_monsieur");
            yield return Root.Subtitles.PlayAndWait("mrs_mai_monsieur2");
            Root.Audio.Hush(1f, 2.5f);
            StartCoroutine(waiter.ReturnToCounter());
            // Cúi nhìn xuống bàn (đang ngồi: chỉ đổi góc nhìn, không dời gốc).
            for (float t = 0f, from = rig.look.Pitch; t < 0.5f; t += Time.deltaTime)
            {
                rig.look.SetAngles(yaw, Mathf.Lerp(from, 32f, Mathf.SmoothStep(0f, 1f, t / 0.5f)));
                yield return null;
            }
            rig.look.SetAngles(yaw, 32f);
            yield return new WaitForSeconds(3f);

            Beat = "E5c";
            Objective("mrs.objective.notebook");
            var target = Root.Content.Notebook.LinkTarget;
            Root.Notebook.EnsureUnlocked(target.A);
            Root.Notebook.EnsureUnlocked(target.B);
            Link = new PageLinkController(target, Root.Events);
            // Sổ phủ lên HUD nên dòng chỉ dẫn bị che: đưa câu chỉ dẫn xuống chân sổ.
            string footer = S("mrs.objective.notebook");
            Root.UI.Notebook.OpenLink(Link, null);
            Root.UI.Notebook.SetFooter(footer);
            float waited = 0f;
            bool hinted = false;
            while (!Link.Linked)
            {
                waited += Time.deltaTime;
                if (!Root.UI.Notebook.IsOpen)
                {
                    Root.UI.Notebook.OpenLink(Link, null);
                    Root.UI.Notebook.SetFooter(footer);
                }
                if (!hinted && waited >= data.Timing("linkHintAfter", 35f))
                {
                    hinted = true;
                    Root.UI.Notebook.HighlightPages(target.A, target.B);
                    footer = S("hint.link_pages");
                    Root.UI.Notebook.SetFooter(footer);
                }
                if (Sim.Enabled && waited >= Sim.StepSeconds)
                    Root.UI.Notebook.SimulateLink(target.A, target.B);
                yield return null;
            }
            Root.UI.Notebook.SetFooter(string.Empty);
            yield return new WaitForSeconds(data.Timing("linkHold", 2f));
            Root.UI.Notebook.Close();
        }

        void OnDestroy()
        {
            PauseController.QuitOnEscape = false;
            if (!Mathf.Approximately(TimeScaleForTests, 1f))
                Time.timeScale = 1f;
        }
    }
}
