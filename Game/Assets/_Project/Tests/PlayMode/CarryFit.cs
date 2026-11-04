using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HaiBanTay.Core;
using HaiBanTay.Npc;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Dò chỗ đặt đồ theo tay, vai (NpcAgent.BagBelowPalm, SackOverShoulder, SackTilt, LoadBetweenPalms) bằng bộ dò xuyên thân, với
    /// NpcAgent tự đặt đồ và kéo tay áp vào đồ như khi chơi: ba phu (PorterVN_A/B/C) vác bao đi tại chỗ (ShoulderCarry), một khách
    /// xách va li (BagWalk), một phu bê bao trước bụng (CarryWalk). Mỗi ứng viên chạy một vòng bước (72 khung 60 Hz) từ đầu clip;
    /// điểm = lún vào đầu/cổ, bao không tựa vai (hổng hay lún quá 3 cm), tay rời đồ, tay lún vào đồ, đồ lún vào chỗ khác của thân;
    /// lấy điểm NPC tệ nhất. In 12 ứng viên tốt nhất (CARRY_FIT) và độ cao hai tay theo thời gian clip PickUp (CARRY_FIT nhặt).
    /// </summary>
    [Category("Screenshots"), Explicit]
    public sealed class CarryFit
    {
        [TearDown] public void TearDown() { Time.captureDeltaTime = 0f; PlayTestUtil.ExitSim(); }

        enum Mode { Sack, Bag, Belly }

        [DefaultExecutionOrder(14400)]
        sealed class Driver : MonoBehaviour
        {
            public readonly Dictionary<Mode, List<NpcAgent>> npcs = new Dictionary<Mode, List<NpcAgent>>
            {
                { Mode.Sack, new List<NpcAgent>() }, { Mode.Bag, new List<NpcAgent>() }, { Mode.Belly, new List<NpcAgent>() },
            };
            public Mode mode;
            public readonly BodyClipDetector detector = new BodyClipDetector { raw = true };
            readonly List<BodyClipDetector.Contact> _contacts = new List<BodyClipDetector.Contact>();
            // Mỗi NPC: lún đầu/cổ lớn nhất, tựa vai lớn nhất, tựa vai nhỏ nhất, tay rời lớn nhất, tay lún lớn nhất, đồ lún chỗ khác.
            public readonly Dictionary<NpcAgent, float[]> stats = new Dictionary<NpcAgent, float[]>();

            public void Clear()
            {
                stats.Clear();
                foreach (var n in npcs[mode]) stats[n] = new[] { 0f, 0f, float.MaxValue, 0f, 0f, 0f };
            }

            void LateUpdate()
            {
                var list = npcs[mode];
                _contacts.Clear();
                detector.Sample(list, _contacts);
                foreach (var npc in list)
                {
                    if (!stats.TryGetValue(npc, out var s)) continue;
                    float head = 0f, support = 0f, gap = 0f, handIn = 0f, other = 0f;
                    foreach (var c in _contacts)
                    {
                        if (c.npc != npc || c.other != null) continue;
                        bool hand = c.a == "bàn tay phải" || (mode == Mode.Belly && c.a == "bàn tay trái");
                        if (c.kind == BodyClipDetector.Kind.HandOff && hand) gap = Mathf.Max(gap, c.depth);
                        else if (c.kind == BodyClipDetector.Kind.HandInItem && hand) handIn = Mathf.Max(handIn, c.depth);
                        else if (c.kind == BodyClipDetector.Kind.ItemInBody)
                        {
                            if (mode == Mode.Sack && (c.a == "đầu" || c.a == "cổ")) head = Mathf.Max(head, c.depth);
                            else if (mode == Mode.Sack && (c.a == "vai phải" || c.a == "bắp tay phải" || c.a == "ngực trên")) support = Mathf.Max(support, c.depth);
                            else other = Mathf.Max(other, c.depth);
                        }
                    }
                    s[0] = Mathf.Max(s[0], head);
                    s[1] = Mathf.Max(s[1], support);
                    s[2] = Mathf.Min(s[2], support);
                    s[3] = Mathf.Max(s[3], gap);
                    s[4] = Mathf.Max(s[4], handIn);
                    s[5] = Mathf.Max(s[5], other);
                }
            }

            /// <summary>Điểm (nhỏ là tốt) của NPC tệ nhất.</summary>
            public float Score(out string detail)
            {
                float worst = 0f;
                detail = "";
                foreach (var npc in npcs[mode])
                {
                    if (!stats.TryGetValue(npc, out var s)) continue;
                    float score = 4f * s[5] + Mathf.Max(0f, s[3] - 0.02f) + Mathf.Max(0f, s[4] - 0.05f);
                    if (mode == Mode.Sack) score += 4f * s[0] + 1.5f * Mathf.Max(0f, s[1] - 0.03f) + 3f * Mathf.Max(0f, 0.005f - s[2]);
                    detail += $" {npc.name}: đầu {s[0] * 100f:F1} tựa {s[2] * 100f:F1}–{s[1] * 100f:F1} tay rời {s[3] * 100f:F1} tay lún {s[4] * 100f:F1} khác {s[5] * 100f:F1};";
                    worst = Mathf.Max(worst, score);
                }
                return worst;
            }
        }

        static void Prepare(NpcAgent npc, Vector3 at)
        {
            npc.keepAnimated = true;
            npc.SetAnimated(true);
            npc.GetComponentInChildren<Animator>().cullingMode = AnimatorCullingMode.AlwaysAnimate;
            npc.Place(at, 0f);
        }

        static void Restart(IEnumerable<NpcAgent> npcs, string state = null)
        {
            foreach (var npc in npcs)
            {
                string s = state ?? npc.carryWalkState;
                npc.Play(s, 0f);
                npc.GetComponentInChildren<Animator>().Play(s, 0, 0f);
            }
        }

        static IEnumerator Cycle(Driver driver, List<(float score, string text)> results, string label)
        {
            Restart(driver.npcs[driver.mode]);
            yield return null;
            driver.Clear();
            for (int f = 0; f < 72; f++) yield return null;
            float score = driver.Score(out var detail);
            results.Add((score, $"{label}: điểm {score * 100f:F2};{detail}"));
        }

        [UnityTest, Timeout(3600000)]
        public IEnumerator ShoulderSackAndBag()
        {
            Time.captureDeltaTime = 1f / 60f;
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[0]);
            yield return null;
            var routines = Object.FindObjectsByType<CrowdRoutine>(FindObjectsSortMode.None);
            foreach (var r in routines) r.enabled = false;
            var driver = new GameObject("CarryFitDriver").AddComponent<Driver>();
            var origin = new Vector3(-0.98f, 0f, -6.15f);
            var porters = routines.Where(x => x.agent.carryWalkState == "ShoulderCarry" && x.prop != null).ToList();
            foreach (var role in new[] { "PorterVN_A", "PorterVN_B", "PorterVN_C" })
            {
                var r = porters.FirstOrDefault(x => x.agent.name.Contains(role));
                if (r == null) continue;
                porters.Remove(r);
                Prepare(r.agent, origin + new Vector3(3f * driver.npcs[Mode.Sack].Count, 0f, 0f));
                r.prop.gameObject.SetActive(true);
                r.agent.PickUp(r.prop);
                driver.npcs[Mode.Sack].Add(r.agent);
            }
            var bag = routines.Select(x => x.agent).FirstOrDefault(a => a.carryWalkState == "BagWalk" && a.Carrying != null);
            if (bag != null)
            {
                Prepare(bag, origin + new Vector3(0f, 0f, 4f));
                driver.npcs[Mode.Bag].Add(bag);
            }
            var belly = porters.First();
            porters.Remove(belly);
            belly.agent.carryWalkState = "CarryWalk";
            belly.agent.carryIdleState = "HoldCarry";
            Prepare(belly.agent, origin + new Vector3(3f, 0f, 4f));
            belly.prop.gameObject.SetActive(true);
            belly.agent.PickUp(belly.prop);
            driver.npcs[Mode.Belly].Add(belly.agent);
            Assert.AreEqual(3, driver.npcs[Mode.Sack].Count, "thiếu phu vác bao");
            Assert.IsNotNull(bag, "thiếu khách xách va li");

            // Hai tay theo thời gian clip PickUp (phu không bê gì).
            var picker = porters.First().agent;
            Prepare(picker, origin + new Vector3(6f, 0f, 4f));
            var animator = picker.GetComponentInChildren<Animator>();
            var lh = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var rh = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Restart(new[] { picker }, "PickUp");
            var curve = new System.Text.StringBuilder("CARRY_FIT nhặt (giây: cao tay so với chân, m):");
            float lowT = 0f, low = float.MaxValue;
            for (float t = 0f; t < 1.3f; t += Time.deltaTime)
            {
                yield return null;
                float h = (lh.position.y + rh.position.y) * 0.5f - picker.transform.position.y;
                if (h < low) { low = h; lowT = t; }
                curve.Append($" {t:F2}:{h:F2}");
            }
            Debug.Log(curve + $"\nthấp nhất {low:F2} m ở {lowT:F2} s ({lowT / 1.3f:F2} của 1,3 s)");

            var results = new List<(float score, string text)>();
            driver.mode = Mode.Sack;
            foreach (float x in new[] { 0.10f, 0.14f, 0.18f, 0.22f })
            foreach (float y in new[] { 0.06f, 0.09f, 0.12f, 0.15f })
            foreach (float z in new[] { -0.18f, -0.12f, -0.06f })
            foreach (float pitch in new[] { 78f, 90f })
            foreach (float roll in new[] { -10f, 0f, 10f })
            {
                foreach (var n in driver.npcs[Mode.Sack])
                {
                    n.sackOffset = new Vector3(x, y, z);
                    n.sackTilt = new Vector3(pitch, 0f, roll);
                }
                yield return Cycle(driver, results, $"bao x={x:F2} y={y:F2} z={z:F2} nghiêng {pitch:F0}/{roll:F0}");
            }
            Debug.Log("CARRY_FIT bao:\n" + string.Join("\n", results.OrderBy(r => r.score).Take(12).Select(r => r.text)));

            results.Clear();
            driver.mode = Mode.Bag;
            foreach (float x in new[] { 0.04f, 0.06f, 0.08f })
            foreach (float y in new[] { -0.16f, -0.17f, -0.19f })
            {
                bag.bagOffset = new Vector3(x, y, 0f);
                yield return Cycle(driver, results, $"va li x={x:F2} y={y:F2}");
            }
            Debug.Log("CARRY_FIT va li:\n" + string.Join("\n", results.OrderBy(r => r.score).Take(9).Select(r => r.text)));

            results.Clear();
            driver.mode = Mode.Belly;
            foreach (float y in new[] { -0.05f, 0f, 0.05f, 0.10f, 0.15f })
            foreach (float z in new[] { -0.05f, 0f, 0.05f, 0.10f })
            {
                belly.agent.loadOffset = new Vector3(0f, y, z);
                yield return Cycle(driver, results, $"bụng y={y:F2} z={z:F2}");
            }
            Debug.Log("CARRY_FIT bụng:\n" + string.Join("\n", results.OrderBy(r => r.score).Take(12).Select(r => r.text)));
        }
    }
}
