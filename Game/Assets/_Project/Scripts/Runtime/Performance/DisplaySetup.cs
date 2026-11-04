using UnityEngine;

namespace HaiBanTay.Performance
{
    /// <summary>1920×1080 FullScreenWindow; nhịp khung theo FrameCap (bản build: vSync theo tầng, Laptop = 2 để khóa 30 fps trên màn 60 Hz).
    /// Điện thoại: độ phân giải gốc của màn hình (không hạ), toàn màn hình.</summary>
    public sealed class DisplaySetup : MonoBehaviour
    {
        public const int Width = 1920;
        public const int Height = 1080;

        public static int VSyncFor(QualityTier tier) => tier == QualityTier.Laptop ? 2 : 1;

        public void Apply(QualityTier tier)
        {
            if (Application.isMobilePlatform)
            {
                var native = Screen.currentResolution;
                if (Screen.width != native.width || Screen.height != native.height)
                    Screen.SetResolution(native.width, native.height, FullScreenMode.FullScreenWindow);
            }
            else if (!Application.isEditor)
                Screen.SetResolution(Width, Height, FullScreenMode.FullScreenWindow);
            FrameCap.Apply(tier);
        }
    }
}
