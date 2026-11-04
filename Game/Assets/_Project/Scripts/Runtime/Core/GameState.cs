using System.Collections.Generic;

namespace HaiBanTay.Core
{
    /// <summary>Trạng thái xuyên cảnh không thuộc bus sự kiện. SaveSystem ghi/đọc toàn bộ.</summary>
    public sealed class GameState
    {
        public const int FirstDay = 1;

        /// <summary>Bụi than trên tay (D10), 0–1.</summary>
        public float Grime;
        /// <summary>Ngày trên tàu đang chơi (days.vi.json, 1..).</summary>
        public int Day = FirstDay;
        /// <summary>Ngày xa nhất đã tới (mở khóa chọn chương).</summary>
        public int MaxDay = FirstDay;
        /// <summary>Mệt mang sang đầu ngày sau (0–1).</summary>
        public float Fatigue;
        /// <summary>Đã tới Marseille ít nhất một lần.</summary>
        public bool ReachedMarseille;

        readonly HashSet<string> _vocab = new HashSet<string>();

        /// <summary>Từ của buổi học bị bớt vì tan ca muộn: ôn trước ở buổi tối sau (Tasks.StudyPlanner).</summary>
        public readonly List<string> PendingWords = new List<string>();

        public IReadOnlyCollection<string> Vocab => _vocab;
        public bool Knows(string wordId) => _vocab.Contains(wordId);
        public bool Learn(string wordId) => !string.IsNullOrEmpty(wordId) && _vocab.Add(wordId);

        public void Reset()
        {
            Grime = 0f;
            Day = FirstDay;
            MaxDay = FirstDay;
            Fatigue = 0f;
            ReachedMarseille = false;
            _vocab.Clear();
            PendingWords.Clear();
        }

        /// <summary>Sang ngày mới: giữ MaxDay là ngày xa nhất.</summary>
        public void AdvanceDay()
        {
            Day++;
            if (Day > MaxDay)
                MaxDay = Day;
        }
    }
}
