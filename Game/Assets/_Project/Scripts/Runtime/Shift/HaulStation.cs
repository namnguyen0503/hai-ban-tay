using System;
using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Player;
using HaiBanTay.Ship;
using HaiBanTay.Tasks;
using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Kéo rổ rau bằng dây sắt ngày biển động (F05b). Rổ đặt ở sàn hầm thực phẩm cạnh chân thang; E buộc rổ vào dây sắt,
    /// rồi giữ chuột trái và đi trước rổ (lên 14 bậc thang, qua boong giếng trước, vào cửa bếp): rổ theo sau một đoạn dây
    /// (HaulLogic). Buông dây khi rổ đang trên thang thì rổ tụt về chân thang. Rổ vào tới bếp là xong một đơn vị.
    /// Ngày có sóng (waveEnabled, lượt DeckWave): rổ chuyến thứ hai lên tới boong mà người chơi đang ở boong giếng, một con sóng
    /// lớn phủ boong, cuốn người chơi tới kẹt giữa cột buồm trước và sợi xích (staged, không ngã, không thất bại); rổ
    /// bị cuốn xuống biển; người chơi men theo dây cứu sinh lấy rổ khác, kéo lại chuyến thứ hai.
    /// </summary>
    public sealed class HaulStation : WorkStation
    {
        /// <summary>Đường dây sắt (tọa độ thế giới): sàn hầm → chân thang → đầu thang → boong giếng → trong bếp.</summary>
        public Vector3[] points = new Vector3[0];
        /// <summary>Chỉ số điểm đầu thang (rổ qua điểm này là đã lên boong).</summary>
        public int deckFrom = 2;
        public Transform basket;
        public TaskTarget basketTarget;
        public LineRenderer rope;
        [Tooltip("Đang kéo rổ nặng: đi chậm lại (m/s).")]
        public float pullSpeedCap = 1.1f;
        public float exertPerMetre = 0.004f;
        public float promptRadius = 7f;

        [Header("Sóng phủ boong (F05b)")]
        public bool waveEnabled;
        /// <summary>Vùng boong giếng trước (thế giới): người chơi ở đây khi rổ lên tới boong thì sóng đánh.</summary>
        public Bounds wellDeck = new Bounds(new Vector3(-5.7f, 1f, 0.9f), new Vector3(1.6f, 2.4f, 5f));
        /// <summary>Chỗ người chơi bị sóng cuốn tới: giữa cột buồm trước và sợi xích.</summary>
        public Transform mastSpot;
        public float waveShove = 2.5f;
        public float waveLockSeconds = 2.6f;

        GameRoot _root;
        PlayerRig _rig;
        int _pending;
        bool _promptShown;
        bool _capped;
        bool _waving;
        float _lastBasket;
        Renderer[] _basketRenderers = new Renderer[0];
        int _basketIndoors = -1;

        public override string Kind => OrderKinds.Haul;
        public HaulLogic Logic { get; } = new HaulLogic(new Vector3[0]);
        public int Pending => _pending;
        public bool WaveDone { get; private set; }
        /// <summary>Sóng đang phủ boong (từ lúc hút tiếng tới khi trả điều khiển); WaveDone bật ngay từ đầu, trước khi người chơi bị cuốn.</summary>
        public bool Waving => _waving;
        public int DeliveredTrips { get; private set; }
        public int LostBaskets { get; private set; }
        public Vector3 BasketPosition => basket != null ? basket.position : Logic.PointAt(Logic.Basket);
        /// <summary>Rổ còn ở hầm hoặc trên thang (chưa lên boong).</summary>
        public bool BasketBelowDeck => Logic.Basket < Logic.DistanceAt(deckFrom) - 0.05f;
        public event Action WaveHit;

        protected override void Awake()
        {
            base.Awake();
            if (basket != null)
                _basketRenderers = basket.GetComponentsInChildren<Renderer>(true);
            // Dây căng từ hầm lên boong: nhận cả đèn trong khoang và mặt trời.
            if (rope != null)
                rope.renderingLayerMask = WorldRenderingLayers.Interior | WorldRenderingLayers.Exterior;
            Logic.SetPath(points);
            PlaceBasket();
            ShowBasket(false);
        }

        void Start()
        {
            _root = GameRoot.Instance;
        }

        protected override void OnActivated()
        {
            _rig = PlayerRig.Current;
            _root ??= GameRoot.Instance;
        }

        public override void OnOrder(int count)
        {
            if (count <= 0)
                return;
            if (_pending == 0)
                ResetBasket();
            _pending += count;
            ShowBasket(true);
        }

        void ResetBasket()
        {
            Logic.Reset();
            _lastBasket = 0f;
            PlaceBasket();
            UpdateRope();
        }

        void ShowBasket(bool on)
        {
            if (basket != null && basket.gameObject.activeSelf != on)
                basket.gameObject.SetActive(on);
            if (rope != null)
                rope.enabled = on && Logic.Tied;
        }

        Transform _end;

        public override bool Engaged(Interactor who) => _pending > 0 && Logic.Tied;

        public override bool Guide(Interactor who, out Transform target, out string key)
        {
            if (_pending <= 0 || _waving)
                return Step(null, null, out target, out key);
            if (!Logic.Tied)
                return Step(basketTarget, "guide.haul_tie", out target, out key);
            if (_end == null && points.Length > 0)
            {
                _end = new GameObject("HaulGuideEnd").transform;
                _end.SetParent(transform, false);
                _end.position = points[points.Length - 1];
            }
            return Step(_end, "guide.haul_pull", out target, out key);
        }

        public override string PromptFor(TaskTarget target, Interactor who)
        {
            if (target == basketTarget && _pending > 0 && !Logic.Tied)
                return who.HandsFree ? "prompt.tie_basket" : "prompt.hands_busy";
            return null;
        }

        public override bool CanInteract(TaskTarget target, Interactor who)
        {
            return target == basketTarget && _pending > 0 && !Logic.Tied && who.HandsFree;
        }

        public override void OnInteract(TaskTarget target, Interactor who)
        {
            if (target != basketTarget || !Logic.Tie())
                return;
            ReportProgress();
            _root?.Audio.PlaySfx("SFX/rope");
            if (rope != null)
                rope.enabled = true;
            UpdateRope();
        }

        protected override void Update()
        {
            base.Update();
            if (State != TaskState.Active || _rig == null || _root == null || _pending == 0 || _waving)
                return;
            var feet = _rig.Position;
            if (Logic.Tied && !Logic.Arrived)
            {
                var interactor = _rig.interactor;
                bool pull = _root.Input.UseHeld && interactor.HandsFree;
                var e = Logic.Tick(feet, pull, Time.deltaTime);
                SetCap(Logic.Holding);
                float moved = Logic.Basket - _lastBasket;
                if (moved > 0f)
                    BodyState.Current?.Exert(moved * exertPerMetre);
                _lastBasket = Logic.Basket;
                if (e == HaulEvent.Moved || e == HaulEvent.Arrived)
                    ReportProgress();
                PlaceBasket();
                UpdateRope();
                UpdatePrompt(feet);
                if (e == HaulEvent.Arrived)
                {
                    Deliver();
                    return;
                }
                if (waveEnabled && DeliveredTrips == 1 && !WaveDone && Logic.Basket >= Logic.DistanceAt(deckFrom) - 0.6f && wellDeck.Contains(feet))
                    StartCoroutine(Wave());
            }
            else
            {
                SetCap(false);
                UpdatePrompt(feet);
            }
        }

        void UpdatePrompt(Vector3 feet)
        {
            string key = null;
            if (Logic.Tied && !Logic.Arrived && !Logic.Holding && (feet - BasketPosition).sqrMagnitude < promptRadius * promptRadius)
            {
                Logic.Project(feet, out float lateral);
                key = lateral <= Logic.Reach ? "prompt.haul" : "prompt.haul_reach";
            }
            if (key == null)
            {
                if (_promptShown)
                {
                    _promptShown = false;
                    _root.UI.Prompt.SetScenePrompt(null);
                }
                return;
            }
            _promptShown = true;
            _root.UI.Prompt.SetScenePrompt(_root.Content.GetString(key));
        }

        void SetCap(bool on)
        {
            if (on == _capped || _rig == null)
                return;
            _capped = on;
            _rig.motor.SpeedCap = on ? pullSpeedCap : float.PositiveInfinity;
        }

        void PlaceBasket()
        {
            if (basket == null || Logic.PointCount == 0)
                return;
            var dir = Logic.DirectionAt(Logic.Basket);
            var flat = new Vector3(dir.x, 0f, dir.z);
            basket.position = Logic.PointAt(Logic.Basket);
            if (flat.sqrMagnitude > 1e-4f)
                basket.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            UpdateBasketLayer();
        }

        /// <summary>Rổ ở hầm/thang hoặc trong bếp: lớp đèn trong khoang; trên boong giếng: lớp ngoài trời.</summary>
        void UpdateBasketLayer()
        {
            var p = basket.position;
            int inside = BasketBelowDeck || p.y < -0.5f || p.x > -5.1f ? 1 : 0;
            if (inside == _basketIndoors)
                return;
            _basketIndoors = inside;
            uint mask = inside == 1 ? WorldRenderingLayers.Interior : WorldRenderingLayers.Exterior;
            foreach (var r in _basketRenderers)
                if (r != null)
                    r.renderingLayerMask = mask;
        }

        /// <summary>Rổ đang dùng lớp đèn trong khoang (test).</summary>
        public bool BasketLitAsInterior => _basketIndoors != 0;

        void UpdateRope()
        {
            if (rope == null)
                return;
            rope.enabled = Logic.Tied && basket != null && basket.gameObject.activeSelf;
            if (!rope.enabled)
                return;
            var from = BasketPosition + Vector3.up * 0.25f;
            Vector3 to;
            if (Logic.Holding && _rig != null && _rig.cam != null)
            {
                var cam = _rig.cam.transform;
                to = cam.position + cam.forward * 0.35f - cam.up * 0.4f;
            }
            else
                to = Logic.PointAt(Mathf.Min(Logic.Length, Logic.Basket + Logic.Rope)) + Vector3.up * 0.03f;
            rope.positionCount = 3;
            rope.SetPosition(0, from);
            // Dây chùng ở giữa khi không kéo căng.
            var mid = Vector3.Lerp(from, to, 0.5f) + (Logic.Holding ? Vector3.zero : Vector3.down * 0.12f);
            rope.SetPosition(1, mid);
            rope.SetPosition(2, to);
        }

        void Deliver()
        {
            SetCap(false);
            ClearPrompt();
            DeliveredTrips++;
            _pending = Mathf.Max(0, _pending - 1);
            _root.Audio.PlaySfx("SFX/sack_thud");
            Work(1);
            if (_pending > 0)
                ResetBasket();
            else
            {
                Logic.Reset();
                ShowBasket(false);
            }
        }

        /// <summary>Sóng lớn phủ boong: tóe trắng màn hình, người chơi bị cuốn tới cột buồm, rổ mất hẳn xuống biển.</summary>
        IEnumerator Wave()
        {
            _waving = true;
            WaveDone = true;
            Logic.Release();
            LostBaskets++;
            SetCap(false);
            ClearPrompt();
            // Im lặng thứ hai (DIRECTOR §5): gió và máy bị hút đi chừng nửa giây như nước rút trước sóng, rồi trắng xóa và ầm.
            if (!Sim.Enabled)
            {
                _root.Audio.Hush(0.03f, 0.25f);
                yield return new WaitForSeconds(0.5f);
            }
            _root.Audio.Hush(1f, 4f);
            _root.UI.Tint.Splash(1f);
            _root.Audio.PlaySfx("SFX/wave_crash", 0.8f);
            _root.Audio.PlaySfx("SFX/chain_rattle", 0.65f);
            _rig.SetControl(false);
            if (mastSpot != null)
            {
                _rig.motor.Teleport(mastSpot.position, mastSpot.eulerAngles.y);
                _rig.look.SetAngles(mastSpot.eulerAngles.y, 12f);
            }
            _rig.SetNoise(1.2f);
            Events?.Raise(EventIds.S2StormWave);
            WaveHit?.Invoke();
            _root.Subtitles.Play("ba_wave");
            _root.Subtitles.Play("jacques_wave");
            var lost = basket;
            foreach (var c in lost.GetComponentsInChildren<Collider>()) c.enabled = false;
            var replacement = Instantiate(lost.gameObject, lost.parent);
            replacement.name = "HaulBasketReplacement";
            basket = replacement.transform;
            basketTarget = replacement.GetComponent<TaskTarget>();
            foreach (var c in replacement.GetComponentsInChildren<Collider>()) c.enabled = true;
            _basketRenderers = replacement.GetComponentsInChildren<Renderer>(true);
            _basketIndoors = -1;
            ResetBasket();
            ShowBasket(false);
            var lossStart = lost.position;
            for (float t = 0f; t < 0.65f; t += Time.deltaTime)
            { lost.position = lossStart + new Vector3(0f, -7f * t, -12f * t); yield return null; }
            Destroy(lost.gameObject);
            yield return new WaitForSeconds(waveLockSeconds);
            _rig.SetControl(true);
            _root.UI.Prompt.ShowHint(_root.Content.GetString("hint.after_wave"), 8f);
            ShowBasket(true);
            _waving = false;
        }

        void ClearPrompt()
        {
            if (!_promptShown)
                return;
            _promptShown = false;
            _root?.UI.Prompt.SetScenePrompt(null);
        }

        public override void SimulateWork()
        {
            if (_pending == 0)
            {
                _pending = 1;
                ResetBasket();
            }
            if (waveEnabled && DeliveredTrips == 1 && !WaveDone)
            {
                WaveDone = true; LostBaskets++;
                Events?.Raise(EventIds.S2StormWave);
            }
            DeliveredTrips++;
            Logic.Finish();
            PlaceBasket();
            _pending = Mathf.Max(0, _pending - 1);
            Work(1);
            if (_pending > 0)
                ResetBasket();
            else
            {
                Logic.Reset();
                ShowBasket(false);
            }
        }

        protected override void OnCompleted()
        {
            StopAllCoroutines();
            if (_waving && _rig != null)
                _rig.SetControl(true);
            _waving = false;
            SetCap(false);
            ClearPrompt();
            _pending = 0;
            Logic.Reset();
            ShowBasket(false);
        }
    }
}
