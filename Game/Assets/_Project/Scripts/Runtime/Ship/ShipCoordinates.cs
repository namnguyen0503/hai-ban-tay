using UnityEngine;

namespace HaiBanTay.Ship
{
    /// <summary>
    /// Phép đổi tọa độ duy nhất của con tàu, dùng chung cho builder, director và test.
    /// Blender (model, deck_zones.json): mũi +X, mạn trái (port) +Y, lên +Z, gốc ở mặt nước giữa tàu, đơn vị mét.
    /// Unity, hệ cục bộ của gốc tàu: mũi +X, lên +Y, port +Z, tức (ux, uy, uz) = (bx, bz, by).
    /// Khối bếp: hệ cục bộ (lx, ly, lz) của các builder bếp cũ; gốc khối bếp nằm trong tàu ở Unity (−0,20; 5,60; −0,85),
    /// xoay 180° quanh Y. Tương đương công thức của deck_zones: bx = −lx − 0,20; by = −lz − 0,85; bz = ly + 5,60.
    /// </summary>
    public static class ShipCoordinates
    {
        /// <summary>Gốc khối bếp trong hệ cục bộ Unity của gốc tàu.</summary>
        public static readonly Vector3 GalleyOrigin = new Vector3(-0.2f, 5.6f, -0.85f);

        /// <summary>Hướng khối bếp trong tàu: vách tây của bếp (−lx) quay về mũi tàu (+X).</summary>
        public static readonly Quaternion GalleyRotation = Quaternion.Euler(0f, 180f, 0f);

        /// <summary>Importer FBX của Unity đặt mũi tàu về −X và port về −Z: model phải xoay đúng một lần quanh Y.</summary>
        public static readonly Quaternion ModelRotation = Quaternion.Euler(0f, 180f, 0f);

        public static Vector3 FromBlender(Vector3 p) => new Vector3(p.x, p.z, p.y);

        public static Vector3 ToBlender(Vector3 p) => new Vector3(p.x, p.z, p.y);

        /// <summary>Kích thước hộp: đổi trục như điểm, không đổi dấu.</summary>
        public static Vector3 SizeFromBlender(Vector3 s) => new Vector3(s.x, s.z, s.y);

        public static Vector3 FromGalley(Vector3 p) => GalleyOrigin + GalleyRotation * p;

        public static Vector3 ToGalley(Vector3 p) => Quaternion.Inverse(GalleyRotation) * (p - GalleyOrigin);

        /// <summary>Góc yaw Blender (dương: +X quay về +Y) sang góc quanh trục Y của Unity (dương: +X quay về −Z).</summary>
        public static float YawFromBlender(float degrees) => -degrees;
    }
}
