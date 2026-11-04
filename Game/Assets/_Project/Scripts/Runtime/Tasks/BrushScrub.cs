using HaiBanTay.Player;
using UnityEngine;

namespace HaiBanTay.Tasks
{
    /// <summary>
    /// Cọ một bề mặt bằng bàn chải thật (chậu, chảo, nồi cao), dùng chung cho các trạm: chuột dời con trỏ ẩn trên mặt (ScrubMotion), mũi lông
    /// đặt đúng con trỏ, bàn chải nghiêng và tay nắm theo BrushReach (trong tầm vai, cán không cắt qua vành), vết bẩn chỉ mất ở chỗ đầu chải
    /// đã đi (DirtMask → texture 64×64 → lớp phủ DirtOverlay qua MaterialPropertyBlock). Sạch 90% thì phần lấm tấm còn lại mờ dần và xong.
    /// Trạm giữ khóa camera/di chuyển; lớp này chỉ lo bàn chải, tay và vết bẩn.
    /// </summary>
    public sealed class BrushScrub
    {
        static readonly int MaskId = Shader.PropertyToID("_MaskTex");
        /// <summary>Sạch chừng này phần mặt thì coi như xong (phần còn lại mờ dần).</summary>
        public const float CleanEnough = 0.9f;
        /// <summary>Thiếu chừng này (phần mặt) vẫn tính là xong: chấm cuối khuất sau quai/đáy không bắt cọ mãi ở 99% (bot 23/10, nồi cao kẹt 0,996).</summary>
        const float DoneSlack = 0.005f;

        public readonly ScrubSurface Surface;
        public readonly BrushShape Shape;
        public readonly DirtMask Mask;
        public readonly ScrubMotion Motion = new ScrubMotion();
        /// <summary>Vật đang cọ (hệ tọa độ của mặt cọ và mặt nạ).</summary>
        public readonly Transform Vessel;
        readonly float _innerRadius, _outerRadius, _rimHeight, _innerBottomRadius;
        readonly Texture2D _texture;
        readonly byte[] _bytes;
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        Renderer _overlay;
        bool _dirtyTexture = true;
        float _fade;

        /// <summary>Tốc độ tay trái xoay vật quanh trục đứng khi đang chà (độ/giây); 0 = không xoay (chảo, nồi cao).</summary>
        public float SpinDegreesPerSecond;
        /// <summary>Sức chải mỗi lượt đầu chải đi qua một ô (0–1).</summary>
        public float PassStrength = 0.55f;
        /// <summary>Bán kính vết chải trên mặt (m).</summary>
        public float FootprintRadius;
        /// <summary>Tư thế bàn chải vừa giải ở khung này.</summary>
        public BrushPose Pose { get; private set; }
        public bool HasPose { get; private set; }
        Matrix4x4 _poseVessel = Matrix4x4.identity;
        public float Progress => Mathf.Clamp01(Mask.Cleaned / CleanEnough);
        public bool Done => Mask.Cleaned >= CleanEnough - DoneSlack;
        /// <summary>Điểm đầu chải trên mặt (thế giới), để đo đầu chải nằm trên mặt.</summary>
        public Vector3 HeadWorld { get; private set; }
        public Vector3 HeadNormal { get; private set; }

