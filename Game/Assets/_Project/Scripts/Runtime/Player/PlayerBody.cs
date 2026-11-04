using HaiBanTay.Ship;
using UnityEngine;
using UnityEngine.Rendering;

namespace HaiBanTay.Player
{
    public enum TrangPhuc { PhuBep, Thuong }

    /// <summary>Thân góc nhìn thứ nhất: clip chân theo dịch chuyển thật, IK nối cổ tay sau camera và HandsRig.</summary>
    [DefaultExecutionOrder(11000)]
    public sealed class PlayerBody : MonoBehaviour
    {
        public PlayerRig rig;
        public Animator animator;
        public SkinnedMeshRenderer skin;
        public PlayerApron apron;
        public int headSubmesh;
        public Mesh phuBepMesh, thuongMesh;
        public Material[] phuBepMaterials, thuongMaterials;
        public TrangPhuc Outfit { get; private set; }

        /// <summary>Đổi thân và cổ tay cùng một lượt; bộ phụ bếp gốc giữ nguyên.</summary>
        public void SetOutfit(TrangPhuc outfit)
        {
            Outfit = outfit;
            skin.sharedMesh = outfit == TrangPhuc.Thuong ? thuongMesh : phuBepMesh;
            skin.sharedMaterials = outfit == TrangPhuc.Thuong ? thuongMaterials : phuBepMaterials;
            apron.gameObject.SetActive(outfit == TrangPhuc.PhuBep);
            BuildCollar();
            BuildShell();
            var shirt = skin.sharedMaterials[0];
            foreach (var pose in rig.hands.leftPoseMeshes)
                SetSleeves(pose, shirt);
            foreach (var pose in rig.hands.rightPoseMeshes)
                SetSleeves(pose, shirt);
            if (rig.hands.riggedLeft != null) SetSleeves(rig.hands.riggedLeft, shirt);
            if (rig.hands.riggedRight != null) SetSleeves(rig.hands.riggedRight, shirt);
        }

        static void SetSleeves(GameObject pose, Material shirt)
        {
            foreach (var renderer in pose.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.name.StartsWith("Sleeve")) renderer.sharedMaterial = shirt;
        }
        [Tooltip("Quãng đường một vòng Walk, đo từ bước chân clip Rocketbox (m).")]
        public float strideMetres = 1.43023f;
        public float runStrideMetres = 1.676833f;
        [Tooltip("Độ gập hông tối đa khi cúi nhìn xuống (độ) để thấy ống quần.")]
        public float lookDownHipFlexDegrees = 30f;

        Transform _hips, _chest, _head, _leftUpper, _leftLower, _leftWrist, _rightUpper, _rightLower, _rightWrist;
        Transform _leftThigh, _leftCalf, _leftFoot, _rightThigh, _rightCalf, _rightFoot;
        Vector3 _leftElbowRest, _leftWristRest, _rightElbowRest, _rightWristRest;
        Quaternion _leftHandFrame, _rightHandFrame;
        Quaternion _leftForearmFrame, _rightForearmFrame;
        CharacterController _controller;
        // Vòng cổ áo: các đỉnh thấp nhất của phần đầu bị ẩn với camera chính, lưu theo xương chính của từng đỉnh.
        Transform[] _collarBones = new Transform[0];
        Vector3[] _collarLocal = new Vector3[0];
        struct Cuff { public Transform mesh; public Vector3 position; public Quaternion rotation; }
        Cuff[] _leftCuffs, _rightCuffs;
        const float ShoeAnkleHeight = 0.10f; // Giày kín: đế thấp hơn xương mắt cá khoảng 9,7 cm trong mesh đã skin.
        Vector3 _previousPosition;
        float _gaitYaw;
        int _state;
        MaterialPropertyBlock _headProperties;
        static readonly int Idle = Animator.StringToHash("Idle");
        static readonly int Walk = Animator.StringToHash("Walk");
        static readonly int Run = Animator.StringToHash("Run");
        static readonly int Strafe = Animator.StringToHash("Strafe");
        static readonly int Backward = Animator.StringToHash("Backward");
        static readonly int Stairs = Animator.StringToHash("Stairs");
        static readonly int Sit = Animator.StringToHash("Squat");

        public Transform LeftWrist => _leftWrist;
        public Transform RightWrist => _rightWrist;
        public Transform Shoulder(bool left) => left ? _leftUpper : _rightUpper;
        /// <summary>Dài cánh tay (vai → khuỷu → cổ tay) lúc nghỉ, m.</summary>
        public float ArmLength(bool left) => left ? _leftArmLength : _rightArmLength;
        float _leftArmLength, _rightArmLength;
        public float ActualSpeed { get; private set; }
        public float ArmStretch { get; private set; } = 1f;

