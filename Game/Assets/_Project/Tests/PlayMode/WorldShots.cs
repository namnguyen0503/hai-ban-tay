#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Text;
using HaiBanTay.Core;
using HaiBanTay.Narrative;
using HaiBanTay.Performance;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Ship;
using NUnit.Framework;
using Unity.Profiling;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>Ảnh đối chứng cùng camera, cùng giờ, cùng tầng Laptop; đo cả cảng Singapore Ngày 2.</summary>
    [Category("Screenshots"), Explicit]
    public class WorldShots
    {
        string Phase => System.Environment.GetEnvironmentVariable("WORLD_SHOTS_PHASE") == "before" ? "before" : "after";
        readonly StringBuilder _stats = new StringBuilder("scene,view,setpass_max,draws_max,tris_max\n");
        RenderTexture _target;

        [TearDown]
        public void TearDown()
        {
            if (_target != null) { _target.Release(); Object.Destroy(_target); }
            ScreenshotCapture.RestoreUi();
            Time.captureDeltaTime = 0f;
            GameRoot.Instance?.Quality.Apply(null);
            PlayTestUtil.ExitSim();
        }

        IEnumerator Load(string scene, int day = 0)
        {
            var root = PlayTestUtil.Root();
            root.ResetProgress();
            root.Quality.Apply("Laptop");
            if (day > 0) ChapterStart.PrepareDay(root, day, day, false);
            if (scene == "90_Sandbox")
                EditorSceneManager.LoadSceneInPlayMode("Assets/_Project/Scenes/90_Sandbox.unity", new LoadSceneParameters(LoadSceneMode.Single));
            else SceneManager.LoadScene(scene);
            for (int i = 0; i < 120; i++) yield return null;
            Assert.IsNotNull(PlayerRig.Current);
            SceneDirector.Current?.StopAllCoroutines();
            root.UI.ResetTransient();
            root.Subtitles.StopAll();
            root.UI.SetFade(0f);
            PlayerRig.Current.SetControl(false);
            PlayerRig.Current.SetNoise(0f);
            var clock = Object.FindAnyObjectByType<TimeOfDay>();
            Assert.IsNotNull(clock);
            clock.followShiftClock = false;
            clock.hoursPerSecond = 0f;
            var motion = clock.GetComponent<WorldMotion>();
            motion.enabled = false;
            motion.transform.localPosition = Vector3.zero;
            motion.transform.localRotation = Quaternion.identity;
            if (day > 0)
            {
                var shift = Object.FindAnyObjectByType<ShiftDirector>();
                Assert.AreEqual(day, shift.Day.Number);
                Assert.IsTrue(Object.FindAnyObjectByType<DayScenery>().Docked);
            }
        }

        void Hour(float hour)
        {
            var clock = Object.FindAnyObjectByType<TimeOfDay>();
            clock.hour = hour;
            clock.Apply();
        }

        void Look(Vector3 feet, Vector3 target)
        {
            var rig = PlayerRig.Current;
            var to = target - feet - Vector3.up * 1.62f;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            rig.motor.Teleport(feet, yaw);
            rig.look.SetAngles(yaw, pitch);
        }

        IEnumerator Shot(string name, Vector3 feet, Vector3 target, bool measure = false)
        {
            Look(feet, target);
            for (int i = 0; i < 15; i++) yield return null;
            if (measure)
            {
                using var sp = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
                using var dc = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                using var tr = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
                _target ??= new RenderTexture(1920, 1080, 24);
                long s = 0, d = 0, t = 0;
                for (int i = 0; i < 30; i++)
                {
                    var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = _target };
                    Assert.IsTrue(UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(Camera.main, request));
                    UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(Camera.main, request);
                    yield return null;
                    s = System.Math.Max(s, sp.LastValue);
                    d = System.Math.Max(d, dc.LastValue);
                    t = System.Math.Max(t, tr.LastValue);
                }
                _stats.AppendLine($"{SceneManager.GetActiveScene().name},{name},{s},{d},{t}");
                Assert.Greater(d, 0, "Phải có dữ liệu GPU thật");
                Assert.LessOrEqual(s, 150, "Giữ nguyên ngân sách SetPass tầng Laptop");
            }
            yield return ScreenshotCapture.CaptureCamera(PlayerRig.Current.cam, "w10_" + Phase + "_" + name);
        }

        [UnityTest]
        public IEnumerator OutdoorViewsAndRenderBudget()
        {
            Time.captureDeltaTime = 1f / 60f;
            yield return Load("90_Sandbox");
            ShipMotion.Current.Intensity = 0.15f;
            foreach (float h in new[] { 4f, 6f, 12f, 19f, 21f })
            {
                Hour(h);
                yield return Shot("B_world_" + h.ToString("00") + "00", new Vector3(-61.8f, .38f, -7f), new Vector3(-95f, 0f, -45f));
            }
            Hour(12f);
            ShipMotion.Current.Intensity = 1f;
            yield return Shot("B_storm_noon", new Vector3(-61.8f, .38f, -7f), new Vector3(-95f, 0f, -45f));
            ShipMotion.Current.Intensity = 0.15f;
            Hour(6f);
            var sun = Object.FindAnyObjectByType<TimeOfDay>().State.SunDirection;
            yield return Shot("B_sun_path_0600", new Vector3(-61.8f, .38f, -7f), new Vector3(-61.8f, 1f, -7f) + new Vector3(sun.x, 0f, sun.z) * 100f);
            yield return Load(SceneFlow.PrologueScene);
            yield return Shot("00b_street_from_spawn", PlayerRig.Current.Position, new Vector3(20f, 2f, -6f));
            Hour(12f);
            yield return Shot("00l_street_noon", new Vector3(-26f, .12f, -1f), new Vector3(0f, 2.5f, -9f));
            yield return Load(SceneFlow.GameScenes[0]);
            Hour(12f);
            yield return Shot("01f_quay_crowd_overview", new Vector3(32f, -4.1f, -17f), new Vector3(-12f, -1.5f, -20f), true);
            yield return Shot("01g_nharong_facade", new Vector3(6f, -4.1f, -30f), new Vector3(6f, 3.5f, -55f));
            yield return Shot("01p_quay_from_deck", new Vector3(1f, 0f, -3.5f), new Vector3(6f, 3f, -56f));
            yield return Shot("01j_ship_from_quay_original", new Vector3(4f, -4.1f, -16.5f), new Vector3(-10f, 2f, -4f));
            yield return Shot("01j_ship_from_quay", new Vector3(12f, -4.1f, -24f), new Vector3(-10f, 2f, -4f));
            yield return Shot("B_river_noon", new Vector3(-5f, 0f, 3.5f), new Vector3(-5f, 0f, 60f));
            var trees = System.Array.FindAll(Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None), l => l.name.StartsWith("World10_"));
            foreach (var tree in trees) tree.ForceLOD(2);
            yield return Shot("01q_tree_billboards", new Vector3(32f, -4.1f, -17f), new Vector3(-12f, -1.5f, -20f));
            foreach (var tree in trees) tree.ForceLOD(-1);
            yield return Load(SceneFlow.GameScenes[1], 2);
            Hour(8f);
            var barrier = GameObject.Find("GangwayBarrier").transform.position;
            yield return Shot("02o1_singapore_from_deck", new Vector3(barrier.x + 2.4f, 0f, barrier.z - 1.4f), new Vector3(-10f, 4f, 58f), true);
            yield return Shot("02o2_rope_barrier", new Vector3(barrier.x - .6f, 0f, barrier.z - 1.6f), barrier + Vector3.up * .6f);
            Hour(12f);
            yield return Shot("02o1_singapore_noon", new Vector3(barrier.x + 2.4f, 0f, barrier.z - 1.4f), new Vector3(-10f, 4f, 58f));
            yield return Shot("02o3_keppel_steamer", new Vector3(barrier.x + 2.4f, 0f, barrier.z - 1.4f), new Vector3(45f, 2f, 45f));
            yield return Load(SceneFlow.GameScenes[2]);
            Hour(5.5f);
            yield return Shot("03a_dawn_fog", new Vector3(3f, 0f, -2f), new Vector3(2f, 2f, -40f));
            Hour(6f);
            yield return Shot("03b_basilica_port", new Vector3(3f, 0f, -2f), new Vector3(-2f, 43f, -150f));
            yield return Shot("03e_quay", new Vector3(2f, -2.5f, -12f), new Vector3(2f, -1f, -40f), true);
            yield return Shot("B_harbor_dawn", new Vector3(2f, -2.5f, -20f), new Vector3(2f, -2f, 40f));
            yield return Shot("B_harbor_water_0600", new Vector3(55f, -2.5f, -9.7f), new Vector3(100f, -5.6f, 25f));
            File.WriteAllText(Path.Combine(ScreenshotCapture.Folder, "world10_render_" + Phase + ".csv"), _stats.ToString());
            Debug.Log("World10 render " + Phase + "\n" + _stats);
        }
    }
}
#endif
