using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Quét góc nhìn thứ nhất bằng PlayerViewProbe ở bếp: cầm từng loại đồ cầm được (mỗi loại một món) rồi nhìn từ ngẩng −75° tới cúi
    /// 85°; đổ sọt than vào thùng, đổ xô tro ở máng, dốc bao khoai ở bàn (đo từng khung). Ghi Logs/qa/player_view.md (mỗi cảnh: lỗi vượt
    /// ngưỡng, giá trị lớn nhất và lúc xảy ra), log PLAYER_VIEW; ảnh góc nhìn người chơi và ảnh ngang thân (thấy cánh tay) ở
    /// Logs/shots/player_view/.
    /// </summary>
    [Category("Screenshots"), Explicit]
    public sealed class PlayerViewSweep
    {
        static GameRoot Root => GameRoot.Instance;
        static PlayerRig Rig => PlayerRig.Current;
        static readonly float[] Pitches = { -75f, -60f, -45f, -30f, -15f, 0f, 15f, 30f, 45f, 60f, 75f, 85f };
        static readonly float[] ShotPitches = { -60f, 0f, 45f, 85f };

        [SetUp] public void SetUp() => Time.captureDeltaTime = 1f / 60f;

        [TearDown]
        public void TearDown()
        {
            if (Root != null) { Root.Input.SimUse = false; Root.Input.SimLook = Vector2.zero; }
            Time.captureDeltaTime = 0f;
            ScreenshotCapture.RestoreUi();
            PlayTestUtil.ExitSim();
        }

        sealed class Worst
        {
            public readonly Dictionary<(PlayerViewProbe.Kind, string), (float value, string when)> max =
                new Dictionary<(PlayerViewProbe.Kind, string), (float, string)>();
            public int frames;

            public void Feed(List<PlayerViewProbe.Issue> issues, string when)
            {
                frames++;
                foreach (var i in issues)
                {
                    var key = (i.kind, i.where);
                    if (!max.TryGetValue(key, out var m) || i.value > m.value) max[key] = (i.value, when);
                }
            }

            public IEnumerable<string> Bad() => max.Where(kv => kv.Value.value > PlayerViewProbe.Limit(kv.Key.Item1))
                .OrderBy(kv => kv.Key.Item1).Select(kv => $"{new PlayerViewProbe.Issue { kind = kv.Key.Item1, where = kv.Key.Item2, value = kv.Value.value }} ({kv.Value.when})");

            public string All() => string.Join("; ", max.OrderBy(kv => kv.Key.Item1).Select(kv =>
                $"{new PlayerViewProbe.Issue { kind = kv.Key.Item1, where = kv.Key.Item2, value = kv.Value.value }}"));
        }

        readonly List<(string scene, Worst worst)> _report = new List<(string, Worst)>();
        PlayerViewProbe _probe;

        static string Slug(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

        IEnumerator FirstPerson(string name)
        {
            Root.UI.SetFade(0f);
            yield return ScreenshotCapture.Capture("player_view/" + name);
            _probe.Measure();
            _probe.See.SaveOverlay(Path.Combine(ScreenshotCapture.Folder, "player_view", name + "_xuyen.png"));
        }

        /// <summary>Ảnh ngang thân người chơi (đầu hiện với camera khác): thấy cánh tay, khuỷu, cổ tay nối bàn tay.</summary>
        static IEnumerator Side(string name, bool left = false)
        {
            var go = new GameObject("PlayerViewSide");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(Rig.cam);
            cam.enabled = false;
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.03f;
            var root = Rig.transform;
            var chest = root.position + Vector3.up * 1.2f + root.forward * 0.25f;
            go.transform.position = chest + root.right * (left ? -1.25f : 1.25f) + root.forward * 0.7f + Vector3.up * 0.25f;
            go.transform.LookAt(chest);
            yield return ScreenshotCapture.CaptureCamera(cam, "player_view/" + name);
            Object.Destroy(go);
        }

        static void Hold(Holdable h)
        {
            var who = Rig.interactor;
            if (who.Holding != null) who.ReleaseHeld();
            if (who.Tool != Tool.None) ToolHook.For(who.Tool)?.ReturnFrom(who);
            h.gameObject.SetActive(true);
            h.Locked = false;
            who.Pick(h);
            Assert.AreEqual(h, who.Holding, h.name);
        }

        IEnumerator Settle(float seconds = 0.6f)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
        }

        void Report()
        {
            var md = new StringBuilder("# Góc nhìn thứ nhất: PlayerViewSweep\n\n");
            md.Append($"Mã bản dựng: {BuildIdText()}\n\nNgưỡng: lỗ cổ > 0 điểm, mắt trong đồ, mặt cắt khoét đồ, nhìn xuyên > 2 tam giác, cổ tay hở > 2 cm, cổ tay gập > 70°, khuỷu lún > 1 cm, đồ đang cầm lún vào thân > 1,5 cm.\n\n");
            foreach (var (scene, worst) in _report)
            {
                var bad = worst.Bad().ToList();
                md.Append($"## {scene} ({worst.frames} khung): {(bad.Count == 0 ? "sạch" : bad.Count + " lỗi")}\n\n");
                foreach (var b in bad) md.Append($"- {b}\n");
                md.Append($"\nĐủ số: {worst.All()}\n\n");
                Debug.Log($"PLAYER_VIEW {scene}: {(bad.Count == 0 ? "sạch" : string.Join(" | ", bad))}");
            }
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Logs", "qa"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "player_view.md"), md.ToString());
        }

        static string BuildIdText()
        {
            var path = Path.Combine(ScreenshotCapture.Folder, "BUILD_ID.txt");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "?";
        }

        // Baseline gọn cho QA-0032/0034/0097: giữ cùng tên ảnh, root lưu riêng thư mục trước và sau sửa.
        [UnityTest, Timeout(300000)]
        public IEnumerator HangingAndTrayExtremes()
        {
            Directory.CreateDirectory(Path.Combine(ScreenshotCapture.Folder, "player_view"));
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            d.meals.cook.enabled = false;
            foreach (var npc in Object.FindObjectsByType<HaiBanTay.Npc.NpcAgent>(FindObjectsSortMode.None)) npc.gameObject.SetActive(false);
            _probe = new PlayerViewProbe(Rig);
            foreach (var task in d.storyStations.OfType<VoyageDelivery>().Where(s => s.orderKind == "ice" || s.orderKind == "water" || s.orderKind == "serve" || s.orderKind == "fish"))
            {
                Hold(task.load);
                _probe.Refresh();
                var worst = new Worst();
                foreach (float pitch in new[] { -30f, 0f, 45f, 85f })
                {
                    Rig.motor.Teleport(new Vector3(-1.5f, 0f, 0.6f), 0f);
                    Rig.look.SetAngles(0f, pitch);
                    yield return Settle(1f);
                    worst.Feed(_probe.Measure(), $"nhìn {pitch:F0}°");
                    yield return FirstPerson($"{task.orderKind}_extreme_p{pitch:F0}");
                    yield return Side($"{task.orderKind}_extreme_p{pitch:F0}_side");
                }
                _report.Add((task.orderKind, worst));
                Rig.interactor.ReleaseHeld();
                task.load.Detach(task.home.position, task.home.rotation, false);
                task.load.gameObject.SetActive(false);
            }
            Report();
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator GalleyHoldsPoursAndSack()
        {
            LogAssert.ignoreFailingMessages = true;
            Directory.CreateDirectory(Path.Combine(ScreenshotCapture.Folder, "player_view"));
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            d.meals.cook.enabled = false;
            // Người khác đứng chắn ảnh ngang thân; các cảnh dưới không cần ai.
            foreach (var npc in Object.FindObjectsByType<HaiBanTay.Npc.NpcAgent>(FindObjectsSortMode.None)) npc.gameObject.SetActive(false);
            _probe = new PlayerViewProbe(Rig);
            Debug.Log($"PLAYER_VIEW hiệu chỉnh mặt: trước {_probe.See.CalibrationFront} px, sau {_probe.See.CalibrationBack} px");
            Assert.Greater(_probe.See.CalibrationFront, 1000);
            Assert.Less(_probe.See.CalibrationBack, _probe.See.CalibrationFront / 50);
            Assert.Greater(_probe.NeckPoints, 8, "dựng được vòng cổ");
            Assert.GreaterOrEqual(_probe.BodyParts, 6, "dựng được vỏ thân, đùi, bắp chân để đo đồ lún vào thân");
            Debug.Log($"PLAYER_VIEW camera: near {Rig.cam.nearClipPlane:F3} m, fov {Rig.cam.fieldOfView:F0}°, vòng cổ {_probe.NeckPoints} điểm");

            // 1. Mỗi loại đồ cầm được: nhìn từ ngẩng tới cúi.
            var seen = new HashSet<string>();
            var holdables = Object.FindObjectsByType<Holdable>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID)
                .Where(h => h.gameObject.scene == d.gameObject.scene).ToList();
            foreach (var h in holdables)
            {
                string kind = $"{h.kind}{(h.hang ? "_xach" : h.ShoulderCarried ? "_vai" : h.twoHanded ? "_om" : "")}";
                if (!seen.Add(kind)) continue;
                Hold(h);
                _probe.Refresh();
                var worst = new Worst();
                foreach (float pitch in Pitches)
                {
                    Rig.motor.Teleport(new Vector3(-1.5f, 0f, 0.6f), 0f);
                    Rig.look.SetAngles(0f, pitch);
                    yield return Settle();
                    worst.Feed(_probe.Measure(), $"nhìn {pitch:F0}°");
                    if (ShotPitches.Contains(pitch))
                    {
                        yield return FirstPerson($"{Slug(kind)}_p{pitch:F0}");
                        if (pitch == 0f || pitch == 85f) yield return Side($"{Slug(kind)}_p{pitch:F0}_side");
                    }
                }
                _report.Add(($"cầm {h.name} ({kind})", worst));
                Rig.interactor.ReleaseHeld();
                h.Detach(h.transform.position, Quaternion.identity, physics: false);
                h.gameObject.SetActive(false);
            }

            // 2. Đổ sọt than vào thùng.
            {
                var coal = d.coal;
                coal.SetStock(0);
                var basket = coal.baskets[0];
                basket.gameObject.SetActive(true);
                Hold(basket);
                Assert.IsFalse(coal.IsEmptyBasket(basket), "sọt đầu ca có than");
                ChoreTestUtil.Look(new Vector3(0.3f, 0f, 1.45f), coal.bin.transform.position + Vector3.up * 0.5f);
                yield return Settle(0.4f);
                _probe.Refresh();
                coal.OnInteract(coal.bin, Rig.interactor);
                yield return Pour("đổ sọt than", "coal_pour", basket.transform, () => coal.PouringBasket);
                // Sọt rỗng còn trên tay (ghi chú 15/10: ngẩng nhìn thẳng thấy xuyên sọt rỗng).
                Assert.IsTrue(coal.IsEmptyBasket(basket));
                var empty = new Worst();
                foreach (float pitch in Pitches)
                {
                    Rig.motor.Teleport(new Vector3(-1.5f, 0f, 0.6f), 0f);
                    Rig.look.SetAngles(0f, pitch);
                    yield return Settle();
                    empty.Feed(_probe.Measure(), $"nhìn {pitch:F0}°");
                    if (ShotPitches.Contains(pitch) || pitch == -15f) yield return FirstPerson($"coal_empty_p{pitch:F0}");
                }
                yield return Side("coal_empty_side");
                _report.Add(("cầm sọt rỗng", empty));
                Rig.interactor.ReleaseHeld();
                basket.gameObject.SetActive(false);
            }

            // 3. Đổ xô tro ở máng.
            {
                var ash = d.ash;
                ash.bucket.gameObject.SetActive(true);
                ash.Rake.ForceFull();
                Hold(ash.bucket);
                var chute = ash.chute.transform;
                var mouth = chute.TransformPoint(new Vector3(0.2f, 0.98f, 0f));
                var stand = mouth + new Vector3(1.0f, 0f, 0.15f);
                stand.y = chute.position.y;
                ChoreTestUtil.Look(stand, mouth);
                yield return Settle(0.4f);
                _probe.Refresh();
                ash.OnInteract(ash.chute, Rig.interactor);
                yield return Pour("đổ xô tro", "ash_pour", ash.bucket.transform, () => ash.bucket.Busy);
                Rig.interactor.ReleaseHeld();
                ash.bucket.Detach(ash.store.transform.position, ash.store.transform.rotation, physics: false);
            }

            // 4. Dốc bao khoai ở bàn: đứng ở bàn nhìn xuống, rồi kéo xuống để dốc.
            {
                var sacks = d.sacks;
                ChoreTestUtil.Look(new Vector3(1.32f, 0f, -1.05f), sacks.mouth.transform.position);
                sacks.sacks[0].gameObject.SetActive(true);
                sacks.zone.Place(sacks.sacks[0]);
                sacks.OnInteract(sacks.mouth, Rig.interactor);
                yield return Settle(0.4f);
                _probe.Refresh();
                var worst = new Worst();
                float yaw = Rig.look.Yaw;
                foreach (float pitch in new[] { 30f, 55f, 70f, 85f })
                {
                    Rig.look.SetAngles(yaw, pitch);
                    yield return Settle();
                    worst.Feed(_probe.Measure(), $"đứng bàn nhìn {pitch:F0}°");
                    yield return FirstPerson($"sack_table_p{pitch:F0}");
                }
                yield return Side("sack_table_side");
                Rig.look.SetAngles(yaw, 40f);
                yield return Settle(0.3f);
                Root.Input.SimUse = true;
                float end = Time.time + 10f;
                int shot = 0;
                while (sacks.Pour.Progress < 0.95f && Time.time < end)
                {
                    Root.Input.SimLook = new Vector2(0f, -12f);
                    yield return null;
                    worst.Feed(_probe.Measure(seeThrough: Time.frameCount % 3 == 0), $"dốc {sacks.Pour.Progress:P0}, nhìn {Rig.look.Pitch:F0}°");
                    if (shot < 3 && sacks.Pour.Progress > 0.3f * (shot + 1))
                    {
                        Root.Input.SimLook = Vector2.zero;
                        yield return FirstPerson($"sack_pour_{shot}");
                        yield return Side($"sack_pour_{shot}_side");
                        shot++;
                    }
                }
                Root.Input.SimUse = false;
                Root.Input.SimLook = Vector2.zero;
                _report.Add(("dốc bao khoai", worst));
            }

            Report();
        }

        IEnumerator Pour(string label, string slug, Transform item, System.Func<bool> busy)
        {
            var worst = new Worst();
            yield return PlayTestUtil.WaitUntil(busy, 2f);
            Assert.IsTrue(busy(), label + ": chưa bắt đầu");
            var shots = new[] { 60f, 120f, 175f };
            int next = 0, frame = 0, seq = 0;
            float start = Time.time;
            while (busy() && Time.time - start < 8f)
            {
                yield return null;
                float tilt = Vector3.Angle(item.up, Vector3.up);
                worst.Feed(_probe.Measure(seeThrough: Time.frameCount % 2 == 0), $"{Time.time - start:F2} s, nghiêng {tilt:F0}°");
                // Chuỗi ảnh mỗi 0,1 s (60 Hz): xem tay chuyển quai ↔ vành, nhấc, lật, đưa về.
                if (frame++ % 6 == 0)
                    yield return ScreenshotCapture.Capture($"player_view/{slug}_seq{seq++:D2}");
                if (next < shots.Length && tilt >= shots[next])
                {
                    yield return FirstPerson($"{slug}_{shots[next]:F0}");
                    yield return Side($"{slug}_{shots[next]:F0}_side");
                    yield return Side($"{slug}_{shots[next]:F0}_side_left", left: true);
                    next++;
                }
            }
            _report.Add((label, worst));
        }
    }
}
