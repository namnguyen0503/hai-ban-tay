using UnityEngine;

namespace HaiBanTay.Ship
{
    /// <summary>
    /// Dữ liệu một lối nối đi được (connections trong deck_zones.json): cầu thang (gắn trên dốc ẩn do ShipWalkable dựng)
    /// hoặc lối đồng mức (steps = 0, chỉ là điểm đánh dấu).
    /// Tọa độ bottom/top theo hệ cục bộ Unity của gốc tàu; test đi bộ và NPC đọc từ đây.
    /// </summary>
    public sealed class ShipStair : MonoBehaviour
    {
        public string id;
        public string fromZone;
        public string toZone;
        public Vector3 bottom;
        public Vector3 top;
        public float width;
        public int steps;
        /// <summary>
        /// Độ lệch ngang (hệ cục bộ gốc tàu) từ đường giữa thang tới giữa làn đi thông. Bằng 0 trừ khi model có vật cản
        /// ngang tầm đầu trên một nửa lòng thang (la bàn trên cầu thang boong xuồng).
        /// </summary>
        public Vector3 lane;

        public Transform Ship => transform.parent != null ? transform.parent.parent : null;

        public Vector3 BottomWorld => Ship != null ? Ship.TransformPoint(bottom) : bottom;

        public Vector3 TopWorld => Ship != null ? Ship.TransformPoint(top) : top;

        /// <summary>Chân thang trên làn đi thông.</summary>
        public Vector3 LaneBottomWorld => Ship != null ? Ship.TransformPoint(bottom + lane) : bottom + lane;

        /// <summary>Đỉnh thang trên làn đi thông.</summary>
        public Vector3 LaneTopWorld => Ship != null ? Ship.TransformPoint(top + lane) : top + lane;

        /// <summary>Độ dốc (độ) của mặt dốc đi được.</summary>
        public float SlopeDegrees
        {
            get
            {
                var d = top - bottom;
                return Mathf.Atan2(Mathf.Abs(d.y), new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            }
        }
    }
}
