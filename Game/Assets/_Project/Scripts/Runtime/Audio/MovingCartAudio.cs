using HaiBanTay.Core;
using UnityEngine;

namespace HaiBanTay.Audio
{
    /// <summary>Xe đỗ im lặng; bánh xe chỉ kêu khi xe thật sự di chuyển.</summary>
    public sealed class MovingCartAudio : MonoBehaviour
    {
        AudioService _audio;
        AudioSource _source;
        Vector3 _last;
        public Transform motionFrame;

        Vector3 Position
        {
            get
            {
                if (motionFrame == null) return transform.position;
                // Ghép tọa độ local tới phông bờ, tránh sai số world → local khi bến lùi.
                var point = transform.localPosition;
                for (var parent = transform.parent; parent != null; parent = parent.parent)
                {
                    if (parent == motionFrame) return point;
                    point = parent.localPosition + parent.localRotation * Vector3.Scale(parent.localScale, point);
                }
                return motionFrame.InverseTransformPoint(transform.position);
            }
        }

        void Start()
        {
            _audio = GameRoot.Instance != null ? GameRoot.Instance.Audio : null;
            _last = Position;
            if (_audio != null)
                _source = _audio.StartLoop("SFX/cart_wheels", AudioChannel.Sfx, gameObject, volume: 0f, spatial: true);
        }

        void Update()
        {
            // Đo trong hệ của phông bờ nếu được gán: bến lùi xa không làm bánh xe đỗ kêu.
            var position = Position;
            float speed = (position - _last).magnitude / Mathf.Max(Time.deltaTime, 0.001f);
            _last = position;
            if (_source != null)
                _source.volume = Time.timeScale > 0f ? Mathf.Clamp01(speed) * 0.45f * _audio.Volume(AudioChannel.Sfx) : 0f;
        }
    }
}