        /// <param name="vessel">Vật đang cọ.</param>
        /// <param name="surface">Mặt cọ trong hệ vật.</param>
        /// <param name="innerRadius">Bán kính thành trong ở vành (m, hệ vật).</param>
        /// <param name="outerRadius">Bán kính thành ngoài (m).</param>
        /// <param name="rimHeight">Độ cao vành (m, hệ vật).</param>
        /// <param name="innerBottomRadius">Bán kính thành trong ở đáy (lòng thu dần xuống đáy); 0 = thành đứng.</param>
        public BrushScrub(Transform vessel, ScrubSurface surface, BrushShape shape, float innerRadius, float outerRadius, float rimHeight,
            float innerBottomRadius = 0f)
        {
            _innerBottomRadius = innerBottomRadius;
            Vessel = vessel;
            Surface = surface;
            Shape = shape;
            _innerRadius = innerRadius;
            _outerRadius = outerRadius;
            _rimHeight = rimHeight;
            Mask = new DirtMask(64, disc: true);
            Mask.Fill();
            FootprintRadius = shape.HeadRadius * 1.15f;
            _bytes = new byte[Mask.Size * Mask.Size];
            _texture = new Texture2D(Mask.Size, Mask.Size, TextureFormat.R8, false, true)
            {
                name = "DirtMask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            Motion.Reset(Vector2.zero);
            Motion.MaxHeadSpeed = MaxHeadSpeed;
        }

        /// <summary>
        /// Tốc độ lớn nhất của đầu chải (m/s): 1,6 cm mỗi khung 60 Hz, chừa ~0,4 cm cho mũi lông dời theo độ nghiêng (lệch theo pháp tuyến
        /// tới một bán kính bó lông) mà vẫn dưới ngưỡng "nhảy" 2 cm của MotionProbe. Tay thật chà nhanh nhất cỡ đó.
        /// </summary>
        public const float MaxHeadSpeed = 0.95f;

        /// <summary>Gắn lớp phủ bẩn (một lưới dựng theo mặt cọ, con của vật).</summary>
        public Renderer CreateOverlay(Material material, string name = "DirtMaskOverlay")
        {
            var go = new GameObject(name);
            go.transform.SetParent(Vessel, false);
            go.AddComponent<MeshFilter>().sharedMesh = DirtOverlayMesh.Build(Surface);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _overlay = renderer;
            _dirtyTexture = true;
            Push();
            return renderer;
        }

        public Renderer Overlay => _overlay;

        /// <summary>Bẩn kín lại (nồi vừa nấu xong).</summary>
        public void Soil()
        {
            Mask.Fill();
            _fade = 0f;
            _dirtyTexture = true;
            Push();
        }

        /// <summary>Sạch hẳn (Sim, tua nhanh).</summary>
        public void Clean()
        {
            Mask.Clear();
            _dirtyTexture = true;
            Push();
        }

        /// <summary>Đặt con trỏ về chỗ gần điểm thế giới (chỗ người chơi đang nhìn) nhất.</summary>
        public void Aim(Vector3 world)
        {
            Motion.Reset(Surface.Unwrap(Vessel.InverseTransformPoint(world)));
        }

        static ScrubVessel VesselWorld(Transform vessel, ScrubSurface surface, float inner, float outer, float rim, float innerBottom = 0f)
        {
            float scale = vessel.lossyScale.y;
            return new ScrubVessel
            {
                Base = vessel.TransformPoint(new Vector3(0f, surface.BottomY, 0f)),
                InnerRadius = inner * scale, OuterRadius = outer * scale, Height = (rim - surface.BottomY) * scale,
                InnerBottomRadius = innerBottom * scale,
                // Trục vật nghiêng theo vật (nồi cao chao khi sóng), không giả là trụ đứng.
                Up = vessel.up
            };
        }

        /// <summary>Thành vật đang cọ trong hệ thế giới (test đo đầu chải không thò qua thành).</summary>
        public ScrubVessel World => VesselWorld(Vessel, Surface, _innerRadius, _outerRadius, _rimHeight, _innerBottomRadius);

        /// <summary>Tư thế bàn chải cho con trỏ <paramref name="c"/> (hệ mặt cọ) với vai thật của người chơi.</summary>
        public BrushPose Solve(Vector2 c, PlayerRig rig)
        {
            Surface.Evaluate(c, out var local, out var localNormal);
            var tip = Vessel.TransformPoint(local);
            var normal = Vessel.TransformDirection(localNormal);
            var body = rig.body;
            var right = body.Shoulder(false).position;
            var left = body.Shoulder(true).position;
            float reach = Mathf.Min(body.ArmLength(false), body.ArmLength(true)) * Interaction.PourMotion.ReachShare;
            return BrushReach.Solve(Shape, tip, normal, right, left, reach, (left + right) * 0.5f,
                World);
        }

        /// <summary>Con trỏ có tư thế đạt (trong tầm, không cắt vành).</summary>
        public bool Reachable(Vector2 c, PlayerRig rig) => Solve(c, rig).Ok;

        /// <summary>
        /// Giữ con trỏ trong mặt cọ và trong tầm với: điểm muốn tới mà không đạt thì lùi theo đoạn từ con trỏ hiện tại tới đó (chia đôi),
        /// dừng ở điểm xa nhất còn đạt.
        /// </summary>
        public Vector2 ClampReach(Vector2 from, Vector2 want, PlayerRig rig)
        {
            want = Surface.Clamp(want);
            if (Reachable(want, rig))
                return want;
            if (!Reachable(from, rig))
                return Recover(from, rig);
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 7; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Reachable(Vector2.Lerp(from, want, mid), rig)) lo = mid; else hi = mid;
            }
            return Vector2.Lerp(from, want, lo);
        }

