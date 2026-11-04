using HaiBanTay.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>Mặt nạ bẩn (17/10): vết bẩn chỉ mất ở chỗ bàn chải đi qua.</summary>
    public class DirtMaskTests
    {
        static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);

        [Test]
        public void StartsCleanUntilFilled()
        {
            var mask = new DirtMask();
            Assert.AreEqual(0f, mask.Remaining, 1e-6f);
            mask.Fill();
            Assert.AreEqual(1f, mask.Remaining, 1e-6f);
            Assert.AreEqual(0f, mask.Cleaned, 1e-6f);
        }

        [Test]
        public void DiscMaskIgnoresTheCornersOutsideTheRim()
        {
            var mask = new DirtMask(64, disc: true);
            mask.Fill();
            Assert.AreEqual(0f, mask[0, 0], "góc ngoài vành không bẩn");
            Assert.IsFalse(mask.IsValid(0, 0));
            Assert.AreEqual(1f, mask[32, 32]);
            Assert.AreEqual(1f, mask.Remaining, 1e-6f, "độ phủ chỉ tính vùng hợp lệ");
            var square = new DirtMask(64, disc: false);
            square.Fill();
            Assert.AreEqual(1f, square[0, 0]);
        }

        [Test]
        public void ScrubOnlyCleansWhereTheBrushPassed()
        {
            var mask = new DirtMask();
            mask.Fill();
            mask.Scrub(new Vector2(0.2f, 0.5f), new Vector2(0.5f, 0.5f), 0.08f, 1f);
            Assert.Less(mask[(int)(0.35f * 64), 32], 0.05f, "dưới đường chải đã sạch");
            Assert.AreEqual(1f, mask[(int)(0.7f * 64), 32], "xa đường chải còn nguyên");
            Assert.AreEqual(1f, mask[22, 57], "ngoài bán kính còn nguyên");
            Assert.Greater(mask.Cleaned, 0f);
            Assert.Less(mask.Cleaned, 0.2f);
        }

        [Test]
        public void ScrubAccumulatesAcrossPasses()
        {
            var mask = new DirtMask();
            mask.Fill();
            mask.Scrub(new Vector2(0.4f, 0.5f), new Vector2(0.6f, 0.5f), 0.08f, 0.4f);
            float once = mask.Cleaned;
            mask.Scrub(new Vector2(0.6f, 0.5f), new Vector2(0.4f, 0.5f), 0.08f, 0.4f);
            Assert.Greater(mask.Cleaned, once);
        }

        [Test]
        public void ARaggedStrokeCoversTheWholeDiscAndReportsCleanEnough()
        {
            var mask = new DirtMask();
            mask.Fill();
            float y = 0.1f;
            bool right = true;
            while (y < 0.9f)
            {
                var a = new Vector2(right ? 0.05f : 0.95f, y);
                var b = new Vector2(right ? 0.95f : 0.05f, y);
                mask.Scrub(a, b, 0.07f, 1f);
                y += 0.06f;
                right = !right;
            }
            Assert.GreaterOrEqual(mask.Cleaned, 0.9f, "quét luống từ mép này sang mép kia phủ hết mặt tròn");
        }

        [Test]
        public void SpeedDoesNotChangeHowMuchOnePassCleans()
        {
            var slow = new DirtMask();
            var fast = new DirtMask();
            slow.Fill();
            fast.Fill();
            var a = new Vector2(0.2f, 0.5f);
            var b = new Vector2(0.8f, 0.5f);
            for (int i = 0; i < 60; i++)
                slow.Scrub(Vector2.Lerp(a, b, i / 60f), Vector2.Lerp(a, b, (i + 1f) / 60f), 0.07f, 0.5f);
            fast.Scrub(a, b, 0.07f, 0.5f);
            Assert.AreEqual(slow[32, 32], fast[32, 32], 0.06f, "cùng quãng, cùng sức một lượt thì sạch như nhau, dù chia nhỏ hay đi một nhát");
            Assert.AreEqual(0.5f, fast[32, 32], 0.15f, "một lượt lấy đi khoảng sức chải ở đường giữa");
        }

        [Test]
        public void StationaryBrushStrengthZeroCleansNothing()
        {
            var mask = new DirtMask();
            mask.Fill();
            Assert.AreEqual(0f, mask.Scrub(Centre, Centre, 0.08f, 0f));
            Assert.AreEqual(1f, mask.Remaining, 1e-6f);
        }

        [Test]
        public void FadeRemovesTheLeftoverSpecks()
        {
            var mask = new DirtMask();
            mask.Fill();
            mask.Dab(Centre, 3f, 0.9f);
            Assert.Greater(mask.Remaining, 0f);
            mask.Fade(0.2f);
            Assert.AreEqual(0f, mask.Remaining, 1e-4f);
        }

        [Test]
        public void CopyToWritesOneByteCellInBlackOnClean()
        {
            var mask = new DirtMask(8);
            mask.Fill();
            var bytes = new byte[64];
            mask.CopyTo(bytes);
            Assert.AreEqual(255, bytes[4 * 8 + 4]);
            Assert.AreEqual(0, bytes[0], "góc ngoài đĩa");
            mask.Clear();
            mask.CopyTo(bytes);
            foreach (var b in bytes) Assert.AreEqual(0, b);
        }

        [Test]
        public void OutOfRangeBrushIsSafe()
        {
            var mask = new DirtMask();
            mask.Fill();
            Assert.DoesNotThrow(() => mask.Scrub(new Vector2(-2f, -2f), new Vector2(3f, 3f), 0.1f, 1f));
            Assert.DoesNotThrow(() => mask.Dab(new Vector2(5f, 5f), 0.1f, 1f));
        }
    }
}
