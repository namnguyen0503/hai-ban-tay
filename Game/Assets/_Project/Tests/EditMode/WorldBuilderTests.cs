using HaiBanTay.Editor.SceneBuilders;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Ship;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace HaiBanTay.Tests
{
    public class WorldBuilderTests
    {
        [Test]
        public void BothTiersAndNamedMasksUseRenderingLayers()
        {
            ProjectSetup.ApplyRenderTiers();
            foreach (var path in new[] { ProjectSetup.LaptopAssetPath, ProjectSetup.PcAssetPath })
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                Assert.IsTrue(asset.useRenderingLayers, path);
            }
            Assert.AreEqual("Interior", RenderingLayerMask.RenderingLayerToName(1));
            Assert.AreEqual("Exterior", RenderingLayerMask.RenderingLayerToName(2));
            Assert.AreEqual(0u, WorldRenderingLayers.Interior & WorldRenderingLayers.Exterior);
        }

        [Test]
        public void PresetsAreBuildReferencedAndRiverIsCalmerThanSea()
        {
            WorldBuilder.EnsureMaterials();
            var sea = AssetDatabase.LoadAssetAtPath<Material>(WorldBuilder.MaterialFolder + "/Ocean_Sea.mat");
            var river = AssetDatabase.LoadAssetAtPath<Material>(WorldBuilder.MaterialFolder + "/Ocean_River.mat");
            var harbor = AssetDatabase.LoadAssetAtPath<Material>(WorldBuilder.MaterialFolder + "/Ocean_Harbor.mat");
            var sky = AssetDatabase.LoadAssetAtPath<Material>(WorldBuilder.MaterialFolder + "/SkyDome.mat");
            Assert.IsNotNull(sky);
            Assert.AreEqual("HaiBanTay/SkyDome", sky.shader.name);
            Assert.AreEqual("HaiBanTay/Ocean", sea.shader.name);
            Assert.Less(river.GetVector("_WaveA").z, harbor.GetVector("_WaveA").z);
            Assert.Less(harbor.GetVector("_WaveA").z, sea.GetVector("_WaveA").z);
            Assert.Less(river.GetFloat("_WaveSpeed"), sea.GetFloat("_WaveSpeed"));
            Assert.IsFalse(ShaderUtil.ShaderHasError(sea.shader), string.Join("\n",
                System.Array.ConvertAll(ShaderUtil.GetShaderMessages(sea.shader), message => message.message)));
            Assert.IsFalse(ShaderUtil.ShaderHasError(sky.shader), string.Join("\n",
                System.Array.ConvertAll(ShaderUtil.GetShaderMessages(sky.shader), message => message.message)));
        }

        [Test]
        public void HelpersSetRendererAndLightMaskIndependentlyOfPhysicsLayer()
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            child.transform.SetParent(root.transform);
            try
            {
                BuilderUtils.SetRenderingLayer(root, WorldRenderingLayers.Exterior, false);
                Assert.AreEqual(WorldRenderingLayers.Exterior, root.GetComponent<Renderer>().renderingLayerMask);
                Assert.AreEqual(1u, child.GetComponent<Renderer>().renderingLayerMask);
                BuilderUtils.SetRenderingLayer(root, WorldRenderingLayers.Interior);
                Assert.AreEqual(WorldRenderingLayers.Interior, child.GetComponent<Renderer>().renderingLayerMask);
                var light = root.AddComponent<Light>();
                BuilderUtils.SetLightLayer(light, WorldRenderingLayers.Interior);
                Assert.AreEqual(WorldRenderingLayers.Interior, light.GetUniversalAdditionalLightData().renderingLayers.value);
                Assert.AreEqual((int)WorldRenderingLayers.Interior, light.renderingLayerMask);
                Assert.AreEqual(0, root.layer);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
