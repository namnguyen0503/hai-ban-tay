using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>Đồng hồ treo tường trong bếp: kim giờ, kim phút quay quanh trục Z cục bộ của mặt đồng hồ (theo chiều kim đồng hồ khi nhìn từ phía trước, −Z).</summary>
    public sealed class WallClock : MonoBehaviour
    {
        public Transform hourHand;
        public Transform minuteHand;

        /// <summary>minutes: phút từ nửa đêm.</summary>
        public void Set(float minutes)
        {
            float hourAngle = Mathf.Repeat(minutes, 720f) / 720f * 360f;
            float minuteAngle = Mathf.Repeat(minutes, 60f) / 60f * 360f;
            if (hourHand != null)
                hourHand.localRotation = Quaternion.Euler(0f, 0f, -hourAngle);
            if (minuteHand != null)
                minuteHand.localRotation = Quaternion.Euler(0f, 0f, -minuteAngle);
        }
    }
}
