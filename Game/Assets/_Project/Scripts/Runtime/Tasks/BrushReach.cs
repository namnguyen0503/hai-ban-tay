using UnityEngine;

namespace HaiBanTay.Tasks
{
    /// <summary>
    /// Hình học một bàn chải cầm tay, dọc trục từ mũi lông (0) tới đuôi cán (<see cref="Length"/>). Lông mọc ở đầu (chổi cọ đầu bó),
    /// nên mũi lông chạm đáy khi trục gần pháp tuyến và cạnh bó lông chạm thành khi trục gần song song với thành.
    /// </summary>
    public struct BrushShape
    {
        /// <summary>Tổng chiều dài từ mũi lông tới đuôi cán (m).</summary>
        public float Length;
        /// <summary>Bán kính bó lông (m): đầu chải chạm thành bằng cạnh bó thì trục lùi khỏi thành chừng này.</summary>
        public float HeadRadius;
        /// <summary>Bàn chải ngắn: tâm nắm (lòng nắm tay phải) cách mũi lông chừng này (m).</summary>
        public float GripAt;
        /// <summary>Bàn chải dài: hai tay cách nhau chừng này dọc cán (m); 0 = một tay.</summary>
        public float GripSpacing;
        /// <summary>Bàn chải dài: tay dưới trượt dọc cán trong khoảng này (m tính từ mũi lông).</summary>
        public float LowMin, LowMax;
        /// <summary>Bán kính cán (m).</summary>
        public float HandleRadius;
        /// <summary>Bán kính ngoài của vòng chùm lông ở mũi (m) và của đai/khối gỗ đầu chải (m), đai cách mũi lông <see cref="HeadLength"/>.</summary>
        public float BristleRadius, StockRadius, HeadLength;
        public bool TwoHanded => GripSpacing > 0f;

        /// <summary>
        /// Bàn chải ngắn ở chậu: cán gỗ ngắn, đầu bó lông, cầm một tay phải như cầm dao găm (ngón cái hướng lên đuôi cán). Mũi lông cách
        /// lòng nắm 0,33 m: đứng thẳng ở mép chậu, nắm tay vẫn ở trên vành nồi khi mũi lông chạm đáy (BrushReachTests).
        /// </summary>
        public static BrushShape Short => new BrushShape
        {
            Length = 0.42f, HeadRadius = 0.035f, GripAt = 0.33f, HandleRadius = 0.015f,
            // ChoreProps.Brush: chùm lông ở 0,78 r + 7 mm, khối gỗ Ø 2,25 r ở 0,074–0,106 m.
            BristleRadius = 0.034f, StockRadius = 0.0394f, HeadLength = 0.1f
        };

        /// <summary>
        /// Bàn chải cán dài 1,9 m (chảo dưới sàn, nồi cao từ trên ghế): hai tay trên cán, tay phải ở dưới. Cán đủ dài để đứng trên ghế đẩu vẫn
        /// chạm đáy nồi cao 1 m mà hai tay ở tầm ngực (BrushReachTests).
        /// </summary>
        public static BrushShape Long => new BrushShape
        {
            Length = 1.9f, HeadRadius = 0.07f, GripSpacing = 0.3f, LowMin = 0.75f, LowMax = 1.55f, HandleRadius = 0.017f,
            // ChoreProps.LongBrush: nan tre ở 0,8 r + 8 mm, đai Ø 1,5 r ở 0,175 m.
            BristleRadius = 0.064f, StockRadius = 0.0525f, HeadLength = 0.175f
        };
    }

