using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>Gọt khoai như thật (yêu cầu 14/10): từng nhát một dải dọc củ, xoay củ, vỏ mất tới đâu ruột lộ tới đó.</summary>
    public class PotatoPeelTests
    {
        [Test]
        public void EachStrokePeelsOneBandThenThePotatoTurns()
        {
            var logic = new PotatoPeelLogic();
            float band = 1f / PotatoPeelLogic.Bands;
            Assert.IsFalse(logic.Add(band * 0.5f));
            Assert.AreEqual(0, logic.Band);
            Assert.AreEqual(0.5f, logic.Stroke, 1e-4f);
            Assert.IsTrue(logic.Add(band * 0.6f), "hết nhát thì báo xoay củ");
            Assert.IsTrue(logic.Turning);
            Assert.IsFalse(logic.Add(1f), "đang xoay củ thì rê không gọt thêm");
            logic.NextBand();
            Assert.AreEqual(1, logic.Band);
            Assert.AreEqual(0f, logic.Stroke);
            for (int b = 1; b < PotatoPeelLogic.Bands; b++)
            {
                Assert.IsTrue(logic.Add(1f));
                logic.NextBand();
            }
            Assert.IsTrue(logic.Done);
            Assert.AreEqual(1f, logic.Progress, 1e-4f);
        }

        [Test]
        public void APotatoTakesTheSameDragAsOneFullPeel()
        {
            // Gọt trắng một củ cần đúng quãng rê của một lần gọt cũ (StrokePixelsForFull), chia đều cho các dải.
            var logic = new PotatoPeelLogic();
            float pixels = 0f;
            for (int i = 0; i < 100000 && !logic.Done; i++)
            {
                if (logic.Turning) { logic.NextBand(); continue; }
                logic.Add(PeelStation.StrokeProgress(5f, 1f / 60f));
                pixels += 5f;
            }
            Assert.IsTrue(logic.Done);
            Assert.AreEqual(PeelStation.StrokePixelsForFull, pixels, 5f * PotatoPeelLogic.Bands);
        }

        [TestCase(false), TestCase(true)]
        public void SkinComesOffBandByBandAndTheFleshStaysInside(bool turnip)
        {
            var potato = new PotatoMesh(7, 1f, turnip);
            try
            {
                int full = (int)potato.Mesh.GetIndexCount(0);
                Assert.Greater(full, 0);
                potato.SetPeeled(0, 0, PotatoMesh.KnifeU(0.5f));
                int half = (int)potato.Mesh.GetIndexCount(0);
                Assert.Less(half, full, "dao đi tới đâu vỏ mất tới đó");
                Assert.Greater(half, full * (PotatoPeelLogic.Bands - 1) / PotatoPeelLogic.Bands, "mới gọt nửa một dải");
                potato.SetPeeled(3, 3, 1.01f);
                Assert.AreEqual(full * (PotatoPeelLogic.Bands - 3) / PotatoPeelLogic.Bands, (int)potato.Mesh.GetIndexCount(0), "ba dải đã gọt hết vỏ");
                potato.SetPeeled(PotatoPeelLogic.Bands, -1, 1f);
                Assert.AreEqual(0, (int)potato.Mesh.GetIndexCount(0), "gọt đủ dải thì hết vỏ");
                Assert.Greater((int)potato.Mesh.GetIndexCount(1), 0, "ruột còn nguyên");
                for (int b = 0; b < PotatoPeelLogic.Bands; b++)
                    for (float u = 0.05f; u < 1f; u += 0.1f)
                        for (float d = -0.49f; d <= 0.49f; d += 0.14f)
                        {
                            float phi = PotatoMesh.BandCenter(b) + d * PotatoMesh.BandAngle;
                            var skin = potato.SkinPoint(u, phi);
                            var flesh = potato.FleshPoint(u, phi, b);
                            Assert.Less(new Vector2(flesh.x, flesh.y).magnitude, new Vector2(skin.x, skin.y).magnitude,
                                $"ruột không lòi ra ngoài vỏ (dải {b}, u {u:0.00})");
                        }
            }
            finally { Object.DestroyImmediate(potato.Mesh); }
        }

        [TestCase(false), TestCase(true)]
        public void KnifeRidesTheBandFacingOutward(bool turnip)
        {
            var potato = new PotatoMesh(3, 1f, turnip);
            try
            {
                for (int b = 0; b < PotatoPeelLogic.Bands; b++)
                    for (float s = 0f; s <= 1f; s += 0.25f)
                    {
                        potato.Frame(b, PotatoMesh.KnifeU(s), out var p, out var n, out var far);
                        Assert.Greater(Vector3.Dot(n, new Vector3(p.x, p.y, 0f).normalized), 0.15f, "pháp tuyến hướng ra ngoài củ");
                        Assert.Greater(far.z, 0f, "dải vỏ đã cắt nằm về phía đầu xa");
                    }
                Assert.Greater(potato.CutLength(0, 1f), potato.Radii.z * 2f, "dải vỏ dài theo mặt cong của củ");
                Assert.Greater(potato.BandWidth(0, 0.5f), potato.BandWidth(0, 0.05f) * 2f, "dải rộng ở giữa củ, hẹp ở hai đầu");
            }
            finally { Object.DestroyImmediate(potato.Mesh); }
        }

        [Test]
        public void TurnipIsRoundWithATaperedRoot()
        {
            var turnip = new PotatoMesh(5, 1f, true);
            var potato = new PotatoMesh(5);
            try
            {
                Assert.AreEqual("Turnip", turnip.Mesh.name);
                // Củ cải tròn hơn khoai: hai bán trục ngang gần bằng nhau.
                Assert.Less(Mathf.Abs(turnip.Radii.x - turnip.Radii.y) / turnip.Radii.x, 0.15f);
                // Phình nhất về phía cuống (đầu xa), đầu gần thuôn thành rễ dài hơn đầu cuống.
                float Width(PotatoMesh m, float u) => new Vector2(m.SkinPoint(u, 0f).x, m.SkinPoint(u, Mathf.PI * 0.5f).y).magnitude;
                Assert.Greater(Width(turnip, 0.62f), Width(turnip, 0.38f));
                Assert.Less(Width(turnip, 0.12f), Width(potato, 0.12f) * 0.75f, "rễ thuôn nhỏ hơn đầu củ khoai");
                Assert.Greater(-turnip.SkinPoint(0f, 0f).z, turnip.SkinPoint(1f, 0f).z * 1.2f, "rễ kéo dài quá nửa thân");
            }
            finally { Object.DestroyImmediate(turnip.Mesh); Object.DestroyImmediate(potato.Mesh); }
        }
    }
}
