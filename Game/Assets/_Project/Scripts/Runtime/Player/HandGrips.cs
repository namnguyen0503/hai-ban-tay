using UnityEngine;

namespace HaiBanTay.Player
{
    /// <summary>
    /// Kiểu cầm một vật bằng bàn tay có xương ngón (<see cref="HandFingers"/>): vị trí/hướng gốc vật trong khung bàn tay (+Z ngón tay,
    /// −Y lòng bàn tay) và tư thế ngón ôm vật. Số giải ngoài Unity bằng FK trên chính Hand_{L,R}_Rig.json: không đỉnh da nào xuyên vật,
    /// đệm ngón chạm mặt vật, rồi đặt cả bàn tay theo ảnh tham chiếu.
    /// </summary>
    [System.Serializable]
    public struct HandGrip
    {
        public Vector3 position, euler;
        public FingerPose fingers;

        public HandGrip(Vector3 position, Vector3 euler, FingerPose fingers)
        {
            this.position = position;
            this.euler = euler;
            this.fingers = fingers;
        }

        public Quaternion Rotation => Quaternion.Euler(euler);

        /// <summary>Gắn vật vào khung bàn tay theo kiểu cầm (giữ scale riêng của vật).</summary>
        public void Attach(Transform item, Transform hand)
        {
            item.SetParent(hand, false);
            item.SetLocalPositionAndRotation(position, Rotation);
        }
    }

    /// <summary>Kiểu cầm dùng chung giữa các cảnh.</summary>
    public static class HandGrips
    {
        /// <summary>
        /// Tay phải cầm thìa, gốc thìa ở điểm cầm (+Y về lòng thìa, +Z mặt lõm): cán tựa cạnh đốt giữa ngón trỏ, gác qua kẽ ngón cái–trỏ
        /// rồi chĩa ra sau mu tay; đệm ngón cái đè mặt trên cán gần cổ thìa; đầu ngón trỏ gần cổ thìa nhất, ngón giữa, nhẫn, út lùi dần và cong dần vào lòng
        /// bàn tay, các ngón khép sát (ảnh tham chiếu ăn kem 16/10, sửa 17/10: trước đó ngón giữa duỗi vươn quá ngón trỏ như móng câu). Cán dẹt ~9 mm như thìa nhựa của prologue; thìa khác dùng lại được khi gốc thìa đặt cùng quy ước.
        /// </summary>
        public static readonly HandGrip SpoonRight = new HandGrip(
            new Vector3(-.0292f, -.0576f, .0896f), new Vector3(2.42f, -67.43f, 175.92f),
            new FingerPose
            {
                thumb = new DigitPose(16.3f, 14.5f, 20.4f, -13.8f, 41.3f),
                index = new DigitPose(71.5f, 68.7f, 9.4f),
                middle = new DigitPose(87.5f, 68.4f, 32.2f),
                ring = new DigitPose(79.7f, 77.1f, 46.9f),
                little = new DigitPose(71.8f, 98.7f, 63.6f)
            });
    }
}