    /// <summary>Vật rỗng hình trụ đứng (nồi, chảo) mà cán và cẳng tay không được cắt qua thành (hệ thế giới).</summary>
    public struct ScrubVessel
    {
        /// <summary>Tâm mặt đáy trong (thế giới).</summary>
        public Vector3 Base;
        public float InnerRadius, OuterRadius;
        /// <summary>Độ cao vành tính từ <see cref="Base"/> (m).</summary>
        public float Height;
        /// <summary>
        /// Bán kính thành trong ở đáy (m); 0 = thành đứng (bằng <see cref="InnerRadius"/>). Lòng nồi chậu thu dần xuống đáy (ChoreProps.Pot:
        /// 0,45 d − 1,2 cm ở đáy, 0,5 d − 1,2 cm ở vành): lấy bán kính vành cho cả chiều cao thì đầu chải sát đáy lọt qua thành (QA-0136).
        /// </summary>
        public float InnerBottomRadius;
        /// <summary>
        /// Trục đứng của vật (đơn vị, thế giới); để trống (0) = thẳng đứng. Nồi cao chao 4° khi có sóng: vành lệch tới 7 cm so với trụ đứng,
        /// đo theo trụ đứng thì đầu chải trên thành nghiêng lúc lọt qua thành, lúc không, vùng với tới chập chờn theo nhịp sóng (Brush17).
        /// </summary>
        public Vector3 Up;

        /// <summary>Độ cao dọc trục vật (tính từ <see cref="Base"/>) và khoảng cách tới trục của một điểm.</summary>
        public void Split(Vector3 p, out float y, out float rho)
        {
            var d = p - Base;
            var up = Up.sqrMagnitude > 0.5f ? Up : Vector3.up;
            y = Vector3.Dot(d, up);
            rho = (d - up * y).magnitude;
        }

        /// <summary>Bán kính thành trong ở độ cao <paramref name="y"/> tính từ đáy trong.</summary>
        public float InnerAt(float y) =>
            InnerBottomRadius > 0f && Height > 1e-4f ? Mathf.Lerp(InnerBottomRadius, InnerRadius, Mathf.Clamp01(y / Height)) : InnerRadius;

        /// <summary>Một điểm (của đầu chải) lọt ra ngoài mặt thành trong bao nhiêu (m), dưới vành; không tính đáy (lông chạm đáy là đúng).</summary>
        public float WallPenetration(Vector3 p)
        {
            Split(p, out float y, out float rho);
            if (y >= Height || y < -0.03f)
                return 0f;
            return Mathf.Max(0f, rho - InnerAt(y));
        }

        /// <summary>
        /// Độ lún của một khối cầu bán kính <paramref name="radius"/> tâm <paramref name="p"/> vào thành hoặc đáy (m, 0 = không chạm). Bên trong
        /// lòng vật (dưới vành) và bên trên vành đều trống; ngoài thành phía dưới vành coi như trống (vật khác do bộ dò xuyên vật lo).
        /// </summary>
        public float Penetration(Vector3 p, float radius)
        {
            Split(p, out float y, out float rho);
            if (y - radius >= Height)
                return 0f;
            float depth = 0f;
            float inner = InnerAt(Mathf.Max(0f, y));
            // Thành: vỏ trụ giữa InnerRadius và OuterRadius, từ đáy tới vành.
            if (rho + radius > inner && rho - radius < OuterRadius && y + radius > -0.03f)
            {
                float fromInside = rho + radius - inner;
                float fromOutside = OuterRadius - (rho - radius);
                float fromTop = Height - (y - radius);
                depth = Mathf.Max(depth, Mathf.Min(fromInside, Mathf.Min(fromOutside, fromTop)));
            }
            // Đáy: trong lòng vật mà xuống dưới mặt đáy.
            if (rho < inner && y - radius < 0f && y > -0.2f)
                depth = Mathf.Max(depth, radius - y);
            return Mathf.Max(0f, depth);
        }
    }

    /// <summary>Tư thế bàn chải đã giải: mũi lông, trục, chỗ nắm và cổ tay từng bên (thế giới).</summary>
    public struct BrushPose
    {
        public Vector3 Tip;
        /// <summary>Trục từ mũi lông về phía đuôi cán (đơn vị).</summary>
        public Vector3 Axis;
        /// <summary>Hướng ngón tay của tay dưới (vuông góc trục): con lăn của bàn chải quanh trục.</summary>
        public Vector3 Fingers;
        public Vector3 LowGrip, HighGrip;
        /// <summary>Khoảng cách tay dưới tới mũi lông dọc cán (m).</summary>
        public float LowAt;
        public Vector3 LowWrist, HighWrist;
        public Quaternion LowHand, HighHand;
        /// <summary>Cổ tay xa vai quá tầm (m, cộng hai tay).</summary>
        public float ReachExcess;
        /// <summary>Cán, nắm tay, cẳng tay lún vào thành/đáy vật (m, lớn nhất).</summary>
        public float Clash;
        /// <summary>Góc trục lệch khỏi pháp tuyến (độ).</summary>
        public float Tilt;
        /// <summary>Cổ tay gập (độ): cẳng tay (vai → chỗ nắm) lệch khỏi mặt phẳng vuông góc với cán. Nắm cán thì cẳng tay gần vuông góc
        /// với cán; cẳng tay chạy dọc cán là tư thế không thể (bàn tay sẽ quay ngược).</summary>
        public float WristBend;
        public const float MaxWristBend = 55f;
        public bool Ok => ReachExcess <= 0.002f && Clash <= 0.002f && WristBend <= MaxWristBend;
    }

