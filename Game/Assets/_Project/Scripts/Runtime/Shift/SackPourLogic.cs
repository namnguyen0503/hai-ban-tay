using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Mở bao khoai trên bàn sơ chế: cởi dây (E), rồi giữ chuột trái và kéo chuột xuống để nghiêng bao. Bao nghiêng quá
    /// ngưỡng thì khoai lăn dần ra thùng; kéo đủ quãng thì bao rỗng. Nhả chuột thì bao giữ nguyên độ nghiêng.
    /// </summary>
    public sealed class SackPourLogic
    {
        /// <summary>Quãng chuột (pixel, kéo xuống) để dốc hết một bao.</summary>
        public float PixelsToEmpty = 900f;
        /// <summary>Số củ khoai hiện ra cho mỗi bao (tượng trưng).</summary>
        public int Potatoes = 8;
        /// <summary>Độ nghiêng (0..1) khoai bắt đầu lăn ra.</summary>
        public float SpillFrom = 0.3f;

        public bool Untied { get; private set; }
        public float Progress { get; private set; }
        public int Released { get; private set; }
        public bool Empty => Progress >= 1f;

        public void Untie() => Untied = true;

        /// <summary>Trả về số củ vừa lăn ra trong khung này.</summary>
        public int Tick(bool held, float mouseDy, float speed = 1f)
        {
            if (!Untied || !held || Empty || mouseDy >= 0f)
                return 0;
            Progress = Mathf.Min(1f, Progress + -mouseDy * Mathf.Max(0f, speed) / PixelsToEmpty);
            return Release(Progress >= 1f ? Potatoes : Mathf.FloorToInt(Mathf.Clamp01((Progress - SpillFrom) / (1f - SpillFrom)) * Potatoes));
        }

        /// <summary>Dốc hết ngay (Sim, test).</summary>
        public int Finish()
        {
            Untied = true;
            Progress = 1f;
            return Release(Potatoes);
        }

        int Release(int target)
        {
            int n = Mathf.Max(0, target - Released);
            Released += n;
            return n;
        }

        public void Reset()
        {
            Untied = false;
            Progress = 0f;
            Released = 0;
        }
    }
}
