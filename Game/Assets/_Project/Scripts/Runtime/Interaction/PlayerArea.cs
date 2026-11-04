using HaiBanTay.Player;
using UnityEngine;

namespace HaiBanTay.Interaction
{
    /// <summary>Vùng biết người chơi có đang đứng trong không (theo bounds của collider, như ColdZone): đồ đã giao chỉ
    /// trở về chỗ cũ khi người chơi không nhìn thấy.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class PlayerArea : MonoBehaviour
    {
        const float Interval = 0.2f;

        Collider _area;
        float _timer;

        public bool PlayerInside { get; private set; }

        void Awake()
        {
            _area = GetComponent<Collider>();
        }

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f)
                return;
            _timer = Interval;
            var rig = PlayerRig.Current;
            PlayerInside = rig != null && _area.bounds.Contains(rig.Position + Vector3.up);
        }

        public bool Contains(Vector3 p) => _area != null && _area.bounds.Contains(p);
    }
}