    /// <summary>
    /// Giải tư thế bàn chải cho một điểm cần cọ (BrushScrub gọi mỗi khung, test EditMode gọi trực tiếp): mũi lông đặt đúng điểm, trục
    /// nghiêng từ pháp tuyến về phía người chừng nào đủ để cổ tay trong tầm vai và cán, nắm tay, cẳng tay không cắt qua vành; điểm sát
    /// thành phía người đứng thì trục dốc hơn (chải bằng mũi). Hướng ngón tay luôn chĩa từ vai ra chỗ nắm, không theo hướng rê chuột:
    /// rê về phía người thì bàn tay không quay 180° (cẳng tay vặn ngược).
    /// </summary>
    public static class BrushReach
    {
        /// <summary>Tâm nắm của mesh Grip trong khung cổ tay (HandsRig.GripForward/GripBelow).</summary>
        public static readonly Vector3 FistInWrist = new Vector3(0f, -0.036f, 0.092f);
        public const float ForearmRadius = 0.045f, FistRadius = 0.045f, ForearmLength = 0.26f;
        /// <summary>Lăn thêm quanh trục cán của từng tay (độ), chọn bằng ClipFit (cẳng tay không vặn ngược, ngón ôm cán).</summary>
        public static float RollLow = 0f, RollHigh = 0f;

        /// <param name="tip">Điểm cần cọ (thế giới).</param>
        /// <param name="normal">Pháp tuyến mặt cọ, hướng ra lòng vật.</param>
        /// <param name="lowShoulder">Vai của tay cầm dưới (tay phải).</param>
        /// <param name="highShoulder">Vai tay trên (tay trái), bàn chải dài.</param>
        /// <param name="reach">Cổ tay cách vai tối đa (m): 0,94 × dài tay.</param>
        /// <param name="body">Điểm giữa hai vai: trục nghiêng về phía này.</param>
        /// <param name="vessel">Vật đang cọ (thành, đáy không được cắt qua).</param>
        /// <param name="prefTilt">Góc nghiêng ưa thích (độ) khi nhiều góc cùng đạt.</param>
        public static BrushPose Solve(BrushShape shape, Vector3 tip, Vector3 normal, Vector3 lowShoulder, Vector3 highShoulder, float reach,
            Vector3 body, ScrubVessel vessel, float prefTilt = 20f, float maxTilt = 104f)
        {
            normal = normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector3.up;
            var toward = Vector3.ProjectOnPlane(body - tip, normal);
            if (toward.sqrMagnitude < 1e-6f) toward = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (toward.sqrMagnitude < 1e-6f) toward = Vector3.ProjectOnPlane(Vector3.forward, normal);
            toward.Normalize();
            var best = default(BrushPose);
            float bestCost = float.MaxValue;
            // Lệch ngang chút ít quanh hướng về người giúp điểm ở mép trái/phải (nắm tay tránh vành bên cạnh).
            for (int side = -2; side <= 2; side++)
            {
                var around = Quaternion.AngleAxis(side * 12f, normal) * toward;
                for (float tilt = 0f; tilt <= maxTilt + 0.01f; tilt += 5f)
                {
                    var axis = (normal * Mathf.Cos(tilt * Mathf.Deg2Rad) + around * Mathf.Sin(tilt * Mathf.Deg2Rad)).normalized;
                    if (shape.TwoHanded)
                    {
                        for (float low = shape.LowMin; low <= shape.LowMax + 0.001f; low += 0.1f)
                            Consider(Evaluate(shape, tip, normal, axis, low, lowShoulder, highShoulder, reach, vessel), prefTilt, side, ref best, ref bestCost);
                    }
                    else
                        Consider(Evaluate(shape, tip, normal, axis, shape.GripAt, lowShoulder, highShoulder, reach, vessel), prefTilt, side, ref best, ref bestCost);
                }
            }
            return best;
        }

