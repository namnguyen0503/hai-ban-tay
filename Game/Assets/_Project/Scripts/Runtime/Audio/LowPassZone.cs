using HaiBanTay.Core;
using HaiBanTay.Player;
using UnityEngine;

namespace HaiBanTay.Audio
{
    /// <summary>E3: đi dọc cầu thang tàu thì tần số cắt giảm từ 22 kHz xuống 800 Hz theo tiến độ start → end.
    /// Ra khỏi vùng thì trả về 22 kHz.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class LowPassZone : MonoBehaviour
    {
        public Transform start;
        public Transform end;
        public float fromCutoff = AudioService.FullCutoff;
        public float toCutoff = 800f;

        bool _inside;
        Transform _player;

        public float Progress { get; private set; }

        void OnTriggerEnter(Collider other)
        {
            var rig = other.GetComponentInParent<PlayerRig>();
            if (rig == null)
                return;
            _inside = true;
            _player = rig.transform;
        }

        void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<PlayerRig>() == null)
                return;
            _inside = false;
            GameRoot.Instance?.Audio.SetLowPass(AudioService.FullCutoff);
        }

        void Update()
        {
            if (!_inside || _player == null || start == null || end == null)
                return;
            Progress = ProgressAlong(start.position, end.position, _player.position);
            GameRoot.Instance?.Audio.SetLowPass(CutoffAt(Progress));
        }

        /// <summary>Nội suy theo thang log để tai nghe thấy giảm đều.</summary>
        public float CutoffAt(float t)
        {
            t = Mathf.Clamp01(t);
            return Mathf.Exp(Mathf.Lerp(Mathf.Log(fromCutoff), Mathf.Log(toCutoff), t));
        }

        public static float ProgressAlong(Vector3 a, Vector3 b, Vector3 p)
        {
            var ab = b - a;
            float len2 = ab.sqrMagnitude;
            return len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
        }
    }
}
