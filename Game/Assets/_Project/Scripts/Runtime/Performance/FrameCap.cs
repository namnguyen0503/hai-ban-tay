using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace HaiBanTay.Performance
{
    /// <summary>
    /// Nhịp khung hình. Play trong Editor: 30 fps (đỡ tốn điện GPU lúc thử). Batchmode (test): không giới hạn, test tự đặt nhịp
    /// (bot 60; khóa nhịp làm sai bộ đếm SetPass của RenderStatsTests). Bản build: vSync theo tầng (Laptop 2, PC 1).
    /// Ghi đè: tham số "-fps N" hoặc biến môi trường HBT_FPS=N (N = 0: không giới hạn).
    /// Cài đặt "Giới hạn FPS" (GameSettings.FpsLimit) thắng mặc định Editor/bản build nhưng không thắng ghi đè, batchmode.
    /// Điện thoại: tự động = 30 fps; không giới hạn = xin tối đa (OS vẫn chặn ở tần số màn).
    /// </summary>
    public static class FrameCap
    {
        public const int EditorFps = 30;
        public const string EnvVar = "HBT_FPS";

        public readonly struct Plan
        {
            public readonly int VSync, TargetFps;

            public Plan(int vSync, int targetFps)
            {
                VSync = vSync;
                TargetFps = targetFps;
            }

            public override string ToString() => $"vSync {VSync}, target {TargetFps}";
        }

        public static Plan Current { get; private set; } = new Plan(1, -1);

        public const int MobileUncapped = 1000;

        public static Plan Resolve(QualityTier tier, bool editor, bool batch, bool noGpu, string[] args, string env) =>
            Resolve(tier, editor, batch, noGpu, args, env, 0, 0);

        /// <param name="limit">GameSettings.FpsLimit: 0 tự động, &gt;0 số khung, -1 không giới hạn.</param>
        /// <param name="mobileRefresh">&gt;0 khi chạy trên điện thoại: tần số màn hình (Hz).</param>
        public static Plan Resolve(QualityTier tier, bool editor, bool batch, bool noGpu, string[] args, string env, int limit, int mobileRefresh)
        {
            if (noGpu)
                return new Plan(0, -1);
            int? fps = Override(args, env);
            if (fps.HasValue)
                return new Plan(0, fps.Value > 0 ? fps.Value : -1);
            if (batch)
                return new Plan(0, -1);
            if (limit > 0)
                return new Plan(0, limit);
            if (limit < 0)
                return new Plan(0, mobileRefresh > 0 ? MobileUncapped : -1);
            if (mobileRefresh > 0 && !editor)
                return new Plan(0, 30);
            if (editor)
                return new Plan(0, EditorFps);
            return new Plan(DisplaySetup.VSyncFor(tier), -1);
        }

        static int? Override(string[] args, string env)
        {
            if (args != null)
            {
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (string.Equals(args[i], "-fps", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out int a))
                        return a;
                }
            }
            return int.TryParse(env, out int e) ? e : (int?)null;
        }

        public static void Apply(QualityTier tier)
        {
            int refresh = 0;
            if (Application.isMobilePlatform)
                refresh = Mathf.Max(30, Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value));
            Current = Resolve(tier, Application.isEditor, Application.isBatchMode,
                SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null,
                Environment.GetCommandLineArgs(), Environment.GetEnvironmentVariable(EnvVar),
                Core.GameSettings.Current.FpsLimit, refresh);
            QualitySettings.vSyncCount = Current.VSync;
            Application.targetFrameRate = Current.TargetFps;
        }

        // Cả cảnh/test chạy trước khi có GameRoot (ảnh chụp, PlayMode).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() => Apply(QualityTierManager.Resolve(Application.platform, Environment.GetCommandLineArgs()));
    }
}
