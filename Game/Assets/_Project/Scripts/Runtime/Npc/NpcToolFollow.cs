using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>
    /// Dụng cụ cầm hai tay bám theo hai bàn tay thật sau IK (chạy sau NpcArmIK): lúc thân đổi dáng (đứng ↔ khom) mà tay chưa với
    /// tới đúng chỗ cầm, dụng cụ dời/xoay cho chỗ cầm tay phải trùng cổ tay phải và cán hướng về cổ tay trái — tay không rời cán.
    /// NpcHandWork bật lúc đổi dáng, tắt khi xong.
    /// </summary>
    [DefaultExecutionOrder(13000)]
    public sealed class NpcToolFollow : MonoBehaviour
    {
        public NpcArmIK arms;
        [System.NonSerialized] public Transform tool, rightGrip, leftGrip;
        [Tooltip("Mới bật bám tay thì kéo dụng cụ về tay dần trong chừng này giây (tay đang lệch cán sẵn vài cm thì không giật).")]
        public float blendSeconds = 0.15f;
        Transform _blending;
        float _weight;

        void LateUpdate()
        {
            if (tool != _blending)
            {
                _blending = tool;
                _weight = 0f;
            }
            if (tool == null || rightGrip == null || leftGrip == null || arms == null || !arms.enabled) return;
            _weight = blendSeconds > 0f ? Mathf.MoveTowards(_weight, 1f, Time.deltaTime / blendSeconds) : 1f;
            var wr = arms.Hand(false);
            var wl = arms.Hand(true);
            if (wr == null || wl == null) return;
            var want = wl.position - wr.position;
            var have = leftGrip.position - rightGrip.position;
            if (want.sqrMagnitude < 1e-6f || have.sqrMagnitude < 1e-6f) return;
            // Không có vùng chết: bỏ qua lệch dưới 1 cm / 1° rồi bù cả một lần thì dụng cụ nhảy 1–6 cm (đo giật 14/10).
            tool.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(have, want), _weight) * tool.rotation;
            tool.position += (wr.position - rightGrip.position) * _weight;
        }
    }
}
