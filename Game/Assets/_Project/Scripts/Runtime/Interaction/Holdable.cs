using UnityEngine;

namespace HaiBanTay.Interaction
{
    /// <summary>Vật cầm được bằng một hoặc hai tay. Khi cầm: tắt va chạm, bám theo điểm giữ dưới camera.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class Holdable : MonoBehaviour, IInteractable
    {
        public bool twoHanded;
        public string kind = "item";
        public float followSpeed = 12f;
        public Transform leftGrip, rightGrip;
        public Vector3 heldOffset;
        /// <summary>Góc cầm cố định (thùng cá nghiêng mặt về phía người cầm).</summary>
        public Vector3 heldEuler;
        /// <summary>Xách một tay bên hông (xô, kẹp đá): vật treo dưới bàn tay phải, theo hướng thân, không theo góc cúi ngẩng
        /// của camera. hangGrip là chỗ bàn tay nắm (quai xô, cán kẹp).</summary>
        public bool hang;
        public Transform hangGrip;

        Rigidbody _rb;
        Collider[] _colliders;
        Renderer[] _renderers;
        Vector3[] _visualPositions, _visualStartPositions;
        Quaternion[] _visualRotations, _visualStartRotations;
        Vector3 _pickPosition;
        Quaternion _pickRotation;
        float _transition;
        bool _placing;
        bool _restoreInterpolation;
        RigidbodyInterpolation _heldInterpolation;
        const float ShoulderBlendSeconds = 0.25f;

        /// <summary>Chỉ bao nặng dùng vai phải; đồ nhẹ và vật hai tay khác giữ cách cầm cũ.</summary>
        public bool ShoulderCarried => twoHanded && (kind == "sack" || kind == "meat");

        // Tảng thịt ngắn hơn bao: mặt thịt nằm sát bàn tay đỡ, không lơ lửng trước vai.
        Vector3 ShoulderOffset => kind == "meat" ? new Vector3(0f, -0.42f, 0.22f) : Vector3.zero;

        public bool Locked { get; set; }
        /// <summary>Đang làm động tác trên tay (PourMotion): E không thả vật.</summary>
        public bool Busy { get; set; }
        /// <summary>Đang cầm mà có giá trị: vật theo đúng pose thế giới này (PourMotion), không theo điểm giữ.</summary>
        public (Vector3 position, Quaternion rotation)? WorldPose { get; set; }
        public bool IsHeld { get; private set; }
        public Rigidbody Body => _rb;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _colliders = GetComponentsInChildren<Collider>(true);
            _renderers = System.Array.FindAll(GetComponentsInChildren<Renderer>(true), r => r.transform != transform);
            int count = _renderers.Length;
            _visualPositions = new Vector3[count];
            _visualRotations = new Quaternion[count];
            _visualStartPositions = new Vector3[count];
            _visualStartRotations = new Quaternion[count];
            for (int i = 0; i < count; i++)
            {
                _visualPositions[i] = _renderers[i].transform.localPosition;
                _visualRotations[i] = _renderers[i].transform.localRotation;
            }
            // Xách: chỗ nắm (quai) trùng điểm treo bên hông, vật thõng xuống dưới bàn tay.
            if (hang && hangGrip != null)
                heldOffset = -transform.InverseTransformPoint(hangGrip.position);
        }

        MeshFilter[] _shape;

        /// <summary>Điểm cực (thế giới) của các phần đang hiện của vật: người cầm dùng để giữ vật ngoài thân mình.</summary>
        public void ShapePoints(System.Collections.Generic.List<Vector3> into)
        {
            _shape ??= GetComponentsInChildren<MeshFilter>(true);
            into.Clear();
            foreach (var f in _shape)
            {
                if (f == null || !f.gameObject.activeInHierarchy || !f.TryGetComponent<MeshRenderer>(out var r) || !r.enabled) continue;
                var t = f.transform;
                foreach (var p in Player.HullPoints.Of(f.sharedMesh)) into.Add(t.TransformPoint(p));
            }
        }

        public string GetPromptKey(Interactor who)
        {
            if (!Locked && !IsHeld && who != null && who.Holding == null && who.Tool != Player.Tool.None)
                // Đang cầm dụng cụ có móc: E treo dụng cụ lại rồi nhấc vật (không bắt người chơi tự đi treo).
                return ToolHook.For(who.Tool) != null ? "prompt.pick_hang_tool" : "prompt.hands_busy";
            if (!CanInteract(who))
                return null;
            return twoHanded ? "prompt.pick_two_hands" : "prompt.pick";
        }

        public bool CanInteract(Interactor who) => !Locked && !IsHeld && who != null && who.Holding == null && !who.WorkCarrying &&
                                                   (who.Tool == Player.Tool.None || ToolHook.For(who.Tool) != null);

        public void Interact(Interactor who)
        {
            if (who.Tool != Player.Tool.None)
                ToolHook.For(who.Tool)?.ReturnFrom(who);
            who.Pick(this);
        }

        /// <summary>Bật/tắt collider của vật (vật nằm yên làm việc tại chỗ, để vùng tương tác khác nhận tia nhìn).</summary>
        public void SetCollidersEnabled(bool on)
        {
            if (_rb == null)
                Awake();
            foreach (var c in _colliders)
                c.enabled = on;
        }

