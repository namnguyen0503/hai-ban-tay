using System.IO;
using System.Linq;
using HaiBanTay.Editor.Import;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.Setup
{
    /// <summary>
    /// Đưa vật liệu của vật mỏng hay hở sang shader hai mặt (AssetFactory.TwoSidedShader) cho các vật đã có sẵn: Gen_*.mat trong
    /// AssetFactory.TwoSidedNames (vỏ tàu nhập từ FBX chỉ dựng lại khi chạy ArtImporter), da tay góc nhìn thứ nhất, rồi nhập lại FBX nhân vật
    /// và FBX tàu để TwoSidedMaterialImport sửa vật liệu nằm trong FBX (tóc; vải bạt xuồng, sọc và sao ống khói, đường mớn nước). Bộ dò
    /// SeeThroughSweep 15/10 thấy các mặt này từ chỗ người chơi đứng. Tools/two_sided.sh gọi Run (Editor phải đóng).
    /// </summary>
    public static class TwoSidedMaterials
    {
        public static void Run()
        {
            int changed = 0;
            foreach (string name in AssetFactory.TwoSidedNames)
            {
                foreach (string path in new[] { $"{AssetFactory.MaterialsFolder}/Gen_{name}.mat", HandsImporter.Folder + $"/Gen_{name}.mat" })
                {
                    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (m == null) continue;
                    AssetFactory.MakeTwoSided(m);
                    EditorUtility.SetDirty(m);
                    changed++;
                }
            }
            AssetDatabase.SaveAssets();
            var models = AssetDatabase.FindAssets("t:Model", new[] { CharacterImporter.CharRoot.TrimEnd('/'), TwoSidedMaterialImport.ShipRoot.TrimEnd('/') })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !p.StartsWith(CharacterImporter.AnimRoot) && p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                .Distinct().ToList();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string path in models)
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"TWO_SIDED: {changed} vật liệu Gen_, nhập lại {models.Count} FBX:\n" + string.Join("\n", models));
            Check();
        }

        /// <summary>Đếm vật liệu thuộc quy tắc hai mặt mà vẫn cull mặt sau (log TWO_SIDED_CHECK).</summary>
        public static void Check()
        {
            var bad = SingleSided(out int ok);
            Debug.Log($"TWO_SIDED_CHECK: {ok} hai mặt, {bad.Count} còn cull mặt sau\n" + string.Join("\n", bad));
        }

        public static System.Collections.Generic.List<string> SingleSided() => SingleSided(out _);

        /// <summary>Vật liệu thuộc quy tắc hai mặt (Gen_ trong AssetFactory.TwoSidedNames, vật liệu FBX nhân vật/tàu theo
        /// TwoSidedMaterialImport.Wants) mà vẫn cull mặt sau.</summary>
        public static System.Collections.Generic.List<string> SingleSided(out int ok)
        {
            ok = 0;
            var bad = new System.Collections.Generic.List<string>();
            foreach (string name in AssetFactory.TwoSidedNames)
                foreach (string path in new[] { $"{AssetFactory.MaterialsFolder}/Gen_{name}.mat", HandsImporter.Folder + $"/Gen_{name}.mat" })
                {
                    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (m == null) continue;
                    if (m.HasProperty("_Cull") && m.GetFloat("_Cull") < 0.5f) ok++;
                    else bad.Add($"{path} ({m.shader.name})");
                }
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { CharacterImporter.CharRoot.TrimEnd('/'), TwoSidedMaterialImport.ShipRoot.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith(CharacterImporter.AnimRoot)) continue;
                foreach (var m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                {
                    if (!TwoSidedMaterialImport.Wants(path, m.name)) continue;
                    if (m.HasProperty("_Cull") && m.GetFloat("_Cull") < 0.5f) ok++;
                    else bad.Add($"{path}:{m.name} ({m.shader.name})");
                }
            }
            return bad;
        }
    }

    /// <summary>Vật liệu nằm trong FBX cần vẽ hai mặt: tóc, vải áo, đồ da nhân vật (tấm một lớp, nhìn từ dưới hay sát thấy xuyên), vải bạt xuồng cứu sinh,
    /// sọc và sao ống khói, đường mớn nước của tàu (tấm mỏng).</summary>
    public sealed class TwoSidedMaterialImport : AssetPostprocessor
    {
        public const string ShipRoot = Import.ArtImporter.ModelRoot + "Ship/";
        static readonly string[] ShipThin = { "Canvas", "FunnelBand", "FunnelStar", "BootTop" };

        // Vật liệu nhân vật một lớp: tóc, vải áo (vạt, gấu, ống tay hở: nhìn từ dưới lên thấy mặt trong), bao tay áo, đồ da.
        static readonly string[] CharThin = { "hair", "cloth", "sleeve", "leather" };

        public static bool Wants(string path, string material) =>
            path.StartsWith(CharacterImporter.CharRoot) && CharThin.Any(k => material.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0)
            || path.StartsWith(ShipRoot) && ShipThin.Contains(material);

        // FBX nhân vật/tàu nhập theo MaterialDescription: URP (FBXMaterialDescriptionPreprocessor, thứ tự −980) đặt shader Lit ở bước
        // này, OnPostprocessMaterial không được gọi; postprocessor này thứ tự 0 nên chạy sau URP.
        public void OnPreprocessMaterialDescription(UnityEditor.AssetImporters.MaterialDescription description, Material material,
            AnimationClip[] clips)
        {
            if (!Wants(assetPath, description.materialName))
                return;
            if (Shader.Find(AssetFactory.TwoSidedShader) == null && material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", 0f);
                return;
            }
            AssetFactory.MakeTwoSided(material);
        }
    }
}
