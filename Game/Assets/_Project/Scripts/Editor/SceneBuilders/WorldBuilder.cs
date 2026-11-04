using System;
using System.Collections.Generic;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Shift;
using HaiBanTay.Ship;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace HaiBanTay.Editor.SceneBuilders
{
    [Serializable]
    public struct WorldConfig
    {
        public WaterKind water;
        public float hour, surfaceHeight, radius;
        public bool followShiftClock;
        public ShiftDirector shift;
        public Transform rollPivot;
        public ShipMotion ship;
        public WorldLightingKeyframe[] keyframes;

        public static WorldConfig Sea(float hour = 12f) => new WorldConfig
        {
            water = WaterKind.Sea, hour = hour, surfaceHeight = -2f, radius = 400f,
            keyframes = TimeOfDayLogic.DefaultKeyframes,
        };
    }

    /// <summary>Mảnh thế giới độc lập model tàu; phông xa có thể gắn thêm dưới WorldRoot.</summary>
    public static class WorldBuilder
    {
        public const string MaterialFolder = "Assets/_Project/Resources/World";
        const string MeshFolder = "Assets/_Project/Art/Generated/World";

        public static Transform Build(Transform parent, WorldConfig cfg)
        {
            if (!float.IsFinite(cfg.radius) || cfg.radius < 20f || !float.IsFinite(cfg.surfaceHeight))
                throw new ArgumentOutOfRangeException(nameof(cfg), "Bán kính phải >= 20 m, độ cao phải hữu hạn.");
            TimeOfDayLogic.Evaluate(cfg.hour, cfg.keyframes);
            EnsureMaterials();
            var root = BuilderUtils.Group("WorldRoot", parent).transform;
            var ocean = BuilderUtils.Group("Ocean", root);
            ocean.transform.localPosition = Vector3.up * cfg.surfaceHeight;
            ocean.AddComponent<MeshFilter>().sharedMesh = OceanMesh(cfg.radius);
            var waterRenderer = ocean.AddComponent<MeshRenderer>();
            waterRenderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/Ocean_{cfg.water}.mat");
            waterRenderer.shadowCastingMode = ShadowCastingMode.Off;
            waterRenderer.lightProbeUsage = LightProbeUsage.Off;
            waterRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            BuilderUtils.SetRenderingLayer(ocean, WorldRenderingLayers.Exterior, false);

            float skyRadius = Mathf.Min(500f, Mathf.Max(120f, cfg.radius * 1.2f));
            var dome = BuilderUtils.Prim(PrimitiveType.Sphere, "SkyDome", root, Vector3.zero,
                Vector3.one * skyRadius * 2f, AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/SkyDome.mat"), false, false);
            BuilderUtils.MakeDistantBackdrop(dome);
            BuilderUtils.SetRenderingLayer(dome, WorldRenderingLayers.Exterior, false);

            var clouds = BuilderUtils.Group("SkyCumulus", root);
            clouds.AddComponent<MeshFilter>().sharedMesh = CloudMesh();
            var cloudRenderer = clouds.AddComponent<MeshRenderer>();
            cloudRenderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/SkyClouds.mat");
            BuilderUtils.MakeDistantBackdrop(clouds);
            BuilderUtils.SetRenderingLayer(clouds, WorldRenderingLayers.Exterior, false);

            var sun = BuilderUtils.Group("WorldSunMoon", root).AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.lightmapBakeType = LightmapBakeType.Realtime;
            sun.shadows = LightShadows.Hard;
            BuilderUtils.SetLightLayer(sun, WorldRenderingLayers.Exterior);
            RenderSettings.sun = sun;
            RenderSettings.skybox = null;

            var clock = root.gameObject.AddComponent<TimeOfDay>();
            clock.hour = cfg.hour;
            clock.followShiftClock = cfg.followShiftClock;
            clock.shift = cfg.shift;
            clock.ship = cfg.ship;
            clock.mainLight = sun;
            clock.sky = dome.GetComponent<Renderer>();
            clock.clouds = cloudRenderer;
            clock.ocean = waterRenderer;
            clock.water = cfg.water;
            clock.keyframes = cfg.keyframes;
            clock.Apply();
            var motion = root.gameObject.AddComponent<WorldMotion>();
            motion.pivot = cfg.rollPivot;
            motion.ship = cfg.ship;
            return root;
        }

        /// <summary>Resources giữ shader trong bản build cả khi sandbox không nằm trong BuildSettings.</summary>
        public static void EnsureMaterials()
        {
            AssetFactory.EnsureFolder(MaterialFolder);
            MaterialFor("SkyDome", "HaiBanTay/SkyDome");
            MaterialFor("SkyClouds", "HaiBanTay/SkyClouds");
            foreach (WaterKind water in Enum.GetValues(typeof(WaterKind)))
            {
                var mat = MaterialFor("Ocean_" + water, "HaiBanTay/Ocean");
                float amplitude = water == WaterKind.River ? 0.08f : water == WaterKind.Harbor ? 0.22f : 1f;
                mat.SetVector("_WaveA", new Vector4(0.766f, -0.643f, 0.065f * amplitude, 12f));
                mat.SetVector("_WaveB", new Vector4(0.914f, -0.407f, 0.055f * amplitude, 8.1f));
                mat.SetVector("_WaveC", new Vector4(0.990f, -0.139f, 0.045f * amplitude, 5.3f));
                mat.SetVector("_WaveD", new Vector4(0.978f, 0.208f, 0.035f * amplitude, 3.4f));
                mat.SetVector("_WaveE", new Vector4(0.883f, 0.469f, 0.025f * amplitude, 2.2f));
                mat.SetVector("_WaveF", new Vector4(0.766f, 0.643f, 0.02f * amplitude, 1.5f));
                mat.SetFloat("_RippleStrength", 0.22f * amplitude);
                mat.SetFloat("_WaveScale", 1f);
                mat.SetFloat("_WaveSpeed", water == WaterKind.River ? 0.45f : water == WaterKind.Harbor ? 0.65f : 1f);
                mat.SetFloat("_SkyReflection", water == WaterKind.River ? 0.28f : water == WaterKind.Harbor ? 0.5f : 0.75f);
                EditorUtility.SetDirty(mat);
            }
        }

        static Material MaterialFor(string name, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
                throw new InvalidOperationException("Thiếu shader " + shaderName);
            if (ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException(shaderName + ": " + string.Join("\n",
                    Array.ConvertAll(ShaderUtil.GetShaderMessages(shader), message => message.message)));
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Mesh CloudMesh()
        {
            const int sides = 16, rings = 8;
            var vertices = new List<Vector3>(); var normals = new List<Vector3>();
            var centers = new List<Vector3>(); var triangles = new List<int>();
            // Phân bố cố định, không dùng Random toàn cục; 24 cụm × 5 bầu trong một mesh/pass.
            for (int cloud = 0; cloud < 24; cloud++)
            {
                float angle = cloud * 2.399963f;
                float distance = 140f + cloud % 4 * 78f;
                var center = new Vector3(Mathf.Cos(angle) * distance, 58f + cloud % 5 * 14f, Mathf.Sin(angle) * distance);
                float size = 9f + cloud % 3 * 3f;
                for (int puff = 0; puff < 5; puff++)
                {
                    var c = center + new Vector3((puff - 2) * size * .78f, puff == 2 ? size * .38f : -size * .15f, Mathf.Sin(puff * 2.2f) * size * .35f);
                    var radius = new Vector3(size * 1.15f, size * (puff == 2 ? .95f : .65f), size);
                    int start = vertices.Count;
                    for (int r = 0; r <= rings; r++) for (int s = 0; s <= sides; s++)
                    {
                        float latitude = r * Mathf.PI / rings, longitude = s * Mathf.PI * 2 / sides;
                        var n = new Vector3(Mathf.Sin(latitude) * Mathf.Cos(longitude), Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Sin(longitude));
                        vertices.Add(c + Vector3.Scale(n, radius)); centers.Add(center);
                        normals.Add(new Vector3(n.x / radius.x, n.y / radius.y, n.z / radius.z).normalized);
                        if (r == rings || s == sides) continue;
                        int a = start + r * (sides + 1) + s, b = a + sides + 1;
                        triangles.AddRange(new[] { a,a+1,b,a+1,b+1,b });
                    }
                }
            }
            var mesh = new Mesh { name = "SkyCumulus", vertices = vertices.ToArray(), normals = normals.ToArray(), triangles = triangles.ToArray() };
            mesh.SetUVs(0, centers);
            // Chứa cả mây bão nở rộng và độ trôi ngang trong lượt demo.
            mesh.bounds = new Bounds(new Vector3(0,65,0), new Vector3(1000,200,1000));
            AssetFactory.EnsureFolder(MeshFolder);
            string path = MeshFolder + "/SkyCumulus.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old == null) { AssetDatabase.CreateAsset(mesh,path); return mesh; }
            EditorUtility.CopySerialized(mesh,old); UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(old); return old;
        }

        // Lưới đồng tâm: gần tâm dày, xa tâm thưa; một mesh, một draw, không có khe giữa các vòng.
        static Mesh OceanMesh(float radius)
        {
            AssetFactory.EnsureFolder(MeshFolder);
            string path = MeshFolder + "/Ocean_" + radius.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
                return existing;
            const int rings = 128, sectors = 256;
            var vertices = new Vector3[1 + rings * sectors];
            var normals = new Vector3[vertices.Length];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            var indices = new int[sectors * (1 + 2 * (rings - 1)) * 3];
            for (int r = 1; r <= rings; r++)
            {
                float distance = radius * Mathf.Pow((float)r / rings, 2f);
                for (int s = 0; s < sectors; s++)
                {
                    float angle = s * 2f * Mathf.PI / sectors;
                    vertices[1 + (r - 1) * sectors + s] = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
                }
            }
            int k = 0;
            for (int s = 0; s < sectors; s++)
            {
                indices[k++] = 0;
                indices[k++] = 1 + (s + 1) % sectors;
                indices[k++] = 1 + s;
            }
            for (int r = 1; r < rings; r++)
                for (int s = 0; s < sectors; s++)
                {
                    int a = 1 + (r - 1) * sectors + s;
                    int b = 1 + (r - 1) * sectors + (s + 1) % sectors;
                    int c = a + sectors, d = b + sectors;
                    indices[k++] = a; indices[k++] = d; indices[k++] = c;
                    indices[k++] = a; indices[k++] = b; indices[k++] = d;
                }
            var mesh = new Mesh { name = "WorldOcean", vertices = vertices, normals = normals, triangles = indices };
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(radius * 2f + 4f, 8f, radius * 2f + 4f));
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}