        void Awake()
        {
            _headProperties = new MaterialPropertyBlock();
            if (GetComponent<ArmsVisibility>() == null) gameObject.AddComponent<ArmsVisibility>();
            RenderPipelineManager.beginCameraRendering += ConfigureHeadForCamera;
            _controller = rig.GetComponent<CharacterController>();
            _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            _chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            _head = animator.GetBoneTransform(HumanBodyBones.Head);
            _leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _leftWrist = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _rightUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _rightLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _rightWrist = animator.GetBoneTransform(HumanBodyBones.RightHand);
            _leftThigh = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            _leftCalf = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            _leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            _rightThigh = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            _rightCalf = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            _rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            _leftArmLength = (Vector3.Distance(_leftUpper.position, _leftLower.position) + Vector3.Distance(_leftLower.position, _leftWrist.position)) * ArmGain;
            _rightArmLength = (Vector3.Distance(_rightUpper.position, _rightLower.position) + Vector3.Distance(_rightLower.position, _rightWrist.position)) * ArmGain;
            _leftElbowRest = _leftLower.localPosition;
            _leftWristRest = _leftWrist.localPosition;
            _rightElbowRest = _rightLower.localPosition;
            _rightWristRest = _rightWrist.localPosition;
            // Khung tay FBX: +Z hướng ngón, +Y mu tay. Animator có thể đã đặt Idle trước Awake;
            // bindposes của mesh mới là khung mà vải được gán trọng số, không dùng tư thế xương hiện tại.
            var bindposes = skin.sharedMesh.bindposes;
            Quaternion HandFrame(Transform elbow, Transform wrist)
            {
                var elbowBind = bindposes[System.Array.IndexOf(skin.bones, elbow)].inverse;
                var wristBind = bindposes[System.Array.IndexOf(skin.bones, wrist)].inverse;
                var direction = (Vector3)(wristBind.GetColumn(3) - elbowBind.GetColumn(3));
                return Quaternion.Inverse(Quaternion.LookRotation(direction.normalized,
                    skin.transform.InverseTransformDirection(transform.up))) * wristBind.rotation;
            }
            _leftHandFrame = HandFrame(_leftLower, _leftWrist);
            _rightHandFrame = HandFrame(_rightLower, _rightWrist);
            Quaternion ForearmFrame(Transform elbow, Transform wrist) =>
                Quaternion.Inverse(bindposes[System.Array.IndexOf(skin.bones, elbow)].inverse.rotation)
                * bindposes[System.Array.IndexOf(skin.bones, wrist)].inverse.rotation;
            _leftForearmFrame = ForearmFrame(_leftLower, _leftWrist);
            _rightForearmFrame = ForearmFrame(_rightLower, _rightWrist);
            Cuff[] Cuffs(GameObject[] poses, Transform hand)
            {
                var result = new System.Collections.Generic.List<Cuff>();
                foreach (var pose in poses)
                    foreach (var renderer in pose.GetComponentsInChildren<MeshRenderer>(true))
                        if (renderer.name.StartsWith("Sleeve")) result.Add(new Cuff { mesh = renderer.transform,
                            position = hand.InverseTransformPoint(renderer.transform.position),
                            rotation = Quaternion.Inverse(hand.rotation) * renderer.transform.rotation });
                return result.ToArray();
            }
            BuildCollar();
            BuildShell();
            // Bàn tay có xương ngón cũng mang ống tay áo riêng.
            GameObject[] WithRigged(GameObject[] poses, GameObject rigged)
            {
                if (rigged == null) return poses;
                var all = new GameObject[poses.Length + 1];
                poses.CopyTo(all, 0);
                all[poses.Length] = rigged;
                return all;
            }
            _leftCuffs = Cuffs(WithRigged(rig.hands.leftPoseMeshes, rig.hands.riggedLeft), rig.hands.left);
            _rightCuffs = Cuffs(WithRigged(rig.hands.rightPoseMeshes, rig.hands.riggedRight), rig.hands.right);
            _previousPosition = rig.Position;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            ShipZone.RegionChanged += SetRegion;
            SetRegion(ShipZone.CurrentRegion);
        }

        void OnDestroy()
        {
            ShipZone.RegionChanged -= SetRegion;
            RenderPipelineManager.beginCameraRendering -= ConfigureHeadForCamera;
        }

        void ConfigureHeadForCamera(ScriptableRenderContext context, Camera camera)
        {
            // Nhận diện camera, không dùng khoảng cách: gương/camera ngoài sát đầu vẫn thấy nhân vật.
            _headProperties.SetFloat("_HidePlayerHead", camera == rig.cam ? 1f : 0f);
            skin.SetPropertyBlock(_headProperties);
        }

        void SetRegion(ShipRegion region)
        {
            bool inside = region == ShipRegion.Interior;
            skin.renderingLayerMask = inside ? WorldRenderingLayers.Interior : WorldRenderingLayers.Exterior;
            skin.lightProbeUsage = inside ? LightProbeUsage.BlendProbes : LightProbeUsage.Off;
        }

        void Update()
        {
            // Motor chạy trước: đo vị trí thật để chân ngừng khi đụng tường, bỏ dịch chuyển tức thời.
            var delta = rig.Position - _previousPosition;
            _previousPosition = rig.Position;
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            ActualSpeed = delta.magnitude < 0.5f ? new Vector2(delta.x, delta.z).magnitude / dt : 0f;
            bool seated = rig.motor.ExtraHeadHeight < -0.3f;
            float direction = Mathf.Atan2(Vector3.Dot(delta, rig.transform.right), Vector3.Dot(delta, rig.transform.forward)) * Mathf.Rad2Deg;
            bool moving = ActualSpeed > 0.035f;
            int state = seated ? Sit : !moving ? Idle : Mathf.Abs(direction) > 135f ? Backward
                : Mathf.Abs(delta.y) / dt > 0.3f && _controller.isGrounded ? Stairs
                : Mathf.Abs(direction) > 45f ? Strafe : ActualSpeed > 2.3f ? Run : Walk;
            if (_state != state)
            {
                animator.CrossFade(state, 0.12f);
                _state = state;
            }
            // Walk lặp 1 vòng/stride; đi nhanh, mang nặng, lùi đều giữ quãng bước theo controller.
            var info = animator.GetCurrentAnimatorStateInfo(0);
            animator.speed = moving && !seated ? Mathf.Clamp(ActualSpeed * Mathf.Max(info.length, 0.6f) / (state == Run ? runStrideMetres : strideMetres), 0.05f, 4f) : 1f;
            float wantYaw = moving && state != Backward && !seated ? direction : 0f;
            _gaitYaw = Mathf.MoveTowardsAngle(_gaitYaw, wantYaw, dt * 540f);
        }

