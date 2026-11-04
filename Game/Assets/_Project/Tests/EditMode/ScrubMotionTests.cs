using System;
using HaiBanTay.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>Chuyển động bàn chải khi cọ (17/10): con trỏ ẩn trên mặt, đưa qua đưa lại theo tốc độ rê, nhấc khi thả chuột.</summary>
    public class ScrubMotionTests
    {
        const float Dt = 1f / 60f;
        const float Radius = 0.17f;
        const float BrushRadius = 0.045f;
        static readonly Func<Vector2, Vector2> Disc = s => Vector2.ClampMagnitude(s, Radius);

        [Test]
        public void CursorFollowsTheMouseOnlyWhileHeldAndStaysOnTheDisc()
        {
            var motion = new ScrubMotion { MetresPerPixel = 0.002f };
            motion.Reset(Vector2.zero);
            motion.Tick(false, new Vector2(100f, 0f), Dt, Disc);
            Assert.AreEqual(Vector2.zero, motion.Cursor, "thả chuột thì con trỏ đứng yên");
            motion.Tick(true, new Vector2(20f, 0f), Dt, Disc);
            Assert.AreEqual(0.04f, motion.Cursor.x, 1e-5f);
            for (int i = 0; i < 200; i++) motion.Tick(true, new Vector2(30f, 30f), Dt, Disc);
            Assert.LessOrEqual(motion.Cursor.magnitude, Radius + 1e-5f, "con trỏ không ra khỏi mặt tròn");
        }

        [Test]
        public void BrushLiftsWhenReleasedAndPressesWhenScrubbing()
        {
            var motion = new ScrubMotion();
            motion.Reset(Vector2.zero);
            Assert.IsFalse(motion.Pressing);
            for (int i = 0; i < 60; i++) motion.Tick(true, new Vector2(8f, 0f), Dt, Disc);
            Assert.AreEqual(0f, motion.Lift, 1e-4f, "giữ chuột thì áp mặt");
            Assert.IsTrue(motion.Pressing);
            for (int i = 0; i < 60; i++) motion.Tick(false, Vector2.zero, Dt, Disc);
            Assert.AreEqual(motion.LiftHeight, motion.Lift, 1e-4f, "thả chuột thì nhấc đầu chải");
            Assert.IsFalse(motion.Pressing);
        }

        [Test]
        public void HoldingStillStopsTheScrub()
        {
            var motion = new ScrubMotion();
            motion.Reset(Vector2.zero);
            for (int i = 0; i < 30; i++) motion.Tick(true, new Vector2(8f, 0f), Dt, Disc);
            Assert.IsTrue(motion.Pressing);
            for (int i = 0; i < 120; i++) motion.Tick(true, Vector2.zero, Dt, Disc);
            Assert.IsFalse(motion.Pressing, "giữ chuột mà không rê thì không cọ (hết sức dần)");
            Assert.AreEqual(0f, motion.Stroke.magnitude, 1e-4f);
        }

        [Test]
        public void StrokeAmplitudeScalesWithActivityAndNeverExceedsHalfAmplitude()
        {
            var motion = new ScrubMotion { MetresPerPixel = 0.0001f };
            motion.Reset(Vector2.zero);
            float max = 0f;
            for (int i = 0; i < 240; i++)
            {
                motion.Tick(true, new Vector2(10f, 0f), Dt, Disc);   // 600 px/s, hết sức
                max = Mathf.Max(max, motion.Stroke.magnitude);
            }
            Assert.AreEqual(motion.StrokeHalfAmplitude, max, 0.003f);
            Assert.LessOrEqual(max, motion.StrokeHalfAmplitude + 1e-5f);
            var slow = new ScrubMotion { MetresPerPixel = 0.0001f };
            slow.Reset(Vector2.zero);
            float slowMax = 0f;
            for (int i = 0; i < 240; i++)
            {
                slow.Tick(true, new Vector2(1f, 0f), Dt, Disc);       // 60 px/s, rê chậm
                slowMax = Mathf.Max(slowMax, slow.Stroke.magnitude);
            }
            Assert.Less(slowMax, max * 0.35f, "rê chậm thì nhát đưa ngắn");
        }

        [Test]
        public void StrokeRunsAlongTheDragDirection()
        {
            var motion = new ScrubMotion { MetresPerPixel = 0.0001f };
            motion.Reset(Vector2.zero);
            for (int i = 0; i < 120; i++) motion.Tick(true, new Vector2(0f, 10f), Dt, Disc);
            Assert.Greater(Vector2.Dot(motion.Heading, Vector2.up), 0.95f);
            float sideways = 0f;
            for (int i = 0; i < 60; i++)
            {
                motion.Tick(true, new Vector2(0f, 10f), Dt, Disc);
                sideways = Mathf.Max(sideways, Mathf.Abs(motion.Stroke.x));
            }
            Assert.Less(sideways, 0.004f, "đưa qua lại dọc hướng rê, không ngang");
        }

        [Test]
        public void StrokesFrequencyIsAboutFourToFivePerSecond()
        {
            var motion = new ScrubMotion { MetresPerPixel = 0.00001f };
            motion.Reset(Vector2.zero);
            for (int i = 0; i < 60; i++) motion.Tick(true, new Vector2(10f, 0f), Dt, Disc);   // đạt hết sức
            int crossings = 0;
            float last = Mathf.Sin(motion.Phase);
            for (int i = 0; i < 120; i++)    // 2 giây
            {
                motion.Tick(true, new Vector2(10f, 0f), Dt, Disc);
                float now = Mathf.Sin(motion.Phase);
                if (last < 0f && now >= 0f) crossings++;
                last = now;
            }
            Assert.That(crossings / 2f, Is.InRange(4f, 5f), "nhát/giây");
        }

        /// <summary>
        /// Rê ngược chiều (cuối luống): hướng nhát xoay dần chứ không lật, đầu chải không nhảy (brush17b: nồi cao, nhát 5 cm, lật hướng
        /// làm mũi lông nhảy 2,9 cm trong một khung 60 Hz).
        /// </summary>
        [Test]
        public void ReversingTheDragTurnsTheStrokeGraduallyWithoutAJump()
        {
            var motion = new ScrubMotion { MetresPerPixel = 0.0018f, StrokeHalfAmplitude = 0.05f };
            motion.Reset(Vector2.zero);
            Func<Vector2, Vector2> wide = c => Vector2.ClampMagnitude(c, 1f);
            for (int i = 0; i < 90; i++) motion.Tick(true, new Vector2(10f, 0.5f), Dt, wide);
            float worstJump = 0f;
            var lastStroke = motion.Stroke;
            for (int i = 0; i < 60; i++)
            {
                motion.Tick(true, new Vector2(-10f, 0.5f), Dt, wide);
                worstJump = Mathf.Max(worstJump, (motion.Stroke - lastStroke).magnitude);
                lastStroke = motion.Stroke;
            }
            // Nhát chạy hết sức đổi chỗ tối đa 2π·f·A·dt ≈ 2,4 cm/khung; hướng xoay dần thêm không quá 1 cm.
            float sweep = 2f * Mathf.PI * motion.StrokeHz * motion.StrokeHalfAmplitude * Dt;
            Assert.LessOrEqual(worstJump, sweep + 0.01f, $"nhát đổi chỗ {worstJump * 100f:F1} cm trong một khung khi rê ngược");
            Assert.Less(Vector2.Dot(motion.Heading, Vector2.right), -0.9f, "rê ngược 1 s thì nhát đã quay theo hướng mới");
        }

        /// <summary>Quét luống từ mép này sang mép kia với tốc độ chuột vừa phải: sạch 90% trong vài giây cọ thật.</summary>
        [Test]
        public void ARasterDragCleansTheDiscInSecondsNotInstantlyNorForever()
        {
            var motion = new ScrubMotion { MetresPerPixel = 0.00097f };
            var mask = new DirtMask();
            mask.Fill();
            motion.Reset(new Vector2(-Radius * 0.6f, -Radius * 0.85f));
            float t = 0f, dir = 1f, vertical = 1f, rowDone = 0f;
            while (mask.Cleaned < 0.9f && t < 60f)
            {
                // Luống ngang: tới gần mép đĩa ở hàng hiện tại thì đổi chiều và kéo sang hàng kế (đến mép trên/dưới thì đi ngược lại).
                float edge = Mathf.Sqrt(Mathf.Max(0f, Radius * Radius - motion.Cursor.y * motion.Cursor.y)) * 0.8f;
                var mouse = new Vector2(dir * 7.5f, 0f);              // 450 px/s
                if (rowDone <= 0f && motion.Cursor.x * dir >= edge - 0.002f)
                {
                    dir = -dir;
                    rowDone = 0.2f;
                    if (Mathf.Abs(motion.Cursor.y) > Radius * 0.8f && motion.Cursor.y * vertical > 0f) vertical = -vertical;
                }
                if (rowDone > 0f) { mouse = new Vector2(0f, vertical * 3f); rowDone -= Dt; }
                var seg = motion.Tick(true, mouse, Dt, Disc);
                float scale = 1f / (2f * Radius);
                mask.ScrubPass(seg.from * scale + new Vector2(0.5f, 0.5f), seg.to * scale + new Vector2(0.5f, 0.5f), BrushRadius * scale, 0.55f);
                t += Dt;
            }
            Assert.GreaterOrEqual(mask.Cleaned, 0.9f, $"quét luống phủ hết mặt (đã sạch {mask.Cleaned:P0} sau {t:F1} s)");
            Assert.That(t, Is.InRange(3f, 25f), "cọ xong sau vài giây chứ không tức thì, không vô tận");
        }

        /// <summary>
        /// Vùng với tới dời (nồi cao chao khi sóng): hàm kẹp trả một điểm đạt xa chỗ đầu chải (như BrushScrub.Recover kéo con trỏ ngoài tầm
        /// về một bước 1/10 quãng). Đầu chải đi về điểm đó trong hạn tốc độ, không vụt tới trong một khung (Brush17 có sóng: 4,3 cm/khung).
        /// </summary>
        [Test]
        public void AClampThatPullsFarAwayDoesNotTeleportTheHead()
        {
            var motion = new ScrubMotion { MetresPerPixel = 0.0018f, StrokeHalfAmplitude = 0.05f, MaxHeadSpeed = BrushScrub.MaxHeadSpeed };
            motion.Reset(Vector2.zero);
            int frame = 0;
            var recovered = new Vector2(0.3f, 0.1f);
            Func<Vector2, Vector2> shifting = c => frame >= 30 && frame < 90 ? recovered : Vector2.ClampMagnitude(c, 1f);
            float worst = 0f;
            var last = motion.Head;
            for (frame = 0; frame < 90; frame++)
            {
                motion.Tick(true, new Vector2(frame % 16 < 8 ? 6f : -6f, 1f), Dt, shifting);
                worst = Mathf.Max(worst, (motion.Head - last).magnitude);
                last = motion.Head;
            }
            Assert.LessOrEqual(worst, BrushScrub.MaxHeadSpeed * Dt + 1e-4f, $"đầu chải vụt {worst * 100f:F1} cm trong một khung");
            Assert.Less((motion.Head - recovered).magnitude, 1e-3f, "vài khung sau đầu chải tới chỗ đạt");
        }
    }
}
