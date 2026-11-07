using System.Collections;
using System.IO;
using System.Linq;
using HaiBanTay.Core;
using HaiBanTay.Narrative;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Ship;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>Đối chứng cùng camera trước/sau; đèn trung tính chỉ dùng cho ảnh kiểm tạo hình.</summary>
    [Category("Screenshots"), Explicit]
    public sealed class Faces13Shots
    {
        static string Prefix => "faces13_" + (System.Environment.GetEnvironmentVariable("FACES13_PHASE") ?? "after") + "_";
        static bool Before => System.Environment.GetEnvironmentVariable("FACES13_PHASE") == "before";

        [TearDown] public void Done() => PlayTestUtil.ExitSim();

        static IEnumerator Save(Camera camera, string name) => ScreenshotCapture.CaptureCamera(camera, Prefix + name);

        static Camera CameraFor(string name)
        {
            var camera = new GameObject(name).AddComponent<Camera>();
            camera.enabled = false; camera.nearClipPlane = .03f; camera.fieldOfView = 34f;
            return camera;
        }

        static IEnumerator InScene(Camera camera, Transform person, string name)
        {
            camera.transform.position = person.position + person.forward * 2.5f + person.right * .65f + Vector3.up * 1.35f;
            camera.transform.LookAt(person.position + Vector3.up * 1.15f);
            yield return Save(camera, name + "_scene");
        }

        static IEnumerator Inspect(Camera camera, GameObject person, Animator animator, string name)
        {
            Assert.IsTrue(animator.isHuman && animator.avatar.isValid, name);
            foreach (var behaviour in person.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            foreach (var collider in person.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            person.transform.SetPositionAndRotation(new Vector3(0f, 30f, 0f), Quaternion.identity);
            foreach (var t in person.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
            var lod = person.GetComponentInChildren<LODGroup>();
            if (lod != null) { lod.enabled = true; lod.ForceLOD(0); }
            var skins = person.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var skin in skins)
            {
                skin.updateWhenOffscreen = true; skin.renderingLayerMask = uint.MaxValue;
                skin.lightProbeUsage = LightProbeUsage.Off;
            }
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            var enabled = lights.Select(x => x.enabled).ToArray();
            foreach (var light in lights) light.enabled = false;
            var timeOfDay = Object.FindObjectsByType<TimeOfDay>(FindObjectsSortMode.None);
            var timeEnabled = timeOfDay.Select(x => x.enabled).ToArray();
            foreach (var clock in timeOfDay) clock.enabled = false;
            var lamp = new GameObject("Faces13NeutralLight").AddComponent<Light>();
            lamp.type = LightType.Directional; lamp.color = Color.white; lamp.intensity = 1.5f;
            lamp.GetUniversalAdditionalLightData().renderingLayers = uint.MaxValue;
            lamp.cullingMask = 1 << 31; lamp.transform.rotation = Quaternion.LookRotation(new Vector3(-.4f, -.5f, -1f));
            var oldSun = RenderSettings.sun; RenderSettings.sun = lamp;
            var oldProbe = RenderSettings.ambientProbe;
            var probe = new SphericalHarmonicsL2(); probe.AddAmbientLight(new Color(.22f, .22f, .22f));
            RenderSettings.ambientProbe = probe;
            bool oldFog = RenderSettings.fog; RenderSettings.fog = false;
            camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.19f, .21f, .23f);
            animator.enabled = true; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var baked = new Mesh();
#if UNITY_EDITOR
            animator.runtimeAnimatorController = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/_Project/Art/Characters/NpcController.controller");
            animator.Rebind(); animator.speed = 0f;
#endif
            try
            {
                foreach (string clipName in new[] { "Idle", "SitIdle", "Talk" })
                {
#if UNITY_EDITOR
                    Assert.IsTrue(animator.HasState(0, Animator.StringToHash(clipName)), name + "/" + clipName);
                    animator.Play(clipName, 0, .4f); animator.Update(0f);
                    if (clipName == "SitIdle")
                    {
                        var thigh = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position;
                        var knee = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position;
                        var ankle = animator.GetBoneTransform(HumanBodyBones.LeftFoot).position;
                        Assert.Less(Vector3.Dot((knee - thigh).normalized, Vector3.down), .65f, "Ngồi: đùi phải chuyển ngang.");
                        Assert.Greater(Vector3.Angle(knee - thigh, ankle - knee), 50f, "Ngồi: gối phải gập.");
                    }
                    foreach (var skin in skins)
                    {
                        skin.BakeMesh(baked);
                        Assert.IsTrue(baked.vertices.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)), name + "/" + clipName);
                        Assert.Less(baked.bounds.size.magnitude, 4.1f, name + "/" + clipName + ": skin bị vỡ");
                    }
                    var head = animator.GetBoneTransform(HumanBodyBones.Head).position + Vector3.up * .085f;
                    foreach (var view in new[] { ("front", new Vector3(0f, .01f, 1f)), ("three_quarter", new Vector3(.70f, .01f, .72f)), ("profile", new Vector3(1f, .01f, 0f)) })
                    {
                        camera.fieldOfView = 30f;
                        camera.transform.position = head + view.Item2; camera.transform.LookAt(head);
                        yield return Save(camera, name + "_" + clipName.ToLowerInvariant() + "_" + view.Item1);
                    }
                    camera.fieldOfView = 34f;
                    camera.transform.position = person.transform.position + new Vector3(.65f, 1f, 3.2f);
                    camera.transform.LookAt(person.transform.position + Vector3.up * .9f);
                    yield return Save(camera, name + "_" + clipName.ToLowerInvariant() + "_body");
#else
                    Assert.Fail("Đối chứng cần clip trong Unity Editor.");
#endif
                }
            }
            finally
            {
                Object.Destroy(baked); RenderSettings.sun = oldSun;
                RenderSettings.ambientProbe = oldProbe; RenderSettings.fog = oldFog;
                for (int i = 0; i < lights.Length; i++) if (lights[i] != null) lights[i].enabled = enabled[i];
                for (int i = 0; i < timeOfDay.Length; i++) if (timeOfDay[i] != null) timeOfDay[i].enabled = timeEnabled[i];
                Object.Destroy(lamp.gameObject);
                person.SetActive(false);
            }
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator BaAndCaptainFrontProfilePosesAndScene()
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[0]);
            var rig = PlayerRig.Current; rig.SetControl(false); rig.SetNoise(0f);
            var camera = CameraFor("Faces13OutsideCamera");
            rig.SetOutfit(TrangPhuc.Thuong);
            yield return InScene(camera, rig.transform, "ba");
            yield return Inspect(camera, rig.body.gameObject, rig.body.animator, "ba");
            Object.Destroy(camera.gameObject);
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[0]);
            var director = Object.FindAnyObjectByType<NhaRongDirector>();
            var captain = director.jobCaptain; captain.gameObject.SetActive(true); captain.SetAnimated(true);
            camera = CameraFor("Faces13CaptainCamera");
            yield return InScene(camera, captain.transform, "captain");
            yield return Inspect(camera, captain.gameObject, captain.GetComponentInChildren<Animator>(), "captain");
            Object.Destroy(camera.gameObject);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator FirstClassFamilyFrontProfilePosesAndScene()
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            var people = Object.FindAnyObjectByType<VoyagePeople>(); people.firstClass.SetActive(true);
            var family = people.firstClass.GetComponentsInChildren<NpcAgent>(); Assert.AreEqual(3, family.Length);
            var father = family.Single(x => x.name.EndsWith("4140"));
            var child = family.Single(x => x.name.EndsWith("4142"));
            if (!Before)
            {
                StringAssert.Contains("Chieu", father.name); StringAssert.Contains("ChieuChild", child.name);
                Assert.AreEqual(Vector3.one, child.transform.localScale, "Trẻ có rig riêng, không thu nhỏ NPC người lớn.");
            }
            var camera = CameraFor("Faces13FamilyCamera");
            camera.transform.position = new Vector3(-4.65f, 4.35f, 2.2f);
            camera.transform.LookAt(new Vector3(-2.85f, 3.85f, 4.8f)); camera.fieldOfView = 44f;
            yield return Save(camera, "family_scene");
            yield return Inspect(camera, father.gameObject, father.GetComponentInChildren<Animator>(), "chieu");
            yield return Inspect(camera, child.gameObject, child.GetComponentInChildren<Animator>(), "child");
            var woman = family.Single(x => x.name.EndsWith("4141"));
            yield return Inspect(camera, woman.gameObject, woman.GetComponentInChildren<Animator>(), "woman");
            Object.Destroy(camera.gameObject);
        }
    }
}
