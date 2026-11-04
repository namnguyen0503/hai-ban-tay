using System;
using UnityEngine;

namespace HaiBanTay.Tasks
{
    /// <summary>
    /// Chuyển động của đầu bàn chải khi cọ, trên một con trỏ ẩn nằm trên bề mặt cần cọ (đáy nồi, lòng chảo): chuột (hay ngón tay) dời con
    /// trỏ trên mặt, giữ chuột trái thì đầu chải áp mặt và đưa qua lại ngắn theo hướng rê, nhanh dần theo tốc độ rê; thả chuột thì nhấc
    /// đầu chải khỏi mặt. Toàn số học trên mặt phẳng của bề mặt (mét, hệ mặt), không biết Transform nào: BrushScrub đổi sang thế giới.
    /// </summary>
    public sealed class ScrubMotion
    {
        /// <summary>Số mét con trỏ đi trên mặt cho mỗi điểm ảnh chuột.</summary>
        public float MetresPerPixel = 0.001f;
        /// <summary>Số lần đưa qua đưa lại mỗi giây khi cọ hết sức.</summary>
        public float StrokeHz = 4.5f;
        /// <summary>Nửa biên độ đưa qua đưa lại khi cọ hết sức (m): hành trình mỗi nhát khoảng 6 cm.</summary>
        public float StrokeHalfAmplitude = 0.03f;
        /// <summary>Tốc độ chuột (điểm ảnh/giây) coi là cọ hết sức.</summary>
        public float FullPixelsPerSecond = 500f;
        /// <summary>Mức hoạt động lên/xuống mỗi giây (làm mượt: chuột khựng thì tay giảm dần chứ không dừng cứng).</summary>
        public float ActivityRise = 14f, ActivityFall = 6f;
        /// <summary>Độ nhấc đầu chải khi thả chuột (m) và tốc độ nhấc/hạ (m/s).</summary>
        public float LiftHeight = 0.04f, LiftSpeed = 0.35f;
        /// <summary>
        /// Hướng đưa qua lại xoay tối đa chừng này độ mỗi giây. Đổi hướng rê ngược lại mà hướng nhát lật ngay thì cả nhát đổi pha nửa vòng,
        /// đầu chải nhảy cả biên độ trong một khung (brush17b: nồi cao, nhát 5 cm, nhảy 2,9 cm/khung); xoay dần thì nhát chỉ đổi hướng êm.
        /// </summary>
        public float HeadingTurnDegreesPerSecond = 540f;

        /// <summary>Vị trí con trỏ ẩn trên mặt (m, hệ mặt).</summary>
        public Vector2 Cursor { get; private set; }
        /// <summary>Hướng rê gần đây (vectơ đơn vị, mượt), hướng đưa qua đưa lại.</summary>
        public Vector2 Heading { get; private set; } = Vector2.up;
        /// <summary>0 = đứng yên hay thả chuột, 1 = cọ hết sức.</summary>
        public float Activity { get; private set; }
        /// <summary>Độ nhấc đầu chải khỏi mặt (m): 0 áp sát, <see cref="LiftHeight"/> nhấc hẳn.</summary>
        public float Lift { get; private set; }
        /// <summary>Pha dao động đưa qua đưa lại (rad).</summary>
        public float Phase { get; private set; }
        /// <summary>Độ lệch đưa qua đưa lại cộng lên con trỏ (m).</summary>
        public Vector2 Stroke => Heading * (Mathf.Sin(Phase) * StrokeHalfAmplitude * Activity);
        /// <summary>Vị trí đầu chải trên mặt: con trỏ + đưa qua đưa lại, đã kẹp trong vùng cọ được và hạn tốc độ (chỗ bàn chải thật sự ở).</summary>
        public Vector2 Head => _lastHead;
        /// <summary>
        /// Tốc độ lớn nhất của đầu chải trên mặt (m/s); vô hạn = không hạn. Rê nhanh cộng nhát đưa nhanh có lúc vượt 2 m/s, đầu chải vụt
        /// 2–3 cm giữa hai khung 60 Hz (brush17b: nồi cao); tay thật chà nhanh nhất chừng 1 m/s.
        /// </summary>
        public float MaxHeadSpeed = float.PositiveInfinity;
        /// <summary>
        /// Gia tốc lớn nhất của đầu chải (m/s²) khi có hạn tốc độ: đầu chải kẹt ở mép tầm tay rồi thoát ra không vụt từ đứng yên lên hết tốc
        /// trong một khung. Nhát 4,5 lần/giây biên độ 5 cm cần chừng 40 m/s².
        /// </summary>
        public float MaxHeadAccel = 45f;
        Vector2 _headVelocity;
        /// <summary>Đầu chải đang áp mặt và chà (giữ chuột, đã hạ xuống).</summary>
        public bool Pressing => Lift <= 0.002f && Activity > 0.02f;
        /// <summary>Tổng quãng đầu chải đã đi trên mặt khi áp mặt (m).</summary>
        public float Travelled { get; private set; }

        Vector2 _lastHead;
        bool _started;

