using System;
using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>Bản đọc tổng hợp của các ô; không lưu bản sao nhiên liệu, nhiệt hay tro.</summary>
    public sealed class FireLogic
    {
        public const float MaxFuel = FireCellState.MaxFuel;
        public const float FullFuel = FireCellState.FullFuel;
        readonly Func<FireCellState[]> _cells;
        readonly Func<int> _fullCells;

        public FireLogic() : this(null, null) { }
        public FireLogic(Func<FireCellState[]> cells, Func<int> fullCells)
        {
            var single = new[] { new FireCellState() };
            _cells = cells ?? (() => single);
            _fullCells = fullCells ?? (() => 1);
        }

        public float BurnPerSecond
        {
            get => _cells()[0].BurnPerSecond;
            set { foreach (var c in _cells()) c.BurnPerSecond = Mathf.Max(0f, value); }
        }
        public float AshPerFuel
        {
            get => _cells()[0].AshPerFuel;
            set { foreach (var c in _cells()) c.AshPerFuel = Mathf.Max(0f, value); }
        }
        public float Response
        {
            get => _cells()[0].Response;
            set { foreach (var c in _cells()) c.Response = Mathf.Max(0f, value); }
        }
        public float Fuel
        {
            get { var cells = _cells(); float n = 0f; foreach (var c in cells) n += c.Fuel; return n / cells.Length; }
        }
        public float Level
        {
            get { float n = 0f; foreach (var c in _cells()) n += c.Heat; return Mathf.Clamp01(n / Mathf.Max(1, _fullCells())); }
        }
        /// <summary>Ô nghẹt nhất cần được cào; lấy trung bình sẽ che một ô đầy tro.</summary>
        public float Ash
        {
            get { float n = 0f; foreach (var c in _cells()) n = Mathf.Max(n, c.Ash); return n; }
        }
        public float Target
        {
            get { float n = 0f; foreach (var c in _cells()) n += c.Target; return Mathf.Clamp01(n / Mathf.Max(1, _fullCells())); }
        }
        public void AddCoal(float amount = 1f)
        {
            var cells = _cells();
            var lowest = cells[0];
            foreach (var c in cells) if (c.Fuel < lowest.Fuel) lowest = c;
            lowest.AddCoal(amount);
        }
        public void Rake(float amount)
        {
            foreach (var c in _cells())
            {
                amount -= c.Rake(amount);
                if (amount <= 0f) break;
            }
        }
        /// <summary>Khởi tạo đầu ngày/test; thao tác trong ca phải chỉ rõ ô.</summary>
        public void Set(float fuel, float level, float ash = 0f)
        {
            foreach (var c in _cells()) c.Set(fuel, level, ash);
        }
        public void Tick(float dt)
        {
            foreach (var c in _cells()) c.Tick(dt);
        }
    }
}
