using UnityEngine;

namespace HaiBanTay.Shift
{
    public enum HaulEvent
    {
        None,
        Moved,
        /// <summary>Rổ tới cuối đường (trong bếp).</summary>
        Arrived
    }

    /// <summary>
    /// Kéo rổ rau bằng dây sắt (F05b): biển động, không vác rổ lên vai được nên buộc rổ vào dây sắt rồi kéo đi giữa hầm và
    /// bếp. Rổ chạy dọc một đường cố định (sàn hầm → thang 14 bậc → boong giếng trước → cửa bếp); vị trí rổ là quãng
    /// Basket (m) tính từ đầu đường. Giữ chuột trái khi đứng gần đường và đi trước rổ thì dây căng, rổ theo sau cách một
    /// đoạn dây. Buông dây khi rổ đang trên thang thì rổ tụt dần về chân thang; sóng có thể đẩy rổ lùi (Shove).
    /// </summary>
    public sealed class HaulLogic
    {
        /// <summary>Độ dài dây giữa tay và rổ (m, theo đường).</summary>
        public float Rope = 1.6f;
        /// <summary>Người chơi cách đường quá mức này thì không kéo được (m).</summary>
        public float Reach = 1.6f;
        /// <summary>Rổ nặng: chạy nhanh nhất chừng này (m/s).</summary>
        public float MaxSpeed = 1.5f;
        /// <summary>Buông dây khi rổ trên thang: tụt về chân thang (m/s).</summary>
        public float SlideBack = 0.45f;
        /// <summary>Người chơi đứng cách cuối đường chừng này (m) là thu dây kéo rổ về tới nơi.</summary>
        public float EndReach = 0.25f;
        /// <summary>Đoạn đường dốc hơn tỉ lệ này (cao/ngang) là thang.</summary>
        public float StairSlope = 0.3f;

        Vector3[] _path = new Vector3[0];
        float[] _at = new float[0];

        public HaulLogic(Vector3[] path)
        {
            SetPath(path);
        }

        public float Length { get; private set; }
        /// <summary>Vị trí rổ trên đường (m).</summary>
        public float Basket { get; private set; }
        public bool Tied { get; private set; }
        /// <summary>Đang giữ dây căng (khung gần nhất).</summary>
        public bool Holding { get; private set; }
        public bool Arrived => Basket >= Length - 0.02f;
        public int PointCount => _path.Length;

        public void SetPath(Vector3[] path)
        {
            _path = path ?? new Vector3[0];
            _at = new float[_path.Length];
            float sum = 0f;
            for (int i = 1; i < _path.Length; i++)
            {
                sum += Vector3.Distance(_path[i - 1], _path[i]);
                _at[i] = sum;
            }
            Length = sum;
            Basket = Mathf.Clamp(Basket, 0f, Length);
        }

        /// <summary>Quãng của điểm thứ i trên đường.</summary>
        public float DistanceAt(int index) => _at.Length == 0 ? 0f : _at[Mathf.Clamp(index, 0, _at.Length - 1)];

        public void Reset()
        {
            Basket = 0f;
            Tied = false;
            Holding = false;
        }

        public bool Tie()
        {
            if (Tied || Arrived)
                return false;
            Tied = true;
            return true;
        }

        /// <summary>Điểm trên đường ở quãng s.</summary>
        public Vector3 PointAt(float s)
        {
            if (_path.Length == 0)
                return Vector3.zero;
            if (s <= 0f)
                return _path[0];
            for (int i = 1; i < _path.Length; i++)
            {
                if (s <= _at[i])
                {
                    float seg = _at[i] - _at[i - 1];
                    return seg > 1e-5f ? Vector3.Lerp(_path[i - 1], _path[i], (s - _at[i - 1]) / seg) : _path[i];
                }
            }
            return _path[_path.Length - 1];
        }

        /// <summary>Hướng đi của đường ở quãng s (chuẩn hóa).</summary>
        public Vector3 DirectionAt(float s)
        {
            int i = SegmentAt(s);
            if (i < 1)
                return Vector3.forward;
            var d = _path[i] - _path[i - 1];
            return d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward;
        }

        int SegmentAt(float s)
        {
            for (int i = 1; i < _path.Length; i++)
                if (s <= _at[i])
                    return i;
            return _path.Length - 1;
        }

        /// <summary>Đoạn đường ở quãng s có phải thang không.</summary>
        public bool OnStairs(float s)
        {
            int i = SegmentAt(s);
            if (i < 1)
                return false;
            var d = _path[i] - _path[i - 1];
            float flat = new Vector2(d.x, d.z).magnitude;
            return flat > 1e-4f && Mathf.Abs(d.y) / flat > StairSlope;
        }

        /// <summary>Quãng của điểm gần p nhất trên đường; lateral: khoảng cách từ p tới đường.</summary>
        public float Project(Vector3 p, out float lateral)
        {
            lateral = float.PositiveInfinity;
            float best = 0f;
            for (int i = 1; i < _path.Length; i++)
            {
                var a = _path[i - 1];
                var ab = _path[i] - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2) : 0f;
                float d = Vector3.Distance(a + ab * t, p);
                if (d < lateral)
                {
                    lateral = d;
                    best = _at[i - 1] + Mathf.Sqrt(len2) * t;
                }
            }
            return best;
        }

        /// <summary>Một khung: player là vị trí chân người chơi; pull: đang giữ chuột trái và tay rảnh.</summary>
        public HaulEvent Tick(Vector3 player, bool pull, float dt)
        {
            if (!Tied || Arrived || dt <= 0f)
            {
                Holding = false;
                return HaulEvent.None;
            }
            float s = Project(player, out float lateral);
            // Chỉ cầm được dây khi đứng gần đường, phía trước rổ, trong tầm dây.
            Holding = pull && lateral <= Reach && s >= Basket - 0.3f && s - Basket <= Rope + Reach;
            float before = Basket;
            if (Holding)
            {
                // Tới cuối đường (trong bếp) thì thu dây tay qua tay: rổ về hẳn tới nơi.
                float want = s >= Length - EndReach ? Length : Mathf.Min(s - Rope, Length);
                if (want > Basket)
                    Basket = Mathf.Min(want, Basket + MaxSpeed * dt);
            }
            else if (OnStairs(Basket))
            {
                // Rổ tụt về chân thang (đầu đoạn thang), không lùi quá.
                int i = SegmentAt(Basket);
                Basket = Mathf.Max(_at[i - 1], Basket - SlideBack * dt);
            }
            if (Arrived)
                return HaulEvent.Arrived;
            return Mathf.Abs(Basket - before) > 1e-5f ? HaulEvent.Moved : HaulEvent.None;
        }

        /// <summary>Tuột tay khỏi dây (sóng đánh, người chơi bị cuốn đi).</summary>
        public void Release()
        {
            Holding = false;
        }

        /// <summary>Sóng xô rổ lùi một đoạn (khi không giữ dây).</summary>
        public void Shove(float metres)
        {
            if (!Holding)
                Basket = Mathf.Max(0f, Basket - Mathf.Max(0f, metres));
        }

        /// <summary>Sim / test.</summary>
        public void Finish()
        {
            Tied = true;
            Basket = Length;
        }
    }
}
