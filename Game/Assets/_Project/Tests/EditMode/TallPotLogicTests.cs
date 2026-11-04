using HaiBanTay.Shift;
using NUnit.Framework;

namespace HaiBanTay.Tests
{
    /// <summary>Vòng đời nồi cao (17/10): một vật, một trạng thái, chỉ đi theo chuỗi hợp lệ.</summary>
    public class TallPotLogicTests
    {
        static readonly TallPotStep[] FullCycle =
        {
            TallPotStep.TowOut, TallPotStep.LiftUp, TallPotStep.Serve, TallPotStep.LiftDown, TallPotStep.TowBack
        };

        static void Dirty(TallPotLogic pot)
        {
            foreach (var step in FullCycle)
                Assert.IsTrue(pot.Apply(step), step.ToString());
        }

        [Test]
        public void StartsCleanAtHome()
        {
            var pot = new TallPotLogic();
            Assert.AreEqual(TallPotState.HomeClean, pot.State);
            Assert.IsTrue(pot.IsClean && pot.IsHome);
            Assert.AreEqual(0, pot.Cycles);
            Assert.AreEqual(TallPotStep.TowOut, pot.Next);
        }

        [Test]
        public void FullCycleReturnsToCleanAtHomeAfterScrubAndRinse()
        {
            var pot = new TallPotLogic();
            Dirty(pot);
            Assert.AreEqual(TallPotState.HomeDirty, pot.State);
            Assert.IsFalse(pot.IsClean);
            Assert.IsFalse(pot.CanApply(TallPotStep.Wash), "chưa cọ, chưa tráng");
            Assert.IsFalse(pot.MarkRinsed(), "phải cọ trước khi tráng");
            Assert.IsTrue(pot.MarkScrubbed());
            Assert.IsFalse(pot.CanApply(TallPotStep.Wash), "chưa tráng");
            Assert.IsTrue(pot.MarkRinsed());
            Assert.IsTrue(pot.Apply(TallPotStep.Wash));
            Assert.AreEqual(TallPotState.HomeClean, pot.State);
            Assert.AreEqual(1, pot.Cycles);
            Assert.IsFalse(pot.Scrubbed || pot.Rinsed, "vòng sau cọ lại từ đầu");
        }

        [Test]
        public void OnlyTheNextStepIsValidAndAnInvalidStepChangesNothing()
        {
            var pot = new TallPotLogic();
            foreach (TallPotStep step in System.Enum.GetValues(typeof(TallPotStep)))
            {
                if (step == pot.Next) continue;
                Assert.IsFalse(pot.Apply(step), step.ToString());
                Assert.AreEqual(TallPotState.HomeClean, pot.State);
            }
            Assert.IsTrue(pot.Apply(TallPotStep.TowOut));
            Assert.IsFalse(pot.Apply(TallPotStep.TowOut), "đã kéo ra rồi");
            Assert.IsFalse(pot.Apply(TallPotStep.TowBack), "chưa nấu, không thể kéo về cọ");
            Assert.AreEqual(TallPotState.AtStoveClean, pot.State);
        }

        [Test]
        public void ScrubAndRinseOnlyCountAtTheWashSpot()
        {
            var pot = new TallPotLogic();
            Assert.IsFalse(pot.MarkScrubbed());
            pot.Apply(TallPotStep.TowOut);
            pot.Apply(TallPotStep.LiftUp);
            pot.Apply(TallPotStep.Serve);
            Assert.IsFalse(pot.MarkScrubbed(), "nồi còn trên bếp");
            Assert.IsFalse(pot.Scrubbed);
        }

        [Test]
        public void StatesPartitionByPlaceAndCleanliness()
        {
            var pot = new TallPotLogic();
            var seen = new System.Collections.Generic.List<(bool home, bool stove, bool hob)>();
            seen.Add((pot.IsHome, pot.IsAtStove, pot.IsOnHob));
            foreach (var step in FullCycle)
            {
                pot.Apply(step);
                seen.Add((pot.IsHome, pot.IsAtStove, pot.IsOnHob));
            }
            foreach (var place in seen)
                Assert.AreEqual(1, (place.home ? 1 : 0) + (place.stove ? 1 : 0) + (place.hob ? 1 : 0), "nồi chỉ ở đúng một chỗ");
        }

        [Test]
        public void FastForwardPutsTheMontagePotOnTheHob()
        {
            var pot = new TallPotLogic();
            Assert.AreEqual(2, pot.FastForwardTo(TallPotState.OnHobClean));
            Assert.AreEqual(TallPotState.OnHobClean, pot.State);
            Assert.AreEqual(0, pot.FastForwardTo(TallPotState.OnHobClean), "đã ở đích thì đứng yên");
        }

        [Test]
        public void FastForwardWrapsAroundAndScrubsOnTheWay()
        {
            var pot = new TallPotLogic();
            pot.FastForwardTo(TallPotState.OnHobDirty);
            int steps = pot.FastForwardTo(TallPotState.OnHobClean);
            Assert.AreEqual(5, steps, "xuống, kéo về, cọ+tráng, kéo ra, nhấc lên");
            Assert.AreEqual(1, pot.Cycles, "đi vòng qua chỗ rửa thì cọ xong một vòng");
            Assert.AreEqual(TallPotState.OnHobClean, pot.State);
        }

        [Test]
        public void FastForwardNeverStallsFromAnyStateToAnyState()
        {
            foreach (TallPotState from in System.Enum.GetValues(typeof(TallPotState)))
                foreach (TallPotState to in System.Enum.GetValues(typeof(TallPotState)))
                {
                    var pot = new TallPotLogic();
                    pot.FastForwardTo(from);
                    pot.FastForwardTo(to);
                    Assert.AreEqual(to, pot.State, $"{from} → {to}");
                }
        }

        [Test]
        public void ResetPutsTheCleanPotBackHome()
        {
            var pot = new TallPotLogic();
            pot.FastForwardTo(TallPotState.HomeDirty);
            pot.MarkScrubbed();
            pot.Reset();
            Assert.AreEqual(TallPotState.HomeClean, pot.State);
            Assert.IsFalse(pot.Scrubbed);
        }
    }
}
