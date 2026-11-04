using System.Collections.Generic;
using UnityEngine;

namespace HaiBanTay.Tasks
{
    public enum TraceEvent
    {
        None,
        /// <summary>Chữ hiện tại đã tô đủ, sang chữ kế tiếp.</summary>
        LetterDone,
        /// <summary>Tô xong mọi chữ.</summary>
        AllDone
    }

    /// <summary>
    /// Tập viết (F07a): tô theo nét mờ bằng chuột, mỗi lượt một từ (QA-0140; trước đây từng chữ cái). Mỗi lượt là vài nét
    /// (dãy điểm theo đơn vị chiều cao chữ: x trong 0–Aspect, y trong 0–1, y hướng xuống);
    /// mỗi nét được rải điểm mẫu đều nhau. Điểm mẫu nằm trong bán kính dung sai của nét mực là đã phủ. Mọi nét đều phủ đủ
    /// tỉ lệ Coverage thì chữ xong. Không bắt đúng thứ tự hay chiều nét; mực lệch ra ngoài không bị phạt (chỉ đếm).
    /// </summary>
    public sealed class TraceLogic
    {
        /// <summary>Bán kính dung sai (theo ô chữ 0–1).</summary>
        public float Tolerance = 0.09f;
        /// <summary>Tỉ lệ điểm mẫu mỗi nét phải phủ.</summary>
        public float Coverage = 0.85f;
        /// <summary>Khoảng cách giữa hai điểm mẫu trên nét.</summary>
        public float SampleStep = 0.035f;

        readonly List<string> _letters = new List<string>();
        readonly List<List<Vector2[]>> _glyphs = new List<List<Vector2[]>>();
        readonly List<float> _aspects = new List<float>();
        readonly List<List<Vector2>> _ink = new List<List<Vector2>>();
        readonly List<Vector2[]> _samples = new List<Vector2[]>();
        readonly List<bool[]> _covered = new List<bool[]>();
        bool _drawing;

        /// <summary>letters: từ (hoặc chữ) theo thứ tự tô; glyphs[i]: các nét của letters[i]; aspects[i]: bề ngang ô của
        /// letters[i] theo đơn vị chiều cao (bỏ trống = ô vuông).</summary>
        public TraceLogic(IList<string> letters, IList<List<Vector2[]>> glyphs, IList<float> aspects = null)
        {
            for (int i = 0; i < letters.Count && i < glyphs.Count; i++)
            {
                _letters.Add(letters[i]);
                _glyphs.Add(glyphs[i]);
                _aspects.Add(aspects != null && i < aspects.Count ? Mathf.Max(0.1f, aspects[i]) : 1f);
            }
            Prepare();
        }

        public int Count => _letters.Count;
        public int Index { get; private set; }
        public bool Done => Index >= Count;
        public string Current => Done ? null : _letters[Index];
        /// <summary>Bề ngang ô của lượt đang tô theo chiều cao (ô vẽ rộng = cao × Aspect).</summary>
        public float Aspect => Done ? 1f : _aspects[Index];
        /// <summary>Các nét mờ của chữ đang tô.</summary>
        public IReadOnlyList<Vector2[]> Guide => Done ? (IReadOnlyList<Vector2[]>)new Vector2[0][] : _glyphs[Index];
        /// <summary>Các nét mực người chơi đã vẽ cho chữ đang tô.</summary>
        public IReadOnlyList<List<Vector2>> Ink => _ink;
        /// <summary>Số điểm mực nằm xa mọi nét (chỉ để thống kê, không phạt).</summary>
        public int StrayPoints { get; private set; }
        public int LettersDone => Index;

        /// <summary>Tiến độ chữ đang tô 0–1 (nét kém nhất quyết định).</summary>
        public float Progress
        {
            get
            {
                if (Done)
                    return 1f;
                float worst = 1f;
                foreach (var c in _covered)
                    worst = Mathf.Min(worst, Ratio(c) / Mathf.Max(0.01f, Coverage));
                return Mathf.Clamp01(worst);
            }
        }

