using UnityEngine;

namespace HaiBanTay.Player
{
    /// <summary>Tư thế một ngón (độ). Gập dương = đầu ngón về phía lòng bàn tay; dạng dương = ra xa ngón giữa.</summary>
    [System.Serializable]
    public struct DigitPose
    {
        public float spread, twist, proximal, intermediate, distal;

        public DigitPose(float proximal, float intermediate, float distal, float spread = 0f, float twist = 0f)
        {
            this.spread = spread; this.twist = twist;
            this.proximal = proximal; this.intermediate = intermediate; this.distal = distal;
        }

        public static DigitPose Lerp(DigitPose a, DigitPose b, float t) => new DigitPose(
            Mathf.Lerp(a.proximal, b.proximal, t), Mathf.Lerp(a.intermediate, b.intermediate, t), Mathf.Lerp(a.distal, b.distal, t),
            Mathf.Lerp(a.spread, b.spread, t), Mathf.Lerp(a.twist, b.twist, t));

        public static bool Same(DigitPose a, DigitPose b) =>
            Mathf.Approximately(a.proximal, b.proximal) && Mathf.Approximately(a.intermediate, b.intermediate) && Mathf.Approximately(a.distal, b.distal)
            && Mathf.Approximately(a.spread, b.spread) && Mathf.Approximately(a.twist, b.twist);

        /// <summary>Lò xo tới tắt dần từng góc: ngón đóng mềm, không dừng đột ngột như nội suy tuyến tính.</summary>
        public static DigitPose SmoothDamp(DigitPose current, DigitPose target, ref DigitPose velocity, float smoothTime, float deltaTime) => new DigitPose(
            Mathf.SmoothDamp(current.proximal, target.proximal, ref velocity.proximal, smoothTime, Mathf.Infinity, deltaTime),
            Mathf.SmoothDamp(current.intermediate, target.intermediate, ref velocity.intermediate, smoothTime, Mathf.Infinity, deltaTime),
            Mathf.SmoothDamp(current.distal, target.distal, ref velocity.distal, smoothTime, Mathf.Infinity, deltaTime),
            Mathf.SmoothDamp(current.spread, target.spread, ref velocity.spread, smoothTime, Mathf.Infinity, deltaTime),
            Mathf.SmoothDamp(current.twist, target.twist, ref velocity.twist, smoothTime, Mathf.Infinity, deltaTime));

        public bool Settled(DigitPose target) =>
            Mathf.Abs(proximal - target.proximal) < .05f && Mathf.Abs(intermediate - target.intermediate) < .05f && Mathf.Abs(distal - target.distal) < .05f
            && Mathf.Abs(spread - target.spread) < .05f && Mathf.Abs(twist - target.twist) < .05f;
    }

    /// <summary>Tư thế cả bàn tay: năm ngón. Tất cả bằng 0 là bàn tay mở (tư thế dựng sẵn của mesh).</summary>
    [System.Serializable]
    public struct FingerPose
    {
        public DigitPose thumb, index, middle, ring, little;

        public DigitPose this[int digit]
        {
            get
            {
                switch (digit)
                {
                    case 0: return thumb;
                    case 1: return index;
                    case 2: return middle;
                    case 3: return ring;
                    default: return little;
                }
            }
            set
            {
                switch (digit)
                {
                    case 0: thumb = value; break;
                    case 1: index = value; break;
                    case 2: middle = value; break;
                    case 3: ring = value; break;
                    default: little = value; break;
                }
            }
        }

        public static FingerPose Lerp(FingerPose a, FingerPose b, float t)
        {
            var result = new FingerPose();
            for (int d = 0; d < HandFingers.Digits; d++) result[d] = DigitPose.Lerp(a[d], b[d], t);
            return result;
        }
    }

    /// <summary>
    /// Xương ngón của bàn tay FP: Wrist + 5 ngón × 3 đốt (Hand_{L,R}_Rig.json, dựng bởi HandRigBuilder). Ở tư thế dựng sẵn mọi xương có
    /// xoay đơn vị nên trục của đốt trùng trục bàn tay (+Z ngón, −Y lòng bàn tay); trục gập của từng ngón tính từ hình học xương lúc chạy:
    /// vuông góc với hướng ngón và pháp tuyến lòng bàn tay, dấu chọn sao cho đầu ngón đi về phía lòng bàn tay. Ba đốt của một ngón
    /// là bản lề song song nên dùng chung trục của đốt gốc. Không Find/GetComponent trong Update: mọi tham chiếu cache ở Setup.
    /// </summary>
    public sealed class HandFingers : MonoBehaviour
    {
        public const int Digits = 5, Joints = 3;
        static readonly Vector3 PalmNormal = Vector3.down, SpreadAxis = Vector3.up;

