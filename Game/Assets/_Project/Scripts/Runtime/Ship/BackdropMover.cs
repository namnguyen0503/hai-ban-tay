using System.Collections;
using UnityEngine;

namespace HaiBanTay.Ship
{
    /// <summary>Phông nền lùi dần (B1.4 nhổ neo): dịch gốc phông theo offset trong khoảng thời gian cho trước.</summary>
    public sealed class BackdropMover : MonoBehaviour
    {
        public Vector3 offset = new Vector3(0f, 0f, -120f);
        public float seconds = 60f;
        [Tooltip("Thời gian tàu tăng tốc (giây); sau đó phông lùi đều.")]
        public float rampSeconds = 6f;

        public bool Moving { get; private set; }

        public void Begin()
        {
            if (!Moving)
                StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            Moving = true;
            // Theo hệ của cha (WorldRoot): thế giới lắc quanh tàu thì phông vẫn lùi đúng hướng.
            var start = transform.localPosition;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                transform.localPosition = start + offset * Progress(t, seconds, rampSeconds);
                yield return null;
            }
            transform.localPosition = start + offset;
            Moving = false;
        }

        /// <summary>Quãng đã đi (0..1): tốc độ tăng đều trong ramp giây đầu rồi giữ nguyên tới hết.</summary>
        public static float Progress(float t, float total, float ramp)
        {
            ramp = Mathf.Clamp(ramp, 0f, total);
            float v = 1f / (total - ramp * 0.5f);
            if (t <= 0f)
                return 0f;
            if (t < ramp)
                return v * t * t / (2f * ramp);
            return Mathf.Min(1f, v * (t - ramp * 0.5f));
        }
    }
}
