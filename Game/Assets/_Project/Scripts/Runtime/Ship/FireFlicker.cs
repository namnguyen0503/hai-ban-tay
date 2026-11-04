using HaiBanTay.Shift;
using UnityEngine;

namespace HaiBanTay.Ship
{
    /// <summary>
    /// Lửa lò nhấp nháy (không cấp phát trong Update): nhiễu Perlin làm đèn lửa, than hồng (emission qua MaterialPropertyBlock)
    /// và các lớp ánh lửa cộng (additive) chớp theo; số hạt ngọn lửa và độ cao ngọn theo mức lửa.
    /// Mức lửa lấy từ CoalStation (SetFire), hoặc ăn theo một CoalStation khác (follow, có sàn followFloor), hoặc cố định (level).
    /// </summary>
    public sealed class FireFlicker : MonoBehaviour
    {
        [System.Serializable]
        public struct Glow
        {
            public Renderer renderer;
            /// <summary>Hệ số nhân màu tint cho _EmissionColor ở mức lửa đầy.</summary>
            public float strength;
        }

        [System.Serializable]
        public struct Overlay
        {
            public Renderer renderer;
            /// <summary>Màu và độ đậm (alpha) của lớp cộng ở mức lửa đầy; áp qua _BaseColor.</summary>
            public Color color;
        }

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public Light fireLight;
        public float lightIntensity = 1f;
        public Color lightColor = new Color(1f, 0.5f, 0.2f);
        public Glow[] glows = new Glow[0];
        public Overlay[] overlays = new Overlay[0];
        public ParticleSystem[] flames = new ParticleSystem[0];
        public CoalStation follow;
        [Range(0f, 1f)] public float followFloor = 0.5f;
        [Range(0f, 1f)] public float level = 1f;
        public Color tint = new Color(1f, 0.46f, 0.19f);
        public float flickerSpeed = 1f;

        MaterialPropertyBlock _mpb;
        float[] _rates;
        float[] _lifetimes;
        float _applied = -1f;
        float _seed;

        /// <summary>Hệ số chớp hiện tại quanh 1 (khoảng 0,75–1,25).</summary>
        public float Flicker { get; private set; } = 1f;

        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            _seed = Mathf.Abs(transform.position.x * 13.1f + transform.position.z * 7.3f) % 97f;
            _rates = new float[flames.Length];
            _lifetimes = new float[flames.Length];
            for (int i = 0; i < flames.Length; i++)
            {
                if (flames[i] == null)
                    continue;
                _rates[i] = flames[i].emission.rateOverTime.constant;
                _lifetimes[i] = flames[i].main.startLifetimeMultiplier;
            }
        }

        /// <summary>CoalStation đẩy mức lửa, màu và cường độ đèn của lò (cường độ gốc, chưa nhân độ chớp).</summary>
        public void SetFire(float fireLevel, Color color, float intensity)
        {
            level = fireLevel;
            tint = color;
            lightColor = color;
            lightIntensity = intensity;
        }

        void Update()
        {
            if (follow != null)
                level = Mathf.Lerp(followFloor, 1f, follow.Fire.Level);
            float t = Time.time * flickerSpeed;
            float slow = Mathf.PerlinNoise(_seed + t * 5.5f, 0.31f);
            float fast = Mathf.PerlinNoise(_seed + 17f + t * 14f, 4.7f);
            Flicker = 1f + 0.18f * (slow - 0.5f) + 0.10f * (fast - 0.5f);

            if (fireLight != null)
            {
                fireLight.intensity = lightIntensity * Flicker;
                fireLight.color = lightColor;
            }
            float shine = Mathf.Clamp01(level);
            float g = Mathf.Lerp(1f, Flicker, 0.9f);
            for (int i = 0; i < glows.Length; i++)
            {
                var r = glows[i].renderer;
                if (r == null)
                    continue;
                _mpb.Clear();
                _mpb.SetColor(EmissionId, shine > 0f ? tint * (glows[i].strength * shine * g) : Color.black);
                r.SetPropertyBlock(_mpb);
            }
            float a = level * level * Flicker;
            for (int i = 0; i < overlays.Length; i++)
            {
                var r = overlays[i].renderer;
                if (r == null)
                    continue;
                var c = overlays[i].color;
                _mpb.Clear();
                _mpb.SetColor(BaseColorId, new Color(c.r, c.g, c.b, c.a * a));
                r.SetPropertyBlock(_mpb);
            }
            if (Mathf.Abs(level - _applied) > 0.02f)
                ApplyLevel();
        }

        void ApplyLevel()
        {
            _applied = level;
            for (int i = 0; i < flames.Length; i++)
            {
                var ps = flames[i];
                if (ps == null)
                    continue;
                var emission = ps.emission;
                emission.enabled = level > 0.07f;
                emission.rateOverTime = _rates[i] * Mathf.Lerp(0.2f, 1f, level);
                var main = ps.main;
                main.startLifetimeMultiplier = _lifetimes[i] * Mathf.Lerp(0.55f, 1f, level);
            }
        }
    }
}
