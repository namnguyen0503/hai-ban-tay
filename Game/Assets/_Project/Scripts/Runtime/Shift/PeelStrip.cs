using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Dải vỏ vừa gọt: bám ở lưỡi dao, phần đã cắt cong lên khỏi củ thành vòng xoắn lỏng (mặt vỏ nâu ở trong, mặt ruột
    /// trắng ở ngoài). Hết nhát thì rời dao, rơi xuống chậu vỏ và nằm lại. Gốc toạ độ ở lưỡi dao: +Z dọc củ về đầu xa
    /// (phía đã cắt), +Y pháp tuyến ngoài của củ.
    /// </summary>
    public sealed class PeelStrip : MonoBehaviour
    {
        const int Steps = 20;
        const float Thickness = 0.0015f;
        /// <summary>Bán kính cuộn khi dải còn bám lưỡi dao, và khi đã nằm trong chậu (duỗi ra lỏng hơn).</summary>
        public const float Curl = 0.008f, RestingCurl = 0.016f;
        Mesh _mesh;
        readonly Vector3[] _v = new Vector3[(Steps + 1) * 4];
        readonly Vector3[] _n = new Vector3[(Steps + 1) * 4];
        Vector3 _velocity, _spin, _target;
        float _floor;
        System.Action<PeelStrip> _landed;
        Renderer _renderer;

        public bool Falling { get; private set; }
        public bool Landed { get; private set; }

        public static PeelStrip Create(Transform parent, Material skin, Material flesh)
        {
            var go = new GameObject("PeelStrip");
            go.transform.SetParent(parent, false);
            var strip = go.AddComponent<PeelStrip>();
            strip._mesh = new Mesh { name = "PeelStrip" };
            strip._mesh.MarkDynamic();
            strip._mesh.vertices = strip._v;
            strip._mesh.subMeshCount = 2;
            var outer = new int[Steps * 6];
            var inner = new int[Steps * 6];
            for (int i = 0; i < Steps; i++)
            {
                // Vỏ: mặt hướng +N; ruột: cùng dải lùi vào trong, quấn ngược.
                int l = i * 2, r = l + 1, l1 = l + 2, r1 = l + 3;
                outer[i * 6] = l; outer[i * 6 + 1] = l1; outer[i * 6 + 2] = r;
                outer[i * 6 + 3] = r; outer[i * 6 + 4] = l1; outer[i * 6 + 5] = r1;
                int o = (Steps + 1) * 2;
                inner[i * 6] = o + l; inner[i * 6 + 1] = o + r; inner[i * 6 + 2] = o + l1;
                inner[i * 6 + 3] = o + r; inner[i * 6 + 4] = o + r1; inner[i * 6 + 5] = o + l1;
            }
            strip._mesh.SetTriangles(outer, 0, false);
            strip._mesh.SetTriangles(inner, 1, false);
            go.AddComponent<MeshFilter>().sharedMesh = strip._mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { skin, flesh };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return strip;
        }

        float _length;
        System.Func<float, float> _widthAt;

        /// <summary>Dựng hình dải dài length (m), cuộn bán kính curl; widthAt(t) cho bề rộng tại t = 0 (lưỡi dao) … 1 (đầu xa).</summary>
        public void Shape(float length, System.Func<float, float> widthAt, float curl = Curl)
        {
            _length = length;
            _widthAt = widthAt;
            int o = (Steps + 1) * 2;
            for (int i = 0; i <= Steps; i++)
            {
                float t = i / (float)Steps, s = t * length;
                // Vòng xoắn nở dần: bán kính lớn lên theo số vòng, dải dài không tự chồng lên mình.
                float r = curl * (1f + 0.35f * s / (2f * Mathf.PI * curl));
                float a = s / r;
                var p = new Vector3(0f, r * (1f - Mathf.Cos(a)), r * Mathf.Sin(a));
                var normal = new Vector3(0f, Mathf.Cos(a), -Mathf.Sin(a));
                float half = Mathf.Max(0.0015f, widthAt(t) * 0.5f);
                // Mép dải hơi gợn như vết dao tay.
                float wobble = 1f + 0.08f * Mathf.Sin(i * 1.7f + length * 40f);
                var side = Vector3.right * half * wobble;
                _v[i * 2] = p - side; _v[i * 2 + 1] = p + side;
                _n[i * 2] = _n[i * 2 + 1] = normal;
                _v[o + i * 2] = p - side - normal * Thickness; _v[o + i * 2 + 1] = p + side - normal * Thickness;
                _n[o + i * 2] = _n[o + i * 2 + 1] = -normal;
            }
            _mesh.vertices = _v;
            _mesh.normals = _n;
            _mesh.RecalculateBounds();
        }

        /// <summary>Rời dao, rơi xuống mặt chậu ở độ cao floor (thế giới) với vận tốc ngang đã tính để rơi vào lòng chậu; nằm
        /// xuống thì tâm dải ở đúng điểm target (x, z).</summary>
        public void Drop(float floor, Vector3 velocity, Vector3 target, System.Action<PeelStrip> landed)
        {
            Falling = true;
            _target = target;
            _floor = floor;
            _landed = landed;
            _velocity = velocity + Vector3.down * 0.1f;
            _spin = new Vector3(Random.Range(-160f, 160f), Random.Range(-90f, 90f), Random.Range(-160f, 160f));
            _renderer = GetComponent<Renderer>();
        }

        void Update()
        {
            if (!Falling || Landed)
                return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            _velocity += Physics.gravity * 0.7f * dt;
            transform.position += _velocity * dt;
            transform.rotation = Quaternion.Euler(_spin * dt) * transform.rotation;
            // Chạm mặt đống vỏ bằng mép thấp nhất của dải, không bằng gốc (gốc ở đầu dải, vòng xoắn có thể thấp hơn).
            float low = _renderer != null ? _renderer.bounds.min.y : transform.position.y;
            if (low > _floor)
                return;
            // Nằm xuống: dải duỗi lỏng thành hình chữ C đứng nghiêng trên đống vỏ (không cuộn chặt thành vòng chồng lên nhau).
            var p = transform.position;
            if (_widthAt != null)
            {
                Shape(_length, _widthAt, RestingCurl * Random.Range(1.5f, 2.5f));
                transform.rotation = Quaternion.Euler(Random.Range(-20f, 20f), Random.Range(0f, 360f), Random.Range(70f, 110f));
                low = _renderer.bounds.min.y;
            }
            var c = _renderer != null ? _renderer.bounds.center : p;
            transform.position = new Vector3(p.x + _target.x - c.x, transform.position.y + (_floor - low), p.z + _target.z - c.z);
            Landed = true;
            Falling = false;
            _landed?.Invoke(this);
        }
    }
}
