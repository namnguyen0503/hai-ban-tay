using System.Collections;
using UnityEngine;

namespace HaiBanTay.Ending
{
    /// <summary>E1: haze xa dịu dần; tiền cảnh giữ màu và tương phản.</summary>
    public sealed class FogLift : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public float from = 0.005f;
        public float to = 0.002f;
        public Renderer[] mistLayers = new Renderer[0];
        public Color mistColor = new Color(0.68f, 0.73f, 0.77f, 0.10f);

        MaterialPropertyBlock _mpb;

        public float Progress { get; private set; }

        void Awake()
        {
            Apply(0f);
        }

        public IEnumerator Run(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                Apply(Mathf.SmoothStep(0f, 1f, t / seconds));
                yield return null;
            }
            Apply(1f);
        }

        void Apply(float k)
        {
            Progress = k;
            RenderSettings.fogDensity = Mathf.Lerp(from, to, k);
            _mpb ??= new MaterialPropertyBlock();
            for (int i = 0; i < mistLayers.Length; i++)
            {
                var r = mistLayers[i];
                if (r == null)
                    continue;
                // Lớp gần tan trước, lớp xa tan sau.
                float local = Mathf.Clamp01(k * 1.4f - i * 0.15f);
                var c = mistColor;
                c.a *= 1f - local;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorId, c);
                r.SetPropertyBlock(_mpb);
                r.enabled = c.a > 0.005f;
            }
        }
    }
}
