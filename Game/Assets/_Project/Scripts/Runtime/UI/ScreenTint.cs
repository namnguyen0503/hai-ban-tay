using UnityEngine;
using UnityEngine.UIElements;

namespace HaiBanTay.UI
{
    /// <summary>Viền màn hình theo cơ thể: lạnh (kho lạnh), ấm (sát lò), tối (mệt). Mỗi lớp dịch mượt về mức đích.
    /// Thêm lớp nước tóe (Splash) phủ cả màn hình khi sóng đánh qua boong (F05b), tự tan.</summary>
    public sealed class ScreenTint
    {
        readonly Layer _cold;
        readonly Layer _warm;
        readonly Layer _dark;
        readonly Layer _splash;

        sealed class Layer
        {
            public readonly VisualElement Element;
            public float Target;
            public float Value;
            readonly float _speed;

            public Layer(VisualElement parent, Texture2D vignette, Color tint, float speed)
            {
                Element = Ui.Box("hbt-cold", parent);
                Element.style.backgroundImage = new StyleBackground(vignette);
                Element.style.unityBackgroundImageTintColor = tint;
                _speed = speed;
            }

            public void Tick(float dt)
            {
                if (Mathf.Approximately(Value, Target))
                    return;
                Value = Mathf.MoveTowards(Value, Target, dt * _speed);
                Element.style.opacity = Value;
            }

            public void Clear()
            {
                Target = Value = 0f;
                Element.style.opacity = 0f;
            }
        }

        public ScreenTint(VisualElement parent)
        {
            var vignette = Vignette();
            _dark = new Layer(parent, vignette, new Color(0f, 0f, 0f, 0.85f), 0.6f);
            var temperature = Vignette(0.78f);
            _warm = new Layer(parent, temperature, new Color(1f, 0.45f, 0.15f, 0.16f), 0.8f);
            _cold = new Layer(parent, temperature, new Color(0.62f, 0.78f, 1f, 0.19f), 1.5f);
            _splash = new Layer(parent, Texture2D.whiteTexture, new Color(0.78f, 0.88f, 0.92f, 0.92f), 0.55f);
        }

        public float Cold => _cold.Value;
        public float Warm => _warm.Value;
        public float Dark => _dark.Value;
        public float SplashLevel => _splash.Value;

        /// <summary>Nước tóe phủ màn hình ngay ở mức strength (0–1) rồi tan dần.</summary>
        public void Splash(float strength)
        {
            _splash.Value = Mathf.Clamp01(strength);
            _splash.Element.style.opacity = _splash.Value;
            _splash.Target = 0f;
        }

        // Viền mềm: trong suốt ở giữa, đậm dần ra mép (hình elip theo tỉ lệ màn hình).
        public static Texture2D Vignette(float start = 0.55f)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v) / 1.4142f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, 1f, r));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>Viền lạnh bật/tắt (cảnh không có ca làm).</summary>
        public void SetCold(bool on)
        {
            _cold.Target = on ? 1f : 0f;
        }

        /// <summary>Mức 0–1 của ba lớp (BodyState).</summary>
        public void SetBody(float cold, float warm, float dark)
        {
            _cold.Target = Mathf.Clamp01(cold);
            _warm.Target = Mathf.Clamp01(warm);
            _dark.Target = Mathf.Clamp01(dark);
        }

        public void Tick()
        {
            float dt = Time.deltaTime;
            _cold.Tick(dt);
            _warm.Tick(dt);
            _dark.Tick(dt);
            _splash.Tick(dt);
        }

        public void Clear()
        {
            _cold.Clear();
            _warm.Clear();
            _dark.Clear();
            _splash.Clear();
        }
    }
}
