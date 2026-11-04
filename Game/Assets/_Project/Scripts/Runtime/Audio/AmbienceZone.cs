using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Player;
using UnityEngine;

namespace HaiBanTay.Audio
{
    /// <summary>Âm nền theo vùng: người chơi vào vùng thì nguồn lặp tăng dần, ra thì giảm.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class AmbienceZone : MonoBehaviour
    {
        public string clipKey;
        public float volume = 0.8f;
        public float fadeSeconds = 1.5f;
        public PlayerArea area;

        AudioSource _source;
        float _target;
        float _level;
        bool _stopping;

        /// <summary>Rời phố/chuyển cảnh: giảm dần và không phát lại khi còn đứng trong vùng.</summary>
        public void FadeOut() => _stopping = true;

        void Start()
        {
            var root = GameRoot.Instance;
            if (root == null)
                return;
            _source = root.Audio.StartLoop(clipKey, AudioChannel.Ambience, gameObject, null, 0f);
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerRig>() != null)
                _target = volume;
        }

        void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<PlayerRig>() != null)
                _target = 0f;
        }

        void Update()
        {
            if (_source == null)
                return;
            if (area != null)
                _target = area.PlayerInside ? volume : 0f;
            if (_stopping)
                _target = 0f;
            float v = GameRoot.Instance != null ? GameRoot.Instance.Audio.Volume(AudioChannel.Ambience) : 1f;
            _level = Mathf.MoveTowards(_level, _target, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
            _source.volume = _level * v;
        }
    }
}