        void LateUpdate()
        {
            if (Time.deltaTime > 0f) AlignBodyAndHands();
        }

        /// <summary>Gọi sau khi camera/tay đổi tư thế; giữ đúng cả khung motif, run tay và nghiêng bao.</summary>
        public void AlignBodyAndHands()
        {
            float down = Mathf.InverseLerp(35f, 80f, rig.look.Pitch);
            transform.localPosition = new Vector3(0f, rig.hands.Working ? rig.head.localPosition.y - rig.motor.headHeight : Mathf.Max(0f, rig.head.localPosition.y - rig.motor.headHeight),
                rig.hands.Working ? -.04f : Mathf.Lerp(-0.09f, -0.28f, down));
            transform.localRotation = Quaternion.Euler(0f, _gaitYaw, 0f);
            // Xương hông/chân hướng bước đi, ngực và vai vẫn hướng camera khi đi ngang hoặc lùi.
            _chest.rotation = Quaternion.AngleAxis(-_gaitYaw, rig.transform.up) * _chest.rotation;
            if (rig.motor.ExtraHeadHeight < -0.3f)
                transform.position += Vector3.up * (rig.head.position.y - (_head.position.y + 0.11f));

            // Cúi nhìn xuống: hơi bước chân ra trước (gập hông, gối thẳng lại) để đùi/gối/ống quần lộ ra dưới gấu tạp dề.
            float flex = rig.motor.ExtraHeadHeight < -0.3f ? 0f : lookDownHipFlexDegrees * down;
            if (flex > 0.01f)
            {
                // Gập đùi rút ngắn chiều cao chân: hạ thân đúng phần cos mất đi để giữ đế trên sàn,
                // thay vì nâng cả hai chân rồi bị GroundFoot hiểu nhầm là pha nhấc chân của bước đi.
                float thighLength = (Vector3.Distance(_leftThigh.position, _leftCalf.position)
                    + Vector3.Distance(_rightThigh.position, _rightCalf.position)) * 0.5f;
                transform.position -= rig.transform.up * (thighLength * (1f - Mathf.Cos(flex * Mathf.Deg2Rad)));
                var right = rig.transform.right;
                foreach (var (thigh, calf, foot, side) in new[]
                    { (_leftThigh, _leftCalf, _leftFoot, -1f), (_rightThigh, _rightCalf, _rightFoot, 1f) })
                {
                    var footRotation = foot.rotation;
                    // Mở chân nhẹ khi cúi: mũi giày ra ngoài cạnh vạt dài, không phải rút ngắn tạp dề.
                    thigh.rotation = Quaternion.AngleAxis(side * 16f * down, rig.transform.forward)
                        * Quaternion.AngleAxis(-flex, right) * thigh.rotation;
                    calf.rotation = Quaternion.AngleAxis(flex, right) * calf.rotation;
                    foot.rotation = footRotation;
                }
                // Hạ người làm bao: để IK gập gối; không nâng thân lại theo đế đang thấp.
                if (ActualSpeed < 0.035f && (!rig.hands.Working || rig.motor.ExtraHeadHeight >= 0f))
                {
                    // Capsule có skinWidth và spawn cao hơn sàn. Khi đứng cúi, dùng mặt sàn thật để đặt thân;
                    // lấy chân cao hơn để cả hai chân đều nằm trong tầm IK rồi GroundFoot chốt từng đế.
                    float GroundGap(Transform foot)
                    {
                        if (Physics.Raycast(foot.position + Vector3.up * 0.35f, Vector3.down, out var hit, 0.7f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                            && hit.collider != _controller && hit.normal.y > 0.6f)
                            return foot.position.y - (hit.point.y + ShoeAnkleHeight);
                        return 0f;
                    }
                    transform.position -= Vector3.up * Mathf.Max(GroundGap(_leftFoot), GroundGap(_rightFoot));
                }
            }

            _leftLower.localPosition = _leftElbowRest * ArmGain;
            _leftWrist.localPosition = _leftWristRest * ArmGain;
            _rightLower.localPosition = _rightElbowRest * ArmGain;
            _rightWrist.localPosition = _rightWristRest * ArmGain;
            // Làm ở bàn không gập thân về trước (17/10): gập thì cổ và vai ra trước mắt, trông như camera lùi sau một thân không đầu.
            KeepBodyBehindEye();
            if (_controller.isGrounded || ActualSpeed < 0.035f)
            {
                GroundFoot(_leftThigh, _leftCalf, _leftFoot);
                GroundFoot(_rightThigh, _rightCalf, _rightFoot);
            }
            apron.FitToLegs();
            UpdateShell();
            PlaceHangingItem();
            if (rig.hands.LeftRelaxed) RestHand(rig.hands.left, -1f);
            if (rig.hands.RightRelaxed) RestHand(rig.hands.right, 1f);
            LiftHeldOverBody();
            float leftStretch = Arm(_leftUpper, _leftLower, _leftWrist, rig.hands.left, _leftHandFrame, _leftForearmFrame, -1f);
            float rightStretch = Arm(_rightUpper, _rightLower, _rightWrist, rig.hands.right, _rightHandFrame, _rightForearmFrame, 1f);
            ArmStretch = Mathf.Max(leftStretch, rightStretch);
            AlignCuffs(true); AlignCuffs(false);
            // Mặc định ẩn đầu trước lượt render; callback từng camera mở pass cho góc nhìn ngoài.
            _headProperties.SetFloat("_HidePlayerHead", 1f);
            skin.SetPropertyBlock(_headProperties);
        }

        /// <summary>
        /// Cánh tay dài hơn xương FBX 9% (vai–cổ tay 0,53 → 0,58 m, đúng tỉ lệ người 1,75 m): camera đứng yên ở mắt (17/10) nên mọi việc
        /// làm bằng tay đứng thẳng, không cúi đầu hay gập thân để với.
        /// </summary>
        public const float ArmGain = 1.09f;

        // Xương đầu (gốc sọ) nằm sau mắt chừng này theo phương ngang.
        const float HeadBehindEye = 0.08f;
        // Vòng cổ áo nằm dưới mép đáy khung hình ít nhất chừng này (m).
        const float CollarMargin = 0.03f;

        /// <summary>Vòng cổ áo: đỉnh của submesh đầu (ẩn với camera chính) thấp hơn đỉnh thấp nhất không quá 3 cm, theo xương có trọng số
        /// lớn nhất; tối đa 24 điểm.</summary>
        void BuildCollar()
        {
            var mesh = skin.sharedMesh;
            if (mesh == null || !mesh.isReadable || headSubmesh < 0 || headSubmesh >= mesh.subMeshCount)
                return;
            var vertices = mesh.vertices;
            var weights = mesh.boneWeights;
            var bindposes = mesh.bindposes;
            var up = skin.transform.InverseTransformDirection(transform.up).normalized;
            var head = new System.Collections.Generic.HashSet<int>(mesh.GetTriangles(headSubmesh));
            float low = float.MaxValue;
            foreach (int i in head) low = Mathf.Min(low, Vector3.Dot(vertices[i], up));
            var ring = new System.Collections.Generic.List<int>();
            foreach (int i in head)
                if (Vector3.Dot(vertices[i], up) <= low + 0.03f) ring.Add(i);
            int step = Mathf.Max(1, ring.Count / 24);
            var bones = new System.Collections.Generic.List<Transform>();
            var locals = new System.Collections.Generic.List<Vector3>();
            for (int k = 0; k < ring.Count; k += step)
            {
                int i = ring[k];
                var w = weights[i];
                int bone = w.weight0 >= w.weight1 && w.weight0 >= w.weight2 && w.weight0 >= w.weight3 ? w.boneIndex0
                    : w.weight1 >= w.weight2 && w.weight1 >= w.weight3 ? w.boneIndex1 : w.weight2 >= w.weight3 ? w.boneIndex2 : w.boneIndex3;
                bones.Add(skin.bones[bone]);
                locals.Add(bindposes[bone].MultiplyPoint3x4(vertices[i]));
            }
            _collarBones = bones.ToArray();
            _collarLocal = locals.ToArray();
        }

        // Vỏ thân: điểm cực của hông, thân, ngực, đùi, bắp chân (theo xương, lấy từ mesh lúc bind) cộng lưới thưa tạp dề, tọa độ thế
        // giới cập nhật cuối mỗi khung. Đồ ôm trước ngực và đồ xách bên hông giữ ngoài vỏ này (ghi chú 15/10: bụng xuyên xô, sọt).
        Transform[] _shellBones = new Transform[0];
        Vector3[] _shellLocal = new Vector3[0];
        readonly System.Collections.Generic.List<Vector3> _shell = new System.Collections.Generic.List<Vector3>();
        readonly System.Collections.Generic.List<Vector3> _itemPoints = new System.Collections.Generic.List<Vector3>();
        const float ShellGap = 0.02f;
        readonly System.Collections.Generic.List<Vector3> _hangPoints = new System.Collections.Generic.List<Vector3>();

        // Chốt sau thân/chân: điểm nghỉ lấy từ vai thật, không từ độ cao hông cố định. Thử đường cạnh/sau đùi để đồ rộng không đẩy
        // khuỷu ngang vai; mỗi phương án vẫn giữ ngoài vỏ thân và nằm trên cung tầm với của cánh tay gần duỗi thẳng.
        void PlaceHangingItem()
        {
            var who = rig.interactor;
            var held = who != null ? who.Holding : null;
            if (held == null || !held.hang || held.WorldPose.HasValue || held.transform.parent != who.holdHang) return;
            var hang = who.holdHang;
            var root = rig.transform;
            var shoulder = _rightUpper.position;
            float reach = _rightArmLength * 0.995f;
            var rest = shoulder - root.up * reach + root.right * 0.025f;
            held.ShapePoints(_itemPoints);
            var start = hang.position;
            var best = rest;
            float score = float.PositiveInfinity;
            for (int i = 0; i <= 6; i++)
            {
                var at = rest - root.forward * (i * 0.04f);
                // Độ cao đổi theo cung tay khi cần nhường bề ngang cho xô.
                for (int pass = 0; pass < 3; pass++)
                {
                    _hangPoints.Clear();
                    foreach (var p in _itemPoints) _hangPoints.Add(p + at - start);
                    float need = Clearance(_hangPoints, root.position, root.right, root.up);
                    if (need > 0f) at += root.right * need;
                    var horizontal = Vector3.ProjectOnPlane(at - shoulder, root.up);
                    at = shoulder + horizontal - root.up * Mathf.Sqrt(Mathf.Max(0f, reach * reach - horizontal.sqrMagnitude));
                }
                float cost = Vector3.ProjectOnPlane(at - shoulder, root.up).sqrMagnitude;
                if (cost < score) { score = cost; best = at; }
            }
            hang.position = best;
            // HandsRig đã chạy trước thân; cập nhật ngay cùng khung, không để bàn tay trễ một khung sau quai.
            if (rig.hands.Hanging && held.hangGrip != null)
                rig.hands.right.SetPositionAndRotation(held.hangGrip.position, held.hangGrip.rotation);
        }

        static readonly HumanBodyBones[] ShellParts =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
        };

