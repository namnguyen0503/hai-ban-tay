using HaiBanTay.Interaction;
using HaiBanTay.Player;
using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>
    /// Né vật cản tĩnh (tường, bàn, tủ) cho NPC đi thẳng giữa các điểm đặt sẵn: đoạn thẳng bị chắn thì rẽ qua một góc của vật cản
    /// (bao nở theo bán kính thân). Không NavMesh; sàn, trần, bậc thấp, trigger, người chơi, NPC và đồ cầm được bỏ qua.
    /// </summary>
    public static class NpcPathing
    {
        const float StepHeight = 0.3f;
        const float BodyHeight = 1.4f;
        /// <summary>Phần cuối đoạn đường (m) không tính là bị chắn: điểm đích sát bàn/tường là chỗ đứng hợp lệ.</summary>
        const float EndSlack = 0.45f;

        static readonly RaycastHit[] Hits = new RaycastHit[24];
        static readonly Collider[] Overlaps = new Collider[16];

        /// <summary>Collider này có tính là vật cản cố định đối với thân NPC ở độ cao feetY không.</summary>
        public static bool IsObstacle(Collider c, Transform self, float feetY)
        {
            if (c == null || c.isTrigger)
                return false;
            if (self != null && c.transform.IsChildOf(self))
                return false;
            var b = c.bounds;
            // Sàn, thảm, ngưỡng cửa thấp và trần không cản chân.
            if (b.max.y < feetY + StepHeight || b.min.y > feetY + BodyHeight)
                return false;
            if (c.GetComponentInParent<NpcAgent>() != null || c.GetComponentInParent<PlayerRig>() != null
                || c.GetComponentInParent<Holdable>() != null)
                return false;
            return true;
        }

        /// <summary>Đoạn from→to (mặt phẳng ngang) có bị vật cản cố định chắn không; trả về vật chắn gần nhất.</summary>
        public static bool Blocked(Vector3 from, Vector3 to, float radius, Transform self, out Collider blocker)
        {
            blocker = null;
            var d = to - from;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist < 0.02f)
                return false;
            var p1 = from + Vector3.up * (StepHeight + radius);
            var p2 = from + Vector3.up * BodyHeight;
            int n = Physics.CapsuleCastNonAlloc(p1, p2, radius, d / dist, Hits, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                // Đang chạm sẵn lúc bắt đầu (điểm va bằng 0): để NPC thoát ra, không coi là chắn.
                if (h.distance <= 0.0001f && h.point == Vector3.zero)
                    continue;
                if (h.distance > dist - EndSlack || h.distance >= best)
                    continue;
                if (!IsObstacle(h.collider, self, from.y))
                    continue;
                best = h.distance;
                blocker = h.collider;
            }
            return blocker != null;
        }

        /// <summary>Chỗ đứng p có trống không (thân không chạm vật cản cố định).</summary>
        /// <summary>Vật tĩnh đầu tiên làm FreeAt(p) sai (chẩn đoán); null nếu trống.</summary>
        public static Collider BlockerAt(Vector3 p, float radius, Transform self)
        {
            int n = Physics.OverlapCapsuleNonAlloc(p + Vector3.up * (StepHeight + radius), p + Vector3.up * BodyHeight, radius * 0.9f, Overlaps,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (IsObstacle(Overlaps[i], self, p.y))
                    return Overlaps[i];
            return null;
        }

        public static bool FreeAt(Vector3 p, float radius, Transform self)
        {
            int n = Physics.OverlapCapsuleNonAlloc(p + Vector3.up * (StepHeight + radius), p + Vector3.up * BodyHeight, radius * 0.9f, Overlaps,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (IsObstacle(Overlaps[i], self, p.y))
                    return false;
            return true;
        }

        /// <summary>
        /// Đoạn from→to bị chắn thì chọn một góc của vật chắn làm điểm trung gian: tới được từ from, đích vẫn còn xa nhất ít.
        /// Trả false khi không bị chắn hoặc không có góc nào dùng được.
        /// </summary>
        public static bool Plan(Vector3 from, Vector3 to, float radius, Transform self, out Vector3 via)
        {
            via = default;
            if (!Blocked(from, to, radius, self, out var blocker))
                return false;
            var b = blocker.bounds;
            float m = radius + 0.12f;
            var corners = new[]
            {
                new Vector3(b.min.x - m, from.y, b.min.z - m), new Vector3(b.min.x - m, from.y, b.max.z + m),
                new Vector3(b.max.x + m, from.y, b.min.z - m), new Vector3(b.max.x + m, from.y, b.max.z + m),
            };
            bool found = false;
            float bestScore = float.PositiveInfinity;
            foreach (var p in corners)
            {
                if (!FreeAt(p, radius, self) || Blocked(from, p, radius, self, out _))
                    continue;
                float score = Vector3.Distance(from, p) + Vector3.Distance(p, to);
                // Chặng sau còn bị chắn: vẫn dùng được (sẽ rẽ tiếp) nhưng kém hơn.
                if (Blocked(p, to, radius, self, out _))
                    score += 50f;
                if (score < bestScore)
                {
                    bestScore = score;
                    via = p;
                    found = true;
                }
            }
            return found;
        }
    }
}
