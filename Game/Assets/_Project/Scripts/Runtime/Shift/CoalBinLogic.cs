namespace HaiBanTay.Shift
{
    /// <summary>Thùng than cạnh lò chứa một số xẻng; cạn thì phải mang sọt than từ hầm than lên đổ vào.</summary>
    public sealed class CoalBinLogic
    {
        public int Capacity = 10;
        public int Stock { get; private set; }
        public bool Empty => Stock <= 0;

        public CoalBinLogic(int capacity = 10, int stock = -1)
        {
            Capacity = capacity;
            Stock = stock < 0 ? capacity : System.Math.Min(stock, capacity);
        }

        public bool Take()
        {
            if (Stock <= 0)
                return false;
            Stock--;
            return true;
        }

        public void Refill()
        {
            Stock = Capacity;
        }

        public void Return() => Stock = System.Math.Min(Capacity, Stock + 1);

        public void Set(int stock)
        {
            Stock = System.Math.Max(0, System.Math.Min(stock, Capacity));
        }
    }
}
