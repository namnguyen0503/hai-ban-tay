using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Narrative;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Tasks;
using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Nồi đồng cao (Ø 0,8 × 1 m, quá to để xách) suốt vòng đời của nó, một chủ duy nhất (TallPotLogic): lệnh "tall_out" kéo lê nồi từ góc
    /// phòng rửa ra trước lò rồi nhấc lên ô bếp; lệnh "scrub_tall" nhấc nồi bẩn xuống, kéo về góc rửa, đứng lên ghế đẩu cọ bằng bàn chải cán
    /// dài (BrushScrub trên đáy và chân thành trong), tráng. Ngày có sóng (PhaseData.TallSway) nồi chao khi cọ tới 40% cho tới khi Jacques
    /// giữ nồi. Camera không bao giờ tự dời/xoay: Ba đứng thẳng, hai lòng bàn tay áp thành nồi ở tầm với khi nhấc, bước theo nồi ở tốc độ đi
    /// bộ; nồi cao 1 m nên phải đứng ghế (nâng thật 0,35 m) mới cọ tới đáy.
    /// </summary>
    public sealed class TallPotStation : WorkStation
    {
        [Tooltip("Rigidbody kinematic ở gốc nồi (SweepTest lúc kéo lê).")]
        public Rigidbody pot;
        [Tooltip("Hình nồi (con của gốc): nghiêng khi kéo, chao khi sóng; lớp bẩn và mặt cọ theo khung này.")]
        public Transform body;
        public TaskTarget potTarget;
        public TaskTarget stool;
        public Transform standPoint;
        [Tooltip("Chỗ nồi ở góc phòng rửa.")]
        public Transform home;
        [Tooltip("Chỗ đặt nồi trên sàn trước ô bếp của nó.")]
        public Transform stoveSpot;
        [Tooltip("Ô bếp của nồi cao (mặt nắp gang).")]
        public Transform hob;
        public ToolHook longBrushHook;
        public Material dirtMaterial;
        [Tooltip("Cánh tay giữ chỗ (khi Jacques không có mặt để giữ nồi).")]
        public NpcArm sailorArm;
        public Transform sailorHoldPoint;
        public float stoolHeight = 0.35f;
        public float swayAt = 0.4f;
        public float swaySeconds = 2.5f;
        public float swayDegrees = 4f;
        public float grimeTo = 0.7f;
        public float arriveRadius = 0.4f;
        /// <summary>Tâm nồi cách người khi kéo (m): vành nghiêng về phía người nằm trong tầm tay đứng thẳng.</summary>
        public const float TowReach = 0.8f;
        /// <summary>Tâm nồi cách người khi nhấc (m): thành gần cách ngực ~9 cm, hai lòng bàn tay áp thành ở tầm với.</summary>
        public const float LiftReach = 0.67f;
        /// <summary>Tâm người cách tâm nồi chừng này sau khi nhấc (m): bán kính nồi 0,4 + vỏ người 0,28 + lớp da 0,08 + 4 cm.</summary>
        public const float LiftClearStand = 0.8f;
        public const float TowTiltDegrees = 6f;
        public const float Radius = 0.4f, Height = 1f;
        /// <summary>Nồi nhấc cao hơn mặt ô bếp chừng này trước khi đưa qua mép lò (m).</summary>
        public const float LiftClear = 0.025f;

        GameRoot _root;
        PlayerRig _rig;
        VesselTow _tow;
        BrushScrub _scrub;
        Transform _leftGrip, _rightGrip, _brush, _lowWrist, _highWrist;
        int _outPending, _scrubPending;
        bool _onStool, _scrubbing, _rinsing, _lifting, _helped, _helping, _slipHinted;
        float _swayTime;
        public TallPotLogic Logic { get; } = new TallPotLogic();
        public override string Kind => OrderKinds.ScrubTall;
        /// <summary>Loại lệnh của đơn vị việc vừa xong (trạm làm hai loại lệnh: tall_out, scrub_tall).</summary>
        public string LastKind { get; private set; } = OrderKinds.ScrubTall;
        public Transform potRoot => pot != null ? pot.transform : null;
        public TowLogic Tow => _tow.Logic;
        public BrushScrub Scrubbing => _scrub;
        public bool OnStool => _onStool;
        public bool Lifting => _lifting;
        public bool SailorHelped => _helped;
        public float RinseProgress { get; private set; }
        public ScrubLogic Scrub { get; } = new ScrubLogic();
        /// <summary>Lượt cọ này có sóng làm nồi chao (ngày 4).</summary>
        public bool Sway { get; set; }
        /// <summary>Jacques đã đứng giữ nồi (DayCast.HoldTallPot đặt).</summary>
        public bool JacquesHolding { get; set; }
        public int OutPending => _outPending;
        public int ScrubPending => _scrubPending;
        /// <summary>Số lần cọ xong (sự kiện s2.scrub.done phát ở lần đầu).</summary>
        public int Scrubbed { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            _tow = new VesselTow(pot) { TowSpeed = 0.55f, ScrapeEvery = 0.5f };
            _tow.Logic.Reach = TowReach;
            _tow.Logic.SlipDistance = 1.5f;
        }

        void Start()
        {
            _root = GameRoot.Instance;
            _leftGrip = new GameObject("TallPotGripL").transform;
            _rightGrip = new GameObject("TallPotGripR").transform;
            _lowWrist = new GameObject("TallBrushWristR").transform;
            _highWrist = new GameObject("TallBrushWristL").transform;
            foreach (var t in new[] { _leftGrip, _rightGrip, _lowWrist, _highWrist }) t.SetParent(transform, false);
            // Lòng nồi cao: đáy trong ở 3 cm, thành trong bán kính 0,38 m; cọ cả đáy và 35 cm chân thành (chỗ cặn bám).
            _scrub = new BrushScrub(body, new ScrubSurface(0.36f, 0.03f, 0.35f, 0.03f), BrushShape.Long, 0.38f, Radius, Height);
            // Lòng nồi cao (đáy + thành) rộng gấp ~4,6 lần lòng chảo: rê chuột đưa đầu chải đi xa gấp 1,8 lần, nhát chải rộng hơn, để cọ
            // xong trong khoảng nửa phút như chảo chứ không gấp bốn.
            _scrub.Motion.MetresPerPixel *= 1.8f;
            _scrub.Motion.StrokeHalfAmplitude = 0.05f;
            if (dirtMaterial != null) _scrub.CreateOverlay(dirtMaterial, "TallDirt");
            _scrub.Clean();
            PlaceAt(home, 0f);
        }

        void OnDisable()
        {
            if (pot != null) NpcAgent.Obstacles.Remove(pot.transform);
        }

        protected override void OnActivated() => _rig = PlayerRig.Current;

        /// <summary>Ngày mới: nồi sạch, đứng ở góc phòng rửa.</summary>
        public void ResetForDay()
        {
            Logic.Reset();
            _outPending = _scrubPending = 0;
            Scrub.Reset();
            _scrub?.Clean();
            PlaceAt(home, 0f);
        }

        /// <summary>Lệnh mới của lượt (ShiftDirector).</summary>
        public void Order(string kind, int count)
        {
            if (kind == OrderKinds.TallOut) _outPending += Mathf.Max(0, count);
            else if (kind == OrderKinds.ScrubTall)
            {
                _scrubPending += Mathf.Max(0, count);
                _helped = false;
                _swayTime = 0f;
                _swayAmp = 0f;
                JacquesHolding = false;
            }
        }

        public override void OnOrder(int count) => Order(OrderKinds.ScrubTall, count);

        // ---------- Đặt nồi ----------

        void PlaceAt(Transform spot, float height)
        {
            if (spot == null || pot == null) return;
            var p = spot.position;
            _tow.Place(new Vector3(p.x, p.y + height, p.z), spot.rotation);
            ResetTilt();
            bool away = spot != home;
            if (away && !NpcAgent.Obstacles.Contains(pot.transform)) NpcAgent.Obstacles.Add(pot.transform);
            if (!away) NpcAgent.Obstacles.Remove(pot.transform);
        }

        /// <summary>Đặt nồi đúng chỗ của trạng thái hiện tại (sau tua nhanh).</summary>
        void PlaceForState()
        {
            if (Logic.IsHome) PlaceAt(home, 0f);
            else if (Logic.IsAtStove) PlaceAt(stoveSpot, 0f);
            else if (hob != null)
            {
                _tow.Place(hob.position, hob.rotation);
                ResetTilt();
                if (!NpcAgent.Obstacles.Contains(pot.transform)) NpcAgent.Obstacles.Add(pot.transform);
            }
            if (Logic.IsClean) _scrub?.Clean(); else if (Logic.State != TallPotState.OnHobDirty) _scrub?.Soil();
        }

        /// <summary>Tua nhanh (lượt montage, Sim): đi các bước hợp lệ tới trạng thái đích, đặt nồi đúng chỗ.</summary>
        public void FastForwardTo(TallPotState target)
        {
            if (Logic.State == target) return;
            Logic.FastForwardTo(target);
            PlaceForState();
        }

        /// <summary>Đã nấu xong và phục vụ (MealPots): nồi bám cặn, vẫn trên bếp.</summary>
        public void Served()
        {
            if (Logic.State == TallPotState.OnHobClean) Logic.Apply(TallPotStep.Serve);
        }

        /// <summary>Đang nấu được: nồi sạch nằm trên ô bếp.</summary>
        public bool CookReady => Logic.State == TallPotState.OnHobClean;

        // ---------- Lời nhắc, hướng dẫn ----------

        bool WantsOut => _outPending > 0 && (Logic.State == TallPotState.HomeClean || Logic.State == TallPotState.AtStoveClean);
        bool WantsScrub => _scrubPending > 0 && (Logic.State == TallPotState.OnHobDirty || Logic.State == TallPotState.AtStoveDirty ||
                                                 Logic.State == TallPotState.HomeDirty);

        public override string PromptFor(TaskTarget target, Interactor who)
        {
            if (_lifting) return null;
            if (target == potTarget)
            {
                if (WantsOut && Logic.State == TallPotState.HomeClean && !Tow.Gripping) return who.HandsFree ? "prompt.tow_tall" : "prompt.hands_busy";
                if (WantsOut && Logic.State == TallPotState.AtStoveClean) return who.HandsFree ? "prompt.lift_tall" : "prompt.hands_busy";
                if (WantsScrub && Logic.State == TallPotState.OnHobDirty) return who.HandsFree ? "prompt.lift_down_tall" : "prompt.hands_busy";
                if (WantsScrub && Logic.State == TallPotState.AtStoveDirty && !Tow.Gripping) return who.HandsFree ? "prompt.tow_tall" : "prompt.hands_busy";
                if (WantsScrub && Logic.State == TallPotState.HomeDirty && _onStool)
                    return Logic.Scrubbed ? "prompt.rinse_pot" : _scrubbing || _turning ? "prompt.scrub" : "prompt.scrub_turn_tall";
            }
            if (target == stool && WantsScrub && Logic.State == TallPotState.HomeDirty && !_onStool)
                return who.Tool == Tool.LongBrush ? "prompt.stool" : "prompt.need_long_brush";
            return null;
        }

        public override bool CanInteract(TaskTarget target, Interactor who)
        {
            if (_lifting) return false;
            if (target == potTarget && who.HandsFree)
                return (WantsOut && Logic.State == TallPotState.AtStoveClean) || (WantsScrub && Logic.State == TallPotState.OnHobDirty);
            if (target == stool)
                return WantsScrub && Logic.State == TallPotState.HomeDirty && !_onStool && who.Tool == Tool.LongBrush;
            if (target == potTarget)
                return CanTurn;
            return false;
        }

        public override void OnInteract(TaskTarget target, Interactor who)
        {
            if (target == potTarget && Logic.State == TallPotState.AtStoveClean)
                StartCoroutine(Lift(true));
            else if (target == potTarget && Logic.State == TallPotState.OnHobDirty)
                StartCoroutine(Lift(false));
            else if (target == stool)
                StartCoroutine(StepUp());
            else if (target == potTarget && CanTurn)
                StartCoroutine(Turn());
        }

        // ---------- Xoay nồi trên ghế (QA-0139) ----------

        bool _turning;
        /// <summary>Đang xoay nồi (không cọ được trong lúc này).</summary>
        public bool Turning => _turning;
        /// <summary>Mỗi lần xoay một phần tư vòng: phần lòng nồi xa tầm với quay về phía ghế.</summary>
        public const float TurnDegrees = 90f, TurnSeconds = 0.8f;

        bool CanTurn => _onStool && !_scrubbing && !_turning && !_rinsing && WantsScrub && Logic.State == TallPotState.HomeDirty && !Logic.Scrubbed;

        /// <summary>
        /// Đứng trên ghế, không cọ: Ba bám vành xoay nồi một phần tư vòng quanh trục đứng (nồi nặng, xoay chậm), lớp bẩn quay theo nồi nên
        /// chỗ chưa với tới về phía người. Camera không xoay. Quai nồi ở 45° + k·90° nên không bao giờ quay vào chỗ ghế (phía đông).
        /// </summary>
        IEnumerator Turn()
        {
            _turning = true;
            var from = pot.rotation;
            var to = Quaternion.AngleAxis(TurnDegrees, Vector3.up) * from;
            _root?.Audio.PlaySfxAt("SFX/metal_clank", pot.position, 0.25f);
            for (float t = 0f; t < TurnSeconds; t += Time.deltaTime)
            {
                _tow.Place(pot.position, Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t / TurnSeconds)));
                yield return null;
            }
            _tow.Place(pot.position, to);
            _turning = false;
            ReportProgress();
        }

        public override bool Engaged(Interactor who) => Tow.Gripping || _lifting || _onStool;

        public override bool Guide(Interactor who, out Transform target, out string key)
        {
            if (_lifting)
                return Step(potTarget, null, out target, out key);
            if (WantsOut)
            {
                if (Logic.State == TallPotState.HomeClean)
                    return Tow.Gripping ? Step(stoveSpot, "guide.tall_tow_out", out target, out key) : Step(potTarget, "guide.tall_grip", out target, out key);
                return Step(potTarget, "guide.tall_lift_up", out target, out key);
            }
            if (WantsScrub)
            {
                switch (Logic.State)
                {
                    case TallPotState.OnHobDirty: return Step(potTarget, "guide.tall_lift_down", out target, out key);
                    case TallPotState.AtStoveDirty:
                        return Tow.Gripping ? Step(home, "guide.tall_tow_back", out target, out key) : Step(potTarget, "guide.tall_grip", out target, out key);
                    case TallPotState.HomeDirty:
                        if (!_onStool && who.Tool != Tool.LongBrush && longBrushHook != null && longBrushHook.Hanging)
                            return Step(longBrushHook.transform, "guide.tall_brush", out target, out key);
                        if (!_onStool) return Step(stool, "guide.scrub_tall_stool", out target, out key);
                        return Step(potTarget, Logic.Scrubbed ? "guide.tall_rinse" : "guide.scrub_tall", out target, out key);
                }
            }
            return Step(null, null, out target, out key);
        }

        // ---------- Khung ----------

        protected override void Update()
        {
            base.Update();
            if (State != TaskState.Active || _rig == null || _root == null || pot == null || _lifting)
                return;
            bool held = _root.Input.UseHeld;
            var who = _rig.interactor;
            if (_onStool)
            {
                TickScrub(held);
                return;
            }
            bool towable = (WantsOut && Logic.State == TallPotState.HomeClean) || (WantsScrub && Logic.State == TallPotState.AtStoveDirty);
            if (!Tow.Gripping)
            {
                EaseTilt(false);
                if (towable && held && who.HandsFree && who.IsLookingAt(potTarget))
                    BeginTow();
                return;
            }
            if (!held || !who.HandsFree)
            {
                EndTow();
                return;
            }
            float moved = _tow.Step(_rig, _root, Time.deltaTime, out bool slipped);
            EaseTilt(true);
            PlaceRimGrips();
            if (moved > 0.0005f) ReportProgress();
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
            var goal = Logic.State == TallPotState.HomeClean ? stoveSpot : home;
            var d = pot.position - goal.position;
            d.y = 0f;
            if (d.magnitude <= arriveRadius)
                Arrive();
        }

        void BeginTow()
        {
            if (!_tow.Begin(_rig, _root))
                return;
            _rig.hands.SetWorkGrips(_leftGrip, _rightGrip);
            EaseTilt(true);
            PlaceRimGrips();
            ReportProgress();
        }

        void EndTow(bool instant = false)
        {
            _tow.End(_rig);
            // Buông tay: nồi ngả về đứng thẳng trong TiltSeconds (Update), không bật thẳng trong một khung.
            if (instant) ResetTilt();
            if (_rig != null)
            {
                _rig.hands.SetWorkGrips(null, null);
                if (_rig.interactor.HandsFree) _rig.hands.SetPose(HandPose.Lowered);
            }
        }

        void Arrive()
        {
            bool outward = Logic.State == TallPotState.HomeClean;
            EndTow();
            StartCoroutine(SettleAt(outward ? stoveSpot : home));
            Logic.Apply(outward ? TallPotStep.TowOut : TallPotStep.TowBack);
            _root?.Audio.PlaySfx("SFX/metal_clank");
            if (!outward)
            {
                _scrub.Soil();
                Scrub.Reset();
                RinseProgress = 0f;
                _root?.Subtitles.Play("chef_tallpot");
            }
            ReportProgress();
        }

        /// <summary>Tới nơi: nồi cạ sàn nốt quãng ngắn vào đúng chỗ (không nhảy).</summary>
        IEnumerator SettleAt(Transform spot)
        {
            var from = pot.position;
            var to = spot.position;
            float seconds = Mathf.Clamp(Vector3.Distance(from, to) / 0.5f, 0.05f, 0.8f);
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                _tow.Place(Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds)), Quaternion.Slerp(pot.rotation, spot.rotation, t / seconds));
                yield return null;
            }
            PlaceAt(spot, 0f);
        }

        /// <summary>Hướng ngang từ nồi tới người chơi.</summary>
        Vector3 ToPlayer()
        {
            var d = _rig.Position - pot.position;
            d.y = 0f;
            return d.sqrMagnitude > 1e-6f ? d.normalized : -pot.transform.forward;
        }

        /// <summary>Thời gian nồi ngả 6° khi bắt đầu kéo / đứng thẳng lại khi buông (s).</summary>
        public const float TiltSeconds = 0.3f;
        float _tilt;
        Vector3 _tiltDir = Vector3.forward;

        /// <summary>
        /// Kéo lê: nồi nghiêng ~6° về phía người kéo, tựa trên mép đáy phía người (vành tới gần tay). Ngả dần vào/ra trong TiltSeconds, hướng
        /// ngả xoay mượt theo người đi vòng (đỉnh nồi cách đáy 1 m: ngả tức thời là vành nhảy 10 cm trong một khung).
        /// </summary>
        void EaseTilt(bool on)
        {
            if (body == null) return;
            float dt = Time.deltaTime;
            if (on)
            {
                var toLocal = pot.transform.InverseTransformDirection(ToPlayer());
                toLocal.y = 0f;
                if (toLocal.sqrMagnitude > 1e-6f)
                {
                    toLocal.Normalize();
                    _tiltDir = _tilt <= 0f ? toLocal : Vector3.Slerp(_tiltDir, toLocal, 1f - Mathf.Exp(-8f * dt)).normalized;
                }
            }
            else if (_tilt <= 0f)
                return;
            _tilt = Mathf.MoveTowards(_tilt, on ? 1f : 0f, dt / TiltSeconds);
            float degrees = TowTiltDegrees * Mathf.SmoothStep(0f, 1f, _tilt);
            var tilt = Quaternion.FromToRotation(Vector3.up, (Vector3.up + _tiltDir * Mathf.Tan(degrees * Mathf.Deg2Rad)).normalized);
            var pivot = _tiltDir * Radius;
            body.SetLocalPositionAndRotation(pivot - tilt * pivot, tilt);
        }

        void ResetTilt()
        {
            _tilt = 0f;
            if (body != null) body.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        /// <summary>Hai tay bám vành phía người (cách nhau 30 cm), lòng bàn tay úp lên vành, ngón quặp vào lòng nồi.</summary>
        void PlaceRimGrips()
        {
            var to = ToPlayer();
            var side = Vector3.Cross(Vector3.up, to).normalized;
            var rot = Quaternion.LookRotation(-to, Vector3.up);
            foreach (var (grip, sign) in new[] { (_leftGrip, 1f), (_rightGrip, -1f) })
            {
                var lateral = 0.15f * sign;
                var local = pot.transform.InverseTransformDirection(to * Mathf.Sqrt(Radius * Radius - lateral * lateral) + side * lateral);
                var rim = body.TransformPoint(new Vector3(local.x, Height + 0.012f, local.z));
                grip.SetPositionAndRotation(rim - rot * new Vector3(0f, -0.01f, 0.07f), rot);
            }
        }

        /// <summary>Hai lòng bàn tay áp thành gần của nồi ở độ cao <paramref name="height"/> (hệ nồi), ngón chĩa lên.</summary>
        void PlaceWallGrips(Vector3 toPlayer, float height)
        {
            var side = Vector3.Cross(Vector3.up, toPlayer).normalized;
            var rot = Quaternion.LookRotation(Vector3.up, toPlayer);
            foreach (var (grip, sign) in new[] { (_leftGrip, 1f), (_rightGrip, -1f) })
            {
                float lateral = 0.15f * sign;
                var contact = pot.position + toPlayer * Mathf.Sqrt(Radius * Radius - lateral * lateral) + side * lateral + Vector3.up * height
                              + toPlayer * 0.012f;
                grip.SetPositionAndRotation(contact - rot * new Vector3(0f, -0.01f, 0.07f), rot);
            }
        }

        // ---------- Nhấc lên / xuống ----------

        /// <summary>
        /// Nhấc nồi giữa sàn trước lò và ô bếp, đứng thẳng (camera không cúi, không tự xoay): hai lòng bàn tay áp thành gần dưới vành 10 cm,
        /// nâng thẳng lên quá mặt bếp, đưa vào qua mép lò (người bước theo ở tốc độ đi bộ), đặt lên mép bếp, hạ tay xuống giữa thành rồi đẩy
        /// nồi vào đúng ô. Nhấc xuống đi ngược lại. Mọi chỗ bám cách vai ≤ dài tay × 1,08 (PlayerBody duỗi được), xem TallPotLiftTests.
        /// </summary>
        IEnumerator Lift(bool up)
        {
            if (hob == null || stoveSpot == null) yield break;
            _lifting = true;
            _rig.motor.MovementLocked = true;
            var into = hob.position - stoveSpot.position;
            into.y = 0f;
            into.Normalize();
            var toPlayer = -into;
            float top = hob.position.y + LiftClear;
            var floorAt = new Vector3(stoveSpot.position.x, stoveSpot.position.y, stoveSpot.position.z);
            // Đặt lên mép bếp: tâm nồi vừa qua mép trước của lò (mép trước cách ô 0,5 m).
            var edgeAt = new Vector3(hob.position.x, hob.position.y, hob.position.z) - into * 0.45f;
            var hobAt = hob.position;
            const float high = Height - 0.1f, mid = 0.5f;
            Vector3 Stand(Vector3 potAt) => new Vector3(potAt.x, _rig.Position.y, potAt.z) + toPlayer * LiftReach;
            _root?.Audio.PlaySfx("SFX/metal_clank", 0.5f);
            if (up)
            {
                // Bước tới chỗ đứng nhấc (tốc độ đi bộ), tay chờ rồi áp vào thành.
                _rig.StepToWork(Stand(pot.position));
                while (_rig.Stepping) yield return null;
                PlaceWallGrips(toPlayer, high);
                _rig.hands.SetWorkGrips(_leftGrip, _rightGrip);
                yield return Hold(0.25f, toPlayer, high, null);
                yield return Move(floorAt, new Vector3(floorAt.x, top, floorAt.z), 0.9f, toPlayer, high, false);
                yield return Move(new Vector3(floorAt.x, top, floorAt.z), new Vector3(edgeAt.x, top, edgeAt.z), 0.6f, toPlayer, high, true);
                yield return Move(new Vector3(edgeAt.x, top, edgeAt.z), edgeAt, 0.15f, toPlayer, high, true);
                yield return Regrip(toPlayer, high, mid, 0.3f);
                yield return Move(edgeAt, hobAt, 0.5f, toPlayer, mid, true);
                Logic.Apply(TallPotStep.LiftUp);
            }
            else
            {
                _rig.StepToWork(Stand(pot.position));
                while (_rig.Stepping) yield return null;
                PlaceWallGrips(toPlayer, mid);
                _rig.hands.SetWorkGrips(_leftGrip, _rightGrip);
                yield return Hold(0.25f, toPlayer, mid, null);
                yield return Move(hobAt, edgeAt, 0.5f, toPlayer, mid, true);
                yield return Regrip(toPlayer, mid, high, 0.3f);
                yield return Move(edgeAt, new Vector3(edgeAt.x, top, edgeAt.z), 0.15f, toPlayer, high, true);
                yield return Move(new Vector3(edgeAt.x, top, edgeAt.z), new Vector3(floorAt.x, top, floorAt.z), 0.6f, toPlayer, high, true);
                yield return Move(new Vector3(floorAt.x, top, floorAt.z), floorAt, 0.9f, toPlayer, high, false);
                Logic.Apply(TallPotStep.LiftDown);
            }
            _root?.Audio.PlaySfx("SFX/metal_clank");
            yield return Hold(0.2f, toPlayer, up ? mid : high, null);
            _rig.hands.SetWorkGrips(null, null);
            _rig.hands.SetPose(HandPose.Lowered);
            // Buông tay: lùi một bước khỏi nồi (tốc độ đi bộ) tới chỗ vỏ va chạm của người không chồng lên nồi, rồi đứng luôn ở đó.
            // Lùi về chỗ đứng trước lúc nhấc thì có khi đúng chỗ nồi vừa đặt xuống: vỏ bị đẩy lên nóc nồi (bot ngày 1, brush17b).
            var clear = new Vector3(pot.position.x, _rig.Position.y, pot.position.z) + toPlayer * LiftClearStand;
            var gap = _rig.Position - pot.position;
            gap.y = 0f;
            if (gap.magnitude < LiftClearStand - 0.01f)
            {
                _rig.StepToWork(clear);
                while (_rig.Stepping) yield return null;
            }
            _rig.EndWorkStanceHere();
            _rig.motor.MovementLocked = false;
            PlaceForState();
            _lifting = false;
            if (up)
            {
                _outPending = Mathf.Max(0, _outPending - 1);
                LastKind = OrderKinds.TallOut;
                Work(1);
            }
            ReportProgress();
        }

        IEnumerator Hold(float seconds, Vector3 toPlayer, float height, System.Action each)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                PlaceWallGrips(toPlayer, height);
                each?.Invoke();
                yield return null;
            }
        }

        /// <summary>Đưa nồi từ <paramref name="from"/> tới <paramref name="to"/> (SmoothStep); <paramref name="follow"/>: người bước theo giữ khoảng nhấc.</summary>
        /// <summary>Tâm người không lại gần tâm ô bếp quá chừng này (m): mặt lò 0,35 + vỏ 0,28 + bắp chân nhô trước vỏ 0,13.</summary>
        const float HobKeepOut = 0.76f;

        /// <summary>
        /// Chỗ đứng theo nồi (cách LiftReach), nhưng không sát mặt lò hơn HobKeepOut: đẩy nồi từ mép lên ô thì người đi theo tới 0,67 m
        /// và bắp chân lún 10 cm vào cửa tro (bot 23/10 D4). Tay với xa thêm vài cm.
        /// </summary>
        Vector3 FollowStand(Vector3 potAt, Vector3 toPlayer)
        {
            var p = new Vector3(potAt.x, _rig.Position.y, potAt.z) + toPlayer * LiftReach;
            float d = Vector3.Dot(p - hob.position, toPlayer);
            return d < HobKeepOut ? p + toPlayer * (HobKeepOut - d) : p;
        }

        /// <summary>Tốc độ người đi theo nồi tối thiểu (m/s): chỗ đứng đổi 9 cm giữa hai khung làm camera giật (Brush17PlayTests, 23/10).</summary>
        const float FollowSpeed = 1.2f;

        /// <summary>Đi dần tới chỗ đứng, luôn tới kịp khi hết <paramref name="remaining"/> giây (bot 23/10: người tụt lại sau nồi, kẹt bàn).</summary>
        void FollowTo(Vector3 want, float remaining)
        {
            var at = _rig.Position;
            var step = want - at;
            step.y = 0f;
            float speed = remaining > Time.deltaTime ? Mathf.Max(FollowSpeed, step.magnitude / remaining) : float.PositiveInfinity;
            _rig.MoveInStance(at + Vector3.ClampMagnitude(step, speed * Time.deltaTime));
        }

        IEnumerator Move(Vector3 from, Vector3 to, float seconds, Vector3 toPlayer, float height, bool follow)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                var at = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds));
                _tow.Place(at, pot.rotation);
                if (follow) FollowTo(FollowStand(at, toPlayer), seconds - t);
                PlaceWallGrips(toPlayer, height);
                yield return null;
            }
            _tow.Place(to, pot.rotation);
            if (follow) FollowTo(FollowStand(to, toPlayer), 0f);
            PlaceWallGrips(toPlayer, height);
        }

        /// <summary>Nồi tựa trên mép bếp: hai tay trượt dọc thành từ độ cao này sang độ cao kia.</summary>
        IEnumerator Regrip(Vector3 toPlayer, float from, float to, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                PlaceWallGrips(toPlayer, Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds)));
                yield return null;
            }
            PlaceWallGrips(toPlayer, to);
        }

        // ---------- Ghế đẩu, cọ, tráng ----------

        IEnumerator StepUp()
        {
            _onStool = true;
            _rig.SetMovement(false);
            _rig.motor.ExtraHeadHeight = stoolHeight;
            // Bước lên ghế: người lên cao thật 0,35 m, hướng nhìn giữ nguyên (game không tự xoay camera, 17/10).
            yield return _rig.MoveTo(standPoint.position, _rig.look.Yaw, _rig.look.Pitch, 0.6f);
            // Đứng trên ghế sát nồi: vỏ va chạm của người (bán kính 0,28 m, ở tầm sàn) chồng lên thân nồi, để bật thì nó đẩy người ra
            // khỏi ghế 14 cm. Trên ghế không đi lại được nên tắt vỏ tới lúc bước xuống (StepOff).
            _cc = _rig.motor.GetComponent<CharacterController>();
            if (_cc != null && _onStool) _cc.enabled = false;
            ReportProgress();
        }

        CharacterController _cc;

        /// <summary>Bước xuống ghế: lùi ra sau ghế (ra khỏi chỗ vỏ va chạm chồng lên nồi) ở tốc độ đi bộ rồi mới cho đi lại.</summary>
        IEnumerator StepOff()
        {
            var away = standPoint.position - potRoot.position;
            away.y = 0f;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.right;
            var to = standPoint.position + away * StepOffDistance;
            to.y = _rig.Position.y;
            // MoveTo bật lại vỏ va chạm khi tới nơi.
            yield return _rig.MoveTo(to, _rig.look.Yaw, _rig.look.Pitch, 0.5f);
            if (_cc != null) _cc.enabled = true;
            _cc = null;
            if (!_onStool) _rig.SetMovement(true);
        }

        /// <summary>Bước xuống thì lùi chừng này ra sau tâm ghế (m): vỏ người (0,28) cách thân nồi (0,4) đủ.</summary>
        public const float StepOffDistance = 0.18f;

        void StepDown()
        {
            EndScrub();
            if (!_onStool) return;
            _onStool = false;
            if (_rig != null)
            {
                _rig.look.Locked = false;
                _rig.motor.ExtraHeadHeight = 0f;
                if (_cc != null && isActiveAndEnabled && standPoint != null && potRoot != null)
                    StartCoroutine(StepOff());
                else
                {
                    if (_cc != null) _cc.enabled = true;
                    _cc = null;
                    _rig.SetMovement(true);
                }
            }
        }

        void TickScrub(bool held)
        {
            var who = _rig.interactor;
            if (Logic.State != TallPotState.HomeDirty)
            {
                StepDown();
                return;
            }
            if (!Logic.Scrubbed)
            {
                bool want = held && !_turning && who.Tool == Tool.LongBrush && (_scrubbing || who.IsLookingAt(potTarget));
                if (want && !_scrubbing)
                {
                    _scrubbing = true;
                    // Đang cọ: chuột dùng để cọ, không xoay camera.
                    _rig.look.Locked = true;
                    _brush = _rig.hands.DetachBrush(Tool.LongBrush);
                    _scrub.Restart();
                    _scrub.AimReachable(who.LookPoint, _rig);
                    _scrub.Tick(_rig, false, Vector2.zero, 0f, Vector3.right, Vector3.forward);
                    _scrub.Place(_brush, _lowWrist, _highWrist);
                    _rig.hands.SetWorkGrips(_highWrist, _lowWrist, HandPose.Grip, HandPose.Grip);
                    _root.Audio.PlaySfxAt("SFX/water_wash", pot.position, 0.4f);
                }
                else if (!want && _scrubbing)
                    EndScrub();
                TickSway();
                if (_scrubbing)
                {
                    var cam = _rig.cam.transform;
                    var delta = _root.Input.GameplayEnabled ? _root.Input.LookRaw : Vector2.zero;
                    bool capped = Sway && !_helped && _scrub.Progress >= swayAt;
                    if (!capped && _scrub.Tick(_rig, true, delta, Time.deltaTime, Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized,
                            Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized))
                    {
                        Scrub.ForceProgress(Mathf.Min(_scrub.Progress, Sway && !_helped ? swayAt : 1f));
                        BodyState.Current?.Exert(0.004f * Time.deltaTime);
                        ReportProgress();
                    }
                    else if (capped)
                        _scrub.Tick(_rig, true, Vector2.zero, Time.deltaTime, Vector3.right, Vector3.forward);
                    _scrub.Place(_brush, _lowWrist, _highWrist);
                    _root.UI.Prompt.SetProgress(Scrub.Progress);
                    if (_scrub.Done && (!Sway || _helped))
                    {
                        _scrub.Clean();
                        Scrub.ForceProgress(1f);
                        Logic.MarkScrubbed();
                        EndScrub();
                    }
                }
                return;
            }
            // Tráng: giữ chuột nhìn vào nồi.
            bool rinsing = held && (_rinsing || who.IsLookingAt(potTarget));
            if (rinsing)
            {
                if (!_rinsing) _root.Audio.PlaySfxAt("SFX/water_wash", pot.position, 0.5f);
                RinseProgress = Mathf.Min(1f, RinseProgress + Time.deltaTime / 1.5f);
                _root.UI.Prompt.SetProgress(RinseProgress);
            }
            else if (_rinsing)
                _root.UI.Prompt.SetProgress(null);
            _rinsing = rinsing;
            if (RinseProgress >= 1f)
                FinishWash();
        }

        float _swayAmp;

        /// <summary>
        /// Sóng (ngày 4): nồi chao quanh đáy. Biên độ lên dần khi sóng tới, Jacques giữ thì lặng dần trong 0,6 s (không bật thẳng trong một khung).
        /// </summary>
        void TickSway()
        {
            if (!Sway || body == null)
                return;
            if (_helped)
            {
                if (_swayAmp <= 0f) return;
                _swayAmp = Mathf.MoveTowards(_swayAmp, 0f, Time.deltaTime / 0.6f);
                _swayTime += Time.deltaTime;
                ApplySway();
                return;
            }
            if (_scrub.Progress < swayAt)
                return;
            _swayTime += Time.deltaTime;
            _swayAmp = Mathf.MoveTowards(_swayAmp, 1f, Time.deltaTime / 0.5f);
            ApplySway();
            if (_swayTime >= swaySeconds && JacquesHolding)
                MarkHelped();
            else if (_swayTime >= swaySeconds + 6f && !_helping)
                StartCoroutine(ArmHelps());
        }

        void ApplySway()
        {
            float a = swayDegrees * Mathf.SmoothStep(0f, 1f, _swayAmp);
            body.localRotation = Quaternion.Euler(Mathf.Sin(_swayTime * 3.5f) * a, 0f, Mathf.Cos(_swayTime * 2.7f) * a * 0.6f);
        }

        IEnumerator ArmHelps()
        {
            _helping = true;
            if (sailorArm != null && sailorHoldPoint != null)
            {
                sailorArm.gameObject.SetActive(true);
                yield return sailorArm.Reach(sailorHoldPoint, 1.8f);
            }
            MarkHelped();
            _root?.Subtitles.Play("sailor_hold");
            _helping = false;
        }

        void MarkHelped()
        {
            if (_helped) return;
            _helped = true;
            // Đang cọ thì TickSway cho nồi lặng dần; ngoài lượt cọ (tua nhanh, xong việc) thì đứng thẳng luôn.
            if (body != null && !_scrubbing) { _swayAmp = 0f; body.localRotation = Quaternion.identity; }
            Events?.Raise(EventIds.S2SailorHelp);
        }

        void EndScrub()
        {
            if (!_scrubbing) return;
            _scrubbing = false;
            if (_rig != null)
            {
                _rig.look.Locked = false;
                _rig.hands.SetWorkGrips(null, null);
                _rig.hands.AttachBrush(Tool.LongBrush);
                if (_rig.interactor.Tool == Tool.LongBrush) _rig.hands.SetPose(HandPose.Grip);
            }
            _brush = null;
            _root?.UI.Prompt.SetProgress(null);
        }

        void FinishWash()
        {
            Logic.MarkRinsed();
            Logic.Apply(TallPotStep.Wash);
            _rinsing = false;
            _root?.UI.Prompt.SetProgress(null);
            StepDown();
            ReturnBrush();
            CompleteScrub();
        }

        void CompleteScrub()
        {
            _scrubPending = Mathf.Max(0, _scrubPending - 1);
            Scrubbed++;
            if (Sway) MarkHelped();
            var root = _root ?? GameRoot.Instance;
            if (root != null)
            {
                if (Scrubbed == 1) root.Events.Raise(EventIds.S2ScrubDone);
                root.State.Grime = Mathf.Max(root.State.Grime, grimeTo);
                var rig = _rig ?? PlayerRig.Current;
                if (rig != null) rig.hands.SetGrime(root.State.Grime);
            }
            if (sailorArm != null && sailorArm.isActiveAndEnabled)
                StartCoroutine(RetractLater());
            LastKind = OrderKinds.ScrubTall;
            Work(1);
        }

        IEnumerator RetractLater()
        {
            yield return new WaitForSeconds(1.5f);
            yield return sailorArm.Retract(1f);
        }

        void ReturnBrush()
        {
            EndScrub();
            var who = _rig != null ? _rig.interactor : PlayerRig.Current != null ? PlayerRig.Current.interactor : null;
            if (longBrushHook != null && who != null && who.Tool == Tool.LongBrush)
                longBrushHook.ReturnFrom(who);
        }

        public override void SimulateWork()
        {
            if (_outPending > 0)
            {
                FastForwardTo(TallPotState.OnHobClean);
                _outPending--;
                LastKind = OrderKinds.TallOut;
                Work(1);
                return;
            }
            if (_scrubPending > 0)
            {
                if (Tow.Gripping) EndTow(true);
                StepDown();
                FastForwardTo(TallPotState.HomeClean);
                CompleteScrub();
            }
        }

        protected override void OnCompleted()
        {
            if (Tow.Gripping) EndTow(true);
            StepDown();
            ReturnBrush();
            _root?.UI.Prompt.SetProgress(null);
        }
    }
}
