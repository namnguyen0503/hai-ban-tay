using System.Collections;
using System.Collections.Generic;
using HaiBanTay.Interaction;
using HaiBanTay.Narrative;
using HaiBanTay.Player;
using HaiBanTay.Tasks;
using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Vác khoai (F03, F05): nhấc bao từ kho lạnh bằng hai tay, leo cầu thang hẹp, đứng cạnh thùng sàn để đặt bao vào điểm dốc. Đặt xong thì cởi dây
    /// (E) và dốc khoai vào thùng (giữ chuột trái, kéo xuống — SackPourLogic); bao rỗng gấp lại để cạnh bàn, bao cũ trở
    /// lại kho khi người chơi không ở trong kho. Thùng khoai do người khác mang đi nấu (NPC, M10). Mỗi bao dốc xong tính
    /// một đơn vị.
    /// </summary>
    public sealed class SackStation : WorkStation
    {
        public DropZone zone;
        public Holdable[] sacks = new Holdable[0];
        public ColdZone coldRoom;
        /// <summary>Miệng bao cạnh thùng sàn: nhắm vào đây để cởi dây và dốc.</summary>
        public TaskTarget mouth;
        /// <summary>Miệng bao cố định phía thùng: bao nghiêng quanh điểm này.</summary>
        public Transform tipPivot;
        /// <summary>Củ khoai trong thùng, hiện dần theo số củ đã dốc.</summary>
        public Transform[] tubPotatoes = new Transform[0];
        /// <summary>Bao rỗng gấp lại cạnh bàn, hiện theo số bao đã dốc.</summary>
        public GameObject[] emptySacks = new GameObject[0];
        public float tipDegrees = 150f;
        /// <summary>Số củ hiện ra mỗi bao: một bao đầy đổ được gần nửa thùng.</summary>
        public int potatoesPerSack = 20;
        public float clearDelay = 1.5f;
        public float exertOnPlace = 0.01f;
        public float exertOnPour = 0.004f;
        public float grimeOnPlace = 0.01f;

        Core.GameRoot _root;
        PlayerRig _rig;
        Holdable _current;
        Vector3 _basePos;
        Quaternion _baseRot;
        bool _pouring;
        Transform _mouthGrip, _bottomGrip;
        /// <summary>Khoảng lùi từ miệng bao tới chỗ đứng khi dốc (m).</summary>
        public const float StandBack = 0.44f;
        /// <summary>Bao đặt cao hơn bản cũ (m): đứng thẳng, camera không cúi (17/10) thì miệng và đáy bao vẫn trong tầm tay.</summary>
        public const float SackLift = 0.25f;
        int _inTub;
        Vector3[] _tubHomes = new Vector3[0];

        public override string Kind => OrderKinds.Sack;
        public int Delivered { get; private set; }
        public SackPourLogic Pour { get; } = new SackPourLogic();
        /// <summary>Bao đang đặt cạnh thùng chờ dốc.</summary>
        public Holdable OnTable => _current;
        public int InTub => _inTub;
        /// <summary>Thùng đang được người khác bê đi đổ (không dốc bao được).</summary>
        public bool TubAway { get; set; }
        /// <summary>Số củ đang lăn từ bao sang thùng.</summary>
        public int Rolling { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            Pour.Potatoes = potatoesPerSack;
            // Với bao dựng đứng, miệng phải nghiêng qua phương ngang rồi củ mới lăn ra.
            Pour.SpillFrom = 0.58f;
            _tubHomes = new Vector3[tubPotatoes.Length];
            for (int i = 0; i < tubPotatoes.Length; i++)
            {
                _tubHomes[i] = tubPotatoes[i].localPosition;
                tubPotatoes[i].gameObject.SetActive(false);
            }
            foreach (var s in emptySacks)
                if (s != null)
                    s.SetActive(false);
        }

        void Start()
        {
            _root = Core.GameRoot.Instance;
            SolidMouth(false);
        }

        /// <summary>
        /// Vùng nhắm miệng bao chỉ đặc khi có bao đặt cạnh thùng (lúc đó collider của bao tắt, vùng này thay chỗ bao);
        /// không có bao thì là trigger, kẻo thành tường vô hình chắn người chơi và NPC (QA-0030). Tia nhắm vẫn trúng trigger.
        /// </summary>
        void SolidMouth(bool solid)
        {
            if (mouth != null && mouth.TryGetComponent<Collider>(out var c))
                c.isTrigger = !solid;
        }

        protected override void OnActivated()
        {
            _rig = PlayerRig.Current;
            zone.Placed += OnPlaced;
        }

        protected override void OnCompleted()
        {
            if (zone != null)
                zone.Placed -= OnPlaced;
            // Chỉ trả lại góc nhìn/chiều cao đầu khi đang dốc bao; không thì không đụng tới (ghi đè trạng thái của việc khác).
            if (_pouring)
                EndPour();
        }

        void OnPlaced(Holdable h)
        {
            if (System.Array.IndexOf(sacks, h) < 0)
                return;
            _current = h;
            h.Body.interpolation = RigidbodyInterpolation.None;
            _basePos = h.transform.position - (tipPivot != null ? tipPivot.forward * .12f : Vector3.zero);
            h.transform.position = _basePos;
            _baseRot = h.transform.rotation;
            _mouthGrip = GripPoint(h.transform, "PourMouthGrip", new Vector3(-0.14f, 0.30f, -0.08f), new Vector3(0f, 10f, 80f));
            _bottomGrip = GripPoint(h.transform, "PourBottomGrip", new Vector3(0.15f, 0.20f, 0f), new Vector3(0f, -10f, -80f));
            // Bao đang đặt cạnh thùng: chỉ miệng bao nhận tương tác.
            h.SetCollidersEnabled(false);
            SolidMouth(true);
            Pour.Reset();
            BodyState.Current?.Exert(exertOnPlace);
            if (_root != null)
            {
                _root.State.Grime = Mathf.Min(0.6f, Mathf.Max(_root.State.Grime, _root.State.Grime + grimeOnPlace));
                _rig?.hands.SetGrime(_root.State.Grime);
            }
        }

        public override string PromptFor(TaskTarget target, Interactor who)
        {
            if (target != mouth || _current == null || Pour.Empty)
                return null;
            if (!who.HandsFree)
                return "prompt.hands_busy";
            if (!Pour.Untied)
                return "prompt.untie";
            return TubAway ? "prompt.tub_away" : "prompt.pour_sack";
        }

        public override bool Engaged(Interactor who) => System.Array.IndexOf(sacks, who.Holding) >= 0 || (_current != null && !Pour.Empty);

        public override bool Guide(Interactor who, out Transform target, out string key)
        {
            if (System.Array.IndexOf(sacks, who.Holding) >= 0)
                return Step(zone, "guide.sack_place", out target, out key);
            if (_current != null && !Pour.Empty)
                return Step(mouth, Pour.Untied ? "guide.sack_pour" : "guide.sack_untie", out target, out key);
            // Bao gần nhất còn trong kho lạnh.
            Holdable best = null;
            float bestDistance = float.MaxValue;
            foreach (var s in sacks)
            {
                if (s == null || !s.gameObject.activeInHierarchy || s.Locked || s.IsHeld)
                    continue;
                float d = (s.transform.position - who.transform.position).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = s; }
            }
            return Step(best, best != null ? "guide.sack_take" : null, out target, out key);
        }

        public override bool CanInteract(TaskTarget target, Interactor who) =>
            target == mouth && _current != null && !Pour.Untied && who.HandsFree;

        public override void OnInteract(TaskTarget target, Interactor who)
        {
            if (!CanInteract(target, who))
                return;
            // Bước tới cách miệng bao StandBack (tốc độ đi bộ, đứng thẳng, giữ hướng nhìn): gần hơn thì bao lọt vào đùi, nhìn vào miệng
            // bao thấy tạp dề bên trong (ghi chú 15/10).
            _rig.StepToWork(new Vector3(mouth.transform.position.x, _rig.transform.position.y, mouth.transform.position.z - StandBack));
            Pour.Untie();
            SetOpen(_current, true);
            // Đang giữ bao trên thùng: thả chuột chỉ nghỉ dốc, hai tay vẫn đỡ bao.
            _rig.motor.MovementLocked = true;
            _rig.hands.SetWorkGrips(_mouthGrip, _bottomGrip);
            _root?.Audio.PlaySfx("SFX/rope");
            ReportProgress();
        }

        protected override void Update()
        {
            base.Update();
            if (State != TaskState.Active || _current == null || _rig == null || _root == null || !Pour.Untied || Pour.Empty)
                return;
            var interactor = _rig.interactor;
            bool held = _root.Input.UseHeld && interactor.HandsFree;
            bool pouring = held && !TubAway && (_pouring || interactor.IsLookingAt(mouth));
            if (pouring)
            {
                var body = BodyState.Current;
                float speed = body != null && body.Simulate ? body.Logic.WorkSpeed : 1f;
                float dy = _root.Input.GameplayEnabled ? _root.Input.LookRaw.y : 0f;
                int n = Pour.Tick(true, dy, speed);
                if (n > 0)
                {
                    body?.Exert(exertOnPour * n);
                    _root.Audio.PlaySfxAt("SFX/potatoes", _current.transform.position, 0.7f);
                    Spill(n);
                }
                Tilt(Pour.Progress);
                _root.UI.Prompt.SetProgress(Pour.Progress);
                if (Pour.Empty)
                {
                    EndPour();
                    Emptied();
                    return;
                }
            }
            if (pouring != _pouring)
            {
                _pouring = pouring;
                // Đang dốc: chuột dùng để nghiêng bao, không xoay camera.
                _rig.look.Locked = pouring;
                _rig.hands.SetWorkGrips(_mouthGrip, _bottomGrip);
                if (!pouring)
                    _root.UI.Prompt.SetProgress(null);
            }
        }

        static Transform GripPoint(Transform sack, string name, Vector3 position, Vector3 angles)
        {
            var grip = sack.Find(name);
            if (grip == null)
            {
                grip = new GameObject(name).transform;
                grip.SetParent(sack, false);
            }
            grip.localPosition = position;
            grip.localRotation = Quaternion.Euler(angles);
            return grip;
        }

        void EndPour()
        {
            if (_rig != null)
            {
                _rig.look.Locked = false;
                _rig.motor.MovementLocked = false;
                if (_rig.interactor.HandsFree)
                    _rig.hands.SetWorkGrips(null, null);
            }
            _pouring = false;
            _root?.UI.Prompt.SetProgress(null);
        }

        /// <summary>Bao nghiêng quanh miệng bao về phía thùng theo tiến độ dốc.</summary>
        void Tilt(float progress)
        {
            if (_current == null)
                return;
            var t = _current.transform;
            t.SetPositionAndRotation(_basePos, _baseRot);
            if (tipPivot != null)
                t.RotateAround(tipPivot.position - tipPivot.forward * .12f, tipPivot.right, -tipDegrees * Mathf.SmoothStep(0f, 1f, progress));
            _current.Body.position = t.position; _current.Body.rotation = t.rotation;
        }

        void Spill(int count)
        {
            var from = _current != null ? _current.transform.TransformPoint(new Vector3(0f, 0.3f, 0f)) : transform.position;
            for (int i = 0; i < count && _inTub < tubPotatoes.Length; i++)
            {
                var p = tubPotatoes[_inTub];
                p.gameObject.SetActive(true);
                if (Application.isPlaying)
                    StartCoroutine(Roll(p, from, _tubHomes[_inTub], i * 0.08f));
                else
                    p.localPosition = _tubHomes[_inTub];
                _inTub++;
            }
        }

        IEnumerator Roll(Transform p, Vector3 from, Vector3 homeLocal, float delay)
        {
            Rolling++;
            p.position = from;
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            var to = p.parent != null ? p.parent.TransformPoint(homeLocal) : homeLocal;
            var spin = Random.rotationUniform;
            for (float t = 0f; t < 0.55f; t += Time.deltaTime)
            {
                float k = t / 0.55f;
                p.position = Vector3.Lerp(from, to, k) + Vector3.up * ((from.y - to.y) * k * (1f - k));
                p.rotation = Quaternion.Slerp(p.rotation, spin, k);
                yield return null;
            }
            p.localPosition = homeLocal;
            Rolling--;
        }

        void Emptied()
        {
            Delivered++;
            for (int i = 0; i < emptySacks.Length; i++)
                if (emptySacks[i] != null)
                    emptySacks[i].SetActive(i < Delivered);
            var h = _current;
            _current = null;
            SolidMouth(false);
            if (zone.Contents == h)
                zone.Take();
            _root?.Audio.PlaySfx("SFX/sack_thud", 0.5f);
            ReportProgress();
            Work(1);
            if (h != null)
            {
                h.gameObject.SetActive(false);
                if (Application.isPlaying)
                    StartCoroutine(ReturnLater(h));
            }
        }

        static void SetOpen(Holdable sack, bool open)
        {
            var closed = sack.transform.Find("Art");
            var opened = sack.transform.Find("OpenSack");
            if (opened == null) return;
            if (closed != null) closed.gameObject.SetActive(!open);
            opened.gameObject.SetActive(open);
        }

        /// <summary>Trả bao về kho khi người chơi không đứng trong kho (không thấy bao hiện ra).</summary>
        IEnumerator ReturnLater(Holdable h)
        {
            yield return new WaitForSeconds(clearDelay);
            while (coldRoom != null && coldRoom.PlayerInside)
                yield return new WaitForSeconds(0.5f);
            SetOpen(h, false);
            h.SetCollidersEnabled(true);
            h.GetComponent<ReturnToStart>()?.Restore();
            h.gameObject.SetActive(true);
        }

        /// <summary>Dốc hết bao đã đặt ngay (test, Sim).</summary>
        public void FinishPour()
        {
            if (_current == null)
                return;
            Spill(Pour.Finish());
            Tilt(1f);
            EndPour();
            Emptied();
        }

        public override void SimulateWork()
        {
            if (_current != null)
            {
                FinishPour();
                return;
            }
            Delivered++;
            Pour.Reset();
            Spill(Pour.Finish());
            for (int i = 0; i < emptySacks.Length; i++)
                if (emptySacks[i] != null)
                    emptySacks[i].SetActive(i < Delivered);
            Work(1);
        }

        /// <summary>
        /// Quản bếp dốc thùng vào nồi: từng củ rời thùng (lần lượt, theo lúc thùng nghiêng) rơi theo đường cong vào lòng nồi rồi
        /// chìm vào nồi; củ cuối chạm nồi thì thùng rỗng và gọi landed.
        /// </summary>
        public IEnumerator DumpTub(Vector3 into, float radius, System.Action landed)
        {
            var falling = new List<Transform>();
            for (int i = 0; i < _inTub && i < tubPotatoes.Length; i++)
                if (tubPotatoes[i] != null && tubPotatoes[i].gameObject.activeSelf)
                    falling.Add(tubPotatoes[i]);
            const float Fall = 0.38f;
            float stagger = falling.Count > 1 ? Mathf.Min(0.03f, 0.45f / falling.Count) : 0f;
            var from = new Vector3[falling.Count];
            var to = new Vector3[falling.Count];
            var started = new bool[falling.Count];
            for (int i = 0; i < falling.Count; i++)
            {
                float a = i * 2.39996f, r = radius * 0.55f * Mathf.Sqrt((i + 0.5f) / falling.Count);
                to[i] = into + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            }
            for (float t = 0f; t < Fall + stagger * falling.Count; t += Time.deltaTime)
            {
                for (int i = 0; i < falling.Count; i++)
                {
                    float k = (t - i * stagger) / Fall;
                    if (k <= 0f || !falling[i].gameObject.activeSelf) continue;
                    // Rời thùng đúng chỗ củ đang nằm lúc đó (thùng còn đang nghiêng).
                    if (!started[i]) { from[i] = falling[i].position; started[i] = true; }
                    if (k >= 1f) { falling[i].gameObject.SetActive(false); continue; }
                    var p = Vector3.Lerp(from[i], to[i], k);
                    p.y = Mathf.Lerp(from[i].y, to[i].y, k * k) + 0.06f * k * (1f - k);
                    falling[i].position = p;
                }
                yield return null;
            }
            ClearTub();
            landed?.Invoke();
        }

        /// <summary>Đầu ngày (hoặc khi người khác mang thùng đi): thùng rỗng.</summary>
        public void ClearTub()
        {
            _inTub = 0;
            foreach (var p in tubPotatoes)
                p.gameObject.SetActive(false);
        }
    }
}
