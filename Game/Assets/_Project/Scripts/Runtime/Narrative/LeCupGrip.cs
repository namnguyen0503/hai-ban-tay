using UnityEngine;

namespace HaiBanTay.Narrative
{
    /// <summary>
    /// Anh Lê cầm cốc kem tay phải (prologue): dáng làm tay trong Blender (Logs/trailer/le_cup_grip.blend, người dùng 07/11 "tạm ổn"):
    /// tay co trước bụng, bốn ngón duỗi ôm theo thành cốc, ngón cái đối diện sát dưới miệng cốc. Riêng cho thao tác này, không
    /// dùng quy tắc chung. Khớp lưu ở hệ gốc model (Unity, m); chạy sau Animator, NpcAgent và NpcArmIK, đè tay phải theo dáng,
    /// cốc đặt theo khung bàn tay thật sau khi đè.
    /// </summary>
    [DefaultExecutionOrder(12100)]
    public sealed class LeCupGrip : MonoBehaviour
    {
        // Xuất từ le_cup_grip.blend (LeRig, Blender → Unity: x=-bx, y=bz, z=-by).
        static readonly Vector3 UpperArm = new Vector3(0.2148f, 1.4231f, -0.0316f);
        static readonly Vector3 LowerArm = new Vector3(0.2598f, 1.1546f, 0.0553f);
        static readonly Vector3 Wrist = new Vector3(0.2400f, 1.0400f, 0.3000f);
        // Mỗi ngón: gốc, giữa, ngọn, đầu ngón (cái, trỏ, giữa, áp út, út).
        static readonly Vector3[][] Digits =
        {
            new[] { new Vector3(0.2400f, 1.0703f, 0.3293f), new Vector3(0.2171f, 1.0703f, 0.3459f), new Vector3(0.1883f, 1.0703f, 0.3343f), new Vector3(0.1624f, 1.0703f, 0.3513f) },
            new[] { new Vector3(0.2451f, 1.0538f, 0.3959f), new Vector3(0.2321f, 1.0538f, 0.4244f), new Vector3(0.2193f, 1.0538f, 0.4444f), new Vector3(0.1996f, 1.0538f, 0.4578f) },
            new[] { new Vector3(0.2400f, 1.0305f, 0.3951f), new Vector3(0.2234f, 1.0305f, 0.4294f), new Vector3(0.2033f, 1.0305f, 0.4495f), new Vector3(0.1756f, 1.0305f, 0.4564f) },
            new[] { new Vector3(0.2316f, 1.0108f, 0.3877f), new Vector3(0.2223f, 1.0108f, 0.4201f), new Vector3(0.2083f, 1.0108f, 0.4401f), new Vector3(0.1863f, 1.0108f, 0.4507f) },
            new[] { new Vector3(0.2188f, 0.9972f, 0.3767f), new Vector3(0.2209f, 0.9972f, 0.4057f), new Vector3(0.2174f, 0.9972f, 0.4207f), new Vector3(0.2090f, 0.9972f, 0.4335f) },
        };
        // Gốc cốc (đáy giữa) đứng thẳng.
        static readonly Vector3 CupBase = new Vector3(0.1770f, 0.9787f, 0.4034f);

        static readonly HumanBodyBones[][] DigitBones =
        {
            new[] { HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal },
            new[] { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal },
            new[] { HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal },
            new[] { HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal },
            new[] { HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal },
        };

        [Tooltip("Giây để tay nâng lên nhận cốc / buông xuống.")]
        public float blendSeconds = 0.55f;

        Transform _model, _upper, _lower, _hand;
        Transform[][] _digits;
        Vector3[] _distalDir; // hướng đốt ngọn trong hệ của nó (không có xương đầu ngón trong Humanoid)
        readonly Quaternion[] _pose = new Quaternion[3 + 15];
        Vector3 _cupInFrame;
        Quaternion _cupRotInFrame;
        Transform _cup;
        HaiBanTay.Npc.NpcArmIK _giver;
        bool _holding;
        float _weight;

        /// <summary>Cốc đã nằm trong tay (hết đoạn bay từ người bán sang).</summary>
        public bool CupInHand { get; private set; }

        void Awake()
        {
            var animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) { enabled = false; return; }
            _model = animator.transform;
            _upper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _lower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            _digits = new Transform[DigitBones.Length][];
            _distalDir = new Vector3[DigitBones.Length];
            for (int d = 0; d < DigitBones.Length; d++)
            {
                _digits[d] = new Transform[3];
                for (int j = 0; j < 3; j++) _digits[d][j] = animator.GetBoneTransform(DigitBones[d][j]);
                var mid = _digits[d][1]; var tip = _digits[d][2];
                _distalDir[d] = Quaternion.Inverse(tip.rotation) * (tip.position - mid.position).normalized;
            }
            // Cốc trong khung bàn tay của dáng (giống NpcArmIK: +Z hướng ngón, +Y mu bàn tay).
            var frame = Frame(Wrist, Digits[2][0], Digits[0][0]);
            _cupInFrame = Quaternion.Inverse(frame) * (CupBase - Wrist);
            _cupRotInFrame = Quaternion.Inverse(frame);
        }

