using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HaiBanTay.Editor.SceneBuilders;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Ship;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace HaiBanTay.Editor.Import
{
    /// <summary>Import the delivered environments only; scene builders remain their consumers.</summary>
    public static class EnvImporter
    {
        public static readonly string[] Areas = { "NhaRong", "SaigonStreet", "Marseille" };
        public const string MaterialRoot = EnvLayout.Root + "Materials/";
        static string Staging => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Staging/art_env"));
        static string ProjectPath(string path) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));

        [MenuItem("HaiBanTay/Art/Import Environments")]
        public static void Run()
        {
            try
            {
                foreach (string area in Areas)
                    foreach (string file in new[] { area + ".fbx", "layout.json" })
                        if (!File.Exists(Path.Combine(Staging, area, file)))
                            throw new FileNotFoundException("Thiếu môi trường", Path.Combine(Staging, area, file));
                foreach (string area in Areas)
                    foreach (string file in new[] { area + ".fbx", "layout.json" })
                        CopyFile(Path.Combine(Staging, area, file), EnvLayout.Root + area + "/" + file);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                foreach (string area in Areas)
                {
                    string path = EnvLayout.Root + area + "/" + area + ".fbx";
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (model == null)
                        throw new InvalidOperationException("Không nạp được " + path);
                    foreach (string slot in model.GetComponentsInChildren<Renderer>(true)
                        .SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).Distinct())
                    {
                        string source = Path.Combine(Staging, "_textures", slot);
                        if (!Directory.Exists(source))
                            continue; // The delivery explicitly includes flat/procedural slots without maps.
                        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                            if (new[] { ".png", ".jpg", ".exr" }.Contains(Path.GetExtension(file).ToLowerInvariant()))
                                CopyFile(file, EnvLayout.Root + area + "/Textures/" + slot + "/"
                                    + Path.GetRelativePath(source, file).Replace('\\', '/'));
                    }
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var materials = new Dictionary<string, Material>();
                foreach (string area in Areas)
                    MakePrefab(EnvLayout.Load(area), materials);
                AssetDatabase.SaveAssets();
                Debug.Log($"EnvImporter: {Areas.Length} khu, {materials.Count} vật liệu, 0 lỗi");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogError("EnvImporter: " + error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        static void CopyFile(string source, string assetPath)
        {
            string target = ProjectPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            if (!File.Exists(target) || !File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(target)))
                File.Copy(source, target, true);
        }

        static Material MaterialFor(string area, Material original, Dictionary<string, Material> cache)
        {
            string slot = original.name;
            if (cache.TryGetValue(slot, out var cached))
                return cached;
            string path = MaterialRoot + "Gen_Env_" + slot + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            // Model UVs are metres. Clone shared surfaces to change tiling without changing old scenes' materials.
            var surface = AssetDatabase.LoadAssetAtPath<Material>(AssetFactory.MaterialsFolder + "/Gen_" + slot + ".mat");
            var source = surface != null ? surface : original;
            bool create = material == null;
            if (create)
                material = new Material(source);
            else
                material.CopyPropertiesFromMaterial(source);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("Thiếu shader URP/Lit.");
            var color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.color;
            material.shader = shader;
            material.name = "Gen_Env_" + slot;
            material.SetColor("_BaseColor", color);
            string folder = ArtImporter.SurfaceRoot + slot;
            if (!AssetDatabase.IsValidFolder(folder))
                folder = EnvLayout.Root + area + "/Textures/" + slot;
            if (AssetDatabase.IsValidFolder(folder))
            {
                var textures = AssetDatabase.FindAssets("t:Texture2D", new[] { folder })
                    .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal).ToArray();
                string Pick(params string[] keys) => textures.FirstOrDefault(p =>
                    keys.Any(k => Path.GetFileNameWithoutExtension(p).ToLowerInvariant().Contains(k)));
                string colorMap = Pick("_color", "_diff", "_albedo", "_basecolor", "_base_color", "_col");
                string normalMap = Pick("_normalgl", "_nor_gl", "_normal", "_nor");
                if (colorMap == null)
                    throw new InvalidOperationException("Surface không có color map: " + folder);
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(colorMap));
                if (normalMap != null)
                {
                    material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalMap));
                    material.SetFloat("_BumpScale", 1f);
                    material.EnableKeyword("_NORMALMAP");
                }
                float tile = AssetFactory.SurfaceTile(surface != null ? surface : material);
                var scale = Vector2.one / tile;
                material.SetTextureScale("_BaseMap", scale);
                material.SetTextureScale("_BumpMap", scale);
            }
            if (create)
            {
                AssetFactory.EnsureFolder(MaterialRoot.TrimEnd('/'));
                AssetDatabase.CreateAsset(material, path);
            }
            EditorUtility.SetDirty(material);
            cache.Add(slot, material);
            return material;
        }

        static void MakePrefab(EnvLayout layout, Dictionary<string, Material> materials)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(layout.PrefabPath);
            if (existing != null && !AssetDatabase.GetLabels(existing).Contains(ArtImporter.GeneratedLabel))
                throw new InvalidOperationException("Không ghi đè prefab làm tay: " + layout.PrefabPath);
            var root = new GameObject("Env_" + layout.Area);
            try
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(EnvLayout.Root + layout.Area + "/" + layout.Fbx);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                instance.transform.localPosition = layout.ModelPosition;
                instance.transform.localRotation = layout.ModelRotation * instance.transform.localRotation;
                instance.transform.localScale = Vector3.Scale(layout.ModelScale, instance.transform.localScale);
                var collisionNames = new HashSet<string>(layout.Blockers.Concat(layout.CollisionSurfaces).Select(b => b.MeshName));
                int colliders = 0;
                foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
                {
                    var renderer = filter.GetComponent<Renderer>();
                    filter.gameObject.layer = 0; // Default, independent of optional project layer setup.
                    if (filter.name.StartsWith("COL_", StringComparison.Ordinal))
                    {
                        if (!collisionNames.Remove(filter.name))
                            throw new InvalidOperationException("COL không có trong layout: " + filter.name);
                        if (renderer != null) renderer.enabled = false;
                        var collider = filter.gameObject.AddComponent<MeshCollider>();
                        collider.sharedMesh = filter.sharedMesh;
                        // All delivered COLs are closed boxes (12 triangles); future non-boxes remain static triangle meshes.
                        collider.convex = filter.sharedMesh.subMeshCount == 1 && filter.sharedMesh.GetIndexCount(0) == 36;
                        GameObjectUtility.SetStaticEditorFlags(filter.gameObject, 0);
                        colliders++;
                        continue;
                    }
                    if (renderer == null)
                        continue;
                    if (renderer.sharedMaterials.Any(m => m == null))
                        throw new InvalidOperationException("Slot vật liệu rỗng: " + filter.name);
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(m => MaterialFor(layout.Area, m, materials)).ToArray();
                    renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                    renderer.renderingLayerMask = WorldRenderingLayers.Exterior;
                    renderer.lightmapIndex = -1;
                    if (renderer is MeshRenderer meshRenderer) meshRenderer.receiveGI = ReceiveGI.LightProbes;
                    var flags = StaticEditorFlags.OccludeeStatic;
                    // Distant backgrounds/foliage are probe-lit occludees, not occlusion walls or lightmap contributors.
                    if (layout.Blockers.Any(b => b.Visuals.Contains(filter.name)))
                        flags |= StaticEditorFlags.OccluderStatic;
                    GameObjectUtility.SetStaticEditorFlags(filter.gameObject, flags);
                }
                if (collisionNames.Count != 0 || colliders == 0)
                    throw new InvalidOperationException("Thiếu COL: " + string.Join(", ", collisionNames));
                AssetFactory.EnsureFolder(EnvLayout.PrefabRoot.TrimEnd('/'));
                var saved = PrefabUtility.SaveAsPrefabAsset(root, layout.PrefabPath, out bool ok);
                if (!ok) throw new InvalidOperationException("Không lưu được " + layout.PrefabPath);
                AssetDatabase.SetLabels(saved, new[] { ArtImporter.GeneratedLabel });
                var slots = saved.GetComponentsInChildren<Renderer>(true).Where(r => !r.name.StartsWith("COL_"))
                    .SelectMany(r => r.sharedMaterials).Select(m => m.name).Distinct().OrderBy(n => n).ToArray();
                Debug.Log($"EnvImporter: {layout.Area} OK · {saved.GetComponentsInChildren<MeshFilter>(true).Length} mesh objects"
                    + $" · {saved.GetComponentsInChildren<Transform>(true).Length} objects · {colliders} colliders · materials {string.Join(",", slots)}");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    public sealed class EnvModelImport : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => 100; // Apply after ArtModelImport's common model defaults.

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(EnvLayout.Root, StringComparison.Ordinal)) return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileUnits = true;
            importer.bakeAxisConversion = false;
            importer.preserveHierarchy = true;
            importer.generateSecondaryUV = false;
            importer.isReadable = true; // Convex COL cooking also works when instantiated in a player.
        }
    }
}
