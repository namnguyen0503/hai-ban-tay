using HaiBanTay.Interaction;
using HaiBanTay.Narrative;
using UnityEngine;

namespace HaiBanTay.Ship
{
    /// <summary>Các tham chiếu bố cục tàu do ShipBuilder dựng; director, tác vụ và test đọc từ đây, không tìm theo tên.</summary>
    public sealed class ShipLayout : MonoBehaviour
    {
        /// <summary>Gốc khối bếp (hệ cục bộ bếp của các builder cũ).</summary>
        public Transform galley;
        /// <summary>Hầm thực phẩm dưới boong giếng trước.</summary>
        public Transform foodHold;
        /// <summary>WorldRoot: biển, trời, phông xa; lắc ngược khi tàu lắc.</summary>
        public Transform world;
        /// <summary>Cầu thang tàu xuống kè hoặc cầu cảng; null khi không có.</summary>
        public Transform gangway;
        /// <summary>Gốc các cầu thang đi được (dốc ẩn trên bậc nhìn thấy).</summary>
        public Transform stairs;
        public ShipMotion motion;
        public ColdZone coldZone;
        public PlayerArea coalHold;
        public PlayerArea holdStairs;
        public Transform holdSlipSpot;
        public ShipZone galleyZone;
        public ShipZone holdZone;
        public HingedDoor galleyDoor;
        public HingedDoor corridorDoor;
        /// <summary>Dây chắn ở mép mở cầu thang tàu (có hình và collider).</summary>
        public GameObject gangwayBarrier;
        public Transform eveningSpawn;
        public InteractTarget restSeat;
        public GameObject[] eveningObjects = new GameObject[0];
        /// <summary>Đèn dầu góc buổi tối (realtime, thuộc LightBudget).</summary>
        public Light oilLamp;
    }
}
