using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Cào lò (F03): cầm cào, nhìn vào cửa tro dưới lò, giữ chuột trái và kéo chuột về phía mình (rê xuống). Mỗi nhát cào
    /// gạt tro khỏi ghi lò xuống xô; đủ nhát thì xô đầy, phải mang đi đổ ở máng tro rồi mới cào tiếp.
    /// </summary>
    public sealed class RakeLogic
    {
        /// <summary>Quãng kéo chuột (pixel, chiều xuống) cho một nhát cào.</summary>
        public float PixelsPerStroke = 240f;
        public int StrokesPerBucket = 4;
        /// <summary>Tro gạt khỏi ghi mỗi nhát (FireLogic.Ash, 0–1).</summary>
        public float AshPerStroke = 0.12f;

        float _pull;

        public int Strokes { get; private set; }
        public int InBucket { get; private set; }
        public float AshAmount { get; private set; }
        public float LastTransfer { get; private set; }
        public bool BucketFull => InBucket >= StrokesPerBucket;
        public float Fill => Capacity > 0f ? Mathf.Clamp01(AshAmount / Capacity) : 1f;
        public float Capacity => StrokesPerBucket * AshPerStroke;
        public float StrokeProgress => Mathf.Clamp01(_pull / Mathf.Max(1f, PixelsPerStroke));

        /// <summary>mouseDy: độ rê chuột theo trục dọc khung này (âm = kéo về phía mình). speed: hệ số làm việc.
        /// Trả về true khi xong một nhát.</summary>
        public bool Tick(bool held, bool atDoor, float mouseDy, float speed = 1f, float availableAsh = float.PositiveInfinity)
        {
            LastTransfer = 0f;
            if (!held || !atDoor || BucketFull || availableAsh <= 0f)
            {
                if (!held)
                    _pull = 0f;
                return false;
            }
            if (mouseDy < 0f)
                _pull += -mouseDy * Mathf.Max(0.05f, speed);
            if (_pull < PixelsPerStroke)
                return false;
            _pull = 0f;
            Strokes++;
            InBucket++;
            LastTransfer = Mathf.Min(AshPerStroke, availableAsh);
            AshAmount += LastTransfer;
            return true;
        }

        /// <summary>Xô đã đổ ở máng tro.</summary>
        public void Empty()
        {
            InBucket = 0;
            AshAmount = LastTransfer = 0f;
            _pull = 0f;
        }

        /// <summary>Sim: đầy xô ngay.</summary>
        public void ForceFull()
        {
            Strokes += StrokesPerBucket - InBucket;
            InBucket = StrokesPerBucket;
            AshAmount = Capacity;
        }
    }
}