        /// <summary>Chỗ về của con trỏ trên mặt: phía người chơi, ở 0,6 tầm với của mặt cọ (0 nếu người đứng ngay trên tâm).</summary>
        Vector2 Home(PlayerRig rig)
        {
            var toward = Surface.Unwrap(Vessel.InverseTransformPoint(rig.Position));
            return toward.sqrMagnitude > 1e-6f ? toward.normalized * Surface.Reachable * 0.6f : Vector2.zero;
        }

        /// <summary>
        /// Con trỏ đang đứng ngoài tầm (vai người chơi dời, hay con trỏ khởi đầu không có chỗ đạt): chỉ lùi theo điểm muốn tới thì kẹt mãi
        /// (QA-0126: giữ chuột 170 s vẫn đứng yên ở 84% sạch), nên kéo con trỏ về chỗ đạt đầu tiên trên đường tới <see cref="Home"/>
        /// rồi tâm mặt.
        /// </summary>
        Vector2 Recover(Vector2 from, PlayerRig rig)
        {
            var home = Home(rig);
            for (int i = 1; i <= 10; i++)
            {
                var c = Vector2.Lerp(from, home, i / 10f);
                if (Reachable(c, rig)) return c;
            }
            for (int i = 1; i <= 10; i++)
            {
                var c = Vector2.Lerp(home, Vector2.zero, i / 10f);
                if (Reachable(c, rig)) return c;
            }
            return from;
        }

        /// <summary>
        /// Con trỏ khởi đầu: điểm đạt gần chỗ đang nhìn nhất (dò dần về phía người nếu chỗ nhìn ngoài tầm).
        /// </summary>
        public void AimReachable(Vector3 lookPoint, PlayerRig rig)
        {
            var want = Surface.Unwrap(Vessel.InverseTransformPoint(lookPoint));
            var toward = Home(rig);
            for (int i = 0; i <= 10; i++)
            {
                var c = Vector2.Lerp(want, toward, i / 10f);
                if (Reachable(c, rig)) { Motion.Reset(c); return; }
            }
            for (int i = 0; i <= 10; i++)
            {
                var c = Vector2.Lerp(toward, Vector2.zero, i / 10f);
                if (Reachable(c, rig)) { Motion.Reset(c); return; }
            }
            Motion.Reset(want);
        }

