using HaiBanTay.Player;
using UnityEngine;

namespace HaiBanTay.Ship
{
    /// <summary>Lắc tàu giả lập (D6): cường độ 0–1 điều khiển noise camera, góc nghiêng cho đèn treo,
    /// gia tốc ngang (có trần) cho đồ rời.</summary>
    public sealed class ShipMotion : MonoBehaviour
    {
        public static ShipMotion Current { get; private set; }

        [Range(0f, 1f)] public float intensity = 0.15f;
        public float rollPeriod = 9.4f;
        public float maxRollDegrees = 7f;
        public float maxLateralAccel = 1.6f;
        public float baseNoise = 0.04f;
        public float noisePerIntensity = 0.65f;
        public float carryNoiseMultiplier = 1.7f;

        float _time;

        public float Intensity
        {
            get => intensity;
            set => intensity = Mathf.Clamp01(value);
        }

        /// <summary>Góc nghiêng hiện tại (độ), dương = nghiêng phải.</summary>
        public float Roll { get; private set; }
        public float Pitch { get; private set; }
        /// <summary>Nhấp nhô chuẩn hóa, biên độ không vượt Intensity; WorldMotion đổi sang mét.</summary>
        public float HeaveFraction { get; private set; }
        /// <summary>Gia tốc ngang theo trục X của tàu (m/s²), đã kẹp trần.</summary>
        public Vector3 LateralAccel { get; private set; }

        void OnEnable()
        {
            Current = this;
        }

        void OnDisable()
        {
            if (Current == this)
                Current = null;
        }

        void Update()
        {
            Step(Time.deltaTime);
            var rig = PlayerRig.Current;
            if (rig != null)
            {
                float carry = rig.motor != null && rig.motor.Carrying ? carryNoiseMultiplier : 1f;
                rig.SetNoise((baseNoise * Mathf.Clamp01(intensity / 0.15f) + intensity * noisePerIntensity) * carry);
            }
        }

        /// <summary>Ba chu kỳ không đồng ước; tổng trọng số 1 giữ biên độ trong [-1; 1].</summary>
        public static float SampleSway(float time, float period, float offset = 0f)
        {
            float phase = 2f * Mathf.PI * time / Mathf.Max(0.1f, period);
            return 0.78f * Mathf.Sin(phase + offset)
                + 0.15f * Mathf.Sin(phase * 1.41421356f + offset + 1.1f)
                + 0.07f * Mathf.Sin(phase * 1.73205081f + offset + 2.4f);
        }

        public void Step(float dt)
        {
            _time += dt;
            float phase = SampleSway(_time, rollPeriod);
            float level = Mathf.Clamp01(intensity);
            Roll = phase * maxRollDegrees * Mathf.Min(1f, level * 1.35f);
            Pitch = SampleSway(_time, rollPeriod * 1.23f, 0.7f) * Mathf.Min(1.5f, level / 0.3f);
            HeaveFraction = SampleSway(_time, rollPeriod * 0.87f, 1.9f) * level;
            float accel = Mathf.Clamp(phase * maxLateralAccel * level, -maxLateralAccel, maxLateralAccel);
            LateralAccel = transform.right * accel;
        }
    }
}
