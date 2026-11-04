using UnityEngine;

namespace HaiBanTay.Player
{
    /// <summary>Hai tay theo camera: mỗi tư thế một mesh, nội suy vị trí/xoay; grime đổi _BaseColor, mồ hôi đổi
    /// _Smoothness qua MaterialPropertyBlock (D5, D10); tay run khi mệt (BodyState).</summary>
    [DefaultExecutionOrder(10000)]
    public sealed class HandsRig : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        public Transform left;
        public Transform right;
        [Tooltip("Mesh theo tư thế, chỉ số = (int)HandPose. Phần tử null thì dùng mesh Open.")]
        public GameObject[] leftPoseMeshes = new GameObject[5];
        public GameObject[] rightPoseMeshes = new GameObject[5];
        public Renderer[] skinRenderers = new Renderer[0];
        [Header("Xương ngón (HandRigBuilder)")]
        [Tooltip("Bàn tay có xương từng ngón (thay mesh theo tư thế khi SetFingers bật), cùng khung với mesh Open.")]
        public GameObject riggedLeft;
        public GameObject riggedRight;
        public HandFingers leftFingers;
        public HandFingers rightFingers;
        public Renderer[] riggedRenderers = new Renderer[0];
        public GameObject shovelProp;
        public GameObject shovelLoad;
        public GameObject rakeProp;
        public GameObject broomProp;
        [Tooltip("Bàn chải ngắn (Tool.Brush), con của bàn tay phải: hiện khi đang cầm; lúc cọ trạm đặt nó theo mặt cọ (DetachBrush).")]
        public GameObject brushProp;
        [Tooltip("Bàn chải cán dài (Tool.LongBrush), con của bàn tay phải: cầm dựng đứng bên phải khi đi, hai tay trên cán khi cọ.")]
        public GameObject longBrushProp;
        public Color skinColor = new Color(0.78f, 0.6f, 0.47f);
        public Color grimeColor = new Color(0.16f, 0.14f, 0.13f);
        public float blendSpeed = 8f;
        public float grimeSpeed = 0.5f;
        public float skinSmoothness = 0.35f;
        public float sweatSmoothness = 0.78f;
        public float trembleAmplitude = 0.006f;
        public HandPoseTarget[] targets = DefaultTargets();

        MaterialPropertyBlock _mpb;
        HandPoseTarget _current;
        bool _overridden;
        float _grime = -1f;
        float _grimeTarget;
        bool _snap = true;
        Vector3 _leftPos, _rightPos;
        Quaternion _leftRot = Quaternion.identity, _rightRot = Quaternion.identity;
        bool _shoulderCarry;
        Transform _carryPivot;
        Transform _workLeft, _workRight;
        float _gripSettle = 1f;
        const float GripSettleSeconds = 0.15f;
        HandPose? _rightMesh, _leftMesh;
        Transform _brushParent, _longBrushParent;
        Vector3 _brushLocalPos, _longBrushLocalPos;
        Quaternion _brushLocalRot, _longBrushLocalRot;
        bool _hang, _gripsOnHeld;
        PlayerRig _rig;
        float _rootPitch, _push;
        float _sweat;
        float _appliedSweat = -1f;

        bool _fingersLeft, _fingersRight;

        public HandPose Pose { get; private set; } = HandPose.Lowered;
        float _overrideEmission = 1f;
        /// <summary>Độ sáng nội tại thêm khi tay ở khung đặt sẵn (SetOverride), 0–1; cảnh có đèn thật chiếu tay (ăn kem) hạ xuống cho da không cháy cam.</summary>
        public float OverrideEmission
        {
            get => _overrideEmission;
            set
            {
                _overrideEmission = Mathf.Clamp01(value);
                ApplyGrime(Mathf.Max(0f, _grime));
            }
        }

