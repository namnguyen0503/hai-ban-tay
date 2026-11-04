using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Narrative;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Ship;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// 01_NhaRong sau M10c (đi bộ thật, 60 khung/giây game): vác bao khoai từ xe qua đám đông, cầu thang tàu, boong, thang hầm
    /// tới chỗ xếp; không NPC nào chắn lối quá 3 giây; nhổ neo thu cầu thang tàu và dời bến; người đứng trên mặt đất; Sim chạy
    /// hết cảnh vào Ngày 1.
    /// </summary>
    public class NhaRongPlayTests
    {
        /// <summary>Vác bao 1,3 m/s; cho dư gấp ba lần quãng đường.</summary>
        const float SlowestSpeed = 1.3f;
        /// <summary>NPC không được chắn lối bắt buộc lâu hơn mức này (giây): NpcAgent nhường người chơi sau 0,8 s.</summary>
        const float MaxBlockedSeconds = 3f;

        static GameInput Input => GameRoot.Instance.Input;
        static PlayerRig Rig => PlayerRig.Current;
        string _failure;
        float _longestStuck;

        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = 1f / 60f;
            _failure = null;
            _longestStuck = 0f;
        }

        [TearDown]
        public void TearDown()
        {
            var root = GameRoot.Instance;
            if (root != null)
            {
                root.Input.SimMove = Vector2.zero;
                root.Input.SimLook = Vector2.zero;
            }
            SceneFlow.FromPrologue = false;
            Time.captureDeltaTime = 0f;
            PlayTestUtil.ExitSim();
        }

        /// <summary>Nạp 01 như khi vào từ prologue (bỏ thẻ và beat giơ tay) và chờ tới B1.3, lúc người chơi tự do đi lại.</summary>
        static IEnumerator LoadQuay()
        {
            PlayTestUtil.Root().ResetProgress();
            // Fixture vận chuyển: tiếp tục từ checkpoint đã được nhận việc; B0 được kiểm riêng qua bot và Sim cả cảnh.
            PlayTestUtil.Root().Events.Raise(EventIds.S1JobAccepted);
            SceneFlow.FromPrologue = true;
            SceneManager.LoadScene(SceneFlow.GameScenes[0]);
            yield return null;
            yield return PlayTestUtil.WaitUntil(() => Object.FindAnyObjectByType<NhaRongDirector>() is NhaRongDirector d && d.Beat == "B1.3"
                && Input.GameplayEnabled, 30f);
            Assert.IsTrue(Input.GameplayEnabled, "người chơi phải điều khiển được ở B1.3");
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>
        /// Đi thẳng tới điểm (mặt phẳng ngang). Đứng gần như yên quá MaxBlockedSeconds (NPC hay vật chắn) hoặc quá giờ thì ghi lỗi;
        /// ghi lại lần đứng yên lâu nhất.
        /// </summary>
        IEnumerator WalkTo(Vector3 target, string label, float radius = 0.25f)
        {
            if (_failure != null)
                yield break;
            float limit = Flat(target - Rig.Position).magnitude / SlowestSpeed * 3f + 4f;
            float time = 0f, stuck = 0f;
            var last = Rig.Position;
            while (Flat(target - Rig.Position).magnitude > radius)
            {
                var to = Flat(target - Rig.Position);
                Rig.look.SetAngles(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 0f);
                Input.SimMove = new Vector2(0f, 1f);
                yield return null;
                time += Time.deltaTime;
                stuck = Vector3.Distance(Rig.Position, last) < 0.01f ? stuck + Time.deltaTime : 0f;
                _longestStuck = Mathf.Max(_longestStuck, stuck);
                last = Rig.Position;
                if (stuck > MaxBlockedSeconds || time > limit)
                {
                    _failure = $"{label}: kẹt/chậm ở {Rig.Position:F2}, cần tới {target:F2} (đứng yên {stuck:F1} s); chạm: {Touching()}";
                    break;
                }
            }
            Input.SimMove = Vector2.zero;
        }

        /// <summary>Collider đang chạm capsule người chơi (nới thêm 5 cm), để báo lỗi kẹt rõ chỗ.</summary>
        static string Touching()
        {
            var cc = Rig.GetComponent<CharacterController>();
            var center = Rig.transform.TransformPoint(cc.center);
            float half = cc.height * 0.5f - cc.radius;
            var hits = Physics.OverlapCapsule(center + Vector3.up * half, center - Vector3.up * half, cc.radius + cc.skinWidth + 0.05f,
                ~0, QueryTriggerInteraction.Ignore);
            return string.Join(", ", hits.Where(h => h != cc).Select(h => $"{h.transform.parent?.name}/{h.name} @{h.ClosestPoint(center):F2}"));
        }

        /// <summary>Nhìn vào một điểm rồi chờ prompt của đúng vật cần.</summary>
        static IEnumerator Aim(Vector3 point, System.Func<bool> hit, float seconds = 5f)
        {
            var head = Rig.Position + Vector3.up * 1.62f;
            var to = point - head;
            Rig.look.SetAngles(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -Mathf.Atan2(to.y, Flat(to).magnitude) * Mathf.Rad2Deg);
            yield return PlayTestUtil.WaitUntil(hit, seconds);
        }

        /// <summary>Từ chỗ xuất hiện: nhấc bao, vác theo lối bắt buộc tới hầm thực phẩm và xếp xuống.</summary>
        IEnumerator CarryOneSack(NhaRongDirector d, Holdable sack)
        {
            var who = Rig.interactor;
            // Đứng cách bao ~1,2 m phía bắc, nhìn xuống bao.
            yield return WalkTo(sack.transform.position + new Vector3(0f, 0f, 1.3f) - Vector3.up * 0.3f, "tới bao khoai");
            yield return Aim(sack.transform.position, () => ReferenceEquals(who.Current, sack));
            if (_failure == null && !ReferenceEquals(who.Current, sack))
                _failure = $"không nhắm trúng bao, đang trúng {(who.LookCollider != null ? who.LookCollider.name : "không gì")}";
            if (_failure != null)
                yield break;
            Input.SimulateInteract();
            yield return PlayTestUtil.WaitUntil(() => who.Holding == sack, 5f);
            Assert.AreEqual(sack, who.Holding, "nhấc được bao");

            var layout = Object.FindAnyObjectByType<ShipLayout>();
            var holdStair = layout.stairs.GetComponentsInChildren<ShipStair>().Single(s => s.BottomWorld.y < -2.7f && s.TopWorld.y > -0.5f);
            var route = new List<Vector3>
            {
                new Vector3(0.9f, -4.1f, -15.7f), new Vector3(-1.5f, -4.1f, -14.2f), new Vector3(-1.5f, -4.1f, -13.0f),
                new Vector3(-1.5f, 0f, -7.38f), new Vector3(-1.5f, 0f, -5f), new Vector3(-1.5f, 0f, -3.0f), new Vector3(-9.4f, 0f, -2.9f), new Vector3(-9.514f, 0f, -2.2f),
                new Vector3(-9.514f, 0f, 0f),
                holdStair.LaneTopWorld + Vector3.left * 0.4f, holdStair.LaneBottomWorld, new Vector3(-2.9f, -2.8f, 1.1f),
            };
            for (int i = 0; i < route.Count; i++)
            {
                yield return WalkTo(route[i], "lối vác bao #" + i);
                if (_failure == null && who.Holding != sack)
                    _failure = $"bao tuột khỏi tay ở chặng {i}";
                if (_failure != null)
                    yield break;
            }
            Assert.IsTrue(layout.holdZone.PlayerInside, "đã xuống tới hầm thực phẩm");
            int before = d.stack.Count;
            yield return Aim(d.stack.transform.position, () => ReferenceEquals(who.Current, d.stack));
            if (!ReferenceEquals(who.Current, d.stack))
                _failure = $"không nhắm trúng chỗ xếp bao, đang trúng {(who.LookCollider != null ? who.LookCollider.name : "không gì")}";
            if (_failure != null)
                yield break;
            Input.SimulateInteract();
            yield return PlayTestUtil.WaitUntil(() => d.stack.Count > before, 5f);
            Assert.AreEqual(before + 1, d.stack.Count, "xếp được bao xuống hầm");
            Assert.IsNull(who.Holding);
        }

        [UnityTest]
        public IEnumerator PlayerCarriesTwoSacksFromCartToHoldThroughTheCrowd()
        {
            yield return LoadQuay();
            var d = Object.FindAnyObjectByType<NhaRongDirector>();
            Assert.AreEqual(0, d.SacksDelivered);
            for (int i = 0; i < d.sackTarget; i++)
            {
                yield return CarryOneSack(d, d.sacks[i]);
                Assert.IsNull(_failure, _failure);
                if (i + 1 < d.sackTarget)
                {
                    // Quay lại kè lấy bao tiếp: ra khỏi hầm, lên thang, xuống cầu thang tàu.
                    var layout = Object.FindAnyObjectByType<ShipLayout>();
                    var holdStair = layout.stairs.GetComponentsInChildren<ShipStair>().Single(s => s.BottomWorld.y < -2.7f && s.TopWorld.y > -0.5f);
                    foreach (var p in new[]
                    {
                        holdStair.LaneBottomWorld, holdStair.LaneTopWorld + Vector3.left * 0.5f, new Vector3(-9.514f, 0f, 0f),
                        new Vector3(-9.514f, 0f, -2.2f), new Vector3(-9.4f, 0f, -2.9f), new Vector3(-1.5f, 0f, -3.0f),
                        new Vector3(-1.5f, 0f, -5f), new Vector3(-1.5f, 0f, -7.9f),
                        new Vector3(-1.5f, -4.1f, -13.0f), new Vector3(0.9f, -4.1f, -15.7f),
                    })
                        yield return WalkTo(p, "quay lại lấy bao");
                    Assert.IsNull(_failure, _failure);
                }
            }
            Assert.AreEqual(d.sackTarget, d.SacksDelivered);
            Assert.AreEqual(0, d.sacks.Sum(s => s.GetComponent<ReturnToStart>().Returns), "bao không rơi xuống sông");
            Assert.Less(_longestStuck, MaxBlockedSeconds, "đám đông không chắn lối quá 3 giây");
            TestContext.WriteLine($"Đứng yên lâu nhất trên lối vác bao: {_longestStuck:F2} s");
            // Đủ bao: tàu nhổ neo (người chơi đang ở trong hầm, tức là trên tàu).
            yield return PlayTestUtil.WaitUntil(() => d.Departed, 10f);
            Assert.IsTrue(d.Departed, "đủ bao thì nhổ neo");
        }

        [UnityTest]
        public IEnumerator PeopleStandOnTheGroundAndNobodyBlocksTheStartingLane()
        {
            yield return LoadQuay();
            yield return new WaitForSeconds(2f);
            var off = new List<string>();
            foreach (var a in Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None))
            {
                if (!a.isActiveAndEnabled || !a.BodyActive)
                    continue;
                var hits = Physics.RaycastAll(a.Position + Vector3.up * 0.6f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore)
                    .Where(h => h.collider.GetComponentInParent<NpcAgent>() == null && h.collider.GetComponentInParent<PlayerRig>() == null)
                    .OrderBy(h => h.distance).ToArray();
                if (hits.Length == 0 || Mathf.Abs(hits[0].point.y - a.Position.y) > 0.35f)
                    off.Add($"{a.name} @ {a.Position:F2} mặt đất {(hits.Length > 0 ? hits[0].point.y.ToString("F2") : "không có")}");
            }
            Assert.IsEmpty(off, "người lơ lửng hoặc chìm: " + string.Join("; ", off));
        }

        /// <summary>Hàng khách: người đầu hàng trình vé, lên cầu thang tàu theo làn giữa, khuất vào khoang rồi hiện lại cuối hàng.</summary>
        [UnityTest]
        public IEnumerator QueuePassengersBoardAndReturnToTheTail()
        {
            yield return LoadQuay();
            Time.timeScale = 6f;
            var queue = Object.FindObjectsByType<CrowdQueue>(FindObjectsSortMode.None).Single();
            var members = queue.members.Select(m => m.agent).ToArray();
            Assert.AreEqual(9, members.Length);
            bool sawHidden = false, sawOnRamp = false;
            yield return PlayTestUtil.WaitUntil(() =>
            {
                sawHidden |= members.Any(m => !m.BodyActive);
                sawOnRamp |= members.Any(m => m.BodyActive && m.Position.y > -3.0f);
                return queue.Served >= 2 && members.All(m => m.BodyActive) && sawHidden && sawOnRamp;
            }, 120f);
            Time.timeScale = 1f;
            Assert.GreaterOrEqual(queue.Served, 2, "hàng tiến lên theo thời gian");
            Assert.IsTrue(sawOnRamp, "có khách đi lên cầu thang tàu (độ cao tăng theo dốc)");
            Assert.IsTrue(sawHidden, "khách khuất vào khoang sau khi lên tàu");
        }

        /// <summary>Nhổ neo: còi, cầu thang tàu thu lên, tháo dây, bến lùi xa cùng người; người đi lại dừng, người vẫy tay ở lại.</summary>
        [UnityTest]
        public IEnumerator DepartureRetractsGangwayUntiesRopesAndMovesTheQuayAway()
        {
            yield return LoadQuay();
            var d = Object.FindAnyObjectByType<NhaRongDirector>();
            var ship = Object.FindAnyObjectByType<ShipLayout>();
            Assert.IsTrue(d.gangway.activeInHierarchy);
            Assert.IsFalse(d.gangwayBarrier.activeSelf);
            Assert.IsTrue(d.ropes.Length >= 4 && d.ropes.All(r => r.activeInHierarchy), "dây buộc tàu");
            // Sim: bao xếp thẳng xuống hầm, người chơi được đưa lên boong.
            PlayTestUtil.EnterSim(1f);
            yield return PlayTestUtil.WaitUntil(() => d.Departed, 60f);
            Assert.IsTrue(d.Departed);
            Assert.IsTrue(d.gangwayBarrier.activeSelf, "dây chắn đóng lối xuống cầu thang tàu");
            Assert.AreEqual(2, d.SacksDelivered);
            var start = d.backdrop.transform.localPosition;
            yield return PlayTestUtil.WaitUntil(() => !d.gangway.activeSelf, 10f);
            Assert.IsFalse(d.gangway.activeSelf, "cầu thang tàu thu lên rồi ẩn");
            Assert.IsTrue(d.ropes.All(r => !r.activeSelf), "đã tháo dây");
            Assert.IsTrue(d.backdrop.Moving);
            Assert.AreEqual(0.15f, ship.motion.Intensity, 1e-3f, "tàu lắc nhẹ 0,15");
            yield return new WaitForSeconds(6f);
            Assert.Greater(Vector3.Distance(start, d.backdrop.transform.localPosition), 0.3f, "bến lùi xa");
            // Người đi lại trên bến dừng lại (không đuổi theo điểm đã dời); người đứng/vẫy vẫn chạy.
            var routines = d.quayCrowd.GetComponentsInChildren<CrowdRoutine>(true);
            Assert.IsTrue(routines.Any(r => r.mode == CrowdMode.Pose && r.agent.isActiveAndEnabled), "người vẫy tay tiễn ở lại");
            foreach (var r in routines.Where(r => r.mode == CrowdMode.Loop && r.agent.BodyActive))
                Assert.IsFalse(r.agent.Walking, $"{r.name} đã dừng đi lại");
        }

        /// <summary>Sim chạy cả cảnh 01 (vào từ Chọn chương: thẻ, giơ tay, bao khoai, nhổ neo) rồi sang Ngày 1 trong bếp.</summary>
        [UnityTest]
        public IEnumerator SimRunsWholeSceneIntoDayOne()
        {
            var root = PlayTestUtil.Root();
            root.ResetProgress();
            SaveSystem.AllowInSim = true;
            var beats = new List<string>();
            PlayTestUtil.EnterSim(20f);
            SceneFlow.FromPrologue = false;
            SceneManager.LoadScene(SceneFlow.GameScenes[0]);
            string last = null;
            bool day1 = false;
            float end = Time.realtimeSinceStartup + 150f;
            while (Time.realtimeSinceStartup < end && !day1)
            {
                var cur = SceneDirector.Current;
                string beat = cur != null ? SceneManager.GetActiveScene().name + ":" + cur.Beat : null;
                if (beat != null && beat != last)
                    beats.Add(beat);
                last = beat;
                day1 = beat != null && beat.StartsWith("02_Galley:D1");
                yield return null;
            }
            string all = string.Join(", ", beats);
            Assert.IsTrue(day1, "phải tới Ngày 1. Beat: " + all);
            foreach (var b in new[] { "B1.1", "B1.2", "B0", "B0b", "B0c", "B0d", "B1.3", "B1.4", "B1.5" })
                Assert.IsTrue(beats.Contains("01_NhaRong:" + b), $"thiếu {b}: {all}");
        }
    }
}
