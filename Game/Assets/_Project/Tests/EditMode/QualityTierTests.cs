using HaiBanTay.Performance;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    public class QualityTierTests
    {
        static readonly string[] None = new string[0];

        [Test]
        public void WindowsPlayerPicksLaptop()
        {
            Assert.AreEqual(QualityTier.Laptop, QualityTierManager.Resolve(RuntimePlatform.WindowsPlayer, None));
        }

        [TestCase(RuntimePlatform.LinuxPlayer)]
        [TestCase(RuntimePlatform.LinuxEditor)]
        [TestCase(RuntimePlatform.WindowsEditor)]
        public void OtherPlatformsPickPc(RuntimePlatform platform)
        {
            Assert.AreEqual(QualityTier.PC, QualityTierManager.Resolve(platform, None));
        }

        [Test]
        public void TierArgumentOverrides()
        {
            Assert.AreEqual(QualityTier.PC,
                QualityTierManager.Resolve(RuntimePlatform.WindowsPlayer, new[] { "game.exe", "-tier", "pc" }));
            Assert.AreEqual(QualityTier.Laptop,
                QualityTierManager.Resolve(RuntimePlatform.LinuxPlayer, new[] { "-tier", "LAPTOP" }));
        }

        [Test]
        public void VSyncMatchesTier()
        {
            Assert.AreEqual(2, DisplaySetup.VSyncFor(QualityTier.Laptop));
            Assert.AreEqual(1, DisplaySetup.VSyncFor(QualityTier.PC));
        }
    }
}
