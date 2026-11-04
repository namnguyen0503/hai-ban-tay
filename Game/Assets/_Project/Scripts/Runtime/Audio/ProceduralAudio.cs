using UnityEngine;

namespace HaiBanTay.Audio
{
    /// <summary>Âm giữ chỗ sinh bằng code khi nhóm chưa có file thật (tiếng máy tàu, sóng/bão).</summary>
    public static class ProceduralAudio
    {
        const int Rate = 22050;

        /// <summary>Tiếng ù trầm lặp liền mạch: nhiễu nâu + sóng 50 Hz điều biên nhẹ.</summary>
        public static AudioClip EngineHum(float seconds = 4f)
        {
            int n = Mathf.RoundToInt(seconds * Rate);
            var data = new float[n];
            var rng = new System.Random(11);
            float brown = 0f;
            for (int i = 0; i < n; i++)
            {
                brown = Mathf.Clamp(brown + ((float)rng.NextDouble() * 2f - 1f) * 0.02f, -1f, 1f);
                float t = (float)i / Rate;
                float tone = Mathf.Sin(2f * Mathf.PI * 50f * t) * 0.35f + Mathf.Sin(2f * Mathf.PI * 100f * t) * 0.12f;
                float pulse = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 2f * t);
                data[i] = (tone * pulse + brown * 0.6f) * 0.5f;
            }
            data = Crossfade(data, Rate / 5);
            var clip = AudioClip.Create("ProceduralEngineHum", data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Tiếng sóng/gió: nhiễu lọc thấp, cường độ dâng hạ chậm.</summary>
        public static AudioClip Surf(float seconds = 6f, int seed = 7)
        {
            int n = Mathf.RoundToInt(seconds * Rate);
            var data = new float[n];
            var rng = new System.Random(seed);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                lp += (white - lp) * 0.08f;
                float t = (float)i / n;
                float swell = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * (t * 2f));
                data[i] = lp * swell * 0.8f;
            }
            data = Crossfade(data, Rate / 4);
            var clip = AudioClip.Create("ProceduralSurf", data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // Trộn đuôi vào đầu rồi cắt đuôi để lặp không có tiếng tách.
        static float[] Crossfade(float[] data, int len)
        {
            int n = data.Length;
            for (int i = 0; i < len; i++)
            {
                float k = (float)i / len;
                data[i] = data[i] * k + data[n - len + i] * (1f - k);
            }
            System.Array.Resize(ref data, n - len);
            return data;
        }
    }
}
