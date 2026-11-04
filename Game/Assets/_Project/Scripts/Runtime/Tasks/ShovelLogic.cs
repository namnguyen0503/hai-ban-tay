namespace HaiBanTay.Tasks
{
    public enum ShovelEvent
    {
        None,
        Loaded,
        Poured,
        Spilled
    }

    /// <summary>Xúc than: giữ chuột trái ở thùng than (0,6 s, chậm hơn khi mệt/lạnh) để xúc đầy; mang tới lò, nhả để đổ.
    /// Nhả khi xẻng rỗng không tính; nhả khi chưa tới lò thì đổ mất.</summary>
    public sealed class ShovelLogic
    {
        public const float FillSeconds = 0.6f;

        public bool Loaded { get; private set; }
        public float Fill { get; private set; }
        public int Poured { get; private set; }

        bool _wasHeld;

        /// <summary>speed: hệ số tốc độ làm việc (BodyLogic.WorkSpeed), 1 = bình thường.</summary>
        public ShovelEvent Tick(bool useHeld, bool atBin, bool atStove, float dt, float speed = 1f)
        {
            var result = ShovelEvent.None;
            if (useHeld)
            {
                if (!Loaded && atBin)
                {
                    Fill += dt * (speed > 0.05f ? speed : 0.05f);
                    if (Fill >= FillSeconds)
                    {
                        Fill = FillSeconds;
                        Loaded = true;
                        result = ShovelEvent.Loaded;
                    }
                }
            }
            else if (_wasHeld)
            {
                if (Loaded)
                {
                    if (atStove)
                    {
                        Poured++;
                        result = ShovelEvent.Poured;
                    }
                    else
                    {
                        result = ShovelEvent.Spilled;
                    }
                    Loaded = false;
                }
                Fill = 0f;
            }
            _wasHeld = useHeld;
            return result;
        }

        /// <summary>Bỏ xẻng xuống: than trên xẻng (nếu có) coi như đổ lại thùng.</summary>
        public void Reset()
        {
            Loaded = false;
            Fill = 0f;
            _wasHeld = false;
        }
    }
}
