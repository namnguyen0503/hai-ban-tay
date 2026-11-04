using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using HaiBanTay.Core;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>Đối chứng chân/cổ chân của toàn bộ vai, không thay bộ test ngân sách render.</summary>
    [Category("Screenshots"), Explicit]
    public sealed class Feet12Shots
    {
        static string Prefix => "feet12_" + (System.Environment.GetEnvironmentVariable("FEET12_PHASE") ?? "after") + "_";
        static readonly string[] Roles = {
            "Cook", "Helper", "SoldierA", "SoldierB", "Jacques", "Mai", "Le", "Captain", "Purser",
            "PorterVN_A", "PorterVN_B", "PorterVN_C", "VendorVN_F", "CivilianVN", "PassengerFR_M1",
            "PassengerFR_M2", "PassengerFR_F", "SailorFR", "DockerMRS", "Waiter"
        };

        [TearDown] public void TearDown() => PlayTestUtil.ExitSim();

        static IEnumerator Save(Camera cam, string name)
        {
            yield return null;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var request = new RenderPipeline.StandardRequest { destination = rt };
            Assert.IsTrue(RenderPipeline.SupportsRenderRequest(cam, request));
            RenderPipeline.SubmitRenderRequest(cam, request);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(ScreenshotCapture.Folder, Prefix + name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
        }

        static IEnumerator Views(Camera cam, Animator animator, string name, float ground)
        {
            var left = animator.GetBoneTransform(HumanBodyBones.LeftFoot).position;
            var right = animator.GetBoneTransform(HumanBodyBones.RightFoot).position;
            var target = (left + right) * .5f + Vector3.up * .08f;
            // Cận đúng một mét, góc chéo thấy gót, mu chân, mặt trong và ống quần.
            cam.fieldOfView = 50f;
            cam.transform.position = target + new Vector3(.65f, .30f, .70f).normalized;
            cam.transform.LookAt(target);
            yield return Save(cam, name + "_close1m");
            cam.fieldOfView = 60f;
            cam.transform.position = new Vector3(target.x + .42f, ground + 1.62f, target.z + .70f);
            cam.transform.LookAt(target);
            yield return Save(cam, name + "_eye162");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator EveryRoleStandingWalkingAndSeated()
        {
#if UNITY_EDITOR
            PlayTestUtil.Root();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[0]);
            var cameraObject = new GameObject("Feet12Camera");
            var cam = cameraObject.AddComponent<Camera>(); cam.enabled = false;
            cam.nearClipPlane = .03f; cam.cullingMask = 1 << 31;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.27f, .29f, .31f);
            var lightObject = new GameObject("Feet12Light");
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional;
            light.intensity = 1.1f; light.cullingMask = 1 << 31;
            light.transform.rotation = Quaternion.Euler(35f, -25f, 0f);
            var report = new StringBuilder("vai,tư_thế,pha,độ_cao_chân_trái,độ_cao_chân_phải\n");
            foreach (string role in Roles)
            {
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/NPC/" + role + ".prefab");
                Assert.IsNotNull(prefab, role);
                var model = Object.Instantiate(prefab, new Vector3(0f, 30f, 0f), Quaternion.identity);
                foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
                model.GetComponent<LODGroup>().ForceLOD(0);
                foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                { skin.updateWhenOffscreen = true; skin.renderingLayerMask = uint.MaxValue; }
                var animator = model.GetComponentInChildren<Animator>();
                Assert.IsTrue(animator.isHuman, role); animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var pose in new[] { ("stand", "Idle", .2f), ("walk", "Walk", .25f), ("sit", "SitChair", .4f), ("sitidle", "SitIdle", .4f) })
                {
                    Assert.IsTrue(animator.HasState(0, Animator.StringToHash(pose.Item2)), role + "/" + pose.Item2);
                    animator.speed = 0f; animator.Play(pose.Item2, 0, pose.Item3); animator.Update(0f);
                    var l = animator.GetBoneTransform(HumanBodyBones.LeftFoot).position;
                    var r = animator.GetBoneTransform(HumanBodyBones.RightFoot).position;
                    report.AppendLine($"{role},{pose.Item2},{pose.Item3},{l.y - 30f},{r.y - 30f}");
                    yield return Views(cam, animator, role + "_" + pose.Item1, 30f);
                }
                if (role.StartsWith("PorterVN"))
                    for (int level = 1; level <= 2; level++)
                    {
                        model.GetComponent<LODGroup>().ForceLOD(level);
                        foreach (var pose in new[] { ("stand", "Idle", .2f), ("walk", "Walk", .25f) })
                        {
                            animator.Play(pose.Item2, 0, pose.Item3); animator.Update(0f);
                            yield return Views(cam, animator, role + "_lod" + level + "_" + pose.Item1, 30f);
                        }
                    }
                Object.Destroy(model); yield return null;
            }
            File.WriteAllText(Path.Combine(ScreenshotCapture.Folder, Prefix + "pose_inventory.csv"), report.ToString());
            Object.Destroy(cameraObject); Object.Destroy(lightObject);
#else
            Assert.Fail("Bộ đối chứng cần prefab trong Unity Editor.");
            yield break;
#endif
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator SeasickRuntimeAnklesWithDiagnosticLight()
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            var people = Object.FindAnyObjectByType<VoyagePeople>(); Assert.IsNotNull(people);
            people.seasick.SetActive(true);
            yield return new WaitForSeconds(.2f);
            var rig = PlayerRig.Current; rig.SetControl(false); rig.SetNoise(0f);
            var npcs = people.seasick.GetComponentsInChildren<NpcAgent>();
            // Polish12 cho người say sóng ngồi ghế thật thay vì ngồi bệt; vẫn phải có đúng một người ngồi để soi cổ chân lúc gập gối.
            Assert.AreEqual(3, npcs.Length, "Đủ ba NPC say sóng, gồm người ngồi ghế.");
            foreach (var npc in npcs)
            {
                npc.SetAnimated(true);
                npc.GetComponentInChildren<Animator>().cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var t in npc.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
                npc.GetComponentInChildren<LODGroup>().ForceLOD(0);
                foreach (var skin in npc.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                { skin.updateWhenOffscreen = true; skin.renderingLayerMask = uint.MaxValue; }
            }
            yield return new WaitForSeconds(.3f);
            Assert.AreEqual(1, npcs.Count(n => n.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.Hips).position.y - n.transform.position.y < .7f),
                "Một người say sóng ngồi ghế (hông thấp hơn 0,7 m so với gốc).");
            var cameraObject = new GameObject("Feet12RuntimeCamera"); var cam = cameraObject.AddComponent<Camera>();
            cam.enabled = false; cam.nearClipPlane = .03f; cam.cullingMask = 1 << 31;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.27f, .29f, .31f);
            var lightObject = new GameObject("Feet12RuntimeLight"); var lamp = lightObject.AddComponent<Light>();
            lamp.type = LightType.Directional; lamp.intensity = 1.1f; lamp.cullingMask = 1 << 31;
            lamp.transform.rotation = Quaternion.Euler(35f, -25f, 0f);
            var sun = RenderSettings.sun; RenderSettings.sun = lamp;
            // Giữ SeasickPose.LateUpdate thật; chỉ tách nền và chiếu sáng để thấy mắt cá.
            foreach (var npc in npcs)
            {
                rig.motor.Teleport(npc.transform.position + npc.transform.forward * 1.5f, 180f);
                yield return new WaitForSeconds(.25f);
                var animator = npc.GetComponentInChildren<Animator>();
                yield return Views(cam, animator, "02_Galley_" + npc.name + "_runtimeLit", npc.transform.position.y);
            }
            RenderSettings.sun = sun; Object.Destroy(lightObject); Object.Destroy(cameraObject);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator SceneAnklesAndPlayerWardrobes()
        {
            PlayTestUtil.Root().ResetProgress();
            var inventory = new StringBuilder("cảnh,NPC,mesh\n");
            foreach (string scene in new[] { SceneFlow.PrologueScene }.Concat(SceneFlow.GameScenes))
            {
                yield return PlayTestUtil.LoadAndSettle(scene);
                yield return new WaitForSeconds(.5f);
                var people = Object.FindAnyObjectByType<VoyagePeople>();
                if (people != null) people.seasick.SetActive(true);
                var rig = PlayerRig.Current; Assert.IsNotNull(rig);
                rig.SetControl(false); rig.SetNoise(0f);
                var go = new GameObject("Feet12SceneCamera"); var cam = go.AddComponent<Camera>();
                cam.CopyFrom(rig.cam); cam.enabled = false; cam.nearClipPlane = .03f;
                var seen = new System.Collections.Generic.HashSet<string>();
                foreach (var npc in Object.FindObjectsByType<NpcAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var animator = npc.GetComponentInChildren<Animator>(true);
                    if (animator == null || !animator.isHuman) continue;
                    string meshes = string.Join("+", npc.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(x => x.sharedMesh.name));
                    inventory.AppendLine($"{scene},{npc.name},{meshes}");
                    bool sick = people != null && npc.transform.IsChildOf(people.seasick.transform);
                    if (!npc.gameObject.activeInHierarchy || (!sick && !seen.Add(meshes))) continue;
                    rig.motor.Teleport(npc.transform.position + npc.transform.forward * 1.5f, 180f);
                    yield return null; yield return null;
                    yield return Views(cam, animator, scene + "_" + npc.name, npc.transform.position.y);
                }
                var body = rig.GetComponentInChildren<PlayerBody>(); Assert.IsNotNull(body);
                body.enabled = false;
                body.skin.renderingLayerMask = uint.MaxValue;
                // Camera riêng chỉ thấy anh Ba: bếp, tường, lan can không che cổ chân đối chứng.
                foreach (var t in body.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
                cam.cullingMask = 1 << 31; cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(.27f, .29f, .31f);
                var lampObject = new GameObject("Feet12PlayerLight"); var lamp = lampObject.AddComponent<Light>();
                lamp.type = LightType.Directional; lamp.intensity = 1.1f; lamp.cullingMask = 1 << 31;
                lamp.transform.rotation = Quaternion.Euler(35f, -25f, 0f);
                var sun = RenderSettings.sun; RenderSettings.sun = lamp;
                foreach (var pose in new[] { ("stand", "Idle", .2f), ("walk", "Walk", .25f), ("squat", "Squat", .4f) })
                {
                    body.animator.speed = 0f; body.animator.Play(pose.Item2, 0, pose.Item3); body.animator.Update(0f);
                    yield return Views(cam, body.animator, scene + "_Ba_" + pose.Item1, rig.Position.y);
                }
                RenderSettings.sun = sun; Object.Destroy(lampObject);
                Object.Destroy(go);
            }
            File.WriteAllText(Path.Combine(ScreenshotCapture.Folder, Prefix + "scene_inventory.csv"), inventory.ToString());
        }
    }
}
