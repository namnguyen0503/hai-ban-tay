using System;
using System.IO;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Player;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.Import
{
    public static class HandsImporter
    {
        public const string Folder = "Assets/_Project/Art/Models/Hands";
        public const string SkinPath = Folder + "/Gen_FP_Skin.mat";
        public const string SleevePath = Folder + "/Gen_FP_Sleeve.mat";

        [MenuItem("HaiBanTay/Art/Import First Person Hands")]
        public static void Run()
        {
            try
            {
                string staging = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Staging/art/hands"));
                foreach (HandPose pose in Enum.GetValues(typeof(HandPose)))
                    foreach (string side in new[] { "L", "R" })
                        if (!File.Exists(Path.Combine(staging, $"Hand_{side}_{pose}.fbx")))
                            throw new FileNotFoundException($"Thiếu tay {side}/{pose}");
                AssetFactory.EnsureFolder(Folder);
                foreach (string file in Directory.GetFiles(staging, "*.fbx"))
                    File.Copy(file, Path.Combine(Application.dataPath, "_Project/Art/Models/Hands", Path.GetFileName(file)), true);
                foreach (string file in Directory.GetFiles(staging, "FP_*.png"))
                    File.Copy(file, Path.Combine(Application.dataPath, "_Project/Art/Models/Hands", Path.GetFileName(file)), true);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var skin = Material(SkinPath, Color.white, 0.28f);
                var color = Texture("FP_Skin_BaseColor", false);
                var normal = Texture("FP_Skin_Normal", true);
                skin.SetTexture("_BaseMap", color);
                skin.SetTexture("_BumpMap", normal);
                skin.SetFloat("_BumpScale", 0.85f);
                // Specular gốc giữ vùng móng hơi bóng hơn da lao động.
                skin.SetFloat("_WorkflowMode", 0f);
                skin.SetTexture("_SpecGlossMap", Texture("FP_Skin_SpecGloss", false, true));
                skin.SetFloat("_SmoothnessTextureChannel", 0f);
                skin.EnableKeyword("_SPECULAR_SETUP");
                skin.EnableKeyword("_METALLICSPECGLOSSMAP");
                skin.EnableKeyword("_NORMALMAP");
                skin.EnableKeyword("_EMISSION");
                skin.SetColor("_EmissionColor", Color.black);
                // Cổ tay hở của mesh tay: nhìn vào thấy mặt trong là da, không xuyên ra sau (SeeThroughSweep 15/10).
                AssetFactory.MakeTwoSided(skin);
                var sleeve = Material(SleevePath, Color.white, 0.08f);
                // Cuff có sẵn nếp/bo trong mesh; dùng đúng vải ngà của thân, không nhân texture cũ làm vàng áo.
                var shirt = SceneBuilders.PlayerBodyBuilder.ShirtMaterial();
                sleeve.CopyPropertiesFromMaterial(shirt);
                foreach (HandPose pose in Enum.GetValues(typeof(HandPose)))
                    foreach (string side in new[] { "L", "R" })
                    {
                        string path = $"{Folder}/Hand_{side}_{pose}.fbx";
                        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                        importer.globalScale = 1f;
                        importer.bakeAxisConversion = true;
                        importer.isReadable = true;
                        importer.importAnimation = false;
                        importer.animationType = ModelImporterAnimationType.None;
                        importer.importCameras = false;
                        importer.importLights = false;
                        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(UnityEngine.Material), "FP_Skin"), skin);
                        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(UnityEngine.Material), "FP_Sleeve"), sleeve);
                        importer.SaveAndReimport();
                        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        int triangles = 0;
                        foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
                        {
                            triangles += mf.sharedMesh.triangles.Length / 3;
                            var bounds = new Bounds(Vector3.zero, Vector3.zero);
                            foreach (var vertex in mf.sharedMesh.vertices)
                                bounds.Encapsulate(model.transform.InverseTransformPoint(mf.transform.TransformPoint(vertex)));
                            Debug.Log($"HandsImporter frame {side}/{pose}/{mf.name}: min {bounds.min:F4}, max {bounds.max:F4}");
                        }
                        if (triangles > 3000) throw new InvalidOperationException($"Vượt ngân sách {path}: {triangles}");
                        Debug.Log($"HandsImporter: {path} {triangles} tam giác");
                    }
                EditorUtility.SetDirty(skin);
                EditorUtility.SetDirty(sleeve);
                AssetFactory.ConfigureHandsShot();
                AssetDatabase.SaveAssets();
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogError("HandsImporter: " + error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        static Texture2D Texture(string name, bool normal, bool linear = false)
        {
            string path = $"{Folder}/{name}.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new FileNotFoundException($"Thiếu texture tay {path}");
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal && !linear;
            importer.maxTextureSize = name.StartsWith("FP_Skin") ? 2048 : 1024;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null || texture.width > 2048 || texture.height > 2048)
                throw new InvalidOperationException($"Texture tay không hợp lệ: {path}");
            return texture;
        }

        static Material Material(string path, Color color, float smoothness)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
