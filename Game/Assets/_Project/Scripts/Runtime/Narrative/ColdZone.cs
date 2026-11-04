using HaiBanTay.Core;
using HaiBanTay.Player;
using UnityEngine;

namespace HaiBanTay.Narrative
{
    /// <summary>Kho lạnh (B2.2): hơi thở thành khói, viền màn hình lạnh. Kiểm tra vị trí người chơi theo
    /// bounds của collider (không dựa vào OnTriggerExit, vốn không chạy khi người chơi bị dịch chuyển tức thời).</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class ColdZone : MonoBehaviour
    {
        const float Interval = 0.2f;

        Collider _area;
        bool _inside;
        float _timer;

        public bool PlayerInside => _inside;

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
            bool inside = rig != null && _area.bounds.Contains(rig.Position + Vector3.up);
            if (inside == _inside)
                return;
            _inside = inside;
            SetCold(rig, inside);
        }

        /// <summary>Có ca làm (BodyState đang tính): thân nhiệt hạ dần, BodyState lo hơi thở và viền lạnh.
        /// Không có: bật/tắt ngay như trước.</summary>
        static void SetCold(PlayerRig rig, bool inside)
        {
            var body = BodyState.Current;
            if (body != null && body.Simulate)
            {
                body.InCold = inside;
                return;
            }
            rig?.SetBreath(inside);
            GameRoot.Instance?.UI.Tint.SetCold(inside);
        }

        void OnDisable()
        {
            if (!_inside)
                return;
            _inside = false;
            SetCold(PlayerRig.Current, false);
        }
    }
}
