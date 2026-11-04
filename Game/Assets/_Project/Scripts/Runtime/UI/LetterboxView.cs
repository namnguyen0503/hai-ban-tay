using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace HaiBanTay.UI
{
    /// <summary>Hai dải đen trên/dưới (E1). Phủ tràn viền, nằm ngoài vùng an toàn.</summary>
    public sealed class LetterboxView
    {
        const float HeightPercent = 11f;
        readonly VisualElement _top;
        readonly VisualElement _bottom;

        public float Amount { get; private set; }

        public LetterboxView(VisualElement parent)
        {
            _top = Ui.Box("hbt-letterbox-bar", parent);
            _top.style.top = 0;
            _bottom = Ui.Box("hbt-letterbox-bar", parent);
            _bottom.style.bottom = 0;
            Set(0f);
        }

        public void Set(float amount)
        {
            Amount = Mathf.Clamp01(amount);
            var h = new Length(HeightPercent * Amount, LengthUnit.Percent);
            _top.style.height = h;
            _bottom.style.height = h;
        }

        public IEnumerator Slide(float to, float seconds)
        {
            float from = Amount;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                Set(Mathf.SmoothStep(from, to, t / seconds));
                yield return null;
            }
            Set(to);
        }
    }
}
