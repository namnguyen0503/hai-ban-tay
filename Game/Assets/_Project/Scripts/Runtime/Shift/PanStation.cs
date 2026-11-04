using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Ship;
using HaiBanTay.Tasks;
using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Kéo lê chảo đồng (F04): chảo quá nặng để nhấc. Nhìn vào chảo, giữ chuột trái để bám, đi lùi; chảo cạ sàn theo sau,
    /// người đi chậm hẳn (0,6 m/s). Tàu lắc đẩy chảo trượt ngang (TowLogic); quay mặt khỏi chảo hay để chảo kẹt thì tuột tay.
    /// Lệnh kéo chảo đổi chiều giữa chỗ đặt trước lò và chỗ rửa; mốc chỉ dẫn trên màn hình chỉ chỗ cần tới (không vẽ gì dưới sàn). Đường kéo dùng chung với nồi cao
    /// (VesselTow). Chảo bẩn ở chỗ rửa: lấy bàn chải cán dài ở móc góc nồi cao, đứng thẳng, hai tay trên cán mà chà lòng chảo (BrushScrub),
    /// không ngồi xổm (luật camera 17/10).
    /// </summary>
    public sealed class PanStation : WorkStation
    {
        public Rigidbody pan;
        public TaskTarget handle;
        /// <summary>Hai chỗ đặt chảo: [0] trước lò, [1] khu rửa. Chảo bắt đầu ở [1].</summary>
        public Transform[] stops = new Transform[2];
        public float towSpeed = 0.6f;
        public float arriveRadius = 0.5f;
        public float exertPerSecond = 0.006f;
        public float scrapeEvery = 0.6f;
        [Tooltip("Móc bàn chải cán dài (góc nồi cao).")]
        public ToolHook longBrushHook;
        [Tooltip("Vật liệu lớp bẩn có mặt nạ cọ (shader HaiBanTay/DirtOverlay).")]
        public Material dirtMaterial;
        [Tooltip("Chỗ đặt chảo trên bếp (builder tính: cạnh ô bếp, quai chĩa trước–sau, không chạm nồi cao ở ô kế bên). Không có thì theo chỗ cũ.")]
        public Transform rangeSpot;
        [Tooltip("Vật trên lò bắc nằm chỗ chảo chiếm (ấm nước sôi): quản bếp dẹp sang bên khi chảo lên bếp, đặt lại khi chảo xuống.")]
        public GameObject[] clearWhileOnRange = new GameObject[0];
        /// <summary>Hình chảo (GalleyChores.BuildPan, QA-0146): đáy trong ở BottomY, bán kính đáy phẳng BottomRadius; thành loe tới vành
        /// RimInner/RimOuter ở cao RimY (hệ chảo, gốc ở sàn).</summary>
        public const float BottomY = 0.052f, BottomRadius = 0.295f, RimInner = 0.375f, RimOuter = 0.395f, RimY = 0.164f;
        public const float PanScrubMargin = 0.06f;
        /// <summary>Chỗ đứng chà: tâm chảo cách người chừng này (m), đứng thẳng, hai tay trên cán dài.</summary>
        public const float ScrubStand = 0.95f;
        /// <summary>
        /// Tâm vòng phấn cách chân người kéo ít nhất ngần này thì mới giao chảo: nửa hộp chảo 0,4 + bán kính người 0,28 + dư. Chảo trượt
        /// vào tâm vòng khi giao; người còn đứng trong hộp chảo thì Ba bước lên mép chảo, camera nhô 0,24 m.
        /// </summary>
        const float PlayerClear = 0.72f;
        /// <summary>Người kẹt sau lưng không lùi được (sát tường): sau chừng này giây giao chảo luôn, chảo dừng cách chân đúng PlayerClear.</summary>
        const float CrowdedWait = 1.5f;

        GameRoot _root;
        PlayerRig _rig;
        int _pending;
        bool _slipHinted, _cookRequested, _dirty, _washing, _rinsing;
        float _crowded;
        Renderer[] _surfaces;
        ParticleSystem[] _steam;
        MaterialPropertyBlock _dirtBlock;
        VesselTow _tow;
        BrushScrub _brushScrub;
        Transform _brush, _lowWrist, _highWrist;
        public ScrubLogic Scrub { get; } = new ScrubLogic();
        /// <summary>Phép cọ lòng chảo (test đo đầu chải).</summary>
        public BrushScrub Scrubbing => _brushScrub;
        public float RinseProgress { get; private set; }
        public bool NeedsWash { get; private set; }

        public void BeginCooking()
        {
            _cookRequested = true;
            if (Next == 1 && !OnRange) StartCoroutine(LiftToRange());
            else if (OnRange) PlaySteam(true);
        }

        /// <summary>Chảo đã nằm trên bếp (nhấc lên ngay khi giao tới vòng trước lò, hoặc khi bắt đầu nấu).</summary>
        bool _clearedForRange;

        public bool OnRange => pan != null && pan.position.y > 0.5f;

        Vector3 RangePosition => rangeSpot != null ? rangeSpot.position : new Vector3(stops[0].position.x, 0.898f, 2.45f);
        Quaternion RangeRotation => rangeSpot != null ? rangeSpot.rotation : pan.rotation;

        void PlaySteam(bool on)
        {
            if (_steam == null) return;
            foreach (var ps in _steam)
            {
                if (on) ps.Play();
                else ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        IEnumerator LiftToRange()
        {
            var from = pan.position;
            var to = RangePosition;
            var fromRot = pan.rotation;
            var toRot = RangeRotation;
            for (float t = 0f; t < 0.8f; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / 0.8f);
                Place(Vector3.Lerp(from, to, k), Quaternion.Slerp(fromRot, toRot, k));
                yield return null;
            }
            Place(to, toRot);
            PlaySteam(_cookRequested);
        }

        public void Served()
        {
            if (!_cookRequested) return;
            _cookRequested = false; _dirty = true;
            foreach (var ps in _steam) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var surface in _surfaces)
            {
                // Thân ngoài sạm khói; lòng chảo bám cặn do lớp bẩn có mặt nạ lo (chỉ mất ở chỗ bàn chải chà qua).
                if (surface.name != "Body") continue;
                surface.GetPropertyBlock(_dirtBlock);
                _dirtBlock.SetColor("_BaseColor", new Color(0.2f, 0.12f, 0.06f));
                surface.SetPropertyBlock(_dirtBlock);
            }
            _brushScrub?.Soil();
        }

        public override string Kind => OrderKinds.Pan;
        public TowLogic Tow => _tow.Logic;
        /// <summary>Chỗ chảo cần tới ở lệnh kế tiếp (chỉ số trong stops).</summary>
        public int Next { get; private set; }
        public int Delivered { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            _dirtBlock = new MaterialPropertyBlock();
            _tow = new VesselTow(pan) { TowSpeed = towSpeed, ExertPerSecond = exertPerSecond, ScrapeEvery = scrapeEvery };
        }

        void Start()
        {
            _root = GameRoot.Instance;
            _surfaces = pan.GetComponentsInChildren<MeshRenderer>();
            _steam = pan.GetComponentsInChildren<ParticleSystem>();
            foreach (var ps in _steam) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            // Lòng chảo: đáy phẳng trong ở BottomY, thành loe tới vành RimY (GalleyChores.BuildPan, QA-0146).
            // Lề đáy: vòng nan tre (bán kính 6,4 cm) ép được 6 mm vào chân thành loe (BrushReach.Head, QA-0136).
            _brushScrub = new BrushScrub(pan.transform, new ScrubSurface(BottomRadius, BottomY, 0f, PanScrubMargin), BrushShape.Long, RimInner, RimOuter, RimY,
                BottomRadius);
            if (dirtMaterial != null) _brushScrub.CreateOverlay(dirtMaterial);
            _brushScrub.Clean();
            _lowWrist = new GameObject("PanBrushWristR").transform;
            _highWrist = new GameObject("PanBrushWristL").transform;
            _lowWrist.SetParent(transform, false);
            _highWrist.SetParent(transform, false);
        }

        void OnDisable()
        {
            if (pan != null)
                NpcAgent.Obstacles.Remove(pan.transform);
        }

        protected override void OnActivated()
        {
            _rig = PlayerRig.Current;
        }

        public override void OnOrder(int count)
        {
            _pending += Mathf.Max(0, count);
        }

        public override bool Engaged(Interactor who) => Tow.Gripping || NeedsWash;

        public override bool Guide(Interactor who, out Transform target, out string key)
        {
            if (NeedsWash)
            {
                if (!Scrub.Done && who.Tool != Tool.LongBrush && longBrushHook != null && longBrushHook.Hanging)
                    return Step(longBrushHook.transform, "guide.pan_brush", out target, out key);
                return Step(handle, Scrub.Done ? "guide.pan_rinse" : "guide.pan_scrub", out target, out key);
            }
            if (_pending <= 0)
                return Step(null, null, out target, out key);
            if (Tow.Gripping)
                return Step(stops[Next], Next == 0 ? "guide.pan_to_stove" : "guide.pan_to_wash", out target, out key);
            return Step(handle, "guide.pan_grip", out target, out key);
        }

        public override string PromptFor(TaskTarget target, Interactor who)
        {
            if (target == handle && NeedsWash)
                return Scrub.Done ? "prompt.rinse_pot" : who.Tool == Tool.LongBrush ? "prompt.scrub" : "prompt.need_long_brush";
            if (target != handle || _pending <= 0 || Tow.Gripping)
                return null;
            return who.HandsFree ? "prompt.tow" : "prompt.hands_busy";
        }

        protected override void Update()
        {
            base.Update();
            bool onRange = OnRange;
            if (onRange != _clearedForRange)
            {
                _clearedForRange = onRange;
                foreach (var go in clearWhileOnRange)
                    if (go != null)
                        go.SetActive(!onRange);
            }
            if (State != TaskState.Active || _rig == null || _root == null || pan == null)
                return;
            var interactor = _rig.interactor;
            bool held = _root.Input.UseHeld;
            if (NeedsWash)
            {
                TickWash(held);
                return;
            }
            if (!Tow.Gripping)
            {
                if (held && _pending > 0 && interactor.HandsFree && interactor.IsLookingAt(handle))
                    BeginTow();
                return;
            }
            if (!held || !interactor.HandsFree)
            {
                EndTow();
                return;
            }

            float moved = _tow.Step(_rig, _root, Time.deltaTime, out bool slipped);
            if (moved > 0.0005f)
                ReportProgress();
            if (slipped)
            {
                EndTow();
                if (!_slipHinted)
                {
                    _slipHinted = true;
                    _root.UI.Prompt.ShowHint(_root.Content.GetString("hint.pan_slip"), 6f);
                }
                return;
            }
            if (Arrived())
                Deliver();
        }

        void Place(Vector3 position, Quaternion rotation) => _tow.Place(position, rotation);

        bool Arrived()
        {
            var stop = stops[Next];
            if (stop == null)
                return false;
            var d = pan.position - stop.position;
            d.y = 0f;
            if (d.magnitude > arriveRadius)
            {
                _crowded = 0f;
                return false;
            }
            // Chảo tới vòng nhưng Ba còn đứng quá sát tâm vòng: chờ Ba lùi thêm (đang đi lùi kéo chảo), không đẩy người.
            if (_rig == null || FlatDistance(_rig.Position, stop.position) >= PlayerClear)
                return true;
            _crowded += Time.deltaTime;
            return _crowded >= CrowdedWait;
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y;
            return Vector3.Distance(a, b);
        }

        /// <summary>Chỗ đặt chảo: tâm vòng, trừ khi người kéo bị kẹt quá gần (hết chỗ lùi) thì cách chân đúng PlayerClear theo hướng vòng – người.</summary>
        Vector3 Settle(Vector3 spot)
        {
            if (_rig == null) return spot;
            var away = spot - _rig.Position;
            away.y = 0f;
            if (away.magnitude >= PlayerClear - 0.02f) return spot;
            if (away.sqrMagnitude < 1e-4f)
            {
                away = pan.position - _rig.Position;
                away.y = 0f;
                if (away.sqrMagnitude < 1e-4f) away = _rig.transform.forward;
            }
            var settled = _rig.Position + away.normalized * PlayerClear;
            settled.y = spot.y;
            return settled;
        }

        void BeginTow()
        {
            if (Next == 1 && pan.position.y > 0.5f) Place(stops[0].position, stops[0].rotation);
            if (!_tow.Begin(_rig, _root))
                return;
            _rig.hands.SetPose(HandPose.Carry);
            ReportProgress();
        }

        void EndTow()
        {
            _tow.End(_rig);
            if (_rig != null && _rig.interactor.HandsFree)
                _rig.hands.SetPose(HandPose.Lowered);
        }

        /// <summary>
        /// Chảo bẩn ở chỗ rửa: cầm bàn chải cán dài, giữ chuột trái nhìn vào chảo thì bước tới chỗ đứng chà (đứng thẳng, camera không cúi),
        /// rê chuột cho đầu chải chà lòng chảo; sạch 90% thì tráng (giữ chuột). Xong thì bàn chải về móc.
        /// </summary>
        void TickWash(bool held)
        {
            var interactor = _rig.interactor;
            if (!Scrub.Done)
            {
                bool washing = held && interactor.Tool == Tool.LongBrush && interactor.Holding == null && (_washing || interactor.IsLookingAt(handle));
                if (washing && !_washing)
                {
                    _washing = true;
                    var away = _rig.Position - pan.position;
                    away.y = 0f;
                    if (away.sqrMagnitude < 1e-4f) away = -_rig.transform.forward;
                    _rig.StepToWork(pan.position + away.normalized * ScrubStand);
                    _rig.motor.MovementLocked = true;
                    _rig.look.Locked = true;
                    _root.Audio.PlaySfxAt("SFX/water_wash", pan.position, 0.5f);
                }
                else if (!washing && _washing)
                    EndWash();
                if (_washing && !_rig.Stepping)
                {
                    if (_brush == null)
                    {
                        _brush = _rig.hands.DetachBrush(Tool.LongBrush);
                        _brushScrub.Restart();
                        _brushScrub.AimReachable(interactor.LookPoint, _rig);
                        _brushScrub.Tick(_rig, false, Vector2.zero, 0f, Vector3.right, Vector3.forward);
                        _brushScrub.Place(_brush, _lowWrist, _highWrist);
                        _rig.hands.SetWorkGrips(_highWrist, _lowWrist, HandPose.Grip, HandPose.Grip);
                    }
                    var cam = _rig.cam.transform;
                    var delta = _root.Input.GameplayEnabled ? _root.Input.LookRaw : Vector2.zero;
                    if (_brushScrub.Tick(_rig, true, delta, Time.deltaTime, Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized,
                            Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized))
                    {
                        Scrub.ForceProgress(_brushScrub.Progress);
                        BodyState.Current?.Exert(exertPerSecond * Time.deltaTime);
                        ReportProgress();
                    }
                    _brushScrub.Place(_brush, _lowWrist, _highWrist);
                    _root.UI.Prompt.SetProgress(Scrub.Progress);
                    if (Scrub.Done)
                    {
                        _brushScrub.Clean();
                        EndWash();
                    }
                }
                return;
            }
            // Tráng: giữ chuột nhìn vào chảo.
            bool rinsing = held && (_rinsing || interactor.IsLookingAt(handle));
            if (rinsing)
            {
                if (!_rinsing) _root.Audio.PlaySfxAt("SFX/water_wash", pan.position, 0.5f);
                RinseProgress = Mathf.Min(1f, RinseProgress + Time.deltaTime / 1.2f);
                _root.UI.Prompt.SetProgress(RinseProgress);
            }
            else if (_rinsing)
                _root.UI.Prompt.SetProgress(null);
            _rinsing = rinsing;
            if (RinseProgress >= 1f)
            {
                NeedsWash = _dirty = false;
                _rinsing = false;
                foreach (var surface in _surfaces) surface.SetPropertyBlock(null);
                _root.UI.Prompt.SetProgress(null);
                ReturnBrush();
                Work(1);
            }
        }

        void EndWash()
        {
            if (!_washing)
                return;
            _washing = false;
            if (_rig != null)
            {
                _rig.motor.MovementLocked = false;
                _rig.look.Locked = false;
                _rig.hands.SetWorkGrips(null, null);
                _rig.hands.AttachBrush(Tool.LongBrush);
                if (_rig.interactor.Tool == Tool.LongBrush) _rig.hands.SetPose(HandPose.Grip);
            }
            _brush = null;
            _root?.UI.Prompt.SetProgress(null);
        }

        void ReturnBrush()
        {
            EndWash();
            var who = _rig != null ? _rig.interactor : null;
            if (longBrushHook != null && who != null && who.Tool == Tool.LongBrush)
                longBrushHook.ReturnFrom(who);
        }

        void Deliver()
        {
            EndTow();
            var stop = stops[Next];
            Place(Settle(new Vector3(stop.position.x, pan.position.y, stop.position.z)), stop.rotation);
            _crowded = 0f;
            _root?.Audio.PlaySfx("SFX/metal_clank");
            Delivered++;
            _pending = Mathf.Max(0, _pending - 1);
            int arrived = Next;
            Next = 1 - Next;
            // Chảo nằm trên sàn trước lò là chỗ đứng đặt xô tro của cửa tro lò B (AshBucketSpot_2): hộp chảo che ô đặt xô và người chơi
            // không đặt xô được (bot ngày 3 sau khi gộp brush17). Giao tới chỗ trước lò thì chảo lên bếp luôn, hơi chỉ bốc khi đang nấu.
            if (arrived == 0) StartCoroutine(LiftToRange());
            if (arrived == 1 && _dirty)
            {
                NeedsWash = true; Scrub.Reset(); RinseProgress = 0f;
                _brushScrub?.Soil();
            }
            else Work(1);
        }

        public override void SimulateWork()
        {
            if (NeedsWash)
            {
                // Sim: chà, tráng xong chảo đang chờ ở chỗ rửa.
                Scrub.ForceProgress(1f);
                RinseProgress = 1f;
                NeedsWash = _dirty = false;
                _brushScrub?.Clean();
                foreach (var surface in _surfaces) surface.SetPropertyBlock(null);
                ReturnBrush();
                Work(1);
                return;
            }
            var stop = stops[Next];
            if (stop != null && pan != null)
                Place(Settle(new Vector3(stop.position.x, pan.position.y, stop.position.z)), stop.rotation);
            Delivered++;
            _pending = Mathf.Max(0, _pending - 1);
            int arrived = Next;
            Next = 1 - Next;
            if (arrived == 0 && pan != null)
                Place(RangePosition, RangeRotation);
            Work(1);
        }

        protected override void OnCompleted()
        {
            if (Tow.Gripping)
                EndTow();
            ReturnBrush();
            _pending = 0;
        }
    }
}