        void BuildShell()
        {
            var mesh = skin.sharedMesh;
            if (mesh == null || !mesh.isReadable)
                return;
            var vertices = mesh.vertices;
            var weights = mesh.boneWeights;
            var bindposes = mesh.bindposes;
            var head = headSubmesh >= 0 && headSubmesh < mesh.subMeshCount
                ? new System.Collections.Generic.HashSet<int>(mesh.GetTriangles(headSubmesh)) : new System.Collections.Generic.HashSet<int>();
            var parts = new System.Collections.Generic.Dictionary<Transform, System.Collections.Generic.List<Vector3>>();
            foreach (var part in ShellParts)
            {
                var bone = animator.GetBoneTransform(part);
                if (bone != null && System.Array.IndexOf(skin.bones, bone) >= 0) parts[bone] = new System.Collections.Generic.List<Vector3>();
            }
            for (int i = 0; i < vertices.Length; i++)
            {
                if (head.Contains(i)) continue;
                var w = weights[i];
                int index = w.weight0 >= w.weight1 && w.weight0 >= w.weight2 && w.weight0 >= w.weight3 ? w.boneIndex0
                    : w.weight1 >= w.weight2 && w.weight1 >= w.weight3 ? w.boneIndex1 : w.weight2 >= w.weight3 ? w.boneIndex2 : w.boneIndex3;
                // Xương phụ (xoắn, cạp) gom về khúc chính gần nhất phía trên.
                var bone = skin.bones[index];
                for (int up = 0; up < 3 && bone != null && !parts.ContainsKey(bone); up++) bone = bone.parent;
                if (bone == null || !parts.TryGetValue(bone, out var list)) continue;
                int at = System.Array.IndexOf(skin.bones, bone);
                list.Add(bindposes[at].MultiplyPoint3x4(vertices[i]));
            }
            var bones = new System.Collections.Generic.List<Transform>();
            var locals = new System.Collections.Generic.List<Vector3>();
            foreach (var kv in parts)
                foreach (var p in HullPoints.Extremes(kv.Value))
                {
                    bones.Add(kv.Key);
                    locals.Add(p);
                }
            _shellBones = bones.ToArray();
            _shellLocal = locals.ToArray();
        }

