using UnityEngine;

namespace HaiBanTay.Ship
{
    /// <summary>Đèn treo dao động theo góc nghiêng của tàu, có quán tính (con lắc tắt dần).</summary>
    public sealed class SwingingLamp : MonoBehaviour
    {
        public float gain = 1.6f;
        public float stiffness = 6f;
        public float damping = 1.2f;

        const float MaxStep = 1f / 60f;

        float _angle;
        float _velocity;

        void Update()
        {
            var ship = ShipMotion.Current;
            float target = ship != null ? -ship.Roll * gain : 0f;
            // Chia bước nhỏ: frame dài (giật, chụp ảnh) không làm lò xo mất ổn định thành NaN.
            float left = Mathf.Min(Time.deltaTime, 0.25f);
            while (left > 0f)
            {
                float dt = Mathf.Min(left, MaxStep);
                left -= dt;
                float accel = (target - _angle) * stiffness - _velocity * damping;
                _velocity += accel * dt;
                _angle += _velocity * dt;
            }
            if (!float.IsFinite(_angle) || !float.IsFinite(_velocity))
            {
                _angle = target;
                _velocity = 0f;
            }
            transform.localRotation = Quaternion.Euler(0f, 0f, _angle);
        }
    }
}
