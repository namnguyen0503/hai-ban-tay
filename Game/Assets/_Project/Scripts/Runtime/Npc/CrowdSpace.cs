using System.Collections.Generic;
using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>
    /// Quy tắc nhường nhau giữa các NPC (lớp thuần, test được). Người khác chắn đường khi đứng trong hình nón phía trước
    /// (NpcStepLogic.Blocked). Hai người cùng đang đi thì chỉ người có số hiệu lớn hơn phải nhường, nên không bao giờ chờ
    /// lẫn nhau. Người đứng yên thì luôn chắn, người đi tới phải vòng qua. Người đi ngược chiều được phát hiện từ xa
    /// (ApproachClearance) để người nhường kịp né trước khi hai bên chạm nhau; người đi cùng chiều chỉ chắn khi sát lưng.
    /// </summary>
    public static class CrowdAvoidLogic
    {
        public const float DefaultClearance = 0.75f;
        public const float DefaultHalfAngle = 50f;
        /// <summary>Phát hiện người đi ngược chiều từ khoảng cách này (m), trong hình nón hẹp hơn.</summary>
        public const float ApproachClearance = 2f;
        public const float ApproachHalfAngle = 35f;

        /// <summary>Hai hướng đi ngược nhau (tích vô hướng &lt; -0,3).</summary>
        public static bool Opposing(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f)
                return false;
            return Vector3.Dot(a.normalized, b.normalized) < -0.3f;
        }

        /// <summary>Nửa bề rộng lối hai thân đi qua nhau không chạm (thân bán kính 0,24 m, chừa 2 cm).</summary>
        public const float PassHalfWidth = 0.5f;

        /// <summary>
        /// Người kia ở phía trước trong length mét và lệch ngang dưới PassHalfWidth: đi tiếp thì hai vai chạm nhau. Hình nón góc chỉ
        /// bắt người ở gần trục; người lệch 0,45 m ở cự ly 0,4 m nằm ngoài nón mà vẫn chạm (verify 14/10: quản bếp, phụ bếp đi ngược
        /// chiều, tâm cách nhau 0,34 m trong 0,38 s).
        /// </summary>
        public static bool InLane(Vector3 me, Vector3 heading, Vector3 other, float length)
        {
            var d = other - me;
            d.y = 0f;
            heading.y = 0f;
            if (heading.sqrMagnitude < 1e-6f)
                return false;
            var h = heading.normalized;
            float along = Vector3.Dot(d, h);
            if (along <= 0f || along > length)
                return false;
            return (d - h * along).magnitude < PassHalfWidth;
        }

        public static bool Blocks(int meId, Vector3 mePosition, Vector3 heading, int otherId, Vector3 otherPosition,
            bool otherWalking, Vector3 otherHeading = default, float clearance = DefaultClearance, float halfAngle = DefaultHalfAngle)
        {
            if (otherId == meId)
                return false;
            bool near = NpcStepLogic.Blocked(mePosition, heading, otherPosition, clearance, halfAngle);
            // Làn chạm vai chỉ xét người đang đi ngược chiều: người đứng yên lệch 0,3–0,5 m thì lướt qua được (chặn cả người đứng thì
            // phụ bếp kẹt sau quản bếp đứng trông bếp giữa lối hẹp).
            bool approaching = !near && otherWalking && Opposing(heading, otherHeading)
                               && (NpcStepLogic.Blocked(mePosition, heading, otherPosition, ApproachClearance, ApproachHalfAngle)
                                   || InLane(mePosition, heading, otherPosition, ApproachClearance));
            if (!near && !approaching)
                return false;
            return !otherWalking || otherId < meId;
        }

        /// <summary>
        /// Hướng đi vòng quanh vật chắn: xoay heading một góc về phía xa vật chắn. Chắn đúng giữa thì rẽ trái, nên hai người
        /// đi ngược chiều nhau cùng rẽ về bên trái của mình và lách qua nhau như trên đường.
        /// </summary>
        public static Vector3 SidestepHeading(Vector3 heading, Vector3 toBlocker, float degrees = 60f)
        {
            heading.y = 0f;
            toBlocker.y = 0f;
            float side = heading.z * toBlocker.x - heading.x * toBlocker.z; // > 0: vật chắn lệch bên phải
            float angle = side > 0.02f ? -degrees : side < -0.02f ? degrees : -degrees;
            return Quaternion.AngleAxis(angle, Vector3.up) * heading;
        }
    }

    /// <summary>
    /// Sổ vị trí mọi NpcAgent đang chạy. Dựng lưới ô vài lần mỗi giây (không mỗi khung), mỗi NPC chỉ hỏi láng giềng trong
    /// các ô quanh mình. Vị trí trong lưới cũ tối đa RebuildInterval giây, nên bán kính tìm được nới thêm một đoạn.
    /// </summary>
    public static class CrowdSpace
    {
        public const float CellSize = 2f;
        public const float RebuildInterval = 0.2f;
        const float StaleMargin = 0.35f;
        const float QueryRadius = CrowdAvoidLogic.ApproachClearance + StaleMargin;

        static readonly List<NpcAgent> Agents = new List<NpcAgent>();
        static readonly CrowdGrid Grid = new CrowdGrid(CellSize);
        static readonly List<int> Hits = new List<int>(16);
        static float _builtAt = float.NegativeInfinity;
        static int _nextId = 1;

        public static int Count => Agents.Count;

        /// <summary>Đăng ký một NPC, trả về số hiệu (nhỏ hơn = được đi trước).</summary>
        public static int Register(NpcAgent agent)
        {
            if (!Agents.Contains(agent))
                Agents.Add(agent);
            _builtAt = float.NegativeInfinity;
            return _nextId++;
        }

        public static void Unregister(NpcAgent agent)
        {
            if (Agents.Remove(agent))
                _builtAt = float.NegativeInfinity;
        }

        static void Refresh()
        {
            float now = Time.time;
            if (now >= _builtAt && now - _builtAt < RebuildInterval)
                return;
            _builtAt = now;
            Grid.Clear();
            foreach (var a in Agents)
                Grid.Add(a.Position);
        }

        /// <summary>NPC khác (đang bật) đứng trong bán kính radius quanh spot, hoặc null.</summary>
        public static NpcAgent Occupant(Vector3 spot, float radius, NpcAgent except)
        {
            foreach (var other in Agents)
            {
                if (other == except || other == null || !other.isActiveAndEnabled)
                    continue;
                var d = other.Position - spot;
                d.y = 0f;
                if (d.sqrMagnitude < radius * radius)
                    return other;
            }
            return null;
        }

        /// <summary>NPC khác (đang bật) có tâm gần point nhất trong bán kính radius, hoặc null.</summary>
        public static NpcAgent Nearest(NpcAgent self, Vector3 point, float radius)
        {
            if (Agents.Count < 2)
                return null;
            Refresh();
            Grid.Query(point, radius + StaleMargin, Hits);
            NpcAgent best = null;
            float bestSq = radius * radius;
            foreach (int i in Hits)
            {
                var other = Agents[i];
                if (other == self || other == null || !other.isActiveAndEnabled)
                    continue;
                var d = other.Position - point;
                d.y = 0f;
                if (d.sqrMagnitude >= bestSq)
                    continue;
                bestSq = d.sqrMagnitude;
                best = other;
            }
            return best;
        }

        /// <summary>NPC đang chắn đường của self khi đi theo heading, hoặc null.</summary>
        public static NpcAgent FindBlocker(NpcAgent self, Vector3 heading)
        {
            if (Agents.Count < 2)
                return null;
            Refresh();
            var me = self.Position;
            Grid.Query(me, QueryRadius, Hits);
            foreach (int i in Hits)
            {
                var other = Agents[i];
                if (other == self || !other.isActiveAndEnabled)
                    continue;
                // Đang đứng chờ (nhường) thì coi như đứng yên: tính là đang đi thì người được nhường đi thẳng xuyên qua người đang chờ
                // (verify 14/10: quản bếp đi xuyên phụ bếp 0,46 s).
                if (CrowdAvoidLogic.Blocks(self.CrowdId, me, heading, other.CrowdId, other.Position, other.Walking && !other.Waiting, other.Heading))
                    return other;
            }
            return null;
        }
    }
}
