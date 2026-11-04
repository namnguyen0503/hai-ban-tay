using HaiBanTay.Performance;
using NUnit.Framework;

namespace HaiBanTay.Tests
{
    public class FrameCapTests
    {
        static readonly string[] None = new string[0];

        static (int, int) Of(FrameCap.Plan p) => (p.VSync, p.TargetFps);

        [Test]
        public void NoGraphicsRunsUncapped()
        {
            Assert.AreEqual((0, -1), Of(FrameCap.Resolve(QualityTier.PC, true, true, true, None, null)));
            Assert.AreEqual((0, -1), Of(FrameCap.Resolve(QualityTier.PC, true, true, true, new[] { "-fps", "30" }, null)));
        }

        [Test]
        public void GpuBatchRunsUncapped()
        {
            Assert.AreEqual((0, -1), Of(FrameCap.Resolve(QualityTier.PC, true, true, false, None, null)));
        }

        [Test]
        public void EditorPlayIsCappedAt30()
        {
            Assert.AreEqual((0, 30), Of(FrameCap.Resolve(QualityTier.PC, true, false, false, None, null)));
        }

        [Test]
        public void PlayerFollowsTierVSync()
        {
            Assert.AreEqual((2, -1), Of(FrameCap.Resolve(QualityTier.Laptop, false, false, false, None, null)));
            Assert.AreEqual((1, -1), Of(FrameCap.Resolve(QualityTier.PC, false, false, false, None, null)));
        }

        [Test]
        public void ArgumentAndEnvironmentOverride()
        {
            Assert.AreEqual((0, 45), Of(FrameCap.Resolve(QualityTier.Laptop, false, false, false, new[] { "game", "-fps", "45" }, null)));
            Assert.AreEqual((0, 20), Of(FrameCap.Resolve(QualityTier.PC, true, false, false, None, "20")));
            Assert.AreEqual((0, 30), Of(FrameCap.Resolve(QualityTier.PC, true, true, false, None, "30")));
            Assert.AreEqual((0, -1), Of(FrameCap.Resolve(QualityTier.Laptop, false, false, false, new[] { "-FPS", "0" }, null)));
            Assert.AreEqual((0, 45), Of(FrameCap.Resolve(QualityTier.PC, false, false, false, new[] { "-fps", "45" }, "20")));
            Assert.AreEqual((1, -1), Of(FrameCap.Resolve(QualityTier.PC, false, false, false, new[] { "-fps", "x" }, "")));
        }

        [Test]
        public void SettingLimitBeatsDefaultsButNotOverrides()
        {
            Assert.AreEqual((0, 60), Of(FrameCap.Resolve(QualityTier.Laptop, false, false, false, None, null, 60, 0)));
            Assert.AreEqual((0, 120), Of(FrameCap.Resolve(QualityTier.PC, true, false, false, None, null, 120, 0)));
            Assert.AreEqual((0, -1), Of(FrameCap.Resolve(QualityTier.PC, false, false, false, None, null, -1, 0)));
            Assert.AreEqual((0, 45), Of(FrameCap.Resolve(QualityTier.PC, false, false, false, new[] { "-fps", "45" }, null, 120, 0)));
            Assert.AreEqual((0, -1), Of(FrameCap.Resolve(QualityTier.PC, false, true, false, None, null, 30, 0)));
        }

        [Test]
        public void PhoneDefaultsToThirtyAndKeepsExplicitFpsChoices()
        {
            Assert.AreEqual((0, 30), Of(FrameCap.Resolve(QualityTier.PC, false, false, false, None, null, 0, 120)));
            Assert.AreEqual((0, 60), Of(FrameCap.Resolve(QualityTier.Laptop, false, false, false, None, null, 60, 120)));
            Assert.AreEqual((0, 30), Of(FrameCap.Resolve(QualityTier.PC, false, false, false, None, null, 30, 120)));
            Assert.AreEqual((0, FrameCap.MobileUncapped), Of(FrameCap.Resolve(QualityTier.PC, false, false, false, None, null, -1, 120)));
        }
    }
}
