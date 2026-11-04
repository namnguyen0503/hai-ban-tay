using System;
using System.Linq;
using HaiBanTay.Editor.Import;
using HaiBanTay.Editor.SceneBuilders;
using HaiBanTay.Player;
using HaiBanTay.Interaction;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Tests
{
    public class FirstPersonHandsTests
    {
        [Test]
        public void EveryPoseHasStaticSkinAndSleeveWithinBudgetAtWristOrigin()
        {
            foreach (HandPose pose in Enum.GetValues(typeof(HandPose)))
                foreach (string side in new[] { "L", "R" })
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{HandsImporter.Folder}/Hand_{side}_{pose}.fbx");
                    Assert.IsNotNull(model, $"Thiếu {side}/{pose}");
                    Assert.IsEmpty(model.GetComponentsInChildren<SkinnedMeshRenderer>(true));
                    var meshes = model.GetComponentsInChildren<MeshFilter>(true);
                    Assert.AreEqual(2, meshes.Length, "da/áo phải tách renderer");
                    Assert.That(meshes.Sum(m => m.sharedMesh.triangles.Length / 3), Is.InRange(200, 3000));
                    var skin = meshes.Single(m => m.name.StartsWith("Skin"));
                    var points = skin.sharedMesh.vertices.Select(v => model.transform.InverseTransformPoint(skin.transform.TransformPoint(v))).ToArray();
                    var bounds = new Bounds(points[0], Vector3.zero);
                    foreach (var v in points) bounds.Encapsulate(v);
                    Assert.That(bounds.size.x, Is.InRange(0.065f, 0.16f), "rộng bàn tay theo mét");
                    Assert.That(bounds.max.z, Is.InRange(0.09f, 0.25f), "ngón phải hướng +Z từ cổ tay");
                    Assert.That(bounds.min.z, Is.InRange(-0.07f, 0.01f), "gốc cổ tay");
                    var sleeve = meshes.Single(m => m.name.StartsWith("Sleeve"));
                    var sleevePoints = sleeve.sharedMesh.vertices.Select(v => model.transform.InverseTransformPoint(sleeve.transform.TransformPoint(v))).ToArray();
                    Assert.That(sleevePoints.Min(v => v.z), Is.InRange(-0.20f, -0.12f), "áo nối về cẳng tay -Z");
                    Assert.That(sleevePoints.Max(v => v.z), Is.InRange(0f, 0.01f), "cổ áo tại gốc cổ tay");
                    foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                        Assert.IsTrue(renderer.sharedMaterials.All(m => m != null && m.name.StartsWith("Gen_FP_")));
                }
        }

        [Test]
        public void HeavySackOnRightShoulderLeavesCenterClearEvenAtBalanceLimits()
        {
            var cam = new GameObject("ShoulderCamera").AddComponent<Camera>();
            cam.fieldOfView = 65f;
            cam.aspect = 16f / 9f;
            cam.nearClipPlane = 0.03f;
            var rig = PlayerBuilder.Spawn(cam, Vector3.zero, 0f);
            var sack = Props.PotatoSack(null, Vector3.zero, new Bounds(Vector3.zero, Vector3.one * 100f));
            try
            {
                rig.interactor.Pick(sack);
                Assert.AreSame(rig.interactor.holdRightShoulder, sack.transform.parent);
                sack.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (float lean in new[] { -22f, 0f, 22f })
                {
                    rig.interactor.holdTwoHands.localRotation = Quaternion.Euler(0f, 0f, lean);
                    foreach (var renderer in sack.GetComponentsInChildren<Renderer>())
                    {
                        Assert.Greater(cam.WorldToViewportPoint(renderer.bounds.center).x, 0.55f,
                            $"Bao phải ở nửa phải khi nghiêng {lean} độ");
                        Assert.Less(cam.WorldToViewportPoint(renderer.bounds.center).x, 1f, "Bao phải còn thấy trong khung hình");
                        Assert.IsFalse(renderer.bounds.IntersectRay(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f))),
                            "Ngay cả bounds renderer cũng không được che tia tâm; collider khi cầm đã tắt");
                    }
                }
                var crate = new GameObject("LightItem").AddComponent<Holdable>();
                try
                {
                    rig.interactor.ReleaseHeld();
                    rig.interactor.Pick(crate);
                    Assert.AreSame(rig.interactor.holdOneHand, crate.transform.parent);
                    Assert.AreEqual(HandPose.Grip, rig.hands.Pose);
                }
                finally { UnityEngine.Object.DestroyImmediate(crate.gameObject); }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sack.gameObject);
                UnityEngine.Object.DestroyImmediate(rig.gameObject);
                UnityEngine.Object.DestroyImmediate(cam.gameObject);
            }
        }

        [Test]
        public void BuiltRigGrimeOnlyTouchesSkinAndDoesNotScaleHands()
        {
            var cam = new GameObject("HandsTestCamera").AddComponent<Camera>();
            PlayerRig rig = null;
            try
            {
                rig = PlayerBuilder.Spawn(cam, Vector3.zero, 0f);
                Assert.AreEqual(10, rig.hands.skinRenderers.Length);
                Assert.IsTrue(rig.hands.skinRenderers.All(r => r.name.StartsWith("Skin")));
                foreach (var mesh in rig.hands.leftPoseMeshes.Concat(rig.hands.rightPoseMeshes))
                {
                    Assert.AreEqual(Vector3.one, mesh.transform.localScale);
                    Assert.AreEqual(Vector3.zero, mesh.transform.localPosition);
                    foreach (var renderer in mesh.GetComponentsInChildren<Renderer>(true))
                        Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, renderer.shadowCastingMode);
                }
            }
            finally
            {
                if (rig != null) UnityEngine.Object.DestroyImmediate(rig.gameObject);
                UnityEngine.Object.DestroyImmediate(cam.gameObject);
            }
        }
    }
}
