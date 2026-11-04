using UnityEngine;

namespace HaiBanTay.Narrative
{
    /// <summary>Chiếu bóng câm: nhấp nháy trên màn vải, bóng người cử động, hai cuộn phim quay; không thêm đèn realtime.</summary>
    public sealed class ProjectionFlicker : MonoBehaviour
    {
        public Renderer screen;
        public Transform[] limbs = System.Array.Empty<Transform>();
        public Transform[] reels = System.Array.Empty<Transform>();
        /// <summary>Màu emission trung bình của vùng sáng (HDR); nhấp nháy nhân hệ số quanh 1.</summary>
        public Color tint = new Color(1.5f, 1.3f, 1f);
        MaterialPropertyBlock _block;
        void Awake() => _block = new MaterialPropertyBlock();
        void Update()
        {
            float value = 1f + 0.09f * Mathf.Sin(Time.time * 31f) + 0.05f * Mathf.Sin(Time.time * 19f);
            _block.SetColor("_EmissionColor", new Color(tint.r * value, tint.g * value, tint.b * value, 1f));
            screen.SetPropertyBlock(_block);
            // Phim câm chạy 12 khung/giây: bước nhỏ, tay chào; chân không xoắn chéo thành hình kéo.
            float frame = Mathf.Floor(Time.time * 12f) / 12f;
            for (int i = 0; i < limbs.Length; i++)
            {
                int part = i % 4;
                float side = part % 2 == 0 ? -1f : 1f;
                float swing = Mathf.Sin(frame * 4f + i / 4f + (part % 2) * Mathf.PI);
                float angle = part < 2 ? side * 8f + swing * 12f : swing * 7f;
                if (i == 5) angle = 45f + swing * 10f;
                limbs[i].localRotation = Quaternion.Euler(0f, 0f, angle);
            }
            // Cuộn nạp quay chậm, cuộn thu nhanh hơn một chút; cũng nhảy theo khung phim.
            for (int i = 0; i < reels.Length; i++)
                reels[i].localRotation = Quaternion.Euler(0f, -frame * (130f + i * 50f), 0f);
        }
    }
}