        [Tooltip("Wrist rồi (Thumb1..3, Index1..3, Middle1..3, Ring1..3, Little1..3): 16 phần tử.")]
        public Transform[] bones = new Transform[1 + Digits * Joints];
        [Tooltip("Xương cẳng tay gốc ở cổ tay (PlayerBody xoay theo ống tay áo): vùng cổ tay của da uốn theo nó. Có thể null.")]
        public Transform forearm;
        [Tooltip("Hướng head→tail của 15 xương ngón lúc dựng sẵn, theo khung bàn tay.")]
        public Vector3[] restDirections = new Vector3[Digits * Joints];
        [Tooltip("Đầu ngón cái, trỏ, giữa, nhẫn, út lúc dựng sẵn (khung bàn tay) để tính phía dạng ngón.")]
        public Vector3[] restHeads = new Vector3[Digits];
        [Tooltip("Thời gian lò xo (giây) để ngón tới tư thế đích; 0 = đặt ngay.")]
        public float smoothTime = 0.11f;
        [Tooltip("Mỗi ngón sau chậm hơn ngón trước thêm tỉ lệ này: bàn tay khép lại thành làn sóng từ trỏ tới út.")]
        public float staggerPerDigit = 0.18f;

        FingerPose _target, _current, _velocity;
        readonly Vector3[] _flexAxis = new Vector3[Digits];
        readonly Vector3[] _digitDirection = new Vector3[Digits];
        readonly float[] _side = new float[Digits];
        bool _ready, _dirty = true;

        public FingerPose Target
        {
            get => _target;
            set { _target = value; _dirty = true; }
        }

        public FingerPose Current => _current;

        /// <summary>Đặt ngay vào tư thế đích, không nội suy.</summary>
        public void Snap()
        {
            _current = _target;
            _velocity = default;
            _dirty = true;
        }

        void Awake() => Setup();

        void Setup()
        {
            if (_ready) return;
            _ready = true;
            for (int d = 0; d < Digits; d++)
            {
                var direction = restDirections[d * Joints].normalized;
                _digitDirection[d] = direction;
                var axis = Vector3.Cross(direction, PalmNormal);
                _flexAxis[d] = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.right;
                // Dạng ra xa ngón giữa: phía của ngón so với ngón giữa; chính ngón giữa dạng về phía ngón trỏ.
                float offset = restHeads[d].x - restHeads[2].x;
                _side[d] = Mathf.Abs(offset) > 1e-4f ? Mathf.Sign(offset) : Mathf.Sign(restHeads[1].x - restHeads[2].x);
            }
        }

        void LateUpdate()
        {
            Setup();
            if (!_dirty) return;
            bool reached = true;
            for (int d = 0; d < Digits; d++)
            {
                var velocity = _velocity[d];
                // Ngón cái đi trước, các ngón sau lần lượt trễ hơn.
                float time = smoothTime * (1f + staggerPerDigit * Mathf.Max(0, d - 1));
                _current[d] = smoothTime <= 0f ? _target[d] : DigitPose.SmoothDamp(_current[d], _target[d], ref velocity, time, Time.deltaTime);
                _velocity[d] = velocity;
                if (_current[d].Settled(_target[d])) { _current[d] = _target[d]; _velocity[d] = default; }
                else reached = false;
            }
            Apply();
            _dirty = !reached;
        }

        /// <summary>Áp tư thế hiện tại lên xương (test gọi trực tiếp, không đợi LateUpdate).</summary>
        public void Apply()
        {
            Setup();
            for (int d = 0; d < Digits; d++)
            {
                var pose = _current[d];
                int first = 1 + d * Joints;
                var axis = _flexAxis[d];
                // Xoay quanh trục ngón trước (các đốt sau gập quanh trục đã xoay theo, như ngón cái thật), rồi gập, rồi dạng ngang lòng bàn tay.
                var root = Quaternion.AngleAxis(pose.spread * _side[d], SpreadAxis) * Quaternion.AngleAxis(pose.proximal, axis)
                    * Quaternion.AngleAxis(pose.twist, _digitDirection[d]);
                Set(first, root);
                Set(first + 1, Quaternion.AngleAxis(pose.intermediate, axis));
                Set(first + 2, Quaternion.AngleAxis(pose.distal, axis));
            }
        }

        void Set(int index, Quaternion rotation)
        {
            var bone = bones[index];
            if (bone != null) bone.localRotation = rotation;
        }
    }
}
