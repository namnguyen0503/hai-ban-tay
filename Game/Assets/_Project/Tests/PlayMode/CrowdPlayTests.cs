using System.Collections;
using System.Collections.Generic;
using HaiBanTay.Core;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Đám đông thật trong 90_Sandbox (khu CrowdZone, 40 NPC): chạy 60 giây không lỗi, phần lớn NPC đi lại thật, không ai
    /// chờ kẹt quá 10 giây, người chơi đi qua lối hẹp không bị chặn quá 3 giây, LOD không vượt số NPC chạy Animator.
    /// Sandbox không nằm trong Build Settings nên nạp bằng EditorSceneManager.LoadSceneAsyncInPlayMode.
    /// </summary>
    public class CrowdPlayTests
    {
        public const string SandboxPath = "Assets/_Project/Scenes/90_Sandbox.unity";

        [TearDown]
        public void TearDown()
        {
            var root = PlayTestUtil.Root();
            root.Input.SimMove = Vector2.zero;
            Time.captureDeltaTime = 0f;
            PlayTestUtil.ExitSim();
        }

        public static IEnumerator LoadSandbox()
        {
            PlayTestUtil.Root();
#if UNITY_EDITOR
            var op = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(SandboxPath,
                new LoadSceneParameters(LoadSceneMode.Single));
            while (!op.isDone)
                yield return null;
#else
            yield break;
#endif
            yield return null;
            yield return null;
        }

        /// <summary>Đưa người chơi tới điểm đo View_*: chân đứng ở vị trí điểm, nhìn theo hướng của điểm.</summary>
        public static void StandAt(Transform marker, float pitchOverride = float.NaN)
        {
            var rig = PlayerRig.Current;
            float yaw = marker.eulerAngles.y;
            float pitch = float.IsNaN(pitchOverride) ? NormalizePitch(marker.eulerAngles.x) : pitchOverride;
            rig.motor.Teleport(marker.position, yaw);
            rig.look.SetAngles(yaw, pitch);
        }

        static float NormalizePitch(float x) => x > 180f ? x - 360f : x;

        static Transform Marker(string name)
        {
            var go = GameObject.Find(name);
            Assert.IsNotNull(go, "thiếu điểm đo " + name);
            return go.transform;
        }

        [UnityTest]
        public IEnumerator SixtySecondsRunWithoutErrorsAndTheCrowdActuallyMoves()
        {
            yield return LoadSandbox();
            var zone = GameObject.Find("CrowdZone");
            Assert.IsNotNull(zone, "sandbox chưa có CrowdZone: chạy SandboxCrowd.RebuildSandbox");
            var agents = zone.GetComponentsInChildren<NpcAgent>(true);
            Assert.AreEqual(40, agents.Length, "khu thử có 40 NPC");
            var lod = zone.GetComponentInChildren<CrowdLod>(true);
            Assert.IsNotNull(lod);

            // Người chơi đứng ở góc nhìn thấy cả đám đông, không chắn lối ai.
            yield return null;
            StandAt(Marker("View_Overview"));
            Time.captureDeltaTime = 1f / 60f;

            int n = agents.Length;
            var start = new Vector3[n];
            var maxMoved = new float[n];
            var waitNow = new float[n];
            var waitMax = new float[n];
            var mover = new bool[n];
            for (int i = 0; i < n; i++)
            {
                start[i] = agents[i].Position;
                var r = agents[i].GetComponent<CrowdRoutine>();
                mover[i] = r != null && (r.mode == CrowdMode.Loop || r.mode == CrowdMode.Wander);
                agents[i].ResetStats();
            }

            int maxAnimatedSeen = 0;
            int switchesAtSettle = 0;
            float overlapSeconds = 0f; // tổng thời gian các cặp NPC chồng lên nhau (tâm cách < 0,3 m)
            const int frames = 60 * 60;
            for (int f = 0; f < frames; f++)
            {
                yield return null;
                for (int i = 0; i < n; i++)
                {
                    float moved = Vector3.Distance(agents[i].Position, start[i]);
                    if (moved > maxMoved[i])
                        maxMoved[i] = moved;
                    if (agents[i].Waiting)
                    {
                        waitNow[i] += Time.deltaTime;
                        if (waitNow[i] > waitMax[i])
                            waitMax[i] = waitNow[i];
                    }
                    else
                        waitNow[i] = 0f;
                }
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        var d = agents[i].Position - agents[j].Position;
                        d.y = 0f;
                        if (d.sqrMagnitude < 0.3f * 0.3f)
                            overlapSeconds += Time.deltaTime;
                    }
                if (lod.AnimatedCount > maxAnimatedSeen)
                    maxAnimatedSeen = lod.AnimatedCount;
                if (f == 5 * 60)
                    switchesAtSettle = lod.Switches;
            }

            int movers = 0, moved1m = 0;
            float worstWait = 0f;
            string worstName = "";
            for (int i = 0; i < n; i++)
            {
                if (mover[i])
                {
                    movers++;
                    if (maxMoved[i] >= 1f)
                        moved1m++;
                }
                if (waitMax[i] > worstWait)
                {
                    worstWait = waitMax[i];
                    worstName = agents[i].name;
                }
            }
            int switchesAfterSettle = lod.Switches - switchesAtSettle;
            Debug.Log($"CrowdPlay: {movers} NPC Loop/Wander, {moved1m} đi xa ≥ 1 m; chờ liên tục lâu nhất {worstWait:0.0}s ({worstName}); " +
                      $"LOD: {lod.AnimatedCount} chạy/{lod.StaticCount} tĩnh/{lod.HiddenCount} ẩn, tối đa {maxAnimatedSeen} chạy Animator, " +
                      $"{switchesAfterSettle} lần đổi chế độ trong 55 giây cuối; chồng nhau {overlapSeconds:0.0} giây-cặp");

            Assert.GreaterOrEqual(movers, 15, "2 vòng bốc hàng (10) + 5 người đi dạo");
            Assert.GreaterOrEqual(moved1m, Mathf.CeilToInt(movers * 0.9f), $"≥ 90 % NPC Loop/Wander phải đi lại: {moved1m}/{movers}");
            Assert.Less(worstWait, 10f, $"{worstName} đứng chờ liên tục {worstWait:0.0}s");
            Assert.LessOrEqual(maxAnimatedSeen, lod.MaxAnimated, "LOD không được cho quá maxAnimated NPC chạy Animator");
            Assert.Greater(lod.StaticCount + lod.HiddenCount, 0, "NPC ngoài bán kính phải hạ xuống tư thế tĩnh hoặc ẩn");
            Assert.LessOrEqual(switchesAfterSettle, n, "LOD đổi chế độ quá nhiều: nhấp nháy");
            Assert.Less(overlapSeconds, 5f, "NPC đi xuyên nhau quá nhiều (tổng giây-cặp có tâm cách < 0,3 m)");
        }

        [UnityTest]
        public IEnumerator PlayerWalksThroughTheNarrowLaneAgainstTheCrowdWithoutBeingStuck()
        {
            yield return LoadSandbox();
            var zone = GameObject.Find("CrowdZone");
            Assert.IsNotNull(zone);
            var rig = PlayerRig.Current;
            var input = PlayTestUtil.Root().Input;
            yield return PlayTestUtil.WaitUntil(() => input.GameplayEnabled, 30f);
            Assert.IsTrue(input.GameplayEnabled);
            Time.captureDeltaTime = 1f / 60f;

            float maxStall = 0f;
            foreach (var (from, to) in new[] { ("Lane_West", "Lane_East"), ("Lane_East", "Lane_West") })
            {
                var a = Marker(from);
                var b = Marker(to);
                StandAt(a, 0f);
                yield return null;
                var dir = b.position - a.position;
                dir.y = 0f;
                float length = dir.magnitude;
                dir /= length;
                input.SimMove = new Vector2(0f, 1f);
                var last = rig.Position;
                float stall = 0f;
                float elapsed = 0f;
                // Đi hết lối và ra khỏi hai đầu (tới điểm đối diện), tối đa 40 giây.
                while (Vector3.Dot(rig.Position - a.position, dir) < length - 0.5f && elapsed < 40f)
                {
                    yield return null;
                    float dt = Time.deltaTime;
                    elapsed += dt;
                    float speed = Vector3.Distance(rig.Position, last) / dt;
                    last = rig.Position;
                    if (speed < 0.3f)
                    {
                        stall += dt;
                        if (stall > maxStall)
                            maxStall = stall;
                    }
                    else
                        stall = 0f;
                }
                input.SimMove = Vector2.zero;
                Assert.Less(elapsed, 40f, $"{from} → {to}: không đi hết lối, kẹt ở {rig.Position}");
                Debug.Log($"CrowdPlay: {from} → {to} mất {elapsed:0.0}s, đứng im lâu nhất {maxStall:0.0}s");
            }
            Assert.Less(maxStall, 3f, "người chơi bị chặn quá 3 giây trong lối hẹp");
        }

        [UnityTest]
        public IEnumerator PlayerStandingInTheWorkersLaneNeverHoldsAnyoneForTenSeconds()
        {
            yield return LoadSandbox();
            var zone = GameObject.Find("CrowdZone");
            Assert.IsNotNull(zone);
            var rig = PlayerRig.Current;
            var agents = zone.GetComponentsInChildren<NpcAgent>(true);
            yield return null;
            // Người chơi đứng chắn giữa làn đi của vòng bốc hàng 1 (z = 13,2), nhìn về phía các phu đi tới.
            var spot = zone.transform.TransformPoint(new Vector3(-12f, 0f, 13.2f));
            rig.motor.Teleport(spot, 270f);
            rig.look.SetAngles(270f, 0f);
            Time.captureDeltaTime = 1f / 60f;

            var wait = new float[agents.Length];
            var longest = new float[agents.Length];
            var passed = new HashSet<NpcAgent>();
            for (int f = 0; f < 60 * 50; f++)
            {
                yield return null;
                for (int i = 0; i < agents.Length; i++)
                {
                    if (agents[i].Waiting)
                    {
                        wait[i] += Time.deltaTime;
                        if (wait[i] > longest[i])
                            longest[i] = wait[i];
                    }
                    else
                        wait[i] = 0f;
                    // Đã đi qua người chơi: ở phía đông chỗ người chơi đứng, trên làn đi của vòng 1.
                    var local = zone.transform.InverseTransformPoint(agents[i].Position);
                    if (agents[i].name.StartsWith("Npc_") && local.x > -10f && local.x < -6f && local.z > 12.7f)
                        passed.Add(agents[i]);
                }
            }
            float worst = 0f;
            string who = "";
            for (int i = 0; i < agents.Length; i++)
                if (longest[i] > worst)
                {
                    worst = longest[i];
                    who = agents[i].name;
                }
            Debug.Log($"CrowdPlay: người chơi đứng chắn làn 1: chờ liên tục lâu nhất {worst:0.0}s ({who}), {passed.Count} NPC đi qua");
            Assert.Less(worst, 10f, $"{who} bị giữ {worst:0.0}s");
            Assert.GreaterOrEqual(passed.Count, 3, "các phu phải vòng qua người chơi để tiếp tục làm việc");
        }

        [UnityTest]
        public IEnumerator QueueAdvancesWhenTheFrontLeavesAndTheLeaverRejoinsTheTail()
        {
            yield return LoadSandbox();
            var queue = Object.FindAnyObjectByType<CrowdQueue>();
            Assert.IsNotNull(queue, "sandbox thiếu hàng chờ");
            queue.autoAdvanceSeconds = 0f; // director (test) tự gọi Advance
            var members = queue.members;
            Assert.AreEqual(6, members.Length, "hàng chờ 6 người");
            StandAt(Marker("View_Queue"));
            Time.captureDeltaTime = 1f / 60f;

            yield return PlayTestUtil.WaitUntil(() => members[0].PlacedSlot == 0 && members[1].PlacedSlot == 1, 30f);
            Assert.AreEqual(0, members[0].PlacedSlot, "người đầu hàng phải vào đúng ô 0");
            for (int i = 0; i < members.Length; i++)
                Assert.AreEqual(i, queue.SlotOf(members[i]), "thứ tự ban đầu theo ô");

            var front = members[0];
            var second = members[1];
            Assert.IsTrue(queue.Advance());
            Assert.AreEqual(5, queue.Count);
            Assert.AreEqual(-1, queue.SlotOf(front), "người đầu hàng đã rời hàng");
            Assert.AreEqual(0, queue.SlotOf(second), "người thứ hai lên đầu hàng");
            Assert.AreEqual(1, queue.Served);
            yield return PlayTestUtil.WaitUntil(() => second.PlacedSlot == 0, 30f);
            Assert.AreEqual(0, second.PlacedSlot, "người thứ hai tiến lên đứng ở đầu hàng");
            var target = queue.slots[0].position;
            Assert.Less(Vector3.Distance(second.agent.Position, target), 0.3f, "và đứng đúng ô 0");

            yield return PlayTestUtil.WaitUntil(() => queue.SlotOf(front) >= 0, 90f);
            Assert.AreEqual(5, queue.SlotOf(front), "người đã được phục vụ đi vòng về vào đuôi hàng");
            Assert.AreEqual(6, queue.Count);
        }

        [UnityTest]
        public IEnumerator LodCapsTheAnimatedNpcCountPerTierAndDensityDropsOnLaptop()
        {
            yield return LoadSandbox();
            var zone = GameObject.Find("CrowdZone");
            var lod = zone.GetComponentInChildren<CrowdLod>(true);
            var agents = zone.GetComponentsInChildren<NpcAgent>(true);
            yield return null;
            // Nới bán kính để mọi NPC trong tầm nhìn đều đủ điều kiện, nên chỉ còn giới hạn theo tầng quyết định.
            lod.animatedRadius = 80f;
            StandAt(Marker("View_Overview"));
            lod.ForceTier(HaiBanTay.Performance.QualityTier.PC);
            yield return PlayTestUtil.WaitUntil(() => false, 2f);
            int activePc = 0;
            foreach (var a in agents)
                if (a.gameObject.activeSelf)
                    activePc++;
            Assert.AreEqual(40, activePc, "tầng PC dùng đủ 40 NPC");
            Assert.AreEqual(lod.MaxAnimated, lod.AnimatedCount, "gần đám đông thì số NPC chạy Animator đúng bằng giới hạn tầng PC");
            Assert.AreEqual(16, lod.MaxAnimated);

            lod.ForceTier(HaiBanTay.Performance.QualityTier.Laptop);
            yield return PlayTestUtil.WaitUntil(() => false, 2f);
            int activeLaptop = 0;
            foreach (var a in agents)
                if (a.gameObject.activeSelf)
                    activeLaptop++;
            Assert.Less(activeLaptop, 40, "tầng Laptop bỏ bớt NPC đứng thừa");
            Assert.Greater(activeLaptop, 30, "chỉ bỏ phần đứng thừa, không bỏ cả đám đông");
            Assert.AreEqual(10, lod.MaxAnimated);
            Assert.LessOrEqual(lod.AnimatedCount, 10, "tầng Laptop tối đa 10 NPC chạy Animator");
            Assert.GreaterOrEqual(lod.AnimatedCount, 8);
        }
    }
}
