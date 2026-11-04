using HaiBanTay.Core;
using UnityEngine;

namespace HaiBanTay.Player
{
    /// <summary>Nhìn bằng chuột: yaw xoay thân, pitch xoay đầu (camera Cinemachine là con của đầu).
    /// Look-assist kéo nhẹ hướng nhìn về mục tiêu khi người chơi không rê chuột.</summary>
    public sealed class LookController : MonoBehaviour
    {
        public Transform head;
        public float sensitivity = 0.08f;
        public float minPitch = -75f;
        public float maxPitch = 85f;
        public float assistDegreesPerSecond = 12f;

        GameInput _input;
        float _yaw;
        float _pitch;

        public bool Locked { get; set; }
        /// <summary>Hệ số xoay ngang (giữ thăng bằng trên cầu thang dùng chuột ngang để đỡ bao).</summary>
        public float YawScale { get; set; } = 1f;
        public Transform AssistTarget { get; set; }
        public float Yaw => _yaw;
        public float Pitch => _pitch;

        void Awake()
        {
            _yaw = transform.eulerAngles.y;
        }

        void Start()
        {
            _input = GameRoot.Instance != null ? GameRoot.Instance.Input : null;
        }

        void Update()
        {
            if (Locked || _input == null)
                return;
            var settings = GameSettings.Current;
            var d = _input.Look * (sensitivity * settings.MouseSensitivity);
            if (settings.InvertY)
                d.y = -d.y;
            _yaw += d.x * YawScale;
            _pitch = Mathf.Clamp(_pitch - d.y, minPitch, maxPitch);

            if (AssistTarget != null && d.sqrMagnitude < 0.0001f && head != null)
            {
                var to = AssistTarget.position - head.position;
                if (to.sqrMagnitude > 0.01f)
                {
                    float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    float wantPitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
                    float step = assistDegreesPerSecond * Time.deltaTime;
                    // Chỉ kéo khi lệch vừa phải: không giật camera nếu người chơi đang nhìn hẳn chỗ khác.
                    if (Mathf.Abs(Mathf.DeltaAngle(_yaw, wantYaw)) < 60f)
                    {
                        _yaw = Mathf.MoveTowardsAngle(_yaw, wantYaw, step);
                        _pitch = Mathf.MoveTowards(_pitch, Mathf.Clamp(wantPitch, minPitch, maxPitch), step);
                    }
                }
            }
            Apply();
        }

        public void SetAngles(float yaw, float pitch)
        {
            _yaw = yaw;
            _pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            Apply();
        }

        void Apply()
        {
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (head != null)
                head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }
    }
}
