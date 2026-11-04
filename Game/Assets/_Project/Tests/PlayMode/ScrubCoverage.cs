using System;
using HaiBanTay.Player;
using HaiBanTay.Tasks;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Rê chuột phủ kín mặt cọ như người chà từng luống (bot và test, brush17b): con trỏ ẩn đi theo các hàng ngang cách nhau chưa tới
    /// một vết chải, hết hàng (hoặc vướng tầm tay) thì sang hàng kế, hết mặt thì quét ngược lại; trên nền đó đưa qua lại nhanh dọc hàng
    /// để cọ hết sức. Vòng kín theo con trỏ thật (BrushScrub.Motion.Cursor), đổi hướng mặt cọ sang chuột theo camera như BrushScrub.Tick.
    /// Rê mù kiểu "qua lại + trôi dọc" chỉ quét một dải giữa (nồi cao: 30% sau 2 phút, không bao giờ tới mốc sóng 40%).
    /// </summary>
    public sealed class ScrubCoverage
    {
        readonly Func<BrushScrub> _scrub;
        BrushScrub _for;
        int _row, _rowStep = 1, _rows, _frame, _stuckFrames;
        float _dir = 1f, _best;
        /// <summary>Đang chải một vòng theo mép tầm với (sau mỗi lượt quét hết các hàng); góc hiện tại và góc đã đi (rad).</summary>
        bool _lap;
        float _lapAngle, _lapDone;

        /// <summary>Tốc độ con trỏ dọc hàng (m/s) và biên độ đưa qua lại (điểm ảnh mỗi khung, đổi chiều mỗi 8 khung).</summary>
        public float AlongSpeed = 0.3f, Shake = 10f;

        public ScrubCoverage(Func<BrushScrub> scrub) => _scrub = scrub;

        public static Func<Vector2> For(Func<BrushScrub> scrub) => new ScrubCoverage(scrub).Mouse;

        float Spacing(BrushScrub s) => Mathf.Max(0.02f, s.FootprintRadius * 1.3f);

        void Begin(BrushScrub s)
        {
            _for = s;
            float r = s.Surface.Reachable;
            _rows = Mathf.Max(1, Mathf.CeilToInt(2f * r / Spacing(s)));
            // Bắt đầu ở hàng gần con trỏ nhất.
            _row = Mathf.Clamp(Mathf.FloorToInt((s.Motion.Cursor.y + r) / Spacing(s)), 0, _rows - 1);
            _rowStep = 1;
            _dir = s.Motion.Cursor.x > 0f ? -1f : 1f;
            NewRow(s);
        }

        void NewRow(BrushScrub s)
        {
            _stuckFrames = 0;
            _best = float.MaxValue;
        }

        float RowY(BrushScrub s) => -s.Surface.Reachable + Spacing(s) * (_row + 0.5f);

        void NextRow(BrushScrub s)
        {
            _dir = -_dir;
            int next = _row + _rowStep;
            if (next < 0 || next >= _rows)
            {
                _rowStep = -_rowStep; next = _row + _rowStep;
                // Hết một lượt hàng: hàng ngang chỉ chạm mép ở hai đầu, dải sát vành (ngoài tầm tâm đầu chải, chỉ mép vết chải với tới) còn
                // bẩn; chải một vòng quanh mép như người thật rồi quét hàng tiếp (bot 22/10: chảo kẹt ở 99,7%).
                _lap = true;
                _lapDone = 0f;
                var c = s.Motion.Cursor;
                _lapAngle = Mathf.Atan2(c.y, c.x);
            }
            _row = Mathf.Clamp(next, 0, _rows - 1);
            NewRow(s);
        }

        public Vector2 Mouse()
        {
            var s = _scrub();
            var rig = PlayerRig.Current;
            _frame++;
            float shake = (_frame / 8) % 2 == 0 ? Shake : -Shake;
            if (s == null || rig == null)
                return new Vector2(shake, 0f);
            if (s != _for) Begin(s);
            var c = s.Motion.Cursor;
            if (_lap)
                return LapMouse(s, rig, c, shake);
            float r = s.Surface.Reachable, y = RowY(s);
            float end = _dir * Mathf.Sqrt(Mathf.Max(0f, r * r - y * y)) * 0.97f;
            float left = (end - c.x) * _dir;
            float dist = Mathf.Abs(left) + Mathf.Abs(y - c.y);
            // Tới cuối hàng, hay 0,6 s không lại gần thêm (mép tầm tay, vành chặn) thì sang hàng kế.
            if (dist < _best - 0.004f) { _best = dist; _stuckFrames = 0; } else _stuckFrames++;
            if (left < 0.02f || _stuckFrames > 36) { NextRow(s); y = RowY(s); end = _dir * Mathf.Sqrt(Mathf.Max(0f, r * r - y * y)) * 0.97f; }
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            // Vận tốc muốn (m/khung, hệ mặt): dọc hàng đều, ngang hàng kéo về hàng.
            var want = new Vector2(_dir * AlongSpeed * dt, Mathf.Clamp((y - c.y) * 0.25f, -AlongSpeed * dt, AlongSpeed * dt));
            return ToMouse(s, rig, want, shake);
        }

        /// <summary>Một vòng theo mép tầm với (bán kính 0,97 × Reachable), con trỏ bám điểm chạy trên vòng.</summary>
        Vector2 LapMouse(BrushScrub s, PlayerRig rig, Vector2 c, float shake)
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float r = s.Surface.Reachable * 0.97f;
            var target = new Vector2(Mathf.Cos(_lapAngle), Mathf.Sin(_lapAngle)) * r;
            // Điểm trên vòng chỉ chạy tiếp khi con trỏ đã bám sát (mép tầm tay giữ con trỏ lại thì chờ, không bỏ qua cung đó).
            if (Vector2.Distance(c, target) < 0.03f)
            {
                _stuckFrames = 0;
                float step = AlongSpeed * dt / Mathf.Max(0.05f, r);
                _lapAngle += step;
                _lapDone += step;
                if (_lapDone >= 2f * Mathf.PI) _lap = false;
            }
            else if ((_stuckFrames += 1) > 60) { _lapAngle += 0.3f; _lapDone += 0.3f; _stuckFrames = 0; if (_lapDone >= 2f * Mathf.PI) _lap = false; }
            var want = Vector2.ClampMagnitude((target - c) * 0.5f, AlongSpeed * dt);
            return ToMouse(s, rig, want, shake * 0.5f);
        }

        static Vector2 ToMouse(BrushScrub s, PlayerRig rig, Vector2 want, float shake)
        {
            float mpp = Mathf.Max(1e-5f, s.Motion.MetresPerPixel);
            var cam = rig.cam.transform;
            var right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
            var forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            var world = s.Vessel.TransformDirection(new Vector3(want.x, 0f, want.y));
            var shakeWorld = s.Vessel.TransformDirection(new Vector3(1f, 0f, 0f));
            var mouse = new Vector2(Vector3.Dot(world, right), Vector3.Dot(world, forward)) / mpp;
            mouse += new Vector2(Vector3.Dot(shakeWorld, right), Vector3.Dot(shakeWorld, forward)).normalized * shake;
            return mouse;
        }
    }
}
