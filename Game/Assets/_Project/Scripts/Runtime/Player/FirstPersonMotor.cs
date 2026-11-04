using HaiBanTay.Core;
using HaiBanTay.Npc;
using UnityEngine;

namespace HaiBanTay.Player
{
    /// <summary>CharacterController: đi 2,0 m/s; mang đồ nặng 1,3 m/s; không nhảy, không chạy; head bob nhẹ.</summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonMotor : MonoBehaviour
    {
        public float walkSpeed = 2.0f;
        public float carrySpeed = 1.3f;
        public float gravity = -9.81f;
        /// <summary>tan(50°) ≈ 1,19: tốc độ ép xuống theo tốc độ ngang để bám dốc tới giới hạn của CharacterController.</summary>
        const float GroundSnap = 1.2f;
        public Transform head;
        public float headHeight = 1.62f;
        public float bobAmplitude = 0.022f;
        public float bobStepsPerSecond = 1.7f;

        CharacterController _cc;
        PlayerRig _rig;
        GameInput _input;
        float _vy;
        float _bobPhase;
        float _bobWeight;
        float _extraHead;
        int _lastStep;
        int _holdableLayer;
        bool _onItem;
        Vector3 _itemPush;
        /// <summary>Hướng thử đẩy ra khỏi đồ (độ quanh trục Y so với hướng ra xa tâm đồ): kẹt vách thì đẩy dọc vách.</summary>
        const float PushMoved = 0.06f;
        static readonly float[] PushTurns = { 0f, 70f, -70f, 140f, -140f, 180f };
        static readonly RaycastHit[] FloorHits = new RaycastHit[8];

        /// <summary>
        /// Phát sau mỗi lần dịch chuyển tức thời. Teleport tắt CharacterController nên PhysX không gửi OnTriggerExit cho
        /// trigger vừa rời; trigger nào cần biết người chơi còn ở trong (ShipZone) tự kiểm lại.
        /// </summary>
        public static event System.Action Teleported;

        public bool Carrying { get; set; }
        /// <summary>Hệ số tốc độ do mệt (BodyState đặt), 1 = bình thường.</summary>
        public float SpeedMultiplier { get; set; } = 1f;
        public bool MovementLocked { get; set; }
        /// <summary>Trần tốc độ (m/s), ví dụ khi kéo lê chảo đồng. Vô cực = không giới hạn.</summary>
        public float SpeedCap { get; set; } = float.PositiveInfinity;
        /// <summary>Độ cao thêm cho đầu (đứng trên ghế đẩu +0,35 m).</summary>
        public float ExtraHeadHeight { get; set; }
        public float CurrentSpeed { get; private set; }
        public Vector3 Velocity { get; private set; }

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _rig = GetComponent<PlayerRig>();
            _holdableLayer = LayerMask.NameToLayer("Holdable");
        }

        void Start()
        {
            _input = GameRoot.Instance != null ? GameRoot.Instance.Input : null;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Vector2 m = (_input != null && !MovementLocked) ? _input.Move : Vector2.zero;
            var move = transform.right * m.x + transform.forward * m.y;
            float speed = (Carrying ? carrySpeed : walkSpeed) * Mathf.Clamp(SpeedMultiplier, 0.3f, 1.5f);
            speed = Mathf.Min(speed, SpeedCap);
            var horizontal = Vector3.ClampMagnitude(move, 1f) * speed;

            // Đang chạm sàn: ép xuống đủ mạnh để bám dốc cầu thang (tới slopeLimit 50°) khi đi xuống, không nảy bậc.
            if (_cc.isGrounded && _vy < 0f)
                _vy = -Mathf.Max(1f, horizontal.magnitude * GroundSnap);
            _vy += gravity * dt;
            if (_cc.enabled)
            {
                // Đồ NPC đang cầm/vác (bao trên vai, xẻng) không chặn capsule kịp trước ngực: bỏ phần bước đi vào đồ (QA-0150).
                var step = NpcAgent.LimitPlayerStep(_rig, horizontal * dt);
                horizontal = dt > 0f ? step / dt : horizontal;
                _cc.Move(step + Vector3.up * (_vy * dt));
                if (_onItem && LiftedAboveFloor())
                {
                    // Capsule gối lên mép hộp va chạm của đồ (xô tro, nồi, chảo) hay của NPC: đẩy người ra khỏi đồ và thả xuống sàn ngay trong
                    // khung này, camera không bao giờ tự nhô lên (bot ngày 1/3/5: Ba bị xô tro nâng 0,19–0,39 m ở lối lấy chổi, đứng sát vách
                    // nên hướng ra xa xô bị vách chặn: thử các hướng dọc vách).
                    for (int i = 0; i < PushTurns.Length; i++)
                    {
                        var before = transform.position;
                        _cc.Move(Quaternion.Euler(0f, PushTurns[i], 0f) * _itemPush + Vector3.down * 0.6f);
                        var moved = transform.position - before;
                        moved.y = 0f;
                        // Đẩy được thì dừng, khung sau đẩy tiếp cùng hướng; chỉ đổi hướng khi vách chặn (các hướng liền nhau triệt tiêu nhau).
                        if (!LiftedAboveFloor() || moved.sqrMagnitude > PushMoved * PushMoved)
                            break;
                    }
                    _vy = 0f;
                }
            }
            _onItem = false;
            Velocity = horizontal;
            CurrentSpeed = horizontal.magnitude;

            UpdateHead(dt);
        }

