using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Player;
using HaiBanTay.Tasks;
using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Rửa nồi (F03): sau mỗi lượt phục vụ, nồi bẩn dồn về chồng cạnh chậu (bếp nấu cho khoảng 700–800 người, F04).
    /// E ở chồng nồi → một chiếc vào chậu; lấy bàn chải ở kệ (lần sau tay tự lấy lại ở mép chậu); giữ chuột trái và rê chuột: đầu bàn chải
    /// chà đúng chỗ con trỏ trên đáy nồi, vết bẩn chỉ mất ở chỗ đã chà, tay trái xoay dần nồi cho phần xa về phía người (BrushScrub).
    /// Sạch thì tráng, rồi E để úp lên giá (cần hai tay: bàn chải đặt lên mép chậu).
    /// </summary>
    public sealed class WashStation : WorkStation
    {
        public MealPots meals;
        public TaskTarget pile;
        public TaskTarget basin;
        public TaskTarget rack;
        /// <summary>Hình chồng nồi bẩn, hiện theo số nồi chờ (tối đa bằng số phần tử).</summary>
        public GameObject[] pileVisuals = new GameObject[0];
        public GameObject basinPot;
        [Tooltip("Móc/kệ bàn chải ngắn cạnh chậu (Tool.Brush).")]
        public ToolHook brushHook;
        [Tooltip("Bàn chải nằm trên mép chậu (hình), hiện khi Ba đặt nó xuống để bê nồi bằng hai tay.")]
        public GameObject brushOnRim;
        [Tooltip("Vật liệu lớp bẩn có mặt nạ cọ (shader HaiBanTay/DirtOverlay).")]
        public Material dirtMaterial;
        [Tooltip("Vòi nước trên chậu (QA-0132): phải gạt cho nước chảy mới cọ, tráng được. Không có vòi thì coi như nước sẵn.")]
        public WashTap tap;
        public TaskTarget tapTarget;
        /// <summary>Tốc độ tay trái xoay nồi trong chậu khi đang chà (độ/giây).</summary>
        public float spinDegreesPerSecond = 22f;
        /// <summary>Nồi sạch trên giá, hiện vòng theo số đã rửa.</summary>
        public GameObject[] rackVisuals = new GameObject[0];
        [Tooltip("Chỗ úp nồi trên giá (gốc ở giữa vành nồi úp trên mặt kệ, quay theo hướng quai). MealPots xếp nồi sạch vào chỗ trống đầu tiên.")]
        public Transform[] rackSlots = new Transform[0];
        public float exertPerPot = 0.012f;
        /// <summary>Chỗ đứng cọ (z theo chậu): bụng sát thành trước của chậu (mặt ngoài ở −0,3175).</summary>
        public const float StandBack = -0.52f;
        /// <summary>Chậu kê cao hơn bồn thường (m) để cọ đứng thẳng (luật camera 17/10).</summary>
        public const float BasinLift = 0.2f;
        /// <summary>Tâm nồi trong chậu (z theo chậu): sát thành gần, vành nồi cách thành trong 2 cm.</summary>
        public const float PotNear = -0.06f;

        GameRoot _root;
        PlayerRig _rig;
        bool _scrubbing, _rinsing;
        GameObject _fallbackPot, _currentMealPot;
        // Va chạm cứng của nồi đang bưng tắt tạm, bật lại khi đặt vào bồn: để bật thì nồi đẩy Ba lùi (QA-0144).
        readonly System.Collections.Generic.List<Collider> _carriedSolid = new System.Collections.Generic.List<Collider>();
        Transform _potParent, _leftGrip, _wrist, _brush;
        BrushScrub _scrub;
        GameObject _scrubPot;
        readonly System.Collections.Generic.Dictionary<GameObject, BrushScrub> _scrubs = new System.Collections.Generic.Dictionary<GameObject, BrushScrub>();
        /// <summary>Bàn chải đang nằm trên mép chậu (Ba đặt xuống để bê nồi).</summary>
        public bool BrushOnRim { get; private set; }
        /// <summary>Phép cọ của nồi đang ở chậu (test đo đầu chải, tầm với).</summary>
        public BrushScrub Scrubbing => _scrub;
        Vector3 _potHome;
        Quaternion _potRotation;
        public bool CarryingPot { get; private set; }
        public float RinseProgress { get; private set; }
        public bool Rinsed => RinseProgress >= 1f;

        public override string Kind => OrderKinds.Wash;
        public WashLogic Logic { get; } = new WashLogic();

        void Start()
        {
            _root = GameRoot.Instance;
            _fallbackPot = basinPot;
            _potParent = basinPot.transform.parent;
            _potHome = basinPot.transform.localPosition;
            _potRotation = basinPot.transform.localRotation;
            _leftGrip = new GameObject("WashLeftGrip").transform;
            _leftGrip.SetParent(basin.transform, false);
            // Cổ tay phải bám theo cán bàn chải (BrushScrub đặt mỗi khung); không là con của chậu vì nồi xoay.
            _wrist = new GameObject("WashBrushWrist").transform;
            _wrist.SetParent(transform, false);
            if (brushOnRim != null) brushOnRim.SetActive(false);
            Refresh();
        }

        protected override void OnActivated()
        {
            _rig = PlayerRig.Current;
        }

        public void AddDirty(int count)
        {
            Logic.AddDirty(count);
            Refresh();
        }

        public void RemoveDirty(int count)
        {
            Logic.RemoveDirty(count);
            Refresh();
        }

        /// <summary>Tay đang cầm bàn chải ngắn, hoặc tay trống mà bàn chải nằm sẵn trên mép chậu (lấy lại khi bắt đầu chà).</summary>
        bool HasBrush(Interactor who) => who.Tool == Tool.Brush || (BrushOnRim && who.HandsFree);
        /// <summary>Bê nồi cần hai tay: tay trống, hoặc đang cầm bàn chải (đặt lên mép chậu trước).</summary>
        static bool FreeForPot(Interactor who) => who.HandsFree || (who.Tool == Tool.Brush && who.Holding == null && !who.WorkCarrying);

        /// <summary>Nước đang chảy vào chậu (hoặc chậu không có vòi).</summary>
        public bool WaterRunning => tap == null || tap.Running;

        /// <summary>Gạt vòi (người chơi, bot, test): nước chảy vào chậu.</summary>
        public void OpenTap()
        {
            if (tap == null || tap.Running) return;
            tap.SetRunning(true);
            _root?.Audio.PlaySfxAt("SFX/water_wash", tap.transform.position, 0.3f);
        }

        void CloseTap()
        {
            if (tap != null) tap.SetRunning(false);
        }

        public override string PromptFor(TaskTarget target, Interactor who)
        {
            if (target != null && target == tapTarget)
                return tap == null || CarryingPot ? null : tap.Running ? "prompt.tap_close" : "prompt.tap_open";
            if (target == basin && !CarryingPot && Logic.InBasin && !WaterRunning && !(Logic.BasinClean && Rinsed))
                return "prompt.need_water";
            if (target == pile && Logic.Pending > 0 && !Logic.InBasin)
                return FreeForPot(who) ? "prompt.take_pot" : "prompt.hands_busy";
            if (target == basin && CarryingPot) return "prompt.place_pot";
            if (target == basin && Logic.BasinClean && !Rinsed) return "prompt.rinse_pot";
            if (target == basin && Logic.InBasin && !Logic.Scrub.Done)
                return HasBrush(who) ? "prompt.wash" : "prompt.need_brush";
            if ((target == basin || target == rack) && Logic.BasinClean && Rinsed)
                return "prompt.rack_pot";
            return null;
        }

        public override bool CanInteract(TaskTarget target, Interactor who)
        {
            if (target != null && target == tapTarget)
                return tap != null && !CarryingPot;
            if (target == pile)
                return Logic.Pending > 0 && !Logic.InBasin && FreeForPot(who);
            if (target == basin && CarryingPot) return true;
            return (target == basin || target == rack) && Logic.BasinClean && Rinsed;
        }

        /// <summary>Đặt bàn chải đang cầm lên mép chậu (hai tay rảnh để bê nồi); lần chà sau tay lấy lại ở đó.</summary>
        void PutBrushOnRim(Interactor who)
        {
            if (who == null || who.Tool != Tool.Brush)
                return;
            EndScrub();
            who.ReleaseTool();
            BrushOnRim = true;
            if (brushOnRim != null) brushOnRim.SetActive(true);
        }

        /// <summary>Lấy bàn chải trên mép chậu vào tay phải.</summary>
        bool TakeBrushFromRim(Interactor who)
        {
            if (!BrushOnRim || !who.HandsFree || !who.TakeTool(Tool.Brush))
                return false;
            BrushOnRim = false;
            if (brushOnRim != null) brushOnRim.SetActive(false);
            _root?.Audio.PlaySfx("SFX/sweep", 0.3f);
            return true;
        }

        /// <summary>Hết lượt rửa: bàn chải (trong tay hay trên mép chậu) về kệ.</summary>
        void ReturnBrush()
        {
            EndScrub();
            var who = _rig != null ? _rig.interactor : PlayerRig.Current != null ? PlayerRig.Current.interactor : null;
            if (brushHook != null && (BrushOnRim || (who != null && who.Tool == Tool.Brush) || !brushHook.Hanging))
                brushHook.ReturnFrom(who);
            BrushOnRim = false;
            if (brushOnRim != null) brushOnRim.SetActive(false);
        }

        public override void OnInteract(TaskTarget target, Interactor who)
        {
            if (target != null && target == tapTarget)
            {
                if (tap == null) return;
                if (tap.Running)
                {
                    EndScrub();
                    CloseTap();
                    _root?.Audio.PlaySfx("SFX/metal_clank", 0.25f);
                }
                else OpenTap();
                return;
            }
            if (target == pile)
            {
                PutBrushOnRim(who);
                if (Logic.TakeToBasin())
                {
                    _currentMealPot = meals != null ? meals.TakeDirty() : null;
                    if (_currentMealPot != null) basinPot = _currentMealPot;
                    CarryingPot = true;
                    _rig.interactor.WorkCarrying = _rig.motor.Carrying = true;
                    RinseProgress = 0f;
                    _carriedSolid.Clear();
                    foreach (var c in basinPot.GetComponentsInChildren<Collider>())
                        if (!c.isTrigger) { _carriedSolid.Add(c); c.enabled = false; }
                    basinPot.transform.SetParent(_rig.hands.transform, false);
                    basinPot.transform.localPosition = new Vector3(0f, -0.36f, 0.55f);
                    _rig.hands.SetPose(HandPose.Carry);
                    _root?.Audio.PlaySfx("SFX/metal_clank");
                    ReportProgress();
                }
            }
            else if (target == basin && CarryingPot)
            {
                CarryingPot = false;
                _rig.interactor.WorkCarrying = _rig.motor.Carrying = false;
                basinPot.transform.SetParent(_potParent, false);
                basinPot.transform.SetLocalPositionAndRotation(_potHome, _potRotation);
                foreach (var c in _carriedSolid) if (c != null) c.enabled = true;
                _carriedSolid.Clear();
                _rig.hands.SetPose(HandPose.Lowered);
                _root?.Audio.PlaySfx("SFX/metal_clank");
                PrepareScrub(basinPot);
            }
            else if (Rinsed && Logic.Rack())
            {
                PutBrushOnRim(who);
                if (_scrub != null) _scrub.Clean();
                if (_currentMealPot != null)
                {
                    meals.Rack(_currentMealPot, Logic.Washed - 1);
                    _currentMealPot = null;
                    basinPot = _fallbackPot;
                }
                _root?.Audio.PlaySfx("SFX/metal_clank");
                BodyState.Current?.Exert(exertPerPot * 0.3f);
                Work(1);
            }
            Refresh();
        }

        /// <summary>Kích thước nồi (đường kính, cao) đọc từ mesh thân (ChoreProps.Pot: thành dày 1,2 cm, đáy trong cao 2,5 cm).</summary>
        public static (float diameter, float height) PotSize(GameObject pot)
        {
            var body = pot.transform.Find("Body");
            var mf = body != null ? body.GetComponent<MeshFilter>() : pot.GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return (0.36f, 0.3f);
            var b = mf.sharedMesh.bounds;
            return (b.size.x * mf.transform.lossyScale.x, b.max.y * mf.transform.lossyScale.y);
        }

        /// <summary>Phép cọ cho nồi vừa đặt vào chậu: mặt đáy, lớp bẩn theo đúng cỡ nồi đó (nồi bữa ăn nhiều cỡ).</summary>
        void PrepareScrub(GameObject pot)
        {
            if (pot == null)
                return;
            if (!_scrubs.TryGetValue(pot, out var scrub))
            {
                var (d, h) = PotSize(pot);
                // Đầu chải không thò qua thành nồi (QA-0136) do BrushReach.Head lo: tư thế nào khối gỗ lọt qua thành thì không đạt, con trỏ dừng ở
                // chỗ còn đạt gần nhất.
                var surface = new ScrubSurface(d * 0.45f - 0.012f, 0.025f, 0f, BrushShape.Short.HeadRadius * 0.6f);
                scrub = new BrushScrub(pot.transform, surface, BrushShape.Short, d * 0.5f - 0.012f, d * 0.5f, h, d * 0.45f - 0.012f)
                {
                    SpinDegreesPerSecond = spinDegreesPerSecond
                };
                if (dirtMaterial != null) scrub.CreateOverlay(dirtMaterial);
                _scrubs[pot] = scrub;
            }
            scrub.Soil();
            _scrub = scrub;
            _scrubPot = pot;
            // Tay trái giữ vành nồi phía trước bên trái (điểm cố định trong chậu, nồi xoay dưới lòng bàn tay).
            var (diameter, height) = PotSize(pot);
            var centre = basin.transform.InverseTransformPoint(pot.transform.position);
            var dir = new Vector3(-0.62f, 0f, -0.78f);
            _leftGrip.localPosition = centre + dir * (diameter * 0.5f + 0.005f) + Vector3.up * (height + 0.012f);
            _leftGrip.localRotation = Quaternion.Euler(0f, 10f, 80f);
        }

        /// <summary>Thôi chà: bàn chải về nắm tay phải, tay thả điểm bám.</summary>
        void EndScrub()
        {
            if (!_scrubbing)
                return;
            _scrubbing = false;
            if (_rig != null)
            {
                _rig.motor.MovementLocked = false;
                _rig.look.Locked = false;
                _rig.hands.SetWorkGrips(null, null);
                _rig.hands.AttachBrush(Tool.Brush);
                if (_rig.interactor.Tool == Tool.Brush) _rig.hands.SetPose(HandPose.Grip);
            }
            _brush = null;
            _root?.UI.Prompt.SetProgress(null);
        }

        protected override void Update()
        {
            base.Update();
            if (State != TaskState.Active || _rig == null || _root == null)
                return;
            var who = _rig.interactor;
            bool scrubbing = false;
            if (!CarryingPot && Logic.InBasin && !Logic.Scrub.Done && _scrub != null && HasBrush(who) && who.Holding == null && !who.WorkCarrying
                && WaterRunning)
            {
                bool held = _root.Input.UseHeld;
                // Đã bắt đầu cọ thì camera khóa: không bắt nhìn lại chậu mỗi khung (tia nhìn trượt làm trạng thái cọ đảo liên tục).
                bool onBasin = _scrubbing || who.IsLookingAt(basin);
                scrubbing = held && onBasin;
                if (scrubbing && who.Tool != Tool.Brush)
                    scrubbing = TakeBrushFromRim(who);
            }
            if (scrubbing && !_scrubbing)
            {
                _scrubbing = true;
                // Bước sát mép chậu (tốc độ đi bộ, giữ hướng nhìn và chiều cao mắt) để chỗ nắm cán nằm trong tầm tay khi đứng thẳng.
                _rig.StepToWork(basin.transform.TransformPoint(new Vector3(0f, 0f, StandBack)));
                _root.Audio.PlaySfxAt("SFX/water_wash", basin.transform.position, 0.5f);
                _rig.motor.MovementLocked = true;
                // Đang cọ: chuột dùng để cọ, không xoay camera.
                _rig.look.Locked = true;
                _scrub.Restart();
                _scrub.AimReachable(who.LookPoint, _rig);
            }
            else if (!scrubbing && _scrubbing)
                EndScrub();
            if (_scrubbing && !_rig.Stepping)
            {
                if (_brush == null)
                {
                    // Bước xong mới đặt bàn chải lên mặt cọ và cho tay bám theo cán (đang bước thì tay giữ nguyên dáng cầm).
                    _brush = _rig.hands.DetachBrush(Tool.Brush);
                    _scrub.Restart();
                    _scrub.AimReachable(who.LookPoint, _rig);
                    _scrub.Tick(_rig, false, Vector2.zero, 0f, Vector3.right, Vector3.forward);
                    _scrub.Place(_brush, _wrist, null);
                    _rig.hands.SetWorkGrips(_leftGrip, _wrist, HandPose.Grip);
                }
                var delta = _root.Input.GameplayEnabled ? _root.Input.LookRaw : Vector2.zero;
                var cam = _rig.cam.transform;
                var right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
                var forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                if (_scrub.Tick(_rig, true, delta, Time.deltaTime, right, forward))
                {
                    BodyState.Current?.Exert(exertPerPot * 0.7f * Time.deltaTime * 2f);
                    Logic.Scrub.ForceProgress(_scrub.Progress);
                    ReportProgress();
                    if (Logic.Scrub.Done)
                    {
                        _scrub.Clean();
                        if (_currentMealPot != null) meals.Mark(_currentMealPot, false);
                        EndScrub();
                        Refresh();
                    }
                }
                _scrub.Place(_brush, _wrist, null);
                if (_scrubbing) _root.UI.Prompt.SetProgress(Logic.Scrub.Progress);
            }
            bool rinsing = false;
            if (!CarryingPot && Logic.BasinClean && !Rinsed && WaterRunning)
            {
                // Đã bắt đầu tráng thì giữ chuột là đủ, tia nhìn trượt khỏi chậu không làm tráng ngắt quãng.
                rinsing = _root.Input.UseHeld && (_rinsing || who.IsLookingAt(basin));
                if (rinsing)
                {
                    RinseProgress = Mathf.Min(1f, RinseProgress + Time.deltaTime / 1.2f);
                    _root.UI.Prompt.SetProgress(RinseProgress);
                    if (!_rinsing) _root.Audio.PlaySfxAt("SFX/water_wash", basin.transform.position, 0.3f);
                    ReportProgress();
                }
            }
            if (_rinsing && !rinsing && !_scrubbing)
                _root.UI.Prompt.SetProgress(null);
            _rinsing = rinsing;
        }

        public override bool Engaged(Interactor who) => CarryingPot || Logic.InBasin;

        public override bool Guide(Interactor who, out Transform target, out string key)
        {
            if (CarryingPot)
                return Step(basin, "guide.wash_place", out target, out key);
            // Nồi đã trong chậu mà nước chưa chảy: gạt vòi trước (QA-0132).
            if (Logic.InBasin && !WaterRunning && tapTarget != null && !(Logic.BasinClean && Rinsed))
                return Step(tapTarget, "guide.wash_tap", out target, out key);
            if (Logic.InBasin && !Logic.Scrub.Done)
            {
                // Chưa có bàn chải trong tay hay trên mép chậu: lấy ở kệ cạnh chậu trước.
                if (!HasBrush(who) && brushHook != null && brushHook.Hanging)
                    return Step(brushHook.transform, "guide.wash_brush", out target, out key);
                return Step(basin, "guide.wash_scrub", out target, out key);
            }
            if (Logic.BasinClean && !Rinsed)
                return Step(basin, "guide.wash_rinse", out target, out key);
            if (Logic.BasinClean && Rinsed)
                return Step(rack, "guide.wash_rack", out target, out key);
            return Step(pile, Logic.Pending > 0 ? "guide.wash_take" : null, out target, out key);
        }

        public override void SimulateWork()
        {
            if (Logic.Pending == 0 && !Logic.InBasin)
            {
                if (meals != null) return;
                Logic.AddDirty(1);
            }
            RinseProgress = 1f;
            var simulatedPot = _currentMealPot != null ? _currentMealPot : meals != null ? meals.TakeDirty() : null;
            if (Logic.CompleteOne())
            {
                _scrub?.Clean();
                if (simulatedPot != null) meals.Rack(simulatedPot, Logic.Washed - 1);
                foreach (var c in _carriedSolid) if (c != null) c.enabled = true;
                _carriedSolid.Clear();
                _currentMealPot = null; basinPot = _fallbackPot; CarryingPot = false;
                if (_rig != null) _rig.interactor.WorkCarrying = _rig.motor.Carrying = false;
                Work(1);
            }
            Refresh();
        }

        protected override void OnCompleted()
        {
            ReturnBrush();
            // Rửa hết lượt: Ba khóa vòi.
            CloseTap();
            _root?.UI.Prompt.SetProgress(null);
        }

        void Refresh()
        {
            for (int i = 0; i < pileVisuals.Length; i++)
                if (pileVisuals[i] != null)
                    pileVisuals[i].SetActive(meals == null && i < Logic.Pending);
            // Hộp va chạm của chồng nồi chỉ đặc khi còn nồi chờ rửa; hết nồi thì là trigger, không thành tường vô hình (QA-0030).
            if (pile != null && pile.TryGetComponent<Collider>(out var pileCollider))
                pileCollider.isTrigger = Logic.Pending == 0;
            if (basinPot != null)
                basinPot.SetActive(Logic.InBasin && (meals == null || _currentMealPot != null));
            for (int i = 0; i < rackVisuals.Length; i++)
                if (rackVisuals[i] != null)
                    rackVisuals[i].SetActive(meals == null && Logic.Washed > 0 && i < Mathf.Min(Logic.Washed, rackVisuals.Length));
            if (_currentMealPot != null && Logic.Scrub.Done) meals.Mark(_currentMealPot, false);
            // Lớp bẩn chỉ hiện trên nồi đang ở chậu và chưa sạch (nồi nằm chồng, trên giá dùng màu bẩn/sạch của MealPots).
            foreach (var pair in _scrubs)
                if (pair.Value.Overlay != null)
                    pair.Value.Overlay.gameObject.SetActive(pair.Key == _scrubPot && Logic.InBasin && !Logic.Scrub.Done);
        }
    }
}