        void UpdateShell()
        {
            _shell.Clear();
            for (int i = 0; i < _shellBones.Length; i++) _shell.Add(_shellBones[i].TransformPoint(_shellLocal[i]));
            ShellOrigin = rig.transform.position;
            apron.Outline(_shell);
        }

        /// <summary>Số điểm vỏ thân (0: mesh thân không đọc được, đồ cầm không được giữ ngoài thân).</summary>
        public int ShellPoints => _shell.Count;

        /// <summary>
        /// Vỏ thân (điểm cực của hông, lưng, ngực, đùi, cẳng chân; thế giới) ở cuối khung trước. Ngực, bụng nhô trước capsule va
        /// chạm ~10 cm nên đồ NPC cầm/vác đo khoảng hở với vỏ này, không với capsule (NpcAgent.ReachAllows, QA-0150).
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<Vector3> Shell => _shell;

        /// <summary>Vị trí gốc người chơi lúc dựng Shell: người đã đi tiếp trong khung này thì dời vỏ theo hiệu hai vị trí.</summary>
        public Vector3 ShellOrigin { get; private set; }

        /// <summary>
        /// Đồ (các điểm thế giới) phải dời theo axis bao nhiêu (m; âm là đã hở) để mọi điểm của nó nằm trước vỏ thân theo axis cộng
        /// ShellGap. Chỉ tính phần vỏ cùng tầm cao (theo up) và cùng tầm ngang (vuông góc axis, up) với đồ. Vỏ của khung trước.
        /// </summary>
        public float Clearance(System.Collections.Generic.List<Vector3> item, Vector3 origin, Vector3 axis, Vector3 up)
        {
            if (item.Count == 0 || _shell.Count == 0)
                return float.NegativeInfinity;
            var side = Vector3.Cross(up, axis);
            float back = float.PositiveInfinity, yMin = float.PositiveInfinity, yMax = float.NegativeInfinity;
            float sMin = float.PositiveInfinity, sMax = float.NegativeInfinity;
            foreach (var p in item)
            {
                var d = p - origin;
                back = Mathf.Min(back, Vector3.Dot(d, axis));
                float y = Vector3.Dot(d, up), s = Vector3.Dot(d, side);
                yMin = Mathf.Min(yMin, y); yMax = Mathf.Max(yMax, y);
                sMin = Mathf.Min(sMin, s); sMax = Mathf.Max(sMax, s);
            }
            float front = float.NegativeInfinity;
            foreach (var q in _shell)
            {
                var d = q - origin;
                float y = Vector3.Dot(d, up), s = Vector3.Dot(d, side);
                if (y < yMin - ShellGap || y > yMax + ShellGap || s < sMin - ShellGap || s > sMax + ShellGap) continue;
                front = Mathf.Max(front, Vector3.Dot(d, axis));
            }
            return float.IsNegativeInfinity(front) ? float.NegativeInfinity : front + ShellGap - back;
        }

