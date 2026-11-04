using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Player;
using HaiBanTay.Tasks;
using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Dọn bếp (F04): đầu ca, sàn còn vệt bẩn (vụn than, vỏ khoai) từ hôm trước. Cầm chổi ở móc, nhìn vào vệt bẩn, giữ
    /// chuột trái và rê chuột để quét; vệt nhạt dần (alpha clip qua MaterialPropertyBlock). Mỗi vệt sạch tính một đơn vị.
    /// </summary>
    public sealed class SweepStation : WorkStation
    {
        static readonly int CutoffId = Shader.PropertyToID("_Cutoff");

        public TaskTarget[] spots = new TaskTarget[0];
        public Renderer[] spotRenderers = new Renderer[0];
        public ToolHook hook;
        public float exertPerSecond = 0.004f;

        MaterialPropertyBlock _mpb;
        GameRoot _root;
        PlayerRig _rig;
        int _sweeping = -1;
        SweepLogic _logic;

        public override string Kind => OrderKinds.Sweep;
        public SweepLogic Logic => _logic ??= new SweepLogic(spots.Length);

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
            Logic.Scatter(count);
            Refresh();
        }

        protected override void Update()
        {
            base.Update();
            if (State != TaskState.Active || _rig == null || _root == null)
                return;
            var interactor = _rig.interactor;
            bool held = _root.Input.UseHeld && interactor.Tool == Tool.Broom;
            int target = -1;
            if (held)
            {
                if (_sweeping >= 0 && Logic.IsDirty(_sweeping))
                    target = _sweeping;
                else
                    for (int i = 0; i < spots.Length; i++)
                        if (Logic.IsDirty(i) && interactor.IsLookingAt(spots[i]))
                        {
                            target = i;
                            break;
                        }
            }
            if (target >= 0)
            {
                var body = BodyState.Current;
                float speed = body != null && body.Simulate ? body.Logic.WorkSpeed : 1f;
                var delta = _root.Input.GameplayEnabled ? _root.Input.LookRaw : Vector2.zero;
                bool cleaned = Logic.Tick(target, true, delta, Time.deltaTime, speed);
                if (delta.sqrMagnitude > 0.25f)
                {
                    body?.Exert(exertPerSecond * Time.deltaTime);
                    ReportProgress();
                }
                Apply(target);
                _root.UI.Prompt.SetProgress(Logic.Progress(target));
                if (cleaned)
                {
                    _root.Audio.PlaySfx("SFX/sweep");
                    Work(1);
                    target = -1;
                }
            }
            if (target != _sweeping)
            {
                bool was = _sweeping >= 0;
                _sweeping = target;
                // Đang quét: chuột đưa chổi, không xoay camera.
                _rig.look.Locked = target >= 0;
                if (was && target < 0)
                    _root.UI.Prompt.SetProgress(null);
            }
        }

        public override string PromptFor(TaskTarget target, Interactor who)
        {
            int i = System.Array.IndexOf(spots, target);
            if (i < 0 || !Logic.IsDirty(i))
                return null;
            if (who.Tool != Tool.Broom)
                return who.HandsFree ? "prompt.need_broom" : null;
            return "prompt.sweep";
        }

        public override bool Engaged(Interactor who) => _sweeping >= 0;

        public override bool Guide(Interactor who, out Transform target, out string key)
        {
            if (who.Tool != Tool.Broom)
                return Step(hook, "guide.take_broom", out target, out key);
            // Vệt bẩn gần nhất còn lại.
            TaskTarget best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < spots.Length; i++)
            {
                if (spots[i] == null || !Logic.IsDirty(i))
                    continue;
                float d = (spots[i].transform.position - who.transform.position).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = spots[i];
                }
            }
            return Step(best, best != null ? "guide.sweep" : null, out target, out key);
        }

        public override void SimulateWork()
        {
            int i = Logic.CleanOne();
            if (i < 0)
            {
                Logic.Scatter(1);
                i = Logic.CleanOne();
            }
            Apply(i);
            Work(1);
        }

        protected override void OnCompleted()
        {
            if (_sweeping >= 0 && _rig != null)
                _rig.look.Locked = false;
            _sweeping = -1;
            _root?.UI.Prompt.SetProgress(null);
            if (hook != null)
                hook.ReturnFrom(_rig != null ? _rig.interactor : null);
        }

        void Refresh()
        {
            for (int i = 0; i < spots.Length; i++)
                Apply(i);
        }

        void Apply(int i)
        {
            if (i < 0 || i >= spots.Length)
                return;
            bool dirty = Logic.IsDirty(i);
            if (spots[i] != null && spots[i].gameObject.activeSelf != dirty)
                spots[i].gameObject.SetActive(dirty);
            if (!dirty || i >= spotRenderers.Length || spotRenderers[i] == null)
                return;
            _mpb ??= new MaterialPropertyBlock();
            var r = spotRenderers[i];
            r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(CutoffId, Mathf.Lerp(0.02f, 1.01f, Logic.Progress(i)));
            r.SetPropertyBlock(_mpb);
        }
    }
}
