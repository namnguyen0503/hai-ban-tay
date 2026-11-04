using System;
using System.Linq;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Ship;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>Thay tán ô bằng cây nhiều cụm, giữ chân cây và collider từ layout; xa dùng billboard.</summary>
    public static class WorldTreeBuilder
    {
        const string Folder = "Assets/_Project/Art/Generated/World10/Trees/";
        public static void Replace(GameObject environment)
        {
            var old = environment.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.name.StartsWith("Tamarind_") || f.name.StartsWith("FarBankTree_")).ToArray();
            if (old.Length == 0) return;
            string path = Folder + "World10Trees.fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Thiếu cây World10; chạy Tools/blender/make_world10_trees.py.");
            if (!importer.isReadable || !importer.preserveHierarchy || importer.globalScale != 1f)
            {
                importer.isReadable = true; importer.preserveHierarchy = true; importer.globalScale = 1f;
                importer.SaveAndReimport();
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var sources = model.GetComponentsInChildren<MeshFilter>(true);
            foreach (var source in sources)
                if (source.sharedMesh.bounds.size.y < 5f || source.sharedMesh.bounds.size.y > 15f)
                    throw new InvalidOperationException("Cây sai trục hoặc đơn vị sau import: " + source.name + " " + source.sharedMesh.bounds);
            var bark = AssetFactory.Lit("World10Bark", new Color(.22f,.14f,.065f), .05f);
            var leaves = AssetFactory.Lit("World10Leaves", new Color(.12f,.25f,.055f), .03f);
            var quad = new Mesh { vertices = new[] { new Vector3(-8,0,0), new Vector3(8,0,0), new Vector3(-8,16,0), new Vector3(8,16,0) },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one }, triangles = new[] { 0,2,1,1,2,3 } };
            // Shader xoay thẻ quanh Y: bounds phải chứa mọi hướng, không chỉ mặt phẳng ban đầu.
            quad.bounds = new Bounds(new Vector3(0,8,0), new Vector3(16,16,16));
            quad = KeppelPortBuilder.Save(quad, "TreeBillboardQuad");
            var kinds = new[] { "Me", "Sao", "Dau" };
            var cards = kinds.Select(CardMaterial).ToArray();
            for (int i = 0; i < old.Length; i++)
            {
                var original = old[i];
                original.GetComponent<Renderer>().enabled = false;
                int kind = original.name.StartsWith("FarBankTree_") ? 0 : i % kinds.Length;
                var root = BuilderUtils.Group("World10_" + kinds[kind], original.transform);
                var levels = new LOD[3];
                for (int level = 0; level < 3; level++)
                {
                    var go = BuilderUtils.Group("LOD" + level, root.transform);
                    var filter = go.AddComponent<MeshFilter>();
                    var renderer = go.AddComponent<MeshRenderer>();
                    if (level == 2) { filter.sharedMesh = quad; renderer.sharedMaterial = cards[kind]; }
                    else
                    {
                        var source = sources.Single(f => f.name == kinds[kind] + "_LOD" + level);
                        filter.sharedMesh = source.sharedMesh;
                        renderer.sharedMaterials = source.GetComponent<Renderer>().sharedMaterials
                            .Select(m => m.name.StartsWith("Bark") ? bark : leaves).ToArray();
                    }
                    levels[level] = new LOD(level == 0 ? .25f : level == 1 ? .09f : .002f, new[] { renderer });
                }
                var lod = root.AddComponent<LODGroup>();
                lod.SetLODs(levels); lod.RecalculateBounds();
                BuilderUtils.MakeDistantBackdrop(root);
                BuilderUtils.SetRenderingLayer(root, WorldRenderingLayers.Exterior);
            }
        }

        static Material CardMaterial(string kind)
        {
            string path = Folder + kind + "_Billboard.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Thiếu " + path);
            if (!importer.alphaIsTransparency || importer.wrapMode != TextureWrapMode.Clamp || !importer.mipmapEnabled)
            {
                importer.alphaIsTransparency = true; importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true; importer.SaveAndReimport();
            }
            var shader = Shader.Find("HaiBanTay/TreeBillboard");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Shader billboard cây chưa hợp lệ.");
            string materialPath = Folder + kind + "_Billboard.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath); }
            material.shader = shader; material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