        static void Consider(BrushPose pose, float prefTilt, int side, ref BrushPose best, ref float bestCost)
        {
            float cost = pose.ReachExcess * 40f + pose.Clash * 60f + Mathf.Max(0f, pose.WristBend - BrushPose.MaxWristBend * 0.7f) * 0.02f
                         + Mathf.Abs(pose.Tilt - prefTilt) * 0.0015f + Mathf.Abs(side) * 0.004f;
            if (cost < bestCost)
            {
                bestCost = cost;
                best = pose;
            }
        }

        /// <summary>Tư thế với trục và chỗ nắm cho trước: tầm với và độ lún (không chọn góc).</summary>
        public static BrushPose Evaluate(BrushShape shape, Vector3 tip, Vector3 normal, Vector3 axis, float lowAt, Vector3 lowShoulder,
            Vector3 highShoulder, float reach, ScrubVessel vessel)
        {
            float tilt = Vector3.Angle(axis, normal);
            // Cạnh bó lông chạm thành: trục lùi khỏi mặt một bán kính bó lông khi trục gần song song với mặt.
            var at = tip + normal * (shape.HeadRadius * Mathf.Sin(Mathf.Min(90f, tilt) * Mathf.Deg2Rad));
            var pose = new BrushPose { Tip = at, Axis = axis, LowAt = lowAt, Tilt = tilt };
            pose.LowGrip = at + axis * lowAt;
            pose.LowHand = Hand(axis, pose.LowGrip, lowShoulder, false, RollLow);
            pose.Fingers = pose.LowHand * Vector3.forward;
            pose.LowWrist = pose.LowGrip - pose.LowHand * FistInWrist;
            pose.ReachExcess = Mathf.Max(0f, Vector3.Distance(pose.LowWrist, lowShoulder) - reach);
            pose.WristBend = Bend(axis, pose.LowGrip - lowShoulder);
            float clash = 0f;
            // Cán từ mũi lông (trừ bó lông) tới đuôi; nắm tay; cẳng tay từ cổ tay về phía vai (khuỷu chùng xuống).
            float end = shape.TwoHanded ? Mathf.Min(shape.Length, lowAt + shape.GripSpacing + 0.12f) : shape.Length;
            clash = Mathf.Max(clash, Segment(vessel, at + axis * 0.09f, at + axis * end, shape.HandleRadius));
            clash = Mathf.Max(clash, vessel.Penetration(pose.LowGrip, FistRadius));
            clash = Mathf.Max(clash, Forearm(vessel, pose.LowWrist, lowShoulder));
            if (shape.TwoHanded)
            {
                pose.HighGrip = at + axis * (lowAt + shape.GripSpacing);
                pose.HighHand = Hand(axis, pose.HighGrip, highShoulder, true, RollHigh);
                pose.HighWrist = pose.HighGrip - pose.HighHand * Mirror(FistInWrist);
                pose.ReachExcess += Mathf.Max(0f, Vector3.Distance(pose.HighWrist, highShoulder) - reach);
                pose.WristBend = Mathf.Max(pose.WristBend, Bend(axis, pose.HighGrip - highShoulder));
                clash = Mathf.Max(clash, vessel.Penetration(pose.HighGrip, FistRadius));
                clash = Mathf.Max(clash, Forearm(vessel, pose.HighWrist, highShoulder));
            }
            clash = Mathf.Max(clash, Head(shape, vessel, at, axis));
            pose.Clash = clash;
            return pose;
        }

