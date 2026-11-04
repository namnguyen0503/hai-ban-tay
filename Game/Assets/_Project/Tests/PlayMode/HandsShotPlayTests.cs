using System.Collections;
using System.Linq;
using HaiBanTay.Core;
using HaiBanTay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    public class HandsShotPlayTests
    {
        [TearDown]
        public void TearDown() => PlayTestUtil.ExitSim();

        [UnityTest]
        public IEnumerator SharedShotKeepsBothPalmsAboveSubtitlesInAllThreeScenes()
        {
            foreach (string scene in new[] { SceneFlow.PrologueScene, SceneFlow.GameScenes[0], SceneFlow.GameScenes[2] })
            {
                var root = PlayTestUtil.Root();
                root.ResetProgress();
                PlayTestUtil.EnterSim(20f);
                yield return PlayTestUtil.LoadAndSettle(scene);
                var rig = PlayerRig.Current;
                // Isolate the shared pose from each narrative's timeline; screenshot suite covers the real beats.
                rig.SetControl(false);
                var director = HaiBanTay.Narrative.SceneDirector.Current;
                director.StopAllCoroutines();
                yield return rig.EnterHandsShot(root.Config.handsShot, scene == SceneFlow.PrologueScene);
                rig.hands.Snap();
                yield return null;
                Assert.That(rig.look.Pitch, Is.InRange(0f, 12f));
                AssertPalms(rig.cam, rig.hands);
                PlayTestUtil.ExitSim();
            }
        }

        public static void AssertPalms(Camera cam, HandsRig hands)
        {
            Assert.AreEqual(HandPose.Open, hands.Pose);
            // A fixed 16:9 viewport matches the shipped 1080p frame, including batchmode without a window.
            float aspect = cam.aspect;
            cam.aspect = 16f / 9f;
            try
            {
                foreach (var r in hands.skinRenderers)
                {
                    if (!r.gameObject.activeInHierarchy) continue;
                    var mf = r.GetComponent<MeshFilter>();
                    var points = mf.sharedMesh.vertices.Select(v => cam.WorldToViewportPoint(mf.transform.TransformPoint(v))).ToArray();
                    var rig = PlayerRig.Current;
                    Debug.Log($"Polish12Palms {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}/{r.name}: y={points.Min(p => p.y):F6}..{points.Max(p => p.y):F6}; z={points.Min(p => p.z):F4}..{points.Max(p => p.z):F4}; tay L={cam.transform.InverseTransformPoint(hands.left.position):F4}, R={cam.transform.InverseTransformPoint(hands.right.position):F4}; vai L={cam.transform.InverseTransformPoint(rig.body.animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position):F4}, R={cam.transform.InverseTransformPoint(rig.body.animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position):F4}; giãn={rig.body.ArmStretch:F4}");
                    foreach (var v in mf.sharedMesh.vertices)
                    {
                        var p = cam.WorldToViewportPoint(mf.transform.TransformPoint(v));
                        Assert.Greater(p.z, cam.nearClipPlane);
                        Assert.That(p.x, Is.InRange(0.05f, 0.95f), r.name);
                        Assert.That(p.y, Is.InRange(0.235f, 0.52f), "tay phải trên phụ đề, trong nửa dưới khung");
                    }
                }
            }
            finally { cam.aspect = aspect; }
        }
    }
}
