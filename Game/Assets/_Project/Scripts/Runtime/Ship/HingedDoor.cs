using HaiBanTay.Interaction;
using HaiBanTay.Player;
using UnityEngine;

namespace HaiBanTay.Ship
{
    /// <summary>
    /// Cánh cửa bản lề thật (cửa bếp, cửa hông hành lang): tự mở khi người chơi tới gần, E đổi trạng thái và giữ lựa chọn
    /// đó trong vài giây. Prompt đổi theo phía người chơi đứng: ngoài là "Vào bếp", trong là "Ra boong".
    /// Trục +X của cửa hướng ra ngoài. Cánh chỉ chặn đường khi đóng hẳn: lúc đang quay hay đã mở thì tắt collider cánh,
    /// để cánh không hất CharacterController đang đứng trong vùng quét.
    /// </summary>
    public sealed class HingedDoor : MonoBehaviour
    {
        public Transform hinge;
        public InteractTarget target;
        public Collider leaf;
        /// <summary>Góc mở (độ, quanh trục Y của bản lề); dấu chọn chiều quay để cánh áp vào vách bên cạnh.</summary>
        public float openAngle = 90f;
        public float autoRadius = 1.4f;
        public float manualSeconds = 3f;
        public float degreesPerSecond = 180f;
        public string outsideKey = "prompt.galley_door";
        public string insideKey = "prompt.door_out";
        public bool audioEnabled;

        Quaternion _closed;
        float _manualUntil = -1f;
        bool _outside = true;
        bool _heardOpen;

        public bool IsOpen { get; private set; }

        void Awake()
        {
            _closed = hinge != null ? hinge.localRotation : Quaternion.identity;
            if (target != null)
                target.promptKey = outsideKey;
        }

        void OnEnable()
        {
            if (target != null)
                target.Interacted += Toggle;
        }

        void OnDisable()
        {
            if (target != null)
                target.Interacted -= Toggle;
        }

        void Toggle(Interactor who)
        {
            IsOpen = !IsOpen;
            _manualUntil = Time.time + manualSeconds;
        }

        /// <summary>Mở hoặc đóng ngay (director, test); tạm dừng chế độ tự mở như khi bấm E.</summary>
        public void Set(bool open)
        {
            IsOpen = open;
            _manualUntil = Time.time + manualSeconds;
        }

        void Update()
        {
            var rig = PlayerRig.Current;
            if (rig != null)
            {
                var delta = rig.Position - transform.position;
                bool near = Mathf.Abs(delta.y) < 1.6f && delta.x * delta.x + delta.z * delta.z < autoRadius * autoRadius;
                if (Time.time >= _manualUntil)
                    IsOpen = near;
                bool outside = Vector3.Dot(delta, transform.right) > 0f;
                if (near && outside != _outside && target != null)
                {
                    _outside = outside;
                    target.promptKey = outside ? outsideKey : insideKey;
                }
            }
            if (_heardOpen != IsOpen)
            {
                _heardOpen = IsOpen;
                if (audioEnabled)
                    Core.GameRoot.Instance?.Audio.PlaySfxAt(IsOpen ? "SFX/door_open" : "SFX/door_close", transform.position, 0.6f);
            }
            if (hinge == null)
                return;
            var goal = _closed * Quaternion.Euler(0f, IsOpen ? openAngle : 0f, 0f);
            hinge.localRotation = Quaternion.RotateTowards(hinge.localRotation, goal, degreesPerSecond * Time.deltaTime);
            if (leaf == null)
                return;
            bool blocks = !IsOpen && Quaternion.Angle(hinge.localRotation, _closed) < 0.5f;
            if (leaf.enabled != blocks)
                leaf.enabled = blocks;
        }
    }
}
