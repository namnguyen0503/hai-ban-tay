using System.Linq;
using HaiBanTay.Editor.Import;
using HaiBanTay.Editor.SceneBuilders;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace HaiBanTay.Tests
{
    public class CharacterImportTests
    {
        [TestCaseSource(typeof(CharacterImporter), nameof(CharacterImporter.Roles))]
        public void RoleHasThreeDecreasingLodsOnOneAnimatedSkeleton(string role)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterImporter.PrefabRoot + role + ".prefab");
            Assert.IsNotNull(prefab, role);
            var instance = Object.Instantiate(prefab);
            var mesh = new Mesh();
            var graph = PlayableGraph.Create("CharacterImportTest");
            try
            {
                var groups = instance.GetComponentsInChildren<LODGroup>(true);
                Assert.AreEqual(1, groups.Length, role);
                var lods = groups[0].GetLODs();
                Assert.AreEqual(3, lods.Length, role);
                Assert.Less(lods[2].screenRelativeTransitionHeight, 0.01f, "Keep CrowdLod's distant silhouettes visible");
                var animators = instance.GetComponentsInChildren<Animator>(true);
                Assert.AreEqual(1, animators.Length, role);
                var animator = animators[0];
                Assert.IsTrue(animator.avatar.isValid && animator.avatar.isHuman, role);
                Assert.AreSame(AssetDatabase.LoadAssetAtPath<Avatar>(CharacterImporter.AvatarModelPath), animator.avatar);
                Assert.AreSame(AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterImporter.ControllerPath), animator.runtimeAnimatorController);
                Assert.IsFalse(animator.applyRootMotion);
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, CharacterImporter.LoadClip("Walk"));
                AnimationPlayableOutput.Create(graph, "Humanoid", animator).SetSourcePlayable(playable);
                graph.Play();
                playable.SetTime(0.3);
                graph.Evaluate(0);
                var originalBones = animator.GetComponentsInChildren<Transform>(true);
                long previous = long.MaxValue;
                for (int level = 0; level < 3; level++)
                {
                    Assert.IsNotEmpty(lods[level].renderers, role + "/LOD" + level);
                    long triangles = 0;
                    foreach (var renderer in lods[level].renderers)
                    {
                        var skin = renderer as SkinnedMeshRenderer;
                        Assert.IsNotNull(skin, role);
                        Assert.IsTrue(skin.sharedMesh.isReadable, "CrowdLod.BakeMesh needs CPU data");
                        Assert.IsTrue(skin.bones.All(b => b != null && originalBones.Contains(b)), role + ": bones must reference LOD0");
                        Assert.IsTrue(originalBones.Contains(skin.rootBone));
                        // Tóc, vải, đồ da một lớp vẽ hai mặt (TwoSidedMaterialImport, ghi chú 15/10 nhìn xuyên); còn lại URP Lit.
                        Assert.IsTrue(skin.sharedMaterials.All(m => m != null && (m.shader.name == "Universal Render Pipeline/Lit"
                            || m.shader.name == HaiBanTay.Editor.Setup.AssetFactory.TwoSidedShader)), role);
                        for (int sub = 0; sub < skin.sharedMesh.subMeshCount; sub++)
                            triangles += skin.sharedMesh.GetIndexCount(sub) / 3;
                        skin.BakeMesh(mesh);
                        Assert.Greater(mesh.vertexCount, 0, role);
                        Assert.Less(mesh.bounds.size.magnitude, 4.1f, role + "/LOD" + level + ": exploded skin");
                        Assert.IsTrue(mesh.vertices.All(v => Finite(v.x) && Finite(v.y) && Finite(v.z)), role);
                    }
                    Assert.Greater(triangles, 0);
                    Assert.Less(triangles, previous, role + "/LOD" + level);
                    TestContext.WriteLine($"{role}/LOD{level}: {triangles} triangles");
                    previous = triangles;
                }
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void TwentyRolesRemainDiscoverableByCrowdBuilder()
        {
            Assert.AreEqual(20, CharacterImporter.Roles.Length);
            CollectionAssert.IsSubsetOf(CharacterImporter.Roles, CrowdBuilder.AvailableRoles());
            CollectionAssert.IsSubsetOf(new[] { "Cook", "Helper", "SoldierA", "SoldierB", "Jacques" }, CharacterImporter.Roles);
        }

        [Test]
        public void ControllerKeepsLegacyStatesAndAddsSixteenTriggers()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterImporter.ControllerPath);
            Assert.IsNotNull(controller);
            var sm = controller.layers[0].stateMachine;
            Assert.AreEqual(31, sm.states.Length);
            Assert.AreEqual("Idle", sm.defaultState.name);
            CollectionAssert.AreEquivalent(CharacterImporter.Clips, sm.states.Select(s => s.state.name));
            foreach (var child in sm.states)
            {
                Assert.AreSame(CharacterImporter.LoadClip(child.state.name), child.state.motion);
                if (CharacterImporter.NewClips.Contains(child.state.name))
                {
                    Assert.IsTrue(controller.parameters.Any(p => p.name == child.state.name && p.type == AnimatorControllerParameterType.Trigger));
                    Assert.IsTrue(sm.anyStateTransitions.Any(t => t.destinationState == child.state
                        && t.conditions.Any(c => c.parameter == child.state.name && c.mode == AnimatorConditionMode.If)));
                }
                else
                    Assert.IsEmpty(child.state.transitions, "Keep legacy NpcAgent.CrossFade behavior");
            }
        }

        [TestCaseSource(typeof(CharacterImporter), nameof(CharacterImporter.Clips))]
        public void HumanoidClipUsesCookAvatarAndCorrectLoopFlag(string name)
        {
            var clip = CharacterImporter.LoadClip(name);
            Assert.IsNotNull(clip, name);
            Assert.IsTrue(clip.humanMotion, name);
            Assert.AreEqual(!CharacterImporter.OneShots.Contains(name), clip.isLooping, name);
            var importer = (ModelImporter)AssetImporter.GetAtPath(CharacterImporter.AnimRoot + name + ".fbx");
            Assert.IsTrue(importer.preserveHierarchy, name);
            Assert.AreEqual(ModelImporterAvatarSetup.CopyFromOther, importer.avatarSetup, name);
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<Avatar>(CharacterImporter.AvatarModelPath), importer.sourceAvatar);
        }

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
