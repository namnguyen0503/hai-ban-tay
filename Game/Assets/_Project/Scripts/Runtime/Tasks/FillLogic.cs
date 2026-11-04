using System.Collections.Generic;

namespace HaiBanTay.Tasks
{
    /// <summary>
    /// Điền từ (đọc câu): mỗi câu tiếng Pháp có một chỗ trống, chọn đúng từ trong vài lựa chọn. Chọn sai thì từ đó mờ đi,
    /// không mất gì ngoài thời gian; chọn đúng thì sang câu sau. Lựa chọn xáo cố định theo seed.
    /// </summary>
    public sealed class FillLogic
    {
        public const string Blank = "___";

        public sealed class Item
        {
            /// <summary>Câu tiếng Pháp có Blank.</summary>
            public string Prompt;
            /// <summary>Nghĩa tiếng Việt cả câu.</summary>
            public string Translation;
            /// <summary>Chữ hiện trên các nút lựa chọn.</summary>
            public string[] Options = new string[0];
            /// <summary>Chỉ số đáp án trong Options (trước khi xáo).</summary>
            public int Answer;
        }

        readonly List<Item> _items;
        readonly List<int[]> _order = new List<int[]>();
        readonly List<bool[]> _wrong = new List<bool[]>();

        public FillLogic(IList<Item> items, int seed = 7)
        {
            _items = new List<Item>(items);
            var rng = new System.Random(seed);
            foreach (var it in _items)
            {
                var order = new int[it.Options.Length];
                for (int i = 0; i < order.Length; i++)
                    order[i] = i;
                for (int i = order.Length - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }
                // Đáp án không luôn đứng đầu.
                if (order.Length > 1 && order[0] == it.Answer)
                    (order[0], order[1]) = (order[1], order[0]);
                _order.Add(order);
                _wrong.Add(new bool[it.Options.Length]);
            }
        }

        public int Count => _items.Count;
        public int Index { get; private set; }
        public bool Done => Index >= Count;
        public Item Current => Done ? null : _items[Index];
        public int WrongAttempts { get; private set; }

        /// <summary>Thứ tự hiện các lựa chọn của câu đang làm (chỉ số trong Item.Options).</summary>
        public int[] Order => Done ? new int[0] : _order[Index];

        public bool WasWrong(int option) => !Done && option >= 0 && option < _wrong[Index].Length && _wrong[Index][option];

        /// <summary>option: chỉ số trong Item.Options. Đúng thì sang câu sau.</summary>
        public bool TryAnswer(int option)
        {
            if (Done || option < 0 || option >= _items[Index].Options.Length)
                return false;
            if (option != _items[Index].Answer)
            {
                if (!_wrong[Index][option])
                    WrongAttempts++;
                _wrong[Index][option] = true;
                return false;
            }
            Index++;
            return true;
        }

        /// <summary>Câu đã điền (đáp án thay chỗ trống).</summary>
        public static string Filled(Item item)
        {
            if (item == null)
                return string.Empty;
            return item.Prompt.Replace(Blank, item.Options[item.Answer]);
        }

        /// <summary>Sim / test.</summary>
        public void SimulateAnswer()
        {
            if (!Done)
                TryAnswer(_items[Index].Answer);
        }
    }
}
