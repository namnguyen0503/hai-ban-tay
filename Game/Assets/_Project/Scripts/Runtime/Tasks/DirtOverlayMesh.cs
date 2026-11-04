using UnityEngine;

namespace HaiBanTay.Tasks
{
    /// <summary>
    /// Lưới lớp bẩn bám trong lòng vật theo đúng phép trải của <see cref="ScrubSurface"/>: vòng tròn đồng tâm trên đáy, phần ngoài bán kính
    /// đáy (nếu có thành) dựng lên dọc thành trong. uv của mỗi đỉnh là uv mặt nạ (không dựa vào uv của mesh nguyên thủy), nên shader
    /// DirtOverlay đọc đúng ô mặt nạ dưới từng điểm. Lưới dựng lúc chạy, nằm sát mặt (lệch 2 mm theo pháp tuyến).
    /// </summary>
    public static class DirtOverlayMesh
    {
        public static Mesh Build(ScrubSurface surface, int rings = 22, int segments = 48, float lift = 0.002f)
        {
            var mesh = new Mesh { name = "DirtOverlay" };
            int ringCount = Mathf.Max(2, rings);
            var vertices = new Vector3[1 + ringCount * segments];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            surface.Evaluate(Vector2.zero, out var p0, out var n0);
            vertices[0] = p0 + n0 * lift;
            normals[0] = n0;
            uv[0] = surface.MaskUv(Vector2.zero);
            for (int r = 1; r <= ringCount; r++)
            {
                float radius = surface.Domain * r / ringCount;
                for (int s = 0; s < segments; s++)
                {
                    float a = s * Mathf.PI * 2f / segments;
                    var c = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                    surface.Evaluate(c, out var p, out var n);
                    int i = 1 + (r - 1) * segments + s;
                    vertices[i] = p + n * lift;
                    normals[i] = n;
                    uv[i] = surface.MaskUv(c);
                }
            }
            var triangles = new int[(segments + (ringCount - 1) * segments * 2) * 3];
            int t = 0;
            for (int s = 0; s < segments; s++)
            {
                triangles[t++] = 0;
                triangles[t++] = 1 + (s + 1) % segments;
                triangles[t++] = 1 + s;
            }
            for (int r = 1; r < ringCount; r++)
                for (int s = 0; s < segments; s++)
                {
                    int a = 1 + (r - 1) * segments + s, b = 1 + (r - 1) * segments + (s + 1) % segments;
                    int c = a + segments, d = b + segments;
                    triangles[t++] = a; triangles[t++] = b; triangles[t++] = c;
                    triangles[t++] = b; triangles[t++] = d; triangles[t++] = c;
                }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
