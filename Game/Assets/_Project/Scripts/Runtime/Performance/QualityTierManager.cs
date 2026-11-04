using System;
using UnityEngine;

namespace HaiBanTay.Performance
{
    public enum QualityTier
    {
        Laptop,
        PC
    }

    /// <summary>Windows/Android → Laptop; Linux player/Editor → PC; ghi đè bằng "-tier laptop|pc" hoặc F9.</summary>
    public sealed class QualityTierManager : MonoBehaviour
    {
        public QualityTier Current { get; private set; }
        public event Action<QualityTier> TierChanged;

        public static QualityTier Resolve(RuntimePlatform platform, string[] args)
        {
            if (args != null)
            {
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (!string.Equals(args[i], "-tier", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (string.Equals(args[i + 1], "laptop", StringComparison.OrdinalIgnoreCase))
                        return QualityTier.Laptop;
                    if (string.Equals(args[i + 1], "pc", StringComparison.OrdinalIgnoreCase))
                        return QualityTier.PC;
                }
            }
            return platform == RuntimePlatform.WindowsPlayer || platform == RuntimePlatform.Android ? QualityTier.Laptop : QualityTier.PC;
        }

        public void Initialize()
        {
            Set(Resolve(Application.platform, Environment.GetCommandLineArgs()));
        }

        /// <summary>Cài đặt chọn tầng ("Laptop" | "PC"); null = theo máy (Resolve).</summary>
        public void Apply(string tier)
        {
            if (Enum.TryParse(tier, out QualityTier t))
            {
                if (t != Current)
                    Set(t);
            }
            else
            {
                var auto = Resolve(Application.platform, Environment.GetCommandLineArgs());
                if (auto != Current)
                    Set(auto);
            }
        }

        public void Toggle()
        {
            Set(Current == QualityTier.Laptop ? QualityTier.PC : QualityTier.Laptop);
        }

        void Set(QualityTier tier)
        {
            Current = tier;
            int index = Array.IndexOf(QualitySettings.names, tier.ToString());
            if (index >= 0)
                QualitySettings.SetQualityLevel(index, true);
            else
                Debug.LogWarning($"QualityTierManager: không có Quality level '{tier}'. Chạy menu HaiBanTay/Setup/Run All.");
            // Android đã cắt mip lớn qua importer (1K/2K); không hạ thêm một mip ở runtime.
            if (Application.platform == RuntimePlatform.Android)
                QualitySettings.globalTextureMipmapLimit = 0;
            TierChanged?.Invoke(tier);
        }
    }
}
