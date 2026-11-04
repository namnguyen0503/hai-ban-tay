using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>Logic các việc thêm ở M9: cào lò, quét bếp, nhặt rau, kéo chảo, giữ thăng bằng, thùng than.</summary>
    public sealed class ChoreTests
    {
        [Test]
        public void RakeNeedsPullingTowardYouAndFillsTheBucket()
        {
            var rake = new RakeLogic { PixelsPerStroke = 100f, StrokesPerBucket = 2 };
            Assert.IsFalse(rake.Tick(true, true, +500f), "đẩy chuột ra xa không tính");
            Assert.IsFalse(rake.Tick(true, false, -500f), "không nhìn cửa tro không tính");
            Assert.IsFalse(rake.Tick(true, true, -60f));
            Assert.IsTrue(rake.Tick(true, true, -60f), "đủ quãng kéo thì xong một nhát");
            Assert.AreEqual(1, rake.InBucket);

            rake.Tick(true, true, -60f);
            rake.Tick(false, true, 0f);
            Assert.IsFalse(rake.Tick(true, true, -60f), "nhả chuột thì nhát đang dở bắt đầu lại");
            Assert.IsTrue(rake.Tick(true, true, -60f));
            Assert.IsTrue(rake.BucketFull);
            Assert.IsFalse(rake.Tick(true, true, -500f), "xô đầy thì không cào thêm");

            rake.Empty();
            Assert.AreEqual(0, rake.InBucket);
            Assert.AreEqual(2, rake.Strokes);
        }

        [Test]
        public void RakingLetsTheFireBurnHigherAgain()
        {
            var fire = new FireLogic();
            fire.Set(FireLogic.FullFuel, 0f, ash: 0.8f);
            float choked = fire.Target;
            fire.Rake(0.8f);
            Assert.Greater(fire.Target, choked + 0.4f);
        }

        [Test]
        public void SweepCleansScatteredSpotsOneByOne()
        {
            var sweep = new SweepLogic(4) { PixelsPerSpot = 100f };
            Assert.AreEqual(3, sweep.Scatter(3));
            Assert.AreEqual(3, sweep.Dirty);
            Assert.IsFalse(sweep.IsDirty(3));
            Assert.IsFalse(sweep.Tick(1, false, new Vector2(80f, 0f), 0.1f), "không giữ chuột thì không quét");
            Assert.IsFalse(sweep.Tick(1, true, new Vector2(60f, 0f), 0.1f));
            Assert.IsTrue(sweep.Tick(1, true, new Vector2(60f, 0f), 0.1f));
            Assert.IsFalse(sweep.IsDirty(1));
            Assert.AreEqual(2, sweep.Dirty);
            Assert.AreEqual(2, sweep.Scatter(5), "chỉ còn hai chỗ sạch để rải");
            Assert.AreEqual(0, sweep.CleanOne());
        }

        [Test]
        public void SortingKeepsGoodLeavesRightAndWiltedLeft()
        {
            var sort = new SortLogic(7) { FlickPixels = 50f, LeavesPerBunch = 4 };
            Assert.IsFalse(sort.TakeBunch(), "sọt chưa có rau");
            sort.AddBunches(1);
            Assert.IsTrue(sort.TakeBunch());
            Assert.AreEqual(0, sort.Pending);

            bool sawWilted = false, sawGood = false;
            for (int i = 0; i < 4; i++)
            {
                sawWilted |= sort.LeafWilted(i);
                sawGood |= !sort.LeafWilted(i);
            }
            Assert.IsTrue(sawWilted && sawGood, "mỗi bó có cả lá tốt và lá úa");

            // Gạt nhầm: lá nằm lại.
            float wrong = sort.CurrentWilted ? +60f : -60f;
            Assert.AreEqual(SortEvent.Wrong, sort.Tick(true, true, wrong));
            Assert.AreEqual(0, sort.Index);
            Assert.AreEqual(SortEvent.None, sort.Tick(true, true, -wrong * 2f), "phải nhả chuột trước khi gạt lá sau");

            var last = SortEvent.None;
            while (sort.HasBunch)
            {
                sort.Tick(false, true, 0f);
                last = sort.Tick(true, true, sort.CurrentWilted ? -60f : +60f);
                Assert.AreNotEqual(SortEvent.Wrong, last);
            }
            Assert.AreEqual(SortEvent.BunchDone, last);
            Assert.AreEqual(1, sort.Bunches);
            Assert.AreEqual(1, sort.Mistakes);
        }

        [Test]
        public void TowedPanFollowsAtArmsLengthAndSlipsWhenYouLookAway()
        {
            var tow = new TowLogic { Reach = 1f, MaxAngle = 40f, SlipDistance = 1.6f, DriftPerRoll = 0f };
            var pan = new Vector3(0f, 0f, 1f);
            var player = Vector3.zero;
            Assert.AreEqual(pan, tow.Step(pan, player, 0f, Vector3.right, 0.1f), "chưa bám thì chảo nằm yên");

            Assert.IsTrue(tow.Grip());
            player = new Vector3(0f, 0f, -0.5f); // lùi nửa mét
            pan = tow.Step(pan, player, 0f, Vector3.right, 0.1f);
            Assert.AreEqual(0.5f, pan.z, 1e-4f, "chảo theo sau, cách tay một sải");
            Assert.IsFalse(tow.CheckSlip(pan, player, Vector3.forward));

            Assert.IsTrue(tow.CheckSlip(pan, player, Vector3.right), "quay mặt khỏi chảo thì tuột tay");
            Assert.IsFalse(tow.Gripping);
            Assert.AreEqual(1, tow.Slips);
        }

        [Test]
        public void RollingShipPushesTheTowedPanSideways()
        {
            var tow = new TowLogic { DriftPerRoll = 0.5f };
            tow.Grip();
            var pan = tow.Step(new Vector3(0f, 0f, 1f), Vector3.zero, 1f, Vector3.right, 1f);
            Assert.AreEqual(0.5f, pan.x, 1e-4f);
        }

        [Test]
        public void BalanceTipsOverUnlessCounteredAgainstTheLean()
        {
            var free = new BalanceLogic { Push = 1f, Instability = 1f, CounterPerPixel = 0.01f };
            bool fell = false;
            for (int i = 0; i < 300 && !fell; i++)
                fell = free.Tick(0.5f, 0f, 0.02f);
            Assert.IsTrue(fell, "để mặc thì bao tuột");
            Assert.AreEqual(1, free.Slips);

            var held = new BalanceLogic { Push = 1f, Instability = 1f, CounterPerPixel = 0.01f };
            for (int i = 0; i < 300; i++)
            {
                // Người chơi rê chuột ngược chiều nghiêng.
                float dx = -held.Lean * 40f;
                Assert.IsFalse(held.Tick(0.5f, dx, 0.02f));
            }
            Assert.Less(Mathf.Abs(held.Lean), 0.5f);
        }

        [Test]
        public void SackPoursOnlyAfterUntyingAndWhenPulledDown()
        {
            var pour = new SackPourLogic { PixelsToEmpty = 100f, Potatoes = 4, SpillFrom = 0.5f };
            Assert.AreEqual(0, pour.Tick(true, -80f), "chưa cởi dây thì không dốc");
            pour.Untie();
            Assert.AreEqual(0, pour.Tick(true, +80f), "đẩy chuột lên không dốc");
            Assert.AreEqual(0, pour.Tick(false, -80f), "không giữ chuột thì không dốc");
            Assert.AreEqual(0, pour.Tick(true, -40f), "nghiêng chưa tới ngưỡng thì khoai chưa lăn ra");
            Assert.AreEqual(1, pour.Tick(true, -30f));
            Assert.AreEqual(3, pour.Tick(true, -500f), "kéo hết quãng thì ra hết số củ còn lại");
            Assert.IsTrue(pour.Empty);
            Assert.AreEqual(4, pour.Released);
            Assert.AreEqual(0, pour.Tick(true, -50f));
            pour.Reset();
            Assert.IsFalse(pour.Untied);
            Assert.AreEqual(4, pour.Finish());
        }

        [Test]
        public void CoalBinRunsOutAndRefillsFromTheHold()
        {
            var bin = new CoalBinLogic(3);
            Assert.AreEqual(3, bin.Stock);
            Assert.IsTrue(bin.Take());
            Assert.IsTrue(bin.Take());
            Assert.IsTrue(bin.Take());
            Assert.IsTrue(bin.Empty);
            Assert.IsFalse(bin.Take());
            bin.Refill();
            Assert.AreEqual(3, bin.Stock);
            bin.Set(-4);
            Assert.AreEqual(0, bin.Stock);
        }
    }
}
