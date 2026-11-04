using System.Collections.Generic;
using HaiBanTay.Content;

namespace HaiBanTay.Tasks
{
    /// <summary>Ghép cặp từ Pháp–Việt (B2.6). Cột tiếng Việt xáo trộn cố định theo seed.</summary>
    public sealed class MatchWordsLogic
    {
        readonly List<FrenchPair> _pairs;
        readonly bool[] _matched;

        public int[] ViOrder { get; }
        public int Count => _pairs.Count;
        public int MatchedCount { get; private set; }
        public bool Done => MatchedCount == Count;
        public int WrongAttempts { get; private set; }

        public MatchWordsLogic(IList<FrenchPair> pairs, int seed = 3)
        {
            _pairs = new List<FrenchPair>(pairs);
            _matched = new bool[_pairs.Count];
            ViOrder = new int[_pairs.Count];
            for (int i = 0; i < ViOrder.Length; i++)
                ViOrder[i] = i;
            var rng = new System.Random(seed);
            for (int i = ViOrder.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (ViOrder[i], ViOrder[j]) = (ViOrder[j], ViOrder[i]);
            }
            // Tránh trường hợp xáo xong vẫn thẳng hàng với cột tiếng Pháp.
            if (ViOrder.Length > 1 && IsIdentity())
                (ViOrder[0], ViOrder[1]) = (ViOrder[1], ViOrder[0]);
        }

        bool IsIdentity()
        {
            for (int i = 0; i < ViOrder.Length; i++)
                if (ViOrder[i] != i)
                    return false;
            return true;
        }

        public FrenchPair Pair(int index) => _pairs[index];
        public bool IsMatched(int index) => _matched[index];

        /// <summary>frIndex: chỉ số cặp ở cột Pháp; viIndex: chỉ số cặp của từ tiếng Việt được chọn.</summary>
        public bool TryMatch(int frIndex, int viIndex)
        {
            if (frIndex < 0 || viIndex < 0 || frIndex >= Count || viIndex >= Count)
                return false;
            if (_matched[frIndex] || _matched[viIndex])
                return false;
            if (frIndex != viIndex)
            {
                WrongAttempts++;
                return false;
            }
            _matched[frIndex] = true;
            MatchedCount++;
            return true;
        }
    }
}
