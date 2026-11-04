using UnityEngine;

namespace HaiBanTay.Tasks
{
    /// <summary>
    /// Mặt nạ bẩn của một bề mặt cần cọ (đáy nồi, lòng chảo, thành nồi cao): lưới size × size ô, mỗi ô từ 0 (sạch) tới 1 (bẩn). Đầu
    /// bàn chải quét qua ô nào thì ô đó sạch dần, nên vết bẩn chỉ mất ở chỗ bàn chải đã đi (lớp phủ cũ giảm đều _Cutoff trên cả
    /// mặt nồi). Mặt nạ không biết gì về Unity: bề mặt đổi tọa độ thế giới sang uv (0–1), ô ngoài vùng hợp lệ (mặt tròn của nồi)
    /// không tính vào độ phủ.
    /// </summary>
    public sealed class DirtMask
    {
        readonly float[] _cells;
        readonly bool[] _valid;
        float _sum;
        int _count;

        public int Size { get; }
        /// <summary>Phần bẩn còn lại, 0–1 (trung bình trên vùng hợp lệ).</summary>
        public float Remaining => _count == 0 ? 0f : _sum / _count;
        /// <summary>Phần đã sạch, 0–1.</summary>
        public float Cleaned => 1f - Remaining;

        /// <param name="size">Cạnh lưới (ô).</param>
        /// <param name="disc">Chỉ tính các ô nằm trong đĩa nội tiếp (mặt tròn); false = cả hình vuông (dải thành nồi).</param>
        public DirtMask(int size = 64, bool disc = true)
        {
            Size = Mathf.Max(4, size);
            _cells = new float[Size * Size];
            _valid = new bool[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float dx = (x + 0.5f) / Size - 0.5f, dy = (y + 0.5f) / Size - 0.5f;
                    bool inside = !disc || dx * dx + dy * dy <= 0.25f;
                    _valid[y * Size + x] = inside;
                    if (inside) _count++;
                }
        }

        public float this[int x, int y] => _cells[y * Size + x];
        public bool IsValid(int x, int y) => _valid[y * Size + x];

        /// <summary>Phủ bẩn kín mọi ô hợp lệ.</summary>
        public void Fill()
        {
            _sum = 0f;
            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i] = _valid[i] ? 1f : 0f;
                if (_valid[i]) _sum += 1f;
            }
        }

        /// <summary>Làm sạch hết.</summary>
        public void Clear()
        {
            System.Array.Clear(_cells, 0, _cells.Length);
            _sum = 0f;
        }

        /// <summary>
        /// Đầu bàn chải đi từ <paramref name="from"/> tới <paramref name="to"/> (uv 0–1), bán kính <paramref name="radius"/> (uv): ô nằm trên
        /// đường giữa của nhát chải mất khoảng <paramref name="passStrength"/> sau một lượt đi qua, ô gần mép vết mất ít hơn (lõi mạnh, mép
        /// nhẹ). Nhát dài chia thành các chạm cách nhau nửa bán kính, sức mỗi chạm tỉ lệ quãng đi: chải nhanh hay chậm, một đoạn dài hay
        /// nhiều đoạn ngắn đều sạch như nhau trên cùng quãng đường; đứng yên thì không sạch thêm. Trả về lượng bẩn đã lấy đi (đơn vị ô).
        /// </summary>
        public float Scrub(Vector2 from, Vector2 to, float radius, float passStrength)
        {
            float length = Vector2.Distance(from, to);
            if (radius <= 0f || passStrength <= 0f || length <= 1e-6f)
                return 0f;
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / (radius * 0.5f)));
            float step = length / steps;
            // Một ô trên đường giữa nhận các chạm trong khoảng 2r, độ mờ mép trung bình trên dây cung ≈ 2/3.
            float per = passStrength * step / (radius * 1.333f);
            float taken = 0f;
            for (int i = 0; i < steps; i++)
                taken += Dab(Vector2.Lerp(from, to, (i + 1f) / steps), radius, per);
            return taken;
        }

        /// <summary>Như <see cref="Scrub"/> (giữ tên cũ của các trạm).</summary>
        public float ScrubPass(Vector2 from, Vector2 to, float radius, float passStrength) => Scrub(from, to, radius, passStrength);

        /// <summary>Một chạm bàn chải tại điểm uv.</summary>
        public float Dab(Vector2 uv, float radius, float strength)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt((uv.x - radius) * Size));
            int x1 = Mathf.Min(Size - 1, Mathf.CeilToInt((uv.x + radius) * Size));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((uv.y - radius) * Size));
            int y1 = Mathf.Min(Size - 1, Mathf.CeilToInt((uv.y + radius) * Size));
            float r2 = radius * radius;
            float taken = 0f;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * Size + x;
                    if (!_valid[i] || _cells[i] <= 0f)
                        continue;
                    float dx = (x + 0.5f) / Size - uv.x, dy = (y + 0.5f) / Size - uv.y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 >= r2)
                        continue;
                    float falloff = 1f - d2 / r2;
                    float cut = Mathf.Min(_cells[i], strength * falloff);
                    _cells[i] -= cut;
                    _sum -= cut;
                    taken += cut;
                }
            return taken;
        }

        /// <summary>Hạ mọi ô còn bẩn xuống một phần (mờ dần vết lấm tấm sót lại khi đã sạch gần hết).</summary>
        public void Fade(float amount)
        {
            if (amount <= 0f)
                return;
            _sum = 0f;
            for (int i = 0; i < _cells.Length; i++)
            {
                if (!_valid[i]) continue;
                _cells[i] = Mathf.Max(0f, _cells[i] - amount);
                _sum += _cells[i];
            }
        }

        /// <summary>Ghi lưới vào bộ đệm byte (R8, hàng đầu là y = 0) để đẩy lên texture.</summary>
        public void CopyTo(byte[] bytes)
        {
            for (int i = 0; i < _cells.Length; i++)
                bytes[i] = (byte)Mathf.RoundToInt(Mathf.Clamp01(_cells[i]) * 255f);
        }
    }
}
