using UnityEngine;

namespace HaiBanTay.Tasks
{
    /// <summary>B2.4: tiến độ cọ nồi kẹp 0–1; chỉ tăng khi đang giữ chuột trái, nhìn vào lòng nồi và có rê chuột.
    /// Có thể đặt trần tạm (nồi chao, chờ Jacques giữ).</summary>
    public sealed class ScrubLogic
    {
        public float PixelsForFull = 9000f;
        public float MinPixelsPerFrame = 0.5f;
        public float MaxPixelsPerSecond = 2500f;

        public float Progress { get; private set; }
        public float Cap { get; set; } = 1f;
        public bool Done => Progress >= 1f;

        /// <summary>Trả về true nếu khung này có tiến triển.</summary>
        public bool Tick(bool held, bool onPot, Vector2 mouseDelta, float dt)
        {
            if (!held || !onPot || Done)
                return false;
            float px = mouseDelta.magnitude;
            if (px < MinPixelsPerFrame)
                return false;
            px = Mathf.Min(px, MaxPixelsPerSecond * Mathf.Max(dt, 1e-4f));
            float before = Progress;
            Progress = Mathf.Clamp(Progress + px / PixelsForFull, 0f, Mathf.Clamp01(Cap));
            return Progress > before;
        }

        public void ForceProgress(float value)
        {
            Progress = Mathf.Clamp01(value);
        }

        /// <summary>Chiếc nồi mới: tiến độ về 0, bỏ trần tạm.</summary>
        public void Reset()
        {
            Progress = 0f;
            Cap = 1f;
        }
    }
}
