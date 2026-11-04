using System.Collections;
using HaiBanTay.Audio;
using HaiBanTay.Core;
using UnityEngine;

namespace HaiBanTay.Ship
{
    /// <summary>Bão (B2.3): tăng ShipMotion từ mức nền lên 1 trong 8 s; dịu lại khi hết ca. Âm sóng/bão theo cường độ.</summary>
    [DefaultExecutionOrder(120)]
    public sealed class StormController : MonoBehaviour
    {
        public ShipMotion ship;
        public float rampSeconds = 8f;
        public float calmIntensity = 0.2f;
        public string stormLoopKey = "Ambience/storm";

        AudioSource _loop;
        float _level;

        public bool Active { get; private set; }

        public void StartStorm()
        {
            Active = true;
            StopAllCoroutines();
            if (_loop == null && GameRoot.Instance != null)
                _loop = GameRoot.Instance.Audio.StartLoop(stormLoopKey, AudioChannel.Ambience, gameObject,
                    GameRoot.Instance.Audio.Clip(stormLoopKey) == null ? ProceduralAudio.Surf() : null, 0f);
            StartCoroutine(Ramp(1f, rampSeconds));
        }

        public void Calm(float seconds = 4f)
        {
            Active = false;
            StopAllCoroutines();
            StartCoroutine(Ramp(calmIntensity, seconds));
        }

        IEnumerator Ramp(float to, float seconds)
        {
            float from = ship.Intensity;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                Set(Mathf.Lerp(from, to, t / seconds));
                yield return null;
            }
            Set(to);
        }

        void Set(float v)
        {
            ship.Intensity = v;
            _level = Mathf.Clamp01((v - calmIntensity) / Mathf.Max(0.01f, 1f - calmIntensity));
        }

        void Update()
        {
            if (_loop != null)
            {
                float ch = GameRoot.Instance != null ? GameRoot.Instance.Audio.Volume(AudioChannel.Ambience) : 1f;
                _loop.volume = _level * ch;
            }
        }

        void LateUpdate()
        {
            if (!Active) return;
            // Mức sáng nền cho boong bão khi chiếu TV; chạy sau ánh sáng giờ, không đổi trời/biển.
            RenderSettings.ambientSkyColor = Floor(RenderSettings.ambientSkyColor, new Color(0.20f, 0.24f, 0.29f));
            RenderSettings.ambientEquatorColor = Floor(RenderSettings.ambientEquatorColor, new Color(0.14f, 0.17f, 0.21f));
            RenderSettings.ambientGroundColor = Floor(RenderSettings.ambientGroundColor, new Color(0.07f, 0.085f, 0.11f));
        }

        static Color Floor(Color color, Color minimum) => new Color(Mathf.Max(color.r, minimum.r),
            Mathf.Max(color.g, minimum.g), Mathf.Max(color.b, minimum.b), 1f);
    }
}