        public bool LeftFingersActive => _fingersLeft;
        public bool RightFingersActive => _fingersRight;
        public bool WorkingLeft => _workLeft != null;
        public bool WorkingRight => _workRight != null;
        /// <summary>Đang làm việc tại chỗ bằng tay bám vật (cọ, dốc bao…); xách xô bên hông và ôm đồ đi (sọt, thùng cá) không tính:
        /// thân lùi khi cúi như lúc tay không, không chồm tới bàn (ghi chú 15/10: ôm sọt cúi xuống thấy lỗ cổ áo).</summary>
        public bool Working => (WorkingLeft || WorkingRight) && !_hang && !_gripsOnHeld;
        /// <summary>Đang xách vật một tay bên hông.</summary>
        public bool Hanging => _hang;
        /// <summary>Hai tay đang bám đồ ôm (sọt, thùng): cả khung tay được kéo về tầm với khi ngẩng/cúi.</summary>
        public bool GripsOnHeld => _gripsOnHeld;
        public bool LeftRelaxed => _workLeft == null && !_overridden && (Pose == HandPose.Lowered || Pose == HandPose.Grip || _shoulderCarry || _hang);
        public bool RightRelaxed => _workRight == null && !_overridden && Pose == HandPose.Lowered;
        /// <summary>Dụng cụ cán dài đang cầm (tư thế Shovel dùng chung cho xẻng, cào, chổi).</summary>
        public Tool Tool { get; private set; }
        public float Grime => Mathf.Max(0f, _grime);
        /// <summary>Tay run 0–1 (mệt).</summary>
        public float Tremble { get; set; }
        /// <summary>Tay ướt mồ hôi 0–1 (gần lò).</summary>
        public float Sweat
        {
            get => _sweat;
            set => _sweat = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Độ lật quanh trục ngón của tay trái khi ôm hai bên vật (tay phải lấy âm): lòng bàn tay áp vào thành vật, mu bàn tay ra ngoài.
        /// Dùng chung cho dáng Carry và chỗ bám hai bên của đồ ôm hai tay (sọt, thùng, khay); ClipFit17.FlipFit/HoldEverything.
        /// </summary>
        public const float SideGripRoll = -100f;

        public static Quaternion SideGrip(bool left) => Quaternion.Euler(0f, 0f, left ? SideGripRoll : -SideGripRoll);

        public static HandPoseTarget[] DefaultTargets()
        {
            return new[]
            {
                new HandPoseTarget(HandPose.Lowered, new Vector3(-0.22f, -0.5f, 0.3f), new Vector3(20f, 0f, 10f),
                    new Vector3(0.22f, -0.5f, 0.3f), new Vector3(20f, 0f, -10f)),
                new HandPoseTarget(HandPose.Open, new Vector3(-0.12f, -0.22f, 0.42f), new Vector3(-15f, 10f, 180f),
                    new Vector3(0.12f, -0.22f, 0.42f), new Vector3(-15f, -10f, 180f)),
                new HandPoseTarget(HandPose.Grip, new Vector3(-0.22f, -0.5f, 0.3f), new Vector3(20f, 0f, 10f),
                    new Vector3(0.17f, -0.27f, 0.45f), new Vector3(0f, -10f, -75f)),
                // Bê hai tay: lòng bàn tay áp vào hai bên vật (lật 180° quanh trục ngón so với ±80°: lòng bàn tay ngửa về phía người,
                // cẳng tay vặn ngược, 17/10 "Tay bê nồi bạn làm ngược cẳng rồi").
                new HandPoseTarget(HandPose.Carry, new Vector3(-0.21f, -0.3f, 0.42f), new Vector3(0f, 10f, SideGripRoll),
                    new Vector3(0.21f, -0.3f, 0.42f), new Vector3(0f, -10f, -SideGripRoll)),
                new HandPoseTarget(HandPose.Shovel, new Vector3(-0.04f, -0.3f, 0.48f), new Vector3(10f, 0f, 60f),
                    new Vector3(0.16f, -0.36f, 0.34f), new Vector3(10f, 0f, -70f)),
            };
        }

        void Awake()
        {
            _rig = GetComponentInParent<PlayerRig>();
            _mpb = new MaterialPropertyBlock();
            if (brushProp != null)
            {
                _brushParent = brushProp.transform.parent;
                _brushLocalPos = brushProp.transform.localPosition;
                _brushLocalRot = brushProp.transform.localRotation;
            }
            if (longBrushProp != null)
            {
                _longBrushParent = longBrushProp.transform.parent;
                _longBrushLocalPos = longBrushProp.transform.localPosition;
                _longBrushLocalRot = longBrushProp.transform.localRotation;
            }
            ApplyMeshes();
            _current = Find(Pose);
            if (left != null)
            {
                _leftPos = left.localPosition;
                _leftRot = left.localRotation;
            }
            if (right != null)
            {
                _rightPos = right.localPosition;
                _rightRot = right.localRotation;
            }
        }

        /// <summary>Bật bàn tay có xương ngón cho từng bên (thay mesh tư thế dựng sẵn); tư thế ngón đặt qua leftFingers/rightFingers.Target.
        /// Tay không có xương dựng thì lệnh bật bị bỏ qua.</summary>
        public void SetFingers(bool leftOn, bool rightOn)
        {
            leftOn &= riggedLeft != null;
            rightOn &= riggedRight != null;
            if (leftOn == _fingersLeft && rightOn == _fingersRight) return;
            _fingersLeft = leftOn;
            _fingersRight = rightOn;
            ApplyMeshes();
            ApplyGrime(Mathf.Max(0f, _grime));
            if (leftOn) leftFingers.Snap();
            if (rightOn) rightFingers.Snap();
        }

        /// <summary>Vác bao: tay phải vòng lên đỡ, tay trái thả; dùng lại mesh Carry, không đổi tư thế đồ nhẹ.</summary>
        public void SetShoulderCarry(bool carrying, Transform pivot)
        {
            _shoulderCarry = carrying;
            _carryPivot = pivot;
        }

        public void SetPose(HandPose pose)
        {
            if (Pose == pose && !_overridden)
                return;
            Pose = pose;
            _overridden = false;
            if (pose != HandPose.Carry)
                _rightMesh = null;
            _leftMesh = null;
            ApplyGrime(Mathf.Max(0f, _grime));
            _current = Find(pose);
            ApplyMeshes();
        }

        /// <summary>Đặt tay theo khung hình cố định (HandsShotPose) với mesh Open.</summary>
        public void SetOverride(HandPoseTarget target)
        {
            Pose = target.pose;
            _current = target;
            _overridden = true;
            ApplyGrime(Mathf.Max(0f, _grime));
            ApplyMeshes();
        }

        /// <summary>Xách bằng tay phải: bàn tay nắm đúng quai (grip), tay trái thả lỏng; null = thôi xách.</summary>
        public void SetHangCarry(Transform grip)
        {
            if (grip == null)
            {
                if (!_hang)
                    return;
                _hang = false;
                _workRight = null;
                SetPose(HandPose.Lowered);
                return;
            }
            _hang = true;
            _workLeft = null;
            _workRight = grip;
            _rightMesh = null;
            SetPose(HandPose.Grip);
            Snap();
        }

        /// <summary>Tay bám quai/vành của vật đang ôm (sọt than, thùng cá): vật nằm trong khung tay nên khung vẫn hãm góc
        /// cúi và đẩy ra trước bụng như khi tay không.</summary>
        public void SetHeldGrips(Transform leftGrip, Transform rightGrip)
        {
            SetWorkGrips(leftGrip, rightGrip);
            _gripsOnHeld = leftGrip != null || rightGrip != null;
        }

        /// <summary>Tay bám hai điểm thật trên vật đang làm việc, dùng mesh Carry sẵn có; rightMesh: bàn tay phải mang tư thế
        /// riêng (Grip: nắm tròn quanh cán dao, tâm nắm trước cổ tay GripForward, dưới mu tay GripBelow).</summary>
        public void SetWorkGrips(Transform leftGrip, Transform rightGrip, HandPose? rightMesh = null, HandPose? leftMesh = null)
        {
            _gripsOnHeld = false;
            _workLeft = leftGrip;
            _workRight = rightGrip;
            // Chỉ tay phải làm (nĩa, thìa): tay phải giữ dáng nắm, tay trái thả lỏng; dáng Carry cho cả người thì tay trái duỗi ra trước như
            // đang bê và xuyên mặt bàn (PlayerClipProbe, 17/10).
            SetPose(leftGrip != null ? HandPose.Carry : HandPose.Lowered);
            // Sau SetPose (dáng khác Carry xóa mesh riêng của tay phải).
            _rightMesh = rightGrip != null ? rightMesh ?? (leftGrip == null ? HandPose.Carry : (HandPose?)null) : null;
            // Tay trái nắm cán (bàn chải dài) thì dùng mesh nắm thay cho mesh ôm.
            _leftMesh = leftGrip != null ? leftMesh : null;
            ApplyMeshes();
            Snap();
        }

        /// <summary>Mesh tay đang hiện của bàn tay phải (test).</summary>
        public HandPose RightMeshPose => _rightMesh ?? Pose;

        /// <summary>Tâm nắm của mesh Grip (cán tròn xuyên qua theo trục X): trước cổ tay và dưới mặt mu bàn tay (m), đo trên mesh
        /// Hand_R_Grip (Tools/blender/make_fp_hands.py, ngón cong 58/75/55 độ).</summary>
        public const float GripForward = 0.092f, GripBelow = 0.036f;

        /// <summary>Điểm tiếp xúc trong lòng bàn tay tính từ cổ tay, cùng khung +Z ngón, −Y lòng bàn tay.</summary>
        /// <summary>Mesh tay đang hiện của bàn tay trái (test).</summary>
        public HandPose LeftMeshPose => _leftMesh ?? Pose;

        public Vector3 ContactOffset(bool left) => (left ? LeftMeshPose : RightMeshPose) == HandPose.Grip
            ? new Vector3(0f, -GripBelow, GripForward) : new Vector3(0f, -0.01f, 0.07f);

        public Transform GripAnchor(bool left) => left ? _workLeft : _workRight;

        public void SetGrime(float value, bool instant = false)
        {
            _grimeTarget = Mathf.Clamp01(value);
            if (instant)
                ApplyGrime(_grimeTarget);
        }

        public void SetTool(Tool tool)
        {
            Tool = tool;
            ApplyMeshes();
        }

        /// <summary>
        /// Lúc cọ: bàn chải đang cầm rời khung tay để trạm đặt nó đúng chỗ trên mặt cọ (thế giới), tay bám theo chỗ nắm trên cán.
        /// Trả về Transform của bàn chải (null nếu không cầm bàn chải).
        /// </summary>
        public Transform DetachBrush(Tool tool)
        {
            var prop = tool == Tool.Brush ? brushProp : tool == Tool.LongBrush ? longBrushProp : null;
            if (prop == null)
                return null;
            prop.SetActive(true);
            if (_blendBrush == prop.transform) _blendBrush = null;
            // Rời nắm tay nhưng vẫn là con của người chơi (gốc PlayerRig): trạm đặt theo tọa độ thế giới mỗi khung; người không đi trong lúc
            // cọ nên không trễ khung. Là đồ của người chơi thì các đầu dò xuyên vật đo nó như dụng cụ/đồ cầm, không coi là vật của cảnh.
            var rig = GetComponentInParent<PlayerRig>();
            if (rig == null) rig = PlayerRig.Current;
            prop.transform.SetParent(rig != null ? rig.transform : null, true);
            return prop.transform;
        }

        /// <summary>Hết cọ: bàn chải về lại trong nắm tay phải (chỗ cũ).</summary>
        public void AttachBrush(Tool tool)
        {
            if (tool == Tool.Brush && brushProp != null)
                BlendBrushHome(brushProp.transform, _brushParent, _brushLocalPos, _brushLocalRot);
            else if (tool == Tool.LongBrush && longBrushProp != null)
                BlendBrushHome(longBrushProp.transform, _longBrushParent, _longBrushLocalPos, _longBrushLocalRot);
            ApplyMeshes();
        }

        /// <summary>Thời gian bàn chải đi từ mặt cọ về nắm tay (s): về tức thời là nhảy cả mét trong một khung (cán dài 1,9 m).</summary>
        public const float BrushHomeSeconds = 0.35f;
        Transform _blendBrush;
        Vector3 _blendFromPos, _blendToPos;
        Quaternion _blendFromRot, _blendToRot;
        float _blendT;

        void BlendBrushHome(Transform brush, Transform parent, Vector3 pos, Quaternion rot)
        {
            // Giữ chỗ đang thấy (thế giới), đổi cha về nắm tay, rồi trượt mượt về chỗ cầm trong BrushHomeSeconds. Nội suy theo thế giới:
            // tay đang về tư thế cầm (xoay vài chục độ) không quật bàn chải dài theo.
            _blendFromPos = brush.position;
            _blendFromRot = brush.rotation;
            brush.SetParent(parent, true);
            _blendBrush = brush;
            _blendToPos = pos;
            _blendToRot = rot;
            _blendT = 0f;
        }

        void TickBrushBlend()
        {
            if (_blendBrush == null) return;
            var parent = _blendBrush.parent;
            if (parent == null || (parent != _brushParent && parent != _longBrushParent)) { _blendBrush = null; return; }
            _blendT = Mathf.Min(1f, _blendT + Time.deltaTime / BrushHomeSeconds);
            float k = Mathf.SmoothStep(0f, 1f, _blendT);
            _blendBrush.SetPositionAndRotation(Vector3.Lerp(_blendFromPos, parent.TransformPoint(_blendToPos), k),
                Quaternion.Slerp(_blendFromRot, parent.rotation * _blendToRot, k));
            if (_blendT >= 1f)
            {
                _blendBrush.SetLocalPositionAndRotation(_blendToPos, _blendToRot);
                _blendBrush = null;
            }
        }

        public void SetShovelLoaded(bool loaded)
        {
            if (shovelLoad != null)
                shovelLoad.SetActive(loaded && Pose == HandPose.Shovel && Tool == Tool.Shovel);
        }

        /// <summary>Đặt ngay vào tư thế đích (không nội suy) — dùng sau màn đen.</summary>
        public void Snap()
        {
            _snap = true;
        }

        HandPoseTarget Find(HandPose pose)
        {
            foreach (var t in targets)
                if (t.pose == pose)
                    return t;
            return targets.Length > 0 ? targets[0] : default;
        }

        void ApplyMeshes()
        {
            Toggle(leftPoseMeshes, _leftMesh ?? Pose, _fingersLeft);
            Toggle(rightPoseMeshes, _rightMesh ?? Pose, _fingersRight);
            if (riggedLeft != null && riggedLeft.activeSelf != _fingersLeft) riggedLeft.SetActive(_fingersLeft);
            if (riggedRight != null && riggedRight.activeSelf != _fingersRight) riggedRight.SetActive(_fingersRight);
            bool longTool = Pose == HandPose.Shovel;
            if (shovelProp != null)
                shovelProp.SetActive(longTool && Tool != Tool.Rake && Tool != Tool.Broom);
            if (rakeProp != null)
                rakeProp.SetActive(longTool && Tool == Tool.Rake);
            if (broomProp != null)
                broomProp.SetActive(longTool && Tool == Tool.Broom);
            if (shovelLoad != null && Pose != HandPose.Shovel)
                shovelLoad.SetActive(false);
            // Bàn chải là đồ trong tay, không theo dáng tay: hiện suốt lúc cầm (kệ, móc thì trống), kể cả khi trạm đang đặt nó trên mặt cọ.
            if (brushProp != null && brushProp.activeSelf != (Tool == Tool.Brush))
                brushProp.SetActive(Tool == Tool.Brush);
            if (longBrushProp != null && longBrushProp.activeSelf != (Tool == Tool.LongBrush))
                longBrushProp.SetActive(Tool == Tool.LongBrush);
        }

        static void Toggle(GameObject[] meshes, HandPose pose, bool hideAll = false)
        {
            int idx = (int)pose;
            bool has = idx < meshes.Length && meshes[idx] != null;
            for (int i = 0; i < meshes.Length; i++)
            {
                if (meshes[i] == null)
                    continue;
                bool on = !hideAll && (has ? i == idx : i == (int)HandPose.Open);
                if (meshes[i].activeSelf != on)
                    meshes[i].SetActive(on);
            }
        }

        /// <summary>Cổ tay phải (khung tay) khi cầm bàn chải cán dài đi lại.</summary>
        public static readonly Vector3 LongBrushCarryPos = new Vector3(0.27f, -0.42f, 0.24f), LongBrushCarryEuler = new Vector3(0f, -10f, -90f);

        // Khung tay theo camera tới góc cúi này; cúi sâu hơn thì tay và đồ ôm trước ngực ở lại (không chúi vào bụng),
        // ngẩng lên thì tay không giơ theo lên trời.
        const float MaxHandsPitch = 50f, MinHandsPitch = -8f;
        // Mặt trước tạp dề/bụng tính từ trục thân (m), cộng khoảng hở cho bàn tay.
        const float BodyFront = 0.27f;

        bool FreeFrame => !_overridden && Pose != HandPose.Open && !_hang && (_gripsOnHeld || (_workLeft == null && _workRight == null));
        /// <summary>Khung tay đi theo camera (không bị trạm/clip ghi đè) và quãng đã đẩy ra trước thân (m): cho test đo.</summary>
        public bool FrameFollowsCamera => FreeFrame;
        public float FramePush => _push;

        void LateUpdate()
        {
            // Khung tay: hãm pitch (mượt), đặt lại gốc trước khi tính đích theo grip thật.
            bool snapped = _snap;
            float pitch = transform.parent != null ? Mathf.DeltaAngle(0f, transform.parent.eulerAngles.x) : 0f;
            // Dụng cụ cán dài (xẻng, cào, chổi) theo đúng hướng nhìn: đầu dụng cụ phải ở chỗ tia nhắm trúng.
            // Bàn chải cầm một tay như đồ nhẹ: khung tay hãm góc như tay không (cán dài dựng đứng không chọc xuống sàn khi cúi nhìn).
            bool aimedTool = Tool != Tool.None && Tool != Tool.Brush && Tool != Tool.LongBrush;
            float wantPitch = FreeFrame && !aimedTool ? Mathf.Clamp(pitch, MinHandsPitch, MaxHandsPitch) - pitch : 0f;
            _rootPitch = _snap ? wantPitch : Mathf.MoveTowards(_rootPitch, wantPitch, 240f * Time.deltaTime);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.Euler(_rootPitch, 0f, 0f);
            if (_rig == null && PlayerRig.Current != null && PlayerRig.Current.hands == this) _rig = PlayerRig.Current;
            var held = _rig != null && _rig.interactor != null ? _rig.interactor.Holding : null;
            // Đổi khung camera làm con của khung dịch theo. Vật đang đổ phải ở pose thế giới trước khi đọc grip, cùng khung này.
            if (held != null && held.WorldPose.HasValue)
                held.transform.SetPositionAndRotation(held.WorldPose.Value.position, held.WorldPose.Value.rotation);
            var target = _current;
            if (Tool == Tool.LongBrush && Pose == HandPose.Grip && _workRight == null)
            {
                // Cầm bàn chải cán dài khi đi: tay phải thấp bên hông phải, cán dựng gần thẳng đứng, mũi lông gần sàn trước chân phải.
                target.rightPos = LongBrushCarryPos;
                target.rightEuler = LongBrushCarryEuler;
            }
            if (_shoulderCarry && Pose == HandPose.Carry)
            {
                target.leftPos = new Vector3(-0.22f, -0.5f, 0.3f);
                target.leftEuler = new Vector3(20f, 0f, 10f);
                target.rightPos = new Vector3(0.32f, -0.10f, 0.43f);
                // Tay phải đặt trên bao vác vai: lòng bàn tay úp xuống bao (lật 180° so với −95°: cẳng tay phải vặn ngược −66°, 17/10).
                target.rightEuler = new Vector3(-35f, -20f, 85f);
                if (_carryPivot != null)
                {
                    var tilt = _carryPivot.localRotation;
                    var center = _carryPivot.localPosition;
                    target.rightPos = center + tilt * (target.rightPos - center);
                    target.rightEuler = (tilt * Quaternion.Euler(target.rightEuler)).eulerAngles;
                }
            }
            // Đang bước lại gần trạm (PlayerRig.StepToward): điểm cầm còn ngoài tầm tay, tay giữ tư thế; bước xong mới trượt tới điểm cầm
            // trong GripSettleSeconds rồi bám chặt. Điểm cầm đặt khi đã đứng yên thì bám ngay như trước.
            bool waiting = _rig != null && _rig.Stepping && (_workLeft != null || _workRight != null);
            _gripSettle = waiting ? 0f : Mathf.MoveTowards(_gripSettle, 1f, Time.deltaTime / GripSettleSeconds);
            if (_workLeft != null && !waiting)
            {
                target.leftPos = transform.InverseTransformPoint(_workLeft.position);
                target.leftEuler = (Quaternion.Inverse(transform.rotation) * _workLeft.rotation).eulerAngles;
            }
            if (_workRight != null && !waiting)
            {
                target.rightPos = transform.InverseTransformPoint(_workRight.position);
                target.rightEuler = (Quaternion.Inverse(transform.rotation) * _workRight.rotation).eulerAngles;
            }
            float blend = 1f - Mathf.Exp(-blendSpeed * Time.deltaTime);
            float k = _snap ? 1f : (_workLeft != null || _workRight != null) && !waiting ? Mathf.Max(blend, _gripSettle) : blend;
            _snap = false;
            _leftPos = Vector3.Lerp(_leftPos, target.leftPos, k);
            _leftRot = Quaternion.Slerp(_leftRot, Quaternion.Euler(target.leftEuler), k);
            _rightPos = Vector3.Lerp(_rightPos, target.rightPos, k);
            _rightRot = Quaternion.Slerp(_rightRot, Quaternion.Euler(target.rightEuler), k);
            // Run tay: nhiễu Perlin nhỏ cộng lên vị trí đã nội suy (không tích lũy qua các khung).
            float t = Time.time * 9f;
            float amp = trembleAmplitude * Tremble;
            Place(left, _leftPos + amp * Shake(t, 0f), _leftRot);
            Place(right, _rightPos + amp * Shake(t, 17f), _rightRot);
            KeepInFrontOfBody(snapped);
            // Sau khi tay đã đặt xong khung này: bàn chải đang về nắm tay đặt theo thế giới.
            TickBrushBlend();

            if (!Mathf.Approximately(_grime, _grimeTarget))
                ApplyGrime(Mathf.MoveTowards(Mathf.Max(0f, _grime), _grimeTarget, grimeSpeed * Time.deltaTime));
            else if (Mathf.Abs(_sweat - _appliedSweat) > 0.02f)
                ApplyGrime(Mathf.Max(0f, _grime));
        }

        /// <summary>Đẩy cả khung tay (tay, dụng cụ, đồ ôm) ra trước mặt tạp dề khi cúi nhìn: thân lùi ra sau còn tay theo
        /// camera thì cổ tay lọt vào bụng.</summary>
        void KeepInFrontOfBody(bool snapped)
        {
            // Vật đang được đặt theo thế giới (PourMotion): không đẩy khung tay (lòng bàn tay đang bám điểm nắm trên vật), giữ quãng
            // đẩy để xong lượt đổ vật về đúng chỗ cũ trước thân.
            var placed = _rig != null && _rig.interactor != null ? _rig.interactor.Holding : null;
            if (placed != null && placed.WorldPose.HasValue) return;
            float need = 0f;
            // Camera (cha của khung tay) không nằm dưới PlayerRig nên GetComponentInParent lúc Awake trả null: trước 15/10 đoạn này
            // không bao giờ chạy, đồ ôm lọt vào bụng khi cúi nhìn.
            if (_rig == null && PlayerRig.Current != null && PlayerRig.Current.hands == this)
                _rig = PlayerRig.Current;
            if (FreeFrame && _rig != null && _rig.body != null && left != null && right != null)
            {
                // Tay thả lỏng do PlayerBody đặt bên hông, không tính.
                var root = _rig.transform;
                float limit = _rig.body.transform.localPosition.z + BodyFront;
                float front = float.PositiveInfinity;
                if (!LeftRelaxed) front = Mathf.Min(front, Vector3.Dot(left.position - root.position, root.forward));
                if (!RightRelaxed) front = Mathf.Min(front, Vector3.Dot(right.position - root.position, root.forward));
                if (!float.IsInfinity(front))
                    need = Mathf.Max(0f, limit - front);
                // Đồ ôm trước ngực: mọi điểm của vật nằm trước vỏ thân (ngực, bụng, tạp dề, đùi gập khi cúi) cùng tầm cao.
                var held = _rig.interactor != null ? _rig.interactor.Holding : null;
                if (held != null && !held.ShoulderCarried && !held.hang)
                    need = Mathf.Max(need, _rig.body.HeldClearance(held));
            }
            // Vật đang được đặt theo thế giới (PourMotion): giữ quãng đẩy, xong lượt đổ vật về đúng chỗ cũ trước thân.
            _push = snapped ? need : Mathf.MoveTowards(_push, need, 1.5f * Time.deltaTime);
            if (_push > 0.0001f && _rig != null)
                transform.position += _rig.transform.forward * _push;
        }

        static Vector3 Shake(float t, float seed)
        {
            return new Vector3(Mathf.PerlinNoise(t, seed) - 0.5f, Mathf.PerlinNoise(seed, t) - 0.5f, 0f) * 2f;
        }

        static void Place(Transform t, Vector3 pos, Quaternion rot)
        {
            if (t == null)
                return;
            t.localPosition = pos;
            t.localRotation = rot;
        }

        void ApplyGrime(float value)
        {
            _grime = value;
            if (_mpb == null)
                _mpb = new MaterialPropertyBlock();
            var color = Color.Lerp(skinColor, grimeColor, value);
            _appliedSweat = _sweat;
            float smooth = Mathf.Lerp(skinSmoothness, sweatSmoothness, _sweat);
            Tint(skinRenderers, color, smooth, value);
            Tint(riggedRenderers, color, smooth, value);
        }

        void Tint(Renderer[] renderers, Color color, float smooth, float grime)
        {
            foreach (var r in renderers)
            {
                if (r == null)
                    continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorId, color);
                _mpb.SetFloat(SmoothnessId, smooth);
                // Nâng nhẹ vùng tay tối trong khung motif, không tiêu tốn đèn realtime.
                _mpb.SetColor(EmissionId, _overridden ? _overrideEmission * Color.Lerp(new Color(0.19f, 0.13f, 0.09f),
                    new Color(0.11f, 0.085f, 0.065f), grime) : Color.black);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
