using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>
    /// Đặt hai bàn tay NPC vào đồ đang cầm bằng IK hai đoạn sau Animator (không cần IK pass trong controller). Dùng cho lúc
    /// tay cầm vật phải thấy rõ trên tuyến chơi (ông Mai gọt khoai làm mẫu). Đích là cổ tay: +Z hướng ngón, +Y mu bàn tay.
    /// Ngón tay cong theo leftCurl/rightCurl (độ ở ba đốt) tính từ tư thế gốc của xương, không cộng dồn qua các khung.
    /// </summary>
    [DefaultExecutionOrder(12000)]
    public sealed class NpcArmIK : MonoBehaviour
    {
        public Transform leftTarget, rightTarget;
        [Tooltip("Mức bám đích (0: theo animation, 1: đúng đích). Đổi dần để tay vươn tới vật hay buông vật không giật.")]
        [Range(0f, 1f)] public float leftWeight = 1f, rightWeight = 1f;
        [Tooltip("Độ cong ba đốt ngón (gốc, giữa, ngọn); ngón cái cong bằng thumbScale lần.")]
        public Vector3 leftCurl, rightCurl;
        public float thumbScale = 0.45f;
        [Tooltip("Hướng khuỷu trong hệ gốc NPC (x lấy theo bên trái/phải): ra ngoài, xuống và ra sau như khi làm việc trước bụng.")]
        public Vector3 elbowHint = new Vector3(0.5f, 0.85f, -0.25f);
        Animator _animator;
        Transform _lu, _ll, _lh, _lm, _lt, _ru, _rl, _rh, _rm, _rt;
        Finger[] _leftFingers, _rightFingers;

        /// <summary>Một đốt ngón: xoay gốc (local) và trục gập về phía lòng bàn tay (local), lấy lúc chưa chạy Animator.</summary>
        struct Joint
        {
            public Transform bone;
            public Quaternion rest;
            public Vector3 axis;
        }

        struct Finger
        {
            public Joint[] joints;
            public bool thumb;
        }

        static readonly HumanBodyBones[][] LeftDigits =
        {
            new[] { HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal },
            new[] { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal },
            new[] { HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal },
            new[] { HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal },
            new[] { HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal },
        };

        static readonly HumanBodyBones[][] RightDigits =
        {
            new[] { HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal },
            new[] { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal },
            new[] { HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal },
            new[] { HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal },
            new[] { HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal },
        };

        void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            if (_animator == null || !_animator.isHuman)
            {
                enabled = false;
                return;
            }
            _lu = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _ll = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _lh = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _lm = _animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            _lt = _animator.GetBoneTransform(HumanBodyBones.LeftThumbProximal);
            _ru = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _rl = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _rh = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            _rm = _animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            _rt = _animator.GetBoneTransform(HumanBodyBones.RightThumbProximal);
            _leftFingers = Fingers(LeftDigits, _lh, _lm, _lt, -1f);
            _rightFingers = Fingers(RightDigits, _rh, _rm, _rt, 1f);
        }

        Finger[] Fingers(HumanBodyBones[][] digits, Transform hand, Transform middle, Transform thumb, float side)
        {
            if (hand == null || middle == null || thumb == null || !Back(hand, middle, thumb, side, out var back))
                return new Finger[0];
            var list = new System.Collections.Generic.List<Finger>();
            for (int d = 0; d < digits.Length; d++)
            {
                var joints = new System.Collections.Generic.List<Joint>();
                Vector3 dir = Vector3.zero;
                for (int j = 0; j < digits[d].Length; j++)
                {
                    var bone = _animator.GetBoneTransform(digits[d][j]);
                    if (bone == null)
                        break;
                    // Hướng đốt: tới đốt kế; đốt ngọn dùng lại hướng đốt giữa (đầu ngón không có trong Humanoid).
                    var next = j + 1 < digits[d].Length ? _animator.GetBoneTransform(digits[d][j + 1]) : null;
                    if (next != null)
                        dir = next.position - bone.position;
                    else if (bone.childCount > 0)
                        dir = bone.GetChild(0).position - bone.position;
                    if (dir.sqrMagnitude < 1e-10f)
                        break;
                    var axis = Vector3.Cross(dir, -back);
                    if (axis.sqrMagnitude < 1e-10f)
                        break;
                    joints.Add(new Joint { bone = bone, rest = bone.localRotation, axis = Quaternion.Inverse(bone.rotation) * axis.normalized });
                }
                if (joints.Count > 0)
                    list.Add(new Finger { joints = joints.ToArray(), thumb = d == 0 });
            }
            return list.ToArray();
        }

        /// <summary>Mu bàn tay: trái = ngón × phía ngón cái, phải = ngược lại (lòng bàn tay úp, ngón ra trước, ngón cái vào trong).</summary>
        static bool Back(Transform hand, Transform middle, Transform thumb, float side, out Vector3 back)
        {
            var fingers = middle.position - hand.position;
            var thumbSide = Vector3.ProjectOnPlane(thumb.position - hand.position, fingers);
            back = side < 0f ? Vector3.Cross(fingers, thumbSide) : Vector3.Cross(thumbSide, fingers);
            return fingers.sqrMagnitude > 1e-8f && thumbSide.sqrMagnitude > 1e-8f && back.sqrMagnitude > 1e-12f;
        }

        /// <summary>Xương bàn tay (cổ tay) của tay trái/phải; null nếu không phải Humanoid.</summary>
        public Transform Hand(bool left) => left ? _lh : _rh;

        /// <summary>Hệ trục cổ tay theo quy ước đích IK (+Z hướng ngón, +Y mu bàn tay) ở tư thế hiện tại của xương.</summary>
        public bool HandFrame(bool left, out Vector3 position, out Quaternion rotation)
        {
            var hand = left ? _lh : _rh;
            var middle = left ? _lm : _rm;
            var thumb = left ? _lt : _rt;
            position = hand != null ? hand.position : transform.position;
            rotation = transform.rotation;
            if (hand == null || middle == null || thumb == null || !Back(hand, middle, thumb, left ? -1f : 1f, out var back))
                return false;
            rotation = Quaternion.LookRotation(middle.position - hand.position, back);
            return true;
        }

        void LateUpdate()
        {
            if (leftTarget != null && _lu != null && leftWeight > 0f)
                Arm(_lu, _ll, _lh, _lm, _lt, _leftFingers, leftCurl, leftTarget, -1f, leftWeight);
            if (rightTarget != null && _ru != null && rightWeight > 0f)
                Arm(_ru, _rl, _rh, _rm, _rt, _rightFingers, rightCurl, rightTarget, 1f, rightWeight);
        }

        // Tư thế animation của tay và ngón trước khi giải IK (trộn theo weight); cấp sẵn, không cấp phát mỗi khung.
        readonly Quaternion[] _pose = new Quaternion[3 + 5 * 3];

        void Arm(Transform upper, Transform lower, Transform hand, Transform middle, Transform thumb, Finger[] fingers, Vector3 curl,
            Transform target, float side, float weight)
        {
            bool blend = weight < 0.999f;
            if (blend) Save(upper, lower, hand, fingers);
            Curl(fingers, curl);
            Solve(upper, lower, hand, middle, thumb, target, side);
            if (blend) Mix(upper, lower, hand, fingers, weight);
        }

        void Save(Transform upper, Transform lower, Transform hand, Finger[] fingers)
        {
            int k = 0;
            _pose[k++] = upper.localRotation; _pose[k++] = lower.localRotation; _pose[k++] = hand.localRotation;
            foreach (var f in fingers)
                for (int j = 0; j < f.joints.Length && k < _pose.Length; j++)
                    _pose[k++] = f.joints[j].bone.localRotation;
        }

        void Mix(Transform upper, Transform lower, Transform hand, Finger[] fingers, float weight)
        {
            int k = 0;
            upper.localRotation = Quaternion.Slerp(_pose[k++], upper.localRotation, weight);
            lower.localRotation = Quaternion.Slerp(_pose[k++], lower.localRotation, weight);
            hand.localRotation = Quaternion.Slerp(_pose[k++], hand.localRotation, weight);
            foreach (var f in fingers)
                for (int j = 0; j < f.joints.Length && k < _pose.Length; j++)
                    f.joints[j].bone.localRotation = Quaternion.Slerp(_pose[k++], f.joints[j].bone.localRotation, weight);
        }

        void Curl(Finger[] fingers, Vector3 curl)
        {
            foreach (var f in fingers)
                for (int j = 0; j < f.joints.Length; j++)
                {
                    float angle = curl[j] * (f.thumb ? thumbScale : 1f);
                    f.joints[j].bone.localRotation = f.joints[j].rest * Quaternion.AngleAxis(angle, f.joints[j].axis);
                }
        }

        void Solve(Transform upper, Transform lower, Transform hand, Transform middle, Transform thumb, Transform target, float side)
        {
            var hint = transform.TransformPoint(new Vector3(side * elbowHint.x, elbowHint.y, elbowHint.z));
            TwoBone(upper, lower, hand, target.position, hint);
            if (middle == null || thumb == null || !Back(hand, middle, thumb, side, out var back))
                return;
            var current = Quaternion.LookRotation(middle.position - hand.position, back);
            hand.rotation = target.rotation * Quaternion.Inverse(current) * hand.rotation;
        }

        /// <summary>
        /// Khoảng vai–cổ tay cho đích ở xa d (tay dài length): gần duỗi hết thì tiến dần về sát tầm tay thay vì chạm trần đột ngột.
        /// Trần cứng làm cổ tay đang theo đích chuyển sang bị vai kéo trong một khung (đo giật 14/10: xẻng nhảy 5–6 cm lúc đứng dậy).
        /// </summary>
        public static float SoftReach(float d, float length)
        {
            float soft = 0.04f * length, start = length - soft;
            if (d <= start) return d;
            return Mathf.Min(start + soft * (1f - Mathf.Exp(-(d - start) / soft)), length * 0.999f);
        }

        /// <summary>IK hai đoạn: gập khuỷu cho đúng khoảng cách vai–đích, xoay cả tay tới đích, rồi xoay quanh trục vai–đích
        /// cho khuỷu về phía hint.</summary>
        public static void TwoBone(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 hint)
        {
            Vector3 a = upper.position, b = lower.position, c = end.position;
            float ab = (b - a).magnitude, bc = (c - b).magnitude;
            if (ab < 1e-4f || bc < 1e-4f)
                return;
            float at = Mathf.Max(SoftReach((target - a).magnitude, ab + bc), 0.01f);
            float current = Vector3.Angle(a - b, c - b);
            float wanted = Mathf.Acos(Mathf.Clamp((ab * ab + bc * bc - at * at) / (2f * ab * bc), -1f, 1f)) * Mathf.Rad2Deg;
            var bend = Vector3.Cross(c - b, a - b);
            if (bend.sqrMagnitude < 1e-10f)
                bend = Vector3.Cross(c - a, hint - a);
            if (bend.sqrMagnitude > 1e-10f)
                lower.rotation = Quaternion.AngleAxis(current - wanted, bend.normalized) * lower.rotation;
            c = end.position;
            upper.rotation = Quaternion.FromToRotation(c - a, target - a) * upper.rotation;
            var axis = (target - a).normalized;
            var elbow = Vector3.ProjectOnPlane(lower.position - a, axis);
            var toward = Vector3.ProjectOnPlane(hint - a, axis);
            if (elbow.sqrMagnitude > 1e-8f && toward.sqrMagnitude > 1e-8f)
                upper.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(elbow, toward, axis), axis) * upper.rotation;
        }
    }
}
