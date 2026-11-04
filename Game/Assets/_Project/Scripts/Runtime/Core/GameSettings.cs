using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace HaiBanTay.Core
{
    /// <summary>
    /// Cài đặt người chơi, lưu ở persistentDataPath/settings.json (tách khỏi bản lưu tiến trình).
    /// Thành phần trong cảnh đọc GameSettings.Current mỗi khung (rẻ, không cần Find); thay đổi phát Changed.
    /// </summary>
    public sealed class GameSettings
    {
        public const float MinSensitivity = 0.3f, MaxSensitivity = 2.5f;
        public const float MinSubtitleScale = 0.8f, MaxSubtitleScale = 1.4f;

        public float MouseSensitivity { get; set; } = 1f;
        public bool InvertY { get; set; }
        public float MasterVolume { get; set; } = 1f;
        public float MusicVolume { get; set; } = 0.7f;
        public float SfxVolume { get; set; } = 1f;
        public float AmbienceVolume { get; set; } = 0.8f;
        public float VoVolume { get; set; } = 1f;
        public float SubtitleScale { get; set; } = 1f;
        public bool SubtitleBackground { get; set; } = true;
        public float SubtitleOpacity { get; set; } = 0.7f;
        /// <summary>0 nhỏ, 1 chuẩn, 2 lớn; phóng cả panel để chữ và vùng bấm cùng tăng.</summary>
        public int UiTextSize { get; set; } = 1;
        [JsonIgnore] public float UiScale => UiTextSize == 0 ? 0.9f : UiTextSize == 2 ? 1.2f : 1f;
        /// <summary>Chạm lần nữa để nhả nút Dùng/Giơ tay. Bản cài đặt cũ thiếu trường này vẫn nhận mặc định Android.</summary>
        public bool TouchToggle { get; set; } = Application.platform == RuntimePlatform.Android;
        /// <summary>Luôn dịch đầy đủ lệnh tiếng Pháp (bỏ qua tiến trình học từ) — cho buổi demo, giáo viên.</summary>
        public bool AlwaysTranslate { get; set; }
        /// <summary>Mức rung lắc camera 0–1 (chống say).</summary>
        public float CameraShake { get; set; } = 1f;
        public float HeadBob { get; set; } = 1f;
        /// <summary>"Laptop" | "PC" | null (tự chọn theo máy).</summary>
        public string Tier { get; set; }
        /// <summary>Giới hạn khung hình: 0 = tự động (vSync theo tầng, điện thoại 30), 30/60/120, -1 = không giới hạn.</summary>
        public int FpsLimit { get; set; }
        public static readonly int[] FpsChoices = { 0, 30, 60, 120, -1 };
        /// <summary>Đã xem màn Cách chơi (tự hiện một lần ở lần chơi đầu).</summary>
        public bool HelpSeen { get; set; }
        /// <summary>Mở mọi chương trong Chọn chương (demo).</summary>
        public bool UnlockAllChapters { get; set; }

        public static GameSettings Current { get; private set; } = new GameSettings();
        public static event Action Changed;

        /// <summary>Thư mục lưu; test đặt thư mục tạm.</summary>
        public static string Folder { get; set; }
        static string FilePath => Path.Combine(Folder ?? Application.persistentDataPath, "settings.json");

        public GameSettings Clone() => (GameSettings)MemberwiseClone();

        public void Clamp()
        {
            MouseSensitivity = Mathf.Clamp(MouseSensitivity, MinSensitivity, MaxSensitivity);
            SubtitleScale = Mathf.Clamp(SubtitleScale, MinSubtitleScale, MaxSubtitleScale);
            SubtitleOpacity = Mathf.Clamp01(SubtitleOpacity);
            UiTextSize = Mathf.Clamp(UiTextSize, 0, 2);
            MasterVolume = Mathf.Clamp01(MasterVolume);
            MusicVolume = Mathf.Clamp01(MusicVolume);
            SfxVolume = Mathf.Clamp01(SfxVolume);
            AmbienceVolume = Mathf.Clamp01(AmbienceVolume);
            VoVolume = Mathf.Clamp01(VoVolume);
            CameraShake = Mathf.Clamp01(CameraShake);
            HeadBob = Mathf.Clamp01(HeadBob);
            if (System.Array.IndexOf(FpsChoices, FpsLimit) < 0)
                FpsLimit = 0;
        }

        public static void Load()
        {
            var loaded = new GameSettings();
            try
            {
                if (File.Exists(FilePath))
                    loaded = JsonConvert.DeserializeObject<GameSettings>(File.ReadAllText(FilePath)) ?? new GameSettings();
            }
            catch (Exception e)
            {
                Debug.LogWarning("GameSettings: không đọc được settings.json, dùng mặc định. " + e.Message);
                loaded = new GameSettings();
            }
            loaded.Clamp();
            Current = loaded;
            Changed?.Invoke();
        }

        /// <summary>Đặt cài đặt mới, ghi file (trừ khi đang chạy test Sim) và báo cho các thành phần.</summary>
        public static void Apply(GameSettings settings)
        {
            settings.Clamp();
            Current = settings;
            if (!Sim.Enabled)
            {
                try
                {
                    SaveSystem.WriteAtomic(FilePath, JsonConvert.SerializeObject(settings, Formatting.Indented));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("GameSettings: không ghi được settings.json. " + e.Message);
                }
            }
            Changed?.Invoke();
        }

        /// <summary>Đặt lại về mặc định trong bộ nhớ, không ghi file (test).</summary>
        public static void ResetForTests()
        {
            Current = new GameSettings();
            Changed?.Invoke();
        }
    }
}
