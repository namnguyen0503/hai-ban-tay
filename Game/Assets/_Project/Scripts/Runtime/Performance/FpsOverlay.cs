using System.Globalization;
using HaiBanTay.Content;
using HaiBanTay.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HaiBanTay.Performance
{
    /// <summary>F1: FPS trung bình và thấp nhất (cửa sổ ~150 khung), ms, tầng, render scale.</summary>
    public sealed class FpsOverlay : MonoBehaviour
    {
        const int Window = 150;
        const float RefreshSeconds = 0.25f;

        readonly float[] _dt = new float[Window];
        int _next;
        int _count;
        float _sinceRefresh;
        UIRoot _ui;
        QualityTierManager _quality;
        string _format;
        string _speedFormat;

        public bool Visible { get; private set; }

        public void Initialize(UIRoot ui, QualityTierManager quality, ContentDatabase content)
        {
            _ui = ui;
            _quality = quality;
            _format = content.GetString("debug.fps");
            _speedFormat = content.GetString("debug.speed");
            _ui.SetFpsVisible(false);
        }

        public void Toggle()
        {
            Visible = !Visible;
            _ui.SetFpsVisible(Shown);
            _next = 0;
            _count = 0;
        }

        /// <summary>Khung FPS cũng hiện khi đang tua nhanh (Ctrl+=), để luôn thấy tốc độ hiện tại.</summary>
        bool Shown => Visible || Core.DevSpeed.Scale > 1f;

        public void ShowSpeed()
        {
            _ui.SetFpsVisible(Shown);
            _sinceRefresh = RefreshSeconds;
        }

        void Update()
        {
            if (!Shown)
                return;
            _dt[_next] = Time.unscaledDeltaTime;
            _next = (_next + 1) % Window;
            if (_count < Window)
                _count++;

            _sinceRefresh += Time.unscaledDeltaTime;
            if (_sinceRefresh < RefreshSeconds)
                return;
            _sinceRefresh = 0f;

            float sum = 0f, max = 0f;
            for (int i = 0; i < _count; i++)
            {
                sum += _dt[i];
                if (_dt[i] > max)
                    max = _dt[i];
            }
            if (sum <= 0f || max <= 0f)
                return;
            float avgFps = _count / sum;
            float minFps = 1f / max;
            float ms = sum / _count * 1000f;
            float scale = (GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset)?.renderScale ?? 1f;
            string text = string.Format(CultureInfo.InvariantCulture, _format,
                avgFps, minFps, ms, _quality.Current, scale);
            if (Core.DevSpeed.Scale > 1f)
                text += string.Format(CultureInfo.InvariantCulture, _speedFormat, Core.DevSpeed.Scale);
            _ui.SetFpsText(text);
        }
    }
}
