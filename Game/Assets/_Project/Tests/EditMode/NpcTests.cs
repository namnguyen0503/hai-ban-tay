using HaiBanTay.Npc;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>NPC đi theo lộ trình thẳng, chờ khi người chơi chắn đường.</summary>
    public sealed class NpcTests
    {
        [Test]
        public void StepMovesTowardTargetAndSnapsOnArrival()
        {
            var p = new Vector3(0f, 0.2f, 0f);
            Assert.IsFalse(NpcStepLogic.Step(ref p, new Vector3(2f, 5f, 0f), 1f, 0.5f));
            Assert.AreEqual(0.5f, p.x, 1e-4f);
            Assert.AreEqual(0.2f, p.y, 1e-6f, "giữ độ cao sàn của NPC");
            Assert.IsTrue(NpcStepLogic.Step(ref p, new Vector3(2f, 5f, 0f), 10f, 0.5f));
            Assert.AreEqual(new Vector3(2f, 0.2f, 0f), p);
        }

        [Test]
        public void NpcTurnsBeforeWalkingTowardABehindTarget()
        {
            // Clip 14/10: phụ bếp vừa bước vừa xoay, đi giật lùi. Đích sau lưng: đứng xoay; đích trước mặt: đủ tốc độ.
            Assert.AreEqual(1f, NpcStepLogic.AlignedSpeed(0f, Vector3.forward), 1e-4f);
            Assert.AreEqual(0f, NpcStepLogic.AlignedSpeed(0f, Vector3.back), 1e-4f);
            Assert.AreEqual(0f, NpcStepLogic.AlignedSpeed(0f, Vector3.right), 1e-4f, "đích ngang 90°: đứng xoay");
            float half = NpcStepLogic.AlignedSpeed(0f, Quaternion.Euler(0f, 55f, 0f) * Vector3.forward);
            Assert.That(half, Is.GreaterThan(0.2f).And.LessThan(0.8f), "lệch vừa: đi chậm lại");
        }

        [Test]
        public void ArrivingWithinTheRadiusDoesNotJumpOntoThePoint()
        {
            // Đo giật 14/10: tới góc đường NavMesh, NPC (và nồi, thùng đang bê) nhảy 5–12 cm trong một khung vào đúng điểm.
            var p = Vector3.zero;
            Assert.IsTrue(NpcStepLogic.Step(ref p, new Vector3(0.1f, 0f, 0f), 1f, 1f / 60f, 0.12f), "trong vùng tới thì coi như tới");
            Assert.AreEqual(Vector3.zero, p, "không kéo bật vào điểm");
            Assert.IsFalse(NpcStepLogic.Step(ref p, new Vector3(0.2f, 0f, 0f), 1f, 1f / 60f, 0.12f));
            Assert.AreEqual(1f / 60f, p.x, 1e-5f, "ngoài vùng tới thì đi đúng một bước");
        }

        [Test]
        public void WalkSpeedRampsUpAndBrakesIntoTheStop()
        {
            // Đo giật 14/10: NPC đạt tốc độ đi trong một khung, nồi đang bê giật từ đứng yên sang 1 m/s.
            float v = 0f;
            v = NpcStepLogic.Ramp(v, 1.2f, NpcAgent.Accel, 1f / 60f);
            Assert.AreEqual(NpcAgent.Accel / 60f, v, 1e-5f, "khung đầu chỉ nhích");
            for (int i = 0; i < 60; i++) v = NpcStepLogic.Ramp(v, 1.2f, NpcAgent.Accel, 1f / 60f);
            Assert.AreEqual(1.2f, v, 1e-5f, "sau một giây đã đủ tốc độ");
            Assert.Less(NpcStepLogic.BrakeSpeed(0.05f, NpcAgent.Accel), 0.6f, "sát đích thì đã chậm");
            Assert.AreEqual(0.2f, NpcStepLogic.BrakeSpeed(0f, NpcAgent.Accel), 1e-5f, "không bò mãi những cm cuối");
            Assert.Greater(NpcStepLogic.BrakeSpeed(1f, NpcAgent.Accel), 1.2f, "còn xa thì không hãm");
        }

        [Test]
        public void TurningSpeedsUpAndSlowsDownWithoutOvershooting()
        {
            // Đo giật 14/10: muôi cầm trước hông vụt 2 cm/khung khi quản bếp bắt đầu quay ngoắt (SmoothDampAngle 0,1 s gần như quay ngay ở
            // tốc độ tối đa).
            float yaw = 0f, velocity = 0f, lastVelocity = 0f, dt = 1f / 60f;
            float t = 0f;
            for (; t < 3f && (Mathf.Abs(Mathf.DeltaAngle(yaw, 90f)) > 0.01f || velocity != 0f); t += dt)
            {
                yaw = NpcStepLogic.TurnStep(yaw, 90f, ref velocity, 300f, NpcAgent.TurnAccel, dt);
                if (Mathf.Abs(velocity) > Mathf.Abs(lastVelocity))
                    Assert.LessOrEqual(Mathf.Abs(velocity) - Mathf.Abs(lastVelocity), NpcAgent.TurnAccel * dt + 1e-3f, "tốc độ quay tăng dần");
                else
                    Assert.LessOrEqual(Mathf.Abs(lastVelocity) - Mathf.Abs(velocity), 3f * NpcAgent.TurnAccel * dt + 1e-3f, "hãm dần, không dừng phắt");
                Assert.LessOrEqual(yaw, 90f + 1e-3f, "không vọt qua hướng cần quay");
                lastVelocity = velocity;
            }
            Assert.AreEqual(90f, yaw, 0.01f);
            Assert.That(t, Is.InRange(0.4f, 1.0f), "quay 90° như người thật, không tức thì");
        }

        [Test]
        public void ArmReachEasesIntoFullExtensionWithoutAKink()
        {
            // Đo giật 14/10: trần tầm tay cứng làm cổ tay đổi từ bám đích sang bị vai kéo trong một khung, xẻng nhảy 5–6 cm.
            const float length = 0.56f;
            Assert.AreEqual(0.4f, NpcArmIK.SoftReach(0.4f, length), 1e-6f, "trong tầm: đúng khoảng cách");
            float last = 0f, lastSlope = 1f;
            for (float d = 0.45f; d < 0.8f; d += 0.001f)
            {
                float r = NpcArmIK.SoftReach(d, length);
                Assert.LessOrEqual(r, length * 0.999f + 1e-6f, "không duỗi quá tay");
                if (last > 0f)
                {
                    float slope = (r - last) / 0.001f;
                    Assert.GreaterOrEqual(slope, -1e-3f, "càng xa càng duỗi");
                    Assert.Less(lastSlope - slope, 0.05f, $"tốc độ cổ tay đổi dần ở {d:F3} m, không gãy");
                    lastSlope = slope;
                }
                last = r;
            }
            Assert.Greater(NpcArmIK.SoftReach(length, length), length - 0.01f, "đích ở đúng tầm tay: hụt dưới 1 cm");
        }

        [Test]
        public void PlayerAheadBlocksButBesideOrBehindDoesNot()
        {
            var npc = Vector3.zero;
            var fwd = Vector3.forward;
            Assert.IsTrue(NpcStepLogic.Blocked(npc, fwd, new Vector3(0.1f, 0f, 0.6f)));
            Assert.IsFalse(NpcStepLogic.Blocked(npc, fwd, new Vector3(0f, 0f, -0.5f)), "sau lưng không chắn");
            Assert.IsFalse(NpcStepLogic.Blocked(npc, fwd, new Vector3(0.7f, 0f, 0.1f)), "đứng ngang không chắn");
            Assert.IsFalse(NpcStepLogic.Blocked(npc, fwd, new Vector3(0f, 0f, 2f)), "còn xa không chắn");
        }

        [Test]
        public void TurnYawHeadsTowardDirectionAtLimitedRate()
        {
            float yaw = NpcStepLogic.TurnYaw(0f, Vector3.right, 90f, 0.5f);
            Assert.AreEqual(45f, yaw, 1e-3f);
            yaw = NpcStepLogic.TurnYaw(yaw, Vector3.right, 90f, 1f);
            Assert.AreEqual(90f, yaw, 1e-3f);
        }

        // ---------- QA-0150: đồ NPC cầm/vác so với vỏ thân Ba ----------

        static NpcStepLogic.Box Crate(Vector3 center, Vector3 half) =>
            new NpcStepLogic.Box(center, Vector3.right, Vector3.up, Vector3.forward, half);

        [Test]
        public void ReachGapMeasuresFromShellPointsToTheItemBox()
        {
            var boxes = new[] { Crate(new Vector3(0f, 1.2f, 1f), new Vector3(0.2f, 0.15f, 0.35f)) };
            var shell = new[] { new Vector3(0f, 1.2f, 0.5f), new Vector3(0.3f, 1.2f, 0f) };
            Assert.AreEqual(0.15f, NpcStepLogic.ReachGap(shell, Vector3.zero, Matrix4x4.identity, boxes, out var away), 1e-4f);
            Assert.AreEqual(-1f, away.z, 1e-4f, "hướng ra khỏi đồ phía Ba");
            // Dời vỏ 0,2 m về phía đồ: điểm ngực lún 5 cm vào hộp (khe có dấu).
            Assert.AreEqual(-0.05f, NpcStepLogic.ReachGap(shell, new Vector3(0f, 0f, 0.2f), Matrix4x4.identity, boxes, out _), 1e-4f);
            // Lún sâu thêm thì khe nhỏ đi, bước đó bị chặn dù đã chạm sẵn.
            float deeper = NpcStepLogic.ReachGap(shell, new Vector3(0f, 0f, 0.25f), Matrix4x4.identity, boxes, out _);
            Assert.AreEqual(-0.1f, deeper, 1e-4f);
            Assert.IsFalse(NpcStepLogic.ReachAllows(-0.05f, deeper));
            var go = new GameObject("ReachBox");
            try
            {
                go.transform.localScale = new Vector3(2f, 1f, 1f);
                go.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                var box = NpcStepLogic.Box.Of(go.transform, new Bounds(new Vector3(0f, 0f, 1f), Vector3.one));
                Assert.AreEqual(1f, box.half.x, 1e-4f, "nửa cạnh gồm scale");
                Assert.AreEqual(1f, box.center.x, 1e-4f, "tâm bounds cục bộ quay theo vật");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PoseShiftMovesTheItemWithTheNpc()
        {
            // Hộp chĩa sang phải NPC 0,55 m (đứng ở gốc, hướng +z). Ba đứng ở x = 0,85, z = 1.
            var boxes = new[] { Crate(new Vector3(0.55f, 1.2f, 0f), new Vector3(0.35f, 0.15f, 0.15f)) };
            var shell = new[] { new Vector3(0.85f, 1.2f, 1f) };
            float now = NpcStepLogic.ReachGap(shell, Vector3.zero, Matrix4x4.identity, boxes, out _);
            Assert.Greater(now, 0.5f);
            // NPC tiến 1 m: hộp tới ngang Ba, mép hộp x 0,9 > 0,85 → lún 5 cm.
            float after = NpcStepLogic.ReachGap(shell, Vector3.zero, NpcStepLogic.PoseShift(Vector3.zero, 0f, new Vector3(0f, 0f, 1f), 0f), boxes, out _);
            Assert.AreEqual(-0.05f, after, 1e-4f);
            Assert.IsFalse(NpcStepLogic.ReachAllows(now, after));
            // NPC quay trái 90° tại chỗ: hộp chĩa về +z, mép xa z 0,9, Ba ở x 0,85 → còn hở.
            float turned = NpcStepLogic.ReachGap(shell, Vector3.zero, NpcStepLogic.PoseShift(Vector3.zero, 0f, Vector3.zero, -90f), boxes, out _);
            Assert.Greater(turned, 0.5f);
            // Đang sát sẵn: lùi ra hay đứng yên vẫn được.
            Assert.IsTrue(NpcStepLogic.ReachAllows(0.01f, 0.02f));
            Assert.IsTrue(NpcStepLogic.ReachAllows(0.01f, 0.01f));
            Assert.IsFalse(NpcStepLogic.ReachAllows(0.03f, 0.01f));
            Assert.IsTrue(NpcStepLogic.ReachAllows(0.2f, NpcStepLogic.ReachMargin));
        }

        [Test]
        public void StuckClockKeepsCountingWhileTheNpcRocksAtTheEdgeOfAnObstacle()
        {
            // CI 01/11 (KitchenHands, phụ bếp ở mép chồng nồi): bị từ chối một bước, trượt được một bước vài cm, lại bị từ chối… Đồng hồ
            // cũ về 0 mỗi bước trượt nên không bao giờ tới lúc thoát.
            var anchor = Vector3.zero;
            float since = -1f, last = float.NegativeInfinity, stuck = 0f;
            for (int f = 0; f < 300; f++)
            {
                if (f % 2 == 1) continue; // khung trượt được: không gọi
                var pos = new Vector3((f % 4 == 0 ? 0.05f : -0.05f), 0f, 0f);
                stuck = NpcStepLogic.StuckClock(ref anchor, ref since, ref last, pos, f / 60f);
            }
            Assert.Greater(stuck, 4.5f, "lắc tại chỗ vẫn là một đợt kẹt");
            // Rời chỗ kẹt quá StuckRadius: đợt mới.
            Assert.AreEqual(0f, NpcStepLogic.StuckClock(ref anchor, ref since, ref last, new Vector3(0.5f, 0f, 0f), 5.1f), 1e-5f);
            // Lâu không bị từ chối: đợt mới.
            Assert.AreEqual(0f, NpcStepLogic.StuckClock(ref anchor, ref since, ref last, new Vector3(0.5f, 0f, 0f), 5.1f + NpcStepLogic.StuckForgetAfter + 0.1f), 1e-5f);
        }

        [Test]
        public void ReachYieldCountsTheWholeBoutEvenWhenTheNpcSlipsClearForAMoment()
        {
            // CI 01/11 (KitchenHands, phụ bếp bê nồi qua Ba): bị chặn 0,5 s, lách ra hở lề 0,2 s, quay lại bị chặn… lặp. Đồng hồ cũ đếm
            // lại mỗi lần hở nên không bao giờ quá 3 s; NPC đứng kẹt bên Ba.
            float since = -1f, last = float.NegativeInfinity;
            bool yielded = false;
            float yieldedAt = -1f;
            const float dt = 1f / 60f;
            for (float t = 0f; t < 8f && !yielded; t += dt)
            {
                if (t % 0.7f > 0.5f)
                    continue; // khung hở lề: không gọi ReachYield
                yielded = NpcStepLogic.ReachYield(ref since, ref last, t, 3f);
                if (yielded) yieldedAt = t;
            }
            Assert.IsTrue(yielded, "lách ra rồi quay lại vẫn là một đợt chờ: quá 3 s thì cho đi");
            Assert.Less(yieldedAt, 3.6f);

            // Hở liền quá ReachForgetAfter thì đợt mới đếm lại từ đầu.
            since = -1f;
            last = float.NegativeInfinity;
            Assert.IsFalse(NpcStepLogic.ReachYield(ref since, ref last, 0f, 3f));
            Assert.IsFalse(NpcStepLogic.ReachYield(ref since, ref last, 2.9f, 3f));
            Assert.IsFalse(NpcStepLogic.ReachYield(ref since, ref last, 5.0f, 3f), "hở 2,1 s: đợt mới bắt đầu lúc 5,0");
            Assert.IsFalse(NpcStepLogic.ReachYield(ref since, ref last, 6.5f, 3f));
            Assert.IsTrue(NpcStepLogic.ReachYield(ref since, ref last, 8.1f, 3f), "đợt mới quá 3 s thì cho đi");
        }
    }
}
