using HaiBanTay.Performance;
using HaiBanTay.Ship;
using NUnit.Framework;

namespace HaiBanTay.Tests
{
    public class LightBudgetLogicTests
    {
        [Test]
        public void EveryGroupSizeAndRepeatedTransitionStaysWithinBudget()
        {
            for (int exterior = 0; exterior <= 6; exterior++)
                for (int interior = 0; interior <= 6; interior++)
                    foreach (var region in new[] { ShipRegion.Exterior, ShipRegion.Interior, ShipRegion.Exterior })
                    {
                        var a = LightBudgetLogic.Resolve(region, exterior, interior);
                        int enabled = a.ExteriorCount + a.InteriorCount;
                        int shadowed = a.HasShadow ? 1 : 0;
                        Assert.LessOrEqual(enabled - shadowed, 2);
                        Assert.LessOrEqual(shadowed, 1);
                        Assert.LessOrEqual(a.ExteriorCount, exterior);
                        Assert.LessOrEqual(a.InteriorCount, interior);
                    }
        }

        [Test]
        public void SunAndFireExchangeShadowsAndMissingPriorityFallsBack()
        {
            var outside = LightBudgetLogic.Resolve(ShipRegion.Exterior, 1, 2);
            var inside = LightBudgetLogic.Resolve(ShipRegion.Interior, 1, 2);
            Assert.AreEqual(ShipRegion.Exterior, outside.ShadowRegion);
            Assert.AreEqual(ShipRegion.Interior, inside.ShadowRegion);
            Assert.AreEqual(3, outside.ExteriorCount + outside.InteriorCount);
            Assert.AreEqual(3, inside.ExteriorCount + inside.InteriorCount);
            Assert.AreEqual(ShipRegion.Exterior, LightBudgetLogic.Resolve(ShipRegion.Interior, 1, 0).ShadowRegion);
            Assert.AreEqual(ShipRegion.Interior, LightBudgetLogic.Resolve(ShipRegion.Exterior, 0, 2).ShadowRegion);
        }
    }
}
