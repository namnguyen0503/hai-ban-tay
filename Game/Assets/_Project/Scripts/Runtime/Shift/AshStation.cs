using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Player;
using HaiBanTay.Tasks;
using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Cào lò (F03): tro tích dưới ghi lò làm lửa không lên hết mức (FireLogic.Ash). Xô tro cất trong hầm than cạnh máng
    /// tro: xách xô ra đặt giữa, ngay trước cửa tro; cầm cào ở móc, nhìn vào cửa tro, giữ chuột trái và kéo chuột về phía
    /// mình; tro rơi vào xô. Xô đầy thì xách xô (cào tự về móc) tới máng tro đổ đi (vị trí máng do game dàn dựng), rồi cất
    /// xô về chỗ cũ trong hầm than. Cất xô xong mới tính một đơn vị: không bỏ xô giữa lối đi.
    /// </summary>
    public sealed class AshStation : WorkStation
    {
        public CoalStation coal;
        public TaskTarget door;
        public TaskTarget spot;
        public TaskTarget chute;
        /// <summary>Chỗ cất xô trong hầm than, cạnh máng tro (xô nằm đây lúc đầu ca và sau mỗi lần đổ).</summary>
        public TaskTarget store;
        public ToolHook hook;
        public Holdable bucket;
        /// <summary>Lớp tro trong xô: nâng lên theo độ đầy.</summary>
        public Transform bucketAsh;
        public Transform ashBottom, ashFull;
        public float exertPerStroke = 0.006f;
        public float grimePerStroke = 0.006f;
        public float grimeCap = 0.5f;

        GameRoot _root;
        PlayerRig _rig;
        bool _raking;
        bool _savedLookLock;
        Vector3 _ashLow, _ashHigh;

        public override string Kind => OrderKinds.Ash;
        public RakeLogic Rake { get; } = new RakeLogic();
        public int Emptied { get; private set; }
        /// <summary>Đã đổ tro, xô chưa cất về chỗ.</summary>
        public bool AwaitingStore { get; private set; }
        Coroutine _pour;

        /// <summary>Xô đang nằm đúng chỗ trước cửa tro.</summary>
        public bool BucketReady => BucketCell >= 0;
        public int BucketCell
        {
            get
            {
                if (bucket == null || bucket.IsHeld || bucket.Busy || coal == null) return -1;
                for (int i = 0; i < coal.cells.Length; i++)
                {
                    var anchor = coal.cells[i].bucketSpot;
                    if (anchor != null && (bucket.transform.position - anchor.transform.position).sqrMagnitude < 0.0025f) return i;
                }
                return -1;
            }
        }

        float _goal = -1f, _removedAtGoal;

        /// <summary>
        /// Chốt lượng tro của lệnh vừa ra (tro trên ghi + tro trong xô lúc này). Lò đang cháy sinh tro liên tục: đo theo tro sống thì
        /// lệnh không bao giờ xong khi tốc độ sinh tro ngang tốc độ cào (bot ngày 3 17/10: 102 lượt đổ xô, lệnh lên xuống 1–4/6 suốt
        /// 38 phút thật). Tro sinh sau lúc chốt thuộc về lệnh sau (ShiftDirector ra lệnh mới khi ghi lại đầy).
        /// </summary>
        public void PinGoal()
        {
            if (coal == null) return;
            _goal = coal.TotalAsh + Rake.AshAmount;
            _removedAtGoal = coal.TotalRemovedAsh;
        }

        public int BucketsNeeded(int requested = int.MaxValue)
        {
            float remaining = (coal != null ? coal.TotalAsh : 0f) + Rake.AshAmount;
            // Phần tro của lệnh đã chốt còn chưa cào (tro đã cào nhưng còn trong xô vẫn phải đem đổ); không quá tro thật đang có.
            if (_goal >= 0f && coal != null)
                remaining = Mathf.Min(remaining, Mathf.Max(0f, _goal - (coal.TotalRemovedAsh - _removedAtGoal)) + Rake.AshAmount);
            int buckets = Mathf.CeilToInt(Mathf.Max(0f, remaining - 0.05f) / Mathf.Max(0.001f, Rake.Capacity));
            if (Rake.InBucket > 0) buckets = Mathf.Max(1, buckets);
            if (AwaitingStore) buckets++;
            return Mathf.Min(Mathf.Max(0, requested), buckets);
        }

        int DoorCell(TaskTarget target)
        {
            if (coal != null)
                for (int i = 0; i < coal.cells.Length; i++) if (coal.cells[i].ashDoor == target) return i;
            return -1;
        }
        int SpotCell(TaskTarget target)
        {
            if (coal != null)
                for (int i = 0; i < coal.cells.Length; i++) if (coal.cells[i].bucketSpot == target) return i;
            return -1;
        }
        int NextCell()
        {
            int best = 0;
            if (coal != null)
                for (int i = 1; i < coal.cells.Length; i++)
                    if (coal.CellState(i).Ash > coal.CellState(best).Ash) best = i;
            return best;
        }

        void EndRaking()
        {
            if (_raking && _rig != null) _rig.look.Locked = _savedLookLock;
            _raking = false;
            Rake.Tick(false, false, 0f);
            _root?.UI.Prompt.SetProgress(null);
        }

        /// <summary>Xô nằm ở chỗ cất trong hầm than.</summary>
        public bool BucketStored => bucket != null && store != null && !bucket.IsHeld && !bucket.Busy &&
                                    (bucket.transform.position - store.transform.position).sqrMagnitude < 0.0025f;

        protected override void Awake()
        {
            base.Awake();
            if (bucketAsh != null)
            {
                _ashHigh = ashFull != null ? ashFull.localPosition : bucketAsh.localPosition;
                _ashLow = ashBottom != null ? ashBottom.localPosition : _ashHigh + Vector3.down * 0.22f;
            }
        }

        void Start()
        {
            _root = GameRoot.Instance;
            ApplyBucket();
        }

        protected override void OnActivated()
        {
            _rig = PlayerRig.Current;
        }

        protected override void Update()
        {
            base.Update();
            if (State != TaskState.Active || _rig == null || _root == null)
                return;
            // Xô rỗng về chỗ cất bằng đường khác (thả xuống đúng chỗ, tự về chỗ khi bỏ ngoài vùng làm): vẫn tính xong.
            if (AwaitingStore && BucketStored)
            {
                AwaitingStore = false;
                Work(1);
            }
            var interactor = _rig.interactor;
            int cell = BucketCell;
            bool can = interactor.Tool == Tool.Rake && cell >= 0 && !Rake.BucketFull && !AwaitingStore && coal.CellState(cell).Ash > 0.001f;
            bool held = _root.Input.UseHeld;
            bool atDoor = can && interactor.IsLookingAt(coal.cells[cell].ashDoor);
            bool raking = can && held && (atDoor || _raking);
            if (raking && !_raking)
            {
                _savedLookLock = _rig.look.Locked;
                _raking = true;
                _rig.look.Locked = true;
            }
            if (raking)
            {
                var body = BodyState.Current;
                float speed = body != null && body.Simulate ? body.Logic.WorkSpeed : 1f;
                float dy = _root.Input.GameplayEnabled ? _root.Input.LookRaw.y : 0f;
                if (Rake.Tick(true, true, dy, speed, coal.CellState(cell).Ash))
                    OnStroke(cell, body);
                _root.UI.Prompt.SetProgress(Rake.StrokeProgress);
            }
            if (!raking) EndRaking();
        }

        void OnStroke(int cell, BodyState body)
        {
            coal.CellState(cell).Rake(Rake.LastTransfer);
            body?.Exert(exertPerStroke);
            float g = Mathf.Max(_root.State.Grime, Mathf.Min(grimeCap, _root.State.Grime + grimePerStroke));
            _root.State.Grime = g;
            _rig.hands.SetGrime(g);
            _root.Audio.PlaySfx("SFX/rake");
            ReportProgress();
            ApplyBucket();
            if (Rake.BucketFull)
            {
                EndRaking();
            }
        }

        public override string PromptFor(TaskTarget target, Interactor who)
        {
            int cell = DoorCell(target);
            if (cell >= 0)
            {
                if (who.Tool != Tool.Rake)
                    return who.HandsFree && coal.CellState(cell).Ash > 0.001f ? "prompt.need_rake" : null;
                if (BucketCell != cell)
                    return "prompt.need_bucket";
                if (coal.CellState(cell).Ash <= 0.001f) return "prompt.ash_cell_empty";
                return Rake.BucketFull ? "prompt.bucket_full" : "prompt.rake";
            }
            if (SpotCell(target) >= 0)
                return who.Holding == bucket && !AwaitingStore ? "prompt.put_bucket" : null;
            if (target == store)
                return who.Holding == bucket && Rake.InBucket == 0 ? "prompt.store_bucket" : null;
            if (target == chute)
            {
                if (who.Holding != bucket)
                    return null;
                // Xô còn rỗng (xách đi trước khi cào): nói rõ phải làm gì, không im lặng.
                if (bucket.Busy)
                    return null;
                return Rake.InBucket > 0 ? "prompt.empty_ash" : AwaitingStore ? null : "prompt.ash_bucket_empty";
            }
            return null;
        }

        public override bool CanInteract(TaskTarget target, Interactor who)
        {
            if (SpotCell(target) >= 0)
                return who.Holding == bucket && !AwaitingStore;
            if (target == store)
                return who.Holding == bucket && Rake.InBucket == 0;
            if (target == chute)
                return who.Holding == bucket && Rake.InBucket > 0;
            return false;
        }

        public override void OnInteract(TaskTarget target, Interactor who)
        {
            if (!CanInteract(target, who)) return;
            if (SpotCell(target) >= 0 && who.Holding == bucket)
            {
                who.ReleaseHeld();
                bucket.Detach(target.transform.position, target.transform.rotation, physics: false);
                _root?.Audio.PlaySfx("SFX/metal_clank");
                ReportProgress();
            }
            else if (target == store && who.Holding == bucket && Rake.InBucket == 0)
            {
                who.ReleaseHeld();
                bucket.Detach(store.transform.position, store.transform.rotation, physics: false);
                _root?.Audio.PlaySfx("SFX/metal_clank");
                if (AwaitingStore)
                {
                    AwaitingStore = false;
                    Work(1);
                }
                else
                    ReportProgress();
            }
            else if (target == chute && who.Holding == bucket && Rake.InBucket > 0 && !bucket.Busy)
            {
                if (Application.isPlaying && _rig != null)
                    _pour = StartCoroutine(PourAsh());
                else
                    Dumped();
            }
        }

        /// <summary>Đưa miệng xô tới miệng máng, nghiêng dần tới úp ngược; tro trút hết khi xô gần úp hẳn.</summary>
        System.Collections.IEnumerator PourAsh()
        {
            var mouth = chute.transform.TransformPoint(new Vector3(0.34f, 1.0f, 0f));
            bool done = false;
            _root?.Audio.PlaySfx("SFX/ash_pour");
            yield return PourMotion.Run(bucket, _rig, new Vector3(0f, 0.34f, 0f), mouth, 1.1f, angle =>
            {
                // Xô chỉ nghiêng tới PourMotion.TwoHandTilt (150°, hai tay trượt xuống đáy): tro trút hết trước góc đó.
                if (bucketAsh != null && angle > 100f && Rake.InBucket > 0)
                    bucketAsh.localPosition = Vector3.Lerp(_ashHigh, _ashLow, Mathf.InverseLerp(100f, PourMotion.TwoHandTilt - 5f, angle));
                if (!done && angle >= PourMotion.TwoHandTilt - 5f)
                {
                    done = true;
                    Dumped();
                }
            });
            if (!done)
                Dumped();
            _pour = null;
        }

        void Dumped()
        {
            Rake.Empty();
            Emptied++;
            AwaitingStore = true;
            ApplyBucket();
            BodyState.Current?.Exert(exertPerStroke);
            ReportProgress();
        }

        public override void SimulateWork()
        {
            if (bucket == null || bucket.IsHeld || bucket.Busy) return;
            if (!AwaitingStore && Rake.InBucket == 0)
            {
                int cell = NextCell();
                var anchor = coal.cells[cell].bucketSpot;
                bucket.Detach(anchor.transform.position, anchor.transform.rotation, physics: false);
                for (int i = 0; i < Rake.StrokesPerBucket && coal.CellState(cell).Ash > 0.001f; i++)
                    if (Rake.Tick(true, true, -Rake.PixelsPerStroke, availableAsh: coal.CellState(cell).Ash))
                        coal.CellState(cell).Rake(Rake.LastTransfer);
            }
            if (Rake.InBucket > 0) Dumped();
            if (AwaitingStore && store != null)
            {
                bucket.Detach(store.transform.position, store.transform.rotation, physics: false);
                AwaitingStore = false;
                Work(1);
            }
        }

        public override bool Engaged(Interactor who) => who.Holding == bucket || AwaitingStore;

        public override bool Guide(Interactor who, out Transform target, out string key)
        {
            if (who.Holding == bucket)
            {
                if (Rake.InBucket > 0)
                    return Step(chute, "guide.ash_dump", out target, out key);
                if (AwaitingStore || coal.TotalAsh <= 0.001f)
                    return Step(store, "guide.ash_store", out target, out key);
                return Step(coal.cells[NextCell()].bucketSpot, "guide.ash_place", out target, out key);
            }
            if (BucketReady && !AwaitingStore)
            {
                if (Rake.BucketFull || (Rake.InBucket > 0 && coal.CellState(BucketCell).Ash <= 0.001f))
                    return Step(bucket, Rake.BucketFull ? "guide.ash_lift" : "guide.ash_lift_partial", out target, out key);
                if (coal.CellState(BucketCell).Ash <= 0.001f)
                    return Step(bucket, "guide.ash_move_bucket", out target, out key);
                if (who.Tool != Tool.Rake)
                    return Step(hook, "guide.take_rake", out target, out key);
                return Step(coal.cells[BucketCell].ashDoor, "guide.rake", out target, out key);
            }
            if (Rake.InBucket > 0)
                return Step(bucket, "guide.ash_lift", out target, out key);
            return Step(bucket, AwaitingStore ? "guide.ash_lift_store" : "guide.ash_get", out target, out key);
        }

        protected override void OnCompleted()
        {
            if (_pour != null)
            {
                StopCoroutine(_pour);
                _pour = null;
                PourMotion.Cancel(bucket, _rig);
            }
            EndRaking();
            if (hook != null)
                hook.ReturnFrom(_rig != null ? _rig.interactor : null);
            ApplyBucket();
        }

        void OnDisable()
        {
            if (_pour != null) { StopCoroutine(_pour); _pour = null; PourMotion.Cancel(bucket, _rig); }
            EndRaking();
        }

        void ApplyBucket()
        {
            if (bucketAsh == null)
                return;
            bucketAsh.gameObject.SetActive(Rake.InBucket > 0);
            bucketAsh.localPosition = Vector3.Lerp(_ashLow, _ashHigh, Rake.Fill);
        }
    }
}
