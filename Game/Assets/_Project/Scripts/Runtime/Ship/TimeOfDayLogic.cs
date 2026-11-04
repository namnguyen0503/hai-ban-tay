using System;
using UnityEngine;

namespace HaiBanTay.Ship
{
    [Serializable]
    public struct WorldLightingKeyframe
    {
        public float hour;
        public Color sky, horizon, ground, light, deepWater, shallowWater;
        public float intensity, fogDensity, night;

        public WorldLightingKeyframe(float hour, Color sky, Color horizon, Color ground, Color light,
            float intensity, float fogDensity, float night, Color deepWater, Color shallowWater)
        {
            this.hour = hour;
            this.sky = sky;
            this.horizon = horizon;
            this.ground = ground;
            this.light = light;
            this.intensity = intensity;
            this.fogDensity = fogDensity;
            this.night = night;
            this.deepWater = deepWater;
            this.shallowWater = shallowWater;
        }
    }

    public struct WorldLightingState
    {
        public Vector3 SunDirection, MoonDirection, MainLightDirection;
        public Color Sky, Horizon, Ground, LightColor, FogColor, DeepWater, ShallowWater;
        public float LightIntensity, FogDensity, Night;
        public bool MoonIsMain;
    }

    /// <summary>Nội suy vòng 24 giờ; không phụ thuộc scene, đồng hồ hay GameObject.</summary>
    public static class TimeOfDayLogic
    {
        static readonly WorldLightingKeyframe[] Defaults =
        {
            new WorldLightingKeyframe(4f, new Color(0.008f, 0.014f, 0.04f), new Color(0.04f, 0.065f, 0.12f),
                new Color(0.015f, 0.022f, 0.035f), new Color(0.56f, 0.68f, 1f), 0.16f, 0.004f, 1f,
                new Color(0.005f, 0.023f, 0.045f), new Color(0.016f, 0.052f, 0.075f)),
            new WorldLightingKeyframe(5.5f, new Color(0.16f, 0.24f, 0.4f), new Color(0.72f, 0.65f, 0.58f),
                new Color(0.16f, 0.15f, 0.16f), new Color(1f, 0.80f, 0.64f), 0.5f, 0.0035f, 0.35f,
                new Color(0.018f, 0.085f, 0.13f), new Color(0.11f, 0.2f, 0.22f)),
            new WorldLightingKeyframe(12f, new Color(0.06f, 0.42f, 0.95f), new Color(0.60f, 0.75f, 0.88f),
                new Color(0.22f, 0.25f, 0.27f), new Color(1f, 0.96f, 0.88f), 1.4f, 0.0025f, 0f,
                new Color(0.006f, 0.065f, 0.28f), new Color(0.025f, 0.19f, 0.43f)),
            new WorldLightingKeyframe(18.5f, new Color(0.13f, 0.2f, 0.35f), new Color(0.96f, 0.43f, 0.2f),
                new Color(0.13f, 0.085f, 0.08f), new Color(1f, 0.56f, 0.28f), 0.55f, 0.005f, 0.2f,
                new Color(0.025f, 0.065f, 0.11f), new Color(0.13f, 0.16f, 0.17f)),
            new WorldLightingKeyframe(21f, new Color(0.006f, 0.012f, 0.032f), new Color(0.027f, 0.044f, 0.085f),
                new Color(0.01f, 0.015f, 0.025f), new Color(0.56f, 0.68f, 1f), 0.14f, 0.004f, 1f,
                new Color(0.004f, 0.018f, 0.035f), new Color(0.012f, 0.04f, 0.06f)),
        };

        public static WorldLightingKeyframe[] DefaultKeyframes => (WorldLightingKeyframe[])Defaults.Clone();

        /// <summary>Sao mất khi nền trời đủ sáng, kể cả lúc mức đêm đang nội suy ở bình minh.</summary>
        public static float StarVisibility(WorldLightingState state) => state.Night * (1f - Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(.04f, .14f, state.Sky.grayscale)));

        public static WorldLightingState Evaluate(float hour, WorldLightingKeyframe[] keyframes = null)
        {
            if (!float.IsFinite(hour))
                throw new ArgumentOutOfRangeException(nameof(hour));
            var keys = keyframes == null || keyframes.Length == 0 ? Defaults : keyframes;
            if (keys.Length < 2)
                throw new ArgumentException("Cần ít nhất hai keyframe.", nameof(keyframes));
            for (int i = 0; i < keys.Length; i++)
                if (!float.IsFinite(keys[i].hour) || keys[i].hour < 0f || keys[i].hour >= 24f
                    || (i > 0 && keys[i].hour <= keys[i - 1].hour))
                    throw new ArgumentException("Keyframe phải tăng dần trong [0, 24).", nameof(keyframes));

            float h = Mathf.Repeat(hour, 24f);
            int next = 0;
            while (next < keys.Length && h >= keys[next].hour)
                next++;
            var a = keys[(next + keys.Length - 1) % keys.Length];
            var b = keys[next % keys.Length];
            float end = b.hour > a.hour ? b.hour : b.hour + 24f;
            float sample = h < a.hour ? h + 24f : h;
            float t = (sample - a.hour) / (end - a.hour);

            // Bình minh 5:30, hoàng hôn 18:30; hai cung nối liên tục qua nửa đêm.
            bool day = h >= 5.5f && h < 18.5f;
            float angle = day ? (h - 5.5f) / 13f * Mathf.PI
                : Mathf.PI + Mathf.Repeat(h - 18.5f, 24f) / 11f * Mathf.PI;
            var sun = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0.35f).normalized;
            float horizonFade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Abs(sun.y) / 0.15f));
            var horizon = Color.Lerp(a.horizon, b.horizon, t);
            return new WorldLightingState
            {
                SunDirection = sun,
                MoonDirection = -sun,
                MainLightDirection = day ? -sun : sun,
                MoonIsMain = !day,
                Sky = Color.Lerp(a.sky, b.sky, t),
                Horizon = horizon,
                Ground = Color.Lerp(a.ground, b.ground, t),
                LightColor = Color.Lerp(a.light, b.light, t),
                LightIntensity = horizonFade * Mathf.Lerp(a.intensity, b.intensity, t),
                FogColor = horizon,
                FogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, t),
                Night = Mathf.Lerp(a.night, b.night, t),
                DeepWater = Color.Lerp(a.deepWater, b.deepWater, t),
                ShallowWater = Color.Lerp(a.shallowWater, b.shallowWater, t),
            };
        }
    }
}
