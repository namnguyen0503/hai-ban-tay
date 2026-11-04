using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>
    /// Một bước đi thẳng của NPC trên mặt sàn (không NavMesh: lộ trình do builder đặt sẵn qua các lối trống).
    /// Người chơi đứng chắn phía trước thì NPC đứng chờ, không đẩy người chơi.
    /// </summary>
    public static class NpcStepLogic
    {
        /// <summary>Đi từ from về phía to, giữ nguyên độ cao from.y. Trả về true khi đã tới (trong arriveRadius).</summary>
        public static bool Step(ref Vector3 from, Vector3 to, float speed, float dt, float arriveRadius = 0.05f, bool slope = false)
        {
            var d = to - from;
            float dy = d.y;
            d.y = 0f;
            float dist = d.magnitude;
            float step = Mathf.Max(0f, speed) * dt;
            if (dist <= step)
            {
                from = new Vector3(to.x, slope ? to.y : from.y, to.z);
                return true;
            }
            // Đã vào vùng tới (góc đường, điểm vòng): coi như tới, đứng nguyên chỗ. Không kéo bật thẳng vào điểm: người và đồ đang bê
            // sẽ nhảy 5–12 cm trong một khung ở mỗi góc đường (đo giật 14/10).
            if (dist <= arriveRadius)
                return true;
            from += d / dist * step;
            // Dốc: độ cao đi cùng tỉ lệ quãng đường ngang đã đi (cầu thang tàu, bậc thềm).
            if (slope)
                from.y += dy * (step / dist);
            return false;
        }

        /// <summary>Người chơi có đứng chắn trên đường đi (trong hình nón phía trước, gần hơn clearance) không.</summary>
        public static bool Blocked(Vector3 npc, Vector3 heading, Vector3 player, float clearance = 0.8f, float halfAngle = 50f)
        {
            var d = player - npc;
            d.y = 0f;
            heading.y = 0f;
            if (d.sqrMagnitude > clearance * clearance || heading.sqrMagnitude < 1e-6f)
                return false;
            if (d.sqrMagnitude < 1e-6f)
                return true;
            return Vector3.Angle(heading, d) <= halfAngle;
        }

        /// <summary>Xoay dần về hướng cần nhìn (độ/giây).</summary>
        /// <summary>
        /// Hệ số tốc độ theo độ lệch giữa hướng mặt (yaw) và hướng cần đi: lệch dưới ~32° đi đủ tốc độ, càng lệch càng chậm, từ ~75°
        /// trở lên đứng xoay tại chỗ. Không có hướng thì 1.
        /// </summary>
        /// <summary>Tốc độ đổi dần về want theo gia tốc accel.</summary>
        public static float Ramp(float now, float want, float accel, float dt) => Mathf.MoveTowards(now, want, accel * dt);

        /// <summary>
        /// Một bước quay về hướng want (độ): tốc độ quay velocity (độ/s) đổi theo gia tốc accel, nhanh nhất maxSpeed, hãm kịp để dừng
        /// đúng hướng (không vọt qua).
        /// </summary>
        public static float TurnStep(float yaw, float want, ref float velocity, float maxSpeed, float accel, float dt)
        {
            float error = Mathf.DeltaAngle(yaw, want);
            if (Mathf.Abs(error) < 0.05f && Mathf.Abs(velocity) < accel * dt * 2f)
            {
                velocity = 0f;
                return want;
            }
            float target = Mathf.Sign(error) * Mathf.Min(maxSpeed, Mathf.Sqrt(2f * accel * Mathf.Abs(error)));
            // Hãm được mạnh gấp ba lúc tăng tốc: theo kịp đường hãm, tới hướng cần quay thì tốc độ quay đã gần 0 (không vọt qua rồi
            // chốt cứng).
            bool braking = Mathf.Abs(target) < Mathf.Abs(velocity) || target * velocity < 0f;
            velocity = Mathf.MoveTowards(velocity, target, (braking ? 3f : 1f) * accel * dt);
            float step = velocity * dt;
            if (Mathf.Abs(step) >= Mathf.Abs(error) && step * error >= 0f)
            {
                velocity = 0f;
                return want;
            }
            return yaw + step;
        }

        /// <summary>Tốc độ lớn nhất còn hãm kịp trong quãng distance (không dưới 0,2 m/s để khỏi bò mãi những cm cuối).</summary>
        public static float BrakeSpeed(float distance, float accel) => Mathf.Max(0.2f, Mathf.Sqrt(2f * accel * Mathf.Max(0f, distance)));

        public static float AlignedSpeed(float yaw, Vector3 heading)
        {
            heading.y = 0f;
            if (heading.sqrMagnitude < 1e-6f)
                return 1f;
            var face = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            return Mathf.Clamp01((Vector3.Dot(face, heading.normalized) - 0.25f) / 0.6f);
        }

        public static float TurnYaw(float yaw, Vector3 dir, float degPerSec, float dt)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f)
                return yaw;
            float want = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            return Mathf.MoveTowardsAngle(yaw, want, degPerSec * dt);
        }
        // ---------- Đồ NPC cầm/vác so với thân thật của Ba ----------

        /// <summary>Hộp định hướng (thế giới): tâm, ba trục đơn vị, nửa cạnh theo từng trục.</summary>
        public struct Box
        {
            public Vector3 center, x, y, z, half;

            public Box(Vector3 center, Vector3 x, Vector3 y, Vector3 z, Vector3 half)
            {
                this.center = center; this.x = x; this.y = y; this.z = z; this.half = half;
            }

            /// <summary>Hộp bao mesh (bounds cục bộ) của transform t, gồm cả scale.</summary>
            public static Box Of(Transform t, Bounds local)
            {
                var s = t.lossyScale;
                var half = new Vector3(Mathf.Abs(local.extents.x * s.x), Mathf.Abs(local.extents.y * s.y), Mathf.Abs(local.extents.z * s.z));
                return new Box(t.TransformPoint(local.center), t.right, t.up, t.forward, half);
            }

            /// <summary>
            /// Khoảng cách có dấu từ p tới hộp (m): dương ở ngoài, âm ở trong (độ lún tới mặt gần nhất). Có dấu để "không lún sâu thêm" còn ý
            /// nghĩa khi đã chạm: khe không dấu bằng 0 cho mọi điểm bên trong, thân đung đưa lọt vào 1 cm là mọi bước sau đều được (test 31/10).
            /// </summary>
            public float Signed(Vector3 p)
            {
                var d = p - center;
                float lx = Mathf.Abs(Vector3.Dot(d, x)) - half.x, ly = Mathf.Abs(Vector3.Dot(d, y)) - half.y, lz = Mathf.Abs(Vector3.Dot(d, z)) - half.z;
                float ox = Mathf.Max(lx, 0f), oy = Mathf.Max(ly, 0f), oz = Mathf.Max(lz, 0f);
                return Mathf.Sqrt(ox * ox + oy * oy + oz * oz) + Mathf.Min(Mathf.Max(lx, Mathf.Max(ly, lz)), 0f);
            }

            /// <summary>Điểm của hộp gần p nhất (p trong hộp thì là chính p).</summary>
            public Vector3 Closest(Vector3 p)
            {
                var d = p - center;
                return center + x * Mathf.Clamp(Vector3.Dot(d, x), -half.x, half.x)
                              + y * Mathf.Clamp(Vector3.Dot(d, y), -half.y, half.y)
                              + z * Mathf.Clamp(Vector3.Dot(d, z), -half.z, half.z);
            }
        }

        /// <summary>
        /// Khoảng hở nhỏ nhất (m, có dấu: âm = lún bấy nhiêu) giữa các điểm vỏ thân (shell, thế giới) và các hộp đồ. Mỗi điểm vỏ được dời
        /// shift rồi đổi qua toBoxes (tư thế dự kiến của NPC → tư thế hiện tại của hộp) trước khi đo. away: hướng ngang từ hộp ra điểm
        /// vỏ gần nhất (để người chơi trượt dọc đồ thay vì dừng hẳn).
        /// </summary>
        public static float ReachGap(System.Collections.Generic.IReadOnlyList<Vector3> shell, Vector3 shift, Matrix4x4 toBoxes,
            System.Collections.Generic.IReadOnlyList<Box> boxes, out Vector3 away)
        {
            away = Vector3.zero;
            float best = float.PositiveInfinity;
            if (shell == null || boxes == null || boxes.Count == 0)
                return best;
            for (int i = 0; i < shell.Count; i++)
            {
                var p = toBoxes.MultiplyPoint3x4(shell[i] + shift);
                for (int b = 0; b < boxes.Count; b++)
                {
                    float d = boxes[b].Signed(p);
                    if (d < best)
                    {
                        best = d;
                        var c = boxes[b].Closest(p);
                        var a = d > 1e-5f ? p - c : p - boxes[b].center;
                        a.y = 0f;
                        away = a.sqrMagnitude > 1e-8f ? a.normalized : Vector3.zero;
                    }
                }
            }
            return best;
        }

        /// <summary>Khoảng hở tối thiểu giữa đồ NPC cầm/vác và vỏ thân Ba (m). Bộ dò bot báo lún đồ từ 4 cm.</summary>
        public const float ReachMargin = 0.04f;

        /// <summary>
        /// Bước (người chơi hay NPC) được phép không: sau bước còn hở ít nhất ReachMargin, hoặc bước không làm khe hẹp thêm
        /// (đang sát sẵn thì vẫn được lùi ra, đứng yên hay đi dọc). Không có dung sai theo bước: khung hình nhanh (batchmode) mỗi bước
        /// dưới 2 mm thì dung sai 2 mm/bước cho đồ bò dần vào thân (test bục trống 31/10).
        /// </summary>
        public static bool ReachAllows(float before, float after) => after >= ReachMargin || after >= before - 1e-5f;

        /// <summary>Kẹt vật tĩnh: không bị từ chối bước liền chừng này giây, hay đã rời chỗ kẹt quá StuckRadius, thì đợt kẹt mới.</summary>
        public const float StuckForgetAfter = 1f;
        public const float StuckRadius = 0.3f;

        /// <summary>
        /// Ghi một lần GuardStep từ chối bước ở pos lúc now; trả số giây của đợt kẹt hiện tại. Bước trượt được xen giữa (lắc qua lại
        /// vài cm ở mép vật) không đếm lại: CI 01/11 phụ bếp lắc ở mép chồng nồi, đồng hồ kẹt cũ về 0 mỗi bước trượt nên không bao giờ
        /// tới lúc thoát, MealPots đặt thẳng sau 6 s. since âm = chưa có đợt.
        /// </summary>
        public static float StuckClock(ref Vector3 anchor, ref float since, ref float last, Vector3 pos, float now)
        {
            var d = pos - anchor;
            d.y = 0f;
            if (since < 0f || now - last > StuckForgetAfter || d.sqrMagnitude > StuckRadius * StuckRadius)
            {
                since = now;
                anchor = pos;
            }
            last = now;
            return now - since;
        }

        /// <summary>Không bị chặn vì đồ sát Ba liền chừng này giây thì đợt chờ trước coi như xong (đếm lại từ đầu).</summary>
        public const float ReachForgetAfter = 2f;

        /// <summary>
        /// Ghi một lần bị chặn vì đồ sát Ba lúc now; true nếu đợt chặn này đã kéo dài quá yieldAfter (cho đi tiếp). Đợt chỉ hết khi
        /// không bị chặn liền ReachForgetAfter: một khung lách ra hở lề không đếm lại (CI 01/11: phụ bếp bê nồi lách ra, quay lại, bị
        /// chặn, lách ra… đồng hồ 3 s không bao giờ đầy, đứng kẹt bên Ba quá 6 s). since âm = chưa có đợt.
        /// </summary>
        public static bool ReachYield(ref float since, ref float last, float now, float yieldAfter)
        {
            if (since < 0f || now - last > ReachForgetAfter)
                since = now;
            last = now;
            return now - since > yieldAfter;
        }

        /// <summary>
        /// Ma trận đổi một điểm thế giới về khung NPC đang đứng (pos, yaw) khi NPC đứng ở tư thế dự kiến (nextPos, nextYaw):
        /// đo điểm đó với hộp đồ ở tư thế hiện tại cũng như đo với hộp đã dời theo NPC.
        /// </summary>
        public static Matrix4x4 PoseShift(Vector3 pos, float yaw, Vector3 nextPos, float nextYaw) =>
            Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), Vector3.one) * Matrix4x4.TRS(nextPos, Quaternion.Euler(0f, nextYaw, 0f), Vector3.one).inverse;
    }
}