        /// <summary>
        /// Hướng bàn tay nắm cán (khung tay: +Z ngón, −Y lòng bàn tay, trục cong của nắm tay là X). Tay phải: trục X chĩa về mũi lông
        /// (ngón cái về đuôi cán); tay trái: ngón cái (+X) về đuôi cán. Ngón chĩa từ vai ra chỗ nắm, vuông góc với cán.
        /// </summary>
        public static Quaternion Hand(Vector3 axis, Vector3 grip, Vector3 shoulder, bool left, float roll)
        {
            var x = left ? axis : -axis;
            var fingers = Vector3.ProjectOnPlane(grip - shoulder, axis);
            if (fingers.sqrMagnitude < 1e-6f) fingers = Vector3.ProjectOnPlane(Vector3.forward, axis);
            fingers.Normalize();
            var y = Vector3.Cross(fingers, x);
            var rot = Quaternion.LookRotation(fingers, y);
            return roll != 0f ? Quaternion.AngleAxis(roll, x) * rot : rot;
        }

        static float Bend(Vector3 axis, Vector3 forearm) =>
            forearm.sqrMagnitude < 1e-8f ? 0f : Mathf.Abs(90f - Vector3.Angle(axis, forearm));

        static Vector3 Mirror(Vector3 v) => new Vector3(-v.x, v.y, v.z);

        static float Forearm(ScrubVessel vessel, Vector3 wrist, Vector3 shoulder)
        {
            var toShoulder = shoulder - wrist;
            var elbow = wrist + toShoulder.normalized * Mathf.Min(ForearmLength, toShoulder.magnitude * 0.55f) + Vector3.down * 0.06f;
            return Segment(vessel, wrist, elbow, ForearmRadius);
        }

        /// <summary>Lông ép mép được chừng này (m) khi áp vào thành cong trước khi tính là thò qua thành.</summary>
        public const float HeadSquash = 0.006f;

        /// <summary>
        /// Đầu chải thò qua thành (QA-0136): điểm xa trục vật nhất trên vòng chùm lông ở mũi và trên vòng đai gỗ (cán nghiêng thì đai văng
        /// ra ngoài trước cả cán). Lông ép được <see cref="HeadSquash"/>.
        /// </summary>
        public static float Head(BrushShape shape, ScrubVessel vessel, Vector3 at, Vector3 axis)
        {
            if (shape.HeadLength <= 0f)
                return 0f;
            // Vòng chùm lông sát mũi; khối gỗ lấy hai vòng (giữa và đai trên) vì cán nghiêng thì giữa khối cũng chạm thành cong.
            float worst = Mathf.Max(Ring(vessel, at + axis * 0.01f, axis, shape.BristleRadius),
                Mathf.Max(Ring(vessel, at + axis * (shape.HeadLength * 0.5f), axis, shape.StockRadius),
                    Ring(vessel, at + axis * shape.HeadLength, axis, shape.StockRadius)));
            return Mathf.Max(0f, worst - HeadSquash);
        }

        const int RingSamples = 12;

        /// <summary>
        /// Điểm lọt ra ngoài thành nhất trên vòng bán kính <paramref name="radius"/> quanh trục: lấy mẫu đều cả vòng (không chỉ hướng xuyên
        /// tâm vật) để độ lún liền mạch khi trục đi qua pháp tuyến; chỉ lấy hướng xuyên tâm thì hướng đó lật ngược lúc nghiêng qua 0° và
        /// lời giải nhảy (bàn chải cán dài giật 10 cm/khung ở thành nồi cao).
        /// </summary>
        static float Ring(ScrubVessel vessel, Vector3 centre, Vector3 axis, float radius)
        {
            var u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var v = Vector3.Cross(axis, u);
            float worst = 0f;
            for (int i = 0; i < RingSamples; i++)
            {
                float a = i * (2f * Mathf.PI / RingSamples);
                worst = Mathf.Max(worst, vessel.WallPenetration(centre + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius));
            }
            return worst;
        }

        static float Segment(ScrubVessel vessel, Vector3 a, Vector3 b, float radius)
        {
            float worst = 0f;
            // Mẫu dày ~2 cm dọc đoạn: cán dài 1,9 m lấy mẫu thưa sẽ lọt qua mép vành.
            int n = Mathf.Max(8, Mathf.CeilToInt(Vector3.Distance(a, b) / 0.02f));
            for (int i = 0; i <= n; i++)
                worst = Mathf.Max(worst, vessel.Penetration(Vector3.Lerp(a, b, i / (float)n), radius));
            return worst;
        }
    }
}
