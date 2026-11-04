using System.Collections;
using HaiBanTay.Audio;
using HaiBanTay.Core;
using UnityEngine;

namespace HaiBanTay.Ship
{
    /// <summary>Âm máy tàu lặp suốt cảnh 1–2 và đầu cảnh 3; tắt dần ở E1. Chưa có file thì dùng âm sinh bằng code.</summary>
    public sealed class EngineHum : MonoBehaviour
    {
        public string clipKey = "Ambience/engine";
        public float volume = 0.45f;
        public bool playOnStart = true;

        AudioSource _source;
        AudioService _audio;
        float _gain = 1f;

        public bool Playing => _source != null && _source.isPlaying;

        void Start()
        {
            if (playOnStart)
                Play();
        }

        public void Play()
        {
            var root = GameRoot.Instance;
            if (root == null || _source != null)
                return;
            _audio = root.Audio;
            _source = _audio.StartLoop(clipKey, AudioChannel.Ambience, gameObject,
                _audio.Clip(clipKey) == null ? ProceduralAudio.EngineHum() : null, volume);
        }

        void Update()
        {
            if (_source != null)
                _source.volume = volume * _gain * _audio.Volume(AudioChannel.Ambience);
        }

        public IEnumerator FadeOut(float seconds)
        {
            if (_source == null)
                yield break;
            float from = _gain;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                _gain = Mathf.Lerp(from, 0f, t / seconds);
                yield return null;
            }
            _gain = 0f;
            _source.Stop();
        }
    }
}
