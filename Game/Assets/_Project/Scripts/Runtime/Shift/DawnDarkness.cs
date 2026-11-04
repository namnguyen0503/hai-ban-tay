using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Bếp bốn giờ sáng ngày 1 (DIRECTOR §6): căn phòng gần như tối, chỉ còn ánh lò; lò bén lửa thì phòng đỏ dần lên.
    /// Lightmap đã bake không tắt được lúc chạy, nên hạ phơi sáng hậu kỳ bằng một Volume toàn cục tạo lúc chạy và nới dần theo
    /// mức lửa. Không đụng đèn, không đụng camera.
    /// </summary>
    public sealed class DawnDarkness : MonoBehaviour
    {
        [Tooltip("Phơi sáng (EV) lúc lò còn nguội.")]
        public float coldExposure = -1.6f;
        [Tooltip("Mức lửa (0..1) coi như phòng đã sáng hẳn.")]
        public float litLevel = 0.6f;

        Volume _volume;
        ColorAdjustments _adjust;
        System.Func<float> _fireLevel;
        float _weight;
        bool _released;

        /// <summary>Bật tối; <paramref name="fireLevel"/> đọc mức lửa lò mỗi khung.</summary>
        public void Begin(System.Func<float> fireLevel)
        {
            _fireLevel = fireLevel;
            _released = false;
            if (_volume == null)
            {
                _volume = gameObject.AddComponent<Volume>();
                _volume.isGlobal = true;
                _volume.priority = 50f;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.hideFlags = HideFlags.DontSave;
                _adjust = profile.Add<ColorAdjustments>();
                _adjust.postExposure.Override(coldExposure);
                _volume.profile = profile;
            }
            _weight = 1f;
            _volume.weight = 1f;
            enabled = true;
        }

        /// <summary>Hết đoạn sáng sớm: nới hẳn (vài giây) dù lửa chưa đủ.</summary>
        public void Release() => _released = true;

        void Update()
        {
            if (_volume == null)
                return;
            float level = _fireLevel != null ? Mathf.Clamp01(_fireLevel() / Mathf.Max(0.01f, litLevel)) : 1f;
            float target = _released ? 0f : 1f - level;
            _weight = Mathf.MoveTowards(_weight, target, Time.deltaTime * (target < _weight ? 0.35f : 1f));
            _volume.weight = _weight;
            if (_released && _weight <= 0f)
                enabled = false;
        }

        void OnDestroy()
        {
            if (_volume != null && _volume.profile != null)
                Destroy(_volume.profile);
        }
    }
}
