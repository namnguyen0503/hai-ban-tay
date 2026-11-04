using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>Một ô có nhiên liệu, nhiệt và tro riêng. Than đã cháy chỉ chuyển thành tro tại chính ô đó.</summary>
    public sealed class FireCellState
    {
        public const float MaxFuel = 6f;
        public const float FullFuel = 3f;
        public const float CookingHeat = 0.5f;
        public float BurnPerSecond = 0.025f;
        public float AshPerFuel = 0.05f;
        public float Response = 0.35f;
        public float Fuel { get; private set; }
        public float Heat { get; private set; }
        public float Ash { get; private set; }
        public float BurnedFuel { get; private set; }
        public float RemovedAsh { get; private set; }
        public float Target => Mathf.Clamp01(Fuel / FullFuel) * (1f - 0.6f * Mathf.Clamp01(Ash));
        public bool Hot => Heat >= CookingHeat;

        public float AddCoal(float amount)
        {
            float added = Mathf.Min(Mathf.Max(0f, amount), MaxFuel - Fuel);
            Fuel += added;
            return added;
        }
        public float Rake(float amount)
        {
            float removed = Mathf.Min(Ash, Mathf.Max(0f, amount));
            Ash -= removed;
            RemovedAsh += removed;
            return removed;
        }
        public void Set(float fuel, float heat, float ash = 0f)
        {
            Fuel = Mathf.Clamp(fuel, 0f, MaxFuel);
            Heat = Mathf.Clamp01(heat);
            Ash = Mathf.Max(0f, ash);
            BurnedFuel = RemovedAsh = 0f;
        }
        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            float burned = Mathf.Min(Fuel, Mathf.Max(0f, BurnPerSecond) * dt);
            Fuel -= burned;
            BurnedFuel += burned;
            // Không kẹp lượng tro: ô đầy vẫn phải giữ phần tro vừa sinh ra.
            Ash += burned * Mathf.Max(0f, AshPerFuel);
            Heat = Mathf.MoveTowards(Heat, Target, Mathf.Max(0f, Response) * dt);
        }
    }
}
