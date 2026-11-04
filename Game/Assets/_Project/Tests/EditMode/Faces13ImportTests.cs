using System.Linq;
using HaiBanTay.Editor.Import;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Tests
{
    public sealed class Faces13ImportTests
    {
        [Test]
        public void StoryIdentitiesDoNotChangeTheSeededCrowdPool() =>
            CollectionAssert.AreEquivalent(CharacterImporter.Roles,
                HaiBanTay.Editor.SceneBuilders.CrowdBuilder.AvailableRoles());

        [TestCase("Ba"), TestCase("Chieu")]
        public void AdultStoryRolesKeepTheCrowdImportContract(string role) =>
            new CharacterImportTests().RoleHasThreeDecreasingLodsOnOneAnimatedSkeleton(role);

        [Test]
        public void ChildHasItsOwnHumanoidProportionsAndAnimatedLods()
        {
            var path = CharacterImporter.CharRoot + "ChieuChild/ChieuChild.fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterImporter.PrefabRoot + "ChieuChild.prefab");
            Assert.IsNotNull(prefab);
            var child = Object.Instantiate(prefab);
            var adult = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CharacterImporter.PrefabRoot + "Le.prefab"));
            var baked = new Mesh();
            try
            {
                var animator = child.GetComponentInChildren<Animator>();
                Assert.IsTrue(animator.avatar.isValid && animator.isHuman);
                Assert.AreSame(AssetDatabase.LoadAssetAtPath<Avatar>(path), animator.avatar);
                Assert.AreNotSame(AssetDatabase.LoadAssetAtPath<Avatar>(CharacterImporter.AvatarModelPath), animator.avatar);
                Assert.AreEqual(Vector3.one, child.transform.localScale);
                var shoulder = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                var elbow = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                var wrist = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                Assert.Less(Mathf.Abs(shoulder.position.y - elbow.position.y), .02f, "Bind T-pose: khuỷu cùng độ cao vai.");
                Assert.Greater(Vector3.Distance(wrist.position, elbow.position), .10f, "Cẳng tay trẻ phải có chiều dài thật.");
                Assert.Less(Vector3.Distance(wrist.position, elbow.position), .23f);
                Assert.Greater(Vector3.Dot((elbow.position - shoulder.position).normalized,
                    (wrist.position - elbow.position).normalized), .95f, "T-pose: cẳng tay thẳng theo tay trên.");
                var childHead = animator.GetBoneTransform(HumanBodyBones.Head);
                var adultAnimator = adult.GetComponentInChildren<Animator>();
                var adultHead = adultAnimator.GetBoneTransform(HumanBodyBones.Head);
                Assert.Less(childHead.position.y, adultHead.position.y * .7f, "Chiều cao trẻ phải có trong bind, không ở root scale.");
                var childSkin = child.GetComponentsInChildren<SkinnedMeshRenderer>().First();
                var adultSkin = adult.GetComponentsInChildren<SkinnedMeshRenderer>().First();
                float HeadRatio(SkinnedMeshRenderer skin, Transform head) =>
                    (skin.transform.TransformPoint(Vector3.up * skin.sharedMesh.bounds.max.y).y - head.position.y)
                    / skin.sharedMesh.bounds.size.y;
                Assert.Greater(HeadRatio(childSkin, childHead), HeadRatio(adultSkin, adultHead) + .02f,
                    "Phần đầu trên xương Head phải chiếm tỉ lệ thân lớn hơn người lớn.");
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.speed = 0f;
                var lods = child.GetComponent<LODGroup>().GetLODs(); Assert.AreEqual(3, lods.Length);
                long previous = long.MaxValue;
                foreach (var lod in lods)
                {
                    long count = lod.renderers.Cast<SkinnedMeshRenderer>().Sum(x => x.sharedMesh.triangles.Length / 3);
                    Assert.Greater(count, 0); Assert.Less(count, previous); previous = count;
                }
                foreach (string pose in new[] { "Idle", "Walk", "SitIdle", "Talk" })
                {
                    Assert.IsTrue(animator.HasState(0, Animator.StringToHash(pose)));
                    animator.Play(pose, 0, .4f); animator.Update(0f);
                    if (pose == "SitIdle")
                    {
                        var thigh = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position;
                        var knee = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position;
                        var ankle = animator.GetBoneTransform(HumanBodyBones.LeftFoot).position;
                        Assert.Less(Vector3.Dot((knee - thigh).normalized, Vector3.down), .65f, "Ngồi: đùi trẻ chuyển ngang.");
                        Assert.Greater(Vector3.Angle(knee - thigh, ankle - knee), 50f, "Ngồi: gối trẻ phải gập.");
                    }
                    foreach (var renderer in lods.SelectMany(x => x.renderers).Cast<SkinnedMeshRenderer>())
                    {
                        renderer.BakeMesh(baked);
                        Assert.IsTrue(baked.vertices.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)), pose);
                        TestContext.WriteLine($"{pose}/{renderer.name}: bounds={baked.bounds}, Head={childHead.position}, bind={renderer.sharedMesh.bindposes[System.Array.IndexOf(renderer.bones, childHead)]}");
                        Assert.Less(baked.bounds.size.magnitude, 1.9f, pose);
                        var weights = renderer.sharedMesh.boneWeights;
                        var points = baked.vertices;
                        var headIndex = System.Array.IndexOf(renderer.bones, childHead);
                        Assert.GreaterOrEqual(headIndex, 0);
                        for (int i = 0; i < weights.Length; i++)
                            if (weights[i].boneIndex0 == headIndex && weights[i].weight0 > .9f)
                                Assert.Less(Vector3.Distance(renderer.transform.TransformPoint(points[i]), childHead.position), .28f,
                                    pose + ": da đầu phải ở gần xương đầu, không chỉ hữu hạn.");
                    }
                }
            }
            finally { Object.DestroyImmediate(baked); Object.DestroyImmediate(child); Object.DestroyImmediate(adult); }
        }
    }
}
