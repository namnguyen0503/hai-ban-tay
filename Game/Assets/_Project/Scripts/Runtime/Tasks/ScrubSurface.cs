using UnityEngine;

namespace HaiBanTay.Tasks
{
    /// <summary>
    /// Mặt cần cọ của một vật rỗng (nồi, chảo) trong hệ tọa độ của chính vật đó (y lên, đáy ở <see cref="BottomY"/>): con trỏ ẩn của
    /// ScrubMotion là một điểm trên đĩa "trải phẳng" bán kính <see cref="Domain"/>. Trong bán kính đáy, con trỏ là điểm trên đáy; ngoài đó
    /// (chỉ khi có <see cref="WallHeight"/>) là điểm trên thành trong, cao dần theo độ dài cung, như trải lòng bát ra mặt phẳng. Mặt nạ bẩn
    /// (DirtMask) và lớp phủ (DirtOverlayMesh) dùng cùng phép trải này nên vết bẩn mất đúng chỗ đầu chải đi qua.
    /// </summary>
    public sealed class ScrubSurface
    {
        /// <summary>Bán kính lòng đáy (m, tới chân thành trong).</summary>
        public readonly float BottomRadius;
        /// <summary>Độ cao mặt đáy trong (m, hệ vật).</summary>
        public readonly float BottomY;
        /// <summary>Phần thành trong tính vào mặt cọ (m, đo dọc thành từ đáy lên); 0 = chỉ có đáy.</summary>
        public readonly float WallHeight;
        /// <summary>Lề không cọ tới sát chân thành (m) khi không có thành: đầu chải có bề ngang.</summary>
        public readonly float Margin;

        public ScrubSurface(float bottomRadius, float bottomY, float wallHeight = 0f, float margin = 0.02f)
        {
            BottomRadius = Mathf.Max(0.01f, bottomRadius);
            BottomY = bottomY;
            WallHeight = Mathf.Max(0f, wallHeight);
            Margin = Mathf.Max(0f, margin);
        }

        /// <summary>Bán kính đĩa trải phẳng (m).</summary>
        public float Domain => BottomRadius + WallHeight;
        /// <summary>Con trỏ đi được tới bán kính này (trừ lề).</summary>
        public float Reachable => WallHeight > 0f ? Domain - Margin : BottomRadius - Margin;

        /// <summary>Giữ con trỏ trong mặt cọ.</summary>
        public Vector2 Clamp(Vector2 c) => Vector2.ClampMagnitude(c, Mathf.Max(0f, Reachable));

        /// <summary>Điểm và pháp tuyến (hướng ra phía mở của lòng vật) của con trỏ, trong hệ vật.</summary>
        public void Evaluate(Vector2 c, out Vector3 point, out Vector3 normal)
        {
            float r = c.magnitude;
            if (r <= BottomRadius - Fillet || WallHeight <= 0f || r < 1e-5f)
            {
                point = new Vector3(c.x, BottomY, c.y);
                normal = Vector3.up;
                return;
            }
            var dir = c / r;
            var inward = new Vector3(-dir.x, 0f, -dir.y);
            if (r <= BottomRadius)
                point = new Vector3(c.x, BottomY, c.y);
            else
            {
                float up = Mathf.Min(r - BottomRadius, WallHeight);
                point = new Vector3(dir.x * BottomRadius, BottomY + up, dir.y * BottomRadius);
            }
            // Góc đáy–thành bo tròn: pháp tuyến quay dần từ "lên" sang "vào tâm" trong dải ±Fillet quanh chân thành. Lật 90° ngay ở chân
            // thành thì bàn chải xoay ngang trong một khung, mũi lông nhảy 6 cm (brush17b, nồi cao từ ghế).
            float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(BottomRadius - Fillet, BottomRadius + Fillet, r));
            normal = Vector3.Slerp(Vector3.up, inward, k).normalized;
        }

        /// <summary>Nửa bề rộng dải bo góc đáy–thành (m, đo trên đĩa trải phẳng).</summary>
        public const float Fillet = 0.08f;

        /// <summary>uv mặt nạ (0–1) của con trỏ: đĩa trải phẳng nội tiếp ô vuông của mặt nạ.</summary>
        public Vector2 MaskUv(Vector2 c) => c / (2f * Domain) + new Vector2(0.5f, 0.5f);

        /// <summary>Bán kính (m trên mặt) đổi sang đơn vị uv của mặt nạ.</summary>
        public float UvScale => 1f / (2f * Domain);

        /// <summary>Con trỏ gần nhất của một điểm trong hệ vật (đặt lại con trỏ theo chỗ tay đang chĩa tới).</summary>
        public Vector2 Unwrap(Vector3 local)
        {
            var flat = new Vector2(local.x, local.z);
            float r = flat.magnitude;
            if (WallHeight <= 0f || r < BottomRadius - 0.005f || local.y <= BottomY + 0.005f)
                return Clamp(flat);
            var dir = r > 1e-5f ? flat / r : Vector2.up;
            return Clamp(dir * (BottomRadius + Mathf.Clamp(local.y - BottomY, 0f, WallHeight)));
        }
    }
}
