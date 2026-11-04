using HaiBanTay.Npc;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Đo cổ tay NPC tới chỗ cầm sau mọi LateUpdate (sau IK và sau NpcToolFollow), đúng tư thế được vẽ ra. Chỉ tính khung mà hai tay
    /// đang bám đủ hai chỗ cầm cho trước (không tính lúc vươn tới / buông). Ghi lần xa nhất và bước động tác lúc đó.
    /// </summary>
    [DefaultExecutionOrder(14000)]
    public sealed class HandProbe : MonoBehaviour
    {
        public NpcHandWork hands;
        public Transform left, right;
        public string activity;
        public float Worst { get; private set; }
        public string WorstAt { get; private set; } = "";
        public int Samples { get; private set; }

        public void Reset(string forActivity)
        {
            activity = forActivity;
            Worst = 0f;
            WorstAt = "";
            Samples = 0;
        }

        void LateUpdate()
        {
            if (hands == null || (activity != null && hands.Activity != activity)) return;
            if (hands.Grip(true) != left || hands.Grip(false) != right || left == null || right == null) return;
            var wl = hands.Wrist(true);
            var wr = hands.Wrist(false);
            if (wl == null || wr == null) return;
            float d = Mathf.Max(Vector3.Distance(wl.position, left.position), Vector3.Distance(wr.position, right.position));
            Samples++;
            if (d > Worst)
            {
                Worst = d;
                WorstAt = hands.Step;
            }
        }
    }
}
