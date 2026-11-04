using UnityEngine;
using UnityEngine.UIElements;

namespace HaiBanTay.UI
{
    /// <summary>Vòng tiến độ quanh tâm màn hình (xúc than, cọ nồi, giơ tay).</summary>
    public sealed class ProgressRing : VisualElement
    {
        float _value;

        public Color TrackColor = new Color(1f, 1f, 1f, 0.25f);
        public Color FillColor = new Color(0.93f, 0.88f, 0.77f, 0.95f);
        public float Thickness = 6f;

        public ProgressRing()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("hbt-ring");
            generateVisualContent += Draw;
        }

        public float Value
        {
            get => _value;
            set
            {
                float v = Mathf.Clamp01(value);
                if (Mathf.Approximately(v, _value))
                    return;
                _value = v;
                MarkDirtyRepaint();
            }
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width <= 0f)
                return;
            var center = r.center;
            float radius = Mathf.Min(r.width, r.height) * 0.5f - Thickness;
            var p = ctx.painter2D;
            p.lineWidth = Thickness;
            p.lineCap = LineCap.Round;

            p.strokeColor = TrackColor;
            p.BeginPath();
            p.Arc(center, radius, 0f, 360f);
            p.Stroke();

            if (_value <= 0f)
                return;
            p.strokeColor = FillColor;
            p.BeginPath();
            p.Arc(center, radius, -90f, -90f + 360f * _value);
            p.Stroke();
        }
    }
}
