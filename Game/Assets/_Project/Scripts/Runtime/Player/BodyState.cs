using HaiBanTay.Core;
using UnityEngine;

namespace HaiBanTay.Player
{
    /// <summary>
    /// Áp BodyLogic lên người chơi: đi chậm, đầu lắc nhiều hơn, tay run, tay bóng mồ hôi, viền tối/ấm/lạnh, hơi thở.
    /// Việc nặng gọi Exert; vác đồ nặng khi di chuyển tự tính gắng sức. Nhiệt độ quanh người lấy từ HeatSource,
    /// AmbientHeat (phòng) và InCold (ColdZone).
    /// </summary>
    public sealed class BodyState : MonoBehaviour
    {
        public static BodyState Current { get; private set; }

        public float carryExertPerSecond = 0.0035f;
        public float heatInterval = 0.25f;

        PlayerRig _rig;
        float _heatTimer;
        float _heatTarget;
        float _busy;

        public BodyLogic Logic { get; } = new BodyLogic();
        /// <summary>Nhiệt nền của phòng (bếp ấm ~0,2), director đặt theo ngày.</summary>
        public float AmbientHeat { get; set; }
        public bool InCold { get; set; }
        /// <summary>Tắt thì không tính mệt (cảnh không có ca làm), hiệu ứng về 0.</summary>
        public bool Simulate { get; set; }

        void Awake()
        {
            _rig = GetComponent<PlayerRig>();
        }

        void OnEnable()
        {
            Current = this;
        }

        void OnDisable()
        {
            if (Current == this)
                Current = null;
        }

        public void Begin(float floor, float fatigue)
        {
            Simulate = true;
            Logic.Begin(floor, fatigue, AmbientHeat);
            Apply();
        }

        /// <summary>Gắng sức một lần (xúc một xẻng, nhấc bao…).</summary>
        public void Exert(float amount)
        {
            if (!Simulate)
                return;
            Logic.Exert(amount);
            _busy = 1f;
        }

        void Update()
        {
            if (!Simulate || _rig == null)
                return;
            float dt = Time.deltaTime;
            _heatTimer -= dt;
            if (_heatTimer <= 0f)
            {
                _heatTimer = heatInterval;
                _heatTarget = ComputeHeatTarget();
            }
            var motor = _rig.motor;
            bool moving = motor != null && motor.CurrentSpeed > 0.1f;
            bool carrying = motor != null && motor.Carrying;
            _busy = Mathf.Max(0f, _busy - dt);
            Logic.Tick(dt, _heatTarget, moving || _busy > 0f, carrying && moving ? carryExertPerSecond : 0f);
            Apply();
        }

        float ComputeHeatTarget()
        {
            if (InCold)
                return -1f;
            float heat = AmbientHeat;
            var p = transform.position + Vector3.up;
            var sources = HeatSource.All;
            for (int i = 0; i < sources.Count; i++)
                heat += sources[i].At(p);
            return Mathf.Clamp(heat, -1f, 1f);
        }

        void Apply()
        {
            if (_rig == null)
                return;
            if (_rig.motor != null)
                _rig.motor.SpeedMultiplier = Logic.SpeedMultiplier;
            _rig.ShakeScale = Logic.ShakeMultiplier;
            if (_rig.hands != null)
            {
                _rig.hands.Tremble = Logic.Tremble;
                _rig.hands.Sweat = Logic.Sweat;
            }
            _rig.SetBreath(Logic.Chill > 0.35f);
            GameRoot.Instance?.UI.Tint.SetBody(Logic.Chill, Logic.Warmth, Logic.Darkness);
        }

        /// <summary>Thôi tính (hết ca, chuyển cảnh): xóa hiệu ứng.</summary>
        public void Stop()
        {
            Simulate = false;
            if (_rig == null)
                return;
            if (_rig.motor != null)
                _rig.motor.SpeedMultiplier = 1f;
            _rig.ShakeScale = 1f;
            if (_rig.hands != null)
                _rig.hands.Tremble = 0f;
            _rig.SetBreath(false);
            GameRoot.Instance?.UI.Tint.SetBody(0f, 0f, 0f);
        }
    }
}
