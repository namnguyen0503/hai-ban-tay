using HaiBanTay.Ship;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    public class WorldStarsTests
    {
        [TestCase(5.5f), TestCase(6f), TestCase(8f), TestCase(12f), TestCase(18.5f)]
        public void BrightSkyHasNoStars(float hour) =>
            Assert.AreEqual(0f, TimeOfDayLogic.StarVisibility(TimeOfDayLogic.Evaluate(hour)));

        [TestCase(4f), TestCase(21f)]
        public void DarkNightStillHasStars(float hour) =>
            Assert.Greater(TimeOfDayLogic.StarVisibility(TimeOfDayLogic.Evaluate(hour)), .9f);

        [Test]
        public void CustomBrightSkySuppressesStarsEvenWithNightAtOne() =>
            Assert.AreEqual(0f, TimeOfDayLogic.StarVisibility(new WorldLightingState { Sky = Color.white, Night = 1f }));
    }
}
