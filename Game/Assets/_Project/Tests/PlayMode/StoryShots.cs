using System;
using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Ending;
using HaiBanTay.Narrative;
using HaiBanTay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>Ảnh nhịp truyện S16. Dàn camera riêng; bot Playthrough kiểm thao tác và đường đi thật.</summary>
    [Category("Screenshots"), Explicit]
    public class StoryShots
    {
        static GameRoot Root => GameRoot.Instance;
        static PlayerRig Rig => PlayerRig.Current;
        static IEnumerator Until(Func<bool> done)
        {
            yield return PlayTestUtil.WaitUntil(done, 240f);
            Assert.IsTrue(done(), "Không tới nhịp truyện cần chụp: " + SceneDirector.Current?.Beat);
        }
        static IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }
        static void Look(Vector3 feet, Vector3 point)
        {
            Rig.motor.Teleport(feet, 0f);
            // Camera Cinemachine cập nhật cuối frame; đầu đã theo điểm teleport ngay lúc này.
            var d = point - Rig.head.position;
            Rig.look.AssistTarget = null;
            Rig.look.SetAngles(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg,
                -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
        }
        static IEnumerator Load(string scene)
        {
            PlayTestUtil.Root().ResetProgress();
            PlayTestUtil.EnterSim(1f);
            Time.captureDeltaTime = 1f / 60f;
            yield return PlayTestUtil.LoadAndSettle(scene);
            ScreenshotCapture.RedirectUi();
        }
        [TearDown]
        public void Done()
        {
            Time.captureDeltaTime = 0f;
            ScreenshotCapture.RestoreUi();
            PlayTestUtil.ExitSim();
            SceneFlow.FromPrologue = false;
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator StoryPrologue()
        {
            yield return Load(SceneFlow.PrologueScene);
            var d = UnityEngine.Object.FindAnyObjectByType<PrologueDirector>();
            yield return Until(() => d.discoveries[1].available);
            var projection = UnityEngine.Object.FindAnyObjectByType<ProjectionFlicker>();
            Assert.IsTrue(projection.screen.sharedMaterial.IsKeywordEnabled("_EMISSION"), "Màn chiếu phải bật emission khi nạp lại cảnh");
            var block = new MaterialPropertyBlock();
            projection.screen.GetPropertyBlock(block);
            var firstGlow = block.GetColor("_EmissionColor");
            Assert.AreEqual(12, projection.limbs.Length, "Ba bóng người phải có đủ chân tay");
            var firstPose = projection.limbs[0].localRotation;
            yield return Wait(.08f);
            projection.screen.GetPropertyBlock(block);
            Assert.AreNotEqual(firstGlow, block.GetColor("_EmissionColor"), "Ánh máy chiếu phải nhấp nháy lúc chạy");
            Assert.AreNotEqual(firstPose, projection.limbs[0].localRotation, "Bóng phim phải cử động lúc chạy");
            Sim.Enabled = false;
            Look(new Vector3(-9f, .12f, -7.2f), new Vector3(-9f, 1.9f, -9.3f));
            yield return Wait(.3f);
            yield return ScreenshotCapture.Capture("story_cinema");
            Sim.Enabled = true;
            yield return Until(() => d.discoveries[2].available);
            Sim.Enabled = false;
            Look(new Vector3(-6.6f, .12f, -7.2f), new Vector3(-5.8f, .9f, -8.8f));
            yield return Wait(.3f);
            yield return ScreenshotCapture.Capture("story_water_pump");
            Sim.Enabled = true;
            yield return Until(() => d.iceCup.gameObject.activeInHierarchy);
            Sim.Enabled = false;
            Look(new Vector3(6f, .12f, -7.4f), d.le.Position + Vector3.up * 1.25f);
            yield return Wait(.3f);
            var leftSkin = Array.Find(Rig.hands.leftPoseMeshes[(int)HandPose.Grip].GetComponentsInChildren<MeshRenderer>(), r => r.name.StartsWith("Skin"));
            var rightSkin = Array.Find(Rig.hands.rightPoseMeshes[(int)HandPose.Grip].GetComponentsInChildren<MeshRenderer>(), r => r.name.StartsWith("Skin"));
            Assert.IsTrue(leftSkin.bounds.Intersects(d.iceCup.Find("PaperCup").GetComponent<MeshRenderer>().bounds), "Cốc phải chạm bàn tay thật");
            Assert.IsTrue(rightSkin.bounds.Intersects(Rig.hands.right.Find("Spoon/SpoonBody").GetComponent<MeshRenderer>().bounds), "Cán thìa phải chạm bàn tay thật");
            yield return ScreenshotCapture.Capture("story_ice_in_hand");
            Sim.Enabled = true;
            yield return Until(() => Root.UI.Card.Visible && Root.UI.Card.CurrentText == Root.Content.GetString("card.prologue.later"));
            yield return Wait(1f);
            yield return ScreenshotCapture.Capture("story_later_card");
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator StoryJobApplication()
        {
            SceneFlow.FromPrologue = true;
            yield return Load(SceneFlow.GameScenes[0]);
            var d = UnityEngine.Object.FindAnyObjectByType<NhaRongDirector>();
            yield return Until(() => d.Beat == "B0b" && d.jobStaff[0].GetComponent<StaffAside>().Laughing && Root.Subtitles.CurrentLineId == "job_staff_whisper");
            Look(new Vector3(-2.9f, 0f, -3.8f), new Vector3(-3.4f, 1.3f, -5.2f));
            yield return Wait(.4f);
            yield return ScreenshotCapture.Capture("story_staff_laugh");
            yield return Until(() => d.Beat == "B0c");
            Sim.Enabled = false;
            yield return Wait(.6f);
            Look(new Vector3(-.3f, 0f, -5.8f), d.mai.Position + Vector3.up * 1.1f);
            yield return Wait(.1f);
            yield return ScreenshotCapture.Capture("story_mai_leads");
            Sim.Enabled = true;
            yield return Until(() => d.Beat == "B0d" && Root.Subtitles.CurrentLineId == "job_captain_work");
            Look(d.jobCaptain.Position + Vector3.forward * 1.4f, d.jobCaptain.Position + Vector3.up * 1.3f);
            yield return Wait(.6f);
            yield return ScreenshotCapture.Capture("story_captain_asks");
            Assert.AreEqual(TrangPhuc.Thuong, Rig.body.Outfit);
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator StoryMarseilleAndMaiCards()
        {
            yield return Load(SceneFlow.GameScenes[2]);
            var d = UnityEngine.Object.FindAnyObjectByType<EndingDirector>();
            yield return Until(() => d.Beat == "E2context" && d.visitingWomen[0].gameObject.activeInHierarchy);
            yield return Until(() => d.visitingWomen[0].Position.z > -6f);
            Look(new Vector3(-1.4f, 0f, -3f), d.visitingWomen[0].Position + Vector3.up);
            yield return Wait(.1f);
            yield return ScreenshotCapture.Capture("story_women_gangway");
            yield return Until(() => d.Beat == "E3" && Rig.body.Outfit == TrangPhuc.Thuong);
            Sim.Enabled = false;
            var outfitPosition = Rig.Position;
            float outfitYaw = Rig.look.Yaw;
            Rig.motor.Teleport(new Vector3(0f, -2.5f, -13.8f), 0f);
            Rig.look.SetAngles(Rig.look.Yaw, 80f);
            yield return Wait(.3f);
            yield return ScreenshotCapture.CaptureCamera(Rig.cam, "story_borrowed_clothes_fp");
            Rig.look.SetAngles(Rig.look.Yaw, 0f);
            yield return Wait(.3f);
            var outside = new GameObject("StoryOutfitCamera").AddComponent<Camera>();
            outside.CopyFrom(Rig.cam); outside.enabled = false;
            outside.transform.position = Rig.Position + Vector3.up * 1.2f + Rig.transform.forward * 2.5f;
            outside.transform.LookAt(Rig.Position + Vector3.up * .95f);
            yield return ScreenshotCapture.CaptureCamera(outside, "story_borrowed_clothes_body");
            UnityEngine.Object.Destroy(outside.gameObject);
            Rig.motor.Teleport(outfitPosition, outfitYaw);
            Sim.Enabled = true;
            yield return Until(() => d.MaiSeated && d.Beat == "E5b");
            yield return Wait(1.3f);
            Look(new Vector3(.5f, -2.4f, -23.15f), d.crew[3].Position + Vector3.up * .9f);
            yield return Wait(.4f);
            yield return ScreenshotCapture.Capture("story_mai_at_cafe");
            for (int i = 0; i < 7; i++)
            {
                int index = i;
                yield return Until(() => Root.UI.Card.Visible && Root.UI.Card.CurrentText == Root.Content.GetString("end.mai." + index) + "\n\n" + Root.Content.GetString("end.mai.source"));
                StringAssert.Contains(Root.Content.GetString("end.mai.source"), Root.UI.Card.CurrentText);
                yield return Wait(1f);
                yield return ScreenshotCapture.Capture("story_mai_end_" + i);
            }
        }
    }
}