        /// <summary>Đồ ôm/cầm đang dùng khung tay: dời bao nhiêu theo forward của người chơi để ra khỏi vỏ thân.</summary>
        public float HeldClearance(Interaction.Holdable held)
        {
            if (held == null || _shell.Count == 0) return float.NegativeInfinity;
            held.ShapePoints(_itemPoints);
            var root = rig.transform;
            return Clearance(_itemPoints, root.position, root.forward, root.up);
        }

        /// <summary>
        /// Mắt phải tiến tới chừng này (theo hướng ngang forward, tính từ eyeBase) để vòng cổ áo nằm dưới mép đáy khung hình. Cúi nhìn mà
        /// mắt ngay trên cổ thì thấy lỗ cổ rỗng (ghi chú 15/10: ôm sọt, dốc bao khoai). Mép đáy khung là mặt phẳng qua mắt, pháp tuyến
        /// n = up + tan(fov/2)·forward của camera (góc mở dọc không đổi theo tỉ lệ màn hình); điểm P khuất khi (P − mắt)·n ≤ 0, mà dời mắt
        /// thêm e theo hướng ngang h thì (P − mắt)·n giảm e·(h·n). Nhìn ngang hay ngẩng (h·n nhỏ) thì đáy khung không quay về sau cổ.
        /// </summary>
        float CollarClearance(Vector3 eyeBase, Vector3 forward)
        {
            var cam = rig.cam;
            if (cam == null || _collarBones.Length == 0)
                return 0f;
            float t = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var n = cam.transform.up + t * cam.transform.forward;
            float hn = Vector3.Dot(forward, n);
            if (hn < 0.2f)
                return 0f;
            float need = float.NegativeInfinity;
            for (int i = 0; i < _collarBones.Length; i++)
                need = Mathf.Max(need, Vector3.Dot(_collarBones[i].TransformPoint(_collarLocal[i]) - eyeBase, n));
            return (need + CollarMargin) / hn;
        }

        /// <summary>Góc nhìn thứ nhất: cúi nhìn không được đẩy cổ và vai ra trước camera (trông như camera lùi ra sau một cái xác không
        /// đầu). Camera đứng yên ở mắt mặc định (17/10: game không tự dời camera); thân lùi theo phương ngang dưới camera cho xương đầu
        /// nằm sau mắt và vòng cổ áo nằm dưới mép đáy khung hình. Đặt lại mỗi khung từ localPosition nên không cộng dồn.</summary>
        void KeepBodyBehindEye()
        {
            var forward = rig.transform.forward;
            var eye = rig.head.position;
            float ahead = Vector3.Dot(_head.position - eye, forward) + HeadBehindEye;
            BodyBack = Mathf.Clamp(Mathf.Max(ahead, CollarClearance(eye, forward)), 0f, MaxBodyBack);
            if (BodyBack > 0f)
                transform.position -= forward * BodyBack;
        }

        /// <summary>Phần thân lùi sau vị trí đứng thường ở khung này (m).</summary>
        public float BodyBack { get; private set; }
        const float MaxBodyBack = 0.4f;

        // Cuff nằm trên cẳng tay; bàn tay được phép gập tại cổ tay mà không kéo theo 18 cm ống áo.
        public Quaternion SleeveRotation(bool left)
        {
            var elbow = left ? _leftLower : _rightLower;
            var wrist = left ? _leftWrist : _rightWrist;
            var hand = left ? rig.hands.left : rig.hands.right;
            return Quaternion.FromToRotation(hand.forward, (wrist.position - elbow.position).normalized) * hand.rotation;
        }

        void AlignCuffs(bool left)
        {
            // Ống tay bám cổ tay thật của xương: khi tay không với tới mục tiêu, ống không được tách khỏi bàn tay.
            var anchor = (left ? _leftWrist : _rightWrist).position;
            var rotation = SleeveRotation(left);
            foreach (var cuff in left ? _leftCuffs : _rightCuffs)
                if (cuff.mesh.gameObject.activeInHierarchy)
                    cuff.mesh.SetPositionAndRotation(anchor + rotation * cuff.position, rotation * cuff.rotation);
            // Bàn tay có xương: vùng cổ tay của da theo ống tay (không gãy góc, không hở lòng ống khi cổ tay gập).
            var fingers = left ? rig.hands.leftFingers : rig.hands.rightFingers;
            if (fingers != null && fingers.forearm != null && fingers.isActiveAndEnabled)
                fingers.forearm.SetPositionAndRotation(fingers.bones[0].position, rotation);
        }

