using System.Collections;
using System.IO;
using System.Text;
using HaiBanTay.Core;
using HaiBanTay.Performance;
using HaiBanTay.Player;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Ngân sách render tầng Laptop (PLAN 3.3): ≲ 150 SetPass (không giới hạn tam giác), đo ở các góc nhìn nặng nhất.
    /// Cần GPU nên chạy cùng Tools/screenshots.sh; kết quả ghi Logs/shots/render_stats.txt.
    /// </summary>
    [Category("Screenshots"), Explicit]
    public class RenderStatsTests
    {
        const long MaxSetPass = 150;
        const int SampleFrames = 30;

        struct View
        {
            public string Name;
            public Vector3 Feet;
            public Vector3 Target;
            public View(string name, Vector3 feet, Vector3 target) { Name = name; Feet = feet; Target = target; }
        }

        // M10b/M10c: cả con tàu thật. 01: gốc tàu (−14,964; −5,6; 0) hướng 0°; 02: gốc tàu (−0,2; −5,6; −0,85) hướng 180°
        // (thế giới = (−0,2 − x; y − 5,6; −0,85 − z) của hệ tàu); 03: gốc tàu (−13,464; −5,6; 4,17) hướng 0°.
        static readonly (string scene, View[] views)[] Plan =
        {
            // Prologue Sài Gòn 1911: phố từ model SaigonStreet, quán cà phê, cả chiều dài phố (tọa độ như ScreenshotTests.Shots00Prologue).
            (SceneFlow.PrologueScene, new[]
            {
                new View("street_length", new Vector3(-26f, 0.12f, -1f), new Vector3(0f, 2.5f, -9f)),
                new View("cafe", new Vector3(0f, 0.12f, -3f), new Vector3(10f, 1.6f, -9f)),
                new View("cafe_storefront", new Vector3(11f, 0.12f, -3.5f), new Vector3(11.5f, 1.5f, -9.8f)),
                new View("lamp_facade", new Vector3(-12f, 0.12f, 3f), new Vector3(-12f, 3f, -8f)),
            }),
            // 01: kè có đám đông ~50 người (tầng Laptop), model Nhà Rồng, tàu chung. Tọa độ như ScreenshotTests.Shots01NhaRong.
            (SceneFlow.GameScenes[0], new[]
            {
                new View("quay_crowd_overview", new Vector3(32f, -4.1f, -17f), new Vector3(-12f, -1.5f, -20f)),
                new View("nharong_facade", new Vector3(6f, -4.1f, -30f), new Vector3(6f, 3.5f, -55f)),
                new View("gangway_queue", new Vector3(2.5f, -4.1f, -22f), new Vector3(-2.5f, -2.5f, -13f)),
                new View("quay_to_ship", new Vector3(-1.5f, -4.1f, -16f), new Vector3(-12f, 3f, 0f)),
                new View("ship_from_quay", new Vector3(4f, -4.1f, -16.5f), new Vector3(-10f, 2f, -4f)),
                new View("deck_overview", new Vector3(9f, 0f, 3f), new Vector3(-5f, 1f, -3f)),
                new View("deck_to_quay", new Vector3(1f, 0f, -3.5f), new Vector3(6.5f, 1.2f, -20f)),
                new View("river", new Vector3(-5f, 0f, 3.5f), new Vector3(-5f, 0f, 60f)),
            }),
            (SceneFlow.GameScenes[1], new[]
            {
                new View("galley_overview", new Vector3(2.9f, 0f, -2.2f), new Vector3(-2f, 1f, 1.5f)),
                new View("stove", new Vector3(-3f, 0f, 0.9f), new Vector3(-4f, 0.5f, -2f)),
                new View("food_hold", new Vector3(-12.5f, -2.8f, -1.85f), new Vector3(-14.7f, -2f, 0.65f)),
                new View("ship_from_boat_deck", new Vector3(34.8f, 5.75f, -5.85f), new Vector3(-30.2f, 0.4f, -0.85f)),
                new View("fore_well_to_galley", new Vector3(-14.2f, 0f, 3.5f), new Vector3(-5f, 1.5f, -2f)),
            }),
            (SceneFlow.GameScenes[2], new[]
            {
                new View("pay_queue", new Vector3(-1f, 0f, -3.1f), new Vector3(-4.2f, 1f, -1.1f)),
                new View("cafe", new Vector3(0.5f, -2.4f, -22.2f), new Vector3(4f, -1.3f, -24f)),
                new View("deck_to_city", new Vector3(3f, 0f, -2f), new Vector3(2f, 2f, -40f)),
                new View("quay", new Vector3(2f, -2.5f, -12f), new Vector3(2f, -1f, -40f)),
                new View("hill", new Vector3(2f, -2.5f, -16f), new Vector3(-2f, 40f, -128f)),
                new View("back_to_sea", new Vector3(2f, -2.5f, -20f), new Vector3(2f, -2f, 40f)),
                new View("quay_to_ship", new Vector3(6f, -2.5f, -14f), new Vector3(-12f, 4f, 2f)),
            }),
        };

        bool _toggled;

        [TearDown]
        public void TearDown()
        {
            if (_toggled)
                GameRoot.Instance?.Quality.Toggle();
            PlayTestUtil.ExitSim();
        }

        static void LookFrom(Vector3 feet, Vector3 target)
        {
            var rig = PlayerRig.Current;
            var head = feet + Vector3.up * 1.62f;
            var to = target - head;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            rig.motor.Teleport(feet, yaw);
            rig.look.SetAngles(yaw, pitch);
        }

        static RenderTexture _rt;

        // Batchmode không có Game view: tự render camera chính ở 1080p (render scale của tầng vẫn áp dụng).
        static void RenderOnce()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            if (_rt == null)
                _rt = new RenderTexture(1920, 1080, 24);
            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = _rt };
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, req))
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
        }

        [UnityTest]
        public IEnumerator LaptopTierWithinRenderBudget()
        {
            var root = PlayTestUtil.Root();
            // Editor Linux mặc định tầng PC; đo ngân sách ở tầng Laptop.
            if (root.Quality.Current != QualityTier.Laptop)
            {
                root.Quality.Toggle();
                _toggled = true;
            }
            var report = new StringBuilder("scene,view,setpass_max,draws_max,tris_max\n");
            var failures = new StringBuilder();

            using var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            using var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            using var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            long seenAny = 0;

            foreach (var (scene, views) in Plan)
            {
                SceneManager.LoadScene(scene);
                float end = Time.realtimeSinceStartup + 3f;
                while (Time.realtimeSinceStartup < end)
                    yield return null;
                root.UI.Card.Hide();
                foreach (var v in views)
                {
                    LookFrom(v.Feet, v.Target);
                    for (int i = 0; i < 5; i++)
                        yield return null;
                    long sp = 0, dc = 0, tr = 0;
                    for (int i = 0; i < SampleFrames; i++)
                    {
                        RenderOnce();
                        yield return null;
                        sp = System.Math.Max(sp, setPass.LastValue);
                        dc = System.Math.Max(dc, draws.LastValue);
                        tr = System.Math.Max(tr, tris.LastValue);
                    }
                    seenAny = System.Math.Max(seenAny, dc);
                    report.AppendLine($"{scene},{v.Name},{sp},{dc},{tr}");
                    if (sp > MaxSetPass)
                        failures.AppendLine($"{scene}/{v.Name}: SetPass {sp} (≤ {MaxSetPass}), tam giác {tr}");
                }
            }

            File.WriteAllText(Path.Combine(ScreenshotCapture.Folder, "render_stats.txt"), report.ToString());
            Debug.Log("RenderStats:\n" + report);
            Assert.Greater(seenAny, 0, "bộ đếm render không có dữ liệu (cần chạy có GPU)");
            Assert.IsEmpty(failures.ToString(), failures.ToString());
        }
    }
}
