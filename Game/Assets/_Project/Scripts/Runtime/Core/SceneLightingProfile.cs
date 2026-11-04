using UnityEngine;

namespace HaiBanTay.Core
{
    /// <summary>Thông số ánh sáng một cảnh; builder đọc khi dựng cảnh. Nhóm chỉnh asset này thay vì sửa scene.</summary>
    [CreateAssetMenu(menuName = "HaiBanTay/Scene Lighting Profile")]
    public sealed class SceneLightingProfile : ScriptableObject
    {
        [Header("Ambient (Trilight)")]
        public Color ambientSky = new Color(0.55f, 0.55f, 0.6f);
        public Color ambientEquator = new Color(0.4f, 0.38f, 0.35f);
        public Color ambientGround = new Color(0.2f, 0.18f, 0.16f);

        [Header("Mặt trời")]
        public bool sun = true;
        public Color sunColor = new Color(1f, 0.95f, 0.85f);
        public float sunIntensity = 1.2f;
        public Vector3 sunEuler = new Vector3(50f, -30f, 0f);
        public bool sunShadows;

        [Header("Sương")]
        public bool fog = true;
        public Color fogColor = new Color(0.7f, 0.72f, 0.75f);
        public float fogDensity = 0.01f;

        [Header("Camera")]
        public Color background = new Color(0.55f, 0.65f, 0.75f);
    }
}
