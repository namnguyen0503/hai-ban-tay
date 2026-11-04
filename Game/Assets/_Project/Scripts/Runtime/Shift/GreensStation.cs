using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Player;
using HaiBanTay.Tasks;
using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Nhặt rau (F03, F05) ở bàn sơ chế: E ở sọt rau để lấy một bó ra thớt; từng lá, giữ chuột trái và gạt chuột sang
    /// phải bỏ lá tốt vào rổ, sang trái bỏ lá úa vào thùng (SortLogic). Lá úa màu vàng nâu, rũ xuống. Mỗi bó tính một đơn vị.
    /// </summary>
    public sealed class GreensStation : WorkStation
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public TaskTarget crate;
        public TaskTarget board;
        /// <summary>Các lá của bó đang nhặt, xếp trên thớt (số phần tử = SortLogic.LeavesPerBunch).</summary>
        public Transform[] leaves = new Transform[0];
        /// <summary>Bó rau trong sọt, hiện theo số bó còn chờ.</summary>
        public GameObject[] crateBunches = new GameObject[0];
        public Transform rawBasket, cleanBasket, rawTablePoint;
        public TaskTarget cleanTarget, sink;
        public Transform[] keptLeaves = new Transform[0];
        public Transform basket;
        public Transform bin;
        public Color goodColor = new Color(0.07f, 0.24f, 0.035f);
        public Color wiltedColor = new Color(0.36f, 0.24f, 0.045f);
        public float exertPerLeaf = 0.001f;

        MaterialPropertyBlock _mpb;
        GameRoot _root;
        PlayerRig _rig;
        bool _sorting;
        bool _rawReady;
        bool _carryingRaw, _carryingClean, _inSink;
        int _batch, _sorted, _kept;
        Transform _rawParent, _cleanParent;
        Vector3 _rawHome, _cleanHome;
        Quaternion _rawRotation, _cleanRotation;
        Collider _rawCollider, _sinkCollider;
        // Va chạm cứng của rổ khi bưng trên tay: để bật thì CharacterController của Ba bị rổ đẩy lùi, leo lên rồi rơi khỏi tàu (QA-0144/0145).
        Collider[] _rawSolid, _cleanSolid;
        public bool RawReady => _rawReady;
        public bool CarryingRaw => _carryingRaw;
        public bool CarryingClean => _carryingClean;
        public bool InSink => _inSink;
        public bool ReadyToWash => _sorted > 0 && Logic.Pending == 0 && !Logic.HasBunch;
        public float WashProgress { get; private set; }
        public bool Washed => WashProgress >= 1f;
        Vector3[] _homes = new Vector3[0];
        Quaternion[] _homeRots = new Quaternion[0];
        float _wobble;
        Renderer[][] _leafRenderers;

        public override string Kind => OrderKinds.Greens;
        public SortLogic Logic { get; } = new SortLogic();

        protected override void Awake()
        {
            base.Awake();
            Logic.LeavesPerBunch = leaves.Length > 1 ? leaves.Length : Logic.LeavesPerBunch;
            _rawParent = rawBasket.parent; _rawHome = rawBasket.localPosition; _rawRotation = rawBasket.localRotation;
            _cleanParent = cleanBasket.parent; _cleanHome = cleanBasket.localPosition; _cleanRotation = cleanBasket.localRotation;
            _rawCollider = crate.GetComponent<Collider>(); _sinkCollider = sink.GetComponent<Collider>();
            _rawSolid = Solids(rawBasket); _cleanSolid = Solids(cleanBasket);
            _homes = new Vector3[leaves.Length];
            _homeRots = new Quaternion[leaves.Length];
            _leafRenderers = new Renderer[leaves.Length][];
            for (int i = 0; i < leaves.Length; i++)
            {
                _leafRenderers[i] = leaves[i].GetComponentsInChildren<Renderer>();
                _homes[i] = leaves[i].localPosition;
                _homeRots[i] = leaves[i].localRotation;
            }
        }

        void Start()
        {
            _root = GameRoot.Instance;
            Refresh();
        }

        protected override void OnActivated()
        {
            _rig = PlayerRig.Current;
        }

        public override void OnOrder(int count)
        {
            Logic.AddBunches(count);
            _batch = count; _sorted = _kept = 0; _rawReady = false;
            WashProgress = 0f;
            rawBasket.SetParent(_rawParent, false); rawBasket.SetLocalPositionAndRotation(_rawHome, _rawRotation);
            cleanBasket.SetParent(_cleanParent, false); cleanBasket.SetLocalPositionAndRotation(_cleanHome, _cleanRotation);
            _rawCollider.enabled = true;
            SetSolid(_rawSolid, true); SetSolid(_cleanSolid, true);
            foreach (var leaf in keptLeaves) leaf.gameObject.SetActive(false);
            Refresh();
        }

        public override string PromptFor(TaskTarget target, Interactor who)
        {
            if (target == crate && !_rawReady && !_carryingRaw && Logic.Pending > 0) return who.HandsFree ? "prompt.take_greens" : "prompt.hands_busy";
            if (target == board && _carryingRaw) return "prompt.place_raw_greens";
            if (target == board && _carryingClean && Washed) return "prompt.deliver_greens";
            if (target == board && _rawReady && Logic.Pending > 0 && !Logic.HasBunch) return "prompt.take_bunch";
            if (target == board && Logic.HasBunch) return "prompt.sort";
            if (target == cleanTarget && ReadyToWash && !_carryingClean && !_inSink) return "prompt.carry_greens_to_wash";
            if (target == sink && _carryingClean) return "prompt.place_greens_in_sink";
            if (target == sink && _inSink) return Washed ? "prompt.take_clean_greens" : "prompt.wash_greens";
            return null;
        }

        public override bool Engaged(Interactor who) => _carryingRaw || _carryingClean || _inSink;

        public override bool Guide(Interactor who, out Transform target, out string key)
        {
            if (_carryingRaw) return Step(board, "guide.greens_place", out target, out key);
            if (_carryingClean) return Washed ? Step(board, "guide.greens_deliver", out target, out key) : Step(sink, "guide.greens_sink", out target, out key);
            if (_inSink) return Step(sink, Washed ? "guide.greens_take_clean" : "guide.greens_wash", out target, out key);
            if (Logic.HasBunch) return Step(board, "guide.greens_sort", out target, out key);
            if (_rawReady && Logic.Pending > 0) return Step(board, "guide.greens_bunch", out target, out key);
            if (ReadyToWash) return Step(cleanTarget, "guide.greens_to_wash", out target, out key);
            return Step(crate, Logic.Pending > 0 ? "guide.greens_crate" : null, out target, out key);
        }

        public override bool CanInteract(TaskTarget target, Interactor who)
        {
            var key = PromptFor(target, who);
            bool free = who.HandsFree || ((_carryingRaw || _carryingClean) && who.Holding == null && who.Tool == Tool.None);
            return free && key != null && key != "prompt.sort" && key != "prompt.wash_greens";
        }

        public override void OnInteract(TaskTarget target, Interactor who)
        {
            if (!CanInteract(target, who)) return;
            if (target == crate)
            {
                _carryingRaw = true;
                _rawCollider.enabled = false;
                Carry(rawBasket);
            }
            else if (target == board && _carryingRaw)
            {
                _carryingRaw = false; _rawReady = true;
                rawBasket.SetParent(rawTablePoint, false);
                rawBasket.localPosition = Vector3.zero; rawBasket.localRotation = Quaternion.identity;
                SetSolid(_rawSolid, true);
                _rig.interactor.WorkCarrying = _rig.motor.Carrying = false;
                _rig.hands.SetPose(HandPose.Lowered);
            }
            else if (target == board && _carryingClean && Washed)
            {
                _carryingClean = false; _rawReady = false;
                cleanBasket.SetParent(_cleanParent, false); cleanBasket.SetLocalPositionAndRotation(_cleanHome, _cleanRotation);
                SetSolid(_cleanSolid, true);
                _sinkCollider.enabled = false;
                _rig.interactor.WorkCarrying = _rig.motor.Carrying = false;
                _rig.hands.SetPose(HandPose.Lowered);
                int units = _sorted; _sorted = 0;
                Work(units);
            }
            else if (target == board) Logic.TakeBunch();
            else if (target == cleanTarget)
            {
                _carryingClean = true; _sinkCollider.enabled = true; Carry(cleanBasket);
            }
            else if (target == sink && _carryingClean)
            {
                _carryingClean = false; _inSink = true;
                cleanBasket.SetParent(sink.transform, false);
                cleanBasket.localPosition = new Vector3(0f, -0.19f, 0f); cleanBasket.localRotation = Quaternion.identity;
                SetSolid(_cleanSolid, true);
                _rig.interactor.WorkCarrying = _rig.motor.Carrying = false;
                _rig.hands.SetPose(HandPose.Lowered);
            }
            else if (target == sink && _inSink && Washed)
            {
                _inSink = false; _carryingClean = true; Carry(cleanBasket);
            }
            ReportProgress(); Refresh();
        }

        Collider[] Solids(Transform item)
        {
            var list = new System.Collections.Generic.List<Collider>();
            foreach (var c in item.GetComponentsInChildren<Collider>(true))
                if (!c.isTrigger && c != _rawCollider) list.Add(c);
            return list.ToArray();
        }

        static void SetSolid(Collider[] colliders, bool on)
        {
            if (colliders == null) return;
            foreach (var c in colliders) if (c != null) c.enabled = on;
        }

        void Carry(Transform item)
        {
            SetSolid(item == cleanBasket ? _cleanSolid : _rawSolid, false);
            item.SetParent(_rig.hands.transform, false);
            item.localPosition = new Vector3(0f, -0.42f, 0.6f); item.localRotation = Quaternion.identity;
            _rig.interactor.WorkCarrying = _rig.motor.Carrying = true;
            _rig.hands.SetPose(HandPose.Carry);
        }

        void KeepLeaf()
        {
            if (_kept < keptLeaves.Length) keptLeaves[_kept++].gameObject.SetActive(true);
        }

        protected override void Update()
        {
            base.Update();
            if (State != TaskState.Active || _rig == null || _root == null)
                return;
            if (_inSink && !Washed)
            {
                bool washing = _root.Input.UseHeld && (_sorting || _rig.interactor.IsLookingAt(sink));
                if (washing)
                {
                    WashProgress = Mathf.Min(1f, WashProgress + Time.deltaTime / 2.5f);
                    ReportProgress();
                    _root.UI.Prompt.SetProgress(WashProgress);
                }
                if (washing != _sorting)
                {
                    _sorting = washing;
                    _rig.look.Locked = washing;
                    _rig.hands.SetPose(washing ? HandPose.Carry : HandPose.Lowered);
                    if (washing) _root.Audio.PlaySfxAt("SFX/water_wash", sink.transform.position, 0.6f);
                }
                return;
            }
            var interactor = _rig.interactor;
            bool held = _root.Input.UseHeld && interactor.HandsFree && Logic.HasBunch;
            bool sorting = held && (_sorting || interactor.IsLookingAt(board));
            float dx = sorting && _root.Input.GameplayEnabled ? _root.Input.LookRaw.x : 0f;
            int index = Logic.Index;
            var e = Logic.Tick(sorting, sorting, dx);
            switch (e)
            {
                case SortEvent.Kept:
                case SortEvent.Discarded:
                case SortEvent.BunchDone:
                    bool kept = e == SortEvent.Kept || (e == SortEvent.BunchDone && !Logic.LeafWilted(index));
                    if (kept) KeepLeaf();
                    StartCoroutine(Fly(index, kept ? basket : bin));
                    _root.Audio.PlaySfx("SFX/leaf");
                    BodyState.Current?.Exert(exertPerLeaf);
                    ReportProgress();
                    if (e == SortEvent.BunchDone)
                        _sorted++;
                    break;
                case SortEvent.Wrong:
                    _wobble = 0.4f;
                    break;
            }
            if (sorting != _sorting)
            {
                _sorting = sorting;
                // Đang gạt lá: chuột dùng để gạt, không xoay camera.
                _rig.look.Locked = sorting;
                _rig.hands.SetPose(sorting ? HandPose.Grip : HandPose.Lowered);
            }
            ShowCurrent();
        }

        /// <summary>Lá đang nhặt nhô lên và lệch theo hướng đang gạt; gạt nhầm thì lá lắc nhẹ.</summary>
        void ShowCurrent()
        {
            if (!Logic.HasBunch || Logic.Index >= leaves.Length)
                return;
            var leaf = leaves[Logic.Index];
            _wobble = Mathf.Max(0f, _wobble - Time.deltaTime);
            float shake = _wobble > 0f ? Mathf.Sin(_wobble * 60f) * 0.02f * _wobble / 0.4f : 0f;
            leaf.localPosition = _homes[Logic.Index] + new Vector3(Logic.Lean * 0.08f + shake, 0.03f, 0f);
        }

        IEnumerator Fly(int index, Transform to)
        {
            if (index < 0 || index >= leaves.Length || to == null)
                yield break;
            var leaf = leaves[index];
            var from = leaf.position;
            for (float t = 0f; t < 0.25f; t += Time.deltaTime)
            {
                float k = t / 0.25f;
                leaf.position = Vector3.Lerp(from, to.position, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.12f);
                yield return null;
            }
            // Đã lấy bó mới trong lúc lá đang bay: lá này thuộc bó mới, đặt lại chỗ.
            if (Logic.HasBunch && index >= Logic.Index)
            {
                leaf.localPosition = _homes[index];
                yield break;
            }
            leaf.gameObject.SetActive(false);
            if (!Logic.HasBunch)
                Refresh();
        }

        /// <summary>Hai lính nhặt giúp một bó (DayCast gọi định kỳ khi đứng ở bàn): tính một đơn vị như người chơi làm.</summary>
        public bool HelperSort()
        {
            if (State != TaskState.Active || !_rawReady || !Logic.HelperBunch())
                return false;
            _sorted++;
            for (int i = 0; i < 3; i++) KeepLeaf();
            Refresh();
            return true;
        }

        public override void SimulateWork()
        {
            if (Logic.Pending == 0 && !Logic.HasBunch)
                Logic.AddBunches(1);
            if (Logic.CompleteBunch())
            {
                _sorted++;
                for (int i = 0; i < 3; i++) KeepLeaf();
                if (Logic.Pending == 0 && !Logic.HasBunch)
                {
                    WashProgress = 1f;
                    int units = _sorted; _sorted = 0;
                    Work(units);
                }
            }
            Refresh();
        }

        protected override void OnCompleted()
        {
            if (_rig != null)
            {
                _rig.look.Locked = false;
                _rig.interactor.WorkCarrying = _rig.motor.Carrying = false;
                _rig.hands.SetPose(HandPose.Lowered);
            }
            _sorting = _carryingRaw = _carryingClean = _inSink = false;
            if (rawBasket != null && rawBasket.parent != _rawParent)
            {
                rawBasket.SetParent(_rawParent, false); rawBasket.SetLocalPositionAndRotation(_rawHome, _rawRotation);
            }
            if (cleanBasket != null)
            {
                cleanBasket.SetParent(_cleanParent, false); cleanBasket.SetLocalPositionAndRotation(_cleanHome, _cleanRotation);
            }
            if (_rawCollider != null) _rawCollider.enabled = true;
            SetSolid(_rawSolid, true); SetSolid(_cleanSolid, true);
            if (_sinkCollider != null) _sinkCollider.enabled = false;
        }

        void Refresh()
        {
            for (int i = 0; i < crateBunches.Length; i++)
                if (crateBunches[i] != null)
                    crateBunches[i].SetActive(i < Logic.Pending);
            _mpb ??= new MaterialPropertyBlock();
            for (int i = 0; i < leaves.Length; i++)
            {
                var leaf = leaves[i];
                bool show = Logic.HasBunch && i >= Logic.Index;
                leaf.gameObject.SetActive(show);
                if (!show)
                    continue;
                leaf.localPosition = _homes[i];
                bool wilted = Logic.LeafWilted(i);
                // Lá úa rũ xuống: phân biệt bằng hình dáng, không chỉ bằng màu.
                leaf.localRotation = _homeRots[i] * Quaternion.Euler(wilted ? 25f : 0f, 0f, wilted ? 18f : 0f);
                foreach (var r in _leafRenderers[i])
                {
                    r.GetPropertyBlock(_mpb);
                    _mpb.SetColor(BaseColorId, wilted ? wiltedColor : goodColor);
                    r.SetPropertyBlock(_mpb);
                }
            }
        }
    }
}
