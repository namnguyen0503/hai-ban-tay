using System.Collections.Generic;
using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Củ khoai sinh lúc chạy, hai submesh trên một lưới: vỏ (0) và ruột (1). Trục dài là Z: đầu gần (u = 0) ở −Z, đầu xa
    /// (u = 1) ở +Z; φ là góc quanh trục, tính từ +X sang +Y. Vỏ chia dải dọc theo φ, gọt tới đâu bỏ tam giác vỏ tới đó.
    /// Ruột nằm trong vỏ, mỗi dải là một mặt vát như vết dao thật, nên củ đã gọt có cạnh chứ không tròn trơn.
    /// Củ cải (S16: "đống củ cải và khoai tây") cùng lưới: thân tròn, phình gần cuống (đầu xa), đầu gần thuôn thành rễ, vỏ nhẵn.
    /// </summary>
    public sealed class PotatoMesh
    {
        public const int Rings = 16, Segments = 24, PerBand = Segments / PotatoPeelLogic.Bands;
        public const float BandAngle = Mathf.PI * 2f / PotatoPeelLogic.Bands;
        /// <summary>Đầu và cuối đường dao (u); hai chỏm nhỏ hơn thì dao không với.</summary>
        public const float StrokeFrom = 0.985f, StrokeTo = 0.015f;
        // Mặt vát của ruột cách trục 0,88 bán kính vỏ ở giữa dải; cạnh giữa hai dải vẫn nằm trong vỏ.
        const float FleshDepth = 0.88f, FleshEdge = 0.965f, FleshEnds = 0.95f;

        public readonly Mesh Mesh;
        /// <summary>Bán trục (m) theo X, Y, Z.</summary>
        public readonly Vector3 Radii;
        public readonly bool Turnip;
        readonly float _n1, _n2, _n3, _n4;
        readonly List<int> _skin = new List<int>(Rings * Segments * 6);
        readonly List<int> _flesh = new List<int>(Rings * Segments * 6);
        int _doneBands = -1, _band = -1;
        float _knifeU = -1f;
        bool _fleshShown;
        // Độ dài cung theo đường giữa mỗi dải, từ đầu xa (u = StrokeFrom) về u, lấy mẫu đều theo u.
        readonly float[,] _arc = new float[PotatoPeelLogic.Bands, ArcSamples + 1];
        const int ArcSamples = 32;

        public PotatoMesh(int seed, float scale = 1f, bool turnip = false)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            Turnip = turnip;
            Radii = turnip
                ? new Vector3(0.034f * (0.95f + 0.1f * R()), 0.033f * (0.95f + 0.1f * R()), 0.042f * (0.92f + 0.16f * R())) * scale
                : new Vector3(0.034f * (0.9f + 0.2f * R()), 0.029f * (0.9f + 0.2f * R()), 0.046f * (0.88f + 0.24f * R())) * scale;
            _n1 = R() * 40f; _n2 = R() * 40f; _n3 = R() * 40f; _n4 = R() * 40f;
            Mesh = new Mesh { name = turnip ? "Turnip" : "Potato" };
            // Ảnh vỏ củ cải trải dọc củ một lần (rễ trắng → cuống tím); ảnh vỏ khoai lặp.
            float along = turnip ? 1f : 1.4f;
            Mesh.MarkDynamic();
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            for (int i = 0; i <= Rings; i++)
                for (int j = 0; j <= Segments; j++)
                {
                    float u = i / (float)Rings, phi = j * Mathf.PI * 2f / Segments;
                    v.Add(SkinPoint(u, phi));
                    uv.Add(new Vector2(phi / Mathf.PI, u * along));
                }
            int fleshStart = v.Count;
            // Ruột: mỗi dải một cột đỉnh riêng để cạnh giữa hai mặt vát sắc như vết dao.
            for (int b = 0; b < PotatoPeelLogic.Bands; b++)
                for (int i = 0; i <= Rings; i++)
                    for (int k = 0; k <= PerBand; k++)
                    {
                        float u = i / (float)Rings, phi = (b * PerBand + k) * Mathf.PI * 2f / Segments;
                        v.Add(FleshPoint(u, phi, b));
                        uv.Add(new Vector2(phi / Mathf.PI, u * along));
                    }
            for (int b = 0; b < PotatoPeelLogic.Bands; b++)
                for (int i = 0; i < Rings; i++)
                    for (int k = 0; k < PerBand; k++)
                    {
                        int a = fleshStart + (b * (Rings + 1) + i) * (PerBand + 1) + k, c = a + PerBand + 1;
                        Quad(_flesh, a, a + 1, c, c + 1);
                    }
            Mesh.SetVertices(v);
            Mesh.SetUVs(0, uv);
            Mesh.subMeshCount = 2;
            SetPeeled(0, -1, 1f);
            Mesh.SetTriangles(_flesh, 1, false);
            Mesh.RecalculateNormals();
            SmoothSkinSeams();
            Mesh.RecalculateBounds();
            _fleshShown = true;
            for (int b = 0; b < PotatoPeelLogic.Bands; b++)
            {
                float phi = BandCenter(b);
                var last = FleshPoint(StrokeFrom, phi, b);
                for (int s = 1; s <= ArcSamples; s++)
                {
                    var p = FleshPoint(Mathf.Lerp(StrokeFrom, StrokeTo, s / (float)ArcSamples), phi, b);
                    _arc[b, s] = _arc[b, s - 1] + Vector3.Distance(p, last);
                    last = p;
                }
            }
        }

        /// <summary>Góc giữa dải (rad).</summary>
        public static float BandCenter(int band) => (band + 0.5f) * BandAngle;

        /// <summary>Vị trí dao theo nhát (0 đầu xa → 1 đầu gần).</summary>
        public static float KnifeU(float stroke) => Mathf.Lerp(StrokeFrom, StrokeTo, Mathf.Clamp01(stroke));

        /// <summary>Gồ ghề liên tục qua đường nối φ = 0 / 2π.</summary>
        float Lump(float u, float phi)
        {
            float c = Mathf.Cos(phi), s = Mathf.Sin(phi);
            float a = Mathf.PerlinNoise(_n1 + 1.3f * c + 2.1f * u, _n2 + 1.3f * s + 1.6f * u) * 2f - 1f;
            float b = Mathf.PerlinNoise(_n3 + 3.7f * c + 4.4f * u, _n4 + 3.7f * s + 3.9f * u) * 2f - 1f;
            return Turnip ? 1f + 0.025f * a + 0.01f * b : 1f + 0.075f * a + 0.03f * b;
        }

        Vector3 Shape(float u, float phi, float radial, float ends)
        {
            float l = Lump(u, phi), r, z = -Mathf.Cos(Mathf.PI * u);
            if (Turnip)
            {
                // Phình to nhất ở 0,62 chiều dài về phía cuống; đầu gần thuôn nhọn, kéo dài thành rễ.
                r = Mathf.Sin(Mathf.PI * Mathf.Pow(u, 1.45f)) * l * radial;
                z *= 1f + 0.35f * Mathf.Pow(1f - u, 6f);
            }
            else
            {
                // Đầu xa hơi to hơn đầu gần, như củ khoai thật.
                r = Mathf.Sin(Mathf.PI * u) * l * (1f + 0.08f * (u - 0.5f)) * radial;
            }
            return new Vector3(Mathf.Cos(phi) * Radii.x * r, Mathf.Sin(phi) * Radii.y * r,
                z * Radii.z * (1f + 0.4f * (l - 1f)) * ends);
        }

        public Vector3 SkinPoint(float u, float phi) => Shape(u, phi, 1f, 1f);

        /// <summary>Điểm trên mặt vát của ruột thuộc dải band.</summary>
        public Vector3 FleshPoint(float u, float phi, int band)
        {
            float d = phi - BandCenter(band);
            return Shape(u, phi, Mathf.Min(FleshDepth / Mathf.Cos(d), FleshEdge), FleshEnds);
        }

        /// <summary>Pháp tuyến ngoài và hướng dọc củ về đầu xa tại đường giữa dải (hệ toạ độ của củ).</summary>
        public void Frame(int band, float u, out Vector3 point, out Vector3 normal, out Vector3 towardFar)
        {
            float phi = BandCenter(band);
            u = Mathf.Clamp(u, 0.1f, 0.9f);
            const float e = 0.01f;
            point = FleshPoint(u, phi, band);
            var du = FleshPoint(u + e, phi, band) - FleshPoint(u - e, phi, band);
            var dphi = FleshPoint(u, phi + e, band) - FleshPoint(u, phi - e, band);
            // Gần hai đầu củ pháp tuyến ngả theo trục dài; dao thật vẫn nằm gần tiếp tuyến dải, không dựng đứng.
            var radial = new Vector3(Mathf.Cos(phi), Mathf.Sin(phi), 0f);
            normal = (Vector3.Cross(dphi, du).normalized + radial * 1.2f).normalized;
            towardFar = Vector3.ProjectOnPlane(du, normal).normalized;
        }

        /// <summary>Độ dài dải vỏ đã cắt (m) khi dao ở nhát stroke của dải band.</summary>
        public float CutLength(int band, float stroke)
        {
            float x = Mathf.Clamp01(stroke) * ArcSamples;
            int i = Mathf.Min(ArcSamples - 1, (int)x);
            return Mathf.Lerp(_arc[band, i], _arc[band, i + 1], x - i);
        }

        /// <summary>Bề rộng dải vỏ của dải band tại u (dây cung giữa hai mép dải).</summary>
        public float BandWidth(int band, float u)
        {
            float phi = BandCenter(band);
            return Vector3.Distance(SkinPoint(u, phi - BandAngle * 0.5f), SkinPoint(u, phi + BandAngle * 0.5f));
        }

        /// <summary>Cập nhật vỏ: các dải trước doneBands đã gọt hết; dải band mất phần dao đã đi qua (u ≥ knifeU).</summary>
        public void SetPeeled(int doneBands, int band, float knifeU)
        {
            if (doneBands == _doneBands && band == _band && Mathf.Abs(knifeU - _knifeU) < 1e-4f)
                return;
            _doneBands = doneBands;
            _band = band;
            _knifeU = knifeU;
            _skin.Clear();
            for (int i = 0; i < Rings; i++)
            {
                float center = (i + 0.5f) / Rings;
                for (int j = 0; j < Segments; j++)
                {
                    int b = j / PerBand;
                    if (b < doneBands || (b == band && center >= knifeU))
                        continue;
                    int a = i * (Segments + 1) + j, c = a + Segments + 1;
                    Quad(_skin, a, a + 1, c, c + 1);
                }
            }
            Mesh.SetTriangles(_skin, 0, false);
        }

        /// <summary>Củ còn nguyên vỏ trong xô không cần vẽ ruột (đỡ một lượt vẽ).</summary>
        public void ShowFlesh(bool on)
        {
            if (_fleshShown == on)
                return;
            _fleshShown = on;
            Mesh.SetTriangles(on ? _flesh : new List<int>(), 1, false);
        }

        static void Quad(List<int> t, int a, int b, int c, int d)
        {
            // a→b theo φ, a→c theo u: (a, b, c) hướng ra ngoài.
            t.Add(a); t.Add(b); t.Add(c);
            t.Add(b); t.Add(d); t.Add(c);
        }

        void SmoothSkinSeams()
        {
            var n = new List<Vector3>();
            Mesh.GetNormals(n);
            for (int i = 0; i <= Rings; i++)
            {
                int a = i * (Segments + 1), b = a + Segments;
                n[a] = n[b] = (n[a] + n[b]).normalized;
            }
            for (int j = 0; j <= Segments; j++)
            {
                n[j] = Vector3.back;
                n[Rings * (Segments + 1) + j] = Vector3.forward;
            }
            Mesh.SetNormals(n);
        }
    }
}
