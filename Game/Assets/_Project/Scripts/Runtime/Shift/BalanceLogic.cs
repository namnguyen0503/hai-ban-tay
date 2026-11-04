using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Giữ thăng bằng khi vác bao lên cầu thang hẹp lúc tàu chòng chành (F05). Bao nghiêng theo tàu và càng nghiêng càng
    /// đổ nhanh; rê chuột ngang ngược chiều nghiêng để đỡ. Nghiêng hết mức thì bao tuột (người không ngã).
    /// </summary>
    public sealed class BalanceLogic
    {
        /// <summary>Độ đẩy của tàu ở độ nghiêng tối đa (đơn vị Lean mỗi giây).</summary>
        public float Push = 0.8f;
        /// <summary>Bao càng nghiêng càng đổ nhanh (mỗi giây, nhân với Lean).</summary>
        public float Instability = 0.9f;
        /// <summary>Mỗi pixel rê chuột đỡ được bao nhiêu Lean.</summary>
        public float CounterPerPixel = 0.004f;

        public float Lean { get; private set; }
        public int Slips { get; private set; }

        public void Reset()
        {
            Lean = 0f;
        }

        /// <summary>roll: độ nghiêng tàu -1…1 (dương = nghiêng phải). mouseDx: rê chuột ngang khung này; rê trái đỡ bao
        /// đang nghiêng phải.
        /// Trả về true khi bao tuột.</summary>
        public bool Tick(float roll, float mouseDx, float dt)
        {
            Lean += (roll * Push + Lean * Instability) * dt + mouseDx * CounterPerPixel;
            Lean = Mathf.Clamp(Lean, -1f, 1f);
            if (Mathf.Abs(Lean) < 1f)
                return false;
            Slips++;
            Lean = 0f;
            return true;
        }
    }
}
