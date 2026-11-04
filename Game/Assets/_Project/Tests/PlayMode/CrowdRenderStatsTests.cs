using System.Collections;
using System.IO;
using System.Text;
using HaiBanTay.Core;
using HaiBanTay.Npc;
using HaiBanTay.Performance;
using HaiBanTay.Player;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Số đo render của đám đông 40 NPC trong sandbox, ở cả hai tầng chất lượng, kèm ảnh chụp (Logs/shots/crowd_*.png)
    /// và Logs/shots/crowd_render_stats.txt. Cần GPU nên chạy cùng Tools/screenshots.sh CrowdRenderStats.
    /// Ngân sách tầng Laptop (PLAN 3.3): SetPass ≲ 150, không giới hạn tam giác.
    /// </summary>
    [Category("Screenshots"), Explicit]
    public class CrowdRenderStatsTests
    {
        const long MaxSetPass = 150;
        const int SampleFrames = 30;
        static RenderTexture _rt;

        [TearDown]
        public void TearDown()
        {
            ScreenshotCapture.RestoreUi();
            Time.captureDeltaTime = 0f;
            GameRoot.Instance?.Quality.Apply(null);
            PlayTestUtil.ExitSim();
        }

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

        static int ActiveSkinned()
        {
            int n = 0;
            foreach (var s in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (s.enabled && s.gameObject.activeInHierarchy)
                    n++;
            return n;
        }

        [UnityTest]
        public IEnumerator CrowdOf40WithinRenderBudgetOnBothTiers()
        {
            yield return CrowdPlayTests.LoadSandbox();
            var root = GameRoot.Instance;
            var zone = GameObject.Find("CrowdZone");
            Assert.IsNotNull(zone);
            var lod = zone.GetComponentInChildren<CrowdLod>(true);
            ScreenshotCapture.RedirectUi();
            // SandboxDirector hiện thẻ và phụ đề sau 1–3 giây: chờ cho nó xong rồi dọn UI để ảnh chỉ có cảnh.
            float settle = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < settle)
                yield return null;
            root.UI.ResetTransient();
            root.Subtitles.StopAll();

            var report = new StringBuilder("tier,view,setpass_max,setpass_avg,draws_max,tris_max,npc_active,animated,static,hidden,skinned_enabled\n");
            var failures = new StringBuilder();
            using var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            using var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            using var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            long seenAny = 0;

            foreach (var tier in new[] { "Laptop", "PC" })
            {
                root.Quality.Apply(tier);
                foreach (var view in new[] { "View_Overview", "View_Close", "View_Queue", "View_Sit" })
                {
                    CrowdPlayTests.StandAt(GameObject.Find(view).transform);
                    // Cho LOD chọn lại tập NPC chạy Animator và bake tư thế trước khi đo.
                    float end = Time.realtimeSinceStartup + 3f;
                    while (Time.realtimeSinceStartup < end)
                        yield return null;
                    long spMax = 0, spSum = 0, dc = 0, tr = 0;
                    for (int i = 0; i < SampleFrames; i++)
                    {
                        RenderOnce();
                        yield return null;
                        spMax = System.Math.Max(spMax, setPass.LastValue);
                        spSum += setPass.LastValue;
                        dc = System.Math.Max(dc, draws.LastValue);
                        tr = System.Math.Max(tr, tris.LastValue);
                    }
                    seenAny = System.Math.Max(seenAny, dc);
                    int active = 0;
                    foreach (var a in zone.GetComponentsInChildren<NpcAgent>(false))
                        active++;
                    report.AppendLine($"{tier},{view},{spMax},{spSum / (float)SampleFrames:0.0},{dc},{tr},{active},{lod.AnimatedCount},{lod.StaticCount},{lod.HiddenCount},{ActiveSkinned()}");
                    if (spMax > MaxSetPass && tier == "Laptop")
                        failures.AppendLine($"{tier}/{view}: SetPass {spMax} (≤ {MaxSetPass}), tam giác {tr}");
                    root.UI.ResetTransient();
                    root.Subtitles.StopAll();
                    yield return ScreenshotCapture.Capture($"crowd_{tier.ToLowerInvariant()}_{view.Substring(5).ToLowerInvariant()}");
                }
            }

            File.WriteAllText(Path.Combine(ScreenshotCapture.Folder, "crowd_render_stats.txt"), report.ToString());
            Debug.Log($"CrowdRenderStats: bake {lod.BakedMeshes} mesh trong {lod.BakeMilliseconds:0.0} ms\n" + report);
            Assert.Greater(seenAny, 0, "bộ đếm render không có dữ liệu (cần chạy có GPU)");
            Assert.IsEmpty(failures.ToString(), failures.ToString());
        }
    }
}
