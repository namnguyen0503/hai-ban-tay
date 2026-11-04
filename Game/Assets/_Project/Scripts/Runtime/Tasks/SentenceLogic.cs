using System.Collections.Generic;

namespace HaiBanTay.Tasks
{
    /// <summary>
    /// Viết câu: các từ của một câu tiếng Pháp bị xáo; bấm lần lượt từng từ theo đúng thứ tự để viết lại cả câu. Bấm sai thì
    /// từ không vào câu (từ trùng chữ thì từ nào cũng được). Xong câu thì sang câu sau.
    /// </summary>
    public sealed class SentenceLogic
    {
        public sealed class Item
        {
            public string[] Tokens = new string[0];
            public string Translation;
        }

        readonly List<Item> _items;
        readonly List<int[]> _shuffled = new List<int[]>();
        bool[] _used = new bool[0];

        public SentenceLogic(IList<Item> items, int seed = 11)
        {
            _items = new List<Item>(items);
            var rng = new System.Random(seed);
            foreach (var it in _items)
            {
                var order = new int[it.Tokens.Length];
                for (int i = 0; i < order.Length; i++)
                    order[i] = i;
                for (int i = order.Length - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }
                // Không để thứ tự xáo trùng câu đúng.
                bool same = true;
                for (int i = 0; i < order.Length; i++)
                    same &= order[i] == i;
                if (same && order.Length > 1)
                    (order[0], order[1]) = (order[1], order[0]);
                _shuffled.Add(order);
            }
            Begin();
        }

        public int Count => _items.Count;
        public int Index { get; private set; }
        public bool Done => Index >= Count;
        public Item Current => Done ? null : _items[Index];
        /// <summary>Số từ đã viết đúng của câu đang làm.</summary>
        public int Placed { get; private set; }
        public int WrongAttempts { get; private set; }
        /// <summary>Thứ tự hiện các nút từ (chỉ số trong Item.Tokens).</summary>
        public int[] Shuffled => Done ? new int[0] : _shuffled[Index];

        public bool IsUsed(int token) => token >= 0 && token < _used.Length && _used[token];

        /// <summary>Phần câu đã viết.</summary>
        public string Written
        {
            get
            {
                if (Done)
                    return string.Empty;
                return string.Join(" ", _items[Index].Tokens, 0, Placed);
            }
        }

        void Begin()
        {
            Placed = 0;
            _used = Done ? new bool[0] : new bool[_items[Index].Tokens.Length];
        }

        /// <summary>token: chỉ số trong Item.Tokens. Trả về true nếu từ được viết vào câu.</summary>
        public bool TryPlace(int token)
        {
            if (Done || token < 0 || token >= _used.Length || _used[token])
                return false;
            var tokens = _items[Index].Tokens;
            if (tokens[token] != tokens[Placed])
            {
                WrongAttempts++;
                return false;
            }
            _used[token] = true;
            Placed++;
            if (Placed >= tokens.Length)
            {
                Index++;
                Begin();
            }
            return true;
        }

        /// <summary>Sim / test: viết đúng cả câu đang làm.</summary>
        public void SimulateSentence()
        {
            if (Done)
                return;
            var tokens = _items[Index].Tokens;
            int index = Index;
            while (!Done && Index == index)
            {
                for (int i = 0; i < tokens.Length; i++)
                    if (!_used[i] && tokens[i] == tokens[Placed])
                    {
                        TryPlace(i);
                        break;
                    }
            }
        }
    }
}