        static Vector2 Rotate(Vector2 v, float degrees)
        {
            float a = degrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>Dời con trỏ (vật xoay dưới con trỏ, đặt lại chỗ nhắm); <paramref name="keepActivity"/> giữ nhịp chà đang có.</summary>
        public void Reset(Vector2 cursor, bool keepActivity)
        {
            if (!keepActivity || !_started)
            {
                Reset(cursor);
                return;
            }
            // Dời đầu chải cùng con trỏ (không nhảy về đích chưa tới vì hạn tốc độ).
            _lastHead += cursor - Cursor;
            Cursor = cursor;
        }

        public void Reset(Vector2 cursor)
        {
            Cursor = cursor;
            Heading = Vector2.up;
            Activity = 0f;
            Lift = LiftHeight;
            Phase = 0f;
            Travelled = 0f;
            _lastHead = cursor;
            _headVelocity = Vector2.zero;
            _started = true;
        }

        /// <summary>
        /// Một khung. <paramref name="mouse"/>: độ dời chuột khung này (điểm ảnh); <paramref name="clamp"/>: giữ con trỏ trong vùng cọ
        /// được (mặt tròn, tầm tay). Trả về đoạn đầu chải vừa đi (từ, tới) khi áp mặt, để quét mặt nạ bẩn; chưa áp mặt thì độ dài 0.
        /// </summary>
        public (Vector2 from, Vector2 to) Tick(bool held, Vector2 mouse, float dt, Func<Vector2, Vector2> clamp)
        {
            if (!_started)
                Reset(Vector2.zero);
            dt = Mathf.Max(dt, 1e-4f);
            var before = _lastHead;
            if (held)
            {
                var step = new Vector2(mouse.x, mouse.y) * MetresPerPixel;
                Cursor = clamp != null ? clamp(Cursor + step) : Cursor + step;
                float speed = mouse.magnitude / dt;
                if (step.sqrMagnitude > 1e-12f)
                {
                    var want2 = Vector2.Lerp(Heading, step.normalized, Mathf.Clamp01(10f * dt)).normalized;
                    float turn = Vector2.SignedAngle(Heading, want2);
                    // Ngược hẳn chiều (Lerp gần 0): quay qua phía của thành phần ngang của bước rê, hướng nào cũng được nhưng phải dần.
                    if (Vector2.Dot(Heading, step.normalized) < -0.999f) turn = 180f;
                    float most = HeadingTurnDegreesPerSecond * dt;
                    Heading = Rotate(Heading, Mathf.Clamp(turn, -most, most)).normalized;
                }
                float want = Mathf.Clamp01(speed / FullPixelsPerSecond);
                Activity = Mathf.MoveTowards(Activity, want, (want > Activity ? ActivityRise : ActivityFall) * dt);
                Lift = Mathf.MoveTowards(Lift, 0f, LiftSpeed * dt);
            }
            else
            {
                Activity = Mathf.MoveTowards(Activity, 0f, ActivityFall * dt);
                Lift = Mathf.MoveTowards(Lift, LiftHeight, LiftSpeed * dt);
            }
            Phase += 2f * Mathf.PI * StrokeHz * dt * Mathf.Lerp(0.4f, 1f, Activity);
            if (Phase > 2f * Mathf.PI * 1000f) Phase -= 2f * Mathf.PI * 1000f;
            var head = Cursor + Stroke;
            if (clamp != null) head = clamp(head);
            if (!float.IsInfinity(MaxHeadSpeed))
            {
                float speedBefore = _headVelocity.magnitude;
                var want = Vector2.ClampMagnitude((head - before) / dt, MaxHeadSpeed);
                _headVelocity += Vector2.ClampMagnitude(want - _headVelocity, MaxHeadAccel * dt);
                _headVelocity = Vector2.ClampMagnitude(_headVelocity, MaxHeadSpeed);
                head = before + _headVelocity * dt;
                if (clamp != null)
                {
                    // Vướng mép tầm tay: dừng ở mép, vận tốc theo đúng quãng đã đi (khung sau tăng tốc lại từ đó). Hàm kẹp có thể trả một
                    // điểm xa chỗ đầu chải đang đứng: chia đôi từ con trỏ (cách đầu chải tới cả biên độ nhát), hay kéo con trỏ ngoài tầm
                    // về chỗ đạt một bước 1/10 quãng tới chỗ về (BrushScrub.Recover). Vùng với tới dời theo nồi chao (sóng ngày 4) nên điểm
                    // vừa đạt hóa ngoài tầm liên tục; nhận thẳng điểm kẹp thì mũi lông vụt 4 cm/khung (Brush17 có sóng). Đầu chải đi về
                    // phía điểm kẹp, vẫn trong hạn tốc độ và gia tốc; vài khung sau là tới.
                    var clamped = clamp(head);
                    float most = Mathf.Min(MaxHeadSpeed, speedBefore + MaxHeadAccel * dt) * dt;
                    head = before + Vector2.ClampMagnitude(clamped - before, most);
                    _headVelocity = (head - before) / dt;
                }
            }
            _lastHead = head;
            if (!Pressing)
                return (head, head);
            Travelled += Vector2.Distance(before, head);
            return (before, head);
        }
    }
}