        // Chân người cao hơn mặt sàn bên dưới (không tính đồ cầm được và NPC) là đang đứng lên đồ hay lên người. Chạm sát hông đồ ở sàn (rổ
        // than ở hầm than, pháp tuyến ~0,2) thì chân vẫn ngang sàn nên không bị đẩy.
        bool LiftedAboveFloor()
        {
            int n = Physics.RaycastNonAlloc(transform.position + Vector3.up * 0.3f, Vector3.down, FloorHits, 1.5f, ~0, QueryTriggerInteraction.Ignore);
            float floor = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var c = FloorHits[i].collider;
                if (c.gameObject.layer == _holdableLayer || c.transform.IsChildOf(transform) || c.GetComponentInParent<NpcAgent>() != null)
                    continue;
                floor = Mathf.Max(floor, FloorHits[i].point.y);
            }
            return !float.IsNegativeInfinity(floor) && transform.position.y - floor > 0.1f;
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // Đồ có Rigidbody (xô, nồi, chảo, rổ) và NPC không phải sàn: chạm từ phía trên nghĩa là có thể đang đứng lên đồ; LiftedAboveFloor xác nhận.
            var body = hit.collider.attachedRigidbody;
            if (body == null || hit.collider.isTrigger || hit.normal.y < 0.05f)
                return;
            if (hit.collider.gameObject.layer == _holdableLayer)
            {
                // Đồ đang cầm hay đang đổ (xô tro đổ ở máng) đi theo tay người chơi: chạm nó không phải đứng lên đồ.
                var item = hit.collider.GetComponentInParent<HaiBanTay.Interaction.Holdable>();
                if (item == null || item.IsHeld || item.Busy)
                    return;
            }
            else if (hit.collider.GetComponentInParent<NpcAgent>() == null)
                return;
            var away = transform.position - hit.collider.bounds.center;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f)
                away = -hit.moveDirection;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-6f)
                return;
            _itemPush = away.normalized * 0.12f;
            _onItem = true;
        }

        void UpdateHead(float dt)
        {
            if (head == null)
                return;
            float moving = Mathf.Clamp01(CurrentSpeed / walkSpeed);
            _bobWeight = Mathf.MoveTowards(_bobWeight, moving, dt * 4f);
            _bobPhase += dt * bobStepsPerSecond * Mathf.PI * (0.5f + 0.5f * moving);
            float amp = bobAmplitude * (Carrying ? 1.6f : 1f) * _bobWeight * GameSettings.Current.HeadBob;
            _extraHead = Mathf.MoveTowards(_extraHead, ExtraHeadHeight, dt * 1.2f);
            // Mắt không bao giờ dời ra trước hay lùi sau theo tư thế làm việc (17/10); thân tự lùi dưới camera (PlayerBody).
            head.localPosition = new Vector3(Mathf.Cos(_bobPhase) * amp * 0.5f,
                headHeight + _extraHead + Mathf.Abs(Mathf.Sin(_bobPhase)) * amp, 0f);
            UpdateFootsteps();
        }

        /// <summary>Mỗi nửa chu kỳ bob là một bước: dò collider dưới chân rồi phát tiếng gỗ, sắt hay đá.</summary>
        void UpdateFootsteps()
        {
            int step = Mathf.FloorToInt(_bobPhase / Mathf.PI);
            if (step == _lastStep)
                return;
            _lastStep = step;
            if (_bobWeight < 0.5f || !_cc.isGrounded)
                return;
            var audio = GameRoot.Instance != null ? GameRoot.Instance.Audio : null;
            if (audio == null)
                return;
            var kind = Audio.FootstepKind.Wood;
            if (Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out var hit, 0.8f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                var t = hit.collider.transform;
                kind = Audio.FootstepSurface.Classify(t.name, t.parent != null ? t.parent.name : null);
            }
            audio.PlaySfx(Audio.FootstepSurface.Key(kind, step), Carrying ? 0.45f : 0.3f);
        }

        /// <summary>Dịch chuyển tức thời (sau màn đen). yaw tính theo độ.</summary>
        public void Teleport(Vector3 position, float yaw)
        {
            bool was = _cc.enabled;
            _cc.enabled = false;
            // Nhấc nhẹ khỏi sàn: capsule chạm sàn sẵn mà khung sau dài (sau chụp màn hình, nạp cảnh) thì có thể rơi xuyên sàn.
            transform.SetPositionAndRotation(position + Vector3.up * 0.05f, Quaternion.Euler(0f, yaw, 0f));
            _cc.enabled = was;
            Physics.SyncTransforms();
            _vy = 0f;
            var look = GetComponent<LookController>();
            if (look != null)
                look.SetAngles(yaw, 0f);
            Teleported?.Invoke();
        }
    }
}
