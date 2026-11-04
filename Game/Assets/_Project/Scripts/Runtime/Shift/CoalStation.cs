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
    /// Xúc than, đốt lò (F03, F04): cầm xẻng ở móc, giữ chuột trái ở thùng than để xúc đầy, mang tới miệng lò, nhả để đổ.
    /// Lửa lò (FireLogic) tự hạ theo thời gian nên phải xúc thêm định kỳ; ánh lửa, độ ấm quanh lò theo mức lửa.
    /// Thùng than cạn thì xuống hầm than vác sọt than lên, đứng trước thùng nghiêng sọt dần tới úp ngược để đổ (PourMotion),
    /// rồi mang sọt rỗng về chỗ cũ trong hầm than (hầm than là bố cục dàn dựng); sọt được xúc đầy lại khi Ba đã ra khỏi hầm.
    /// </summary>
    public sealed class CoalStation : WorkStation
    {
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public TaskTarget bin;
        /// <summary>Miệng lò của ô bếp đầu tiên (giữ cho test/hướng nhìn); đủ ô nằm trong cells.</summary>
        public TaskTarget stove;
        /// <summary>Mỗi ô bếp (ba nắp mỗi lò) có buồng đốt, miệng nạp và lửa riêng; phải xúc than cho toàn bộ ô.</summary>
        public FireCell[] cells = new FireCell[0];
        [Tooltip("Một xẻng làm đầy một ô trong bao lâu (nhiên liệu cộng vào ô).")]
        public float fuelPerPour = 4.5f;
        [Tooltip("Bếp coi như đầy lửa khi chừng này ô đang cháy đủ.")]
        public int cellsForFullFire = 3;
        public ToolHook hook;
        public Light fireLight;
        public Renderer fireGlow;
        public HeatSource heat;
        /// <summary>Lửa nhấp nháy (đèn, than hồng, ngọn lửa); có thì nó nhận mức lửa thay cho fireLight/fireGlow trực tiếp.</summary>
        public FireFlicker flicker;
        [Header("Thùng than / hầm than")]
        public Transform binPile;
        public Holdable[] baskets = new Holdable[0];
        /// <summary>Chỗ để sọt trong hầm than (nhắm vào đây khi mang sọt rỗng về).</summary>
        public TaskTarget basketReturn;
        public PlayerArea hold;
        public Transform[] basketHomes = new Transform[0];
        public MealPots meals;
        public AshStation ashStation;
        public HelperRoutine helper;
        public float initialFuel, initialHeat;
        public float pourSeconds = 1.4f;
        public float exertPerBasket = 0.02f;
        public Color coldFire = new Color(1f, 0.45f, 0.2f);
        public Color hotFire = new Color(1f, 0.72f, 0.38f);
        public float maxFireIntensity = 4f;
        public float exertPerLoad = 0.008f;
        public float grimePerPour = 0.012f;
        public float grimeCap = 0.45f;

        MaterialPropertyBlock _mpb;
        GameRoot _root;
        PlayerRig _rig;
        bool _emptyHinted;
        Vector3 _pileScale;
        AudioSource _sizzle;
        readonly System.Collections.Generic.HashSet<Holdable> _empty = new System.Collections.Generic.HashSet<Holdable>();
        bool _pouringBasket;
        Coroutine _pour;
        // Khối than của sọt đang đổ: vị trí/cỡ gốc để trả lại nếu hết ca giữa lúc đổ.
        Transform _pourLoad;
        Vector3 _pourLoadPos, _pourLoadScale;
        Renderer[][] _basketRenderers;
        NpcHandWork _helperHands;
        bool _helperLoaded, _helperFunded;

        bool BasketVisible(Holdable basket)
        {
            var rig = _rig != null ? _rig : PlayerRig.Current;
            if (rig == null || rig.cam == null) return true;
            int index = System.Array.IndexOf(baskets, basket);
            if (_basketRenderers == null || index < 0) return true;
            foreach (var r in _basketRenderers[index])
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var bounds = r.bounds;
                for (int corner = 0; corner < 9; corner++)
                {
                    var point = corner == 8 ? bounds.center : bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    var v = rig.cam.WorldToViewportPoint(point);
                    if (v.z <= 0f || v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f) continue;
                    if (!Physics.Linecast(rig.cam.transform.position, point, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
                        hit.transform.IsChildOf(basket.transform)) return true;
                }
            }
            return false;
        }

        public bool CanRestockBasket(Holdable basket) => basket != null && IsEmptyBasket(basket) && !basket.IsHeld && !basket.Busy &&
            (hold == null || !hold.PlayerInside) && !BasketVisible(basket);

        void ReturnBasket(Holdable basket)
        {
            int i = System.Array.IndexOf(baskets, basket);
            var home = i >= 0 && i < basketHomes.Length ? basketHomes[i] : null;
            if (home != null) basket.Detach(home.position, home.rotation, physics: false);
        }

        [System.Serializable]
        public class FireCell
        {
            public TaskTarget mouth;
            public FireFlicker flicker;
            public Transform hob;
            public Transform ashDoorFrame;
            public Transform ashHinge;
            public TaskTarget ashDoor, bucketSpot;
            public Transform ashPile;
            /// <summary>Ô thuộc lò nào; danh tính giữ nguyên từ builder tới lúc cào tro.</summary>
            public int stoveIndex;
        }

        public override string Kind => OrderKinds.Fire;
        FireLogic _fire;
        public FireLogic Fire => _fire ??= new FireLogic(() => { EnsureCells(); return _cellFire; }, () => CellCount == 0 ? 1 : cellsForFullFire);
        public ShovelLogic Shovel { get; } = new ShovelLogic();
        public CoalBinLogic Bin { get; } = new CoalBinLogic();
        public int Refills { get; private set; }

        // Mục tiêu đọc lượng than còn trong ô; việc làm sớm giữ nguyên cho đến khi lượng than thực sự cháy hết.
        int _goalCells;
        FireCellState[] _cellFire = new FireCellState[0];
        int _aim = -1;

        int CellCount => cells != null ? cells.Length : 0;
        bool CanFeed(int i) => i >= 0 && i < CellCount && cells[i].mouth != null &&
            FireCellState.MaxFuel - CellState(i).Fuel >= fuelPerPour - 0.0001f;
        int CellOf(TaskTarget t)
        {
            for (int i = 0; i < CellCount; i++)
                if (cells[i].mouth == t)
                    return i;
            return -1;
        }

        public override void OnOrder(int count) => _goalCells = Mathf.Clamp(count, 0, Mathf.Max(1, CellCount));

        /// <summary>Số xẻng còn phải đổ cho lệnh đang mở.</summary>
        public int PendingPours => PoursNeeded(_goalCells);
        /// <summary>Mức lửa từng ô (0 nếu chưa có ô).</summary>
        public float CellLevel(int i) => CellState(i)?.Heat ?? 0f;
        public FireCellState CellState(int i)
        {
            EnsureCells();
            return i >= 0 && i < _cellFire.Length ? _cellFire[i] : null;
        }

        public int CellForHob(Transform hob)
        {
            for (int i = 0; i < CellCount; i++) if (cells[i].hob == hob) return i;
            return -1;
        }

        public int PoursNeeded(int count)
        {
            EnsureCells();
            int fuelled = 0, cooking = 0;
            for (int i = 0; i < _cellFire.Length; i++)
            {
                if (_cellFire[i].Fuel > FireCellState.MaxFuel - fuelPerPour) fuelled++;
                else if (meals != null && i < CellCount && meals.UsesHob(cells[i].hob)) cooking++;
            }
            return Mathf.Max(cooking, Mathf.Max(0, Mathf.Min(count, _cellFire.Length) - fuelled));
        }

        public float TotalAsh
        {
            get { EnsureCells(); float ash = 0f; foreach (var c in _cellFire) ash += c.Ash; return ash; }
        }

        /// <summary>Tro đã cào khỏi mọi ô từ đầu ngày.</summary>
        public float TotalRemovedAsh
        {
            get { EnsureCells(); float ash = 0f; foreach (var c in _cellFire) ash += c.RemovedAsh; return ash; }
        }

        /// <summary>Ô cần than tiếp: ô còn được nhận mà lửa yếu nhất; hoà thì ô gần người hơn.</summary>
        int CellToFeed(Interactor who)
        {
            int best = -1;
            float bestLevel = 0f, bestDist = 0f;
            var p = who != null ? who.transform.position : transform.position;
            bool cookingFirst = false;
            for (int i = 0; i < CellCount; i++)
            {
                if (!CanFeed(i))
                    continue;
                bool cooking = meals != null && meals.UsesHob(cells[i].hob) && CellState(i).Fuel < FireCellState.FullFuel;
                float level = CellLevel(i), dist = (cells[i].mouth.transform.position - p).sqrMagnitude;
                if (best < 0 || (cooking && !cookingFirst) || (cooking == cookingFirst &&
                    (level < bestLevel - 0.05f || (Mathf.Abs(level - bestLevel) <= 0.05f && dist < bestDist))))
                {
                    best = i; bestLevel = level; bestDist = dist; cookingFirst = cooking;
                }
            }
            return best;
        }

        void EnsureCells()
        {
            int count = Mathf.Max(1, CellCount);
            if (_cellFire.Length == count)
                return;
            var old = _cellFire;
            _cellFire = new FireCellState[count];
            for (int i = 0; i < count; i++)
            {
                _cellFire[i] = i < old.Length ? old[i] : new FireCellState();
                if (i >= old.Length) _cellFire[i].Set(initialFuel, initialHeat);
            }
        }

        /// <summary>Đốt đúng một lần trên từng ô; Fire chỉ đọc tổng hợp.</summary>
        void TickCells(float dt)
        {
            Fire.Tick(dt);
        }

        protected override void Awake()
        {
            base.Awake();
            if (binPile != null)
                _pileScale = binPile.localScale;
        }

        void Start()
        {
            _root = GameRoot.Instance;
            if (helper != null) _helperHands = helper.GetComponent<NpcHandWork>();
            _basketRenderers = new Renderer[baskets.Length][];
            for (int i = 0; i < baskets.Length; i++)
                _basketRenderers[i] = baskets[i] != null ? baskets[i].GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            if (_root != null && stove != null)
                _sizzle = _root.Audio.StartLoop("SFX/sizzle", Audio.AudioChannel.Sfx, stove.gameObject, volume: 0f, spatial: true);
            ApplyFire();
            ApplyPile();
        }

        // Routine NPC chỉ được đọc: lấy cạnh xuất hiện/biến mất của tải than và trạng thái thao tác sẵn có.
        void LateUpdate()
        {
            if (helper == null || helper.shovelLoad == null) return;
            bool loaded = helper.enabled && helper.shovelLoad.activeSelf;
            if (loaded && !_helperLoaded)
            {
                _helperFunded = State == TaskState.Active && Bin.Take();
                if (!_helperFunded) { helper.shovelLoad.SetActive(false); loaded = false; }
                ApplyPile();
            }
            if (!loaded && _helperLoaded && _helperFunded)
            {
                int cell = -1;
                float distance = 0.65f * 0.65f;
                for (int i = 0; i < CellCount; i++)
                {
                    float d = (cells[i].mouth.transform.position - helper.shovel.position).sqrMagnitude;
                    if (d < distance) { distance = d; cell = i; }
                }
                if (helper.enabled && _helperHands != null && _helperHands.Activity == "feed" && CanFeed(cell))
                {
                    CellState(cell).AddCoal(fuelPerPour);
                    Work(1);
                }
                else Bin.Return();
                _helperFunded = false;
                ApplyPile();
            }
            _helperLoaded = loaded;
        }

        /// <summary>Đầu ngày: số xẻng còn trong thùng.</summary>
        public void SetStock(int stock)
        {
            Bin.Set(stock < 0 ? Bin.Capacity : stock);
            _emptyHinted = false;
            ApplyPile();
        }

        protected override void OnActivated()
        {
            _rig = PlayerRig.Current;
        }

        protected override void Update()
        {
            base.Update();
            TickCells(Time.deltaTime);
            TidyBaskets(Time.deltaTime);
            if (_sizzle != null)
                _sizzle.volume = Time.timeScale > 0f && State == TaskState.Active
                    ? Fire.Level * 0.3f * _root.Audio.Volume(Audio.AudioChannel.Sfx) : 0f;
            if (heat != null)
                heat.Level = Fire.Level;
            ApplyFire();
            if (State != TaskState.Active || _rig == null || _root == null)
                return;

            var interactor = _rig.interactor;
            if (interactor.Tool != Tool.Shovel)
            {
                if (Shovel.Loaded) Bin.Return();
                if (Shovel.Loaded || Shovel.Fill > 0f)
                {
                    Shovel.Reset();
                    ApplyPile();
                }
                return;
            }
            bool held = _root.Input.UseHeld;
            bool atBin = interactor.IsLookingAt(bin) && !Bin.Empty;
            _aim = -1;
            for (int i = 0; i < CellCount && _aim < 0; i++)
                if (cells[i].mouth != null && interactor.IsLookingAt(cells[i].mouth))
                    _aim = i;
            if (_aim < 0 && CellCount == 0 && interactor.IsLookingAt(stove))
                _aim = 0;
            bool atStove = _aim >= 0 && (CellCount == 0 || CanFeed(_aim));
            var body = BodyState.Current;
            float speed = body != null && body.Simulate ? body.Logic.WorkSpeed : 1f;
            var e = Shovel.Tick(held, atBin, atStove, Time.deltaTime, speed);
            _root.UI.Prompt.SetProgress(!Shovel.Loaded && Shovel.Fill > 0f ? Shovel.Fill / ShovelLogic.FillSeconds : (float?)null);
            switch (e)
            {
                case ShovelEvent.Loaded:
                    Bin.Take();
                    ApplyPile();
                    if (Bin.Empty && !_emptyHinted)
                    {
                        _emptyHinted = true;
                        _root.UI.Prompt.ShowHint(_root.Content.GetString("hint.bin_empty"), 8f);
                    }
                    _rig.hands.SetShovelLoaded(true);
                    _root.Audio.PlaySfx("SFX/shovel");
                    body?.Exert(exertPerLoad);
                    ReportProgress();
                    break;
                case ShovelEvent.Poured:
                    _rig.hands.SetShovelLoaded(false);
                    _root.Audio.PlaySfx("SFX/coal_pour");
                    CellState(Mathf.Max(0, _aim)).AddCoal(fuelPerPour);
                    AddGrime(grimePerPour);
                    Work(1);
                    break;
                case ShovelEvent.Spilled:
                    _rig.hands.SetShovelLoaded(false);
                    break;
            }
        }

        void AddGrime(float amount)
        {
            float g = Mathf.Max(_root.State.Grime, Mathf.Min(grimeCap, _root.State.Grime + amount));
            _root.State.Grime = g;
            _rig.hands.SetGrime(g);
        }

        public override void SimulateWork()
        {
            EnsureCells();
            int low = CellCount > 0 ? CellToFeed(null) : 0;
            if (low < 0 || !Bin.Take()) return;
            CellState(low).AddCoal(fuelPerPour);
            ApplyPile();
            Work(1);
        }

        bool IsBasket(Holdable h) => h != null && System.Array.IndexOf(baskets, h) >= 0;
        public bool IsEmptyBasket(Holdable h) => h != null && _empty.Contains(h);
        public bool PouringBasket => _pouringBasket;

        public override bool CanInteract(TaskTarget target, Interactor who)
        {
            if (target == basketReturn)
                return IsEmptyBasket(who.Holding);
            if (target == bin && who.Tool == Tool.Shovel && Shovel.Loaded)
                return Bin.Stock < Bin.Capacity;
            return target == bin && IsBasket(who.Holding) && !IsEmptyBasket(who.Holding) && !_pouringBasket && Bin.Stock < Bin.Capacity;
        }

        public override void OnInteract(TaskTarget target, Interactor who)
        {
            if (!CanInteract(target, who))
                return;
            if (target == bin && who.Tool == Tool.Shovel && Shovel.Loaded)
            {
                Bin.Return();
                Shovel.Reset();
                _rig?.hands.SetShovelLoaded(false);
                ApplyPile();
                ReportProgress();
                return;
            }
            var h = who.Holding;
            if (target == basketReturn)
            {
                who.ReleaseHeld();
                ReturnBasket(h);
                h.Locked = true;
                _root?.Audio.PlaySfx("SFX/metal_clank", 0.4f);
                ReportProgress();
                if (Application.isPlaying)
                    StartCoroutine(RefillBasket(h));
                return;
            }
            if (Application.isPlaying && _rig != null)
                _pour = StartCoroutine(PourBasket(h));
            else
                FinishPour(h);
        }

        /// <summary>Nghiêng sọt dần tới úp ngược trên thùng; than rơi từ khoảng 90°, thùng đầy lên khi sọt gần úp hẳn.</summary>
        IEnumerator PourBasket(Holdable h)
        {
            _pouringBasket = true;
            var load = h.transform.Find("Load");
            Vector3 loadPos = load != null ? load.localPosition : Vector3.zero, loadScale = load != null ? load.localScale : Vector3.one;
            _pourLoad = load; _pourLoadPos = loadPos; _pourLoadScale = loadScale;
            // Miệng sọt trên đống than trong thùng, lật ra trước (quanh trục ngang): hai tay nắm hai quai bên giữ nguyên bên mình. Lật sang
            // trái 180° thì quai phải vòng qua đỉnh sang bên trái, hai tay chéo nhau, cổ tay trái hở 20 cm (PlayerViewSweep 15/10).
            var mouth = (binPile != null ? binPile.position : bin.transform.position) + Vector3.up * 0.32f;
            bool poured = false, sounded = false;
            _root?.Audio.PlaySfx("SFX/rope", 0.2f);
            yield return PourMotion.Run(h, _rig, new Vector3(0f, 0.34f, 0f), mouth, pourSeconds, reach: 0.62f, onTilt: angle =>
            {
                if (load == null)
                    return;
                // Khối than trượt khỏi miệng sọt xuống thùng rồi tan vào đống than trong thùng; sọt chỉ nghiêng tới PourMotion.TwoHandTilt
                // (150°, hai tay xuống đáy) nên than trượt hết trước góc đó.
                float k = Mathf.InverseLerp(85f, PourMotion.TwoHandTilt - 5f, angle);
                if (k > 0f && !sounded)
                {
                    sounded = true;
                    _root?.Audio.PlaySfx("SFX/coal_pour");
                }
                if (k > 0f && load.gameObject.activeSelf)
                {
                    var from = h.transform.TransformPoint(loadPos);
                    var to = binPile != null ? binPile.position : bin.transform.position;
                    load.position = Vector3.Lerp(from, to, k);
                    load.localScale = loadScale * Mathf.Lerp(1f, 0.45f, k);
                }
                if (angle >= PourMotion.TwoHandTilt - 5f && !poured)
                {
                    poured = true;
                    load.gameObject.SetActive(false);
                    load.localPosition = loadPos;
                    load.localScale = loadScale;
                    FinishPour(h);
                }
            });
            if (!poured)
                FinishPour(h);
            _pouringBasket = false;
            _pour = null;
        }

        void FinishPour(Holdable h)
        {
            Bin.Refill();
            Refills++;
            _emptyHinted = false;
            ApplyPile();
            SetLoad(h, false);
            BodyState.Current?.Exert(exertPerBasket);
            ReportProgress();
        }

        /// <summary>Montage màn đen vẫn dùng đúng sọt đầy có thật, rút tải rồi trả về anchor để tiếp than ngoài tầm nhìn.</summary>
        public bool RefillForMontage()
        {
            var basket = FullBasket();
            if (basket == null) return false;
            FinishPour(basket);
            ReturnBasket(basket);
            basket.Locked = true;
            if (Application.isPlaying) StartCoroutine(RefillBasket(basket));
            return true;
        }

        void SetLoad(Holdable h, bool full)
        {
            if (full) _empty.Remove(h);
            else _empty.Add(h);
            var load = h.transform.Find("Load");
            if (load != null)
                load.gameObject.SetActive(full);
        }

        /// <summary>Sọt rỗng nằm ở chỗ cũ: có người xúc đầy lại khi Ba không đứng trong hầm than.</summary>
        IEnumerator RefillBasket(Holdable h)
        {
            yield return new WaitForSeconds(0.5f);
            while (!CanRestockBasket(h))
                yield return new WaitForSeconds(0.5f);
            _root?.Audio.PlaySfx("SFX/shovel", 0.35f);
            SetLoad(h, true);
            h.Locked = false;
        }

        bool InHold(Holdable b) => hold != null && hold.Contains(b.transform.position + Vector3.up * 0.2f);

        /// <summary>Sọt rỗng đang không ở chỗ cũ (đang cầm hoặc bỏ giữa đường). Sọt để trong hầm than coi như đã mang về.</summary>
        Holdable StrayEmptyBasket()
        {
            foreach (var b in baskets)
                if (b != null && b.gameObject.activeInHierarchy && _empty.Contains(b) && !b.Locked && (b.IsHeld || !InHold(b)))
                    return b;
            return null;
        }

        float _tidyTimer;

        /// <summary>Sọt rỗng để trong hầm than (không đúng chỗ cũ): khi Ba rời hầm, tự về chỗ cũ và được xúc đầy lại.</summary>
        void TidyBaskets(float dt)
        {
            _tidyTimer -= dt;
            if (_tidyTimer > 0f || hold == null || hold.PlayerInside || _pouringBasket)
                return;
            _tidyTimer = 0.5f;
            foreach (var b in baskets)
            {
                if (b == null || !b.gameObject.activeInHierarchy || !_empty.Contains(b) || b.Locked || b.IsHeld || b.Busy || !InHold(b) || BasketVisible(b))
                    continue;
                ReturnBasket(b);
                b.Locked = true;
                if (Application.isPlaying)
                    StartCoroutine(RefillBasket(b));
            }
        }

        Holdable FullBasket()
        {
            Holdable best = null;
            foreach (var b in baskets)
                if (b != null && b.gameObject.activeInHierarchy && !b.Locked && !b.IsHeld && !_empty.Contains(b))
                    best = b;
            return best;
        }

        public override bool Engaged(Interactor who) => IsBasket(who.Holding) || StrayEmptyBasket() != null ||
                                                         (who.Tool == Tool.Shovel && Shovel.Loaded);

        public override bool Guide(Interactor who, out Transform target, out string key)
        {
            if (IsBasket(who.Holding))
                return IsEmptyBasket(who.Holding)
                    ? Step(basketReturn, "guide.coal_basket_return", out target, out key)
                    : Step(bin, "guide.coal_refill", out target, out key);
            // Xẻng đang đầy thì đổ vào lò trước (QA-0026): nhát vừa xúc có thể làm thùng hết than, nhưng việc trong tay vẫn là đổ.
            if (who.Tool == Tool.Shovel && Shovel.Loaded)
            {
                int feed = CellToFeed(who);
                return Step(feed >= 0 ? cells[feed].mouth : bin, feed >= 0 ? "guide.pour_coal" : "guide.return_coal", out target, out key);
            }
            // Sọt rỗng bỏ ngoài chỉ nhắc khi không còn than phải xúc; còn việc xúc thì chỉ bước xúc tiếp theo.
            var stray = StrayEmptyBasket();
            if (stray != null && PendingPours <= 0)
                return Step(stray, "guide.coal_basket_back", out target, out key);
            if (Bin.Empty)
                return GuideBasket(out target, out key);
            if (PendingPours <= 0)
                return Step(stove, "guide.fire_warming", out target, out key);
            if (who.Tool != Tool.Shovel)
                return Step(hook, "guide.take_shovel", out target, out key);
            if (!Shovel.Loaded)
                return Step(bin, "guide.shovel", out target, out key);
            int cell = CellToFeed(who);
            return Step(cell >= 0 ? cells[cell].mouth : stove, "guide.pour_coal", out target, out key);
        }

        /// <summary>Chỉ dẫn lệnh vác sọt than (CoalFetchStation).</summary>
        public bool GuideFetch(Interactor who, out Transform target, out string key)
        {
            if (IsBasket(who.Holding))
                return IsEmptyBasket(who.Holding)
                    ? Step(basketReturn, "guide.coal_basket_return", out target, out key)
                    : Step(bin, "guide.coal_refill", out target, out key);
            return GuideBasket(out target, out key);
        }

        bool GuideBasket(out Transform target, out string key)
        {
            var full = FullBasket();
            if (full != null) return Step(full, "guide.coal_basket", out target, out key);
            var stray = StrayEmptyBasket();
            if (stray != null) return Step(stray, "guide.coal_basket_back", out target, out key);
            return Step(basketReturn, "guide.coal_restock_wait", out target, out key);
        }

        void ApplyPile()
        {
            if (binPile == null)
                return;
            float k = Bin.Capacity > 0 ? (float)Bin.Stock / Bin.Capacity : 0f;
            binPile.gameObject.SetActive(k > 0.01f);
            binPile.localScale = new Vector3(_pileScale.x, _pileScale.y * Mathf.Lerp(0.25f, 1f, k), _pileScale.z);
        }

        protected override void OnCompleted()
        {
            _root?.UI.Prompt.SetProgress(null);
            if (Shovel.Loaded) Bin.Return();
            Shovel.Reset();
            ApplyPile();
            if (hook != null)
                hook.ReturnFrom(_rig != null ? _rig.interactor : null);
            // Kết thúc chỉ hủy chuyển động; không đổi chỗ hoặc tự đổ đầy vật đang nhìn thấy.
            if (_pour != null)
            {
                StopCoroutine(_pour);
                _pour = null;
                _pouringBasket = false;
                if (_pourLoad != null)
                {
                    _pourLoad.localPosition = _pourLoadPos;
                    _pourLoad.localScale = _pourLoadScale;
                }
            }
            foreach (var b in baskets)
            {
                if (b == null)
                    continue;
                PourMotion.Cancel(b, _rig);
            }
        }

        void ApplyFire()
        {
            float k = Fire.Level;
            var color = Color.Lerp(coldFire, hotFire, k);
            if (CellCount > 0)
            {
                EnsureCells();
                int lit = -1;
                float best = 0f;
                for (int i = 0; i < CellCount; i++)
                {
                    var c = cells[i];
                    float own = _cellFire[i].Heat;
                    if (c.flicker != null)
                        c.flicker.SetFire(own, Color.Lerp(coldFire, hotFire, own), maxFireIntensity * own);
                    if (own > best) { best = own; lit = i; }
                    if (c.ashPile != null)
                    {
                        c.ashPile.gameObject.SetActive(_cellFire[i].Ash > 0.001f);
                        var s = c.ashPile.localScale;
                        s.y = Mathf.Lerp(0.004f, 0.10f, Mathf.Clamp01(_cellFire[i].Ash));
                        c.ashPile.localScale = s;
                        var p = c.ashPile.localPosition;
                        p.y = 0.268f + s.y * 0.5f;
                        c.ashPile.localPosition = p;
                    }
                    if (c.ashHinge != null)
                        // Nắp cửa tro mở vào trong lò (lọt xuống khay tro), không chìa ra trước: chìa ra 14,5 cm ở độ cao 0,41 m thì bắp chân Ba đứng
                        // nâng nồi cao lún vào nắp 9,8 cm (bot ngày 5 20/10, QA-0124).
                        c.ashHinge.localRotation = Quaternion.Euler(ashStation != null && ashStation.BucketCell == i ? 75f : 0f, 0f, 0f);
                }
                // Một suất realtime di chuyển tới ô đang nóng nhất; mọi ô có emission/hạt riêng.
                if (fireLight != null)
                {
                    fireLight.intensity = maxFireIntensity * best;
                    fireLight.color = Color.Lerp(coldFire, hotFire, best);
                    if (lit >= 0 && cells[lit].hob != null) fireLight.transform.position = cells[lit].hob.position - Vector3.up * 0.34f;
                }
                return;
            }
            if (flicker != null)
            {
                flicker.SetFire(k, color, maxFireIntensity * k);
                return;
            }
            if (fireLight != null)
            {
                fireLight.intensity = maxFireIntensity * k;
                fireLight.color = color;
            }
            if (fireGlow != null)
            {
                _mpb ??= new MaterialPropertyBlock();
                fireGlow.GetPropertyBlock(_mpb);
                _mpb.SetColor(EmissionId, k > 0f ? color * (1.5f * k) : Color.black);
                fireGlow.SetPropertyBlock(_mpb);
            }
        }

        public override string PromptFor(TaskTarget target, Interactor who)
        {
            if (target == basketReturn)
                return IsEmptyBasket(who.Holding) ? "prompt.return_basket" : null;
            if (target == bin && who.Tool == Tool.Shovel && Shovel.Loaded)
                return Bin.Stock < Bin.Capacity ? "prompt.return_coal" : null;
            if (target == bin && IsBasket(who.Holding))
                return IsEmptyBasket(who.Holding) || _pouringBasket ? null : Bin.Stock < Bin.Capacity ? "prompt.refill_bin" : "prompt.bin_full";
            if (target == bin && Bin.Empty)
                return "prompt.bin_empty";
            if (who.Tool != Tool.Shovel)
                return target == bin ? "prompt.need_shovel" : null;
            if (target == bin && !Shovel.Loaded)
                return "prompt.shovel";
            if (Shovel.Loaded && (target == stove || CellOf(target) >= 0))
                return CellCount == 0 || CanFeed(CellOf(target)) ? "prompt.pour" : "prompt.stove_full";
            return null;
        }
    }
}
