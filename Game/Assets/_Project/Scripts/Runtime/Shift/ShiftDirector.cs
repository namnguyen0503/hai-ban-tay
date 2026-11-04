using System.Collections;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Narrative;
using HaiBanTay.Npc;
using HaiBanTay.Performance;
using HaiBanTay.Player;
using HaiBanTay.Ship;
using HaiBanTay.Tasks;
using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Cảnh 02 — một ngày trên tàu, chạy theo days.vi.json (ngày GameState.Day, sáu ngày từ Biển Đông tới gần Marseille).
    /// Mỗi ngày: thẻ ngày → ca làm 4:00 → 21:00 gồm nhiều lượt (lệnh của quản bếp trên bảng việc, làm theo thứ tự tùy ý;
    /// lò tự hạ lửa; NPC của ngày tới giúp, ghé thăm — DayCast) → hết ca → buổi tối (học với người dạy hoặc tự chép chữ:
    /// ghép từ, tô chữ, điền từ, viết câu theo StudyPlanner) → ngày sau, hoặc Marseille sau ngày cuối.
    /// Đồng hồ chạy theo việc (ShiftClockLogic): mỗi lượt một khung giờ, kim đi theo tiến độ bảng việc, đứng không thì giờ
    /// không trôi. Lượt montage tua nhanh việc lặp lại (bữa trưa, rửa nồi…) bằng một thẻ giờ ngắn. Không có thất bại.
    /// </summary>
    public sealed class ShiftDirector : SceneDirector
    {
        public ShipMotion ship;
        public StormController storm;
        public CoalStation coal;
        public SackStation sacks;
        public WashStation wash;
        public MealPots meals;
        public AshStation ash;
        public SweepStation sweep;
        public GreensStation greens;
        public PanStation pan;
        public SecurePotsTask pots;
        [Tooltip("Nồi cao: kéo ra lò, nhấc lên bếp, nhấc xuống, kéo về, cọ trên ghế (tall_out, scrub_tall).")]
        public TallPotStation tall;
        public HaulStation haul;
        public StudyTask study;
        public WorkStation[] storyStations = new WorkStation[0];
        public VoyagePeople people;
        [Tooltip("NPC theo ngày: hai lính, Jacques, ông Mai.")]
        public DayCast cast;
        [Tooltip("Phông cảng, đám đông trên boong, nước cảng, máy êm khi tàu đỗ.")]
        public DayScenery scenery;
        public InteractTarget restSeat;
        public WallClock wallClock;
        public ChalkboardView board;
        public Transform eveningSpawn;
        public GameObject[] dayOnly = new GameObject[0];
        public GameObject[] eveningOnly = new GameObject[0];
        [Header("Đi tự do trên tàu")]
        public ShipZone galleyZone;
        public ShipZone holdZone;
        [Tooltip("Ba ở ngoài bếp quá lâu khi còn việc chờ thì quản bếp gọi.")]
        public float outsideCallSeconds = 30f;
        [Header("Đèn realtime trong khoang")]
        public LightBudget lights;
        [Tooltip("Đèn thứ hai cạnh lửa lò: đèn treo ban ngày, đèn dầu buổi tối.")]
        public Light dayLamp;
        public Light eveningLamp;
        [Tooltip("Nhiệt nền của bếp (nhân với DayData.Heat).")]
        public float roomHeat = 0.2f;
        [Tooltip("Lửa dưới mức này thì quản bếp đòi thêm than.")]
        public float lowFire = 0.4f;
        [Tooltip("Lượt có NeedsFire chỉ xong khi lửa từ mức này trở lên.")]
        public float fireNeeded = 0.5f;
        public int extraFireShovels = 3;
        [Tooltip("Tro dưới ghi từ mức này thì quản bếp bảo cào lò.")]
        public float highAsh = 0.45f;
        public float lowFireLineInterval = 45f;
        public float restHintSeconds = 25f;
        [Tooltip("Thời gian thật để tua đồng hồ tới giờ bắt đầu lượt sau.")]
        public float fastForwardSeconds = 1.5f;
        [Tooltip("Thời gian thật của thẻ montage (giờ chạy qua cả khung của lượt tua nhanh).")]
        public float montageSeconds = 4.5f;
        [Header("Mũi tên chỉ hướng tới việc đang chờ")]
        public Transform galleyBeacon;
        public Transform galleyDoorBeacon;
        public Transform companionBeacon;
        public Transform holdBeacon;
        [Tooltip("Gần đích hơn mức này thì ẩn mũi tên (m).")]
        public float pointerMinDistance = 3f;

        BodyState _body;
        bool _fireWasLit;
        float _lastFireLine = -999f;
        float _lastAshLine = -999f;
        bool _rested;
        string _shownClock;
        float _outsideSeconds;
        bool _holdHinted;
        float _eveningMinutes;
        float _nextPointer;
        int _guideHands = -1;
        WorkStation[] _stations;
        bool _resting;
        /// <summary>Màn đang đen vì lượt montage; lượt montage liền sau (hoặc hết ca) giữ màn đen, không mở ra rồi đóng lại.</summary>
        bool _dark, _keepDark;

        public DayData Day { get; private set; }
        public PhaseData Phase { get; private set; }
        public int PhaseIndex { get; private set; } = -1;
        public OrderBoardLogic Board { get; } = new OrderBoardLogic();
        public ShiftClockLogic Clock { get; private set; }
        public bool InShift { get; private set; }
        public bool Finished { get; private set; }
        /// <summary>Giờ bắt đầu buổi tối (phút từ nửa đêm).</summary>
        public float EveningMinutes => _eveningMinutes;
        /// <summary>Kế hoạch học của buổi tối (null nếu tối nghỉ hoặc chưa tới).</summary>
        public StudyPlanner.Plan StudyPlan { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            Day = Root.Content.GetDay(Root.State.Day) ?? Root.Content.GetDay(GameState.FirstDay);
            // Khung cảnh trước Start của mọi thứ khác (EngineHum đọc âm lượng ở Start).
            if (Day != null)
                scenery?.Apply(Day);
        }

        IEnumerator Start()
        {
            if (Day == null)
            {
                Debug.LogError("ShiftDirector: days.vi.json không có ngày nào.");
                yield break;
            }
            Root.State.Day = Day.Number;
            if (Root.State.MaxDay < Day.Number)
                Root.State.MaxDay = Day.Number;
            Prepare();
            SaveSystem.Write(SaveSystem.Capture(SceneFlow.GameScenes[1], Root.State, Root.Flags, Root.Notebook, Root.Events));
            // Ngày 1 bốn giờ sáng: bếp tối, chỉ ánh lò; nhóm lò xong thì phòng đỏ dần lên (DIRECTOR §6).
            DawnDarkness dark = null;
            if (Day.Number == 1 && !Sim.Enabled)
            {
                dark = gameObject.AddComponent<DawnDarkness>();
                dark.Begin(() => coal.Fire.Level);
            }

            // Thẻ ngày: tên ngày, chặng, giờ bắt đầu.
            Beat = $"D{Day.Number}.intro";
            BeginCutscene();
            Root.UI.Card.Blackout();
            Root.Audio.PlayMusic("Music/voyage", loop: true);
            // Tám tiếng chuông (giờ đổi phiên 04:00, lúc Ba dậy) mở mỗi ngày trên màn đen (DIRECTOR §6).
            Root.Audio.PlaySfx("SFX/ship_bell_8", 0.7f);
            yield return Root.UI.Card.ShowDated(Day.Title, $"{Day.Leg} · {ShiftClockLogic.Format(Day.ClockStart)}", 3.5f);
            Root.UI.Card.Hide();
            EndCutscene();
            foreach (var ev in Day.StartEvents)
                Root.Events.Raise(ev);

            StartShift();
            for (int i = 0; i < Day.Phases.Count; i++)
            {
                PhaseIndex = i;
                // Montage liền sau, hoặc montage là lượt cuối (hết ca cũng tối màn): giữ màn đen.
                _keepDark = i + 1 < Day.Phases.Count ? Day.Phases[i + 1].Montage : Day.Phases[i].Montage;
                yield return RunPhase(Day.Phases[i]);
                if (dark != null && Day.Phases[i].Id == "dawn")
                    dark.Release();
            }
            if (dark != null)
                dark.Release();
            yield return EndShift();
            yield return Evening();
            NextDay();
        }

        void Prepare()
        {
            SetGrime(Mathf.Max(Root.State.Grime, Day.GrimeStart), instant: true);
            ship.Intensity = Day.Ship;
            // Ấn Độ Dương: than cháy nhiều tro (lò nghẹt nhanh, phải cào nhiều).
            coal.Fire.AshPerFuel *= Mathf.Max(0f, Day.AshRate);
            foreach (var go in eveningOnly)
                if (go != null)
                    go.SetActive(false);
            foreach (var go in dayOnly)
                if (go != null)
                    go.SetActive(true);
            lights?.SetInterior(coal.fireLight, dayLamp);
            if (restSeat != null)
                restSeat.available = false;
            Clock = new ShiftClockLogic(Day.ClockStart) { Running = false };
            wallClock?.Set(Clock.Minutes);
            // 4 giờ sáng: lò nguội, phải nhóm lửa.
            coal.Fire.Set(0f, 0f, Day.AshStart);
            coal.SetStock(Day.CoalStock);
            _body = Rig != null ? Rig.GetComponent<BodyState>() : null;
            if (_body != null)
            {
                _body.AmbientHeat = roomHeat * Day.Heat;
                float floor = Mathf.Max(Day.FatigueStart, Root.State.Fatigue * 0.5f);
                _body.Begin(floor, floor);
            }
            Board.ResetDay();
            Board.Changed += RefreshBoard;
            Root.UI.Shift.SetDay(Day.Title);
            cast?.Begin(Day);
            tall?.ResetForDay();
            meals?.HideBread();
        }

        void StartShift()
        {
            Root.Audio.StopMusic();
            InShift = true;
            Clock.Running = true;
            Root.UI.Shift.Show(true);
            foreach (var s in Stations())
            {
                s.Worked += OnWorked;
                s.Activate();
            }
        }

        WorkStation[] Stations()
        {
            var all = new WorkStation[] { coal, sacks, wash, ash, sweep, greens, pan, haul, tall };
            var joined = new System.Collections.Generic.List<WorkStation>(all);
            joined.AddRange(storyStations);
            return joined.FindAll(s => s != null && s.Kind != OrderKinds.Coffee).ToArray();
        }

        void OnWorked(WorkStation station, int units)
        {
            if (station == coal || station == ash) SyncStoveBoard();
            else Board.Report(station is TallPotStation t ? t.LastKind : station.Kind, units);
        }

        void SyncStoveBoard()
        {
            if (coal != null) Board.SetStateRemaining(OrderKinds.Fire, coal.PendingPours);
            if (ash != null) Board.SetStateRemaining(OrderKinds.Ash, ash.BucketsNeeded());
        }

        void Update()
        {
            SyncStoveBoard();
            int hands = Rig != null && Rig.interactor != null ? Rig.interactor.HandsVersion : 0;
            if (hands != _guideHands && Clock != null)
            {
                // Đồ trên tay vừa đổi: bước hướng dẫn đổi ngay, chữ và mốc không còn chỉ vào vật đang cầm (QA-0070).
                _guideHands = hands;
                UpdateGuide();
            }
            if (Time.time >= _nextPointer && Clock != null)
            {
                _nextPointer = Time.time + 0.2f;
                UpdateGuide();
                if (InShift)
                    UpdatePointer();
            }
            if (!InShift || Clock == null)
                return;
            if (Phase != null)
                Clock.SetProgress(ShiftClockLogic.BoardProgress(Board));
            Clock.Tick(Time.deltaTime);
            wallClock?.Set(Clock.Minutes);
            var text = Clock.Text;
            if (text != _shownClock)
            {
                _shownClock = text;
                Root.UI.Shift.SetClock(text);
            }
            if (coal.Fire.Level >= 0.8f)
                _fireWasLit = true;
            // Lò sắp tắt giữa ca: thêm lệnh xúc than (không chồng lệnh).
            if (_fireWasLit && coal.Fire.Level < lowFire && !Board.HasOpen(OrderKinds.Fire))
                AddExtraFire();
            // Tro đầy ghi: thêm lệnh cào lò.
            if (ash != null && Phase != null && coal.Fire.Ash >= highAsh && !Board.HasOpen(OrderKinds.Ash))
                AddExtraAsh();
            bool holdOrder = Board.HasOpen(OrderKinds.Sack) || Board.HasOpen(OrderKinds.Greens) || Board.HasOpen(OrderKinds.Haul) || Board.HasOpen(OrderKinds.Meat) || Board.HasOpen(OrderKinds.Fish) || Board.HasOpen(OrderKinds.Ice);
            // Lệnh đầu tiên cần xuống hầm thực phẩm: chỉ đường một lần.
            if (holdOrder && !_holdHinted)
            {
                _holdHinted = true;
                Root.UI.Prompt.ShowHint(S("hint.hold_route"), 9f);
            }
            TickOutsideCall(holdOrder);
        }

        /// <summary>
        /// Chỉ dẫn từng bước (dòng việc tiếp theo + mốc trên màn hình): việc người chơi đang dở tay trước (đang cầm xô tro,
        /// sọt than, dụng cụ…), không thì lệnh chưa xong đầu tiên trên bảng; hết ca thì chỉ ghế nghỉ.
        /// </summary>
        void UpdateGuide()
        {
            var who = Rig != null ? Rig.interactor : null;
            Transform target = null;
            string key = null;
            if (who != null && GuideFor(who, out target, out key))
            {
                Root.UI.Guide.Set(S(key), target);
                return;
            }
            Root.UI.Guide.Clear();
        }

        /// <summary>Bước chỉ dẫn hiện tại (test đọc).</summary>
        public bool GuideFor(Interactor who, out Transform target, out string key)
        {
            target = null;
            key = null;
            if (_resting && restSeat != null && restSeat.available)
            {
                target = restSeat.transform;
                key = "guide.rest";
                return true;
            }
            if (Board.Orders.Count == 0)
                return false;
            _stations ??= Stations();
            foreach (var s in _stations)
                if (s.State == TaskState.Active && s.Engaged(who) && s.Guide(who, out target, out key))
                    return true;
            foreach (var o in Board.Orders)
            {
                if (o.IsDone)
                    continue;
                TaskBase task = TaskFor(o.Kind);
                if (task == null)
                    task = StationFor(o.Kind);
                if (task != null && task.State == TaskState.Active && task.Guide(who, out target, out key))
                    return true;
            }
            target = null;
            key = null;
            return false;
        }


        /// <summary>
        /// Mũi tên chỉ đường (HUD) tới chỗ việc đang chờ khi người chơi ở xa: việc trong bếp mà người chơi ở ngoài, việc ở
        /// hầm mà người chơi ở bếp hay trên boong, rổ rau ngày bão. Chỉ tới cửa kế tiếp trên đường đi (cửa bếp, cửa buồng
        /// thang), nhãn ghi đích cuối và khoảng cách.
        /// </summary>
        void UpdatePointer()
        {
            if (Rig == null || Board.AllDone)
            {
                Root.UI.Shift.SetPointer(null, 0f);
                return;
            }
            var feet = Rig.Position;
            bool inGalley = galleyZone != null && galleyZone.PlayerInside;
            bool inHold = holdZone != null && holdZone.PlayerInside;
            bool carrying = Rig.interactor != null && Rig.interactor.Holding != null;
            bool holdWork = Board.HasOpen(OrderKinds.Sack) || Board.HasOpen(OrderKinds.Greens) || Board.HasOpen(OrderKinds.Meat) || Board.HasOpen(OrderKinds.Fish) || Board.HasOpen(OrderKinds.Ice);
            bool galleyWork = false;
            foreach (var o in Board.Orders)
                if (!o.IsDone && o.Kind != OrderKinds.Sack && o.Kind != OrderKinds.Greens && o.Kind != OrderKinds.Haul && o.Kind != OrderKinds.Meat && o.Kind != OrderKinds.Fish && o.Kind != OrderKinds.Ice)
                    galleyWork = true;

            string label = null;
            Vector3 goal = default, next = default;
            var guided = Root.UI.Guide.Target;
            if (guided != null && !Board.HasOpen(OrderKinds.Haul))
            {
                // Theo đúng bước chỉ dẫn: đích ở hầm thực phẩm mà người chơi ở trên, hoặc ngược lại thì chỉ cửa kế tiếp.
                bool targetInHold = holdZone != null && holdZone.Contains(guided.position);
                bool targetInGalley = galleyZone != null && galleyZone.Contains(guided.position);
                if (targetInHold && !inHold)
                {
                    goal = guided.position;
                    label = S("hud.go_hold");
                    next = Toward(holdBeacon, inGalley, inHold);
                }
                else if (targetInGalley && !inGalley)
                {
                    goal = guided.position;
                    label = S("hud.go_galley");
                    next = Toward(galleyBeacon, inGalley, inHold);
                }
            }
            else if (Board.HasOpen(OrderKinds.Haul) && haul != null)
            {
                // Rổ chưa buộc: tới rổ; đã buộc: đi về bếp (đi trước rổ).
                bool tied = haul.Logic.Tied;
                goal = tied ? Beacon(galleyBeacon) : haul.BasketPosition;
                label = S(tied ? "hud.go_galley" : "hud.go_basket");
                next = tied ? Toward(galleyBeacon, inGalley, inHold) : Toward(holdBeacon, inGalley, inHold, haul.BasketBelowDeck);
                if (!tied && !haul.BasketBelowDeck)
                    next = goal;
            }
            else if ((galleyWork || (carrying && holdWork)) && !inGalley && !(holdWork && inHold && !carrying))
            {
                goal = Beacon(galleyBeacon);
                label = S("hud.go_galley");
                next = Toward(galleyBeacon, inGalley, inHold);
            }
            else if (holdWork && !galleyWork && !inHold && !carrying)
            {
                goal = Beacon(holdBeacon);
                label = S("hud.go_hold");
                next = Toward(holdBeacon, inGalley, inHold);
            }
            if (label == null || (goal - feet).magnitude < pointerMinDistance)
            {
                Root.UI.Shift.SetPointer(null, 0f);
                return;
            }
            var dir = next - feet;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float view = Rig.cam != null ? Rig.cam.transform.eulerAngles.y : Rig.transform.eulerAngles.y;
            Root.UI.Shift.SetPointer(string.Format(S("hud.pointer"), label, (goal - feet).magnitude), Mathf.DeltaAngle(view, yaw));
        }

        static Vector3 Beacon(Transform t) => t != null ? t.position : Vector3.zero;

        /// <summary>Điểm kế tiếp trên đường tới đích (bếp, hầm): ra cửa bếp, vào buồng thang, hoặc thẳng tới đích.</summary>
        Vector3 Toward(Transform target, bool inGalley, bool inHold, bool targetBelow = false)
        {
            bool toHold = target == holdBeacon || targetBelow;
            if (toHold)
            {
                if (inHold)
                    return Beacon(target);
                if (inGalley && galleyDoorBeacon != null)
                    return galleyDoorBeacon.position;
                return companionBeacon != null ? companionBeacon.position : Beacon(target);
            }
            if (inHold && companionBeacon != null)
                return companionBeacon.position;
            if (!inGalley && galleyDoorBeacon != null)
                return galleyDoorBeacon.position;
            return Beacon(target);
        }

        /// <summary>
        /// Đi tự do không dừng đồng hồ. Ba ở ngoài bếp quá lâu khi việc còn chờ thì quản bếp gọi (câu S16 theo loại việc);
        /// đang ở hầm thực phẩm vì lệnh lấy khoai, lấy rau thì không bị gọi.
        /// </summary>
        void TickOutsideCall(bool holdOrder)
        {
            bool inGalley = galleyZone == null || galleyZone.PlayerInside;
            // Đang ở hầm vì lệnh lấy khoai, lấy rau; ngày bão đang kéo rổ rau qua boong: không bị gọi.
            bool fetching = (holdOrder && holdZone != null && holdZone.PlayerInside) || Board.HasOpen(OrderKinds.Haul);
            if (inGalley || fetching || Board.AllDone)
            {
                _outsideSeconds = 0f;
                return;
            }
            _outsideSeconds += Time.deltaTime;
            if (_outsideSeconds < outsideCallSeconds)
                return;
            _outsideSeconds = 0f;
            Root.Subtitles.Play(OutsideCall(Board, Phase != null ? Phase.Line : null));
        }

        /// <summary>
        /// Câu gọi đúng việc còn chờ, ưu tiên lửa lò. Ba câu S16 ("thêm than", "dọn chảo", "đem nước") chỉ dùng cho đúng
        /// việc đó; việc khác dùng câu lệnh của chính việc ấy, không có thì nhắc lại câu mở lượt (fallback).
        /// </summary>
        public static string OutsideCall(OrderBoardLogic board, string fallback = null)
        {
            if (board.HasOpen(OrderKinds.Fire)) return "chef_call_coal";
            if (board.HasOpen(OrderKinds.Ash)) return "chef_ash";
            if (board.HasOpen(OrderKinds.Pan)) return "chef_call_pan";
            if (board.HasOpen(OrderKinds.Water)) return "chef_call_water";
            if (board.HasOpen(OrderKinds.TallOut)) return "chef_tall_out";
            if (board.HasOpen(OrderKinds.ScrubTall)) return "chef_tall_back";
            if (board.HasOpen(OrderKinds.Wash)) return "chef_wash";
            if (board.HasOpen(OrderKinds.Sack)) return "chef_potatoes";
            if (board.HasOpen(OrderKinds.Greens)) return "chef_greens";
            if (board.HasOpen(OrderKinds.Haul)) return "chef_haul";
            if (board.HasOpen(OrderKinds.Sweep)) return "chef_story_sweep";
            if (board.HasOpen(OrderKinds.CoalFetch)) return "chef_story_coal";
            if (board.HasOpen(OrderKinds.Meat) || board.HasOpen(OrderKinds.Fish) || board.HasOpen(OrderKinds.Ice)) return "chef_story_hold";
            if (board.HasOpen(OrderKinds.Serve)) return "chef_story_serve";
            if (board.HasOpen(OrderKinds.Peel)) return "chef_story_peel";
            if (board.HasOpen(OrderKinds.Asparagus)) return "chef_story_asparagus";
            if (board.HasOpen(OrderKinds.Soup)) return "chef_story_soup";
            return string.IsNullOrEmpty(fallback) ? "chef_late_quay" : fallback;
        }

        void AddExtraFire()
        {
            int count = coal.PoursNeeded(extraFireShovels);
            if (count == 0) return;
            coal.OnOrder(extraFireShovels);
            Board.Add(OrderKinds.Fire, count, extra: true);
            if (Time.time - _lastFireLine >= lowFireLineInterval)
            {
                _lastFireLine = Time.time;
                Root.Subtitles.Play("chef_fire_low");
            }
        }

        void AddExtraAsh()
        {
            ash.PinGoal();
            int count = ash.BucketsNeeded();
            if (count == 0) return;
            Board.Add(OrderKinds.Ash, count, extra: true);
            if (Time.time - _lastAshLine >= lowFireLineInterval)
            {
                _lastAshLine = Time.time;
                Root.Subtitles.Play("chef_ash");
            }
        }

        /// <summary>Lượt trước xong sớm: đồng hồ quay nhanh tới giờ bắt đầu lượt sau (thấy được trên HUD và đồng hồ tường).</summary>
        IEnumerator FastForward(int to)
        {
            if (to <= 0 || Clock.Minutes >= to)
                yield break;
            float from = Clock.Minutes;
            float seconds = Sim.Enabled ? 0.05f : fastForwardSeconds;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                Clock.Minutes = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds));
                yield return null;
            }
            Clock.Minutes = to;
        }

        /// <summary>Giờ kết thúc lượt: giờ phục vụ nếu có, không thì đầu khung + số phút của lượt.</summary>
        public static float PhaseEnd(PhaseData phase, float from) =>
            phase.ServiceAt > 0 ? Mathf.Max(from, phase.ServiceAt) : from + Mathf.Max(0, phase.Minutes);

        /// <summary>Số đơn vị lệnh của lượt: lượt rửa lấy chính hàng nồi bẩn đã được mang về, tối đa count của JSON.</summary>
        int OrderCount(OrderData o)
        {
            if (o.Task == OrderKinds.Wash && meals != null) return Mathf.Min(o.Count, WashNeed(wash.Logic));
            if (o.Task == OrderKinds.Fire) return coal.PoursNeeded(o.Count);
            if (o.Task == OrderKinds.Ash) { ash.PinGoal(); return ash.BucketsNeeded(); }
            return o.Count;
        }

        IEnumerator RunPhase(PhaseData phase)
        {
            yield return FastForward(phase.StartAt);
            float from = Mathf.Max(Clock.Minutes, phase.StartAt);
            float to = PhaseEnd(phase, from);
            Clock.SetWindow(from, to);
            if (phase.Montage)
            {
                yield return RunMontage(phase, to);
                yield break;
            }
            Phase = phase;
            Beat = $"D{Day.Number}.{phase.Id}";
            Board.Clear();
            foreach (var station in storyStations)
                if (station is VoyageDelivery delivery && delivery.Pending == 0) delivery.load.gameObject.SetActive(false);
            if (phase.Dirty > 0 || phase.CookTall) meals?.BeginMeal(phase);
            if (haul != null)
                haul.waveEnabled = phase.DeckWave;
            if (tall != null) tall.Sway = phase.TallSway;
            foreach (var o in phase.Orders)
            {
                Board.Add(o.Task, OrderCount(o));
                OrderStation(o);
            }
            foreach (var c in phase.StartCards)
                Root.UI.Cards.Show(c);
            if (phase.Storm)
            {
                Root.Events.Raise(EventIds.S2StormStart);
                storm.StartStorm();
            }
            if (phase.Calm && storm.Active)
            {
                storm.Calm(4f);
                Root.Events.Raise(EventIds.S2StormCalm);
            }
            if (!string.IsNullOrEmpty(phase.Line))
                Root.Subtitles.Play(phase.Line);
            // Ngày 5 từ trưa (DIRECTOR §6, S16): ba câu gọi của quản bếp dồn dập nối nhau, không đợi Ba làm xong việc trước.
            if (Day.Number == 5 && phase.Id == "lunch" && !phase.Montage)
                foreach (var call in new[] { "chef_call_water", "chef_call_pan", "chef_call_coal" })
                    Root.Subtitles.Play(call);
            if (Board.HasOpen(OrderKinds.Haul))
                Root.UI.Prompt.ShowHint(S("hint.haul"), 9f);
            cast?.PhaseStarted(phase);
            people?.Phase(phase);
            foreach (var o in phase.Orders)
            {
                var task = TaskFor(o.Task);
                if (task != null)
                    StartCoroutine(RunTaskOrder(task, o.Task));
            }

            float simAt = Time.time + Sim.StepSeconds;
            while (!PhaseDone(phase))
            {
                if (phase.NeedsFire && Board.AllDone && (!FireReady || (meals != null && !meals.ReadyToServe)) && !Board.HasOpen(OrderKinds.Fire))
                    AddExtraFire();
                // Sim: làm xong mọi lệnh đang mở; lặp lại nếu giữa chừng có lệnh thêm (lửa yếu, tro đầy).
                if (Sim.Enabled && Time.time >= simAt)
                {
                    simAt = Time.time + Sim.StepSeconds;
                    SimulatePhase();
                }
                yield return null;
            }
            Clock.SetProgress(1f);

            cast?.PhaseEnded(phase);
            if (cast != null) yield return cast.SayAfterWork(phase.AfterLines);
            else foreach (var line in phase.AfterLines) yield return Root.Subtitles.PlayAndWait(line);
            foreach (var ev in phase.Events)
                Root.Events.Raise(ev);
            foreach (var c in phase.Cards)
                Root.UI.Cards.Show(c);
            if (phase.ServiceAt > 0)
                Root.Audio.PlaySfx("SFX/service_bell");
            if (phase.Dirty > 0 || phase.CookTall)
            {
                if (meals != null) yield return meals.Serve();
                else wash.AddDirty(phase.Dirty);
            }
            // Ngày 5, xong chồng nồi tám chiếc: quản bếp đặt mẩu bánh mì cạnh Ba rồi quay đi (DIRECTOR §6).
            if (Day.Number == 5 && phase.Id == "dishes_noon" && meals != null && !Sim.Enabled)
                yield return meals.ChefBread();
            yield return new WaitForSeconds(1.5f);
        }

        /// <summary>Số nồi lượt rửa phải rửa xong: nồi bẩn còn ở chồng và chiếc đang ở chậu. Rửa sớm hết thì 0, lượt xong
        /// ngay (QA-0001).</summary>
        public static int WashNeed(WashLogic logic) => logic.Pending + (logic.InBasin ? 1 : 0);

        /// <summary>
        /// Lượt tua nhanh (việc lặp lại: bữa trưa, rửa nồi…): màn đen ngắn với thẻ giờ "10:00 – 11:30" và câu tóm tắt; trong
        /// lúc đó việc của lượt được làm xong theo đúng thứ tự (nấu, phục vụ, rửa nồi, lửa lò) bằng cùng đường sự kiện như
        /// khi người chơi làm, rồi đồng hồ chạy tới cuối khung. Không thao tác, không NPC đứng chờ nói chuyện.
        /// </summary>
        IEnumerator RunMontage(PhaseData phase, float to)
        {
            Phase = phase;
            Beat = $"D{Day.Number}.{phase.Id}";
            bool show = !Sim.Enabled;
            float from = Clock.Minutes;
            if (show && !_dark)
            {
                BeginCutscene();
                yield return Root.Scenes.Fade(0f, 1f, 0.5f);
                Root.UI.SetFade(0f);
                Root.UI.Card.Blackout();
                _dark = true;
            }
            Board.Clear();
            foreach (var station in storyStations)
                if (station is VoyageDelivery delivery && delivery.Pending == 0) delivery.load.gameObject.SetActive(false);
            // Bữa tua nhanh trọn vẹn: nấu, phục vụ, rồi mới tới các lệnh (rửa cả nồi của chính bữa này).
            if (phase.Dirty > 0 || phase.CookTall)
            {
                meals?.BeginMeal(phase, instant: true);
                PrimeMontageFire();
                if (meals != null) yield return meals.Serve(instant: true);
                else wash.AddDirty(phase.Dirty);
                // Nồi cao vừa phục vụ trong lúc tua nhanh: không còn lượt cọ nồi cao nào trước bữa nấu kế tiếp trong ngày thì
                // phụ bếp kéo về cọ luôn (màn còn đen).
                if (phase.CookTall && tall != null && !ScrubTallAhead(phase))
                    tall.FastForwardTo(TallPotState.HomeClean);
            }
            if (haul != null)
                haul.waveEnabled = false;
            if (tall != null) tall.Sway = phase.TallSway;
            foreach (var o in phase.Orders)
            {
                Board.Add(o.Task, OrderCount(o));
                OrderStation(o);
            }
            cast?.PhaseStarted(phase);
            people?.Phase(phase);
            if (phase.Storm)
            {
                Root.Events.Raise(EventIds.S2StormStart);
                storm.StartStorm();
            }
            if (phase.Calm && storm.Active)
            {
                storm.Calm(0.5f);
                Root.Events.Raise(EventIds.S2StormCalm);
            }
            // Việc của lượt làm xong theo thứ tự lệnh; việc dạng task (chằng nồi) xong ngay.
            for (int guard = 0; guard < 20 && !PhaseDone(phase); guard++)
            {
                SimulatePhase();
                foreach (var o in Board.Orders)
                    if (!o.IsDone && TaskFor(o.Kind) != null)
                        Board.Report(o.Kind, o.Remaining);
            }
            if (!PhaseDone(phase))
                Debug.LogWarning($"ShiftDirector: montage {phase.Id} chưa xong lệnh: {Board.Orders.Count}");
            cast?.PhaseEnded(phase);
            foreach (var ev in phase.Events)
                Root.Events.Raise(ev);
            Clock.Running = false;
            bool said = string.IsNullOrEmpty(phase.Line);
            if (!said)
                StartCoroutine(Then(Root.Subtitles.PlayAndWait(phase.Line), () => said = true));
            if (show)
            {
                string range = string.Format(S("card.montage_time"), ShiftClockLogic.Format(from), ShiftClockLogic.Format(to));
                bool shown = false;
                StartCoroutine(Then(Root.UI.Card.ShowDated(range, S(phase.Caption), montageSeconds), () => shown = true));
                for (float t = 0f; !shown; t += Time.deltaTime)
                {
                    Clock.Minutes = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / montageSeconds)));
                    wallClock?.Set(Clock.Minutes);
                    yield return null;
                }
            }
            while (!said) yield return null;
            Clock.Minutes = Mathf.Max(Clock.Minutes, to);
            Clock.SetWindow(Clock.Minutes, Clock.Minutes);
            Clock.Running = true;
            wallClock?.Set(Clock.Minutes);
            if (show && !_keepDark)
                yield return Unveil();
            foreach (var c in phase.StartCards)
                Root.UI.Cards.Show(c);
            foreach (var c in phase.Cards)
                Root.UI.Cards.Show(c);
        }

        /// <summary>Mở màn sau montage.</summary>
        IEnumerator Unveil()
        {
            if (!_dark)
                yield break;
            _dark = false;
            Root.UI.Card.Hide();
            Root.UI.SetFade(1f);
            yield return Root.Scenes.Fade(1f, 0f, 0.5f);
            EndCutscene();
        }

        static IEnumerator Then(IEnumerator routine, System.Action done)
        {
            yield return routine;
            done();
        }

        bool PhaseDone(PhaseData phase)
        {
            return Board.AllDone && (!phase.NeedsFire || FireReady) && (meals == null || meals.ReadyToServe);
        }

        bool FireReady => meals != null && meals.Cooking > 0 ? meals.ReadyToServe : coal.Fire.Level >= fireNeeded;

        void PrimeMontageFire()
        {
            for (int i = 0; i < coal.cells.Length; i++)
            {
                if (coal.Bin.Empty && !coal.RefillForMontage()) break;
                coal.SimulateWork();
            }
            coal.Fire.Tick(MealPots.CookingSeconds + 2f);
            meals?.TickCooking(MealPots.CookingSeconds);
        }

        TaskBase TaskFor(string kind)
        {
            switch (kind)
            {
                case OrderKinds.Secure: return pots;
                default: return null;
            }
        }

        /// <summary>Giao lệnh cho trạm (trạm nồi cao nhận hai loại lệnh).</summary>
        void OrderStation(OrderData o)
        {
            var station = StationFor(o.Task);
            if (station is TallPotStation t) t.Order(o.Task, o.Count);
            else station?.OnOrder(o.Count);
        }

        /// <summary>Sau lượt này, còn lượt cọ nồi cao trước bữa nấu nồi cao kế tiếp trong ngày không.</summary>
        bool ScrubTallAhead(PhaseData after)
        {
            int i = Day.Phases.IndexOf(after);
            for (int k = i + 1; k < Day.Phases.Count; k++)
            {
                var p = Day.Phases[k];
                if (p.Orders.Exists(o => o.Task == OrderKinds.ScrubTall)) return true;
                if (p.CookTall) return false;
            }
            return false;
        }

        IEnumerator RunTaskOrder(TaskBase task, string kind)
        {
            if (kind == OrderKinds.Secure)
                foreach (var pot in pots.pots)
                {
                    pot.enabled = true;
                    foreach (var collider in pot.GetComponentsInChildren<Collider>()) collider.enabled = true;
                }
            task.Activate();
            while (task.State != TaskState.Done)
                yield return null;
            Board.Report(kind, 1);
        }

        /// <summary>Sim: làm xong mọi lệnh của lượt (giữ đúng đường đi của sự kiện như khi người chơi tự làm).</summary>
        void SimulatePhase()
        {
            if (Phase.NeedsFire || Board.HasOpen(OrderKinds.Fire)) PrimeMontageFire();
            foreach (var o in Board.Orders)
            {
                var task = TaskFor(o.Kind);
                if (task != null)
                {
                    if (task.State != TaskState.Done)
                        task.ForceComplete();
                    continue;
                }
                var station = StationFor(o.Kind);
                if (station == null)
                    continue;
                for (int guard = 0; guard < 50 && !o.IsDone; guard++)
                    station.SimulateWork();
            }
        }

        WorkStation StationFor(string kind)
        {
            switch (kind)
            {
                case OrderKinds.Fire: return coal;
                case OrderKinds.Sack: return sacks;
                case OrderKinds.Wash: return wash;
                case OrderKinds.Ash: return ash;
                case OrderKinds.Sweep: return sweep;
                case OrderKinds.Greens: return greens;
                case OrderKinds.Pan: return pan;
                case OrderKinds.TallOut:
                case OrderKinds.ScrubTall: return tall;
                case OrderKinds.Haul: return haul;
                default: return System.Array.Find(storyStations, s => s.Kind == kind);
            }
        }

        string LabelFor(Order o) => Vocabulary.Render(S(OrderKinds.LabelKey(o.Kind)));

        void RefreshBoard()
        {
            Root.UI.Shift.MoreFormat = S("hud.more_orders");
            Root.UI.Shift.SetOrders(Board.Orders, LabelFor);
            board?.SetContent(Day.Title, Board.Orders, LabelFor);
        }

        IEnumerator EndShift()
        {
            Beat = $"D{Day.Number}.end";
            InShift = false;
            Clock.Running = false;
            Phase = null;
            // F04: làm tới 9 giờ tối; trời ngoài ô cửa sổ theo đồng hồ ca.
            Clock.Minutes = StudyPlanner.EndOfShift(Clock.Minutes, 0);
            _eveningMinutes = StudyPlanner.EveningStart(Clock.Minutes);
            Root.UI.Shift.SetPointer(null, 0f);
            cast?.EndShift();
            foreach (var s in Stations())
            {
                s.Worked -= OnWorked;
                s.Close();
            }
            Board.Clear();
            Root.UI.Shift.Show(false);
            Root.Events.Raise(EventIds.S2ShiftEnd);
            Root.Subtitles.Play("chef_shift_end");
            if (storm.Active)
            {
                storm.Calm(4f);
                Root.Events.Raise(EventIds.S2StormCalm);
            }
            if (_body != null)
                Root.State.Fatigue = _body.Logic.Fatigue;
            yield return new WaitForSeconds(2f);

            BeginCutscene();
            if (!_dark)
                yield return Root.Scenes.Fade(0f, 1f, 1.2f);
            _dark = false;
            Root.UI.SetFade(0f);
            Root.UI.Card.Blackout();
            foreach (var go in dayOnly)
                if (go != null)
                    go.SetActive(false);
            foreach (var go in eveningOnly)
                if (go != null)
                    go.SetActive(true);
            lights?.SetInterior(coal.fireLight, eveningLamp);
            if (Rig != null && eveningSpawn != null)
            {
                Rig.motor.Teleport(eveningSpawn.position, eveningSpawn.eulerAngles.y);
                Rig.look.SetAngles(eveningSpawn.eulerAngles.y, 18f);
            }
            ship.Intensity = Mathf.Min(Day.Ship, 0.12f);
            // Tối: sau giờ tan ca một tiếng.
            Clock.Minutes = _eveningMinutes;
            wallClock?.Set(Clock.Minutes);
            yield return Root.UI.Card.Show($"{S("card.time_evening")} · {ShiftClockLogic.Format(_eveningMinutes)}", 3f);
            Root.UI.Card.Hide();
            EndCutscene();
        }

        /// <summary>
        /// Buổi tối (F04, F07a: tối đọc, viết tới 11 giờ hoặc nửa đêm): người dạy ngồi ở góc đèn dầu, mở đầu bằng thoại của
        /// họ, rồi bài học trong sổ theo kế hoạch (tan ca muộn thì ít mục hơn, từ còn lại để tối sau); xong bài là thoại sau
        /// buổi học (Ngày 6: lính cho mượn sách), rồi ngồi nghỉ hết ngày.
        /// </summary>
        public Transform letterChair, letterLook;
        public BlankPaper blankPaper;
        /// <summary>Đèn bến Colombo xa, chỉ tối ngày 3 (tàu đậu ngoài Colombo).</summary>
        public GameObject colomboLights;
        IEnumerator Evening()
        {
            Beat = $"D{Day.Number}.evening";
            var ev = Day.Evening;
            people?.Evening(Day.Number);
            if (colomboLights != null) colomboLights.SetActive(Day.Number == 3);
            if (ev != null && ev.Coffee)
            {
                cast?.PrepareCoffee();
                Root.Subtitles.Play("ba_coffee");
                var coffee = StationFor(OrderKinds.Coffee);
                coffee.Activate(); Board.Add(OrderKinds.Coffee, 1);
                coffee.Worked += OnWorked; coffee.OnOrder(1);
                Root.UI.Shift.Show(true);
                yield return WaitFor(() => Board.AllDone, () => coffee.SimulateWork());
                coffee.Worked -= OnWorked; coffee.Close();
                Root.UI.Shift.Show(false); Board.Clear();
                Root.Events.Raise(EventIds.S2CoffeeDone);
            }
            var teach = cast != null ? cast.BeginEvening() : null;
            if (ev != null && ev.HasStudy)
            {
                if (!string.IsNullOrEmpty(ev.Line))
                    yield return Root.Subtitles.PlayAndWait(ev.Line);
                if (teach != null && cast != null)
                    yield return cast.Say(teach.Lines);
                StudyPlan = StudyPlanner.Make(Root.Content, Root.State, ev, _eveningMinutes);
                if (!string.IsNullOrEmpty(ev.StoryLesson))
                {
                    if (Day.Number == 5)
                    {
                        yield return Root.Subtitles.PlayAndWait("ba_viet_teach");
                        yield return Root.Subtitles.PlayAndWait("soldier_viet");
                    }
                    else yield return Root.Subtitles.PlayAndWait("ba_letter");
                    var lesson = Root.Content.GetLesson(ev.StoryLesson);
                    var story = new StudyPlanner.Plan { Kind = lesson.Kind, Heading = lesson.Heading };
                    story.Items.AddRange(lesson.Items);
                    var oldChair = study.chair; var oldLook = study.studyLookAt;
                    if (Day.Number == 6) { study.chair = letterChair; study.studyLookAt = letterLook; }
                    study.Begin(story); yield return TaskRunner.Run(study);
                    study.chair = oldChair; study.studyLookAt = oldLook;
                    // DIRECTOR §6: ngày 5 người lính già đọc lại chữ "má" rồi nói khẽ; ngày 6 người nhờ viết thư gọi về bàn bài.
                    if (Day.Number == 5) yield return Root.Subtitles.PlayAndWait("linh_d5_mere");
                    else if (Day.Number == 6)
                    {
                        yield return Root.Subtitles.PlayAndWait("ban_viet_cards");
                        blankPaper?.Offer();
                    }
                }
                if (StudyPlan.MainCount < Mathf.Max(1, ev.Items) && StudyPlan.Deferred.Count > 0)
                    Root.UI.Prompt.ShowHint(string.Format(S("study.late"), StudyPlan.MainCount), 6f);
                if (study != null)
                {
                    study.Begin(StudyPlan);
                    yield return TaskRunner.Run(study);
                }
                else
                {
                    StudyPlanner.Finish(StudyPlan, Root.State);
                    Root.Events.Raise(EventIds.S2WordsDone);
                }
                if (cast != null)
                    yield return cast.Say(ev.After);
                if (ev.Book)
                    Root.Events.Raise(EventIds.S2BookLent);
            }
            if (ev != null && ev.HasStudy)
            {
                Clock.Minutes = Mathf.Max(23f * 60f, _eveningMinutes + 60f);
                wallClock?.Set(Clock.Minutes);
                yield return Root.UI.Card.Show($"{S("card.study_late")} · {ShiftClockLogic.Format(Clock.Minutes)}", 3f);
                Root.UI.Card.Hide();
            }
            if (ev != null) foreach (var e in ev.Events) Root.Events.Raise(e);
            if (ev != null)
                foreach (var c in ev.Cards)
                    Root.UI.Cards.Show(c);
            yield return Rest();
        }

        /// <summary>Ngồi nghỉ ở góc đèn dầu để hết ngày (người chơi tự chọn lúc nào).</summary>
        IEnumerator Rest()
        {
            Beat = $"D{Day.Number}.rest";
            if (restSeat == null)
                yield break;
            _rested = false;
            restSeat.available = true;
            _resting = true;
            restSeat.Interacted += OnRest;
            float start = Time.time;
            bool hinted = false;
            yield return WaitFor(() =>
            {
                if (!hinted && Time.time - start > restHintSeconds)
                {
                    hinted = true;
                    Root.UI.Prompt.ShowHint(S("hint.rest"), 8f);
                }
                return _rested;
            }, () => _rested = true);
            restSeat.Interacted -= OnRest;
            restSeat.available = false;
            _resting = false;
            Root.UI.Guide.Clear();
        }

        void OnRest(Interactor who)
        {
            _rested = true;
        }

        void NextDay()
        {
            Finished = true;
            BeginCutscene();
            if (_body != null)
                Root.State.Fatigue = _body.Logic.Fatigue;
            Root.State.AdvanceDay();
            var next = Root.Content.GetDay(Root.State.Day);
            if (next != null)
            {
                Root.Scenes.LoadWithTransition(SceneFlow.GameScenes[1], next.Transition);
                return;
            }
            Root.State.Day = Day.Number;
            Root.Scenes.LoadWithTransition(SceneFlow.GameScenes[2], "B");
        }

        void OnDestroy()
        {
            Board.Changed -= RefreshBoard;
            if (Rig != null && Rig.motor != null)
                Rig.motor.SpeedCap = float.PositiveInfinity;
            if (Root != null && Root.UI != null)
                Root.UI.Shift.Clear();
            if (_body != null)
                _body.Stop();
        }
    }
}
