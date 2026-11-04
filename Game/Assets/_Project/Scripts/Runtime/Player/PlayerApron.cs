using UnityEngine;

namespace HaiBanTay.Player
{
    /// <summary>Tạp dề eo dài: vỏ vải 4 mm, nếp dọc; đẩy ra trước chân sau animation/IK, không physics cloth.</summary>
    public sealed class PlayerApron : MonoBehaviour
    {
        public const int Columns = 40, Rows = 32;
        public const float Thickness = 0.004f;
        public PlayerBody body;
        public Vector3 waistOffset;
        public MeshRenderer Renderer { get; private set; }
        public Mesh Mesh { get; private set; }
        Transform[] _legs;
        Vector3[] _vertices;
        readonly Vector3[] _joints = new Vector3[6];
        Transform _hips;

        public static Mesh CreateMesh()
        {
            int face = (Columns + 1) * (Rows + 1);
            var vertices = new Vector3[face * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new System.Collections.Generic.List<int>();
            for (int layer = 0; layer < 2; layer++)
                for (int j = 0; j <= Rows; j++) for (int i = 0; i <= Columns; i++)
                {
                    int a = layer * face + j * (Columns + 1) + i;
                    float u = (float)i / Columns, t = (float)j / Rows;
                    vertices[a] = new Vector3((u * 2f - 1f) * Mathf.Lerp(0.195f, 0.23f, t),
                        1.055f - 0.70f * t, 0.17f + Drape(u, t) + Fold(u, t) - layer * Thickness);
                    uv[a] = new Vector2(u, 1f - t);
                    if (i < Columns && j < Rows)
                    {
                        int b = a + Columns + 1;
                        if (layer == 0) triangles.AddRange(new[] { a, b, b + 1, a, b + 1, a + 1 });
                        else triangles.AddRange(new[] { a, b + 1, b, a, a + 1, b + 1 });
                    }
                }
            // Đóng cả bốn mép: gấu và cạnh có độ dày thật, không tấm phẳng hai mặt.
            void Edge(int a, int b) => triangles.AddRange(new[] { a, b, b + face, a, b + face, a + face });
            for (int i = 0; i < Columns; i++) { Edge(i + 1, i); Edge(Rows * (Columns + 1) + i, Rows * (Columns + 1) + i + 1); }
            for (int j = 0; j < Rows; j++) { Edge(j * (Columns + 1), (j + 1) * (Columns + 1)); Edge((j + 1) * (Columns + 1) + Columns, j * (Columns + 1) + Columns); }
            var mesh = new Mesh { name = "BaLongApron" };
            mesh.vertices = vertices; mesh.uv = uv; mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        // Bốn nếp mềm (đủ đỉnh để mép vải không thành răng cưa khi cúi nhìn xuống).
        static float Fold(float u, float t) => 0.011f * Mathf.Sin(u * Mathf.PI * 8f + 0.4f) * Mathf.Lerp(0.25f, 1f, t)
            + 0.003f * Mathf.Sin(u * Mathf.PI * 22f + t * 4f) * t;

        // Chỉ ôm eo ở phần trên; gấu rủ, không cuộn cứng quanh đùi.
        static float Drape(float u, float t) => -0.035f * Mathf.Pow(u * 2f - 1f, 2f) * Mathf.Pow(1f - t, 2f)
            + 0.01f * Mathf.Sin(t * Mathf.PI);

        void Awake()
        {
            var animator = body.animator;
            _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            _legs = new[] { animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg), animator.GetBoneTransform(HumanBodyBones.LeftFoot),
                animator.GetBoneTransform(HumanBodyBones.RightUpperLeg), animator.GetBoneTransform(HumanBodyBones.RightLowerLeg), animator.GetBoneTransform(HumanBodyBones.RightFoot) };
            Renderer = GetComponent<MeshRenderer>();
            Mesh = GetComponent<MeshFilter>().mesh; // Bản riêng mỗi người; không ghi vào asset chung.
            Mesh.MarkDynamic();
            _vertices = Mesh.vertices;
        }

        void OnDestroy() { if (Mesh != null) Destroy(Mesh); }

        /// <summary>Lưới thưa mặt ngoài tạp dề (thế giới) theo tư thế khung này, mỗi 4 cột × 4 hàng.</summary>
        public void Outline(System.Collections.Generic.List<Vector3> into)
        {
            if (_vertices == null || !isActiveAndEnabled) return;
            // Lưới thưa nhưng luôn gồm hàng gấu và hai mép (bước 4 bỏ sót mép khi Rows/Columns không chia hết: xô lướt qua gấu váy).
            for (int j = 0; j <= Rows; j = j == Rows ? Rows + 1 : Mathf.Min(j + 2, Rows))
                for (int i = 0; i <= Columns; i = i == Columns ? Columns + 1 : Mathf.Min(i + 2, Columns))
                    into.Add(transform.TransformPoint(_vertices[j * (Columns + 1) + i]));
        }

        /// <summary>Gọi từ PlayerBody sau GroundFoot: chân và váy dùng cùng tư thế cuối khung hình.</summary>
        public void FitToLegs()
        {
            if (_vertices == null) return;
            for (int k = 0; k < 6; k++) _joints[k] = transform.InverseTransformPoint(_legs[k].position);
            var hip = transform.InverseTransformPoint(_hips.position);
            float top = hip.y + waistOffset.y;
            float hem = Mathf.Max(top - 0.70f, Mathf.Min(_joints[2].y, _joints[5].y) + 0.26f);
            int face = (Columns + 1) * (Rows + 1);
            float previousFront = hip.z + waistOffset.z;
            for (int j = 0; j <= Rows; j++)
            {
                float t = (float)j / Rows, y = Mathf.Lerp(top, hem, t);
                float bridge = hip.z + waistOffset.z;
                // Cùng một đường bao trước hai chân: vải bắc cầu qua khe, không tạo hai ụ.
                for (int leg = 0; leg < 2; leg++) for (int segment = 0; segment < 2; segment++)
                {
                    var a = _joints[leg * 3 + segment];
                    var b = _joints[leg * 3 + segment + 1];
                    float radius = segment == 0 ? 0.14f : 0.105f;
                    // Lấy mẫu capsule dày hơn quần; biên an toàn bù khoảng giữa mẫu và bề dày vải.
                    for (int sample = 0; sample <= 12; sample++)
                    {
                        var center = Vector3.Lerp(a, b, sample / 12f);
                        float dy = y - center.y;
                        float inside = radius * radius - dy * dy;
                        if (inside > 0f) bridge = Mathf.Max(bridge, center.z + Mathf.Sqrt(inside) + 0.028f);
                    }
                }
                // Từ đùi xuống gấu, trọng lực giữ vải rủ gần thẳng thay vì ôm cong trở lại chân.
                bridge = Mathf.Max(bridge, previousFront - .0015f);
                previousFront = bridge;
                // Mép trên buộc dây sát bụng: vài hàng đầu bám eo rồi mới loe ra trước đùi (cúi gập người không làm cạp
                // tạp dề chìa ngang như tấm ván).
                float waist = hip.z + waistOffset.z;
                float tuck = Mathf.SmoothStep(0f, 1f, t / 0.28f);
                float front = Mathf.Lerp(waist, bridge, tuck);
                for (int i = 0; i <= Columns; i++)
                {
                    float u = (float)i / Columns;
                    var p = new Vector3(hip.x + (u * 2f - 1f) * Mathf.Lerp(0.195f, 0.23f, t), y,
                        Mathf.Max(front, waist + Drape(u, t)) + Fold(u, t) * tuck);
                    int index = j * (Columns + 1) + i;
                    _vertices[index] = p;
                    _vertices[index + face] = p - Vector3.forward * Thickness;
                }
            }
            Mesh.vertices = _vertices;
            Mesh.RecalculateNormals();
            Mesh.RecalculateBounds();
            Renderer.renderingLayerMask = body.skin.renderingLayerMask;
            Renderer.lightProbeUsage = body.skin.lightProbeUsage;
        }
    }
}
