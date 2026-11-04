using System.Collections.Generic;

namespace HaiBanTay.Npc
{
    /// <summary>Băm số nguyên xác định (cùng seed luôn cùng kết quả, không phụ thuộc UnityEngine.Random).</summary>
    public static class CrowdHash
    {
        public static uint Mix(int a, int b)
        {
            unchecked
            {
                uint h = (uint)a * 2654435761u ^ (uint)b * 40503u + 0x9E3779B9u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                h *= 3266489917u;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>Số thực trong [0, 1).</summary>
        public static float Unit(int a, int b) => (Mix(a, b) & 0xFFFFFFu) / 16777216f;
    }

    /// <summary>
    /// Luân phiên nói chuyện trong nhóm đứng quanh nhau. Thuần theo thời gian và seed nhóm, nên mọi thành viên tự tính ra
    /// cùng một người đang nói mà không cần trao đổi với nhau.
    /// </summary>
    public static class CrowdGroupLogic
    {
        /// <summary>Độ dài một lượt (giây), cố định theo nhóm trong 3–5 giây.</summary>
        public static float TurnSeconds(int groupSeed) => 3f + 2f * CrowdHash.Unit(groupSeed, 7);

        /// <summary>Chỉ số người đang nói tại thời điểm t, hoặc -1 khi cả nhóm im lặng (khoảng 1/6 số lượt).</summary>
        public static int SpeakerAt(float t, int groupSeed, int count)
        {
            if (count <= 0)
                return -1;
            int turn = (int)(System.Math.Max(0f, t) / TurnSeconds(groupSeed));
            int pick = (int)(CrowdHash.Mix(groupSeed, turn) % (uint)(count + 1)); // count = lượt im lặng
            if (count > 1)
            {
                int before = (int)(CrowdHash.Mix(groupSeed, turn - 1) % (uint)(count + 1));
                if (pick == before && pick < count)
                    pick = (pick + 1) % (count + 1);
            }
            return pick >= count ? -1 : pick;
        }

        /// <summary>Người nghe có đảo mắt nhìn quanh (IdleLook) trong lượt này không.</summary>
        public static bool ListenerLooksAround(float t, int groupSeed, int member)
        {
            int slot = (int)(System.Math.Max(0f, t) / 2.5f);
            return CrowdHash.Mix(groupSeed * 31 + member, slot) % 3u == 0u;
        }
    }

    /// <summary>
    /// Thứ tự một hàng chờ. Số 0 là đầu hàng; người đầu rời đi (Dequeue) thì mọi người sau tiến lên một ô; người mới
    /// (hoặc người đi vòng quay lại) vào đuôi hàng.
    /// </summary>
    public sealed class CrowdQueueLogic
    {
        readonly List<int> _order = new List<int>();

        public int Count => _order.Count;
        public int Front => _order.Count > 0 ? _order[0] : -1;

        /// <summary>Vào đuôi hàng. Đã có trong hàng thì không làm gì.</summary>
        public void Enqueue(int id)
        {
            if (!_order.Contains(id))
                _order.Add(id);
        }

        /// <summary>Người đầu hàng rời đi. Trả về số hiệu của người đó, -1 nếu hàng rỗng.</summary>
        public int Dequeue()
        {
            if (_order.Count == 0)
                return -1;
            int id = _order[0];
            _order.RemoveAt(0);
            return id;
        }

        /// <summary>Ô đang đứng của id (0 = đầu hàng), -1 nếu không ở trong hàng.</summary>
        public int SlotOf(int id) => _order.IndexOf(id);

        public int IdAt(int slot) => slot >= 0 && slot < _order.Count ? _order[slot] : -1;
    }
}
