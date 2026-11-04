using UnityEngine;

namespace HaiBanTay.Player
{
    /// <summary>Khung hình "hai bàn tay" của B1.2; E2 dùng lại y hệt (PLAN mục 7.2, 7.4).</summary>
    [CreateAssetMenu(menuName = "HaiBanTay/Hands Shot Pose")]
    public sealed class HandsShotPose : ScriptableObject
    {
        [Tooltip("Góc cúi đầu (độ, dương = nhìn xuống).")]
        public float pitch = 8f;
        public float blendSeconds = 1.2f;
        public Vector3 leftPos = new Vector3(-0.15f, -0.08f, 0.58f);
        public Vector3 leftEuler = new Vector3(-35f, 8f, 180f);
        public Vector3 rightPos = new Vector3(0.15f, -0.06f, 0.58f);
        public Vector3 rightEuler = new Vector3(-35f, -8f, 180f);

        [Header("Prologue P3 (dùng chung khung B1.2/E2)")]
        [Tooltip("Góc cúi đầu khi giơ tay trước anh Lê: nhỏ hơn mặc định để còn thấy mặt anh.")]
        public float prologuePitch = 8f;
        [Tooltip("Dịch hai bàn tay (hệ camera) so với khung mặc định: lên cao, ra xa chút để nằm trên vùng phụ đề.")]
        public Vector3 prologueHandOffset = Vector3.zero;
        [Tooltip("Lùi người chơi (m) trước khi giơ tay để anh Lê vào trọn khung.")]
        public float prologueStepBack = 0.35f;

        public HandPoseTarget Target => new HandPoseTarget(HandPose.Open, leftPos, leftEuler, rightPos, rightEuler);

        /// <summary>Khung giơ tay P3 dùng chung tư thế; offset mặc định bằng không.</summary>
        public HandPoseTarget PrologueTarget => new HandPoseTarget(HandPose.Open, leftPos + prologueHandOffset, leftEuler,
            rightPos + prologueHandOffset, rightEuler);
    }
}
