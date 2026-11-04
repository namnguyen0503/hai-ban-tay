namespace HaiBanTay.Shift
{
    public enum SortEvent
    {
        None,
        Kept,
        Discarded,
        Wrong,
        BunchDone
    }

    /// <summary>
    /// Nhặt rau (F03, F05): lấy một bó từ sọt ra thớt; từng lá một, giữ chuột trái và gạt chuột sang phải để bỏ lá tốt vào
    /// rổ, sang trái để bỏ lá úa vào thùng. Gạt nhầm thì lá nằm lại, không mất gì ngoài thời gian.
    /// </summary>
    public sealed class SortLogic
    {
        public float FlickPixels = 110f;
        public int LeavesPerBunch = 5;

        readonly System.Random _rng;
        bool[] _wilted = new bool[0];
        float _dx;
        bool _flicked;

        public int Pending { get; private set; }
        public bool HasBunch { get; private set; }
        public int Index { get; private set; }
        public int Sorted { get; private set; }
        public int Mistakes { get; private set; }
        public int Bunches { get; private set; }
        public bool CurrentWilted => HasBunch && Index < _wilted.Length && _wilted[Index];
        public bool LeafWilted(int i) => i >= 0 && i < _wilted.Length && _wilted[i];
        /// <summary>Độ gạt hiện tại, -1…1 (âm = sang trái).</summary>
        public float Lean => FlickPixels > 0f ? UnityEngine.Mathf.Clamp(_dx / FlickPixels, -1f, 1f) : 0f;

        public SortLogic(int seed = 1911)
        {
            _rng = new System.Random(seed);
        }

        public void AddBunches(int count)
        {
            if (count > 0)
                Pending += count;
        }

        /// <summary>Lấy một bó ra thớt. Mỗi bó có ít nhất một lá úa và một lá tốt.</summary>
        public bool TakeBunch()
        {
            if (Pending <= 0 || HasBunch)
                return false;
            Pending--;
            HasBunch = true;
            Index = 0;
            _dx = 0f;
            _wilted = new bool[UnityEngine.Mathf.Max(2, LeavesPerBunch)];
            for (int i = 0; i < _wilted.Length; i++)
                _wilted[i] = _rng.NextDouble() < 0.4;
            _wilted[_rng.Next(_wilted.Length)] = true;
            int good = _rng.Next(_wilted.Length);
            if (_wilted[good])
            {
                bool anyGood = false;
                foreach (var w in _wilted)
                    anyGood |= !w;
                if (!anyGood)
                    _wilted[good] = false;
            }
            return true;
        }

        /// <summary>mouseDx: độ rê chuột ngang khung này.</summary>
        public SortEvent Tick(bool held, bool atBoard, float mouseDx)
        {
            if (!HasBunch || !held || !atBoard)
            {
                _dx = 0f;
                _flicked = false;
                return SortEvent.None;
            }
            if (_flicked)
                return SortEvent.None;
            _dx += mouseDx;
            if (UnityEngine.Mathf.Abs(_dx) < FlickPixels)
                return SortEvent.None;
            bool keep = _dx > 0f;
            _dx = 0f;
            // Mỗi lần giữ chuột chỉ gạt một lá: nhả ra rồi giữ lại để gạt lá sau.
            _flicked = true;
            if (keep == _wilted[Index])
            {
                Mistakes++;
                return SortEvent.Wrong;
            }
            Index++;
            Sorted++;
            if (Index < _wilted.Length)
                return keep ? SortEvent.Kept : SortEvent.Discarded;
            HasBunch = false;
            Bunches++;
            return SortEvent.BunchDone;
        }

        /// <summary>Người giúp (hai lính, F05) nhặt hẳn một bó còn trong sọt, không đụng bó đang trên thớt.</summary>
        public bool HelperBunch()
        {
            if (Pending <= 0)
                return false;
            Pending--;
            Bunches++;
            return true;
        }

        /// <summary>Sim: xong cả một bó ngay (lấy bó nếu chưa có).</summary>
        public bool CompleteBunch()
        {
            if (!HasBunch && !TakeBunch())
                return false;
            Sorted += _wilted.Length - Index;
            Index = _wilted.Length;
            HasBunch = false;
            Bunches++;
            return true;
        }
    }
}
