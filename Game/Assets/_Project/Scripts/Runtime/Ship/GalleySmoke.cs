using HaiBanTay.Shift;
using UnityEngine;

namespace HaiBanTay.Ship
{
    /// <summary>Khói ở đỉnh ống bếp: mật độ theo mức lửa của lò (cảnh 02); không có lò (01, 03) thì khói nhẹ cố định.</summary>
    public sealed class GalleySmoke : MonoBehaviour
    {
        public CoalStation fire;
        public ParticleSystem smoke;
        [Range(0f, 1f)] public float idleLevel = 0.15f;
        public float minRate = 0.5f;
        public float maxRate = 10f;

        float _shown = -1f;

        public float Rate { get; private set; }

        void Update()
        {
            float level = fire != null ? fire.Fire.Level : idleLevel;
            if (Mathf.Abs(level - _shown) < 0.01f)
                return;
            _shown = level;
            Rate = Mathf.Lerp(minRate, maxRate, level);
            var emission = smoke.emission;
            emission.rateOverTime = Rate;
        }
    }
}
