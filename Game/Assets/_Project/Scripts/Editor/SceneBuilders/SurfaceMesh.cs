using System.Collections.Generic;
using HaiBanTay.Editor.Setup;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>
    /// Mesh primitive có UV theo mét thật cho vật liệu có texture (Art/Surfaces): khối 6×3 m lặp texture 3×1,5 lần
    /// thay vì kéo giãn một ô. Mesh lưu thành asset dùng chung giữa các scene, có UV2 cho lightmap.
    /// </summary>
    public static class SurfaceMesh
    {
        public const string Folder = AssetFactory.Root + "/Art/Meshes";

        static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        public static Mesh For(PrimitiveType type, Mesh source, Vector3 size, float tile, Vector3 origin = default)
        {
            size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            string key = $"Gen_{type}_{Q(size.x)}x{Q(size.y)}x{Q(size.z)}_t{Q(tile)}";
            if (origin != Vector3.zero) key += $"_o{Q(origin.x)}_{Q(origin.y)}_{Q(origin.z)}";
            if (Cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            string path = $"{Folder}/{key}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                AssetFactory.EnsureFolder(Folder);
                mesh = Object.Instantiate(source);
                mesh.name = key;
                mesh.uv = BuildUv(type, source, size, tile, origin);
                Unwrapping.GenerateSecondaryUVSet(mesh);
                mesh.RecalculateTangents();
                AssetDatabase.CreateAsset(mesh, path);
            }
            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>Đĩa tròn bán kính 0,5 trong mặt phẳng XY, mặt trước hướng +Z; UV phủ 0..1, v theo +Y.</summary>
        public static Mesh Disk()
        {
            const string key = "Gen_Disk";
            if (Cache.TryGetValue(key, out var cached) && cached != null)
                return cached;
            string path = $"{Folder}/{key}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                AssetFactory.EnsureFolder(Folder);
                const int segments = 48;
                var verts = new Vector3[segments + 1];
                var uv = new Vector2[segments + 1];
                var normals = new Vector3[segments + 1];
                var tris = new int[segments * 3];
                uv[0] = new Vector2(0.5f, 0.5f);
                normals[0] = Vector3.forward;
                for (int i = 0; i < segments; i++)
                {
                    float a = i * 2f * Mathf.PI / segments;
                    float x = Mathf.Cos(a) * 0.5f, y = Mathf.Sin(a) * 0.5f;
                    verts[i + 1] = new Vector3(x, y, 0f);
                    // Nhìn từ +Z thì +X nằm bên trái: lật u để ảnh không bị soi gương.
                    uv[i + 1] = new Vector2(0.5f - x, 0.5f + y);
                    normals[i + 1] = Vector3.forward;
                    tris[i * 3] = 0;
                    tris[i * 3 + 1] = i + 1;
                    tris[i * 3 + 2] = (i + 1) % segments + 1;
                }
                mesh = new Mesh { name = key, vertices = verts, uv = uv, normals = normals, triangles = tris };
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, path);
            }
            Cache[key] = mesh;
            return mesh;
        }

        static string Q(float v) => Mathf.RoundToInt(v * 100f).ToString();

        static Vector2[] BuildUv(PrimitiveType type, Mesh m, Vector3 size, float tile, Vector3 origin)
        {
            var verts = m.vertices;
            var normals = m.normals;
            var uv = m.uv;
            var result = new Vector2[verts.Length];
            float inv = 1f / Mathf.Max(0.01f, tile);
            for (int i = 0; i < verts.Length; i++)
            {
                var n = normals[i];
                var p = Vector3.Scale(verts[i], size) + origin;
                switch (type)
                {
                    case PrimitiveType.Plane:
                        // Plane gốc 10×10 đơn vị.
                        result[i] = new Vector2(uv[i].x * size.x * 10f, uv[i].y * size.z * 10f) * inv;
                        break;
                    case PrimitiveType.Cylinder when Mathf.Abs(n.y) < 0.5f:
                    {
                        float circumference = Mathf.PI * (size.x + size.z) * 0.5f;
                        result[i] = new Vector2(uv[i].x * circumference, uv[i].y * size.y * 2f) * inv;
                        break;
                    }
                    case PrimitiveType.Sphere:
                    case PrimitiveType.Capsule:
                    {
                        float circumference = Mathf.PI * (size.x + size.z) * 0.5f;
                        result[i] = new Vector2(uv[i].x * circumference, uv[i].y * circumference * 0.5f) * inv;
                        break;
                    }
                    default:
                        result[i] = BoxProject(p, Vector3.Scale(n, new Vector3(1f / Mathf.Max(size.x, 1e-4f), 1f / Mathf.Max(size.y, 1e-4f), 1f / Mathf.Max(size.z, 1e-4f)))) * inv;
                        break;
                }
            }
            return result;
        }

        // Chiếu theo trục pháp tuyến trội (sau khi co giãn). Mặt đứng lấy y làm v để vân gỗ/gạch dựng đúng chiều.
        static Vector2 BoxProject(Vector3 p, Vector3 n)
        {
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            if (ay >= ax && ay >= az)
                return new Vector2(p.x, p.z);
            if (ax >= az)
                return new Vector2(p.z * Mathf.Sign(n.x), p.y);
            return new Vector2(-p.x * Mathf.Sign(n.z), p.y);
        }
    }
}
