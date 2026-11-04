using HaiBanTay.Ship;
using NUnit.Framework;

namespace HaiBanTay.Tests
{
    public class BackdropMoverTests
    {
        [Test]
        public void ProgressRampsThenMovesAtConstantSpeed()
        {
            Assert.AreEqual(0f, BackdropMover.Progress(0f, 50f, 6f), 1e-5f);
            Assert.AreEqual(1f, BackdropMover.Progress(50f, 50f, 6f), 1e-4f);
            Assert.AreEqual(1f, BackdropMover.Progress(80f, 50f, 6f), 1e-5f);
            // Liền mạch ở cuối đoạn tăng tốc.
            Assert.AreEqual(BackdropMover.Progress(5.999f, 50f, 6f), BackdropMover.Progress(6.001f, 50f, 6f), 1e-3f);
            // Sau tăng tốc, quãng tăng đều theo thời gian.
            float a = BackdropMover.Progress(20f, 50f, 6f) - BackdropMover.Progress(10f, 50f, 6f);
            float b = BackdropMover.Progress(40f, 50f, 6f) - BackdropMover.Progress(30f, 50f, 6f);
            Assert.AreEqual(a, b, 1e-4f);
        }

        [Test]
        public void QuayIsClearlyMovingWithinFifteenSeconds()
        {
            // Người chơi đứng lan can lúc nhổ neo: sau ~13 s bến phải lùi rõ (≥ 15% quãng).
            Assert.GreaterOrEqual(BackdropMover.Progress(13f, 50f, 6f), 0.15f);
        }
    }
}
