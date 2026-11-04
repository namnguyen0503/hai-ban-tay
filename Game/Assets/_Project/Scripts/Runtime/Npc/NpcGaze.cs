using HaiBanTay.Player;
using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>
    /// NPC đang nói chuyện với người chơi (TalkGaze bật theo câu thoại): cổ và đầu quay theo mắt người chơi (camera), trộn dần trên
    /// tư thế của clip; người chơi lệch nhiều thì thân quay theo cho thẳng mặt. Câu làm mẫu việc tay ("gaze": "task": gọt, thái), câu
    /// nói với người khác ("gaze": "other"), lúc đang đi, lúc cúi nhìn theo kịch bản (Listen) thì để đầu theo clip. Đang làm bằng tay
    /// (khuấy, bàn sơ chế, tay IK) hay đang ngồi thì chỉ ngoái đầu, thân không quay. Thêm vào NPC khi cần lần đầu (NpcGaze.Of).
    /// Chạy sau Animator, trước NpcAgent.LateUpdate (đồ đang bê theo thân vừa quay) và trước StaffAside, NpcArmIK.
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public sealed class NpcGaze : MonoBehaviour
    {
        public enum Mode { Talk, Task, Other }

        /// <summary>Thời gian trộn vào/ra (giây).</summary>
        public const float BlendSeconds = 0.45f;
        /// <summary>Đầu đuổi theo hướng mới (giây làm mượt, tốc độ quay tối đa độ/s).</summary>
        public const float FollowSeconds = 0.15f, MaxHeadSpeed = 220f;
        /// <summary>Phần xoay cổ gánh (phần còn lại ở đầu).</summary>
        public const float NeckShare = 0.4f;
        /// <summary>Người chơi xa quá thì thôi nhìn theo (m); thân chỉ quay theo khi người chơi trong BodyRange.</summary>
        public const float TalkRange = 8f, BodyRange = 5f;
        /// <summary>Tốc độ quay thân theo người chơi (độ/s): chậm hơn lúc đi cho ra dáng xoay người khi nói.</summary>
        public const float BodyTurnSpeed = 150f;

        NpcAgent _agent;
        NpcHandWork _hands;
        Transform _tf, _head, _neck;
        Vector3 _faceLocal;
        bool _init;
        float _until;
        Mode _mode = Mode.Talk;
        float _weight, _yaw, _pitch, _yawVel, _pitchVel; // _yaw: góc ngang thế giới của hướng nhìn
        bool _turning, _wrote;
        Quaternion _baseHead, _baseNeck, _wroteHead, _wroteNeck;

        /// <summary>Mức đầu đang theo người chơi (0: theo clip, 1: theo hẳn).</summary>
        public float Weight => _weight;
        /// <summary>Đang trong cuộc nói chuyện (câu của mình hoặc của Ba vừa rồi) và câu đó nói với người chơi.</summary>
        public bool Talking => Time.time < _until && _mode == Mode.Talk;
        public Mode CurrentMode => _mode;
        /// <summary>Thân đang quay theo người chơi.</summary>
        public bool BodyTurning => _turning;
        /// <summary>Lệch (độ) giữa hướng mặt sau khi xoay và hướng tới mắt người chơi (test).</summary>
        public float LookError { get; private set; } = 180f;
        /// <summary>Lệch ngang (độ) giữa thân và người chơi (test).</summary>
        public float BodyError { get; private set; } = 180f;
        /// <summary>Hướng mặt trong hệ xương đầu (lấy từ bind pose).</summary>
        public Vector3 FaceLocal => _faceLocal;
        /// <summary>Hướng mặt lấy được từ bind pose (false: lấy theo tư thế lúc thêm component).</summary>
        public bool FromBindPose { get; private set; }

        public static NpcGaze Of(NpcAgent agent)
        {
            if (agent == null) return null;
            var gaze = agent.GetComponent<NpcGaze>();
            return gaze != null ? gaze : agent.gameObject.AddComponent<NpcGaze>();
        }

        /// <summary>Bắt đầu một câu của NPC này: nhìn theo người chơi (Talk) hoặc không (Task, Other) trong seconds giây.</summary>
        public void Engage(float seconds, Mode mode)
        {
            _mode = mode;
            _until = Time.time + seconds;
        }

        /// <summary>Ba đang đáp lời: người vừa nói với Ba tiếp tục nhìn Ba.</summary>
        public void Listen(float seconds)
        {
            if (Talking) _until = Mathf.Max(_until, Time.time + seconds);
        }

        /// <summary>Thôi nhìn ngay (đổi cảnh, kịch bản cần đầu theo clip).</summary>
        public void Release() => _until = 0f;

        /// <summary>Ngửa cổ cho ngẩng mặt lên khi nhìn vật ở cao (độ, vượt giới hạn nhìn người).</summary>
        public const float LookPointPitchUp = 55f;
        Vector3? _lookPoint;

        /// <summary>Kịch bản cho NPC nhìn hẳn vào một điểm (ngẩng nhìn đèn); ưu tiên hơn nhìn người chơi. null: thôi.</summary>
        public void LookAt(Vector3? point) => _lookPoint = point;

        void Awake() => Init();

        void Init()
        {
            if (_init) return;
            _init = true;
            _tf = transform;
            _agent = GetComponent<NpcAgent>();
            _hands = GetComponent<NpcHandWork>();
            var animator = GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isHuman) return;
            _head = animator.GetBoneTransform(HumanBodyBones.Head);
            _neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            if (_head != null) _faceLocal = BindAxis(_head, Vector3.forward);
        }

        /// <summary>
        /// Hướng dir của thân (hệ gốc NPC) tính trong hệ xương bone theo bind pose (tư thế chữ T: mặt nhìn thẳng trước), không theo tư thế
        /// clip đang chạy (clip Talk, Listen làm đầu cúi, ngoái). Không tìm được bind pose thì lấy theo tư thế hiện tại.
        /// </summary>
        Vector3 BindAxis(Transform bone, Vector3 dir)
        {
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;
                int i = System.Array.IndexOf(smr.bones, bone);
                if (i < 0) continue;
                var binds = mesh.bindposes;
                if (i >= binds.Length) continue;
                var m = binds[i].inverse;
                var boneInMesh = Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1));
                var meshInRoot = Quaternion.Inverse(_tf.rotation) * smr.transform.rotation;
                FromBindPose = true;
                return (Quaternion.Inverse(boneInMesh) * (Quaternion.Inverse(meshInRoot) * dir)).normalized;
            }
            return (Quaternion.Inverse(bone.rotation) * (_tf.rotation * dir)).normalized;
        }

        /// <summary>Clip tự định hướng đầu (cúi nhìn tay, thì thầm với nhau, đang ngồi xuống/đứng dậy, cúi nhặt): đầu theo clip.</summary>
        static bool HeadScripted(string state) => state == "Listen" || state == "Talk2" || state == "SitDown" || state == "StandUp"
                                                  || state == "PickUp";

        /// <summary>Đang làm việc tại chỗ: ngoái đầu được, thân không quay (tay đang bám dụng cụ).</summary>
        static bool Working(string state) => state == "WorkTable" || state == "WorkMid" || state == "Stir" || state == "Documents";

        static bool Sitting(string state) => state != null && state.StartsWith("Sit");

        void OnDisable()
        {
            Restore();
            _weight = 0f;
            _turning = false;
        }

        void LateUpdate()
        {
            Init();
            Restore();
            var rig = PlayerRig.Current;
            if (_head == null || _agent == null || rig == null)
            {
                _weight = 0f;
                return;
            }
            var eye = rig.cam != null ? rig.cam.transform.position : rig.Position + Vector3.up * 1.6f;
            var toPlayer = rig.Position - _tf.position;
            float dy = Mathf.Abs(toPlayer.y);
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;
            string state = _agent.State;
            bool working = _agent.keepAnimated || Working(state) || (_hands != null && !string.IsNullOrEmpty(_hands.Activity))
                           || _agent.Carrying != null;
            bool point = _lookPoint.HasValue && !_agent.Walking && _agent.Animated;
            if (point) eye = _lookPoint.Value;
            bool talk = point || (Talking && !_agent.Walking && !HeadScripted(state) && dist < TalkRange && dy < 2.5f && _agent.Animated);

            // Thân quay theo cho thẳng mặt (không quay khi ngồi, khi đang làm tay, khi người chơi ở xa, khi kịch bản cho nhìn vật).
            float bodyYaw = _tf.eulerAngles.y;
            BodyError = dist > 0.05f ? Mathf.Abs(Mathf.DeltaAngle(bodyYaw, Mathf.Atan2(toPlayer.x, toPlayer.z) * Mathf.Rad2Deg)) : 0f;
            _turning = talk && !point && !working && !Sitting(state) && dist < BodyRange && dist > 0.3f && GazeLogic.BodyTurning(_turning, BodyError);
            if (_turning)
            {
                _agent.TrackTurn(toPlayer, BodyTurnSpeed);
                bodyYaw = _tf.eulerAngles.y;
            }

            GazeLogic.Angles(eye - _head.position, bodyYaw, out float wantYaw, out float wantPitch);
            bool reachable = GazeLogic.Clamp(ref wantYaw, ref wantPitch);
            if (point)
            {
                // Nhìn vật ở cao: cho ngửa quá giới hạn nhìn người, vật lệch quá tầm thì vẫn nhìn ở mức tối đa.
                GazeLogic.Angles(eye - _head.position, bodyYaw, out _, out wantPitch);
                wantPitch = Mathf.Clamp(wantPitch, -GazeLogic.MaxPitchDown, LookPointPitchUp);
                reachable = true;
            }
            _weight = Mathf.MoveTowards(_weight, talk && reachable ? 1f : 0f, Time.deltaTime / BlendSeconds);
            // Làm mượt theo hướng thế giới (_yaw là góc ngang thế giới): thân quay bên dưới thì đầu vẫn ghim vào Ba, không bị thân kéo
            // đi rồi đuổi lại (đo 15/10: làm mượt theo hệ thân, đầu quay vụt 590°/s khi thân quay theo Ba).
            float wantWorld = bodyYaw + wantYaw;
            if (_weight <= 0f)
            {
                _yaw = wantWorld;
                _pitch = wantPitch;
                _yawVel = _pitchVel = 0f;
                LookError = 180f;
                return;
            }
            if (Time.deltaTime > 0f)
            {
                _yaw = Mathf.SmoothDampAngle(_yaw, wantWorld, ref _yawVel, FollowSeconds, MaxHeadSpeed);
                _pitch = Mathf.SmoothDampAngle(_pitch, wantPitch, ref _pitchVel, FollowSeconds, MaxHeadSpeed);
            }
            float yaw = Mathf.Clamp(Mathf.DeltaAngle(bodyYaw, _yaw), -GazeLogic.MaxYaw, GazeLogic.MaxYaw);

            float w = Mathf.SmoothStep(0f, 1f, _weight);
            var face = _head.rotation * _faceLocal;
            var delta = GazeLogic.Delta(face, GazeLogic.Direction(bodyYaw, yaw, _pitch));
            var headAnim = _head.rotation;
            _baseHead = _head.localRotation;
            if (_neck != null)
            {
                _baseNeck = _neck.localRotation;
                _neck.rotation = Quaternion.Slerp(Quaternion.identity, delta, w * NeckShare) * _neck.rotation;
                _wroteNeck = _neck.localRotation;
            }
            _head.rotation = Quaternion.Slerp(Quaternion.identity, delta, w) * headAnim;
            _wroteHead = _head.localRotation;
            _wrote = true;
            LookError = Vector3.Angle(_head.rotation * _faceLocal, eye - _head.position);
        }

        /// <summary>
        /// Animator không ghi lại xương khung này (NPC ngoài khung hình, CrowdLod tắt Animator): trả cổ, đầu về tư thế clip trước khi xoay,
        /// không cộng dồn xoay qua các khung.
        /// </summary>
        void Restore()
        {
            if (!_wrote) return;
            _wrote = false;
            if (_head.localRotation != _wroteHead || (_neck != null && _neck.localRotation != _wroteNeck)) return;
            _head.localRotation = _baseHead;
            if (_neck != null) _neck.localRotation = _baseNeck;
        }
    }
}
