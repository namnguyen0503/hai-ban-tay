using System.Linq;
using System.Reflection;
using HaiBanTay.Editor.Import;
using HaiBanTay.Npc;
using HaiBanTay.Performance;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Tests
{
    public class CrowdLodImportTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void StaticHiddenAndAnimatedModesKeepOneLodRepresentation(bool withLods)
        {
            var root = new GameObject("CrowdLodImportTest");
            var cameraObject = new GameObject("CrowdLodTestCamera");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = new Vector3(0, 0.9f, -8f);
                var lod = root.AddComponent<CrowdLod>();
                lod.tierOverride = QualityTier.PC;
                lod.maxAnimatedPc = 0;
                lod.minSeconds = 0;
                typeof(CrowdLod).GetField("_camera", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(lod, camera);
                var npc = new GameObject("Npc");
                npc.transform.SetParent(root.transform, false);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterImporter.PrefabRoot + "Cook.prefab");
                var body = Object.Instantiate(prefab, npc.transform);
                var group = body.GetComponent<LODGroup>();
                var original = group.GetLODs();
                if (!withLods)
                {
                    foreach (var renderer in original.Skip(1).SelectMany(l => l.renderers))
                        Object.DestroyImmediate(renderer.gameObject);
                    Object.DestroyImmediate(group);
                    group = null;
                }
                var skins = body.GetComponentsInChildren<SkinnedMeshRenderer>();
                var animator = body.GetComponentInChildren<Animator>();
                animator.Rebind();
                animator.Update(0);
                var agent = npc.AddComponent<NpcAgent>();
                agent.Play("Idle");
                lod.Register(agent);
                Assert.AreEqual(1, lod.MemberCount);
                Tick(lod);
                Assert.AreEqual(1, lod.StaticCount);
                Assert.IsFalse(agent.Animated);
                Assert.IsFalse(animator.enabled);
                Assert.IsTrue(skins.All(s => !s.enabled));
                var statics = npc.GetComponentsInChildren<MeshRenderer>();
                Assert.AreEqual(skins.Length, statics.Length);
                Assert.IsTrue(statics.All(s => s.enabled));
                if (group != null)
                {
                    Assert.IsTrue(group.enabled);
                    var baked = group.GetLODs();
                    Assert.AreEqual(3, baked.Length);
                    CollectionAssert.AreEquivalent(statics, baked.SelectMany(l => l.renderers));
                    Assert.AreEqual(statics.Length, baked.SelectMany(l => l.renderers).Distinct().Count());
                    for (int level = 0; level < 3; level++)
                    {
                        Assert.AreEqual(original[level].screenRelativeTransitionHeight, baked[level].screenRelativeTransitionHeight);
                        Assert.AreEqual(Triangles(original[level]), Triangles(baked[level]), "static LOD must use corresponding mesh");
                    }
                }
                // Hidden mode disables native LOD selection and both renderer sets.
                camera.transform.position = new Vector3(0, 0.9f, -200f);
                Tick(lod);
                Assert.AreEqual(1, lod.HiddenCount);
                Assert.IsTrue(statics.All(s => !s.enabled));
                Assert.IsTrue(skins.All(s => !s.enabled));
                if (group != null) Assert.IsFalse(group.enabled);
                // Returning to animation restores the original renderer arrays and keeps the Animator cap.
                camera.transform.position = new Vector3(0, 0.9f, -8f);
                lod.maxAnimatedPc = 1;
                Tick(lod);
                Assert.AreEqual(1, lod.AnimatedCount);
                Assert.IsTrue(agent.Animated && animator.enabled);
                Assert.IsTrue(skins.All(s => s.enabled));
                Assert.IsTrue(statics.All(s => !s.enabled));
                Assert.LessOrEqual(lod.AnimatedCount, lod.MaxAnimated);
                if (group != null)
                {
                    Assert.IsTrue(group.enabled);
                    for (int level = 0; level < 3; level++)
                        CollectionAssert.AreEqual(original[level].renderers, group.GetLODs()[level].renderers);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cameraObject);
            }
        }

        static void Tick(CrowdLod lod) => typeof(CrowdLod).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(lod, null);

        static long Triangles(LOD lod)
        {
            long total = 0;
            foreach (var renderer in lod.renderers)
            {
                var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>().sharedMesh;
                for (int i = 0; i < mesh.subMeshCount; i++) total += mesh.GetIndexCount(i) / 3;
            }
            return total;
        }
    }
}
