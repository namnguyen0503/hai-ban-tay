using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>
    /// Đưa đồ không cúi: clip HandOver (gốc là tài liệu đưa ra) gập ngực tới ~35° và cúi đầu ~50° (đo 22/10, Idle chỉ 5°/11°). Trong lúc
    /// NPC ở trạng thái HandOver, phần gập quá ngưỡng được xoay ngược lại ở xương sống; hai cánh tay giữ hướng cũ trong thế giới nên vẫn
    /// đưa ra trước. Chạy ngay sau Animator, trước NpcGaze (đầu quay theo Ba) và NpcAgent.LateUpdate.
    /// </summary>
    [DefaultExecutionOrder(-70)]
    public sealed class NpcUpright : MonoBehaviour
    {
        /// <summary>Gập tự nhiên của dáng đứng (độ) giữ lại; chỉ bỏ phần vượt quá.</summary>
        public const float KeepChest = 4f, KeepHead = 8f;
        /// <summary>Thời gian trộn vào/ra (giây).</summary>
        public const float BlendIn = 0.12f, BlendOut = 0.4f;

        NpcAgent _agent;
        Animator _animator;
        Transform _spine, _chest, _head, _leftArm, _rightArm;
        float _weight;
        bool _init;

        void Init()
        {
            if (_init) return;
            _init = true;
            _agent = GetComponent<NpcAgent>();
            _animator = GetComponentInChildren<Animator>();
            if (_animator == null || !_animator.isHuman) return;
            _spine = _animator.GetBoneTransform(HumanBodyBones.Spine);
            _chest = _animator.GetBoneTransform(HumanBodyBones.Chest);
            if (_chest == null) _chest = _spine;
            _head = _animator.GetBoneTransform(HumanBodyBones.Head);
            _leftArm = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _rightArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        }

        /// <summary>Góc gập (độ, dương = chúi về trước) của trục lên của xương so với thẳng đứng, đo quanh trục ngang của NPC.</summary>
        float Pitch(Transform bone)
        {
            var right = transform.right;
            var up = Vector3.ProjectOnPlane(bone.up, right);
            return Vector3.SignedAngle(Vector3.up, up, right);
        }

        /// <summary>Bỏ phần gập quá ngưỡng của pose hiện tại với mức weight (0–1). Gọi trong LateUpdate; test gọi thẳng sau SampleAnimation.</summary>
        public void Apply(float weight)
        {
            Init();
            if (_spine == null || _chest == null)
                return;
            var right = transform.right;
            float chest = Pitch(_chest);
            float cut = Mathf.Max(0f, chest - KeepChest) * weight;
            if (cut > 0.01f)
            {
                var back = Quaternion.AngleAxis(-cut, right);
                var armL = _leftArm != null ? _leftArm.rotation : Quaternion.identity;
                var armR = _rightArm != null ? _rightArm.rotation : Quaternion.identity;
                _spine.rotation = back * _spine.rotation;
                if (_leftArm != null) _leftArm.rotation = armL;
                if (_rightArm != null) _rightArm.rotation = armR;
            }
            if (_head != null)
            {
                float headCut = Mathf.Max(0f, Pitch(_head) - KeepHead) * weight;
                if (headCut > 0.01f)
                    _head.rotation = Quaternion.AngleAxis(-headCut, right) * _head.rotation;
            }
        }

        void LateUpdate()
        {
            Init();
            if (_spine == null || _agent == null || _animator == null || !_animator.enabled)
                return;
            bool active = _agent.Animated && _agent.State == "HandOver";
            float target = active ? 1f : 0f;
            _weight = Mathf.MoveTowards(_weight, target, Time.deltaTime / (active ? BlendIn : BlendOut));
            if (_weight <= 0f)
                return;

            Apply(_weight);
        }
    }
}