        /// <summary>
        /// Một khung cọ. <paramref name="mouse"/>: độ rê chuột (điểm ảnh); <paramref name="right"/>, <paramref name="forward"/>: hướng ngang
        /// của camera trên mặt phẳng ngang (thế giới), để rê sang phải là đầu chải sang phải. Trả về true nếu mặt sạch thêm.
        /// </summary>
        public bool Tick(PlayerRig rig, bool held, Vector2 mouse, float dt, Vector3 right, Vector3 forward)
        {
            // Tay trái xoay vật trong lúc chà: con trỏ giữ nguyên chỗ trong thế giới, phần vật xa tay lần lượt quay về phía người.
            if (SpinDegreesPerSecond != 0f && Motion.Pressing)
            {
                float turn = SpinDegreesPerSecond * dt;
                Vessel.Rotate(Vector3.up, turn, Space.World);
                var c = Motion.Cursor;
                float a = turn * Mathf.Deg2Rad;
                // Vật quay +turn quanh trục đứng (thế giới): tọa độ vật của một điểm đứng yên quay −turn.
                float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                var rotated = new Vector2(c.x * cos + c.y * sin, -c.x * sin + c.y * cos);
                Motion.Reset(rotated, keepActivity: true);
            }
            // Tay có quán tính: rê chuột đổi chiều thì đầu chải đổi chiều trong ~0,1 s (không giật ngược trong một khung).
            _mouse = held ? Vector2.Lerp(_mouse, mouse, 1f - Mathf.Exp(-Mathf.Max(0f, dt) / MouseSmoothSeconds)) : Vector2.zero;
            mouse = _mouse;
            var dirWorld = right * mouse.x + forward * mouse.y;
            var local = Vessel.InverseTransformDirection(dirWorld);
            var surfaceDelta = new Vector2(local.x, local.z);
            if (surfaceDelta.sqrMagnitude > 1e-12f)
                surfaceDelta = surfaceDelta.normalized * mouse.magnitude;
            var start = Motion.Cursor;
            // Tay có quán tính cả khi vướng tầm với: vận tốc con trỏ đổi tối đa CursorAccel mỗi giây. Con trỏ kẹt ở mép tầm tay rồi thoát
            // ra thì đầu chải tăng tốc dần, không vụt đi cả bước chuột trong một khung (brush17b: nồi cao từ ghế, 0,9 → 2,9 cm/khung).
            if (held && Motion.MetresPerPixel > 0f && dt > 0f)
            {
                var wantStep = surfaceDelta * Motion.MetresPerPixel;
                var step = _cursorStep + Vector2.ClampMagnitude(wantStep - _cursorStep, CursorAccel * dt * dt);
                surfaceDelta = step / Motion.MetresPerPixel;
            }
            var seg = Motion.Tick(held, surfaceDelta, dt, want => ClampReach(start, want, rig));
            _cursorStep = held ? Motion.Cursor - start : Vector2.zero;
            bool cleaned = false;
            if ((seg.to - seg.from).sqrMagnitude > 1e-10f)
            {
                float scale = Surface.UvScale;
                cleaned = Mask.ScrubPass(Surface.MaskUv(seg.from), Surface.MaskUv(seg.to), FootprintRadius * scale, PassStrength) > 0f;
                _dirtyTexture |= cleaned;
            }
            if (Done && Mask.Remaining > 0f)
            {
                // Đã sạch đủ: vết lấm tấm còn lại mờ dần thay vì bắt người chơi đi tìm từng chấm.
                _fade += dt;
                Mask.Fade(dt * 0.9f);
                _dirtyTexture = true;
                cleaned = true;
            }
            var pose = Follow(Motion.Head, rig, dt);
            // Nhấc đầu chải khỏi mặt khi thả chuột: cả bàn chải dời dọc trục.
            pose.Tip += pose.Axis * Motion.Lift;
            pose.LowGrip += pose.Axis * Motion.Lift;
            pose.HighGrip += pose.Axis * Motion.Lift;
            pose.LowWrist += pose.Axis * Motion.Lift;
            pose.HighWrist += pose.Axis * Motion.Lift;
            Pose = pose;
            HasPose = true;
            _poseVessel = Vessel.localToWorldMatrix;
            Surface.Evaluate(Motion.Head, out var headLocal, out var headNormal);
            HeadWorld = Vessel.TransformPoint(headLocal);
            HeadNormal = Vessel.TransformDirection(headNormal);
            Push();
            return cleaned;
        }

        Vector3 _axis, _normal = Vector3.up;
        float _lowAt;
        /// <summary>Tốc độ quay lớn nhất của pháp tuyến dùng đặt bàn chải (độ/giây).</summary>
        public const float NormalTurnDegreesPerSecond = 240f;
        bool _smooth;

