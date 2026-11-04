using System.Collections;
using System.Linq;
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
    /// Dò chỗ nắm, chỗ ngồi theo PlayerClipDetector (17/10: tay xuyên vật ở mọi nơi). Mỗi ứng viên chạy cả lượt việc với một bộ dò mới,
    /// in CLIP_FIT: lún sâu nhất theo loại (cánh tay ↔ sọt đang bê, bắp chân ↔ ghế, tay ↔ bàn) và cổ tay hở lớn nhất (tầm với).
    /// </summary>
    [Category("Screenshots"), Explicit]
    public sealed class ClipFit17
    {
        static GameRoot Root => GameRoot.Instance;
        static PlayerRig Rig => PlayerRig.Current;

        // Test dò tay: tay hiện như trước khi ẩn (17/10) để bộ dò đo cánh tay, bàn tay.
        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = 1f / 60f;
            PlayerRig.ArmsShownByDefault = true;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            PlayerRig.ArmsShownByDefault = false;
            foreach (var probe in Object.FindObjectsByType<PlayerClipProbe>(FindObjectsSortMode.None)) Object.Destroy(probe.gameObject);
            PlayTestUtil.ExitSim();
        }

        static string Worst(PlayerClipProbe probe, System.Func<PlayerClipTrack.Finding, bool> pick)
        {
            var list = probe.Track.Findings.Values.Where(pick).OrderByDescending(f => f.maxDepth).Take(3).ToList();
            return list.Count == 0 ? "0" : string.Join(", ", list.Select(f => $"{f.part}↔{f.obj} {f.maxDepth * 100f:F1}cm/{f.longest:F2}s"));
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator CoalBasketGrips()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            d.meals.cook.enabled = false;
            foreach (var npc in Object.FindObjectsByType<HaiBanTay.Npc.NpcAgent>(FindObjectsSortMode.None)) npc.gameObject.SetActive(false);
            var coal = d.coal;
            var basket = coal.baskets[0];
            var view = new PlayerViewProbe(Rig);
            // Chỗ ôm khi bê (builder: x 0,24, y 0,30); lúc đổ PourMotion trượt hai tay xuống hai bên đáy.
            float gx = 0.24f, gy = 0.3f;
            float tilt = PourMotion.TwoHandTilt;
            const bool toBase = true;
                {
                    basket.leftGrip.localPosition = new Vector3(-gx, gy, 0f);
                    basket.rightGrip.localPosition = new Vector3(gx, gy, 0f);
                    var who = Rig.interactor;
                    if (who.Holding != null) who.ReleaseHeld();
                    basket.gameObject.SetActive(true);
                    basket.Locked = false;
                    coal.SetStock(0);
                    // Sọt đầy lại cho ứng viên sau (SetLoad là việc nội bộ của trạm).
                    typeof(CoalStation).GetMethod("SetLoad", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(coal, new object[] { basket, true });
                    who.Pick(basket);
                    ChoreTestUtil.Look(new Vector3(0.3f, 0f, 1.45f), coal.bin.transform.position + Vector3.up * 0.5f);
                    yield return new WaitForSeconds(0.4f);
                    var probe = PlayerClipProbe.Attach();
                    float gap = 0f;
                    for (float t = 0f; t < 1f; t += Time.deltaTime) { yield return null; gap = Mathf.Max(gap, view.WristGap(true), view.WristGap(false)); }
                    coal.OnInteract(coal.bin, who);
                    bool began = false;
                    for (float t = 0f; t < 8f && !(began && !coal.PouringBasket); t += Time.deltaTime)
                    {
                        began |= coal.PouringBasket;
                        yield return null;
                        gap = Mathf.Max(gap, view.WristGap(true), view.WristGap(false));
                    }
                    Debug.Log($"CLIP_FIT sọt nghiêng {tilt:F0}° {(toBase ? "tay xuống đáy" : "tay ở vành")}: hết than {coal.IsEmptyBasket(basket)}; tay↔sọt {Worst(probe, f => f.kind == PlayerClipDetector.Kind.Arm && f.obj.Contains("CoalBasket"))}; " +
                              $"tay↔khác {Worst(probe, f => f.kind != PlayerClipDetector.Kind.Body && !f.obj.Contains("CoalBasket"))}; cổ tay hở {gap * 100f:F1} cm");
                    Object.Destroy(probe.gameObject);
                    yield return null;
                }
        }

        /// <summary>Độ xoay dọc cẳng tay (PlayerBody.TwistLeft/Right) ở từng việc: tìm tay cầm ngược cẳng (17/10 "Flip lại 180 độ").</summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator ForearmTwist()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            d.meals.cook.enabled = false;
            var body = Rig.body;
            IEnumerator Measure(string label, float seconds, string shot = null)
            {
                float lMin = 999f, lMax = -999f, rMin = 999f, rMax = -999f;
                for (float t = 0f; t < seconds; t += Time.deltaTime)
                {
                    yield return null;
                    lMin = Mathf.Min(lMin, body.TwistLeft); lMax = Mathf.Max(lMax, body.TwistLeft);
                    rMin = Mathf.Min(rMin, body.TwistRight); rMax = Mathf.Max(rMax, body.TwistRight);
                }
                Debug.Log($"TWIST {label}: trái {lMin:F0}…{lMax:F0}°, phải {rMin:F0}…{rMax:F0}°");
                if (shot != null)
                {
                    yield return ScreenshotCapture.Capture("twist17_" + shot + "_fp");
                    var hands = (Rig.hands.left.position + Rig.hands.right.position) * 0.5f;
                    yield return Detail("twist17_" + shot + "_right", Rig.transform.TransformPoint(new Vector3(0.75f, 1.2f, 0.5f)), hands);
                    yield return Detail("twist17_" + shot + "_left", Rig.transform.TransformPoint(new Vector3(-0.75f, 1.2f, 0.5f)), hands);
                }
            }
            yield return Measure("thả lỏng", 0.5f);
            // Bê nồi tới chậu rồi cọ.
            d.meals.BeginMeal(new HaiBanTay.Content.PhaseData { Id = "kiểm_xoay_rửa", Dirty = 1 });
            yield return d.meals.Serve();
            var wash = d.wash;
            var basin = wash.basin.transform;
            ChoreTestUtil.Look(basin.TransformPoint(new Vector3(0f, 0f, WashStation.StandBack - 0.35f)), basin.position + Vector3.up * 0.68f);
            wash.OnInteract(wash.pile, Rig.interactor);
            yield return Measure("bê nồi", 0.6f, "carry_pot");
            wash.OnInteract(wash.basin, Rig.interactor); wash.OpenTap(); // nước chảy rồi mới cọ (QA-0132)
            Root.Input.SimUse = true;
            Root.Input.SimLook = new Vector2(12f, 8f);
            yield return new WaitForSeconds(0.6f);
            yield return Measure("cọ nồi", 0.6f, "scrub");
            Root.Input.SimUse = false; Root.Input.SimLook = Vector2.zero;
            yield return new WaitForSeconds(0.5f);
            // Gọt khoai (bỏ phần ông Mai gọt mẫu).
            var peel = d.storyStations.OfType<PeelStation>().First(x => !x.asparagus);
            peel.OnOrder(1);
            var back = peel.stand + (peel.stand - peel.board.transform.position).WithY(0f).normalized * 0.6f;
            ChoreTestUtil.Look(back, peel.board.transform.position);
            peel.OnInteract(peel.board, Rig.interactor);
            yield return PlayTestUtil.WaitUntil(() => peel.Ready, 120f);
            Root.Input.SimUse = true;
            Root.Input.SimLook = new Vector2(0f, 12f);
            yield return new WaitForSeconds(0.5f);
            yield return Measure("gọt", 0.8f, "peel");
            Root.Input.SimUse = false; Root.Input.SimLook = Vector2.zero;
        }

        /// <summary>
        /// Lật bàn tay quanh trục ngón (17/10: "Tay bê nồi bạn làm ngược cẳng rồi. Flip lại 180 độ đi. Lúc gọt cũng thế"): bê nồi (dáng
        /// Carry) và tay trái cầm củ lúc gọt, mỗi góc lật một lượt; in độ xoay cẳng tay, chụp góc nhìn và hai bên.
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator FlipFit()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            d.meals.cook.enabled = false;
            var body = Rig.body;
            var hands = Rig.hands;
            int carry = System.Array.FindIndex(hands.targets, x => x.pose == HandPose.Carry);
            var baseCarry = hands.targets[carry];
            IEnumerator Measure(string label, float seconds, string shot)
            {
                float lMin = 999f, lMax = -999f, rMin = 999f, rMax = -999f;
                for (float t = 0f; t < seconds; t += Time.deltaTime)
                {
                    yield return null;
                    lMin = Mathf.Min(lMin, body.TwistLeft); lMax = Mathf.Max(lMax, body.TwistLeft);
                    rMin = Mathf.Min(rMin, body.TwistRight); rMax = Mathf.Max(rMax, body.TwistRight);
                }
                Debug.Log($"FLIP {label}: trái {lMin:F0}…{lMax:F0}°, phải {rMin:F0}…{rMax:F0}°");
                yield return ScreenshotCapture.Capture("flip17_" + shot + "_fp");
                var mid = (hands.left.position + hands.right.position) * 0.5f;
                yield return Detail("flip17_" + shot + "_right", Rig.transform.TransformPoint(new Vector3(0.75f, 1.2f, 0.5f)), mid);
                yield return Detail("flip17_" + shot + "_left", Rig.transform.TransformPoint(new Vector3(-0.75f, 1.2f, 0.5f)), mid);
            }
            // Bê nồi: dáng Carry lật quanh trục ngón (tay phải lật đối xứng).
            d.meals.BeginMeal(new HaiBanTay.Content.PhaseData { Id = "kiểm_lật_bê", Dirty = 1 });
            yield return d.meals.Serve();
            var wash = d.wash;
            var basin = wash.basin.transform;
            ChoreTestUtil.Look(basin.TransformPoint(new Vector3(0f, 0f, WashStation.StandBack - 0.35f)), basin.position + Vector3.up * 0.68f);
            wash.OnInteract(wash.pile, Rig.interactor);
            foreach (float roll in new[] { 0f, 90f, 180f, 270f })
            {
                var t = baseCarry;
                t.leftEuler = baseCarry.leftEuler + new Vector3(0f, 0f, roll);
                t.rightEuler = baseCarry.rightEuler - new Vector3(0f, 0f, roll);
                hands.targets[carry] = t;
                hands.SetPose(HandPose.Lowered);
                hands.SetPose(HandPose.Carry);
                yield return new WaitForSeconds(0.6f);
                yield return Measure($"bê nồi lật {roll:F0}°", 0.3f, $"carry_{roll:F0}");
            }
            hands.targets[carry] = baseCarry;
            wash.OnInteract(wash.basin, Rig.interactor); wash.OpenTap(); // nước chảy rồi mới cọ (QA-0132)
            yield return new WaitForSeconds(0.3f);
            // Gọt: tay trái cầm củ lật quanh trục ngón, giữ nguyên điểm lòng bàn tay chạm củ.
            var peel = d.storyStations.OfType<PeelStation>().First(x => !x.asparagus);
            peel.OnOrder(1);
            var back = peel.stand + (peel.stand - peel.board.transform.position).WithY(0f).normalized * 0.6f;
            ChoreTestUtil.Look(back, peel.board.transform.position);
            peel.OnInteract(peel.board, Rig.interactor);
            yield return PlayTestUtil.WaitUntil(() => peel.Ready, 120f);
            var potatoes = Object.FindAnyObjectByType<PotatoPeeling>();
            var grip = potatoes.leftGrip;
            var (basePos, baseRot) = (grip.localPosition, grip.localRotation);
            var contactLocal = new Vector3(0f, -0.01f, 0.07f);
            foreach (float roll in new[] { 0f, 90f, 180f, 270f })
            {
                var rot = baseRot * Quaternion.Euler(0f, 0f, roll);
                // Điểm chạm trong lòng bàn tay (hệ cha của grip) giữ nguyên khi lật.
                var contact = basePos + baseRot * contactLocal;
                grip.SetLocalPositionAndRotation(contact - rot * contactLocal, rot);
                Root.Input.SimUse = true;
                Root.Input.SimLook = new Vector2(0f, 12f);
                yield return new WaitForSeconds(0.5f);
                yield return Measure($"gọt tay trái lật {roll:F0}°", 0.5f, $"peel_{roll:F0}");
                Root.Input.SimUse = false; Root.Input.SimLook = Vector2.zero;
            }
            grip.SetLocalPositionAndRotation(basePos, baseRot);
        }

        /// <summary>Cầm thử từng loại đồ cầm được trong bếp: độ xoay cẳng tay (ngược cẳng) và xuyên vật, in HOLD_SWEEP.</summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator HoldEverything()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            d.meals.cook.enabled = false;
            var who = Rig.interactor;
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var h in Object.FindObjectsByType<Holdable>(FindObjectsInactive.Include, FindObjectsSortMode.None).OrderBy(x => x.name))
            {
                string kind = PlayerClipDetector.Generic(h.name);
                if (!seen.Add(kind)) continue;
                if (who.Holding != null) who.ReleaseHeld();
                yield return null;
                h.gameObject.SetActive(true);
                h.Locked = false;
                ChoreTestUtil.Look(new Vector3(0f, 0f, -1.2f), new Vector3(0f, 1.0f, 0.5f));
                who.Pick(h);
                if (who.Holding != h) { Debug.Log($"HOLD_SWEEP {kind}: không cầm được"); continue; }
                var probe = PlayerClipProbe.Attach();
                float worst = 0f; string arm = "";
                for (float t = 0f; t < 0.8f; t += Time.deltaTime)
                {
                    yield return null;
                    float r = PlayerClipDetector.Reversal(Rig.body, out string a);
                    if (r >= worst) { worst = r; arm = a; }
                }
                Debug.Log($"HOLD_SWEEP {kind} ({(h.twoHanded ? "hai tay" : "một tay")}{(h.hang ? ", xách" : "")}): xoay T {Rig.body.TwistLeft:F0}° P {Rig.body.TwistRight:F0}°, " +
                          $"ngược {(worst > 0f ? $"CÓ {arm}" : "không")}; xuyên {Worst(probe, f => f.Severity != "INFO")}");
                Object.Destroy(probe.gameObject);
                // Đồ vừa buông nằm ngay trước người: cất đi cho món sau không đo nhầm vào nó.
                who.ReleaseHeld();
                h.gameObject.SetActive(false);
            }
            if (who.Holding != null) who.ReleaseHeld();
        }

        /// <summary>Đổ xô tro: dò góc lật quanh trục ngón của mốc vành phải (PourRimR) cho cẳng tay phải không vặn ngược suốt lượt đổ.</summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator AshBucketRim()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            d.meals.cook.enabled = false;
            var shovel = GameObject.Find("HelperShovel");
            if (shovel != null)
                Debug.Log($"ASH_FIT xẻng phụ bếp ở {shovel.transform.position:F2} xoay {shovel.transform.eulerAngles:F0}, cha {shovel.transform.parent?.name}");
            foreach (var follow in Object.FindObjectsByType<HaiBanTay.Npc.NpcToolFollow>(FindObjectsSortMode.None))
                if (follow.tool != null) follow.tool.gameObject.SetActive(false);
            foreach (var npc in Object.FindObjectsByType<HaiBanTay.Npc.NpcAgent>(FindObjectsSortMode.None)) npc.gameObject.SetActive(false);
            var ash = d.ash;
            var marker = ash.bucket.transform.Find("PourRimR");
            var markerL = ash.bucket.transform.Find("PourRimL");
            foreach (float roll in new[] { 55f, 70f, 85f, 100f, 115f, 130f })
            {
                marker.localRotation = Quaternion.Euler(0f, 0f, roll);
                ash.bucket.gameObject.SetActive(true);
                ash.Rake.ForceFull();
                var who = Rig.interactor;
                if (who.Holding != null) who.ReleaseHeld();
                ash.bucket.Locked = false;
                who.Pick(ash.bucket);
                var chute = ash.chute.transform;
                var mouth = chute.TransformPoint(new Vector3(0.2f, 0.98f, 0f));
                var stand = mouth + new Vector3(1.0f, 0f, 0.15f);
                stand.y = chute.position.y;
                ChoreTestUtil.Look(stand, mouth);
                yield return new WaitForSeconds(0.4f);
                ash.OnInteract(ash.chute, who);
                float lMin = 999f, lMax = -999f, rMin = 999f, rMax = -999f, worst = 0f;
                string arm = "";
                bool began = false;
                for (float t = 0f; t < 8f && !(began && !ash.bucket.Busy); t += Time.deltaTime)
                {
                    began |= ash.bucket.Busy;
                    yield return null;
                    lMin = Mathf.Min(lMin, Rig.body.TwistLeft); lMax = Mathf.Max(lMax, Rig.body.TwistLeft);
                    rMin = Mathf.Min(rMin, Rig.body.TwistRight); rMax = Mathf.Max(rMax, Rig.body.TwistRight);
                    float r = PlayerClipDetector.Reversal(Rig.body, out string a);
                    if (r > worst) { worst = r; arm = a; }
                }
                Debug.Log($"ASH_FIT vành phải lật {roll:F0}° (trái {markerL.localEulerAngles.z:F0}°): trái {lMin:F0}…{lMax:F0}°, phải {rMin:F0}…{rMax:F0}°, ngược {worst:F0}° {arm}");
                yield return new WaitForSeconds(0.5f);
            }
        }

        /// <summary>Thùng cá: dò chỗ hai tay ôm hai vách (dọc thùng z, ra ngoài x) cho cẳng tay không lún vách thùng khi bưng.</summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator FishCrateGrips()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            d.meals.cook.enabled = false;
            foreach (var npc in Object.FindObjectsByType<HaiBanTay.Npc.NpcAgent>(FindObjectsSortMode.None)) npc.gameObject.SetActive(false);
            var crate = Object.FindObjectsByType<Holdable>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(h => h.name.EndsWith("_fish"));
            var view = new PlayerViewProbe(Rig);
            var who = Rig.interactor;
            foreach (float gy in new[] { 0.2f, 0.1f, 0.03f })
            foreach (float gz in new[] { -0.16f, -0.24f })
                foreach (float gx in new[] { 0.3f, 0.33f })
                {
                    crate.leftGrip.localPosition = new Vector3(-gx, gy, gz);
                    crate.rightGrip.localPosition = new Vector3(gx, gy, gz);
                    if (who.Holding != null) who.ReleaseHeld();
                    yield return null;
                    crate.gameObject.SetActive(true);
                    crate.Locked = false;
                    ChoreTestUtil.Look(new Vector3(0f, 0f, -1.2f), new Vector3(0f, 1.0f, 0.5f));
                    who.Pick(crate);
                    var probe = PlayerClipProbe.Attach();
                    float gap = 0f, worst = 0f;
                    for (float t = 0f; t < 1.2f; t += Time.deltaTime)
                    {
                        yield return null;
                        gap = Mathf.Max(gap, view.WristGap(true), view.WristGap(false));
                        worst = Mathf.Max(worst, PlayerClipDetector.Reversal(Rig.body, out _));
                    }
                    Debug.Log($"CRATE_FIT x {gx:F2} y {gy:F2} z {gz:F2}: tay↔thùng {Worst(probe, f => f.kind == PlayerClipDetector.Kind.Arm || f.kind == PlayerClipDetector.Kind.Hand || f.kind == PlayerClipDetector.Kind.Grip)}; " +
                              $"cổ tay hở {gap * 100f:F1} cm; ngược {worst:F0}°");
                    yield return Detail($"crate17_{gx * 100f:F0}_{gy * 100f:F0}_{-gz * 100f:F0}", Rig.cam.transform.position + Rig.transform.right * 0.7f + Vector3.down * 0.25f, crate.transform.position);
                    Object.Destroy(probe.gameObject);
                }
            if (who.Holding != null) who.ReleaseHeld();
        }

        /// <summary>Xẻng phụ bếp cắm dựng giữa hai lượt (17/10: cán chĩa vào hông Ba lúc đổ sọt): ảnh cạnh thùng, góc đứng đổ, dải ảnh lúc kéo cán về xúc.</summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator ShovelLean()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            var helper = Object.FindAnyObjectByType<HaiBanTay.Npc.HelperRoutine>();
            helper.enabled = false;
            yield return null;
            var bin = d.coal.bin.transform.position;
            var side = bin + new Vector3(1.6f, 1.3f, -0.5f);
            Debug.Log($"SHOVEL_LEAN gốc {helper.shovel.position:F2} cán {helper.shovel.forward:F2}");
            yield return Detail("shovel17_side", side, bin + Vector3.up * 0.6f);
            yield return Detail("shovel17_pour", new Vector3(0.3f, 1.62f, 1.45f), bin + Vector3.up * 0.4f);
            helper.enabled = true;
            helper.ScoopStand(out var stand, out _);
            for (float t = 0f; t < 25f && (helper.transform.position - stand).WithY(0f).magnitude > 0.2f; t += Time.deltaTime) yield return null;
            for (int i = 0; i < 10; i++)
            {
                yield return Detail($"shovel17_pick_{i}", side, bin + Vector3.up * 0.6f);
                yield return new WaitForSeconds(0.12f);
            }
        }

        /// <summary>Đổ xô tro theo từng khung: pha, độ xoay hai cẳng tay, góc xô; ảnh góc nhìn thứ nhất và cạnh ở các khung quanh chỗ vặn ngược.</summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator AshBucketTrace()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            d.meals.cook.enabled = false;
            foreach (var follow in Object.FindObjectsByType<HaiBanTay.Npc.NpcToolFollow>(FindObjectsSortMode.None))
                if (follow.tool != null) follow.tool.gameObject.SetActive(false);
            foreach (var npc in Object.FindObjectsByType<HaiBanTay.Npc.NpcAgent>(FindObjectsSortMode.None)) npc.gameObject.SetActive(false);
            var ash = d.ash;
            ash.bucket.gameObject.SetActive(true);
            ash.Rake.ForceFull();
            var who = Rig.interactor;
            who.Pick(ash.bucket);
            var chute = ash.chute.transform;
            var mouth = chute.TransformPoint(new Vector3(0.2f, 0.98f, 0f));
            var stand = mouth + new Vector3(1.0f, 0f, 0.15f);
            stand.y = chute.position.y;
            ChoreTestUtil.Look(stand, mouth);
            yield return new WaitForSeconds(0.4f);
            ash.OnInteract(ash.chute, who);
            var grip = Rig.hands.GripAnchor(false);
            for (int f = 0; f < 120 && (f < 3 || ash.bucket.Busy); f++)
            {
                yield return null;
                var up = ash.bucket.transform.up;
                Debug.Log($"ASH_TRACE khung {f}: T {Rig.body.TwistLeft:F0}° P {Rig.body.TwistRight:F0}°; xô lên {up:F2}; tay phải bám {(grip != null ? grip.name : "-")} " +
                          $"{(grip != null ? grip.rotation.eulerAngles.ToString("F0") : "")}");
                if (f >= 12 && f <= 24 && f % 3 == 0)
                {
                    yield return ScreenshotCapture.Capture($"ash17_fp_{f}");
                    yield return Detail($"ash17_side_{f}", Rig.transform.TransformPoint(new Vector3(1.2f, 1.3f, 0.5f)), Rig.transform.TransformPoint(new Vector3(0.2f, 1.0f, 0.4f)));
                }
            }
        }

        /// <summary>Tay ẩn (17/10): ảnh góc nhìn thứ nhất (nhìn thẳng, cúi 50°) và cạnh thân Ba khi cầm chổi, ôm sọt than; rồi bật tay lại để so.</summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator ArmsHidden()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            d.meals.cook.enabled = false;
            foreach (var npc in Object.FindObjectsByType<HaiBanTay.Npc.NpcAgent>(FindObjectsSortMode.None)) npc.gameObject.SetActive(false);
            var who = Rig.interactor;
            ChoreTestUtil.Look(new Vector3(0f, 0f, -1.2f), new Vector3(0f, 0.3f, 0.6f));
            Rig.ArmsShown = false;
            who.TakeTool(Tool.Broom);
            Rig.hands.Snap();
            for (int i = 0; i < 10; i++) yield return null;
            foreach (float pitch in new[] { 50f, 75f })
            {
                Rig.look.SetAngles(Rig.look.Yaw, pitch);
                for (int i = 0; i < 5; i++) yield return null;
                yield return ScreenshotCapture.Capture($"arms17_fp{pitch:F0}");
            }
            Rig.look.SetAngles(Rig.look.Yaw, 0f);
            yield return Detail("arms17_front", Rig.transform.TransformPoint(new Vector3(0.2f, 1.5f, 1.4f)), Rig.transform.TransformPoint(new Vector3(0f, 1.2f, 0f)));
            who.ReleaseTool();
            yield return null;
            foreach (bool shown in new[] { false, true })
            {
                Rig.ArmsShown = shown;
                string tag = shown ? "shown" : "hidden";
                who.TakeTool(Tool.Broom);
                Rig.hands.Snap();
                for (int i = 0; i < 20; i++) yield return null;
                foreach (float pitch in new[] { 0f, 50f })
                {
                    Rig.look.SetAngles(Rig.look.Yaw, pitch);
                    for (int i = 0; i < 5; i++) yield return null;
                    yield return ScreenshotCapture.Capture($"arms17_{tag}_broom_fp{pitch:F0}");
                    var cam = Rig.cam.transform;
                    var near = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                        .Where(r => r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy && r.bounds.size.magnitude < 3f
                            && (r.bounds.ClosestPoint(cam.position) - cam.position).magnitude < 0.9f)
                        .Select(r => $"{(r.transform.parent != null ? r.transform.parent.name + "/" : "")}{r.name}[{r.GetType().Name},{LayerMask.LayerToName(r.gameObject.layer)}]");
                    var anim = Rig.body.animator;
                    var lu = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                    var ll = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                    Debug.Log($"ARMS_BONES {tag} {pitch:F0}: bắp tay trái scale {lu.localScale.x:F4}, cẳng tay trái {ll.position:F2} lossy {ll.lossyScale.x:F4}, " +
                              $"bounds thân {Rig.body.skin.bounds.size:F2}");
                    yield return Detail($"arms17_{tag}_broom_eye{pitch:F0}", cam.position, cam.position + cam.forward);
                }
                var drawn = Rig.hands.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !r.forceRenderingOff && r.isVisible)
                    .Select(r => r.transform.parent != null ? r.transform.parent.name + "/" + r.name : r.name);
                Debug.Log($"ARMS_HIDDEN {tag}: mesh dưới tay còn vẽ: {string.Join(", ", drawn)}");
                Rig.look.SetAngles(Rig.look.Yaw, 0f);
                yield return Detail($"arms17_{tag}_broom_side", Rig.transform.TransformPoint(new Vector3(1.1f, 1.4f, 1.2f)),
                    Rig.transform.TransformPoint(new Vector3(0f, 1.1f, 0.1f)));
                yield return Detail($"arms17_{tag}_broom_back", Rig.transform.TransformPoint(new Vector3(-0.6f, 1.9f, -1.3f)),
                    Rig.transform.TransformPoint(new Vector3(0f, 1.2f, 0.2f)));
                who.ReleaseTool();
                yield return null;
            }
            Rig.ArmsShown = false;
        }

        static IEnumerator Detail(string name, Vector3 eye, Vector3 target)
        {
            var go = new GameObject("ClipFitDetail");
            var camera = go.AddComponent<Camera>();
            camera.CopyFrom(Rig.cam); camera.enabled = false; camera.fieldOfView = 50f; camera.nearClipPlane = 0.03f;
            var source = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(Rig.cam);
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(camera);
            data.renderPostProcessing = source.renderPostProcessing; data.volumeLayerMask = source.volumeLayerMask; data.volumeTrigger = Rig.cam.transform;
            go.transform.position = eye; go.transform.LookAt(target);
            yield return ScreenshotCapture.CaptureCamera(camera, name);
            Object.Destroy(go);
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator SoupSeat()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            var soup = d.storyStations.OfType<VoyageEncounter>().First(x => x.Kind == OrderKinds.Soup);
            var sit = Rig.GetComponent<SitController>();
            var hips = Rig.body.animator.GetBoneTransform(HumanBodyBones.Hips);
            var knee = Rig.body.animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            var foot = Rig.body.animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            foreach (var (fwd, up) in new[] { (0f, 0f), (0.12f, 0.06f), (0.19f, 0.06f), (0.19f, 0.11f), (0.24f, 0.11f) })
            {
                float back = -fwd;
                sit.SitForStudy(soup.seat, soup.seatLook.position, soup.seatClip);
                yield return new WaitForSeconds(1f);
                Rig.transform.position += Rig.transform.forward * fwd + Vector3.up * up;
                yield return new WaitForSeconds(0.3f);
                var probe = PlayerClipProbe.Attach();
                yield return new WaitForSeconds(1f);
                var seat = soup.seat;
                Debug.Log($"CLIP_FIT ghế tới {fwd:F2} lên {up:F2}: mắt {seat.InverseTransformPoint(Rig.head.position):F2} thân lùi {Rig.body.BodyBack:F2}; chân↔ghế {Worst(probe, f => f.kind == PlayerClipDetector.Kind.Body)}; tay↔ {Worst(probe, f => f.kind != PlayerClipDetector.Kind.Body)}; " +
                          $"hông {seat.InverseTransformPoint(hips.position):F2} gối {seat.InverseTransformPoint(knee.position):F2} bàn chân {seat.InverseTransformPoint(foot.position):F2} (hệ ghế, yaw rig {Rig.transform.eulerAngles.y:F0}, ghế {seat.eulerAngles.y:F0})");
                Object.Destroy(probe.gameObject);
                sit.Stand();
                yield return new WaitForSeconds(0.3f);
            }
        }
    }
}
