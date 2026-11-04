using UnityEngine;

namespace HaiBanTay.Narrative
{
    /// <summary>Cười nhỏ và nói riêng: vai rung nhẹ, cúi đầu; không dùng clip Laugh chưa có.</summary>
    [DefaultExecutionOrder(500)]
    public sealed class StaffAside : MonoBehaviour
    {
        Transform _head, _chest;
        public bool Laughing { get; set; }
        void Awake()
        {
            var animator = GetComponentInChildren<Animator>();
            _head = animator.GetBoneTransform(HumanBodyBones.Head);
            _chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        }
        void LateUpdate()
        {
            if (!Laughing) return;
            float pulse = Mathf.Sin(Time.time * 12f);
            _head.localRotation *= Quaternion.Euler(7f + pulse * 2f, 0f, 0f);
            _chest.localRotation *= Quaternion.Euler(pulse * 1.8f, 0f, 0f);
        }
    }
}
