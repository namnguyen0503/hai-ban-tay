using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Kéo lê chảo đồng (F04): chảo quá nặng để nhấc. Nhìn vào chảo, giữ chuột trái để bám rồi đi lùi; chảo trượt theo
    /// trên sàn như được kéo bằng tay, không lại gần quá. Tàu lắc đẩy chảo trượt ngang; quay mặt khỏi chảo hay để chảo kẹt
    /// thì tuột tay, phải bám lại.
    /// </summary>
    public sealed class TowLogic
    {
        /// <summary>Khoảng cách tay – chảo khi kéo (m, mặt phẳng ngang).</summary>
        public float Reach = 0.95f;
        /// <summary>Lệch quá góc này so với hướng nhìn thì tuột tay.</summary>
        public float MaxAngle = 40f;
        /// <summary>Chảo bị kẹt lùi lại quá xa thì tuột tay.</summary>
        public float SlipDistance = 1.6f;
        /// <summary>Trượt ngang mỗi giây ở độ nghiêng tối đa (m/s).</summary>
        public float DriftPerRoll = 0.35f;

        public bool Gripping { get; private set; }
        public int Slips { get; private set; }

        /// <summary>Bắt đầu bám (người gọi kiểm tra chuột trái, nhìn trúng chảo và đủ gần).</summary>
        public bool Grip()
        {
            if (Gripping)
                return false;
            Gripping = true;
            return true;
        }

        public void Release()
        {
            Gripping = false;
        }

        /// <summary>Vị trí chảo muốn tới khung này (chưa xét va chạm). roll: độ nghiêng tàu -1…1, beam: trục ngang tàu.</summary>
        public Vector3 Step(Vector3 pan, Vector3 hands, float roll, Vector3 beam, float dt)
        {
            var next = pan;
            if (Gripping)
            {
                var flat = Flat(next - hands);
                float d = flat.magnitude;
                if (d > Reach)
                    next -= flat / d * (d - Reach);
            }
            next += Flat(beam).normalized * (roll * DriftPerRoll * dt);
            return next;
        }

        /// <summary>Có tuột tay không: chảo lệch khỏi hướng nhìn hoặc kẹt xa quá.</summary>
        public bool CheckSlip(Vector3 pan, Vector3 hands, Vector3 forward)
        {
            if (!Gripping)
                return false;
            var to = Flat(pan - hands);
            bool slip = to.magnitude > SlipDistance ||
                        (to.sqrMagnitude > 0.01f && Vector3.Angle(Flat(forward), to) > MaxAngle);
            if (slip)
            {
                Gripping = false;
                Slips++;
            }
            return slip;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
