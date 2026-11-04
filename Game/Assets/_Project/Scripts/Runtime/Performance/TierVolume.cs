using HaiBanTay.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace HaiBanTay.Performance
{
    /// <summary>Đổi VolumeProfile hậu kỳ theo tầng chất lượng.</summary>
    [RequireComponent(typeof(Volume))]
    public sealed class TierVolume : MonoBehaviour
    {
        public VolumeProfile laptop;
        public VolumeProfile pc;

        Volume _volume;
        QualityTierManager _quality;

        void Awake()
        {
            _volume = GetComponent<Volume>();
        }

        void Start()
        {
            _quality = GameRoot.Instance != null ? GameRoot.Instance.Quality : null;
            if (_quality == null)
                return;
            _quality.TierChanged += Apply;
            Apply(_quality.Current);
        }

        void OnDestroy()
        {
            if (_quality != null)
                _quality.TierChanged -= Apply;
        }

        void Apply(QualityTier tier)
        {
            var p = tier == QualityTier.Laptop ? laptop : pc;
            if (p != null)
                _volume.sharedProfile = p;
        }
    }
}
