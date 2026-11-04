using System.Collections;
using System.Linq;
using HaiBanTay.Core;
using HaiBanTay.Npc;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Dò chỗ nắm khi bê xẻng (NpcHandWork.CarryRight/CarryLeft): dời cả hai chỗ nắm theo lưới, đo lún thân và khoảng vai–tay trái. Không kết luận;
    /// chọn hằng số theo bảng in ra (SHOVEL_SCAN).
    /// </summary>
    [Category("Screenshots"), Explicit]
    public sealed class HelperShovelCarryScan
    {
        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            foreach (var p in Object.FindObjectsByType<BodyClipProbe>(FindObjectsSortMode.None)) Object.Destroy(p.gameObject);
            PlayTestUtil.ExitSim();
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator Scan()
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            var helper = Object.FindAnyObjectByType<HelperRoutine>();
            helper.StopAllCoroutines();
            helper.enabled = false;
            helper.agent.StopAllCoroutines();
            var hands = helper.GetComponent<NpcHandWork>();
            var animator = helper.agent.GetComponentInChildren<Animator>();
            var shoulder = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var shoulderR = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Time.captureDeltaTime = 1f / 60f;
            yield return hands.PickShovel(helper.shovel, helper.shovelGripLeft, helper.shovelGripRight);
            yield return hands.Scoop(helper.shovel, helper.shovelLoad);
            var baseR = new Vector3(0.18f, 0.91f, -0.067f);
            var baseL = new Vector3(-0.05f, 0.99f, 0.15f);
            var carryR = NpcHandWork.CarryRight;
            var carryL = NpcHandWork.CarryLeft;
            var table = new System.Text.StringBuilder("SHOVEL_SCAN\n");
            foreach (float dz in new[] { 0.09f })
            foreach (float dy in new[] { 0.12f })
            foreach (float dx in new[] { 0.05f })
            {
                NpcHandWork.CarryRight = baseR + new Vector3(dx, dy, dz);
                NpcHandWork.CarryLeft = baseL + new Vector3(dx, dy, dz);
                yield return hands.CarryShovel(helper.shovel, 0.3f);
                var go = new GameObject("BodyClipProbe");
                var probe = go.AddComponent<BodyClipProbe>();
                probe.animateAll = true;
                probe.Watch(helper.agent);
                helper.agent.Play("Idle");
                for (float t = 0f; t < 1.2f; t += Time.deltaTime) yield return null;
                var found = probe.Track.Findings.Values.Where(f => f.npc == helper.agent.name).OrderByDescending(f => f.maxDepth).ToList();
                float limb = found.Where(f => f.kind == BodyClipDetector.Kind.LimbInBody).Select(f => f.maxDepth).DefaultIfEmpty(0f).Max();
                float item = found.Where(f => f.kind == BodyClipDetector.Kind.ItemInBody).Select(f => f.maxDepth).DefaultIfEmpty(0f).Max();
                float off = found.Where(f => f.kind == BodyClipDetector.Kind.HandOff).Select(f => f.maxDepth).DefaultIfEmpty(0f).Max();
                float reachL = Vector3.Distance(shoulder.position, helper.shovelGripLeft.position);
                float reachR = Vector3.Distance(shoulderR.position, helper.shovelGripRight.position);
                string worst = found.Count > 0 ? $"{found[0].kind} {found[0].a}↔{found[0].b}" : "";
                table.AppendLine($"d=({dx:0.00},{dy:0.00},{dz:0.00}) tay↔thân {limb * 100f:0.0} cm, xẻng↔thân {item * 100f:0.0} cm, tay rời {off * 100f:0.0} cm, vai–tay T {reachL:0.00} P {reachR:0.00}; tệ nhất {worst}");
                Object.Destroy(go);
                yield return null;
            }
            NpcHandWork.CarryRight = carryR;
            NpcHandWork.CarryLeft = carryL;
            Debug.Log(table.ToString());
            Assert.Pass(table.ToString());
        }

        /// <summary>Ảnh cận phụ bếp bê xẻng (đứng): trước, trái, phải, để người xem chỗ nắm và tay trước tạp dề.</summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator CarryCloseUps() => CloseUps("tp18_helper_carry_", false);

        /// <summary>Cùng ảnh cận với chỗ nắm cũ (so sánh).</summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator CarryCloseUpsOld() => CloseUps("tp18_helper_carry_old_", true);

        static IEnumerator CloseUps(string prefix, bool old)
        {
            var carryR = NpcHandWork.CarryRight;
            var carryL = NpcHandWork.CarryLeft;
            if (old)
            {
                NpcHandWork.CarryRight = new Vector3(0.18f, 0.91f, -0.067f);
                NpcHandWork.CarryLeft = new Vector3(-0.05f, 0.99f, 0.15f);
            }
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            var helper = Object.FindAnyObjectByType<HelperRoutine>();
            helper.StopAllCoroutines();
            helper.enabled = false;
            helper.agent.StopAllCoroutines();
            var hands = helper.GetComponent<NpcHandWork>();
            var rig = HaiBanTay.Player.PlayerRig.Current;
            var input = PlayTestUtil.Root().Input;
            yield return PlayTestUtil.WaitUntil(() => input.GameplayEnabled, 30f);
            yield return hands.PickShovel(helper.shovel, helper.shovelGripLeft, helper.shovelGripRight);
            yield return hands.Scoop(helper.shovel, helper.shovelLoad);
            helper.agent.Play("Idle");
            for (float t = 0f; t < 1.5f; t += Time.deltaTime) yield return null;
            var pos = helper.agent.transform.position;
            foreach (var (name, side) in new[] { ("front", 0f), ("left", -65f), ("right", 65f) })
            {
                var dir = Quaternion.Euler(0f, helper.agent.transform.eulerAngles.y + side, 0f) * Vector3.forward;
                var feet = pos + dir * 1.15f;
                feet.y = rig.Position.y;
                float yaw = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;
                rig.motor.Teleport(feet, yaw);
                rig.look.SetAngles(yaw, 22f);
                for (int f = 0; f < 20; f++) yield return null;
                yield return ScreenshotCapture.Capture(prefix + name);
            }
            NpcHandWork.CarryRight = carryR;
            NpcHandWork.CarryLeft = carryL;
        }

        /// <summary>Dò chỗ nắm khi khom (LowRight/LowLeft): dời theo lưới, mỗi phương án chạy trọn một lượt nạp lò (khom, đẩy, lật, rút, đứng dậy).</summary>
        [UnityTest, Timeout(3600000)]
        public IEnumerator ScanLow()
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            var helper = Object.FindAnyObjectByType<HelperRoutine>();
            helper.StopAllCoroutines();
            helper.enabled = false;
            helper.agent.StopAllCoroutines();
            var hands = helper.GetComponent<NpcHandWork>();
            var animator = helper.agent.GetComponentInChildren<Animator>();
            var shoulder = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var shoulderR = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Time.captureDeltaTime = 1f / 60f;
            yield return hands.PickShovel(helper.shovel, helper.shovelGripLeft, helper.shovelGripRight);
            yield return hands.Scoop(helper.shovel, helper.shovelLoad);
            var baseR = new Vector3(0.2f, 0.95f, 0.18f);
            var baseL = new Vector3(0.05f, 0.825f, 0.38f);
            var table = new System.Text.StringBuilder("SHOVEL_SCAN_LOW\n");
            foreach (float dz in new[] { 0f, 0.1f, 0.2f })
            foreach (float dy in new[] { 0f, 0.07f, 0.14f })
            foreach (float dx in new[] { 0f, 0.06f })
            {
                NpcHandWork.LowRight = baseR + new Vector3(dx, dy, dz);
                NpcHandWork.LowLeft = baseL + new Vector3(dx, dy, dz);
                var go = new GameObject("BodyClipProbe");
                var probe = go.AddComponent<BodyClipProbe>();
                probe.animateAll = true;
                probe.Watch(helper.agent);
                float maxReachL = 0f, maxReachR = 0f;
                var run = helper.StartCoroutine(hands.Feed(helper.shovel, helper.shovelLoad));
                bool done = false;
                helper.StartCoroutine(Wait(run, () => done = true));
                for (float t = 0f; t < 12f && !done; t += Time.deltaTime)
                {
                    yield return null;
                    maxReachL = Mathf.Max(maxReachL, Vector3.Distance(shoulder.position, helper.shovelGripLeft.position));
                    maxReachR = Mathf.Max(maxReachR, Vector3.Distance(shoulderR.position, helper.shovelGripRight.position));
                }
                var found = probe.Track.Findings.Values.Where(f => f.npc == helper.agent.name).OrderByDescending(f => f.maxDepth).ToList();
                float limb = found.Where(f => f.kind == BodyClipDetector.Kind.LimbInBody).Select(f => f.maxDepth).DefaultIfEmpty(0f).Max();
                float item = found.Where(f => f.kind == BodyClipDetector.Kind.ItemInBody).Select(f => f.maxDepth).DefaultIfEmpty(0f).Max();
                float off = found.Where(f => f.kind == BodyClipDetector.Kind.HandOff).Select(f => f.maxDepth).DefaultIfEmpty(0f).Max();
                string worst = found.Count > 0 ? $"{found[0].kind} {found[0].a}↔{found[0].b}" : "";
                table.AppendLine($"d=({dx:0.00},{dy:0.00},{dz:0.00}) tay↔thân {limb * 100f:0.0} cm, xẻng↔thân {item * 100f:0.0} cm, tay rời {off * 100f:0.0} cm, vai–tay tối đa T {maxReachL:0.00} P {maxReachR:0.00}; tệ nhất {worst}; xong {done}");
                Object.Destroy(go);
                yield return null;
            }
            NpcHandWork.LowRight = baseR;
            NpcHandWork.LowLeft = baseL;
            Debug.Log(table.ToString());
            Assert.Pass(table.ToString());
        }

        [UnityTest, Timeout(3600000)]
        public IEnumerator ScanLowRight()
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            var helper = Object.FindAnyObjectByType<HelperRoutine>();
            helper.StopAllCoroutines();
            helper.enabled = false;
            helper.agent.StopAllCoroutines();
            var hands = helper.GetComponent<NpcHandWork>();
            var animator = helper.agent.GetComponentInChildren<Animator>();
            var shoulder = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var shoulderR = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Time.captureDeltaTime = 1f / 60f;
            yield return hands.PickShovel(helper.shovel, helper.shovelGripLeft, helper.shovelGripRight);
            yield return hands.Scoop(helper.shovel, helper.shovelLoad);
            var baseR = new Vector3(0.2f, 1.02f, 0.28f);
            var baseL = new Vector3(0.05f, 0.895f, 0.48f);
            var table = new System.Text.StringBuilder("SHOVEL_SCAN_LOW_R\n");
            foreach (float dz in new[] { 0f, 0.05f, 0.1f })
            foreach (float dy in new[] { -0.05f, 0f })
            foreach (float dx in new[] { 0f, 0.06f, 0.1f })
            {
                NpcHandWork.LowRight = baseR + new Vector3(dx, dy, dz);
                NpcHandWork.LowLeft = baseL;
                var go = new GameObject("BodyClipProbe");
                var probe = go.AddComponent<BodyClipProbe>();
                probe.animateAll = true;
                probe.Watch(helper.agent);
                float maxReachL = 0f, maxReachR = 0f;
                var run = helper.StartCoroutine(hands.Feed(helper.shovel, helper.shovelLoad));
                bool done = false;
                helper.StartCoroutine(Wait(run, () => done = true));
                for (float t = 0f; t < 12f && !done; t += Time.deltaTime)
                {
                    yield return null;
                    maxReachL = Mathf.Max(maxReachL, Vector3.Distance(shoulder.position, helper.shovelGripLeft.position));
                    maxReachR = Mathf.Max(maxReachR, Vector3.Distance(shoulderR.position, helper.shovelGripRight.position));
                }
                var found = probe.Track.Findings.Values.Where(f => f.npc == helper.agent.name).OrderByDescending(f => f.maxDepth).ToList();
                float limb = found.Where(f => f.kind == BodyClipDetector.Kind.LimbInBody).Select(f => f.maxDepth).DefaultIfEmpty(0f).Max();
                float item = found.Where(f => f.kind == BodyClipDetector.Kind.ItemInBody).Select(f => f.maxDepth).DefaultIfEmpty(0f).Max();
                float off = found.Where(f => f.kind == BodyClipDetector.Kind.HandOff).Select(f => f.maxDepth).DefaultIfEmpty(0f).Max();
                string worst = found.Count > 0 ? $"{found[0].kind} {found[0].a}↔{found[0].b}" : "";
                table.AppendLine($"d=({dx:0.00},{dy:0.00},{dz:0.00}) tay↔thân {limb * 100f:0.0} cm, xẻng↔thân {item * 100f:0.0} cm, tay rời {off * 100f:0.0} cm, vai–tay tối đa T {maxReachL:0.00} P {maxReachR:0.00}; tệ nhất {worst}; xong {done}");
                Object.Destroy(go);
                yield return null;
            }
            NpcHandWork.LowRight = baseR;
            NpcHandWork.LowLeft = baseL;
            Debug.Log(table.ToString());
            Assert.Pass(table.ToString());
        }

        /// <summary>Dò hướng khuỷu (NpcArmIK.elbowHint) của phụ bếp: mỗi phương án chạy một lượt nạp lò (khom, đẩy, rút, đứng dậy) rồi bê xẻng đứng.</summary>
        [UnityTest, Timeout(3600000)]
        public IEnumerator ScanElbow()
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            var helper = Object.FindAnyObjectByType<HelperRoutine>();
            helper.StopAllCoroutines();
            helper.enabled = false;
            helper.agent.StopAllCoroutines();
            var hands = helper.GetComponent<NpcHandWork>();
            var ik = helper.agent.GetComponentInChildren<NpcArmIK>(true) ?? helper.GetComponentInChildren<NpcArmIK>(true);
            Assert.IsNotNull(ik, "phụ bếp có NpcArmIK");
            var original = ik.elbowHint;
            Time.captureDeltaTime = 1f / 60f;
            yield return hands.PickShovel(helper.shovel, helper.shovelGripLeft, helper.shovelGripRight);
            yield return hands.Scoop(helper.shovel, helper.shovelLoad);
            var table = new System.Text.StringBuilder("ELBOW_SCAN\n");
            foreach (float z in new[] { -0.25f, 0f, 0.25f })
            foreach (float y in new[] { 0.85f, 1.05f })
            foreach (float x in new[] { 0.5f, 0.75f, 1f })
            {
                ik.elbowHint = new Vector3(x, y, z);
                var go = new GameObject("BodyClipProbe");
                var probe = go.AddComponent<BodyClipProbe>();
                probe.animateAll = true;
                probe.Watch(helper.agent);
                var run = helper.StartCoroutine(hands.Feed(helper.shovel, helper.shovelLoad));
                bool done = false;
                helper.StartCoroutine(Wait(run, () => done = true));
                for (float t = 0f; t < 12f && !done; t += Time.deltaTime) yield return null;
                yield return hands.CarryShovel(helper.shovel, 0.3f);
                helper.agent.Play("Idle");
                for (float t = 0f; t < 1.2f; t += Time.deltaTime) yield return null;
                var found = probe.Track.Findings.Values.Where(f => f.npc == helper.agent.name).OrderByDescending(f => f.maxDepth).ToList();
                float limb = found.Where(f => f.kind == BodyClipDetector.Kind.LimbInBody).Select(f => f.maxDepth).DefaultIfEmpty(0f).Max();
                float item = found.Where(f => f.kind == BodyClipDetector.Kind.ItemInBody).Select(f => f.maxDepth).DefaultIfEmpty(0f).Max();
                float off = found.Where(f => f.kind == BodyClipDetector.Kind.HandOff).Select(f => f.maxDepth).DefaultIfEmpty(0f).Max();
                string worst = found.Count > 0 ? $"{found[0].kind} {found[0].a}↔{found[0].b}" : "";
                table.AppendLine($"hint=({x:0.00},{y:0.00},{z:0.00}) tay↔thân {limb * 100f:0.0} cm, xẻng↔thân {item * 100f:0.0} cm, tay rời {off * 100f:0.0} cm, nặng nhất: {worst}");
                Object.Destroy(go);
                yield return null;
            }
            ik.elbowHint = original;
            Debug.Log(table.ToString());
            Assert.Pass(table.ToString());
        }

        /// <summary>Một lượt nạp lò + bê xẻng đứng + đi: in độ lún lớn nhất theo từng bước (NpcHandWork.Step/Activity) của mọi mục thân.</summary>
        [UnityTest, Timeout(1800000)]
        public IEnumerator ScanStates()
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            var helper = Object.FindAnyObjectByType<HelperRoutine>();
            helper.StopAllCoroutines();
            helper.enabled = false;
            helper.agent.StopAllCoroutines();
            var hands = helper.GetComponent<NpcHandWork>();
            Time.captureDeltaTime = 1f / 60f;
            yield return hands.PickShovel(helper.shovel, helper.shovelGripLeft, helper.shovelGripRight);
            yield return hands.Scoop(helper.shovel, helper.shovelLoad);
            var go = new GameObject("BodyClipProbe");
            var probe = go.AddComponent<BodyClipProbe>();
            probe.animateAll = true;
            probe.Watch(helper.agent);
            var run = helper.StartCoroutine(hands.Feed(helper.shovel, helper.shovelLoad));
            bool done = false;
            helper.StartCoroutine(Wait(run, () => done = true));
            for (float t = 0f; t < 12f && !done; t += Time.deltaTime) yield return null;
            yield return hands.CarryShovel(helper.shovel, 0.3f);
            helper.agent.Play("Idle");
            for (float t = 0f; t < 1.2f; t += Time.deltaTime) yield return null;
            var table = new System.Text.StringBuilder("STATE_SCAN\n");
            foreach (var f in probe.Track.Findings.Values.Where(f => f.npc == helper.agent.name && f.maxDepth > 0.03f).OrderByDescending(f => f.maxDepth))
                table.AppendLine($"{f.kind} {f.a}↔{f.b} {f.maxDepth * 100f:0.0} cm | " + string.Join(", ", f.states.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}:{kv.Value * 100f:0.0}")) + " | " + f.worst);
            Object.Destroy(go);
            Debug.Log(table.ToString());
            Assert.Pass(table.ToString());
        }

        static IEnumerator Wait(Coroutine inner, System.Action finished)
        {
            yield return inner;
            finished();
        }
    }
}
