using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Đồng hồ ca chạy theo việc, không theo thời gian thật: mỗi lượt có khung giờ [From, To]; giờ đích = From + tiến độ
    /// việc × độ dài khung. Đứng không thì giờ không trôi; làm xong một phần việc thì kim đuổi tới giờ đích (mượt, không
    /// nhảy). Tiến độ chỉ tăng, việc phát sinh giữa lượt chỉ làm kim chờ chứ không quay lui.
    /// </summary>
    public sealed class ShiftClockLogic
    {
        public float Minutes;
        public bool Running = true;
        /// <summary>Tốc độ kim đuổi tới giờ đích (phút trong game mỗi giây thật).</summary>
        public float CatchUpPerSecond = 30f;

        public float From { get; private set; }
        public float To { get; private set; }
        public float Progress { get; private set; }
        public float Target => Mathf.Lerp(From, To, Progress);

        public ShiftClockLogic(float startMinutes)
        {
            Minutes = From = To = startMinutes;
        }

        /// <summary>Khung giờ của lượt mới; tiến độ về 0. Không lùi giờ.</summary>
        public void SetWindow(float from, float to)
        {
            From = Mathf.Max(from, Minutes);
            To = Mathf.Max(to, From);
            Progress = 0f;
        }

        /// <summary>Tiến độ việc của lượt (0–1); chỉ tăng.</summary>
        public void SetProgress(float progress)
        {
            Progress = Mathf.Max(Progress, Mathf.Clamp01(progress));
        }

        public void Tick(float dt)
        {
            if (Running && dt > 0f && Minutes < Target)
                Minutes = Mathf.MoveTowards(Minutes, Target, CatchUpPerSecond * dt);
        }

        public bool IsPast(int minute) => minute > 0 && Minutes >= minute;

        public string Text => Format(Minutes);

        /// <summary>"4:05", "19:40" (24 giờ, giờ không có số 0 đầu).</summary>
        public static string Format(float minutes)
        {
            int total = Mathf.FloorToInt(Mathf.Repeat(minutes, 24f * 60f));
            return $"{total / 60}:{total % 60:00}";
        }

        /// <summary>Tiến độ của bảng việc: phần việc đã làm trên tổng việc (bảng trống = xong).</summary>
        public static float BoardProgress(OrderBoardLogic board)
        {
            int need = 0, done = 0;
            foreach (var o in board.Orders)
            {
                need += o.Need;
                done += System.Math.Min(o.Done, o.Need);
            }
            return need == 0 ? 1f : (float)done / need;
        }
    }
}
