using System.Linq;
using HaiBanTay.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace HaiBanTay.Tests
{
    public class TemperatureTintTests
    {
        [TestCase(1, 0.32f)]
        [TestCase(2, 0.38f)]
        public void TemperatureCoverageIsAtMostHalfPreviousArea(int layer, float previousMaximum)
        {
            var parent = new VisualElement();
            _ = new ScreenTint(parent);
            var elements = parent.Children().ToArray();
            var temperature = elements[layer];
            float tint = temperature.style.unityBackgroundImageTintColor.value.a;
            var old = ScreenTint.Vignette();
            var current = temperature.style.backgroundImage.value.texture;
            try
            {
                int Covered(Texture2D texture, float alpha) => texture.GetPixels32().Count(p => p.a / 255f * alpha > 0.1f);
                Assert.LessOrEqual(Covered(current, tint), Covered(old, previousMaximum) / 2);
                Assert.Less(tint * current.GetPixels32().Max(p => p.a) / 255f, previousMaximum);
                CollectionAssert.AreEqual(old.GetPixels32(), elements[0].style.backgroundImage.value.texture.GetPixels32(), "Lớp tối giữ nguyên.");
                Assert.AreEqual(0.85f, elements[0].style.unityBackgroundImageTintColor.value.a);
                Assert.AreEqual(Texture2D.whiteTexture, elements[3].style.backgroundImage.value.texture);
                Assert.AreEqual(0.92f, elements[3].style.unityBackgroundImageTintColor.value.a);
            }
            finally
            {
                Object.DestroyImmediate(old);
                Object.DestroyImmediate(current);
                Object.DestroyImmediate(elements[0].style.backgroundImage.value.texture);
            }
        }
    }
}
