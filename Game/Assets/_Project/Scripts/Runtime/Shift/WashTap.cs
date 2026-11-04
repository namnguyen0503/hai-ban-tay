using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Vòi nước trên chậu rửa (QA-0132): gạt cần thì cần quay xuống và dòng nước chảy vào chậu; gạt lại thì tắt. Dòng nước là một trụ mảnh
    /// và vệt bắn dưới chân dòng, chỉ bật khi đang chảy (không hạt, không đèn: ngân sách Laptop). Chỉ chạy Update lúc cần đang quay.
    /// </summary>
    public sealed class WashTap : MonoBehaviour
    {
        [Tooltip("Trục cần gạt (quay quanh trục X cục bộ).")]
        public Transform lever;
        [Tooltip("Dòng nước và vệt bắn: bật khi đang chảy.")]
        public GameObject stream;
        public float offAngle = 0f;
        public float onAngle = -70f;
        public float turnSeconds = 0.25f;

        float _angle, _target;

        public bool Running { get; private set; }

        void Awake()
        {
            _angle = _target = offAngle;
            Apply();
            if (stream != null) stream.SetActive(false);
            enabled = false;
        }

        /// <summary>Mở hoặc khóa vòi; cần quay trong <see cref="turnSeconds"/>, nước chảy ngay khi mở và tắt ngay khi khóa.</summary>
        public void SetRunning(bool on)
        {
            if (Running == on) return;
            Running = on;
            _target = on ? onAngle : offAngle;
            if (stream != null) stream.SetActive(on);
            enabled = true;
        }

        void Update()
        {
            float speed = Mathf.Abs(onAngle - offAngle) / Mathf.Max(0.01f, turnSeconds);
            _angle = Mathf.MoveTowards(_angle, _target, speed * Time.deltaTime);
            Apply();
            if (Mathf.Approximately(_angle, _target)) enabled = false;
        }

        void Apply()
        {
            if (lever != null) lever.localRotation = Quaternion.Euler(_angle, 0f, 0f);
        }
    }
}
