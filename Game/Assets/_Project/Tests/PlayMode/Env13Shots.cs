#if UNITY_EDITOR
using System.Collections;
using System.IO;
using HaiBanTay.Core;
using HaiBanTay.Narrative;
using HaiBanTay.Player;
using HaiBanTay.Ship;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>Đối chứng env13: cùng camera, giờ và tầng Laptop; không thêm đèn chụp ảnh.</summary>
    [Category("Screenshots"), Explicit]
    public sealed class Env13Shots
    {
        string Prefix => "env13_" + (System.Environment.GetEnvironmentVariable("ENV13_SHOTS_PHASE") == "before" ? "before_" : "after_");

        [TearDown]
        public void Done()
        {
            ScreenshotCapture.RestoreUi();
            Time.captureDeltaTime = 0f;
            GameRoot.Instance?.Quality.Apply(null);
            PlayTestUtil.ExitSim();
        }

        IEnumerator Load(string scene, float hour)
        {
            var root = PlayTestUtil.Root();
            root.ResetProgress(); root.Quality.Apply("Laptop");
            Time.captureDeltaTime = 1f / 60f;
            if (scene == "90_Sandbox")
                EditorSceneManager.LoadSceneInPlayMode("Assets/_Project/Scenes/90_Sandbox.unity", new LoadSceneParameters(LoadSceneMode.Single));
            else SceneManager.LoadScene(scene);
            for (int i = 0; i < 120; i++) yield return null;
            Assert.IsNotNull(PlayerRig.Current);
            SceneDirector.Current?.StopAllCoroutines();
            root.UI.ResetTransient(); root.Subtitles.StopAll(); root.UI.SetFade(0f);
            PlayerRig.Current.SetControl(false); PlayerRig.Current.SetNoise(0f);
            var clock = Object.FindAnyObjectByType<TimeOfDay>();
            Assert.IsNotNull(clock);
            clock.followShiftClock = false; clock.hoursPerSecond = 0f; clock.hour = hour;
            var motion = clock.GetComponent<WorldMotion>();
            motion.enabled = false; motion.transform.localPosition = Vector3.zero; motion.transform.localRotation = Quaternion.identity;
            clock.Apply();
        }

        IEnumerator Shot(string name, Vector3 feet, Vector3 target)
        {
            ChoreTestUtil.Look(feet, target);
            for (int i = 0; i < 15; i++) yield return null;
            yield return ScreenshotCapture.CaptureCamera(PlayerRig.Current.cam, Prefix + name);
        }

        [UnityTest]
        public IEnumerator SkyHarborAndCar()
        {
            yield return Load("90_Sandbox", 12f);
            ShipMotion.Current.Intensity = .15f;
            yield return Shot("sea_noon", new Vector3(-61.8f,.38f,-7f), new Vector3(-95f,0f,-45f));
            ShipMotion.Current.Intensity = 1f;
            yield return Shot("storm_noon", new Vector3(-61.8f,.38f,-7f), new Vector3(-95f,0f,-45f));
            yield return Load(SceneFlow.GameScenes[0], 12f);
            yield return Shot("river_ship", new Vector3(-5f,0f,3.5f), new Vector3(139f,-1f,145f));
            yield return Load(SceneFlow.GameScenes[2], 6f);
            yield return Shot("harbor_water_0600", new Vector3(55f,-2.5f,-9.7f), new Vector3(100f,-5.6f,25f));
            yield return Shot("harbor_dawn", new Vector3(2f,-2.5f,-20f), new Vector3(2f,-2f,40f));
            yield return Shot("car_dawn", new Vector3(-1.4f,-2.5f,-18.2f), new Vector3(-4.2f,-1.6f,-20.5f));
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator HeldPropsAndWall()
        {
            // Dùng lại thao tác thật của test gốc, giữ nguyên assertion và góc nhìn.
            yield return new VoyageStoryShots().WaterMeatFishIceAndFrenchCooks();
            foreach (string name in new[] { "water_held", "fish_held", "ice_source", "ice_held" })
                File.Copy(Path.Combine(ScreenshotCapture.Folder, "02story_" + name + ".png"),
                    Path.Combine(ScreenshotCapture.Folder, Prefix + name + ".png"), true);
            yield return Shot("wall_seams", new Vector3(-2f,0f,-1.4f), new Vector3(4.2f,1.5f,3f));
        }

        [UnityTest]
        public IEnumerator HarborGeometryDiagnosis()
        {
            yield return Load(SceneFlow.GameScenes[2], 6f);
            foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (renderer.name.StartsWith("DistantSteamer_")) renderer.enabled = false;
            yield return Shot("harbor_without_old_ships", new Vector3(55f,-2.5f,-9.7f), new Vector3(100f,-5.6f,25f));
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator MarseilleStory()
        {
            yield return new StoryShots().StoryMarseilleAndMaiCards();
            File.Copy(Path.Combine(ScreenshotCapture.Folder,"story_borrowed_clothes_body.png"),
                Path.Combine(ScreenshotCapture.Folder,Prefix+"marseille_story.png"),true);
        }
    }
}
#endif
