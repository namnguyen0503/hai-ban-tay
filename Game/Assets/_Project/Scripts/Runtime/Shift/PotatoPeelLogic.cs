using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Gọt một củ khoai bằng dao, kéo dao về phía ngón cái: mỗi nhát lột một dải vỏ dọc củ từ đầu xa về đầu gần, rồi xoay củ
    /// trong tay để dải kế tiếp ngửa lên. Đủ số dải quanh củ thì củ trắng. Rê dọc càng xa thì dao đi càng xa (không theo
    /// số khung hình); đang xoay củ thì không nhận rê.
    /// </summary>
    public sealed class PotatoPeelLogic
    {
        /// <summary>Số dải vỏ quanh một củ (mỗi nhát một dải).</summary>
        public const int Bands = 6;

        /// <summary>Dải đang gọt (0..Bands-1); bằng Bands khi củ đã trắng.</summary>
        public int Band { get; private set; }

        /// <summary>Nhát đang kéo, 0 = dao ở đầu xa, 1 = dao tới đầu gần.</summary>
        public float Stroke { get; private set; }

        /// <summary>Đã hết nhát, đang xoay củ sang dải kế.</summary>
        public bool Turning { get; private set; }

        public bool Done => Band >= Bands;

        /// <summary>Tiến độ cả củ 0–1.</summary>
        public float Progress => Mathf.Clamp01((Band + Stroke) / Bands);

        /// <summary>Cộng tiến độ (phần của cả củ, xem PeelStation.StrokeProgress). Trả về true khi vừa hết một nhát.</summary>
        public bool Add(float potatoFraction)
        {
            if (Done || Turning || potatoFraction <= 0f)
                return false;
            Stroke = Mathf.Min(1f, Stroke + potatoFraction * Bands);
            if (Stroke < 1f)
                return false;
            Turning = true;
            return true;
        }

        /// <summary>Xoay củ xong: sang dải kế.</summary>
        public void NextBand()
        {
            if (!Turning)
                return;
            Turning = false;
            Band++;
            Stroke = 0f;
        }

        public void Reset()
        {
            Band = 0;
            Stroke = 0f;
            Turning = false;
        }
    }
}
