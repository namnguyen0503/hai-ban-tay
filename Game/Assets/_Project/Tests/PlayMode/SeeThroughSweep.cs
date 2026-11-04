using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HaiBanTay.Core;
using HaiBanTay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Quét nhìn xuyên toàn cảnh bằng SeeThroughProbe (ghi chú 15/10: bát ở bàn gọt chỉ còn vành và đáy, bao khoai, sọt nhìn xuyên). Điểm
    /// đứng: rải đều trên NavMesh và lưới sàn quanh chỗ người chơi xuất hiện (đứng được: có sàn, đủ chỗ cho thân), mắt cao 1,62 m; mỗi
    /// điểm nhìn 8 hướng × 4 góc (ngẩng 30°, gần ngang, cúi 50°, cúi 80°), vật trong 8 m. Ghi Logs/qa/see_through.md (mỗi vật: số
    /// điểm nhìn thấy xuyên, pixel lớn nhất, điểm nhìn tệ nhất) và ảnh thật + ảnh dò (đỏ = nhìn xuyên) cho 30 vật tệ nhất mỗi cảnh ở
    /// Logs/shots/see_through/.
    /// </summary>
    [Category("Screenshots"), Explicit]
    public sealed class SeeThroughSweep
    {
        static readonly float[] Pitches = { -30f, 10f, 50f, 80f };
        const float Range = 8f, Eye = 1.62f;
        const int MinPixels = 40, MaxPoints = 700, Worst = 30;

        [TearDown] public void TearDown() { Time.captureDeltaTime = 0f; ScreenshotCapture.RestoreUi(); PlayTestUtil.ExitSim(); }

        sealed class Row
        {
            public string path, material, scene;
            public int views, max;
            public Vector3 eye;
            public Quaternion rot;
            public Renderer renderer;
        }

        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (var x = t; x != null && parts.Count < 6; x = x.parent) parts.Add(x.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        static List<Vector3> Viewpoints(PlayerRig rig)
        {
            var cells = new Dictionary<Vector3Int, Vector3>();
            const float cell = 1.0f;
            void Add(Vector3 p)
            {
                var key = new Vector3Int(Mathf.FloorToInt(p.x / cell), Mathf.RoundToInt(p.y / 1.2f), Mathf.FloorToInt(p.z / cell));
                if (!cells.ContainsKey(key) && Standable(p)) cells[key] = p;
            }
            var tri = NavMesh.CalculateTriangulation();
            var rng = new System.Random(15);
            for (int i = 0; i + 2 < tri.indices.Length; i += 3)
            {
                Vector3 a = tri.vertices[tri.indices[i]], b = tri.vertices[tri.indices[i + 1]], c = tri.vertices[tri.indices[i + 2]];
                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                int n = Mathf.Max(1, Mathf.CeilToInt(area / (cell * cell) * 2f));
                for (int k = 0; k < n; k++)
                {
                    float u = (float)rng.NextDouble(), v = (float)rng.NextDouble();
                    if (u + v > 1f) { u = 1f - u; v = 1f - v; }
                    Add(a + (b - a) * u + (c - a) * v);
                }
            }
            if (rig != null)
            {
                var o = rig.transform.position;
                for (float x = -14f; x <= 14f; x += cell)
                for (float z = -14f; z <= 14f; z += cell)
                {
                    var from = new Vector3(o.x + x, o.y + 1.8f, o.z + z);
                    if (Physics.Raycast(from, Vector3.down, out var hit, 2.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                        && hit.normal.y > 0.7f)
                        Add(hit.point);
                }
            }
            var list = cells.Values.ToList();
            if (list.Count > MaxPoints)
            {
                float step = list.Count / (float)MaxPoints;
                list = Enumerable.Range(0, MaxPoints).Select(i => list[Mathf.FloorToInt(i * step)]).ToList();
            }
            return list;
        }

        /// <summary>Đứng được: thân (ống 0,22 m từ 0,35 tới 1,7 m) không đụng vật.</summary>
        static bool Standable(Vector3 p) =>
            !Physics.CheckCapsule(p + Vector3.up * 0.35f, p + Vector3.up * 1.7f, 0.22f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        [UnityTest, Timeout(7200000)]
        public IEnumerator AllScenes() => Scan(new[] { SceneFlow.PrologueScene }.Concat(SceneFlow.GameScenes));

        [UnityTest, Timeout(7200000)]
        public IEnumerator Galley() => Scan(new[] { SceneFlow.GameScenes[1] });

        [UnityTest, Timeout(300000)]
        public IEnumerator BeamGeometryProjection()
        {
            yield return ChoreTestUtil.Galley();
            var rig = PlayerRig.Current;
            var beam = SeeThroughProbe.Candidates().Single(r => r.name == "Beam_4");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetPositionAndRotation(beam.transform.position, beam.transform.rotation);
            cube.transform.localScale = beam.transform.lossyScale;
            var copy = cube.GetComponent<Renderer>();
            copy.sharedMaterial = beam.sharedMaterial;
            var scene = SeeThroughProbe.Candidates(r => r != copy && !r.transform.IsChildOf(rig.transform));
            using var probe = new SeeThroughProbe();
            int original = 0, dynamic = 0, together = 0;
            foreach (var p in Viewpoints(rig))
            {
                var eye = p + Vector3.up * Eye;
                if (beam.bounds.SqrDistance(eye) > 16f) continue;
                var view = SeeThroughProbe.View(eye, Quaternion.Euler(-30f, 90f, 0f));
                int Pixels(IEnumerable<Renderer> rs) => probe.Render(view, rig.cam.fieldOfView, rig.cam.nearClipPlane, Range+2f, rs, out _).Sum(h => h.pixels);
                int a = Pixels(new[] { beam }), b = Pixels(new[] { copy });
                int c = probe.Render(view, rig.cam.fieldOfView, rig.cam.nearClipPlane, Range+2f, scene, out _)
                    .Where(h => h.renderer == beam).Sum(h => h.pixels);
                original = Mathf.Max(original, a); dynamic = Mathf.Max(dynamic, b); together = Mathf.Max(together, c);
                if (a >= 40 || b >= 40 || c >= 40)
                    Debug.Log($"BEAM_GEOMETRY eye={eye.ToString("F6")} isolated={a} cube={b} scene={c} inside={beam.bounds.Contains(eye)} static={beam.isPartOfStaticBatch} mesh={beam.GetComponent<MeshFilter>().sharedMesh.name} scale={beam.transform.lossyScale}");
            }
            Debug.Log($"BEAM_GEOMETRY max isolated={original} cube={dynamic} scene={together}");
            Object.Destroy(cube);
            Assert.Less(together, MinPixels, "Beam_4 từ điểm đứng thật: không thấy mặt sau xuyên trần");
        }

        IEnumerator Scan(IEnumerable<string> scenes)
        {
            LogAssert.ignoreFailingMessages = true;
            var probe = new SeeThroughProbe();
            Debug.Log($"SEE_THROUGH hiệu chỉnh mặt: trước {probe.CalibrationFront} px, sau {probe.CalibrationBack} px");
            Assert.Greater(probe.CalibrationFront, 1000);
            Assert.Less(probe.CalibrationBack, probe.CalibrationFront / 50);
            var md = new StringBuilder("# Nhìn xuyên: SeeThroughSweep\n\n");
            md.Append($"Mã bản dựng: {BuildId()}\n\nĐiểm đứng trên NavMesh và sàn quanh chỗ xuất hiện; mỗi điểm 8 hướng × ngẩng 30°, 10°, cúi 50°, 80°; vật trong {Range} m; " +
                      $"tính khi ≥ {MinPixels} px ở {SeeThroughProbe.Width}×{SeeThroughProbe.Height}.\n\n");
            var shots = Path.Combine(ScreenshotCapture.Folder, "see_through");
            Directory.CreateDirectory(shots);
            try
            {
                foreach (string scene in scenes)
                {
                    PlayTestUtil.Root().ResetProgress();
                    yield return PlayTestUtil.LoadAndSettle(scene);
                    yield return new WaitForSeconds(1f);
                    var rig = PlayerRig.Current;
                    var cam = rig != null ? rig.cam : Camera.main;
                    var renderers = SeeThroughProbe.Candidates(r => rig == null || !r.transform.IsChildOf(rig.transform));
                    var bounds = renderers.Select(r => r.bounds).ToArray();
                    var points = Viewpoints(rig);
                    var rows = new Dictionary<string, Row>();
                    int views = 0;
                    float started = Time.realtimeSinceStartup;
                    var near = new List<Renderer>();
                    foreach (var p in points)
                    {
                        var eye = p + Vector3.up * Eye;
                        near.Clear();
                        for (int i = 0; i < renderers.Count; i++)
                            if (renderers[i] != null && bounds[i].SqrDistance(eye) < Range * Range && (cam.cullingMask & (1 << renderers[i].gameObject.layer)) != 0)
                                near.Add(renderers[i]);
                        for (int yaw = 0; yaw < 360; yaw += 45)
                        foreach (float pitch in Pitches)
                        {
                            var rot = Quaternion.Euler(pitch, yaw, 0f);
                            var hits = probe.Render(SeeThroughProbe.View(eye, rot), cam.fieldOfView, cam.nearClipPlane, Range + 2f, near, out _);
                            views++;
                            foreach (var h in hits)
                            {
                                if (h.pixels < MinPixels) continue;
                                // Điểm lấy mẫu lọt vào trong vật nhỏ không có collider (người đứng, đèn treo): người chơi không đứng ở đó.
                                var hb = h.renderer.bounds;
                                hb.Expand(0.2f);
                                if (hb.Contains(eye) && Mathf.Max(hb.size.x, hb.size.z) < 2.5f) continue;
                                string key = PathOf(h.renderer.transform) + "#" + h.submesh;
                                if (!rows.TryGetValue(key, out var row))
                                    rows[key] = row = new Row { path = key, material = h.Material, scene = scene, renderer = h.renderer };
                                row.views++;
                                if (h.pixels > row.max) { row.max = h.pixels; row.eye = eye; row.rot = rot; }
                            }
                        }
                        if (views % 256 == 0) yield return null;
                    }
                    float seconds = Time.realtimeSinceStartup - started;
                    var sorted = rows.Values.OrderByDescending(r => r.views * Mathf.Sqrt(r.max)).ToList();
                    md.Append($"## {scene}: {sorted.Count} vật nhìn xuyên ({points.Count} điểm đứng, {views} lần nhìn, {seconds:F0} s)\n\n");
                    md.Append("| # | vật (đường dẫn#submesh) | vật liệu | số lần nhìn | px lớn nhất | điểm nhìn tệ nhất |\n|---|---|---|---|---|---|\n");
                    for (int i = 0; i < sorted.Count; i++)
                    {
                        var r = sorted[i];
                        var e = r.rot.eulerAngles;
                        md.Append($"| {i + 1} | {r.path} | {r.material} | {r.views} | {r.max} | ({r.eye.x:F6}, {r.eye.y:F6}, {r.eye.z:F6}) hướng {e.y:F0}° cúi {Mathf.DeltaAngle(0f, e.x):F0}° |\n");
                    }
                    md.Append("\n");
                    Debug.Log($"SEE_THROUGH {scene}: {sorted.Count} vật nhìn xuyên, {points.Count} điểm, {views} lần nhìn, {seconds:F0} s");

                    // Ảnh thật và ảnh dò cho các vật tệ nhất.
                    var shotCam = new GameObject("SeeThroughShot").AddComponent<Camera>();
                    shotCam.CopyFrom(cam);
                    shotCam.enabled = false;
                    for (int i = 0; i < Mathf.Min(Worst, sorted.Count); i++)
                    {
                        var r = sorted[i];
                        string name = $"{scene}_{i + 1:D2}_{Slug(r.renderer != null ? r.renderer.name : "x")}";
                        shotCam.transform.SetPositionAndRotation(r.eye, r.rot);
                        yield return ScreenshotCapture.CaptureCamera(shotCam, "see_through/" + name);
                        var around = renderers.Where((x, k) => x != null && bounds[k].SqrDistance(r.eye) < Range * Range).ToList();
                        probe.Render(SeeThroughProbe.View(r.eye, r.rot), cam.fieldOfView, cam.nearClipPlane, Range + 2f, around, out _);
                        probe.SaveOverlay(Path.Combine(shots, name + "_xuyen.png"));
                    }
                    Object.Destroy(shotCam.gameObject);
                }
            }
            finally
            {
                probe.Dispose();
                var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Logs", "qa"));
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "see_through.md"), md.ToString());
            }
        }

        static string Slug(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

        static string BuildId()
        {
            var path = Path.Combine(ScreenshotCapture.Folder, "BUILD_ID.txt");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "?";
        }
    }
}
