using UnityEngine;

namespace HaiBanTay.Player
{
    /// <summary>
    /// Thể lực và thân nhiệt của người chơi (cách game thể hiện sự vất vả).
    /// Mệt 0–1 tăng theo việc nặng và khi quá nóng/quá lạnh; đứng nghỉ thì hồi dần nhưng không xuống dưới mức nền của ngày.
    /// Thân nhiệt −1 (lạnh) … +1 (nóng) dịch dần về nhiệt độ quanh người (lò, kho lạnh).
    /// Không bao giờ chặn người chơi: mọi hiệu ứng đều có trần.
    /// </summary>
    public sealed class BodyLogic
    {
        public float RestDelay = 2.5f;
        public float RecoverPerSecond = 0.006f;
        public float HeatResponse = 0.2f;
        public float ExtremeFatiguePerSecond = 0.0015f;
        /// <summary>Ngồi nghỉ (phím C): hồi nhanh hơn đứng nghỉ, không phải chờ RestDelay.</summary>
        public float SeatedRecoverMultiplier = 2.5f;

        float _rest;

        public float Fatigue { get; private set; }
        public float Heat { get; private set; }
        /// <summary>Mức mệt nền (mang từ hôm trước); nghỉ không hồi xuống dưới mức này.</summary>
        public float Floor { get; private set; }
        public bool Resting => Seated || _rest >= RestDelay;
        public bool Seated { get; set; }

        public void Begin(float floor, float fatigue, float heat = 0f)
        {
            Floor = Mathf.Clamp01(floor);
            Fatigue = Mathf.Clamp(fatigue, Floor, 1f);
            Heat = Mathf.Clamp(heat, -1f, 1f);
            _rest = 0f;
        }

        public void Exert(float amount)
        {
            if (amount <= 0f)
                return;
            Fatigue = Mathf.Min(1f, Fatigue + amount);
            _rest = 0f;
        }

        /// <summary>busy: đang đi hoặc đang làm (không tính là nghỉ). exertPerSecond: gắng sức liên tục (vác nặng).</summary>
        public void Tick(float dt, float heatTarget, bool busy, float exertPerSecond)
        {
            if (dt <= 0f)
                return;
            Heat = Mathf.MoveTowards(Heat, Mathf.Clamp(heatTarget, -1f, 1f), HeatResponse * dt);
            float extreme = Mathf.Max(0f, Mathf.Abs(Heat) - 0.4f) / 0.6f;
            Fatigue = Mathf.Min(1f, Fatigue + (Mathf.Max(0f, exertPerSecond) + extreme * ExtremeFatiguePerSecond) * dt);
            if (busy || exertPerSecond > 0f)
                _rest = 0f;
            else
                _rest += dt;
            if (Resting)
                Fatigue = Mathf.MoveTowards(Fatigue, Floor, RecoverPerSecond * (Seated ? SeatedRecoverMultiplier : 1f) * dt);
            Fatigue = Mathf.Clamp(Fatigue, Floor, 1f);
        }

        static float Ramp(float from, float to, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, x));

        /// <summary>Hệ số tốc độ đi: 1 → 0,7 khi kiệt sức.</summary>
        public float SpeedMultiplier => 1f - 0.3f * Ramp(0.35f, 1f, Fatigue);
        /// <summary>Hệ số rung camera do mệt (nhân với lắc tàu).</summary>
        public float ShakeMultiplier => 1f + 0.9f * Ramp(0.3f, 1f, Fatigue);
        public float Tremble => Ramp(0.5f, 1f, Fatigue);
        public float Darkness => 0.55f * Ramp(0.45f, 1f, Fatigue);
        public float Warmth => Ramp(0.3f, 1f, Heat);
        public float Chill => Ramp(0.3f, 1f, -Heat);
        public float Sweat => Ramp(0.35f, 0.9f, Heat);
        /// <summary>Hệ số tốc độ làm việc tay (xúc than): chậm khi mệt và khi lạnh cóng.</summary>
        public float WorkSpeed => 1f - 0.25f * Ramp(0.5f, 1f, Fatigue) - 0.15f * Chill;
    }
}