        public void AttachTo(Transform point)
        {
            if (_rb == null)
                Awake();
            if (_placing) RestoreVisuals();
            _placing = false;
            IsHeld = true;
            _rb.isKinematic = true;
            // Bao theo camera bằng Transform: nội suy pose vật lý cũ gây trễ/giật khi nhìn lên xuống.
            if (!_restoreInterpolation)
            {
                _heldInterpolation = _rb.interpolation;
                _restoreInterpolation = true;
                _rb.interpolation = RigidbodyInterpolation.None;
            }
            foreach (var c in _colliders)
                c.enabled = false;
            transform.SetParent(point, true);
            // Giữ chuyển tiếp nhấc bao, nhưng không để bao cách điểm giữ quá 14 cm.
            WorldPose = null;
            Busy = false;
            var pickup = ShoulderCarried ? ShoulderOffset + Vector3.ClampMagnitude(transform.localPosition - ShoulderOffset, 0.14f) : heldOffset;
            transform.SetLocalPositionAndRotation(pickup, ShoulderCarried ? Quaternion.identity : Quaternion.Euler(heldEuler));
            _pickPosition = transform.localPosition;
            _pickRotation = transform.localRotation;
            _transition = 0f;
        }

        /// <summary>Thả ra tại vị trí cho trước. physics = false: nằm yên (đặt vào DropZone).</summary>
        public void Detach(Vector3 position, Quaternion rotation, bool physics)
        {
            if (_rb == null)
                Awake();
            // Pose vật lý và sự kiện đặt vẫn tức thì; chỉ mesh đi từ vai xuống trong 0,25 giây.
            bool blend = Application.isPlaying && IsHeld && ShoulderCarried;
            if (_placing) RestoreVisuals();
            if (blend)
                for (int i = 0; i < _renderers.Length; i++)
                {
                    _visualStartPositions[i] = _renderers[i].transform.position;
                    _visualStartRotations[i] = _renderers[i].transform.rotation;
                }
            _placing = blend;
            _transition = 0f;
            IsHeld = false;
            WorldPose = null;
            Busy = false;
            transform.SetParent(null, true);
            transform.SetPositionAndRotation(position, rotation);
            // Rigidbody có nội suy: phải đặt cả pose vật lý, không thì bước nội suy sau kéo vật về chỗ tay cũ (xô lơ lửng).
            _rb.position = position;
            _rb.rotation = rotation;
            foreach (var c in _colliders)
                c.enabled = true;
            _rb.isKinematic = !physics;
            if (physics)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            if (_restoreInterpolation)
            {
                _rb.interpolation = _heldInterpolation;
                _restoreInterpolation = false;
            }
            if (blend) BlendPlacement(0f);
        }

        void OnDisable()
        {
            if (_placing) RestoreVisuals();
            _placing = false;
        }

        void RestoreVisuals()
        {
            if (_renderers == null) return;
            for (int i = 0; i < _renderers.Length; i++)
                _renderers[i].transform.SetLocalPositionAndRotation(_visualPositions[i], _visualRotations[i]);
        }

        void BlendPlacement(float t)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                var visual = _renderers[i].transform;
                var parent = visual.parent;
                visual.SetPositionAndRotation(Vector3.Lerp(_visualStartPositions[i], parent.TransformPoint(_visualPositions[i]), t),
                    Quaternion.Slerp(_visualStartRotations[i], parent.rotation * _visualRotations[i], t));
            }
        }

        void LateUpdate()
        {
            if (_placing)
            {
                _transition += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, _transition / ShoulderBlendSeconds);
                BlendPlacement(t);
                if (_transition >= ShoulderBlendSeconds)
                {
                    _placing = false;
                    RestoreVisuals();
                }
            }
            if (!IsHeld)
                return;
            if (WorldPose.HasValue)
            {
                transform.SetPositionAndRotation(WorldPose.Value.position, WorldPose.Value.rotation);
                return;
            }
            if (ShoulderCarried)
            {
                _transition += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, _transition / ShoulderBlendSeconds);
                transform.SetLocalPositionAndRotation(Vector3.Lerp(_pickPosition, ShoulderOffset, t),
                    Quaternion.Slerp(_pickRotation, Quaternion.identity, t));
                return;
            }
            // Đồ cầm giữ thăng bằng theo hướng thân: khung tay ngả theo góc cúi (tới 50°) thì sọt, thùng, khay nghiêng theo, miệng sọt
            // chĩa ra trước, cúi nhìn chỉ thấy đáy ngoài như nhìn xuyên (ghi chú 15/10). Chỗ cầm vẫn theo khung tay.
            var parent = transform.parent;
            var flat = parent != null ? Vector3.ProjectOnPlane(parent.forward, Vector3.up) : Vector3.zero;
            if (flat.sqrMagnitude < 1e-6f)
            {
                transform.SetLocalPositionAndRotation(heldOffset, Quaternion.Euler(heldEuler));
                return;
            }
            transform.SetPositionAndRotation(parent.TransformPoint(heldOffset), Quaternion.LookRotation(flat) * Quaternion.Euler(heldEuler));
        }
    }
}
