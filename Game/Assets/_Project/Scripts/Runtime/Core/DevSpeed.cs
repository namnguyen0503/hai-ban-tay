using UnityEngine;

namespace HaiBanTay.Core
{
    /// <summary>
    /// Tua nhanh để chơi thử (Ctrl+= / Ctrl+-): ×1, ×2, ×4, ×8. Giữ qua chuyển cảnh; thoại phát nhanh theo (pitch, tối đa ×3).
    /// Không áp khi Sim (bot/test tự điều khiển Time.timeScale).
    /// </summary>
    public static class DevSpeed
    {
        static readonly float[] Levels = { 1f, 2f, 4f, 8f };
        static int _level;

        public static float Scale => Levels[_level];

        /// <summary>Tốc độ phát thoại: theo Scale nhưng không quá ×3 (giới hạn pitch của AudioService).</summary>
        public static float VoiceRate => Mathf.Clamp(Scale, 1f, 3f);

        public static void Step(int direction)
        {
            if (Sim.Enabled)
                return;
            _level = Mathf.Clamp(_level + direction, 0, Levels.Length - 1);
            // Đang tạm dừng (timeScale 0) thì để PauseController/DebugNotes trả lại; chỉ đổi khi game đang chạy.
            if (Time.timeScale > 0f)
                Time.timeScale = Scale;
            Debug.Log($"DevSpeed x{Scale}");
        }
    }
}
