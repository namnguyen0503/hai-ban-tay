using System.Collections.Generic;
using System.Linq;
using HaiBanTay.Npc;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Đo giật (phản hồi 14/10: ảnh tĩnh để lọt lỗi chuyển động). Sau mọi LateUpdate (sau IK, sau NpcToolFollow), lấy mẫu theo nhịp
    /// 60 lần mỗi giây giờ game (đúng như người chơi thấy ở 60 fps, không phụ thuộc khung batch rất ngắn): cổ tay trong hệ trục thân NPC
    /// (đi, quay người không tính), vật (muôi, nắp, nồi, thùng, xẻng) trong hệ thế giới. Nhảy = quãng dời một nhịp quá 2 cm và gấp
    /// 2,5 lần nhịp trước (cộng 0,5 cm): vật "bay" hay nhảy chỗ giữa hai khung. Kèm góc lớn nhất giữa hướng thân đang dời và hướng mặt
    /// (đi giật lùi, đi ngang).
    /// </summary>
    [DefaultExecutionOrder(14100)]
    public sealed class MotionProbe : MonoBehaviour
    {
        const float Tick = 1f / 60f;
        /// <summary>Quãng nhảy nhỏ nhất đáng kể trong một khung 60 fps (m).</summary>
        public const float PopStep = 0.02f;

        public NpcHandWork hands;
        readonly List<(string name, Transform t, bool local)> _items = new List<(string, Transform, bool)>();
        readonly List<Vector3> _last = new List<Vector3>();
        readonly List<float> _lastStep = new List<float>();
        readonly List<(float step, string text)> _pops = new List<(float, string)>();
        bool _primed, _rootPrimed;
        double _lastTime;
        float _lastInterval;
        Vector3 _lastRoot;

        /// <summary>Cú nhảy lớn nhất (m trong một khung 60 fps); 0 nếu không có.</summary>
        public float Worst { get; private set; }
        public string WorstWhat { get; private set; } = "";
        /// <summary>Quãng dời lớn nhất trong một khung 60 fps (kể cả nhanh mà đều), để tham khảo.</summary>
        public float Fastest { get; private set; }
        public string FastestWhat { get; private set; } = "";
        public int Samples { get; private set; }
        /// <summary>Nhịp dài quá 1,5 nhịp chuẩn (máy test khựng): không dùng làm mốc so nhảy.</summary>
        public int Hitches { get; private set; }
        /// <summary>Góc lớn nhất giữa hướng thân đang dời và hướng mặt (độ) khi đang đi: lớn là đi giật lùi, đi ngang.</summary>
        public float WorstWalkAngle { get; private set; }
        public string WorstWalkWhat { get; private set; } = "";

        /// <summary>Theo dõi một vật trong hệ thế giới (muôi, nắp, thùng…).</summary>
        public MotionProbe Watch(string name, Transform t) => Add(name, t, false);

        /// <summary>Cổ tay trái, phải của NPC có tay IK, trong hệ trục thân.</summary>
        public MotionProbe WatchWrists()
        {
            if (hands == null) return this;
            Add("cổ tay trái", hands.Wrist(true), true);
            return Add("cổ tay phải", hands.Wrist(false), true);
        }

        MotionProbe Add(string name, Transform t, bool local)
        {
            _items.Add((name, t, local));
            _last.Add(Vector3.zero);
            _lastStep.Add(-1f);
            _primed = false;
            return this;
        }

        public void Restart()
        {
            Worst = Fastest = WorstWalkAngle = 0f;
            WorstWhat = FastestWhat = WorstWalkWhat = "";
            Samples = Hitches = 0;
            _pops.Clear();
            for (int i = 0; i < _lastStep.Count; i++) _lastStep[i] = -1f;
            _primed = _rootPrimed = false;
        }

        public string Report => $"nhảy lớn nhất {Worst * 100f:F1} cm/khung ở {WorstWhat}, dời nhanh nhất {Fastest * 100f:F1} cm/khung ({Fastest / Tick:F1} m/s) ở {FastestWhat}, " +
                                $"hướng đi lệch mặt tối đa {WorstWalkAngle:F0}° ({WorstWalkWhat}), {Samples} nhịp ({Hitches} nhịp khựng bỏ qua)"
                                + (_pops.Count > 0 ? "; nhảy: " + string.Join("; ", _pops.OrderByDescending(p => p.step).Take(6).Select(p => p.text)) : "");

        Animator _animator;

        /// <summary>Clip đang chạy của NPC (tên trong danh sách hay gặp), thời điểm, có đang chuyển clip, tốc độ Animator.</summary>
        string AnimState()
        {
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            if (_animator == null) return "";
            var info = _animator.GetCurrentAnimatorStateInfo(0);
            string Name(AnimatorStateInfo i) => new[] { "Idle", "Walk", "PickUp", "CarryIdle", "CarryWalk", "IdleLook", "Stir", "WorkMid" }.FirstOrDefault(i.IsName) ?? i.shortNameHash.ToString();
            string text = $"clip {Name(info)} {info.normalizedTime:F2}, tốc độ {_animator.speed:F1}";
            if (_animator.IsInTransition(0))
                text += $" → {Name(_animator.GetNextAnimatorStateInfo(0))} {_animator.GetAnimatorTransitionInfo(0).normalizedTime:F2}";
            return text;
        }

        /// <summary>Ghi vết từng khung (không theo nhịp) của 10 khung gần nhất; có nhảy thì in vết ra log (MOTION_TRACE) để truy nguồn.</summary>
        public bool trace;
        readonly Queue<string> _frames = new Queue<string>();
        int _traced;

        void Trace()
        {
            var root = transform;
            var sb = new System.Text.StringBuilder();
            sb.Append($"f{Time.frameCount} t{Time.time:F3} dt{Time.deltaTime * 1000f:F0}ms {(hands != null ? hands.Activity + "/" + hands.Step : "")} {AnimState()}");
            for (int i = 0; i < _items.Count; i++)
            {
                var (name, t, local) = _items[i];
                if (t == null) continue;
                var p = local ? root.InverseTransformPoint(t.position) : root.InverseTransformPoint(t.position);
                sb.Append($" | {name} {p.x * 100f:F1},{p.y * 100f:F1},{p.z * 100f:F1}");
            }
            var follow = GetComponent<NpcToolFollow>();
            sb.Append($" | follow {(follow != null && follow.tool != null ? follow.tool.name : "-")}");
            if (hands != null && hands.arms != null)
                for (int k = 0; k < 2; k++)
                {
                    bool left = k == 0;
                    var h = hands.arms.Hand(left);
                    var target = left ? hands.arms.leftTarget : hands.arms.rightTarget;
                    float w = left ? hands.arms.leftWeight : hands.arms.rightWeight;
                    sb.Append($" | IK{(left ? "L" : "R")} w{w:F2} {(target != null && h != null ? $"miss {(h.position - target.position).magnitude * 100f:F1} cm ({target.name})" : "-")}");
                }
            sb.Append($" | lean {hands?.lean:F1} speed {(_animator != null ? _animator.speed : 0f):F1}");
            var agent = GetComponent<NpcAgent>();
            if (agent != null)
                sb.Append($" | gốc {root.position.x * 100f:F1},{root.position.z * 100f:F1} yaw {root.eulerAngles.y:F1} đi {agent.Walking} chờ {agent.Waiting} ({agent.BlockedBy})");
            _frames.Enqueue(sb.ToString());
            while (_frames.Count > 10) _frames.Dequeue();
        }

        void LateUpdate()
        {
            if (trace) Trace();
            // Đồng hồ double: chạy cả bộ PlayMode (nhiều test tua ×8–×20) thì Time.time lên hàng chục nghìn giây, float chỉ còn bước ~8 ms,
            // nhịp 1/60 bị làm tròn xen kẽ 15,6/23,4 ms ⇒ nửa số khung bị bỏ, nửa còn lại bị coi là khựng (StovePlayTests 17/10).
            double now = Time.timeAsDouble;
            // Nhịp 1/60 có thể ngắn hơn Tick vài micro giây; không bỏ cách khung vì sai số đó.
            if (_primed && now - _lastTime < Tick - 0.0001f) return;
            float interval = _primed ? (float)(now - _lastTime) : Tick;
            // Khung test khựng (nửa giây một khung): quãng dời quy về một nhịp thành gần 0 (tay đi một vòng vung rồi về chỗ cũ), nhịp
            // sau bình thường bị coi là "từ đứng yên vụt đi" (verify 14/10: 0,0 → 2,1 cm ở Walk 0,40). Chỉ so khi cả hai nhịp đều đều.
            bool steady = interval <= 1.5f * Tick && _lastInterval <= 1.5f * Tick;
            if (_primed && !steady) Hitches++;
            // Khung thật dài hơn một nhịp: quy quãng dời về một khung 60 fps.
            float scale = interval > 0f ? Tick / Mathf.Max(interval, Tick) : 1f;
            var root = transform;
            string doing = hands != null ? $"{hands.Activity}/{hands.Step}" : "";

            var moved = root.position - _lastRoot;
            moved.y = 0f;
            if (_rootPrimed && moved.magnitude * scale > 0.3f * Tick)
            {
                float angle = Vector3.Angle(moved, root.forward);
                if (angle > WorstWalkAngle)
                {
                    WorstWalkAngle = angle;
                    var agent = GetComponent<NpcAgent>();
                    WorstWalkWhat = $"{doing}, đang đi {agent?.Walking}, chờ {agent?.Waiting}, {moved.magnitude * scale / Tick:F2} m/s, khung {Time.frameCount}";
                }
            }
            _lastRoot = root.position;
            _rootPrimed = true;

            for (int i = 0; i < _items.Count; i++)
            {
                var (name, t, local) = _items[i];
                if (t == null) continue;
                var p = local ? root.InverseTransformPoint(t.position) : t.position;
                if (_primed)
                {
                    float step = (p - _last[i]).magnitude * scale;
                    if (step > Fastest)
                    {
                        Fastest = step;
                        FastestWhat = $"{name} ({doing})";
                    }
                    // Chưa có nhịp trước làm mốc (vừa gắn đầu đo, vừa khựng): nhịp này chỉ làm mốc (verify 14/10: nhịp đầu bị so với 0,
                    // tay đang vung bình thường 2,7 cm/khung bị báo nhảy).
                    if (steady && _lastStep[i] >= 0f && step > PopStep && step > 2.5f * _lastStep[i] + 0.005f)
                    {
                        if (step > Worst)
                        {
                            Worst = step;
                            WorstWhat = $"{name} ({doing})";
                        }
                        if (trace && _traced < 4)
                        {
                            _traced++;
                            Debug.Log($"MOTION_TRACE {name} {step * 100f:F1} cm:\n" + string.Join("\n", _frames));
                        }
                        _pops.Add((step, $"khung {Time.frameCount} {name} ({doing}) {step * 100f:F1} cm (nhịp trước {_lastStep[i] * 100f:F1}), cha {(t.parent != null ? t.parent.name : "-")}, {AnimState()}"));
                        if (_pops.Count > 40)
                            _pops.Remove(_pops.OrderBy(x => x.step).First());
                    }
                    _lastStep[i] = steady ? step : -1f;
                }
                _last[i] = p;
            }
            if (_primed) Samples++;
            _primed = true;
            _lastTime = now;
            _lastInterval = interval;
        }
    }
}
