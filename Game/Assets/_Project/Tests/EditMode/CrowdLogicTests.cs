using System.Collections.Generic;
using HaiBanTay.Editor.SceneBuilders;
using HaiBanTay.Npc;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>Phần logic thuần của hệ đám đông: lưới láng giềng, nhường nhau, hàng chờ, nhóm nói chuyện, màu biến thể.</summary>
    public sealed class CrowdLogicTests
    {
        // ---------- CrowdGrid ----------

        [Test]
        public void GridQueryMatchesBruteForce()
        {
            var rng = new System.Random(11);
            var grid = new CrowdGrid(2f);
            var pts = new List<Vector3>();
            for (int i = 0; i < 300; i++)
            {
                var p = new Vector3((float)rng.NextDouble() * 60f - 30f, 0f, (float)rng.NextDouble() * 60f - 30f);
                pts.Add(p);
                Assert.AreEqual(i, grid.Add(p));
            }
            var hits = new List<int>();
            for (int q = 0; q < 50; q++)
            {
                var c = pts[rng.Next(pts.Count)] + new Vector3(0.3f, 0f, -0.2f);
                float r = 0.5f + (float)rng.NextDouble() * 4f;
                grid.Query(c, r, hits);
                var expected = new List<int>();
                for (int i = 0; i < pts.Count; i++)
                    if ((pts[i].x - c.x) * (pts[i].x - c.x) + (pts[i].z - c.z) * (pts[i].z - c.z) <= r * r)
                        expected.Add(i);
                CollectionAssert.AreEquivalent(expected, hits, $"truy vấn {q}, bán kính {r}");
            }
        }

        [Test]
        public void GridHandlesNegativeCoordsAndClear()
        {
            var grid = new CrowdGrid(2f);
            grid.Add(new Vector3(-0.1f, 0f, -0.1f));
            grid.Add(new Vector3(0.1f, 0f, 0.1f));
            var hits = new List<int>();
            Assert.AreEqual(2, grid.Query(Vector3.zero, 0.5f, hits), "hai điểm kề nhau hai bên gốc tọa độ, khác ô");
            grid.Clear();
            Assert.AreEqual(0, grid.Count);
            Assert.AreEqual(0, grid.Query(Vector3.zero, 5f, hits));
            Assert.AreEqual(0, grid.Add(new Vector3(1f, 0f, 1f)), "chỉ số bắt đầu lại từ 0 sau Clear");
        }

        // ---------- Nhường nhau ----------

        [Test]
        public void OnlyTheHigherIdWaitsWhenTwoWalkersMeetHeadOn()
        {
            var a = Vector3.zero;
            var b = new Vector3(0f, 0f, 0.6f);
            bool aWaits = CrowdAvoidLogic.Blocks(1, a, Vector3.forward, 2, b, true, Vector3.back);
            bool bWaits = CrowdAvoidLogic.Blocks(2, b, Vector3.back, 1, a, true, Vector3.forward);
            Assert.IsFalse(aWaits, "số hiệu nhỏ được đi trước");
            Assert.IsTrue(bWaits, "số hiệu lớn nhường");
        }

        [Test]
        public void HeadOnWalkersAreSeenFromTwoMetresSoTheYielderCanStepAsideInTime()
        {
            var a = Vector3.zero;
            var b = new Vector3(0f, 0f, 1.8f);
            Assert.IsTrue(CrowdAvoidLogic.Blocks(2, a, Vector3.forward, 1, b, true, Vector3.back), "số hiệu lớn nhường từ xa");
            Assert.IsFalse(CrowdAvoidLogic.Blocks(1, b, Vector3.back, 2, a, true, Vector3.forward), "số hiệu nhỏ cứ đi");
            Assert.IsFalse(CrowdAvoidLogic.Blocks(2, a, Vector3.forward, 1, new Vector3(0f, 0f, 2.5f), true, Vector3.back), "xa quá 2 m chưa cần né");
            Assert.IsFalse(CrowdAvoidLogic.Blocks(2, a, Vector3.forward, 1, new Vector3(1.2f, 0f, 1.5f), true, Vector3.back), "lệch ngoài hình nón hẹp");
        }

        [Test]
        public void WalkersGoingTheSameWayOnlyBlockWhenRightOnTheirHeels()
        {
            var me = Vector3.zero;
            Assert.IsFalse(CrowdAvoidLogic.Blocks(9, me, Vector3.forward, 1, new Vector3(0f, 0f, 1.5f), true, Vector3.forward),
                "đi cùng chiều ở 1,5 m: không dừng chờ nhau (hàng chờ, đoàn phu nối đuôi)");
            Assert.IsTrue(CrowdAvoidLogic.Blocks(9, me, Vector3.forward, 1, new Vector3(0f, 0f, 0.5f), true, Vector3.forward),
                "sát lưng thì số hiệu lớn chờ");
            Assert.IsTrue(CrowdAvoidLogic.Opposing(Vector3.forward, Vector3.back));
            Assert.IsFalse(CrowdAvoidLogic.Opposing(Vector3.forward, Vector3.right));
            Assert.IsFalse(CrowdAvoidLogic.Opposing(Vector3.forward, Vector3.zero));
        }

        [Test]
        public void AStandingNpcAlwaysBlocksTheWalkerHeadingIntoIt()
        {
            var me = Vector3.zero;
            var other = new Vector3(0f, 0f, 0.6f);
            Assert.IsTrue(CrowdAvoidLogic.Blocks(1, me, Vector3.forward, 5, other, otherWalking: false));
            Assert.IsTrue(CrowdAvoidLogic.Blocks(9, me, Vector3.forward, 5, other, otherWalking: false));
        }

        [Test]
        public void NpcBehindOrBesideOrFarDoesNotBlock()
        {
            var me = Vector3.zero;
            Assert.IsFalse(CrowdAvoidLogic.Blocks(9, me, Vector3.forward, 1, new Vector3(0f, 0f, -0.5f), false), "sau lưng");
            Assert.IsFalse(CrowdAvoidLogic.Blocks(9, me, Vector3.forward, 1, new Vector3(0.7f, 0f, 0.1f), false), "đứng ngang");
            Assert.IsFalse(CrowdAvoidLogic.Blocks(9, me, Vector3.forward, 1, new Vector3(0f, 0f, 3f), false), "còn xa");
            Assert.IsFalse(CrowdAvoidLogic.Blocks(9, me, Vector3.forward, 9, new Vector3(0f, 0f, 0.5f), false), "chính mình");
        }

        [Test]
        public void WaitingNeverFormsACycleAmongWalkers()
        {
            // Ba người đứng sát nhau, ai cũng đang đi, mỗi người hướng về người kế tiếp: đồ thị chờ phải không có vòng,
            // nên luôn có ít nhất một người không phải chờ.
            var pos = new[] { new Vector3(0f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(0.25f, 0f, 0.43f) };
            int free = 0;
            for (int me = 0; me < 3; me++)
            {
                int next = (me + 1) % 3;
                var heading = pos[next] - pos[me];
                bool waits = false;
                for (int o = 0; o < 3; o++)
                    if (o != me && CrowdAvoidLogic.Blocks(me + 1, pos[me], heading, o + 1, pos[o], true))
                        waits = true;
                if (!waits)
                    free++;
            }
            Assert.GreaterOrEqual(free, 1);
        }

        [Test]
        public void SidestepTurnsAwayFromTheBlockerAndHeadOnPairsPassOnTheirOwnLeft()
        {
            var fwd = Vector3.forward;
            var right = CrowdAvoidLogic.SidestepHeading(fwd, new Vector3(0.3f, 0f, 0.6f));
            Assert.Less(right.x, 0f, "vật chắn lệch phải thì rẽ trái");
            var left = CrowdAvoidLogic.SidestepHeading(fwd, new Vector3(-0.3f, 0f, 0.6f));
            Assert.Greater(left.x, 0f, "vật chắn lệch trái thì rẽ phải");
            // Chắn đúng giữa: hai người đi ngược chiều cùng rẽ về bên trái của mình, tức hai phía thế giới đối nhau.
            var a = CrowdAvoidLogic.SidestepHeading(Vector3.forward, Vector3.forward);
            var b = CrowdAvoidLogic.SidestepHeading(Vector3.back, Vector3.back);
            Assert.Less(a.x * b.x, 0f);
        }

        // ---------- Hàng chờ ----------

        [Test]
        public void QueueKeepsOrderAndAdvancesWhenTheFrontLeaves()
        {
            var q = new CrowdQueueLogic();
            for (int id = 10; id < 16; id++)
                q.Enqueue(id);
            Assert.AreEqual(6, q.Count);
            Assert.AreEqual(10, q.Front);
            for (int slot = 0; slot < 6; slot++)
                Assert.AreEqual(10 + slot, q.IdAt(slot));

            Assert.AreEqual(10, q.Dequeue());
            Assert.AreEqual(5, q.Count);
            Assert.AreEqual(11, q.Front);
            Assert.AreEqual(0, q.SlotOf(11), "người thứ hai lên đầu hàng");
            Assert.AreEqual(4, q.SlotOf(15), "mọi người sau tiến một ô");
            Assert.AreEqual(-1, q.SlotOf(10), "người đã rời không còn trong hàng");
        }

        [Test]
        public void QueueEnqueueGoesToTheTailAndIsIdempotent()
        {
            var q = new CrowdQueueLogic();
            q.Enqueue(1);
            q.Enqueue(2);
            q.Dequeue();
            q.Enqueue(1);
            q.Enqueue(1);
            Assert.AreEqual(2, q.Count);
            Assert.AreEqual(2, q.IdAt(0));
            Assert.AreEqual(1, q.IdAt(1), "người quay lại vào đuôi hàng");
        }

        [Test]
        public void QueueCyclesInFairOrderLikeTheSandboxLine()
        {
            // 6 người, mỗi lần đầu hàng rời đi rồi vào lại đuôi: sau 6 lần Advance trở về đúng thứ tự ban đầu.
            var q = new CrowdQueueLogic();
            for (int id = 0; id < 6; id++)
                q.Enqueue(id);
            var served = new List<int>();
            for (int i = 0; i < 12; i++)
            {
                int id = q.Dequeue();
                served.Add(id);
                q.Enqueue(id);
            }
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5, 0, 1, 2, 3, 4, 5 }, served);
            Assert.AreEqual(-1, new CrowdQueueLogic().Dequeue(), "hàng rỗng trả -1");
        }

        // ---------- Nhóm nói chuyện ----------

        [Test]
        public void GroupSpeakerIsDeterministicInRangeAndEveryoneGetsATurn()
        {
            var seen = new HashSet<int>();
            int silent = 0, turns = 600;
            for (int k = 0; k < turns; k++)
            {
                float t = k * CrowdGroupLogic.TurnSeconds(302) + 0.1f;
                int s = CrowdGroupLogic.SpeakerAt(t, 302, 4);
                Assert.AreEqual(s, CrowdGroupLogic.SpeakerAt(t, 302, 4), "cùng thời điểm luôn cùng người nói");
                Assert.That(s, Is.InRange(-1, 3));
                if (s < 0)
                    silent++;
                else
                    seen.Add(s);
            }
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, seen, "cả 4 người đều có lượt nói");
            Assert.That(silent, Is.InRange(turns / 20, turns / 3), "thỉnh thoảng cả nhóm im lặng, không quá nhiều");
        }

        [Test]
        public void GroupTurnLengthIsBetweenThreeAndFiveSeconds()
        {
            for (int seed = 0; seed < 100; seed++)
                Assert.That(CrowdGroupLogic.TurnSeconds(seed), Is.InRange(3f, 5f));
            Assert.AreEqual(-1, CrowdGroupLogic.SpeakerAt(5f, 1, 0));
        }

        [Test]
        public void GroupOffsetsMakeMembersLookAroundAtDifferentTimes()
        {
            int differing = 0;
            for (int k = 0; k < 100; k++)
                if (CrowdGroupLogic.ListenerLooksAround(k * 2.5f, 301, 0) != CrowdGroupLogic.ListenerLooksAround(k * 2.5f, 301, 1))
                    differing++;
            Assert.Greater(differing, 10);
        }

        // ---------- Màu biến thể ----------

        [Test]
        public void PaletteIsDeterministicAndVariedAcrossSeeds()
        {
            var seen = new HashSet<Color>();
            for (int seed = 0; seed < 40; seed++)
            {
                var c = CrowdPalette.Pick(CrowdTintKind.Cotton, seed, 0);
                Assert.AreEqual(c, CrowdPalette.Pick(CrowdTintKind.Cotton, seed, 0), "cùng seed cùng màu");
                seen.Add(c);
            }
            Assert.GreaterOrEqual(seen.Count, 5, "40 seed cho ít nhất 5 màu khác nhau");
        }

        [Test]
        public void PaletteColorsAreValidLdrMultipliers()
        {
            foreach (CrowdTintKind kind in System.Enum.GetValues(typeof(CrowdTintKind)))
            {
                var colors = CrowdPalette.ColorsOf(kind);
                Assert.Greater(colors.Length, 3, kind.ToString());
                foreach (var c in colors)
                {
                    Assert.That(c.r, Is.InRange(0f, 1f));
                    Assert.That(c.g, Is.InRange(0f, 1f));
                    Assert.That(c.b, Is.InRange(0f, 1f));
                }
            }
        }

        [Test]
        public void TintSlotsAreRecognisedByRocketboxMaterialSuffix()
        {
            Assert.AreEqual(CrowdTintKind.Clothes, CrowdBuilder.TintKindOf("Cook_Body"));
            Assert.AreEqual(CrowdTintKind.Cotton, CrowdBuilder.TintKindOf("Helper_Cotton"));
            Assert.AreEqual(CrowdTintKind.Hat, CrowdBuilder.TintKindOf("Jacques_CapCloth"));
            Assert.AreEqual(CrowdTintKind.Hair, CrowdBuilder.TintKindOf("SoldierA_Hair"));
            Assert.IsNull(CrowdBuilder.TintKindOf("Cook_Head"), "da không đổi màu");
            Assert.IsNull(CrowdBuilder.TintKindOf("Cook_ForearmSkin"));
            Assert.IsNull(CrowdBuilder.TintKindOf("SoldierB_PlainDarkLeather"));
            Assert.IsNull(CrowdBuilder.TintKindOf(null));
        }

        // ---------- Vật liệu biến thể dùng chung ----------

        [Test]
        public void SharedTintMaterialsAreReusedAcrossNpcsAndKeepTheBaseTexture()
        {
            CrowdVariation.ClearPool();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.IsNotNull(shader);
            var baseMat = new Material(shader) { name = "Test_Cotton" };
            var red = new Color(0.8f, 0.2f, 0.2f);
            var blue = new Color(0.2f, 0.2f, 0.8f);

            var a = CrowdVariation.Shared(baseMat, red);
            Assert.AreSame(a, CrowdVariation.Shared(baseMat, red), "cùng (material, màu) dùng chung một bản");
            Assert.AreNotSame(a, CrowdVariation.Shared(baseMat, blue));
            Assert.AreSame(a, CrowdVariation.Shared(a, red), "gọi lại trên bản biến thể vẫn về cùng material gốc");
            Assert.AreEqual(2, CrowdVariation.PooledMaterialCount);
            Assert.AreEqual(red.r, a.GetColor("_BaseColor").r, 0.01f);
            Assert.AreSame(shader, a.shader);
            Object.DestroyImmediate(baseMat);
            CrowdVariation.ClearPool();
            Assert.AreEqual(0, CrowdVariation.PooledMaterialCount);
        }

        [Test]
        public void VariationAppliesTheSameMaterialToNpcsWithTheSameSeed()
        {
            CrowdVariation.ClearPool();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var baseMat = new Material(shader) { name = "Test_Cotton" };
            var created = new List<Object> { baseMat };
            Renderer Npc(int seed)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                created.Add(go);
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterials = new[] { baseMat };
                var v = go.AddComponent<CrowdVariation>();
                v.seed = seed;
                v.slots = new[] { new CrowdTintSlot { renderer = r, materialIndex = 0, kind = CrowdTintKind.Cotton } };
                v.Apply();
                return r;
            }
            var first = Npc(5);
            var second = Npc(5);
            Assert.AreSame(first.sharedMaterial, second.sharedMaterial, "cùng seed cùng vật liệu: SRP Batcher gộp được");
            Assert.AreNotSame(baseMat, first.sharedMaterial);
            var different = new HashSet<Material>();
            for (int seed = 0; seed < 30; seed++)
                different.Add(Npc(seed).sharedMaterial);
            Assert.LessOrEqual(different.Count, CrowdPalette.ColorsOf(CrowdTintKind.Cotton).Length, "số vật liệu bị chặn bởi cỡ bảng màu");
            Assert.GreaterOrEqual(different.Count, 4);
            foreach (var o in created)
                Object.DestroyImmediate(o);
            CrowdVariation.ClearPool();
        }
    
        [Test]
        public void WalkersPassingShoulderToShoulderYield()
        {
            // Verify 14/10: hai người đi ngược chiều lệch ngang 0,45 m (ngoài hình nón 35°/50°) đi qua nhau, vai chồng 14 cm.
            var me = Vector3.zero;
            var other = new Vector3(0.45f, 0f, 0.4f);
            Assert.IsTrue(CrowdAvoidLogic.Blocks(2, me, Vector3.forward, 1, other, true, Vector3.back), "sắp chạm vai: số hiệu lớn nhường");
            Assert.IsFalse(CrowdAvoidLogic.Blocks(2, me, Vector3.forward, 1, new Vector3(0.6f, 0f, 0.4f), true, Vector3.back),
                "lệch 0,6 m: đi qua nhau không chạm");
            Assert.IsTrue(CrowdAvoidLogic.InLane(me, Vector3.forward, new Vector3(0.3f, 0f, 1.5f), 2f));
            Assert.IsFalse(CrowdAvoidLogic.InLane(me, Vector3.forward, new Vector3(0.3f, 0f, -0.2f), 2f), "sau lưng");
        }
    }
}