        /// <summary>Đồ ôm bằng hai tay (sọt, thùng) nằm trong khung tay theo camera; ngẩng lên thì điểm bám xa vai hơn độ dài
        /// cánh tay, tay thật không với tới còn đồ bay lơ lửng. Kéo cả khung tay (đồ đi theo) về cho vừa tầm với.</summary>
        // Đồ ôm to (thùng cá) đẩy ra trước vỏ thân thì tay không với tới, kéo về lại lọt vào tạp dề: nâng dần khung tay lên (tối đa
        // MaxLift) tới khi đồ đã kéo về tầm tay vẫn ở ngoài vỏ thân, hạ lại khi không cần.
        float _lift;
        const float MaxLift = 0.3f, LiftSpeed = 0.8f;

        void LiftHeldOverBody()
        {
            var held = rig.interactor != null ? rig.interactor.Holding : null;
            // Đang đổ: PourMotion đặt vật theo thế giới và đã kéo các điểm nắm vào tầm vai. Nâng hay kéo khung tay lúc này làm lòng bàn
            // tay rời điểm nắm; giữ _lift để vật về tay đúng độ nâng cũ.
            if (held != null && held.WorldPose.HasValue) return;
            bool front = held != null && !held.hang && !held.ShoulderCarried && rig.hands.FrameFollowsCamera;
            if (!front)
            {
                _lift = 0f;
                PullHeldGripsIntoReach();
                return;
            }
            if (_lift > 0.0001f) rig.hands.transform.position += rig.transform.up * _lift;
            PullHeldGripsIntoReach();
            float over = HeldClearance(held);
            if (float.IsNegativeInfinity(over)) over = -MaxLift;
            float want = Mathf.Clamp(_lift + over, 0f, MaxLift);
            _lift = Time.deltaTime > 0.2f ? want : Mathf.MoveTowards(_lift, want, LiftSpeed * Time.deltaTime);
        }

        void PullHeldGripsIntoReach()
        {
            var hands = rig.hands;
            if (!hands.GripsOnHeld)
                return;
            float reach = (Vector3.Distance(_leftUpper.position, _leftLower.position) + Vector3.Distance(_leftLower.position, _leftWrist.position)) * 0.97f;
            var pull = Vector3.zero;
            float worst = 0f;
            foreach (var (shoulder, target) in new[] { (_leftUpper, hands.left), (_rightUpper, hands.right) })
            {
                var toShoulder = shoulder.position - target.position;
                float excess = toShoulder.magnitude - reach;
                if (excess > worst)
                {
                    worst = excess;
                    pull = toShoulder.normalized * excess;
                }
            }
            if (worst > 0.0001f)
                hands.transform.position += pull;
        }

        void RestHand(Transform target, float side)
        {
            var (position, rotation) = RestPose(side);
            target.SetPositionAndRotation(position, rotation);
        }

        /// <summary>Tư thế bàn tay thả lỏng (thế giới; side −1 trái, 1 phải): cổ tay ngang hông, ngón xuôi xuống, không phụ thuộc pitch
        /// của camera.</summary>
        public (Vector3 position, Quaternion rotation) RestPose(float side) =>
            (Shoulder(side < 0f).position - rig.transform.up * (ArmLength(side < 0f) * 0.995f)
                + rig.transform.right * (side * 0.025f),
                rig.transform.rotation * Quaternion.Euler(82f, side * -8f, side * 8f));

