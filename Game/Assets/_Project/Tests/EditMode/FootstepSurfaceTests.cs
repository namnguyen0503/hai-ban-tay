using HaiBanTay.Audio;
using NUnit.Framework;

namespace HaiBanTay.Tests
{
    public class FootstepSurfaceTests
    {
        [TestCase("COL_QuayFoundation", null, FootstepKind.Stone)]
        [TestCase("COL_Sidewalk_1", null, FootstepKind.Stone)]
        [TestCase("COL_CafePavement", null, FootstepKind.Stone)]
        [TestCase("GalleyFloor", null, FootstepKind.Stone)]
        [TestCase("Step_03", "ShipStairs", FootstepKind.Metal)]
        [TestCase("Ladder", null, FootstepKind.Metal)]
        [TestCase("ShipFloor", null, FootstepKind.Wood)]
        [TestCase("HoldFloor", null, FootstepKind.Wood)]
        [TestCase(null, null, FootstepKind.Wood)]
        public void ClassifiesByColliderName(string name, string parent, FootstepKind expected)
        {
            Assert.AreEqual(expected, FootstepSurface.Classify(name, parent));
        }

        [Test]
        public void KeysCycleVariantsWithoutRepeatingNeighbours()
        {
            for (int i = 0; i < 12; i++)
            {
                Assert.AreNotEqual(FootstepSurface.Key(FootstepKind.Wood, i), FootstepSurface.Key(FootstepKind.Wood, i + 1));
                StringAssert.StartsWith("SFX/footstep_metal_", FootstepSurface.Key(FootstepKind.Metal, i));
            }
            Assert.AreEqual("SFX/footstep_stone_1", FootstepSurface.Key(FootstepKind.Stone, -4));
        }
    }
}
