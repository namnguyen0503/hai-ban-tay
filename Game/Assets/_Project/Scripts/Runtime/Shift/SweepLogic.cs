using HaiBanTay.Tasks;
using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Dọn bếp (F04): những vệt bẩn trên sàn từ ca trước. Cầm chổi, nhìn vào vệt bẩn, giữ chuột trái và rê chuột để quét
    /// (như cọ nồi, ScrubLogic). Mỗi vệt sạch tính một đơn vị.
    /// </summary>
    public sealed class SweepLogic
    {
        public float PixelsPerSpot = 1600f;

        ScrubLogic[] _spots = new ScrubLogic[0];
        bool[] _dirty = new bool[0];

        public int Capacity => _spots.Length;
        public int Dirty { get; private set; }

        public SweepLogic(int capacity)
        {
            _spots = new ScrubLogic[Mathf.Max(0, capacity)];
            _dirty = new bool[_spots.Length];
            for (int i = 0; i < _spots.Length; i++)
                _spots[i] = new ScrubLogic { PixelsForFull = PixelsPerSpot };
        }

        public bool IsDirty(int i) => i >= 0 && i < _dirty.Length && _dirty[i];
        public float Progress(int i) => i >= 0 && i < _spots.Length ? _spots[i].Progress : 0f;

        /// <summary>Rải thêm vệt bẩn (vào chỗ đang sạch, theo thứ tự). Trả về số vệt đã rải.</summary>
        public int Scatter(int count)
        {
            int made = 0;
            for (int i = 0; i < _dirty.Length && made < count; i++)
            {
                if (_dirty[i])
                    continue;
                _dirty[i] = true;
                _spots[i].PixelsForFull = PixelsPerSpot;
                _spots[i].Reset();
                Dirty++;
                made++;
            }
            return made;
        }

        /// <summary>Quét vệt i. Trả về true khi vệt vừa sạch hẳn.</summary>
        public bool Tick(int i, bool held, Vector2 mouseDelta, float dt, float speed = 1f)
        {
            if (!IsDirty(i))
                return false;
            if (!_spots[i].Tick(held, true, mouseDelta * Mathf.Max(0.05f, speed), dt) || !_spots[i].Done)
                return false;
            _dirty[i] = false;
            Dirty--;
            return true;
        }

        /// <summary>Sim: quét sạch một vệt ngay. Trả về chỉ số vệt, -1 nếu không còn.</summary>
        public int CleanOne()
        {
            for (int i = 0; i < _dirty.Length; i++)
            {
                if (!_dirty[i])
                    continue;
                _spots[i].ForceProgress(1f);
                _dirty[i] = false;
                Dirty--;
                return i;
            }
            return -1;
        }
    }
}
