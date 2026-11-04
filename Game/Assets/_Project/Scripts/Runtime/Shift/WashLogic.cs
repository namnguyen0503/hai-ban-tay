using HaiBanTay.Tasks;
using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>Rửa nồi: nồi bẩn dồn ở chồng (Pending) → lấy một chiếc vào chậu → cọ sạch → úp lên giá.</summary>
    public sealed class WashLogic
    {
        public readonly ScrubLogic Scrub = new ScrubLogic { PixelsForFull = 3200f };

        public int Pending { get; private set; }
        public bool InBasin { get; private set; }
        public int Washed { get; private set; }
        public bool BasinClean => InBasin && Scrub.Done;

        public void AddDirty(int count)
        {
            Pending += Mathf.Max(0, count);
        }

        /// <summary>Nồi bẩn được lấy khỏi chồng mà không qua chậu (phụ bếp khác cọ vội khi thiếu nồi sạch).</summary>
        public void RemoveDirty(int count)
        {
            Pending = Mathf.Max(0, Pending - Mathf.Max(0, count));
        }

        public bool TakeToBasin()
        {
            if (Pending <= 0 || InBasin)
                return false;
            Pending--;
            InBasin = true;
            Scrub.Reset();
            return true;
        }

        /// <summary>Trả về true nếu khung này có tiến triển.</summary>
        public bool Tick(bool held, bool onBasin, Vector2 mouseDelta, float dt)
        {
            return InBasin && !Scrub.Done && Scrub.Tick(held, onBasin, mouseDelta, dt);
        }

        public bool Rack()
        {
            if (!BasinClean)
                return false;
            InBasin = false;
            Washed++;
            return true;
        }

        /// <summary>Sim / phím demo: rửa xong một chiếc ngay.</summary>
        public bool CompleteOne()
        {
            if (!InBasin && !TakeToBasin())
                return false;
            Scrub.ForceProgress(1f);
            return Rack();
        }
    }
}
