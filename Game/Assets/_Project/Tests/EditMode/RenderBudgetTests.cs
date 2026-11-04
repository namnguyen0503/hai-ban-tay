using HaiBanTay.Editor.Setup;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.Rendering.Universal;

namespace HaiBanTay.Tests
{
    /// <summary>Cấu hình URP hai tầng đúng PLAN 3.3 (tầng Laptop là ràng buộc cứng).</summary>
    public class RenderBudgetTests
    {
        static UniversalRenderPipelineAsset Load(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            Assert.IsNotNull(asset, "thiếu " + path);
            return asset;
        }

        static bool SsaoActive(UniversalRenderPipelineAsset asset)
        {
            var list = new SerializedObject(asset).FindProperty("m_RendererDataList");
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is ScriptableRendererData data)
                    foreach (var f in data.rendererFeatures)
                        if (f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion" && f.isActive)
                            return true;
            return false;
        }

        [Test]
        public void LaptopTierWithinBudget()
        {
            var a = Load(ProjectSetup.LaptopAssetPath);
            Assert.That(a.renderScale, Is.InRange(0.67f, 0.75f));
            Assert.AreEqual(UpscalingFilterSelection.FSR, a.upscalingFilter);
            Assert.LessOrEqual(a.shadowDistance, 15f);
            Assert.AreEqual(1, a.shadowCascadeCount);
            Assert.LessOrEqual(a.mainLightShadowmapResolution, 1024);
            Assert.IsFalse(SsaoActive(a), "tầng Laptop phải tắt SSAO");
        }

        [Test]
        public void PcTierHasSsao()
        {
            var a = Load(ProjectSetup.PcAssetPath);
            Assert.AreEqual(1f, a.renderScale, 0.001f);
            Assert.IsTrue(SsaoActive(a), "tầng PC bật SSAO");
        }
    }
}