        float Arm(Transform shoulder, Transform elbow, Transform wrist, Transform target, Quaternion handFrame, Quaternion forearmFrame, float side)
        {
            float length = Vector3.Distance(shoulder.position, elbow.position) + Vector3.Distance(elbow.position, wrist.position);
            float stretch = Mathf.Max(1f, Vector3.Distance(shoulder.position, target.position) / (length * 0.998f));
            // Vải chỉ giãn 3%; tư thế camera tự thu tay về tầm thật, grip tại trạm phải được bố trí trong tầm.
            stretch = Mathf.Min(stretch, 1.03f);
            bool workGrip = side < 0f ? rig.hands.WorkingLeft : rig.hands.WorkingRight;
            if (!workGrip)
                target.position = shoulder.position + Vector3.ClampMagnitude(target.position - shoulder.position, length * stretch * 0.998f);
            elbow.localPosition *= stretch;
            wrist.localPosition *= stretch;
            // Khuỷu xuống và ra ngoài vai, tránh dựng cẳng tay ngay trước camera khi đưa dụng cụ lên miệng.
            var pole = shoulder.position + rig.transform.right * side * .25f - rig.transform.up * .35f;
            SolveTwoBone(shoulder, elbow, wrist, target.position, pole);
            if (workGrip)
            {
                // Tay bám vật theo chỗ bám (đổ xô, dốc bao, lật sọt): bàn tay có lúc quay ngược hướng khuỷu mặc định, cổ tay gập tới 135–160°
                // (ghi chú 15/10). Thử đặt khuỷu sau cổ tay theo hướng ngón tay (cẳng tay thẳng hàng bàn tay), giữ cách gập ít hơn; còn gập quá
                // MaxWristBend thì xoay bàn tay về phía cẳng tay cho vừa.
                float bend = Vector3.Angle(wrist.position - elbow.position, target.forward);
                if (bend > 40f)
                {
                    var rotations = (shoulder.rotation, elbow.rotation);
                    var aligned = target.position - target.forward * Vector3.Distance(elbow.position, wrist.position);
                    SolveTwoBone(shoulder, elbow, wrist, target.position, aligned);
                    if (Vector3.Angle(wrist.position - elbow.position, target.forward) > bend)
                    {
                        shoulder.rotation = rotations.Item1;
                        elbow.rotation = rotations.Item2;
                    }
                }
                // Grip đánh dấu cổ tay ở thế nắm gốc. Giữ nguyên điểm tiếp xúc trong lòng bàn tay khi đổi hướng ngón, rồi giải lại
                // khuỷu/cổ tay. Chỉ xoay tại gốc cổ tay như trước làm lòng bàn tay trượt khỏi vành khi lật vật 180°.
                var contactOffset = rig.hands.ContactOffset(side < 0f);
                var contact = target.TransformPoint(contactOffset);
                for (int pass = 0; pass < 8; pass++)
                {
                    var forearm = (wrist.position - elbow.position).normalized;
                    bend = Vector3.Angle(forearm, target.forward);
                    if (bend <= GripWristBend + 0.01f) break;
                    target.rotation = Quaternion.FromToRotation(target.forward,
                        Vector3.RotateTowards(forearm, target.forward, GripWristBend * Mathf.Deg2Rad, 0f)) * target.rotation;
                    target.position = contact - target.rotation * contactOffset;
                    var aligned = target.position - target.forward * Vector3.Distance(elbow.position, wrist.position);
                    SolveTwoBone(shoulder, elbow, wrist, target.position, aligned);
                }
            }
            wrist.rotation = target.rotation * handFrame;
            // Chia độ xoay dọc cẳng tay theo cổ tay. Blend tuyến tính hai xương xoay ngược nhau làm vải
            // thắt/pinch tại khuỷu; xoay quanh đúng trục không đổi vị trí cổ tay hay độ gập khuỷu.
            var wristRotation = wrist.rotation;
            var axis = (wrist.position - elbow.position).normalized;
            var delta = wristRotation * Quaternion.Inverse(forearmFrame) * Quaternion.Inverse(elbow.rotation);
            var projected = Vector3.Project(new Vector3(delta.x, delta.y, delta.z), axis);
            var twist = new Quaternion(projected.x, projected.y, projected.z, delta.w);
            float twistDegrees = Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(Vector3.Dot(projected, axis), delta.w) * Mathf.Rad2Deg);
            if (side < 0f) TwistLeft = twistDegrees; else TwistRight = twistDegrees;
            if (Quaternion.Dot(twist, twist) > 0.000001f) elbow.rotation = twist.normalized * elbow.rotation;
            wrist.rotation = wristRotation;
            return stretch;
        }

        /// <summary>Độ xoay dọc cẳng tay (độ, có dấu) khung vừa rồi so với tư thế gốc của xương, để soát tay cầm ngược cẳng (17/10).</summary>
        public float TwistLeft { get; private set; }
        public float TwistRight { get; private set; }

        /// <summary>Cổ tay gập tối đa (độ) giữa cẳng tay và bàn tay khi tay bám vật.</summary>
        public const float MaxWristBend = 70f;
        const float GripWristBend = 55f; // Biên an toàn 5° dưới mục tiêu QA 60°, không đổi ngưỡng lỗi 70°.

        void GroundFoot(Transform thigh, Transform calf, Transform foot)
        {
            // Dò mặt bậc thật, bỏ trigger/capsule người chơi; không thêm collider hay physics lên thân.
            var origin = foot.position + Vector3.up * 0.35f;
            if (!Physics.Raycast(origin, Vector3.down, out var hit, 0.7f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return;
            if (hit.collider == _controller || hit.normal.y < 0.6f)
                return;
            var target = foot.position;
            float ankle = hit.point.y + ShoeAnkleHeight;
            if (target.y < ankle + 0.055f || ActualSpeed < 0.035f)
            {
                target.y = ankle;
                var rotation = foot.rotation;
                SolveTwoBone(thigh, calf, foot, target, thigh.position + rig.transform.forward);
                foot.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * rotation;
            }
        }

        /// <summary>Nghiệm hình học hai đoạn: giữ độ dài, hướng khuỷu/gối bởi pole; không cần package IK.</summary>
        public static void SolveTwoBone(Transform root, Transform joint, Transform tip, Vector3 target, Vector3 pole)
        {
            float a = Vector3.Distance(root.position, joint.position), b = Vector3.Distance(joint.position, tip.position);
            var offset = target - root.position;
            float distance = Mathf.Clamp(offset.magnitude, Mathf.Abs(a - b) + 0.0001f, a + b - 0.0001f);
            var axis = offset.normalized;
            var bend = Vector3.ProjectOnPlane(pole - root.position, axis).normalized;
            if (bend.sqrMagnitude < 0.001f) bend = Vector3.ProjectOnPlane(Vector3.up, axis).normalized;
            float along = (a * a - b * b + distance * distance) / (2f * distance);
            var elbow = root.position + axis * along + bend * Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            root.rotation = Quaternion.FromToRotation(joint.position - root.position, elbow - root.position) * root.rotation;
            joint.rotation = Quaternion.FromToRotation(tip.position - joint.position, target - joint.position) * joint.rotation;
        }
    }
}
