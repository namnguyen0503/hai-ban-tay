using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HaiBanTay.Editor.SceneBuilders;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Đèn phải khớp thực tế (wave4-light): mọi Light point/spot có vật đèn nhìn thấy và phát sáng trong bán kính 0,6 m,
    /// và mọi vật đèn (đèn treo, đèn tường, đèn dầu, đèn đường, đèn quán) có Light cùng phần kính/ngọn lửa phát sáng.
    /// Mở cảnh đã dựng (chạy rebuild_scenes trước). Ghi bản kê vào Logs/light_dump.txt để soi.
    /// </summary>
    public class LightFixtureTests
    {
        [TestCase("02_Galley"), Category("LIGHT")]
        [TestCase("03_Marseille")]
        public void EveryStoveCellControlsItsOwnGlowAndSharesOneRealtimeLight(string scene)
        {
            EditorSceneManager.OpenScene(BuilderUtils.ScenePath(scene), OpenSceneMode.Single);
            var coal = Object.FindAnyObjectByType<HaiBanTay.Shift.CoalStation>(FindObjectsInactive.Include);
            Assert.IsNotNull(coal, scene);
            Assert.AreEqual(6, coal.cells.Length);
            Assert.IsFalse(Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Any(l => l.name == "RangeLight"), "LIGHT: không bake ánh đỏ cố định của lò");
            for (int lit = 0; lit < coal.cells.Length; lit++)
            {
                coal.Fire.Set(0f, 0f);
                coal.CellState(lit).Set(4.5f, 1f);
                Invoke(coal, "ApplyFire");
                for (int i = 0; i < coal.cells.Length; i++)
                {
                    var fx = coal.cells[i].flicker;
                    Assert.AreEqual(i == lit ? 1f : 0f, fx.level, 0.0001f, scene + "/ô " + i);
                    Assert.IsNull(fx.fireLight, "LIGHT: từng ô không chiếm thêm suất realtime");
                    foreach (var glow in fx.glows)
                        Assert.IsFalse((UnityEditor.GameObjectUtility.GetStaticEditorFlags(glow.renderer.gameObject) & UnityEditor.StaticEditorFlags.ContributeGI) != 0,
                            "LIGHT: phần nóng thay đổi không được bake GI cố định");
                }
                Assert.Greater(coal.fireLight.intensity, 0f);
                Assert.Less(Vector3.Distance(coal.fireLight.transform.position, coal.cells[lit].hob.position), 0.4f);
            }
            coal.Fire.Set(0f, 0f);
            Invoke(coal, "ApplyFire");
            Assert.AreEqual(0f, coal.fireLight.intensity, "LIGHT: tắt mọi ô thì đèn lửa tắt hẳn");
        }

        [Test, Category("LIGHT")]
        public void ExtinguishedFireHasNoEmissiveFloor()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var fire = go.AddComponent<HaiBanTay.Ship.FireFlicker>();
                fire.glows = new[] { new HaiBanTay.Ship.FireFlicker.Glow { renderer = go.GetComponent<Renderer>(), strength = 2f } };
                Invoke(fire, "Awake");
                fire.SetFire(0f, Color.red, 0f);
                Invoke(fire, "Update");
                var block = new MaterialPropertyBlock();
                go.GetComponent<Renderer>().GetPropertyBlock(block);
                Assert.AreEqual(Color.black, block.GetColor("_EmissionColor"), "LIGHT: ô tắt không giữ nền đỏ");
            }
            finally { Object.DestroyImmediate(go); }
        }

        static void Invoke(object component, string method) => component.GetType()
            .GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(component, null);

        const float Radius = 0.6f;
        /// <summary>Vật liệu coi là "phát sáng thật": bật _EMISSION và một kênh màu phát ≥ 1 (HDR); bảng phấn/biển tên (0,3–0,45) không tính.</summary>
        const float MinGlow = 1f;

        public static readonly string[] Scenes = { "00_Menu", "00_Prologue", "01_NhaRong", "02_Galley", "03_Marseille" };

        /// <summary>Tên Light không cần vật đèn riêng (tiền tố tên) + lý do.</summary>
        static readonly (string prefix, string reason)[] LightExceptions =
        {
            ("HandsFill", "đèn fill của hai bàn tay FPS gắn vào camera, không phải nguồn sáng trong thế giới"),
        };

        /// <summary>Tên ArtSlot là vật đèn; mỗi vật phải có Light và phần phát sáng.</summary>
        static readonly string[] FixtureSlots = { "ArtSlot_StormLantern", "ArtSlot_GasWallLantern", "ArtSlot_OilLamp" };

        struct Glow
        {
            public Renderer renderer;
            public Bounds bounds;
            public string material;
        }

        static float Peak(Material m)
        {
            if (m == null || !m.IsKeywordEnabled("_EMISSION") || !m.HasProperty("_EmissionColor"))
                return 0f;
            var c = m.GetColor("_EmissionColor");
            return Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        }

        static Bounds WorldBounds(Transform t, Bounds local)
        {
            var c = local.center;
            var x = local.extents;
            var b = new Bounds(t.TransformPoint(c), Vector3.zero);
            for (int i = 0; i < 8; i++)
                b.Encapsulate(t.TransformPoint(c + new Vector3((i & 1) == 0 ? -x.x : x.x, (i & 2) == 0 ? -x.y : x.y, (i & 4) == 0 ? -x.z : x.z)));
            return b;
        }

        /// <summary>Từng vật liệu phát sáng (≥ MinGlow) của renderer cùng hộp bao thế giới của riêng nó (mesh không đọc được thì lấy cả renderer).</summary>
        static IEnumerable<Glow> GlowsOf(Renderer r)
        {
            var filter = r.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (Peak(mats[i]) < MinGlow)
                    continue;
                var bounds = r.bounds;
                if (mesh != null && mesh.subMeshCount == 1)
                    bounds = WorldBounds(r.transform, mesh.bounds);
                else if (mesh != null && mesh.isReadable && i < mesh.subMeshCount)
                    bounds = WorldBounds(r.transform, mesh.GetSubMesh(i).bounds);
                yield return new Glow { renderer = r, bounds = bounds, material = mats[i].name };
            }
        }

        static string PathOf(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent)
                sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        static string Pos(Vector3 v) => $"({v.x:0.00}, {v.y:0.00}, {v.z:0.00})";

        static bool IsFixtureLight(Light l) => l.type != LightType.Directional;

        static bool IsLampMaterial(string name)
        {
            var n = name.ToLowerInvariant();
            return n.Contains("lamp") || n.Contains("lantern") || n.Contains("bulb") || n.Contains("glow");
        }

        sealed class Report
        {
            public readonly List<string> orphanLights = new List<string>();
            public readonly List<string> darkFixtures = new List<string>();
            public readonly StringBuilder dump = new StringBuilder();
        }

        static Report Scan(string scene)
        {
            EditorSceneManager.OpenScene(BuilderUtils.ScenePath(scene), OpenSceneMode.Single);
            var rep = new Report();
            var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(IsFixtureLight).OrderBy(l => PathOf(l.transform)).ToArray();
            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var glows = renderers.SelectMany(GlowsOf).ToList();
            rep.dump.AppendLine($"=== {scene}: {lights.Length} đèn point/spot, {glows.Count} mảng phát sáng ===");

            // (i) Mỗi Light có mảng phát sáng trong bán kính.
            foreach (var l in lights)
            {
                int near = glows.Count(g => g.bounds.SqrDistance(l.transform.position) <= Radius * Radius);
                rep.dump.AppendLine($"  LIGHT {PathOf(l.transform)} {l.type} {(l.lightmapBakeType)} I={l.intensity:0.0} R={l.range:0} {Pos(l.transform.position)} enabled={l.enabled} activeInHierarchy={l.gameObject.activeInHierarchy} glow_gần={near}");
                if (near > 0)
                    continue;
                var ex = LightExceptions.FirstOrDefault(e => l.name.StartsWith(e.prefix));
                if (ex.prefix != null)
                    continue;
                rep.orphanLights.Add($"[{scene}] {PathOf(l.transform)} tại {Pos(l.transform.position)} ({l.lightmapBakeType}, I={l.intensity:0.0}): không có vật đèn phát sáng trong {Radius} m");
            }

            // (ii) Vật đèn: ArtSlot đèn + mọi mảng phát sáng mang vật liệu "lamp/glow" ngoài các ArtSlot đó.
            var slots = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(t => FixtureSlots.Contains(t.name)).OrderBy(PathOf).ToArray();
            foreach (var slot in slots)
            {
                var rs = slot.GetComponentsInChildren<Renderer>(true);
                var slotGlows = rs.SelectMany(GlowsOf).ToList();
                var bounds = rs.Length > 0 ? rs[0].bounds : new Bounds(slot.position, Vector3.zero);
                foreach (var r in rs) bounds.Encapsulate(r.bounds);
                bool hasLight = lights.Any(l => bounds.SqrDistance(l.transform.position) <= Radius * Radius);
                rep.dump.AppendLine($"  FIXTURE {PathOf(slot)} bounds={Pos(bounds.center)} size={Pos(bounds.size)} renderers={rs.Length} glow={slotGlows.Count} light={hasLight}"
                    + " mats=[" + string.Join(", ", rs.SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name + ":" + Peak(m).ToString("0.0")).Distinct()) + "]");
                if (!hasLight)
                    rep.darkFixtures.Add($"[{scene}] {PathOf(slot)} tại {Pos(bounds.center)}: không có Light trong {Radius} m");
                if (slotGlows.Count == 0)
                    rep.darkFixtures.Add($"[{scene}] {PathOf(slot)} tại {Pos(bounds.center)}: kính/ngọn lửa không phát sáng (vật liệu: "
                        + string.Join(", ", rs.SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).Distinct()) + ")");
            }
            foreach (var g in glows.Where(g => IsLampMaterial(g.material) && !g.renderer.GetComponentsInParent<Transform>(true).Any(t => FixtureSlots.Contains(t.name))))
            {
                bool hasLight = lights.Any(l => g.bounds.SqrDistance(l.transform.position) <= Radius * Radius);
                rep.dump.AppendLine($"  GLOWMESH {PathOf(g.renderer.transform)} mat={g.material} bounds={Pos(g.bounds.center)} size={Pos(g.bounds.size)} light={hasLight}");
                if (!hasLight)
                    rep.darkFixtures.Add($"[{scene}] vật đèn {PathOf(g.renderer.transform)} ({g.material}) tại {Pos(g.bounds.center)}: không có Light trong {Radius} m");
            }
            // Mọi renderer tên giống đèn mà không có phần phát sáng (mô hình đèn đen).
            foreach (var r in renderers)
            {
                var n = r.name.ToLowerInvariant();
                if ((n.Contains("lamp") || n.Contains("lantern") || n.Contains("bulb")) && !r.GetComponentsInParent<Transform>(true).Any(t => FixtureSlots.Contains(t.name)))
                    rep.dump.AppendLine($"  LAMPNAME {PathOf(r.transform)} bounds={Pos(r.bounds.center)} size={Pos(r.bounds.size)} mats=[{string.Join(", ", r.sharedMaterials.Where(m => m != null).Select(m => m.name + ":" + Peak(m).ToString("0.0")))}]");
            }
            return rep;
        }

        static void Write(string scene, Report rep)
        {
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Logs"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "light_dump_" + scene + ".txt"), rep.dump.ToString());
        }

        [Test]
        public void RealtimeLightsStayInsideTheLightBudget([ValueSource(nameof(Scenes))] string scene)
        {
            EditorSceneManager.OpenScene(BuilderUtils.ScenePath(scene), OpenSceneMode.Single);
            // Góc buổi tối (đèn dầu) tắt sẵn: 02 bật lúc chạy và đổi vào LightBudget qua ShiftDirector.eveningLamp; 01/03 tắt hẳn.
            var realtime = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(l => l.type != LightType.Directional && l.lightmapBakeType != LightmapBakeType.Baked && l.gameObject.activeInHierarchy).ToArray();
            var director = Object.FindAnyObjectByType<HaiBanTay.Shift.ShiftDirector>(FindObjectsInactive.Include);
            if (director != null)
            {
                Assert.IsNotNull(director.eveningLamp, scene + ": ShiftDirector thiếu đèn buổi tối");
                Assert.AreNotEqual(LightmapBakeType.Baked, director.eveningLamp.lightmapBakeType, "đèn dầu buổi tối phải realtime (vào LightBudget)");
                Assert.AreEqual(LightShadows.None, director.eveningLamp.shadows);
            }
            var budget = Object.FindAnyObjectByType<HaiBanTay.Performance.LightBudget>(FindObjectsInactive.Include);
            var managed = new HashSet<Light>();
            if (director != null)
                managed.Add(director.eveningLamp);
            if (budget != null)
            {
                foreach (var l in budget.exterior) managed.Add(l);
                foreach (var l in budget.interior) managed.Add(l);
            }
            // HandsFill do PlayerRig bật/tắt và LightBudget.Lend nhường suất; đèn realtime còn lại phải nằm trong LightBudget.
            var loose = realtime.Where(l => !managed.Contains(l) && !l.name.StartsWith("HandsFill")).ToArray();
            if (budget == null)
                Assert.LessOrEqual(loose.Length, 2, scene + ": cảnh không có LightBudget chỉ được tối đa 2 đèn realtime không bóng");
            else
                Assert.IsEmpty(loose.Select(l => PathOf(l.transform)).ToArray(), scene + ": đèn realtime ngoài LightBudget");
            // Trạng thái lưu trong scene đã đúng ngân sách: đèn điểm realtime không bóng (LightBudget cấp bóng lúc chạy).
            foreach (var l in realtime.Where(l => !l.name.StartsWith("HandsFill")))
                Assert.AreEqual(LightShadows.None, l.shadows, scene + ": " + PathOf(l.transform) + " không được lưu với bóng");
        }

        [Test]
        public void RealtimeFixturesDimTheirGlassWhenTheLightIsOff([ValueSource(nameof(Scenes))] string scene)
        {
            EditorSceneManager.OpenScene(BuilderUtils.ScenePath(scene), OpenSceneMode.Single);
            var slots = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(t => FixtureSlots.Contains(t.name)).ToArray();
            var syncs = Object.FindObjectsByType<HaiBanTay.Performance.LampGlowSync>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var missing = new List<string>();
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                         .Where(l => l.type != LightType.Directional && l.lightmapBakeType != LightmapBakeType.Baked && !l.name.StartsWith("HandsFill")))
            {
                // Lửa lò: ngọn lửa là vật liệu Fire gắn với CoalStation, không phải kính đèn.
                if (l.name == "FireLight")
                    continue;
                if (!syncs.Any(s => s.lamp == l && s.parts.Length > 0 && s.parts.All(p => p.unlit != null && Peak(p.unlit) < 0.1f)))
                    missing.Add(PathOf(l.transform));
            }
            Assert.IsEmpty(missing, scene + ": đèn realtime thiếu LampGlowSync (đèn tắt mà kính vẫn sáng):\n" + string.Join("\n", missing));
            Assert.IsNotNull(slots);
        }

        [Test]
        public void FixtureGlassIsTransparentWithAFlameInside([ValueSource(nameof(Scenes))] string scene)
        {
            EditorSceneManager.OpenScene(BuilderUtils.ScenePath(scene), OpenSceneMode.Single);
            var bad = new List<string>();
            foreach (var slot in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                         .Where(t => FixtureSlots.Contains(t.name)).OrderBy(PathOf))
            {
                var rs = slot.GetComponentsInChildren<Renderer>(true);
                var glass = rs.SelectMany(r => r.sharedMaterials.Select(m => (r, m))).Where(x => x.m != null && x.m.name.ToLowerInvariant().Contains("glass")
                    && x.m.name != "LampGlassOff").ToList();
                if (glass.Count == 0 || glass.Any(x => x.m.GetFloat("_Surface") < 0.5f || Peak(x.m) >= MinGlow))
                    bad.Add(PathOf(slot) + ": kính phải là vật liệu Transparent, phát sáng < " + MinGlow);
                // Ngọn lửa (phát sáng mạnh, đặc) nằm trong hộp bao của phần kính.
                var flames = rs.SelectMany(r => r.sharedMaterials.Select(m => (r, m))).Where(x => Peak(x.m) >= MinGlow && x.m.GetFloat("_Surface") < 0.5f).ToList();
                if (flames.Count == 0)
                    bad.Add(PathOf(slot) + ": không có lõi lửa phát sáng đặc nhìn xuyên qua kính");
                else if (glass.Count > 0)
                {
                    var gb = glass[0].r.bounds;
                    foreach (var g in glass) gb.Encapsulate(g.r.bounds);
                    gb.Expand(0.05f);
                    if (!flames.Any(f => gb.Contains(f.r.bounds.center)))
                        bad.Add(PathOf(slot) + ": lõi lửa không nằm trong phần kính");
                }
            }
            Assert.IsEmpty(bad, scene + ":\n" + string.Join("\n", bad));
        }

        [OneTimeTearDown]
        public void Close() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        [Test]
        public void EveryLightHasAGlowingFixtureNearby([ValueSource(nameof(Scenes))] string scene)
        {
            var rep = Scan(scene);
            Write(scene, rep);
            Assert.IsEmpty(rep.orphanLights, "Light vô hình (không có vật đèn phát sáng):\n" + string.Join("\n", rep.orphanLights));
        }

        [Test]
        public void EveryFixtureHasALightAndGlowingGlass([ValueSource(nameof(Scenes))] string scene)
        {
            var rep = Scan(scene);
            Write(scene, rep);
            Assert.IsEmpty(rep.darkFixtures, "Vật đèn thiếu Light hoặc kính đen:\n" + string.Join("\n", rep.darkFixtures));
        }
    }
}