        static float Ratio(bool[] covered)
        {
            if (covered.Length == 0)
                return 1f;
            int n = 0;
            foreach (bool b in covered)
                if (b)
                    n++;
            return (float)n / covered.Length;
        }

        void Prepare()
        {
            _ink.Clear();
            _samples.Clear();
            _covered.Clear();
            _drawing = false;
            if (Done)
                return;
            foreach (var stroke in _glyphs[Index])
            {
                var pts = Sample(stroke, SampleStep);
                _samples.Add(pts);
                _covered.Add(new bool[pts.Length]);
            }
        }

        /// <summary>Rải điểm đều trên một nét (gồm cả hai đầu).</summary>
        public static Vector2[] Sample(Vector2[] stroke, float step)
        {
            var list = new List<Vector2>();
            if (stroke == null || stroke.Length == 0)
                return list.ToArray();
            list.Add(stroke[0]);
            float carry = 0f;
            for (int i = 1; i < stroke.Length; i++)
            {
                var a = stroke[i - 1];
                var b = stroke[i];
                float len = Vector2.Distance(a, b);
                float t = step - carry;
                while (t <= len)
                {
                    list.Add(Vector2.Lerp(a, b, t / len));
                    t += step;
                }
                carry = len - (t - step);
            }
            if (list.Count == 0 || Vector2.Distance(list[list.Count - 1], stroke[stroke.Length - 1]) > step * 0.25f)
                list.Add(stroke[stroke.Length - 1]);
            return list.ToArray();
        }

        public void BeginStroke(Vector2 p)
        {
            if (Done)
                return;
            _drawing = true;
            _ink.Add(new List<Vector2> { p });
            Cover(p, p);
        }

        /// <summary>Thêm điểm vào nét đang vẽ (ô chữ 0–1). Trả về sự kiện khi chữ xong.</summary>
        public TraceEvent AddPoint(Vector2 p)
        {
            if (Done)
                return TraceEvent.None;
            if (!_drawing)
            {
                BeginStroke(p);
                return Check();
            }
            var stroke = _ink[_ink.Count - 1];
            var last = stroke[stroke.Count - 1];
            if ((p - last).sqrMagnitude < 1e-6f)
                return TraceEvent.None;
            stroke.Add(p);
            Cover(last, p);
            return Check();
        }

        public void EndStroke()
        {
            _drawing = false;
        }

        /// <summary>Xóa mực của chữ đang tô để tô lại.</summary>
        public void ClearInk()
        {
            Prepare();
        }

        /// <summary>Sim / test: phủ hết chữ đang tô.</summary>
        public TraceEvent SimulateLetter()
        {
            if (Done)
                return TraceEvent.None;
            // Nét cuối có thể đủ độ phủ trước khi hết điểm: dừng ngay khi lượt này xong, không vẽ tràn sang từ sau.
            foreach (var stroke in _glyphs[Index])
            {
                BeginStroke(stroke[0]);
                for (int i = 1; i < stroke.Length; i++)
                {
                    var ev = AddPoint(stroke[i]);
                    if (ev != TraceEvent.None)
                    {
                        EndStroke();
                        return ev;
                    }
                }
                EndStroke();
            }
            return Check();
        }

        void Cover(Vector2 a, Vector2 b)
        {
            bool near = false;
            float r2 = Tolerance * Tolerance;
            for (int s = 0; s < _samples.Count; s++)
            {
                var pts = _samples[s];
                var cov = _covered[s];
                for (int i = 0; i < pts.Length; i++)
                {
                    float d2 = SegmentDistanceSq(pts[i], a, b);
                    if (d2 <= r2)
                    {
                        cov[i] = true;
                        near = true;
                    }
                    else if (d2 <= r2 * 6.25f)
                        near = true;
                }
            }
            if (!near)
                StrayPoints++;
        }

        TraceEvent Check()
        {
            foreach (var c in _covered)
                if (Ratio(c) < Coverage)
                    return TraceEvent.None;
            Index++;
            Prepare();
            return Done ? TraceEvent.AllDone : TraceEvent.LetterDone;
        }

        public static float SegmentDistanceSq(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            return (a + ab * t - p).sqrMagnitude;
        }
    }
}