        /// <summary>
        /// Tư thế cho đầu chải ở khung này: góc trục và chỗ nắm đi mượt về lời giải tốt nhất (không nhảy giữa hai ứng viên rời rạc của
        /// BrushReach), mũi lông luôn đúng điểm đầu chải.
        /// </summary>
        BrushPose Follow(Vector2 head, PlayerRig rig, float dt)
        {
            var target = Solve(head, rig);
            float k = _smooth ? 1f - Mathf.Exp(-10f * Mathf.Max(0f, dt)) : 1f;
            _axis = _smooth ? Vector3.Slerp(_axis, target.Axis, k).normalized : target.Axis;
            _lowAt = _smooth ? Mathf.Lerp(_lowAt, target.LowAt, k) : target.LowAt;
            Surface.Evaluate(head, out var local, out var localNormal);
            // Bó lông áp mặt lệch khỏi điểm cọ theo pháp tuyến tới một bán kính bó lông (7 cm ở bàn chải cán dài): pháp tuyến quay nhanh
            // qua góc đáy–thành thì cả bàn chải vụt theo (brush17b: 2,5 cm/khung). Pháp tuyến dùng cho tư thế quay tối đa NormalTurn độ/giây.
            var wantNormal = Vessel.TransformDirection(localNormal);
            _normal = _smooth ? Vector3.RotateTowards(_normal, wantNormal, NormalTurnDegreesPerSecond * Mathf.Deg2Rad * Mathf.Max(0f, dt), 0f) : wantNormal;
            _smooth = true;
            var body = rig.body;
            float reach = Mathf.Min(body.ArmLength(false), body.ArmLength(true)) * Interaction.PourMotion.ReachShare;
            return BrushReach.Evaluate(Shape, Vessel.TransformPoint(local), _normal, _axis, _lowAt,
                body.Shoulder(false).position, body.Shoulder(true).position, reach, World);
        }

        /// <summary>Bắt đầu lượt cọ mới: tư thế đầu tiên lấy thẳng lời giải, không đi mượt từ lượt trước.</summary>
        public void Restart()
        {
            _smooth = false;
            _mouse = Vector2.zero;
            _cursorStep = Vector2.zero;
        }

        /// <summary>Hằng thời gian làm mượt rê chuột (s).</summary>
        public const float MouseSmoothSeconds = 0.05f;
        /// <summary>Gia tốc lớn nhất của con trỏ trên mặt (m/s²): từ đứng yên tới nhịp rê nhanh của nồi cao (1,3 m/s) trong khoảng 0,07 s.</summary>
        public const float CursorAccel = 20f;
        Vector2 _mouse, _cursorStep;

        /// <summary>Đặt bàn chải (mũi lông ở gốc, +Y dọc cán, +Z theo hướng ngón tay dưới) và hai chỗ bám cổ tay theo tư thế đã giải.</summary>
        public void Place(Transform brush, Transform lowWrist, Transform highWrist)
        {
            if (!HasPose)
                return;
            var pose = Pose;
            // Khung không giải lại tư thế (dừng cọ, chờ Jacques giữ…) mà nồi vẫn đung đưa: bàn chải đi theo nồi, không đứng yên giữa
            // không trung rồi vụt về chỗ mới khi cọ tiếp (Brush17: 11,9 cm/khung).
            var moved = Vessel.localToWorldMatrix * _poseVessel.inverse;
            if (!moved.Equals(Matrix4x4.identity))
            {
                var turn = moved.rotation;
                pose.Tip = moved.MultiplyPoint3x4(pose.Tip);
                pose.LowWrist = moved.MultiplyPoint3x4(pose.LowWrist);
                pose.HighWrist = moved.MultiplyPoint3x4(pose.HighWrist);
                pose.Fingers = turn * pose.Fingers;
                pose.Axis = turn * pose.Axis;
                pose.LowHand = turn * pose.LowHand;
                pose.HighHand = turn * pose.HighHand;
            }
            if (brush != null)
                brush.SetPositionAndRotation(pose.Tip, Quaternion.LookRotation(pose.Fingers, pose.Axis));
            if (lowWrist != null)
                lowWrist.SetPositionAndRotation(pose.LowWrist, pose.LowHand);
            if (highWrist != null && Shape.TwoHanded)
                highWrist.SetPositionAndRotation(pose.HighWrist, pose.HighHand);
        }

        void Push()
        {
            if (_overlay == null || !_dirtyTexture)
                return;
            _dirtyTexture = false;
            Mask.CopyTo(_bytes);
            _texture.LoadRawTextureData(_bytes);
            _texture.Apply(false, false);
            _overlay.GetPropertyBlock(_block);
            _block.SetTexture(MaskId, _texture);
            _overlay.SetPropertyBlock(_block);
            _overlay.enabled = Mask.Remaining > 0.0005f;
        }
    }
}