        /// <summary>Tay phải: ngón ra trước, mu bàn tay = phía ngón cái × hướng ngón.</summary>
        static Quaternion Frame(Vector3 wrist, Vector3 middle, Vector3 thumb)
        {
            var fingers = middle - wrist;
            var thumbSide = Vector3.ProjectOnPlane(thumb - wrist, fingers);
            return Quaternion.LookRotation(fingers, Vector3.Cross(thumbSide, fingers));
        }

        [Tooltip("Điểm trao cốc (đáy cốc, world); reachAmount 1: tay với tới đó, 0: tay về dáng cầm trước bụng.")]
        public Vector3 handoff;
        [Range(0f, 1f)] public float reachAmount;

        /// <summary>Cốc đang nằm trên lòng bàn tay phải (ngửa) của người đưa; cốc theo tay người đưa tới khi Reach() nắm được.</summary>
        public void Offer(Transform cup, HaiBanTay.Npc.NpcArmIK giver)
        {
            _cup = cup;
            _giver = giver;
            _holding = false;
            CupInHand = false;
        }

        /// <summary>Tay phải với tới điểm trao rồi nắm cốc (CupInHand khi nắm xong).</summary>
        public void Reach()
        {
            _holding = true;
        }

        /// <summary>Đáy cốc trên lòng bàn tay ngửa (hệ cổ tay NpcArmIK: +Z ngón, +Y mu bàn tay).</summary>
        public static readonly Vector3 PalmCup = new Vector3(0f, -0.015f, 0.075f);

        void LateUpdate()
        {
            if (!CupInHand && _giver != null && _cup != null && _cup.gameObject.activeSelf && _giver.HandFrame(false, out var gw, out var gf))
            {
                var flat = Vector3.ProjectOnPlane(_model.forward, Vector3.up);
                _cup.SetPositionAndRotation(gw + gf * PalmCup, Quaternion.LookRotation(flat, Vector3.up));
            }
            bool want = _holding && _cup != null && _cup.gameObject.activeSelf;
            _weight = Mathf.MoveTowards(_weight, want ? 1f : 0f, Time.deltaTime / Mathf.Max(blendSeconds, 0.01f));
            if (!want && _weight <= 0f) { _holding = false; return; }
            if (_weight <= 0f) return;
            Save();
            Pose();
            if (_weight < 0.999f) Mix(Mathf.SmoothStep(0f, 1f, _weight));
            if (!want) return;
            var hand = Frame(_hand.position, _digits[2][0].position, _digits[0][0].position);
            var position = _hand.position + hand * (_cupInFrame * _model.lossyScale.y);
            var rotation = hand * _cupRotInFrame;
            if (!CupInHand)
            {
                if (_weight < 1f) return; // cốc còn trên tay người đưa
                CupInHand = true;
                _giver = null;
            }
            _cup.SetPositionAndRotation(position, rotation);
        }

        void Pose()
        {
            var root = _model.rotation;
            // Cánh tay: giữ đúng khuỷu và cổ tay so với vai như dáng (vai theo animation).
            var a = _upper.position;
            float s = _model.lossyScale.y;
            // Với tới điểm trao: dời cả cổ tay để đáy cốc trong dáng trùng điểm trao.
            var reach = (handoff - (a + root * (CupBase - UpperArm) * s)) * reachAmount;
            HaiBanTay.Npc.NpcArmIK.TwoBone(_upper, _lower, _hand, a + root * (Wrist - UpperArm) * s + reach, a + root * (LowerArm - UpperArm) * s + reach);
            var target = root * Frame(Wrist, Digits[2][0], Digits[0][0]);
            var current = Frame(_hand.position, _digits[2][0].position, _digits[0][0].position);
            _hand.rotation = target * Quaternion.Inverse(current) * _hand.rotation;
            for (int d = 0; d < _digits.Length; d++)
                for (int j = 0; j < 3; j++)
                {
                    var bone = _digits[d][j];
                    var now = j < 2 ? _digits[d][j + 1].position - bone.position : bone.rotation * _distalDir[d];
                    var want = root * (Digits[d][j + 1] - Digits[d][j]);
                    bone.rotation = Quaternion.FromToRotation(now, want) * bone.rotation;
                }
        }

        void Save()
        {
            int k = 0;
            _pose[k++] = _upper.localRotation; _pose[k++] = _lower.localRotation; _pose[k++] = _hand.localRotation;
            foreach (var digit in _digits)
                foreach (var bone in digit) _pose[k++] = bone.localRotation;
        }

        void Mix(float w)
        {
            int k = 0;
            _upper.localRotation = Quaternion.Slerp(_pose[k++], _upper.localRotation, w);
            _lower.localRotation = Quaternion.Slerp(_pose[k++], _lower.localRotation, w);
            _hand.localRotation = Quaternion.Slerp(_pose[k++], _hand.localRotation, w);
            foreach (var digit in _digits)
                foreach (var bone in digit) bone.localRotation = Quaternion.Slerp(_pose[k++], bone.localRotation, w);
        }
    }
}
