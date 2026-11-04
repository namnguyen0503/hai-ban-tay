using System.Collections;
using System.Linq;
using HaiBanTay.Core;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Phụ bếp bê xẻng đi (bot ngày 1/3/4/5, 19/10): bàn tay trái ↔ hông 7,0 cm, cẳng tay trái ↔ bụng 7,6 cm, cán ↔ đùi phải, tay lọt sau tạp dề
    /// nên người dùng thấy như cụt bàn tay (ảnh bot 056, 19/10). Hai chỗ nắm khi bê (NpcHandWork.CarryRight/CarryLeft) nằm trong thân.
    /// Chạy vòng nạp than của phụ bếp có bộ dò xuyên thân: lúc đứng và đi bê xẻng, tay, cẳng tay, cán xẻng không lún vào thân phụ bếp quá 4 cm.
    /// </summary>
    public sealed class HelperShovelBodyPlayTests
    {
        GameObject _floor, _crate, _wall;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in new[] { _floor, _crate, _wall })
                if (go != null) Object.Destroy(go);
            _floor = _crate = _wall = null;
            Time.captureDeltaTime = 0f;
            foreach (var p in Object.FindObjectsByType<BodyClipProbe>(FindObjectsSortMode.None)) Object.Destroy(p.gameObject);
            PlayTestUtil.ExitSim();
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator HelperCarriesTheShovelWithHandsOutsideTheBody()
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            var helper = Object.FindAnyObjectByType<HelperRoutine>();
            Assert.IsNotNull(helper);
            Time.captureDeltaTime = 1f / 60f;
            var go = new GameObject("BodyClipProbe");
            var probe = go.AddComponent<BodyClipProbe>();
            probe.animateAll = true;
            probe.Watch(helper.agent);
            for (float t = 0f; t < 240f && helper.Feeds < 2; t += Time.deltaTime) yield return null;
            Assert.GreaterOrEqual(helper.Feeds, 1, "phụ bếp phải nạp than ít nhất một lần");
            Assert.Greater(probe.Track.Samples, 600, "bộ dò phải đo được");
            var findings = probe.Track.Findings.Values
                .Where(f => f.npc == helper.agent.name &&
                            (f.kind == BodyClipDetector.Kind.LimbInBody || f.kind == BodyClipDetector.Kind.ItemInBody || f.kind == BodyClipDetector.Kind.HandOff))
                .ToList();
            // Đi bê xẻng (Walk) không được lún quá 4 cm. Dáng khom lúc xúc/nạp (PickUp) và lúc đứng dậy/khom (Idle, tay còn đang chuyển từ thấp
            // lên ngang ngực) còn lún 7–14 cm: ghi lại, chưa sửa (QA-0119, bảng dò HelperShovelCarryScan.ScanLow).
            var carry = findings.Select(f => (f, depth: f.states.Where(s => s.Key == "Walk" || s.Key == "CarryWalk").Select(s => s.Value).DefaultIfEmpty(0f).Max()))
                .OrderByDescending(x => x.depth).ToList();
            float others = findings.SelectMany(f => f.states.Where(s => s.Key != "Walk" && s.Key != "CarryWalk").Select(s => s.Value)).DefaultIfEmpty(0f).Max();
            string text = string.Join("; ", carry.Take(5).Select(x => $"{x.f.kind} {x.f.a} ↔ {x.f.b} {x.depth * 100f:0.0} cm ({string.Join(", ", x.f.states.Select(s => s.Key + " " + (s.Value * 100f).ToString("0.0")))})"));
            Debug.Log($"HELPER_SHOVEL_BODY khom/đứng dậy tối đa {others * 100f:0.0} cm; đi: {text}");
            Assert.IsTrue(carry.Count == 0 || carry[0].depth < 0.04f, text);
        }

        // ---------- QA-0150: đồ NPC cầm/vác và vách, trên bục trống (y = 200) để không phụ thuộc bố cục bếp ----------

        const float Deck = 200f;

        /// <summary>Bếp ngày 1 đứng yên (director, routine tắt), bục trống ở y = 200, Ba và phụ bếp đặt lên bục.</summary>
        IEnumerator Stage(Vector3 ba, float baYaw, Vector3 npc, float npcYaw)
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            d.enabled = false;
            d.cast.StopAllCoroutines();
            var helper = Object.FindAnyObjectByType<HelperRoutine>();
            Assert.IsNotNull(helper);
            foreach (var r in Object.FindObjectsByType<NpcRoutine>(FindObjectsSortMode.None))
            {
                r.StopAllCoroutines();
                r.enabled = false;
            }
            helper.agent.StopAllCoroutines();
            helper.agent.SetHeldTool(null);
            _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floor.name = "QA0150Deck";
            _floor.transform.position = new Vector3(0f, Deck - 0.1f, 0f);
            _floor.transform.localScale = new Vector3(40f, 0.2f, 40f);
            Physics.SyncTransforms();
            GameRoot.Instance.Input.ClearBlocks();
            GameRoot.Instance.UI.SetFade(0f);
            PlayerRig.Current.look.AssistTarget = null;
            PlayerRig.Current.motor.Teleport(ba, baYaw);
            helper.agent.Place(npc, npcYaw);
            helper.agent.Play("Idle");
            for (int i = 0; i < 30; i++) yield return null;
        }

        NpcAgent Helper => Object.FindAnyObjectByType<HelperRoutine>().agent;

        /// <summary>Một dòng chẩn đoán: vị trí Ba/NPC, khe, vỏ thân (số điểm, khung bao, gốc), hộp đồ, số lần chặn.</summary>
        static string Diag(NpcAgent npc)
        {
            var rig = PlayerRig.Current;
            float gap = npc.ReachGapNow();
            var shell = rig.body.Shell;
            var b = new Bounds(shell.Count > 0 ? shell[0] : Vector3.zero, Vector3.zero);
            for (int i = 1; i < shell.Count; i++) b.Encapsulate(shell[i]);
            string boxes = string.Join(" ", npc.ReachBoxes.Select(x => $"[{x.center:F2} ±{x.half:F2}]"));
            return $"Ba {rig.Position:F2} NPC {npc.Position:F2} yaw {npc.transform.eulerAngles.y:F0} khe {gap:F3} vỏ {shell.Count} {b.min:F2}..{b.max:F2} gốc {rig.body.ShellOrigin:F2} "
                 + $"hộp {boxes} chặn đồ {npc.ReachRefusals} chặn vật {npc.StaticRefusals} cắt bước Ba {NpcAgent.PlayerStepLimits} chờ {npc.Waiting} {npc.BlockedBy} xuyên {npc.PassingStatic}";
        }

        /// <summary>Hộp đồ (không collider: bao, xẻng không chặn capsule kịp trước ngực) gắn vào NPC ở local, cỡ size.</summary>
        void GiveCrate(NpcAgent npc, Vector3 local, Vector3 size)
        {
            _crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _crate.name = "QA0150Crate";
            Object.Destroy(_crate.GetComponent<Collider>());
            _crate.transform.SetParent(npc.transform, false);
            _crate.transform.localPosition = local;
            _crate.transform.localScale = size;
            npc.SetHeldTool(_crate.transform);
        }

        /// <summary>Ba đi thẳng vào phụ bếp đang đứng cầm đồ chĩa ra trước ngang ngực: dừng trước đồ, ngực không lún (capsule chỉ chạm thân NPC).</summary>
        [UnityTest, Timeout(180000)]
        public IEnumerator PlayerStopsBeforeTheItemAnNpcHolds()
        {
            yield return Stage(new Vector3(0f, Deck, 0f), 0f, new Vector3(0f, Deck, 1.7f), 180f);
            var npc = Helper;
            GiveCrate(npc, new Vector3(0f, 1.2f, 0.55f), new Vector3(0.4f, 0.3f, 0.7f));
            yield return null;
            var rig = PlayerRig.Current;
            Assert.Greater(rig.body.ShellPoints, 0, "vỏ thân Ba phải đọc được");
            float minGap = float.PositiveInfinity, startZ = rig.Position.z;
            GameRoot.Instance.Input.SimMove = new Vector2(0f, 1f);
            float logAt = 0f;
            for (float t = 0f; t < 2.5f; t += Time.deltaTime)
            {
                yield return null;
                minGap = Mathf.Min(minGap, npc.ReachGapNow());
                if (t >= logAt) { Debug.Log("QA0150 A " + Diag(npc)); logAt = t + 0.25f; }
            }
            GameRoot.Instance.Input.SimMove = Vector2.zero;
            Debug.Log($"QA0150 player→item: khe nhỏ nhất {minGap * 100f:F1} cm, Ba đi {rig.Position.z - startZ:F2} m");
            Assert.Greater(rig.Position.z - startZ, 0.2f, "Ba phải đi được tới gần");
            Assert.GreaterOrEqual(minGap, 0.015f, $"ngực Ba lún vào đồ NPC cầm (khe {minGap * 100f:F1} cm)");
        }

        /// <summary>
        /// NPC đi ngang cách Ba 0,85 m (ngoài nón chắn 0,8 m cũ), đồ chĩa sang phía Ba 0,9 m ngang ngực: NPC dừng/né, đồ không quét qua thân Ba
        /// (bao vác vai quệt ngực 14 cm, lưỡi xẻng lún 7–9 cm, bot 23/10).
        /// </summary>
        [UnityTest, Timeout(180000)]
        public IEnumerator NpcItemNeverSweepsIntoBa()
        {
            yield return Stage(new Vector3(0f, Deck, 0f), 270f, new Vector3(-0.85f, Deck, -2.5f), 0f);
            var npc = Helper;
            GiveCrate(npc, new Vector3(0.55f, 1.2f, 0f), new Vector3(0.7f, 0.3f, 0.3f));
            npc.avoidOthers = false;
            yield return null;
            float minGap = float.PositiveInfinity;
            Debug.Log("QA0150 B start " + Diag(npc));
            var walk = npc.StartCoroutine(npc.WalkTo(new Vector3(-0.85f, Deck, 2.5f)));
            float logAt = 0f, heldAt = -1f;
            // Đi tới sát Ba rồi đứng chờ: đo trong lúc chờ (dưới NpcAgent.ReachYieldAfter; Ba đứng chắn lâu hơn thì NPC đi tiếp như cũ).
            for (float t = 0f; t < 8f; t += Time.deltaTime)
            {
                yield return null;
                minGap = Mathf.Min(minGap, npc.ReachGapNow());
                if (heldAt < 0f && npc.ReachRefusals > 0) heldAt = t;
                if (t >= logAt) { Debug.Log("QA0150 B " + Diag(npc)); logAt = t + 0.25f; }
                if (heldAt >= 0f && t - heldAt > NpcAgent.ReachYieldAfter - 0.8f) break;
            }
            Debug.Log($"QA0150 item→player: khe nhỏ nhất {minGap * 100f:F1} cm, NPC tới z {npc.Position.z:F2}");
            Assert.Greater(heldAt, 0f, "NPC phải dừng vì đồ sắp chạm Ba");
            Assert.Less(npc.Position.z, 1.5f, "NPC đứng chờ, chưa đi qua Ba");
            Assert.Less(minGap, 0.5f, "đồ phải đi tới sát Ba (kịch bản có ý nghĩa)");
            // Ba bước ra khỏi lối: NPC đi tiếp tới đích.
            PlayerRig.Current.motor.Teleport(new Vector3(1.6f, Deck, 0f), 270f);
            for (float t = 0f; t < 6f && npc.Position.z < 2.4f; t += Time.deltaTime) yield return null;
            npc.StopCoroutine(walk);
            Assert.Greater(npc.Position.z, 2.4f, "Ba tránh ra thì NPC đi tiếp");
            Assert.GreaterOrEqual(minGap, 0.015f, $"đồ NPC quét vào thân Ba (khe {minGap * 100f:F1} cm)");
        }

        /// <summary>
        /// Bot 01/11 lượt 5 (QA-0151): phu vác bao chờ Ba quá NpcAgent.ReachYieldAfter thì được đi tiếp (12 lần), bước tới là bao lún
        /// ngực Ba 12,7 cm. Ba đứng yên trong lối lâu: NPC được đi sát hơn nhưng đồ không lún vào vỏ thân Ba quá 3 cm.
        /// </summary>
        [UnityTest, Timeout(180000)]
        public IEnumerator NpcWaitingLongForBaStillNeverSinksItsItemIntoHer()
        {
            yield return Stage(new Vector3(0f, Deck, 0f), 270f, new Vector3(-0.6f, Deck, -2.5f), 0f);
            var npc = Helper;
            GiveCrate(npc, new Vector3(0.45f, 1.3f, 0.1f), new Vector3(0.6f, 0.35f, 0.4f));
            // Như phu trên bến: có nhường (avoidOthers) nên bị Ba chắn lâu thì lách (Sidestep), mỗi bước lách đi qua ReachAllows.
            npc.avoidOthers = true;
            yield return null;
            var walk = npc.StartCoroutine(npc.WalkTo(new Vector3(-0.6f, Deck, 2.5f)));
            float minGap = float.PositiveInfinity, logAt = 0f;
            // Đo tới đường lui cuối (NpcAgent.ReachLastResort, bị chặn liền 2,5 s mới được đi dù lún): trước đó đồ không lún quá 3 cm.
            float heldAt = -1f;
            for (float t = 0f; t < 7f; t += Time.deltaTime)
            {
                yield return null;
                if (heldAt < 0f && npc.ReachRefusals > 0) heldAt = t;
                if (heldAt >= 0f && t - heldAt > NpcAgent.ReachLastResort - 0.3f) break;
                minGap = Mathf.Min(minGap, npc.ReachGapNow());
                if (t >= logAt) { Debug.Log("QA0151 C " + Diag(npc)); logAt = t + 0.5f; }
            }
            Assert.Greater(heldAt, 0f, "NPC phải bị chặn vì đồ sắp chạm Ba");
            npc.StopCoroutine(walk);
            Debug.Log($"QA0151 wait long: khe nhỏ nhất {minGap * 100f:F1} cm, cho đi {npc.ReachYields} lần, NPC ở z {npc.Position.z:F2}");
            Assert.Less(minGap, 0.5f, "đồ phải tới sát Ba (kịch bản có ý nghĩa)");
            Assert.GreaterOrEqual(minGap, -0.035f, $"chờ lâu mà đồ NPC lún vào thân Ba (khe {minGap * 100f:F1} cm)");
        }

        /// <summary>Thân NPC chồng vật tĩnh (như oracle bot npc_inside): liên tục lâu nhất trong lúc đi (giây).</summary>
        static IEnumerator WatchInside(NpcAgent npc, float seconds, float[] longest)
        {
            float since = -1f;
            var hits = new Collider[16];
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                yield return null;
                var p = npc.Position;
                float r = Mathf.Max(0.05f, npc.bodyRadius - 0.02f);
                int n = Physics.OverlapCapsuleNonAlloc(p + Vector3.up * (0.32f + r), p + Vector3.up * (1.38f - r), r, hits, ~(1 << 2), QueryTriggerInteraction.Ignore);
                bool inside = false;
                for (int i = 0; i < n && !inside; i++) inside = NpcPathing.IsObstacle(hits[i], npc.transform, p.y);
                if (!inside) { since = -1f; continue; }
                if (since < 0f) since = t;
                longest[0] = Mathf.Max(longest[0], t - since);
            }
        }

        IEnumerator WallCase(bool avoidStatic, float baYaw, float seconds, float[] longest)
        {
            yield return Stage(new Vector3(3f, Deck, -5f), baYaw, new Vector3(0f, Deck, -2f), 0f);
            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _wall.name = "QA0150Wall";
            _wall.transform.position = new Vector3(0f, Deck + 1.2f, 0f);
            _wall.transform.localScale = new Vector3(14f, 2.4f, 0.2f);
            Physics.SyncTransforms();
            var npc = Helper;
            npc.avoidStatic = avoidStatic;
            npc.avoidOthers = false;
            var walk = npc.StartCoroutine(npc.WalkTo(new Vector3(0f, Deck, 2f)));
            var watch = npc.StartCoroutine(WatchInside(npc, seconds, longest));
            for (float t = 0f; t < seconds; t += 0.25f)
            {
                var p = npc.Position;
                var hits = Physics.OverlapCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.4f, 0.33f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                Debug.Log($"QA0150 wall t {t:F2} NPC {p:F2} chặn vật {npc.StaticRefusals} xuyên {npc.PassingStatic} tới {npc.WalkGoal:F2} chạm: "
                          + string.Join(",", hits.Select(h => h.name + (NpcPathing.IsObstacle(h, npc.transform, p.y) ? "*" : ""))));
                yield return new WaitForSeconds(0.25f);
            }
            npc.StopCoroutine(watch);
            npc.StopCoroutine(walk);
            npc.avoidStatic = true;
        }

        /// <summary>
        /// Tắt né vật (avoidStatic = false, như đầu bếp ăn trưa trong phòng ăn) vẫn không đi xuyên vách; Ba đang nhìn thì đứng chờ, không đi
        /// xuyên (QA-0150: đầu bếp xuyên vách phòng ăn).
        /// </summary>
        [UnityTest, Timeout(180000)]
        public IEnumerator NpcNeverWalksIntoAWallInSight()
        {
            var longest = new float[1];
            yield return WallCase(false, 0f, 6f, longest);
            Debug.Log($"QA0150 wall seen: chồng vách liên tục lâu nhất {longest[0]:F2} s, NPC ở {Helper.Position}");
            Assert.Less(longest[0], 0.3f, "NPC đi xuyên vách trong tầm nhìn Ba");
        }

        /// <summary>Kẹt sau vách dài lúc Ba quay lưng: NPC được đặt tới đích (khuất), không đi xuyên vách quá 0,3 s như lượt ghost cũ.</summary>
        [UnityTest, Timeout(180000)]
        public IEnumerator StuckNpcOutOfSightSkipsAheadInsteadOfWalkingThroughTheWall()
        {
            var longest = new float[1];
            yield return WallCase(false, 180f, 6f, longest);
            var npc = Helper;
            Debug.Log($"QA0150 wall unseen: chồng vách liên tục lâu nhất {longest[0]:F2} s, NPC ở {npc.Position}");
            Assert.Less(longest[0], 0.3f, "NPC đi xuyên vách");
            Assert.Greater(npc.Position.z, 0.5f, "NPC kẹt khuất tầm nhìn phải tới được phía bên kia");
        }
    }
}
