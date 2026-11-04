using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Dò nhìn xuyên bằng render (không cần đọc mesh, chạy cả với mesh Art không bật Read/Write): vẽ mọi vật trong tầm bằng FacingId
    /// (hai mặt, giữ mặt gần nhất) từ một điểm nhìn ở tỉ lệ điện thoại 2,2:1. Pixel mà mặt gần nhất là mặt sau của vật liệu chỉ vẽ mặt
    /// trước là chỗ trong game bị cull, nhìn xuyên ra sau: lòng bát, bao, sọt chỉ có vỏ ngoài; lỗ cổ áo người chơi; camera lọt vào trong vật.
    /// Mặt trước/sau tự chỉnh theo một khối lập phương đo trước (chiều lật Y của RenderTexture tùy API đồ họa).
    /// </summary>
    public sealed class SeeThroughProbe : System.IDisposable
    {
        public const int Width = 594, Height = 270;
        static readonly int IdProp = Shader.PropertyToID("_FacingId");
        static readonly int TwoSidedProp = Shader.PropertyToID("_FacingTwoSided");
        static readonly int FlipProp = Shader.PropertyToID("_FacingFlip");
        static readonly int PassProp = Shader.PropertyToID("_FacingPass");

        readonly Material _mat;
        readonly RenderTexture _rt;
        readonly Texture2D _read;
        readonly CommandBuffer _cb = new CommandBuffer { name = "SeeThroughProbe" };
        readonly List<(Renderer r, int sub, bool twoSided)> _draws = new List<(Renderer, int, bool)>();
        Color32[] _pixels;
        float _flip;

        /// <summary>Submesh không vẽ (đầu người chơi bị ẩn với camera chính).</summary>
        public Renderer skipRenderer;
        public int skipSubmesh = -1;

        public struct Hit
        {
            public Renderer renderer;
            public int submesh;
            public int pixels;
            public string Material
            {
                get
                {
                    var mats = renderer != null ? renderer.sharedMaterials : new Material[0];
                    var m = mats.Length == 0 ? null : mats[Mathf.Min(submesh, mats.Length - 1)];
                    return m != null ? m.name : "?";
                }
            }
        }

        public SeeThroughProbe()
        {
            var shader = Shader.Find("Hidden/HaiBanTay/FacingId");
            if (shader == null) throw new System.InvalidOperationException("thiếu shader Hidden/HaiBanTay/FacingId");
            _mat = new Material(shader);
            _rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            _rt.Create();
            _read = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true);
            Calibrate();
        }

        public void Dispose()
        {
            _cb.Release();
            _rt.Release();
            Object.Destroy(_rt);
            Object.Destroy(_read);
            Object.Destroy(_mat);
        }

        /// <summary>Mặt trước/sau đúng chiều: khối lập phương kín nhìn từ ngoài phải toàn mặt trước.</summary>
        public int CalibrationFront { get; private set; }
        public int CalibrationBack { get; private set; }

        void Calibrate()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(cube.GetComponent<Collider>());
            var at = new Vector3(0f, -3000f, 0f);
            cube.transform.SetPositionAndRotation(at + new Vector3(0f, 0f, 2f), Quaternion.Euler(20f, 30f, 0f));
            _flip = 0f;
            var hits = Render(View(at, Quaternion.identity), 60f, 0.05f, 100f, new[] { cube.GetComponent<Renderer>() }, out int front);
            int back = hits.Sum(h => h.pixels);
            if (back > front)
            {
                _flip = 1f;
                hits = Render(View(at, Quaternion.identity), 60f, 0.05f, 100f, new[] { cube.GetComponent<Renderer>() }, out front);
                back = hits.Sum(h => h.pixels);
            }
            CalibrationFront = front;
            CalibrationBack = back;
            Object.DestroyImmediate(cube);
        }

        public static Matrix4x4 View(Vector3 position, Quaternion rotation) =>
            Matrix4x4.Inverse(Matrix4x4.TRS(position, rotation, new Vector3(1f, 1f, -1f)));

        public static bool CullsBack(Material m) => m == null || !m.HasProperty("_Cull") || m.GetFloat("_Cull") > 0.5f;

        /// <summary>Số submesh của chính renderer: renderer gộp tĩnh (static batching) trỏ vào mesh gộp của cả cảnh, chỉ dùng số vật liệu
        /// của nó (DrawRenderer tự cộng chỉ số submesh đầu trong mesh gộp).</summary>
        static int SubmeshCount(Renderer r)
        {
            if (r.isPartOfStaticBatch) return r.sharedMaterials.Length;
            var mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
            return mesh != null ? mesh.subMeshCount : 0;
        }

        /// <summary>Renderer vẽ được (mesh, không phải hạt/đường), chỉ mức LOD0 của LODGroup.</summary>
        public static List<Renderer> Candidates(System.Func<Renderer, bool> keep = null)
        {
            var lodOther = new HashSet<Renderer>();
            foreach (var g in Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
            {
                var lods = g.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                    foreach (var r in lods[i].renderers) if (r != null) lodOther.Add(r);
            }
            return Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(r => (r is MeshRenderer || r is SkinnedMeshRenderer) && !lodOther.Contains(r)
                            && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly && SubmeshCount(r) > 0 && (keep == null || keep(r)))
                .ToList();
        }

        /// <summary>Vẽ các renderer đang hiện trong tầm từ điểm nhìn cho trước; trả về số pixel nhìn xuyên của từng (renderer, submesh).</summary>
        public List<Hit> Render(Matrix4x4 view, float fov, float near, float far, IEnumerable<Renderer> renderers, out int front)
        {
            _draws.Clear();
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.forceRenderingOff) continue;
                var mats = r.sharedMaterials;
                int subs = SubmeshCount(r);
                for (int s = 0; s < subs; s++)
                {
                    var mat = mats.Length == 0 ? null : mats[Mathf.Min(s, mats.Length - 1)];
                    // Vật liệu ẩn với camera chính (đầu, mũ người chơi) và vật trong suốt không tính.
                    if (mat == null || mat.renderQueue >= 2500 || mat.HasProperty("_HidePlayerHead")) continue;
                    if (r == skipRenderer && s == skipSubmesh) continue;
                    _draws.Add((r, s, !CullsBack(mat)));
                }
            }
            _cb.Clear();
            _cb.SetRenderTarget(_rt);
            _cb.ClearRenderTarget(true, true, Color.clear);
            _cb.SetViewProjectionMatrices(view, GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(fov, Width / (float)Height, near, far), true));
            _cb.SetGlobalFloat(FlipProp, _flip);
            for (int pass = 0; pass < 2; pass++)
            {
                _cb.SetGlobalFloat(PassProp, pass);
                for (int k = 0; k < _draws.Count; k++)
                {
                    var (r, sub, twoSided) = _draws[k];
                    _cb.SetGlobalFloat(IdProp, k + 1);
                    _cb.SetGlobalFloat(TwoSidedProp, twoSided ? 1f : 0f);
                    _cb.DrawRenderer(r, _mat, sub, 0);
                }
            }
            Graphics.ExecuteCommandBuffer(_cb);
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            _read.Apply(false);
            RenderTexture.active = prev;
            _pixels = _read.GetPixels32();
            var counts = new Dictionary<int, int>();
            front = 0;
            foreach (var p in _pixels)
            {
                if (p.a == 0) continue;
                if (p.r > 127)
                {
                    int id = p.g + p.b * 256;
                    counts[id] = counts.TryGetValue(id, out int n) ? n + 1 : 1;
                }
                else front++;
            }
            return counts.Where(kv => kv.Key >= 1 && kv.Key <= _draws.Count)
                .Select(kv => new Hit { renderer = _draws[kv.Key - 1].r, submesh = _draws[kv.Key - 1].sub, pixels = kv.Value })
                .OrderByDescending(h => h.pixels).ToList();
        }

        /// <summary>Theo camera thật (ma trận nhìn, góc mở, mặt cắt gần của nó), chỉ vật trong range mét.</summary>
        public List<Hit> Render(Camera cam, IEnumerable<Renderer> renderers, float range)
        {
            var eye = cam.transform.position;
            float r2 = range * range;
            return Render(cam.worldToCameraMatrix, cam.fieldOfView, cam.nearClipPlane, Mathf.Min(cam.farClipPlane, range + 2f),
                renderers.Where(r => r != null && r.bounds.SqrDistance(eye) < r2 && (cam.cullingMask & (1 << r.gameObject.layer)) != 0), out _);
        }

        /// <summary>Ảnh lần vẽ cuối: đỏ = nhìn xuyên, xám = mặt trước (sáng tối theo lệnh vẽ), đen = trống.</summary>
        public void SaveOverlay(string path)
        {
            if (_pixels == null) return;
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            var o = new Color32[_pixels.Length];
            for (int i = 0; i < o.Length; i++)
            {
                var p = _pixels[i];
                if (p.a == 0) o[i] = new Color32(0, 0, 0, 255);
                else if (p.r > 127) o[i] = new Color32(255, 30, 30, 255);
                else
                {
                    byte g = (byte)(70 + (p.g * 37 + p.b * 11) % 120);
                    o[i] = new Color32(g, g, g, 255);
                }
            }
            tex.SetPixels32(o);
            tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
        }
    }
}
