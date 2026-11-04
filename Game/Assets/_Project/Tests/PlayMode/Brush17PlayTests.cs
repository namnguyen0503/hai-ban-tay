using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HaiBanTay.Core;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// brush17: bàn chải thật, nồi cao, chảo cọ bằng bàn chải cán dài. Mỗi việc đo từng khung 60 Hz theo luật camera 17/10 (mắt ở chỗ
    /// mặc định trên người, game không tự xoay camera, không dịch chuyển tức thời), những gì người chơi THẤY (17/10, tay đã ẩn):
    /// <list type="bullet">
    /// <item>bàn chải, đồ đang cầm, thân không xuyên vật (PlayerClipProbe: Tool/Held/Body; bàn chải tách khỏi nắm tay vẫn là đồ của Ba);</item>
    /// <item>đầu chải nằm trên mặt cọ, vết bẩn mất đúng chỗ đầu chải đi qua, chỗ khác còn bẩn;</item>
    /// <item>bàn chải, nồi cao chuyển động mượt (MotionProbe, nhảy &lt; 2 cm một khung); nồi cao không lún vào lò, bàn, vách.</item>
    /// </list>
    /// </summary>
    public class Brush17PlayTests
    {
        const float EyeSlack = 0.05f;
        const float StepPerFrame = 0.06f;
        /// <summary>Nồi cao lún vào vật va chạm của cảnh quá mức này là xuyên (hộp va chạm nồi bao cả thân).</summary>
        const float PotInto = 0.02f;

        static GameRoot Root => GameRoot.Instance;
        static PlayerRig Rig => PlayerRig.Current;

        [SetUp] public void SetUp() => Time.captureDeltaTime = 1f / 60f;

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            if (Root != null) { Root.Input.SimUse = false; Root.Input.SimLook = Vector2.zero; Root.Input.SimMove = Vector2.zero; }
            foreach (var probe in Object.FindObjectsByType<PlayerClipProbe>(FindObjectsSortMode.None)) Object.Destroy(probe.gameObject);
            foreach (var probe in Object.FindObjectsByType<MotionProbe>(FindObjectsSortMode.None)) Object.Destroy(probe);
            PlayTestUtil.ExitSim();
        }

        /// <summary>Đo một lượt việc: mắt, xoay, nhảy, xuyên vật của Ba, giật của vật, nồi cao lún vào cảnh.</summary>
        sealed class Watch
        {
            readonly string _label;
            readonly PlayerClipProbe _clip;
            readonly MotionProbe _motion;
            float _yaw, _pitch;
            Vector3 _lastRig, _lastEye;
            bool _started;
            int _frames;
            string _phase = "";
            public float EyeForward, EyeDrop, Turn, Jump, PotDepth;
            public string EyeAt = "", TurnAt = "", JumpAt = "", PotAt = "";
            public bool CheckTurn = true;
            public Rigidbody Pot;

            public Watch(string label)
            {
                _label = label;
                _clip = PlayerClipProbe.Attach(shots: true);
                _clip.Label = () => _phase;
                _motion = new GameObject("Brush17Motion").AddComponent<MotionProbe>();
                Rebase();
                Brush17Recorder.Begin(label);
            }

            public Watch Track(string name, Transform t)
            {
                if (t != null) _motion.Watch(name, t);
                return this;
            }

            public void Rebase()
            {
                _yaw = Rig.look.Yaw;
                _pitch = Rig.look.Pitch;
                _started = false;
            }

            public void Sample(string phase)
            {
                _frames++;
                Brush17Recorder.Frame();
                string at = $"{phase} (khung {_frames})";
                _phase = at;
                var eye = Rig.head.position;
                var local = Rig.transform.InverseTransformPoint(eye);
                // Ghế đẩu: mắt lên cao đúng bằng ghế (motor nâng dần), không ra trước, không hạ.
                float baseHeight = Rig.motor.headHeight + Mathf.Max(0f, Rig.motor.ExtraHeadHeight);
                if (Mathf.Abs(local.z) > EyeForward) { EyeForward = Mathf.Abs(local.z); EyeAt = at; }
                // Đang bước lên ghế: mắt lên dần tới độ cao ghế (motor 1,2 m/s), thấp hơn đích không phải hạ đầu.
                float drop = baseHeight - local.y;
                if (Rig.motor.ExtraHeadHeight > 0.01f) drop = Mathf.Min(drop, Rig.motor.headHeight - local.y);
                if (drop > EyeDrop) { EyeDrop = drop; EyeAt = at; }
                if (CheckTurn)
                {
                    float turn = Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(_yaw, Rig.look.Yaw)), Mathf.Abs(Rig.look.Pitch - _pitch));
                    if (turn > Turn) { Turn = turn; TurnAt = at; }
                }
                if (_started)
                {
                    float jump = (Rig.transform.position - _lastRig).magnitude;
                    float eyeJump = ((eye - _lastEye) - (Rig.transform.position - _lastRig)).magnitude;
                    jump = Mathf.Max(jump, eyeJump);
                    if (jump > Jump)
                    {
                        Jump = jump;
                        JumpAt = $"{at}: người {_lastRig:F2} → {Rig.transform.position:F2}, mắt dời thêm {eyeJump * 100f:F1} cm" +
                                 (Pot != null ? $", nồi {Pot.position:F2}" : "");
                    }
                }
                _lastRig = Rig.transform.position;
                _lastEye = eye;
                _started = true;
                if (Pot != null)
                {
                    float d = PotPenetration(Pot, out string what);
                    if (d > PotDepth) { PotDepth = d; PotAt = $"{at}: {what}"; }
                }
            }

            public void AssertClean()
            {
                var majors = _clip.Track.Majors.Where(f => f.kind == PlayerClipDetector.Kind.Tool || f.kind == PlayerClipDetector.Kind.Held ||
                                                           f.kind == PlayerClipDetector.Kind.Body).ToList();
                Brush17Recorder.End();
                string report = _clip.WriteReport("brush17_" + _label);
                Debug.Log($"BRUSH17 {_label}: mắt tới {EyeForward * 100f:F1} cm, hạ {EyeDrop * 100f:F1} cm ({EyeAt}); xoay {Turn:F1}° ({TurnAt}); " +
                          $"nhảy {Jump * 100f:F1} cm/khung ({JumpAt}); nồi cao lún {PotDepth * 100f:F1} cm ({PotAt})\n" +
                          $"MOTION {_motion.Report}\nPLAYER_CLIP {report}\n{_clip.Track.Summary()}");
                Object.Destroy(_clip.gameObject);
                Object.Destroy(_motion.gameObject);
                Assert.LessOrEqual(EyeForward, EyeSlack, $"{_label}: mắt dời ra trước/sau {EyeForward * 100f:F1} cm ({EyeAt})");
                Assert.LessOrEqual(EyeDrop, EyeSlack, $"{_label}: mắt hạ {EyeDrop * 100f:F1} cm ({EyeAt})");
                Assert.LessOrEqual(Turn, 0.5f, $"{_label}: game tự xoay camera {Turn:F1}° ({TurnAt})");
                Assert.LessOrEqual(Jump, StepPerFrame, $"{_label}: người/camera nhảy {Jump * 100f:F1} cm một khung ({JumpAt})");
                Assert.Less(_motion.Worst, MotionProbe.PopStep, $"{_label}: vật giật/nhảy chỗ: {_motion.Report}");
                Assert.LessOrEqual(PotDepth, PotInto, $"{_label}: nồi cao lún vào cảnh {PotDepth * 100f:F1} cm ({PotAt})");
                Assert.IsEmpty(majors.Select(f => f.Line), $"{_label}: bàn chải/đồ cầm/thân của Ba xuyên vật\n{_clip.Track.Summary()}");
            }
        }

        /// <summary>Va chạm nồi cao lún vào vật va chạm (không trigger) của cảnh, trừ người chơi, sàn và chính nồi.</summary>
        static float PotPenetration(Rigidbody pot, out string what)
        {
            what = "";
            float worst = 0f;
            foreach (var mine in pot.GetComponentsInChildren<Collider>())
            {
                if (mine.isTrigger || !mine.enabled) continue;
                var b = mine.bounds;
                foreach (var c in Physics.OverlapBox(b.center, b.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (c.attachedRigidbody == pot || c.transform.IsChildOf(Rig.transform)) continue;
                    // Sàn tàu (nồi đứng trên sàn: đáy chạm mặt sàn là đúng).
                    if (c.bounds.max.y < pot.position.y + 0.03f) continue;
                    var t = mine.transform;
                    if (!Physics.ComputePenetration(mine, t.position, t.rotation, c, c.transform.position, c.transform.rotation, out var dir, out float d)) continue;
                    // Đáy nồi tựa lên mặt bếp (đẩy thẳng lên) là đặt, không phải xuyên.
                    if (Vector3.Dot(dir, Vector3.up) > 0.9f && d < 0.03f) continue;
                    if (d > worst) { worst = d; what = c.name; }
                }
            }
            return worst;
        }

        static ShiftDirector Quiet()
        {
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            d.meals.cook.enabled = false;
            foreach (var follow in Object.FindObjectsByType<NpcToolFollow>(FindObjectsSortMode.None))
                if (follow.tool != null) follow.tool.gameObject.SetActive(false);
            foreach (var npc in Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None)) npc.gameObject.SetActive(false);
            return d;
        }

        static IEnumerator Run(Watch watch, string phase, float seconds, System.Func<Vector2> look = null, bool use = true, System.Func<bool> until = null,
            System.Action each = null)
        {
            Root.Input.SimUse = use;
            for (float t = 0f; t < seconds && (until == null || !until()); t += Time.deltaTime)
            {
                Root.Input.SimLook = look != null && Rig.look.Locked ? look() : Vector2.zero;
                each?.Invoke();
                yield return null;
                if (Root.Input.SimLook != Vector2.zero && !Rig.look.Locked) watch.Rebase();
                watch.Sample(phase);
            }
            Root.Input.SimUse = false;
            Root.Input.SimLook = Vector2.zero;
        }

        /// <summary>Rê chuột thành luống: qua lại theo ngang, nhích dần theo dọc, đổi hướng dọc mỗi 3 s (như người chơi cọ).</summary>
        static Vector2 Stroke() => new Vector2(Time.frameCount % 30 < 15 ? 12f : -12f, (Time.frameCount / 180) % 2 == 0 ? 2.5f : -2.5f);

        /// <summary>Ghi khung bàn chải dời quá 3 cm (truy nguồn giật): con trỏ, nhát chải, nhấc, trục.</summary>
        sealed class BrushTrace
        {
            readonly BrushScrub _scrub;
            readonly Transform _brush;
            Vector3 _last, _lastHead;
            Vector2 _lastCursor;
            float _lastStep, _lastStepShown;
            bool _primed;
            int _logged;
            public BrushTrace(BrushScrub scrub, Transform brush) { _scrub = scrub; _brush = brush; }
            public void Add()
            {
                var p = _brush.position;
                var m = _scrub.Motion;
                float step = (p - _last).magnitude;
                bool pop = _primed && step > MotionProbe.PopStep && step > _lastStep * 2.5f + 0.005f;
                _lastStep = _primed ? step : 0f;
                if (pop && _logged++ < 12)
                    Debug.Log($"BRUSH17_TRACE f{Time.frameCount}: mũi dời {step * 100f:F1} cm (trước {_lastStepShown * 100f:F1}), điểm cọ dời {(_scrub.HeadWorld - _lastHead).magnitude * 100f:F1} cm, " +
                              $"con trỏ {_lastCursor:F3}→{m.Cursor:F3}, nhát {m.Stroke:F3}, nhấc {m.Lift:F3}, hoạt {m.Activity:F2}, nghiêng {_scrub.Pose.Tilt:F0}°, " +
                              $"nắm {_scrub.Pose.LowAt:F2}, ok {_scrub.Pose.Ok}");
                _lastStepShown = step;
                _last = p; _lastHead = _scrub.HeadWorld; _lastCursor = m.Cursor; _primed = true;
            }
        }

        /// <summary>Vết đầu chải trên mặt nạ (hệ vật, kể cả khi nồi xoay): mỗi khung một điểm.</summary>
        sealed class HeadPath
        {
            public readonly List<Vector2> Uvs = new List<Vector2>();
            readonly BrushScrub _scrub;
            public HeadPath(BrushScrub scrub) => _scrub = scrub;
            public void Add()
            {
                if (_scrub.Motion.Pressing) Uvs.Add(_scrub.Surface.MaskUv(_scrub.Motion.Head));
            }
        }

        /// <summary>
        /// Đầu chải nằm trên mặt cọ (mũi lông cách điểm cọ không quá bán kính bó lông); vết bẩn mất đúng chỗ đầu chải đi qua: ô trong vệt
        /// chải sạch hơn hẳn, ô xa vệt chải (quá 2,5 nhát) vẫn bẩn.
        /// </summary>
        static void AssertBrushOnSurface(BrushScrub scrub, Transform brush, string label, float before, HeadPath path)
        {
            Assert.IsNotNull(brush, $"{label}: bàn chải đang đặt trên mặt cọ");
            float gap = Vector3.Distance(brush.position, scrub.HeadWorld);
            Assert.LessOrEqual(gap, scrub.Shape.HeadRadius + 0.01f, $"{label}: mũi lông cách điểm cọ {gap * 100f:F1} cm");
            var mask = scrub.Mask;
            Assert.Greater(path.Uvs.Count, 30, $"{label}: đầu chải ít khi chạm mặt");
            float foot = scrub.FootprintRadius * scrub.Surface.UvScale;
            float inSum = 0f, outSum = 0f;
            int inN = 0, outN = 0;
            for (int y = 0; y < mask.Size; y++)
                for (int x = 0; x < mask.Size; x++)
                {
                    if (!mask.IsValid(x, y)) continue;
                    var c = new Vector2((x + 0.5f) / mask.Size, (y + 0.5f) / mask.Size);
                    float near = float.MaxValue;
                    foreach (var p in path.Uvs) near = Mathf.Min(near, (p - c).sqrMagnitude);
                    near = Mathf.Sqrt(near);
                    if (near <= foot * 0.6f) { inSum += mask[x, y]; inN++; }
                    else if (near > foot * 2.5f) { outSum += mask[x, y]; outN++; }
                }
            float inMean = inN > 0 ? inSum / inN : 1f, outMean = outN > 0 ? outSum / outN : 1f;
            Debug.Log($"BRUSH17 {label}: sạch {before:P0} → {mask.Cleaned:P0}; trong vệt chải ({inN} ô) còn bẩn {inMean:F2}, xa vệt ({outN} ô) {outMean:F2}; " +
                      $"mũi lông cách điểm cọ {gap * 100f:F1} cm; {path.Uvs.Count} khung chạm mặt");
            Assert.Greater(mask.Cleaned, before + 0.03f, $"{label}: cọ mà mặt nạ bẩn không đổi");
            Assert.Less(inMean, 0.35f, $"{label}: chỗ đầu chải đi qua vẫn bẩn ({inMean:F2})");
            if (outN >= 20) Assert.Greater(outMean, 0.85f, $"{label}: vết bẩn mất cả chỗ đầu chải chưa tới ({outMean:F2})");
        }

        /// <summary>Phần ô mặt nạ (trong đĩa) mà đầu chải đặt tới được với tư thế đạt từ chỗ người chơi đang đứng.</summary>
        static readonly Dictionary<string, int> _why = new Dictionary<string, int>();

        static float ReachableShare(BrushScrub scrub)
        {
            _why.Clear();
            var mask = scrub.Mask;
            float domain = scrub.Surface.Domain;
            int ok = 0, all = 0;
            for (int y = 0; y < mask.Size; y += 2)
                for (int x = 0; x < mask.Size; x += 2)
                {
                    if (!mask.IsValid(x, y)) continue;
                    var c = (new Vector2((x + 0.5f) / mask.Size, (y + 0.5f) / mask.Size) - new Vector2(0.5f, 0.5f)) * (2f * domain);
                    all++;
                    // Lề sát mép không cọ tới vẫn được tính sạch nhờ bề ngang vết chải: dùng điểm kẹp vào vùng con trỏ đi được.
                    var pose = scrub.Solve(scrub.Surface.Clamp(c), Rig);
                    if (pose.Ok) { ok++; continue; }
                    // Chỗ không với tới: gần/xa người, đáy/thành, lý do (tầm, cán lún vành, gập cổ tay).
                    var local = scrub.Vessel.InverseTransformPoint(Rig.Position);
                    bool near = Vector2.Dot(c, new Vector2(local.x, local.z)) > 0f;
                    string key = $"{(near ? "gần" : "xa")}/{(c.magnitude > scrub.Surface.BottomRadius ? "thành" : "đáy")}/" +
                                 (pose.ReachExcess > 0.002f ? "tầm" : pose.Clash > 0.002f ? "lún" : "cổ tay");
                    _why.TryGetValue(key, out int n);
                    _why[key] = n + 1;
                }
            var sh = scrub.Vessel.InverseTransformPoint((Rig.body.Shoulder(false).position + Rig.body.Shoulder(true).position) * 0.5f);
            Debug.Log($"BRUSH17 tầm với: người (hệ nồi) {scrub.Vessel.InverseTransformPoint(Rig.Position):F2}, thân local {Rig.body.transform.localPosition:F2}, " +
                      $"đang làm {Rig.hands.Working}, nhìn {Rig.look.Yaw:F0}/{Rig.look.Pitch:F0}, xoay nồi {scrub.Vessel.eulerAngles:F0}; vai (hệ nồi) {sh:F2}, scale {scrub.Vessel.lossyScale:F2}, tay {Rig.body.ArmLength(false):F2} m; không tới: " +
                      string.Join(", ", _why.Select(kv => $"{kv.Key} {kv.Value}")));
            return all > 0 ? (float)ok / all : 0f;
        }

        static Transform DetachedBrush(Tool tool)
        {
            var prop = tool == Tool.Brush ? Rig.hands.brushProp : Rig.hands.longBrushProp;
            return prop != null && prop.transform.parent == Rig.transform ? prop.transform : null;
        }

        // ---------------- Chậu: bàn chải ngắn ----------------

        [UnityTest, Timeout(240000)]
        public IEnumerator ScrubbingTheBasinPotWithTheShortBrush()
        {
            yield return ChoreTestUtil.Galley();
            var d = Quiet();
            d.meals.BeginMeal(new HaiBanTay.Content.PhaseData { Id = "kiểm_chải_chậu", Dirty = 1 }, instant: true);
            yield return d.meals.Serve();
            var wash = d.wash;
            wash.brushHook.Interact(Rig.interactor);
            Assert.AreEqual(Tool.Brush, Rig.interactor.Tool, "cầm bàn chải ngắn từ giá");
            wash.OnInteract(wash.pile, Rig.interactor);
            wash.OnInteract(wash.basin, Rig.interactor); wash.OpenTap(); // nước chảy rồi mới cọ (QA-0132)
            Assert.IsTrue(wash.Logic.InBasin, "nồi nằm trong chậu");
            var basin = wash.basin.transform;
            ChoreTestUtil.Look(basin.TransformPoint(new Vector3(0f, 0f, WashStation.StandBack - 0.35f)), basin.position + Vector3.up * 0.68f);
            yield return new WaitForSeconds(0.4f);
            var watch = new Watch("cọ chậu bàn chải");
            yield return Run(watch, "trước khi cọ", 0.3f, use: false);
            var scrub = wash.Scrubbing;
            float before = scrub != null ? scrub.Mask.Cleaned : 0f;
            yield return Run(watch, "bắt đầu cọ", 0.6f, Stroke, until: () => DetachedBrush(Tool.Brush) != null);
            var brush = DetachedBrush(Tool.Brush);
            Assert.IsNotNull(brush, "bắt đầu cọ: bàn chải rời nắm tay đặt lên lòng nồi");
            scrub = wash.Scrubbing;
            before = scrub.Mask.Cleaned;
            watch.Track("bàn chải", brush);
            var path = new HeadPath(scrub);
            yield return Run(watch, "cọ", 4f, Stroke, each: path.Add);
            Root.Input.SimUse = true;
            yield return null;
            AssertBrushOnSurface(scrub, brush, "chậu", before, path);
            yield return Run(watch, "nghỉ cọ", 0.8f, use: false);
            Assert.AreEqual(Tool.Brush, Rig.interactor.Tool, "nghỉ cọ: bàn chải vẫn trong tay");
            watch.AssertClean();
        }

        // ---------------- Chảo đồng: bàn chải cán dài ----------------

        [UnityTest, Timeout(240000)]
        public IEnumerator ScrubbingTheCopperPanWithTheLongBrush()
        {
            yield return ChoreTestUtil.Galley();
            var d = Quiet();
            var pan = d.pan;
            pan.Activate();
            var spot = pan.stops[1].position;
            pan.pan.transform.position = spot;
            pan.pan.position = spot;
            typeof(PanStation).GetProperty("NeedsWash").GetSetMethod(true).Invoke(pan, new object[] { true });
            pan.Scrub.Reset();
            yield return null;
            pan.Scrubbing.Soil();
            pan.longBrushHook.Interact(Rig.interactor);
            Assert.AreEqual(Tool.LongBrush, Rig.interactor.Tool, "cầm bàn chải cán dài từ móc");
            // Đứng lùi khỏi chỗ chà 0,4 m, nhìn vào tay cầm chảo: giữ chuột thì Ba bước tới (tốc độ đi bộ).
            var away = (pan.longBrushHook.transform.position - spot);
            away.y = 0f;
            ChoreTestUtil.Look(spot + away.normalized * (PanStation.ScrubStand + 0.4f), pan.handle.transform.position);
            yield return new WaitForSeconds(0.4f);
            yield return PlayTestUtil.WaitUntil(() => Rig.interactor.IsLookingAt(pan.handle), 3f);
            var watch = new Watch("chà chảo cán dài");
            float before = pan.Scrubbing.Mask.Cleaned;
            yield return Run(watch, "bước tới chảo", 3f, Stroke, until: () => DetachedBrush(Tool.LongBrush) != null);
            var brush = DetachedBrush(Tool.LongBrush);
            Assert.IsNotNull(brush, "bắt đầu chà: bàn chải cán dài đặt vào lòng chảo");
            watch.Track("bàn chải cán dài", brush);
            var path = new HeadPath(pan.Scrubbing);
            yield return Run(watch, "chà", 4f, Stroke, each: path.Add);
            Root.Input.SimUse = true;
            yield return null;
            AssertBrushOnSurface(pan.Scrubbing, brush, "chảo", before, path);
            yield return Run(watch, "nghỉ chà", 0.8f, use: false);
            watch.AssertClean();
        }

        // ---------------- Nồi cao: kéo, nhấc, ghế đẩu ----------------

        static TallPotStation Tall(ShiftDirector d)
        {
            var tall = d.tall;
            Assert.IsNotNull(tall, "ShiftDirector.tall");
            tall.Activate();
            tall.ResetForDay();
            return tall;
        }

        static Vector3 Flat(Vector3 p, float y) => new Vector3(p.x, y, p.z);

        [UnityTest, Timeout(300000)]
        public IEnumerator TowingTheTallPotOutOfTheWashCorner()
        {
            yield return ChoreTestUtil.Galley();
            var d = Quiet();
            var tall = Tall(d);
            tall.Order(OrderKinds.TallOut, 1);
            var home = tall.potRoot.position;
            float y = Rig.Position.y;
            Vector3 Aim() => tall.potRoot.position + Vector3.up * 0.75f;
            ChoreTestUtil.Look(new Vector3(home.x, y, home.z + 0.85f), Aim());
            yield return new WaitForSeconds(0.4f);
            yield return PlayTestUtil.WaitUntil(() => Rig.interactor.IsLookingAt(tall.potTarget), 3f);
            var watch = new Watch("kéo nồi cao") { CheckTurn = false, Pot = tall.pot };
            watch.Track("nồi cao", tall.body);
            Root.Input.SimUse = true;
            yield return PlayTestUtil.WaitUntil(() => tall.Tow.Gripping, 5f);
            Assert.IsTrue(tall.Tow.Gripping, "bám vành nồi cao");
            var start = tall.potRoot.position;
            Vector3? lastBody = null;
            int traced = 0;
            // Đi lùi lên bắc ra cửa vách ngăn rồi sang tây (đường bot), nhìn theo nồi như người chơi.
            foreach (var goal in new[] { new Vector3(home.x, y, 0.3f), new Vector3(1.7f, y, 0.3f) })
            {
                for (float t = 0f; t < 20f && tall.Tow.Gripping; t += Time.deltaTime)
                {
                    var flat = goal - Rig.Position;
                    flat.y = 0f;
                    if (flat.magnitude < 0.12f) break;
                    var dir = Aim() - Rig.head.position;
                    Rig.look.SetAngles(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, -Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg);
                    flat.Normalize();
                    Root.Input.SimUse = true;
                    Root.Input.SimMove = new Vector2(Vector3.Dot(flat, Rig.transform.right), Vector3.Dot(flat, Rig.transform.forward));
                    yield return null;
                    watch.Sample("kéo");
                    var bp = tall.body.position;
                    if (lastBody.HasValue && (bp - lastBody.Value).magnitude > 0.03f && traced++ < 8)
                        Debug.Log($"BRUSH17_TRACE kéo f{Time.frameCount}: thân nồi dời {(bp - lastBody.Value).magnitude * 100f:F1} cm, gốc nồi {tall.potRoot.position:F3}, " +
                                  $"thân local {tall.body.localPosition:F3} {tall.body.localEulerAngles:F1}, người {Rig.Position:F3}, bám {tall.Tow.Gripping}");
                    lastBody = bp;
                }
            }
            Root.Input.SimMove = Vector2.zero;
            float moved = Vector3.Distance(Flat(start, 0f), Flat(tall.potRoot.position, 0f));
            Debug.Log($"BRUSH17 kéo nồi cao: nồi đi {moved:F2} m, trượt tay {tall.Tow.Slips} lần, bám {tall.Tow.Gripping}");
            Assert.IsTrue(tall.Tow.Gripping, "kéo hết đoạn mà không tuột tay");
            Assert.Greater(moved, 2f, "nồi cao theo người kéo qua cửa vách ngăn");
            Root.Input.SimUse = false;
            yield return Run(watch, "buông", 0.5f, use: false);
            watch.AssertClean();
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator LiftingTheTallPotOntoTheHob() => Lift(true);

        [UnityTest, Timeout(240000)]
        public IEnumerator LiftingTheTallPotDownFromTheHob() => Lift(false);

        IEnumerator Lift(bool up)
        {
            yield return ChoreTestUtil.Galley();
            var d = Quiet();
            var tall = Tall(d);
            if (up)
            {
                tall.Order(OrderKinds.TallOut, 1);
                tall.FastForwardTo(TallPotState.AtStoveClean);
            }
            else
            {
                tall.FastForwardTo(TallPotState.OnHobClean);
                tall.Served();
                tall.Order(OrderKinds.ScrubTall, 1);
            }
            var into = tall.hob.position - tall.stoveSpot.position;
            into.y = 0f;
            into.Normalize();
            // Đứng xa chỗ nhấc 0,5 m: bắt đầu nhấc thì Ba bước tới ở tốc độ đi bộ.
            var feet = Flat(tall.potRoot.position, Rig.Position.y) - into * (TallPotStation.LiftReach + 0.5f);
            ChoreTestUtil.Look(feet, tall.potRoot.position + Vector3.up * 0.7f);
            yield return new WaitForSeconds(0.4f);
            string label = up ? "nhấc nồi cao lên bếp" : "nhấc nồi cao xuống";
            var watch = new Watch(label) { Pot = tall.pot };
            watch.Track("nồi cao", tall.potRoot);
            yield return Run(watch, "trước khi nhấc", 0.3f, use: false);
            Assert.IsTrue(tall.CanInteract(tall.potTarget, Rig.interactor), $"{label}: nhấc được");
            tall.OnInteract(tall.potTarget, Rig.interactor);
            yield return Run(watch, "nhấc", 15f, use: false, until: () => !tall.Lifting);
            Assert.IsFalse(tall.Lifting, $"{label}: nhấc xong");
            Assert.AreEqual(up ? TallPotState.OnHobClean : TallPotState.AtStoveDirty, tall.Logic.State, label);
            var rest = up ? tall.hob.position : tall.stoveSpot.position;
            Assert.Less(Vector3.Distance(tall.potRoot.position, rest), 0.03f, $"{label}: nồi nằm đúng chỗ");
            // Buông tay rồi đứng yên quá thời gian thôi tư thế làm việc (1,2 s): người đứng trên sàn, vỏ va chạm không chồng lên nồi
            // (bot ngày 1 brush17b: lùi về chỗ đứng trước lúc nhấc = chỗ nồi vừa đặt, bị đẩy lên nóc nồi y 1,03).
            yield return Run(watch, "sau khi nhấc", 2.5f, use: false);
            var off = Rig.Position - tall.potRoot.position;
            off.y = 0f;
            Assert.Less(Rig.Position.y, 0.1f, $"{label}: người bị đẩy lên nóc nồi (y {Rig.Position.y:F2})");
            Assert.GreaterOrEqual(off.magnitude, TallPotStation.LiftClearStand - 0.02f, $"{label}: người đứng chồng lên nồi ({off.magnitude:F2} m)");
            Assert.IsFalse(Rig.InWorkStance, $"{label}: thôi tư thế làm việc");
            watch.AssertClean();
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator ScrubbingTheTallPotFromTheStool() => StoolScrub(false);

        /// <summary>Ngày 4: sóng làm nồi chao giữa lúc cọ, Jacques sang đứng phía bên kia giữ nồi; không chồng thân với ai.</summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator JacquesHoldsTheSwayingTallPotWhileBaScrubs() => StoolScrub(true);

        IEnumerator StoolScrub(bool sway)
        {
            yield return ChoreTestUtil.Galley();
            var d = sway ? Object.FindAnyObjectByType<ShiftDirector>() : Quiet();
            if (sway) d.meals.cook.enabled = false;
            var tall = Tall(d);
            tall.Order(OrderKinds.ScrubTall, 1);
            tall.FastForwardTo(TallPotState.HomeDirty);
            tall.Sway = sway;
            Assert.AreEqual(TallPotState.HomeDirty, tall.Logic.State);
            tall.longBrushHook.Interact(Rig.interactor);
            Assert.AreEqual(Tool.LongBrush, Rig.interactor.Tool, "cầm bàn chải cán dài từ móc");
            NpcOverlapProbe overlap = null;
            NpcAgent jacques = null;
            if (sway)
            {
                var cast = Object.FindAnyObjectByType<DayCast>();
                jacques = cast.jacques;
                jacques.gameObject.SetActive(true);
                jacques.Place(cast.jacquesTallPot.position, cast.jacquesTallPot.eulerAngles.y);
                overlap = new GameObject("Brush17Overlap").AddComponent<NpcOverlapProbe>();
                overlap.Watch(Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None));
                cast.StartCoroutine(cast.HoldTallPot());
            }
            var stool = tall.stool.transform.position;
            var toPot = tall.potRoot.position - stool;
            toPot.y = 0f;
            ChoreTestUtil.Look(Flat(stool, Rig.Position.y) - toPot.normalized * 0.45f, stool + Vector3.up * 0.2f);
            yield return new WaitForSeconds(0.4f);
            string label = sway ? "cọ nồi cao có sóng" : "cọ nồi cao trên ghế";
            var watch = new Watch(label);
            watch.Track("nồi cao", tall.body);
            Assert.IsTrue(tall.CanInteract(tall.stool, Rig.interactor), "bước lên ghế được");
            tall.OnInteract(tall.stool, Rig.interactor);
            yield return Run(watch, "bước lên ghế", 2f, use: false);
            Assert.IsTrue(tall.OnStool, "đứng trên ghế");
            // Người chơi tự nhìn vào lòng nồi (xoay chuột, chưa cọ): đổi mốc hướng nhìn.
            var aim = tall.potRoot.position + Vector3.up * 0.3f - Rig.head.position;
            Rig.look.SetAngles(Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg, -Mathf.Atan2(aim.y, new Vector2(aim.x, aim.z).magnitude) * Mathf.Rad2Deg);
            yield return null;
            watch.Rebase();
            yield return PlayTestUtil.WaitUntil(() => Rig.interactor.IsLookingAt(tall.potTarget), 3f);
            Assert.IsTrue(Rig.interactor.IsLookingAt(tall.potTarget), "nhìn vào nồi cao từ trên ghế");
            float before = tall.Scrubbing.Mask.Cleaned;
            yield return Run(watch, "bắt đầu cọ", 1f, Stroke, until: () => DetachedBrush(Tool.LongBrush) != null);
            var brush = DetachedBrush(Tool.LongBrush);
            Assert.IsNotNull(brush, "bàn chải cán dài vào lòng nồi cao");
            // Cọ xong cần sạch 90% mặt: đang cọ trên ghế (thân ở tư thế làm việc), đầu chải phải với tới (tư thế đạt) ít nhất ngần ấy
            // ô mặt nạ. Đo lúc tay đang làm: chưa làm mà cúi nhìn thì thân lùi 0,28 m (không thấy lòng thân), vai sai chỗ.
            float reach = ReachableShare(tall.Scrubbing);
            Debug.Log($"BRUSH17 {label}: từ ghế với tới {reach:P0} mặt cọ");
            Assert.GreaterOrEqual(reach, BrushScrub.CleanEnough + 0.02f, $"{label}: từ ghế chỉ với tới {reach:P0} mặt cọ, không cọ xong được");
            watch.Track("bàn chải cán dài", brush);
            if (!sway)
            {
                var path = new HeadPath(tall.Scrubbing);
                var trace = new BrushTrace(tall.Scrubbing, brush);
                yield return Run(watch, "cọ", 4f, Stroke, each: () => { path.Add(); trace.Add(); });
                Root.Input.SimUse = true;
                yield return null;
                AssertBrushOnSurface(tall.Scrubbing, brush, "nồi cao", before, path);
                yield return Run(watch, "nghỉ cọ", 0.6f, use: false);
            }
            else
            {
                float nextLog = 0f, waited = 0f;
                var cover = ScrubCoverage.For(() => tall.Scrubbing);
                yield return Run(watch, "cọ tới lúc sóng", 120f, cover, until: () => tall.SailorHelped || tall.Logic.Scrubbed, each: () =>
                {
                    waited += Time.deltaTime;
                    if (waited < nextLog) return;
                    nextLog += 10f;
                    Debug.Log($"BRUSH17 sóng t={waited:F0}s: tiến độ {tall.Scrubbing.Progress:P0}, Jacques giữ {tall.JacquesHolding}, " +
                              $"đang cọ {DetachedBrush(Tool.LongBrush) != null}, dụng cụ {Rig.interactor.Tool}, trạng thái {tall.Logic.State}");
                });
                Assert.IsTrue(tall.SailorHelped, $"sóng tới, Jacques giữ nồi (tiến độ {tall.Scrubbing.Progress:P0}, Jacques giữ {tall.JacquesHolding})");
                Assert.IsTrue(tall.JacquesHolding, "Jacques (không phải cánh tay giữ chỗ) giữ nồi");
                yield return Run(watch, "cọ tiếp khi Jacques giữ", 3f, cover, until: () => tall.Logic.Scrubbed);
                float toBa = Vector3.Distance(Flat(jacques.Position, 0f), Flat(Rig.Position, 0f));
                Debug.Log($"BRUSH17 Jacques: {overlap.Report}; cách Ba {toBa:F2} m");
                Assert.AreEqual(0f, overlap.Total, $"NPC không chồng thân: {overlap.Report}");
                Assert.Greater(toBa, 0.6f, "Jacques đứng phía bên kia nồi, không chồng lên Ba");
                Object.Destroy(overlap.gameObject);
            }
            watch.AssertClean();
        }
    }
}
