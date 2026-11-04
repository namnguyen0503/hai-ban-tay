using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Tasks;
using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Gọt rau củ ở bàn sơ chế; ông Mai làm mẫu, không phạt. Khoai (ngày 1): gọt hết xô, từng củ, từng dải, củ trắng thả
    /// nồi nước (PotatoPeeling). Măng tây (ngày 5): giữ chuột và rê dọc thân; gọt trụi thì ông Mai bỏ đi và chỉ lại.
    /// </summary>
    public sealed class PeelStation : WorkStation
    {
        public bool asparagus;
        public TaskTarget board;
        [Header("Măng tây")]
        public Transform knife, vegetable, peel, leftGrip, rightGrip;
        public GameObject asparagusVisual;
        [Header("Khoai")]
        public PotatoPeeling potatoes;
        [Tooltip("Nhả chuột lâu hơn chừng này (giây) thì đặt củ gọt dở xuống thớt; nhả rồi bấm lại ngay thì tay vẫn cầm.")]
        public float releaseDelay = 0.35f;
        public Transform maiSpot;
        public DayCast cast;
        public NpcRoutine[] nearbyCrew;
        bool[] _crewRunning;
        public Vector3 stand;
        public override string Kind => asparagus ? OrderKinds.Asparagus : OrderKinds.Peel;
        public int Pending { get; private set; }
        public int Peeled { get; private set; }
        /// <summary>Tiến độ của củ (thân măng) đang gọt, 0–1.</summary>
        public float Progress { get; private set; }
        public bool Ready { get; private set; }
        public bool CorrectMethod { get; private set; }
        public bool Discarded { get; private set; }
        public bool Demonstrating { get; private set; }
        public bool ShowingDemo { get; private set; }
        /// <summary>Hai tay đang ở việc gọt.</summary>
        public bool Working => _working;
        GameRoot _root;
        PlayerRig _rig;
        bool _working;
        float _stroke, _idle;
        Renderer _stalk;
        MaterialPropertyBlock _skin;
        Color _stalkColor;
        void Start()
        {
            _skin = new MaterialPropertyBlock();
            if (asparagusVisual == null) return;
            _stalk = asparagusVisual.transform.Find("Stalk").GetComponent<Renderer>();
            _stalkColor = _stalk.sharedMaterial.GetColor("_BaseColor");
        }
        public override void OnOrder(int count)
        {
            if (Pending == 0)
            {
                cast.ReservePeeling(true);
                _crewRunning = new bool[nearbyCrew.Length];
                for (int i = 0; i < nearbyCrew.Length; i++)
                {
                    var routine = nearbyCrew[i];
                    _crewRunning[i] = routine.enabled;
                    routine.enabled = false;
                    if (routine is CookRoutine cook) cook.ReturnTubBeforePause();
                    routine.agent.ClearWalkState();
                    routine.agent.Place(i == 0 ? new Vector3(-2.65f, 0f, 1.5f) : new Vector3(-3.4f, 0f, -1.85f), i == 0 ? 0f : 180f);
                    routine.agent.Play("Idle");
                }
            }
            Pending += count; Peeled = 0; CorrectMethod = !asparagus; Discarded = false;
            if (asparagus) asparagusVisual.SetActive(true);
            // Xô đủ khoai cho cả lượt, thêm một củ ông Mai gọt làm mẫu.
            else potatoes.Fill(Pending + 1);
            board.gameObject.SetActive(true); ResetPeel();
        }
        protected override void OnActivated() { _root = GameRoot.Instance; _rig = PlayerRig.Current; }
        public override string PromptFor(TaskTarget target, Interactor who)
        {
            if (target != board || Pending == 0 || Demonstrating) return null;
            return who.HandsFree || _working ? (Ready ? "prompt.peel_drag" : "prompt.peel_take") : "prompt.hands_busy";
        }
        public override bool Guide(Interactor who, out Transform target, out string key) =>
            Step(board, Pending == 0 || Demonstrating ? null : Ready ? "guide.peel_drag" : asparagus ? "guide.asparagus" : "guide.peel", out target, out key);
        public override bool CanInteract(TaskTarget target, Interactor who) => target == board && Pending > 0 && !Ready && !Demonstrating && who.HandsFree;
        public override void OnInteract(TaskTarget target, Interactor who)
        {
            if (!CanInteract(target, who)) return;
            if (!asparagus && Peeled == 0) StartCoroutine(Demonstrate(false));
            else Ready = true;
        }
        IEnumerator Demonstrate(bool correction)
        {
            Demonstrating = true; Ready = false;
            float oldSpeed = cast.mai.walkSpeed; float oldCarry = cast.mai.carrySpeed;
            cast.mai.walkSpeed = correction ? 2.2f : 1.4f; cast.mai.carrySpeed = 2.2f;
            // Tay ông cầm củ và dao thật (IK): phải chạy Animator suốt buổi dạy, kể cả khi người chơi đang nhìn thớt.
            cast.mai.keepAnimated = true;
            // Ông Mai đi vào từ hành lang (hoặc từ chỗ đang đứng trong bếp), không hiện ra giữa bếp.
            yield return cast.BringMai(maiSpot.position);
            cast.mai.Place(maiSpot.position, maiSpot.eulerAngles.y);
            cast.mai.Play("WorkTable");
            if (correction)
            {
                // Măng gọt trụi bị bỏ xuống biển; không trừ điểm, không phạt.
                var start = vegetable.position; var rotation = vegetable.rotation; var parent = vegetable.parent;
                cast.mai.PickUp(vegetable);
                cast.galleyDoor.Set(true);
                // Đứng cách lan can (z ≈ 6,2) một thân như chỗ lính tựa lan can (z 5,75): z 6,1 thì thân chồng Obstacles của boong (bot 23/10).
                yield return cast.mai.WalkPath(new Vector3(-2.2f, 0f, 0.8f), new Vector3(-3.35f, 0f, 1.2f),
                    new Vector3(-4.55f, 0f, 2.6f), new Vector3(-5.7f, 0f, 2.6f), new Vector3(-5.5f, 0f, 5.75f));
                var release = vegetable.position;
                cast.mai.PutDown(release, rotation, parent);
                for (float t = 0f; t < 0.8f; t += Time.deltaTime)
                { vegetable.position = Vector3.Lerp(release, new Vector3(-6.2f, -6f, 8f), t / 0.8f); yield return null; }
                Discarded = true; vegetable.gameObject.SetActive(false);
                cast.galleyDoor.Set(true);
                yield return cast.mai.WalkPath(new Vector3(-5.7f, 0f, 2.6f), new Vector3(-4.55f, 0f, 2.6f), new Vector3(-3.35f, 0f, 1.2f), new Vector3(-2.2f, 0f, 0.8f), maiSpot.position);
                vegetable.SetPositionAndRotation(start, rotation); vegetable.gameObject.SetActive(true);
                cast.mai.Place(maiSpot.position, maiSpot.eulerAngles.y); cast.mai.Play("WorkTable");
                yield return _root.Subtitles.PlayAndWait("mai_asparagus");
                CorrectMethod = true;
                ShowingDemo = true;
                for (float t = 0f; t < 1.8f; t += Time.deltaTime)
                { Progress = t / 1.8f; Pose(Progress); yield return null; }
                ShowingDemo = false;
            }
            else
            {
                // Ông Mai lấy một củ trong xô, gọt mẫu bằng chính tay mình trong lúc nói: từng dải dọc củ, xoay củ, rồi thả
                // củ trắng vào nồi nước.
                var ik = cast.mai.GetComponent<NpcArmIK>();
                if (ik == null) ik = cast.mai.gameObject.AddComponent<NpcArmIK>();
                cast.mai.Play("Idle");
                // Ba hỏi trước khi ông Mai gọt mẫu (DIRECTOR §2: câu hỏi ngày 1).
                yield return _root.Subtitles.PlayAndWait("ba_d1_peel");
                potatoes.BeginDemo(ik);
                bool said = false;
                StartCoroutine(Said(_root.Subtitles.PlayAndWait("mai_peel"), () => said = true));
                ShowingDemo = true;
                float total = PotatoPeelLogic.Bands * PotatoPeeling.DemoBandSeconds;
                for (float t = 0f; t < total + 0.2f; t += Time.deltaTime)
                { potatoes.DemoAt(t); Progress = Mathf.Clamp01(t / total); yield return null; }
                ShowingDemo = false;
                yield return potatoes.EndDemo();
                while (!said) yield return null;
            }
            ResetPeel();
            cast.mai.Play("Idle");
            cast.mai.keepAnimated = false;
            cast.mai.walkSpeed = oldSpeed; cast.mai.carrySpeed = oldCarry;
            Demonstrating = false; Ready = true;
        }
        static IEnumerator Said(IEnumerator line, System.Action done)
        {
            yield return line;
            done();
        }
        protected override void Update()
        {
            base.Update();
            if (State != TaskState.Active || _root == null || _rig == null) return;
            if (!asparagus) { UpdatePotatoes(); return; }
            if (Pending == 0 || Demonstrating) return;
            // Bắt đầu gọt cần đang nhìn củ; đã gọt thì giữ chuột là đủ. Tư thế làm việc đổi mắt/khung tay nên tia nhìn có thể
            // trượt khỏi củ; nếu bắt nhìn lại mỗi khung thì trạng thái đảo liên tục và hình nhấp nháy.
            bool looking = _working || _rig.interactor.IsLookingAt(board);
            bool working = Ready && _root.Input.UseHeld && looking && _rig.interactor.HandsFree;
            if (_working != working)
            {
                _working = working; _rig.look.Locked = working;
                if (working) StepToBoard();
                _rig.hands.SetPose(working ? HandPose.Carry : HandPose.Lowered);
                _rig.hands.SetWorkGrips(working ? leftGrip : null, working ? rightGrip : null);
                if (!working) _root.UI.Prompt.SetProgress(null);
            }
            if (!working) return;
            var d = _root.Input.LookRaw;
            // Chỉ rê dọc củ mới bong vỏ; rê ngang không được tính.
            if (Mathf.Abs(d.y) <= Mathf.Abs(d.x) * 0.5f || Mathf.Abs(d.y) < 0.5f) return;
            _stroke = Mathf.Clamp01(_stroke + d.y / 100f);
            Progress = Mathf.Min(1f, Progress + StrokeProgress(d.y, Time.deltaTime));
            Pose(_stroke); UpdateAsparagusSkin(); ReportProgress(); _root.UI.Prompt.SetProgress(Progress);
            if (Progress < 1f) return;
            _rig.look.Locked = false; _working = false;
            _rig.hands.SetWorkGrips(null, null); _rig.hands.SetPose(HandPose.Lowered);
            if (!CorrectMethod)
            { asparagusVisual.transform.localScale = new Vector3(0.3f, 1f, 0.3f); StartCoroutine(Demonstrate(true)); return; }
            Pending--; Peeled++; Work();
            Ready = false; ResetPeel();
            if (Pending == 0) { board.gameObject.SetActive(false); ReleaseCrew(); }
        }
        void UpdatePotatoes()
        {
            var p = potatoes;
            // Giữ chuột là gọt; đã cầm củ thì không cần nhìn trúng thớt (tư thế làm việc làm tia nhìn trượt, xem trên).
            bool want = Pending > 0 && Ready && !Demonstrating && _root.Input.UseHeld && (_working || _rig.interactor.HandsFree)
                && (_working || _rig.interactor.IsLookingAt(board));
            _idle = want ? 0f : _idle + Time.deltaTime;
            p.AutoNext = want;
            if (want && !_working) BeginPotatoWork();
            if (want && !p.Engaged && !p.Busy) p.Engage();
            // Nhả chuột rồi bấm lại ngay thì tay vẫn cầm củ; nhả lâu thì đặt củ gọt dở xuống thớt (không nhấp nháy lên xuống).
            if (!want && p.Engaged && !p.Busy && _idle > releaseDelay) p.Disengage();
            if (_working && !want && !p.Engaged && !p.Busy) { EndPotatoWork(); return; }
            if (!_working) return;
            Progress = p.Logic.Progress;
            _root.UI.Prompt.SetProgress(Pending > 0 && p.HasPotato ? Progress : (float?)null);
            if (!want || !p.Held || p.Busy) return;
            var d = _root.Input.LookRaw;
            // Kéo dao dọc củ (rê dọc) mới bong vỏ; rê ngang không được tính.
            if (Mathf.Abs(d.y) <= Mathf.Abs(d.x) * 0.5f || Mathf.Abs(d.y) < 0.5f) return;
            bool finished = p.Peel(StrokeProgress(d.y, Time.deltaTime));
            Progress = p.Logic.Progress; ReportProgress();
            if (!finished) return;
            // Củ trắng: tay thả vào nồi nước; còn khoai thì tự lấy củ kế khi người chơi vẫn giữ chuột.
            Pending--; Peeled++; Work();
            if (Pending == 0) { p.AutoNext = false; ReleaseCrew(); }
        }
        void BeginPotatoWork()
        {
            _working = true; _rig.look.Locked = true;
            StepToBoard();
            _rig.hands.SetWorkGrips(potatoes.leftGrip, potatoes.rightGrip, HandPose.Grip);
        }
        void EndPotatoWork()
        {
            _working = false; _rig.look.Locked = false;
            _rig.hands.SetWorkGrips(null, null); _rig.hands.SetPose(HandPose.Lowered);
            _root.UI.Prompt.SetProgress(null);
        }
        /// <summary>Bước sát mép bàn (tốc độ đi bộ, giữ hướng nhìn): củ cầm trước bụng trong tầm tay đứng thẳng; game không tự xoay hay
        /// dịch chuyển tức thời camera (17/10).</summary>
        void StepToBoard()
        {
            var flat = _rig.Position - stand; flat.y = 0f;
            if (flat.magnitude > 0.03f) _rig.StepToWork(stand);
        }
        /// <summary>Tiến độ gọt của một khung: theo quãng rê dọc củ (pixel), trần theo tốc độ rê, không theo số khung hình
        /// (30 fps và 60 fps gọt nhanh như nhau, QA-0005).</summary>
        public static float StrokeProgress(float dy, float dt) =>
            Mathf.Min(Mathf.Abs(dy), MaxStrokePixelsPerSecond * Mathf.Max(dt, 1e-4f)) / StrokePixelsForFull;

        /// <summary>Quãng rê (pixel) để gọt xong một củ, và trần tốc độ rê tính tiến độ.</summary>
        public const float StrokePixelsForFull = 900f, MaxStrokePixelsPerSecond = 600f;

        void Pose(float t)
        {
            knife.localPosition = new Vector3(0.045f, 0.07f, Mathf.Lerp(-0.14f, CorrectMethod ? 0.02f : 0.14f, t));
            knife.localRotation = Quaternion.Euler(0f, -8f, -15f);
            peel.localScale = new Vector3(1f, 1f, Mathf.Max(0.02f, Progress));
            peel.gameObject.SetActive(true);
        }
        void UpdateAsparagusSkin()
        {
            if (!asparagus || _stalk == null) return;
            _skin.SetColor("_BaseColor", Color.Lerp(_stalkColor, new Color(0.85f, 0.88f, 0.64f), CorrectMethod ? Progress * 0.2f : Progress));
            _stalk.SetPropertyBlock(_skin);
            foreach (Transform part in asparagusVisual.transform)
                if (part.name == "TipScale") part.gameObject.SetActive(CorrectMethod || Progress < 0.8f);
        }
        void ResetPeel()
        {
            Progress = 0f; _stroke = 0f;
            if (!asparagus) return;
            peel.gameObject.SetActive(false);
            asparagusVisual.transform.localScale = Vector3.one; UpdateAsparagusSkin();
        }
        public override void SimulateWork()
        {
            if (Pending == 0) return;
            CorrectMethod = true; Discarded |= asparagus; Pending--; Peeled++;
            if (!asparagus)
            {
                potatoes.SimulateOne();
                // Hết lượt thì cả củ ông Mai gọt mẫu cũng đã nằm trong nồi.
                while (Pending == 0 && potatoes.InBucket > 0) potatoes.SimulateOne();
            }
            Work();
            if (Pending == 0) ReleaseCrew();
        }
        void ReleaseCrew()
        {
            cast.ReservePeeling(false);
            if (_crewRunning != null)
                for (int i = 0; i < nearbyCrew.Length; i++)
                {
                    var routine = nearbyCrew[i];
                    // Routine chạy lại từ chỗ đang đứng: tự đi (NavMesh) tới nồi/thùng than kế tiếp, không dịch tức thời.
                    routine.enabled = _crewRunning[i];
                }
            _crewRunning = null;
        }
        protected override void OnCompleted()
        {
            ReleaseCrew();
            if (_rig != null) { _rig.look.Locked = false; _rig.hands.SetWorkGrips(null, null); }
            _working = false; board.gameObject.SetActive(false);
            if (!asparagus) potatoes.Clear();
        }
    }
}
