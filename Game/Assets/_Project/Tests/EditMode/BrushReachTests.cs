using HaiBanTay.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Hình học bàn chải (brush17) với số đo thật của Ba (Brush17Probe: vai phải (0,204; 1,352; −0,115), vai trái (−0,19; 1,343; −0,119)
    /// theo hệ người, tay 0,576 m; tầm cổ tay 0,94 × tay). Hệ người: x phải, y từ chân, z trước. Mọi điểm cọ phải có tư thế mà mũi lông
    /// đúng điểm, cổ tay trong tầm vai, cán/nắm tay/cẳng tay không cắt thành hay đáy vật.
    /// </summary>
    public class BrushReachTests
    {
        static readonly Vector3 RightShoulder = new Vector3(0.204f, 1.352f, -0.115f);
        static readonly Vector3 LeftShoulder = new Vector3(-0.19f, 1.343f, -0.119f);
        const float Reach = 0.576f * 0.94f;

        /// <summary>Nồi bữa trong chậu (LoosePot Ø 0,36 × 0,3): đứng ở WashStation.StandBack, tâm nồi trước người 0,46 m, đáy nồi ở 0,77 m.</summary>
        static ScrubVessel BasinPot(out ScrubSurface surface, out Vector3 centre)
        {
            centre = new Vector3(0f, 0.77f, 0.46f);
            surface = new ScrubSurface(0.36f * 0.45f - 0.012f, 0.025f, 0f, BrushShape.Short.HeadRadius * 0.6f);
            return new ScrubVessel { Base = centre + Vector3.up * 0.025f, InnerRadius = 0.168f, OuterRadius = 0.18f, Height = 0.3f - 0.025f,
                InnerBottomRadius = 0.36f * 0.45f - 0.012f };
        }

        /// <summary>Nồi cao trên sàn, Ba đứng ghế đẩu (vai +0,35 m), tâm nồi trước người 0,72 m (bán kính nồi 0,4 + thân người 0,28 + 4 cm).</summary>
        static ScrubVessel TallPot(out ScrubSurface surface, out Vector3 centre)
        {
            // Ghế cách tâm nồi 0,62 m; cúi nhìn vào lòng nồi thì thân (và vai) lùi thêm ~0,135 m so với vai đứng thẳng ở trên
            // (PlayerBody.KeepBodyBehindEye, đo trong game: vai cách tâm nồi 0,97 m khi ghế ở 0,72 m).
            centre = new Vector3(0f, 0f, 0.62f + 0.135f);
            surface = new ScrubSurface(0.36f, 0.03f, 0.35f, 0.03f);
            return new ScrubVessel { Base = centre + Vector3.up * 0.03f, InnerRadius = 0.38f, OuterRadius = 0.4f, Height = 0.97f };
        }

        /// <summary>Chảo đồng dưới sàn, đứng thẳng, tâm chảo trước người 0,95 m (PanStation.ScrubStand).</summary>
        static ScrubVessel Pan(out ScrubSurface surface, out Vector3 centre)
        {
            centre = new Vector3(0f, 0f, 0.95f);
            surface = new ScrubSurface(HaiBanTay.Shift.PanStation.BottomRadius, HaiBanTay.Shift.PanStation.BottomY, 0f, HaiBanTay.Shift.PanStation.PanScrubMargin);
            return new ScrubVessel
            {
                Base = centre + Vector3.up * HaiBanTay.Shift.PanStation.BottomY, InnerRadius = HaiBanTay.Shift.PanStation.RimInner,
                OuterRadius = HaiBanTay.Shift.PanStation.RimOuter, Height = HaiBanTay.Shift.PanStation.RimY - HaiBanTay.Shift.PanStation.BottomY,
                InnerBottomRadius = HaiBanTay.Shift.PanStation.BottomRadius
            };
        }

        static BrushPose Solve(BrushShape shape, ScrubSurface surface, Vector3 centre, ScrubVessel vessel, Vector2 c, float lift = 0f)
        {
            surface.Evaluate(c, out var p, out var n);
            var up = Vector3.up * lift;
            return BrushReach.Solve(shape, centre + p, n, RightShoulder + up, LeftShoulder + up, Reach, (RightShoulder + LeftShoulder) * 0.5f + up, vessel);
        }

        /// <summary>Phần mặt cọ (theo diện tích mặt nạ) có tư thế đạt.</summary>
        static float ReachableShare(BrushShape shape, ScrubSurface surface, Vector3 centre, ScrubVessel vessel, float lift, out string worst)
        {
            int total = 0, ok = 0;
            worst = "";
            float worstCost = -1f;
            const int n = 20;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var c = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f) * surface.Domain;
                    if (c.magnitude > surface.Reachable) continue;
                    total++;
                    var pose = Solve(shape, surface, centre, vessel, c, lift);
                    if (pose.Ok) ok++;
                    float cost = pose.ReachExcess + pose.Clash;
                    if (cost > worstCost) { worstCost = cost; worst = $"c={c:F2} tầm {pose.ReachExcess * 100f:F1} cm, lún {pose.Clash * 100f:F1} cm"; }
                }
            return ok / (float)total;
        }

        [Test]
        public void TipSitsExactlyOnTheScrubPoint()
        {
            var vessel = BasinPot(out var surface, out var centre);
            var c = new Vector2(0.05f, -0.06f);
            surface.Evaluate(c, out var p, out _);
            var pose = Solve(BrushShape.Short, surface, centre, vessel, c);
            // Trục gần pháp tuyến: mũi lông chỉ lùi khỏi mặt bán kính bó lông × sin(góc nghiêng).
            float offset = BrushShape.Short.HeadRadius * Mathf.Sin(pose.Tilt * Mathf.Deg2Rad);
            Assert.AreEqual(offset, Vector3.Distance(pose.Tip, centre + p), 1e-4f, "mũi lông đặt đúng điểm cọ");
            Assert.AreEqual(BrushShape.Short.GripAt, Vector3.Distance(pose.LowGrip, pose.Tip), 1e-4f, "chỗ nắm trên trục cán");
        }

        [Test]
        public void HandPointsFromTheShoulderNotAlongTheDrag()
        {
            // Hướng bàn tay chỉ phụ thuộc chỗ chải, không theo hướng rê: rê từ thành xa về phía người theo từng cm, trục cán (thứ người chơi
            // thấy; tay đã ẩn) xoay liên tục, ngón luôn chĩa ra khỏi vai, cổ tay không gập quá BrushPose.MaxWristBend.
            var vessel = BasinPot(out var surface, out var centre);
            Vector3? last = null;
            for (float y = 0.1f; y >= -surface.Reachable; y -= 0.01f)
            {
                var pose = Solve(BrushShape.Short, surface, centre, vessel, new Vector2(0.02f, y));
                // Điểm ngoài tầm không bao giờ được chải: BrushScrub.ClampReach kéo con trỏ về vùng đạt.
                if (!pose.Ok) { last = null; continue; }
                var fingers = pose.LowHand * Vector3.forward;
                Assert.Greater(Vector3.Dot(fingers, (pose.LowGrip - RightShoulder).normalized), 0f, $"y={y:F2}: ngón chĩa ra khỏi vai");
                Assert.LessOrEqual(pose.WristBend, BrushPose.MaxWristBend + 0.01f, $"y={y:F2}: cổ tay gập {pose.WristBend:F0}°");
                if (last.HasValue)
                    Assert.Less(Vector3.Angle(last.Value, pose.Axis), 15f, $"y={y:F2}: cán lật giữa hai điểm cách 1 cm");
                last = pose.Axis;
            }
        }

        [Test]
        public void ShortBrushReachesTheCentreAndARadiusOfTheBasinPotStandingUpright()
        {
            // Tâm đáy và cả bán kính phía người đều đạt: tay trái xoay nồi (BrushScrub.SpinDegreesPerSecond) đưa mọi điểm qua bán kính này.
            var vessel = BasinPot(out var surface, out var centre);
            for (float r = 0f; r <= surface.Reachable + 1e-4f; r += 0.01f)
            {
                var pose = Solve(BrushShape.Short, surface, centre, vessel, new Vector2(0f, -r));
                Assert.IsTrue(pose.Ok, $"r={r:F2} về phía người: tầm thừa {pose.ReachExcess * 100f:F1} cm, lún {pose.Clash * 100f:F1} cm");
            }
            float share = ReachableShare(BrushShape.Short, surface, centre, vessel, 0f, out string worst);
            Debug.Log($"BRUSH_REACH chậu: đạt {share:P0} mặt đáy khi nồi đứng yên; tệ nhất {worst}");
        }

        [Test]
        public void TheOldBlockBrushCouldNotReachTheBasinPotCentre()
        {
            // Bằng chứng cho đổi thiết kế (bàn chải khối cầm lòng bàn tay, lông cách cổ tay ~8 cm): đứng thẳng, tâm đáy nồi bữa cách cổ tay
            // gần nhất quá tầm vai, nên bàn chải ngắn phải có cán (nắm tay ở trên vành nồi).
            var vessel = BasinPot(out var surface, out var centre);
            var block = new BrushShape { Length = 0.12f, HeadRadius = 0.035f, GripAt = 0.09f, HandleRadius = 0.04f };
            var pose = Solve(block, surface, centre, vessel, Vector2.zero);
            Assert.IsFalse(pose.Ok, "bàn chải khối không với tới tâm đáy khi đứng thẳng");
        }

        [Test]
        public void LongBrushFromTheStoolCoversTheTallPotBowl()
        {
            var vessel = TallPot(out var surface, out var centre);
            float share = ReachableShare(BrushShape.Long, surface, centre, vessel, 0.35f, out string worst);
            Debug.Log($"BRUSH_REACH nồi cao: đạt {share:P0} lòng bát; tệ nhất {worst}");
            Assert.GreaterOrEqual(share, BrushScrub.CleanEnough, $"phải với tới ≥ 90% đáy và chân thành để cọ xong ({worst})");
        }

        [Test]
        public void LongBrushStandingCoversTheCopperPan()
        {
            var vessel = Pan(out var surface, out var centre);
            float share = ReachableShare(BrushShape.Long, surface, centre, vessel, 0f, out string worst);
            Debug.Log($"BRUSH_REACH chảo: đạt {share:P0} lòng chảo; tệ nhất {worst}");
            Assert.GreaterOrEqual(share, BrushScrub.CleanEnough, $"đứng thẳng với tới ≥ 90% lòng chảo ({worst})");
        }

        [Test]
        public void HandleAndForearmNeverCutTheRimWhereThePoseIsOk()
        {
            var vessel = TallPot(out var surface, out var centre);
            for (int i = 0; i < 40; i++)
            {
                var c = Random.insideUnitCircle * surface.Reachable;
                var pose = Solve(BrushShape.Long, surface, centre, vessel, c, 0.35f);
                if (!pose.Ok) continue;
                for (int k = 0; k <= 20; k++)
                {
                    var p = Vector3.Lerp(pose.Tip + pose.Axis * 0.09f, pose.HighGrip, k / 20f);
                    Assert.LessOrEqual(vessel.Penetration(p, BrushShape.Long.HandleRadius), 0.005f, $"c={c}: cán cắt thành nồi");
                }
            }
        }

        /// <summary>Đo vòng đầu chải dày (16 điểm quanh trục) ở mũi lông và ở khối gỗ: lọt ra ngoài thành trong bao nhiêu (m).</summary>
        static float HeadOut(BrushShape shape, ScrubVessel vessel, BrushPose pose)
        {
            float worst = 0f;
            var side = Vector3.Cross(pose.Axis, Vector3.up);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(pose.Axis, Vector3.right);
            side.Normalize();
            var other = Vector3.Cross(pose.Axis, side);
            foreach (var (along, radius) in new[] { (0.01f, shape.BristleRadius), (shape.HeadLength * 0.5f, shape.StockRadius), (shape.HeadLength, shape.StockRadius) })
                for (int k = 0; k < 16; k++)
                {
                    float a = k * Mathf.PI / 8f;
                    var p = pose.Tip + pose.Axis * along + (side * Mathf.Cos(a) + other * Mathf.Sin(a)) * radius;
                    worst = Mathf.Max(worst, vessel.WallPenetration(p));
                }
            return worst;
        }

        /// <summary>QA-0136: mọi tư thế đạt thì đầu chải (lông, khối gỗ) không thò qua thành quá độ ép của lông.</summary>
        [Test]
        public void BrushHeadNeverPokesThroughTheWall()
        {
            foreach (var (name, shape, lift, make) in new (string, BrushShape, float, System.Func<(ScrubVessel, ScrubSurface, Vector3)>)[]
                     {
                         ("chậu", BrushShape.Short, 0f, () => { var v = BasinPot(out var s, out var c); return (v, s, c); }),
                         ("nồi cao", BrushShape.Long, 0.35f, () => { var v = TallPot(out var s, out var c); return (v, s, c); }),
                         ("chảo", BrushShape.Long, 0f, () => { var v = Pan(out var s, out var c); return (v, s, c); }),
                     })
            {
                var (vessel, surface, centre) = make();
                const int n = 24;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        var c = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f) * surface.Domain;
                        if (c.magnitude > surface.Reachable) continue;
                        var pose = Solve(shape, surface, centre, vessel, c, lift);
                        if (!pose.Ok) continue;
                        float out_ = HeadOut(shape, vessel, pose);
                        Assert.LessOrEqual(out_, BrushReach.HeadSquash + 0.004f, $"{name} c={c:F2}: đầu chải thò qua thành {out_ * 100f:F1} cm");
                    }
            }
        }

        [Test]
        public void ShortBrushCoversTheWholeBasinPotBottomWithItsFootprint()
        {
            // Lề rộng bằng khối gỗ mà vết chải vẫn phủ tới chân thành: tầm con trỏ + bán kính vết ≥ bán kính đáy.
            BasinPot(out var surface, out _);
            Assert.GreaterOrEqual(surface.Reachable + BrushShape.Short.HeadRadius * 1.15f, surface.BottomRadius - 1e-4f);
            Pan(out var pan, out _);
            Assert.GreaterOrEqual(pan.Reachable + BrushShape.Long.HeadRadius * 1.15f, pan.BottomRadius - 1e-4f);
        }

        /// <summary>Pháp tuyến mặt cọ quay đều qua góc đáy–thành (không lật 90° ở chân thành làm bàn chải xoay ngang trong một khung).</summary>
        [Test]
        public void SurfaceNormalTurnsSmoothlyRoundTheWallFoot()
        {
            var surface = new ScrubSurface(0.36f, 0.03f, 0.35f, 0.03f);
            surface.Evaluate(new Vector2(0.25f, 0f), out var lastPoint, out var last);
            float worst = 0f, worstStep = 0f;
            for (float r = 0.251f; r <= 0.5f; r += 0.001f)
            {
                surface.Evaluate(new Vector2(r, 0f), out var point, out var normal);
                worst = Mathf.Max(worst, Vector3.Angle(last, normal));
                worstStep = Mathf.Max(worstStep, Vector3.Distance(point, lastPoint));
                last = normal;
                lastPoint = point;
            }
            Assert.LessOrEqual(worst, 3f, $"pháp tuyến lật {worst:F1}° trên 1 mm con trỏ");
            Assert.LessOrEqual(worstStep, 0.0015f, "điểm cọ liền mạch qua chân thành");
            surface.Evaluate(new Vector2(0.2f, 0f), out _, out var bottom);
            surface.Evaluate(new Vector2(0.5f, 0f), out _, out var wall);
            Assert.AreEqual(0f, Vector3.Angle(bottom, Vector3.up), 0.01f, "giữa đáy pháp tuyến hướng lên");
            Assert.AreEqual(0f, Vector3.Angle(wall, Vector3.left), 0.01f, "trên thành pháp tuyến hướng vào tâm");
        }

        /// <summary>
        /// Nồi cao chao 4° (sóng ngày 4): điểm trên thành trong của nồi đã nghiêng vẫn nằm trong thành, không bị coi là lọt ra ngoài khi đo
        /// theo trụ đứng (vành lệch tới 7 cm, vùng với tới chập chờn theo nhịp sóng, bàn chải nhảy chỗ — Brush17 có sóng).
        /// </summary>
        [Test]
        public void ATiltedVesselMeasuresItsWallAlongItsOwnAxis()
        {
            var tilt = Quaternion.Euler(4f, 0f, 0f);
            var up = tilt * Vector3.up;
            var vessel = new ScrubVessel { Base = Vector3.zero, InnerRadius = 0.38f, OuterRadius = 0.4f, Height = 0.97f, Up = up };
            var upright = vessel;
            upright.Up = Vector3.zero;
            // Điểm sát thành trong (cách trục 0,37 m) ở 0,6 m dọc trục nồi, phía nồi nghiêng ra.
            var onWall = tilt * new Vector3(0f, 0.6f, 0.37f);
            Assert.AreEqual(0f, vessel.WallPenetration(onWall), 1e-5f, "điểm trong lòng nồi nghiêng không lọt qua thành");
            Assert.Greater(upright.WallPenetration(onWall), 0.01f, "đo theo trụ đứng thì sai (điều kiện của test)");
            Assert.AreEqual(0f, vessel.Penetration(onWall, 0.005f), 1e-5f, "khối cầu nhỏ trong lòng nồi nghiêng không chạm thành");
            // Để trống trục: như trụ đứng cũ.
            Assert.AreEqual(upright.WallPenetration(new Vector3(0.39f, 0.5f, 0f)), 0.01f, 1e-5f);
        }
    }
}
