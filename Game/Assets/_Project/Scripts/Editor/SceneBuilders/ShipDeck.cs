using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>
    /// Tường vô hình chặn biên khu vực chơi ngoài tàu (kè, cầu cảng, phố). Từ M10b, sàn, thành mạn và vật cản trên tàu
    /// dựng theo deck_zones.json ở Ship/ShipWalkable; model tàu đặt ở Ship/ShipBuilder.
    /// </summary>
    public static class ShipDeck
    {
        public static GameObject Wall(Transform parent, Vector3 pos, Vector3 size)
        {
            var go = new GameObject("InvisibleWall");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<BoxCollider>().size = size;
            go.layer = BuilderUtils.IgnoreRaycastLayer;
            return go;
        }
    }
}
