using System;
using System.Linq;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>Làm nổi đôi rồng chầu nguyệt theo ảnh Nhà Rồng; không đổi mặt đứng, mái hay collider.</summary>
    public static class NhaRongRoofBuilder
    {
        public static void Enhance(GameObject environment)
        {
            var filter = environment.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == "NhaRongCustoms");
            var mesh = UnityEngine.Object.Instantiate(filter.sharedMesh);
            var vertices = mesh.vertices;
            var world = vertices.Select(filter.transform.TransformPoint).ToArray();
            var crest = new bool[vertices.Length];
            // Các primitive trang trí tách vertex khỏi mái: chỉ chọn tam giác hoàn toàn trên sống nóc.
            var indices = mesh.triangles;
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = indices[i], b = indices[i+1], c = indices[i+2];
                if (world[a].y > 7.6f && world[b].y > 7.6f && world[c].y > 7.6f)
                    crest[a] = crest[b] = crest[c] = true;
            }
            if (crest.Count(v => v) < 100) throw new InvalidOperationException("Không tìm thấy cụm rồng Nhà Rồng trong mesh nhập.");
            var moon = new Vector3(6f,8.95f,-56f);
            for (int i = 0; i < vertices.Length; i++)
            {
                if (!crest[i]) continue;
                var p = world[i];
                // Mặt nguyệt vẫn tròn; thân cong, đầu và sừng cao hơn đường mái khi nhìn gần kè.
                if (Mathf.Abs(p.x - moon.x) < .6f && p.y > 8.5f)
                    p = moon + (p - moon) * 1.65f + Vector3.up * .96f;
                else p.y = 8.2f + (p.y - 8f) * 1.8f;
                vertices[i] = filter.transform.InverseTransformPoint(p);
            }
            mesh.vertices = vertices;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            filter.sharedMesh = KeppelPortBuilder.Save(mesh, "NhaRongCustoms_Crest");
        }
    }
}
