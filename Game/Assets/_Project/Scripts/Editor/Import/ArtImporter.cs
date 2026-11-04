using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.Import
{
    /// <summary>
    /// Đưa model CC0/CC-BY từ Staging/art/, Staging/art_hq/ (Models, Dressing) và Staging/art_hq_fbx/ (GLB/glTF đã đổi sang
    /// FBX bằng Tools/blender/gltf_to_fbx.py) vào Art/Models/{thư mục}/ và sinh
    /// Prefabs/Art/{slot}.prefab cho ArtSlot. Prefab chỉ chứa phần hình; ArtSlot tự co giãn vừa khối xám
    /// và giữ collider/child chức năng của khối xám. Prefab do nhóm tạo tay (không có nhãn hbt-generated) không bị ghi đè.
    /// Tools/import_art.sh gọi Run.
    /// </summary>
    public static class ArtImporter
    {
        public const string ModelRoot = "Assets/_Project/Art/Models/";
        public const string SurfaceRoot = "Assets/_Project/Art/Surfaces/";
        public const string GeneratedLabel = "hbt-generated";
        /// <summary>Nhãn prefab: co giãn không đều cho khớp đúng khối xám (mặt bàn, quầy cần đúng độ cao).</summary>
        public const string StretchLabel = "hbt-fit-stretch";
        /// <summary>Nhãn prefab: vật chứa (nồi, xô) luôn đứng thẳng, không xoay 90° quanh X/Z khi khớp khối xám.</summary>
        public const string UprightLabel = "hbt-fit-upright";

        /// <summary>Thư mục nguồn trong Staging (tương đối, ví dụ "art/CafeChair", "art_hq/Dressing/WoodenBucket").</summary>
        struct Entry
        {
            public string Slot, Folder, File, Source;
            public bool Stretch;
            /// <summary>Thay toàn bộ vật liệu của model (texture gốc chỉ là mảng màu phẳng).</summary>
            public Func<Material> Material;
            /// <summary>Chỉ lấy một object con của model (FBX chứa nhiều vật, ví dụ 6 củ khoai), đặt về gốc.</summary>
            public string Child;
            /// <summary>Thay vật liệu theo tên slot trong FBX (null: giữ vật liệu từ FBX).</summary>
            public Func<string, Material> SlotMaterial;
            public bool Upright;
            public Entry(string slot, string folder, string file, bool stretch, string source = null, Func<Material> material = null,
                string child = null, Func<string, Material> slotMaterial = null, bool upright = false)
            {
                Upright = upright;
                Slot = slot; Folder = folder; File = file; Stretch = stretch;
                Source = source ?? "art/" + slot;
                Material = material;
                Child = child;
                SlotMaterial = slotMaterial;
            }
        }

        // PotRack (kệ đứng, không phải giá treo), Porthole, Facade, NhaRongBuilding, NotreDameDeLaGarde chưa có
        // model đạt nên để khối xám. Bản HQ (art_hq) thay Stove_A/B (lò gang có buồng lửa), Book, CoffeeCup, Tram, GalleyDoor.
        static readonly Entry[] Entries =
        {
            new Entry("CafeChair", "CafeChair", "painted_wooden_chair_02_1k.fbx", false),
            new Entry("CafeTable", "CafeTable", "round_wooden_table_01_1k.fbx", true),
            new Entry("CoalBin", "CoalBin", "wooden_crate_02_1k.fbx", true),
            // Bỏ nắp: việc cọ nồi cao cần miệng nồi mở (lớp bẩn nằm ở mặt trên).
            new Entry("CopperPotTall", "CopperPotTall", "brass_pot_01_2k.fbx", true, child: "brass_pot_01", upright: true),
            new Entry("LoosePot", "LoosePot", "pot_enamel_01_1k.fbx", false),
            new Entry("OilLamp", "OilLamp", "vintage_oil_lamp_1k.fbx", false),
            new Entry("PotCounter", "PotCounter", "WoodenTable_03_1k.fbx", true),
            new Entry("PrepTable", "PrepTable", "painted_wooden_table_1k.fbx", true),
            new Entry("Shovel", "Shovel", "tool-shovel.fbx", false),
            new Entry("SmallCrate", "SmallCrate", "wooden_crate_01_1k.fbx", true),
            new Entry("SmallTable", "SmallTable", "small_wooden_table_01_1k.fbx", true),
            new Entry("Stool", "Stool", "wooden_stool_01_1k.fbx", true),
            new Entry("StormLantern", "StormLantern", "Lantern_01_1k.fbx", false),
            // HQ: lò gang, sách, tách, tàu điện (GLB/glTF → FBX), cửa gỗ.
            new Entry("Stove_A", "Stove", "CastIronStove.fbx", false, "art_hq_fbx/Stove_A", StoveIron),
            new Entry("Stove_B", "Stove", "CastIronStove.fbx", false, "art_hq_fbx/Stove_A", StoveIron),
            new Entry("Book", "Book", "Book.fbx", false, "art_hq_fbx/Book"),
            new Entry("CoffeeCup", "CoffeeCup", "CoffeeCup.fbx", false, "art_hq_fbx/CoffeeCup"),
            new Entry("Tram", "TramHQ", "Tram.fbx", false, "art_hq_fbx/Tram"),
            new Entry("Anchor", "Anchor", "Anchor.fbx", false, "art_hq_fbx/Anchor"),
            new Entry("GalleyDoor", "GalleyDoor", "WoodenDoor.fbx", true, "art_hq/Models/GalleyDoor"),
            // Tự dựng bằng Tools/blender/make_potato_sack.py, make_empty_sack.py, make_potatoes.py (vải hessian_380, CC0).
            new Entry("PotatoSack", "PotatoSack", "PotatoSack.fbx", false, "art_hq_fbx/PotatoSack"),
            new Entry("PotatoSackEmpty", "PotatoSackEmpty", "PotatoSackEmpty.fbx", false, "art_hq_fbx/PotatoSackEmpty"),
            new Entry("Potato_0", "Potato", "Potato.fbx", false, "art_hq_fbx/Potato", child: "Potato_0"),
            new Entry("Potato_1", "Potato", "Potato.fbx", false, "art_hq_fbx/Potato", child: "Potato_1"),
            new Entry("Potato_2", "Potato", "Potato.fbx", false, "art_hq_fbx/Potato", child: "Potato_2"),
            new Entry("Potato_3", "Potato", "Potato.fbx", false, "art_hq_fbx/Potato", child: "Potato_3"),
            new Entry("Potato_4", "Potato", "Potato.fbx", false, "art_hq_fbx/Potato", child: "Potato_4"),
            new Entry("Potato_5", "Potato", "Potato.fbx", false, "art_hq_fbx/Potato", child: "Potato_5"),
            // Đồ bày trí và đồ dùng trong việc mới (M9).
            // Tàu Amiral Latouche-Tréville (F10) dựng bằng Tools/blender/make_ship.py; ba mức LOD đặt 1:1, không qua ArtSlot (ShipBuilder.BuildModel).
            new Entry("Ship", "Ship", "AmiralLatoucheTreville.fbx", false, "art_hq_fbx/Ship", slotMaterial: ShipMaterial),
            new Entry("Ship_LOD1", "Ship", "AmiralLatoucheTreville_LOD1.fbx", false, "art_hq_fbx/Ship", slotMaterial: ShipMaterial),
            new Entry("Ship_LOD2", "Ship", "AmiralLatoucheTreville_LOD2.fbx", false, "art_hq_fbx/Ship", slotMaterial: ShipMaterial),

            new Entry("Barrel", "Barrel", "wine_barrel_01_2K.fbx", false, "art_hq/Models/Barrel"),
            new Entry("Crate", "Crate", "wooden_crate_01_2K.fbx", false, "art_hq/Models/Crate"),
            new Entry("Ladder", "Ladder", "wooden_ladder_02_2K.fbx", false, "art_hq/Models/Ladder"),
            new Entry("GalleyShelf", "GalleyShelf", "worn_metal_rack_2K.fbx", true, "art_hq/Dressing/GalleyShelf"),
            new Entry("GasWallLantern", "GasWallLantern", "gaslamp.fbx", false, "art_hq/Dressing/GasWallLantern"),
            new Entry("PortBarrels", "PortBarrels", "wooden_barrels_01_2K.fbx", false, "art_hq/Dressing/PortBarrels"),
            new Entry("PortBench", "PortBench", "painted_wooden_bench_2K.fbx", false, "art_hq/Dressing/PortBench"),
            new Entry("WickerBasket", "WickerBasket", "wicker_basket_01_2K.fbx", true, "art_hq/Dressing/WickerBasket"),
            new Entry("WoodenBucket", "WoodenBucket", "wooden_bucket_01_2K.fbx", false, "art_hq/Dressing/WoodenBucket", upright: true),
        };

        // Map URP Lit không dùng riêng metal/rough/displacement; bỏ để nhẹ repo. Chỉ xét hậu tố tên map (tên asset như
        // "worn_metal_rack" vẫn giữ).
        static readonly Regex SkipMap = new Regex(
            @"_(metal|metallic|metalness|rough|roughness|disp|displacement|normaldx|nor_dx|ao|ambientocclusion|arm)(_\d+k)?\.(png|jpg|exr)$");

        /// <summary>Slot tàu dùng chung bề mặt PBR với boong khối xám; slot màu trơn giữ màu từ FBX.</summary>
        static Material ShipMaterial(string slot)
        {
            const float uv = 2f;
            switch (slot)
            {
                case "Hull": return Setup.AssetFactory.LitModel("ShipModel_Hull", "Hull", new Color(0.1f, 0.1f, 0.11f), uv, 0.3f);
                case "ShipWhite": return Setup.AssetFactory.LitModel("ShipModel_White", "ShipWhite", new Color(0.86f, 0.84f, 0.78f), uv, 0.25f);
                case "DeckPlanks": return Setup.AssetFactory.LitModel("ShipModel_Deck", "DeckPlanks", new Color(0.55f, 0.43f, 0.3f), uv, 0.2f);
                case "Rail": return Setup.AssetFactory.LitModel("ShipModel_Rail", "Rail", new Color(0.35f, 0.25f, 0.16f), uv, 0.3f);
                case "Iron": return Setup.AssetFactory.LitModel("ShipModel_Iron", "Iron", new Color(0.18f, 0.18f, 0.19f), uv, 0.4f, 0.6f);
                case "Funnel": return Setup.AssetFactory.LitModel("ShipModel_Funnel", "Funnel", new Color(0.1f, 0.09f, 0.08f), uv, 0.3f);
                case "Glass": return Setup.AssetFactory.Transparent("Glass", new Color(0.65f, 0.78f, 0.8f, 0.12f));
                default: return null;
            }
        }

        static Material StoveIron() => Setup.AssetFactory.Lit("StoveIron", new Color(0.13f, 0.13f, 0.14f), 0.38f, 0.65f);

        static string Staging => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Staging"));
        static string StagingHqRoot => Path.Combine(Staging, "art_hq");

        /// <summary>Chỉ nhập tàu; không ghi lại prefab của những phần art khác.</summary>
        public static void RunShip()
        {
            try
            {
                CopyShip();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                foreach (var entry in Entries.Where(e => e.Folder == "Ship"))
                    if (!MakePrefab(entry))
                        throw new InvalidOperationException("Không nhập được " + entry.File);
                AssetDatabase.SaveAssets();
                Debug.Log("ArtImporter: tàu LOD0/1/2 và deck_zones v2 đã nhập.");
            }
            catch (Exception ex)
            {
                Debug.LogError("ArtImporter: " + ex);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        static void CopyShip()
        {
            string src = Path.Combine(Staging, "art_hq_fbx", "Ship");
            string dst = Path.Combine(Application.dataPath, "_Project", "Art", "Models", "Ship");
            CopyFiltered(src, dst);
            // Builder đọc TextAsset trong Game, không phụ thuộc Staging khi dựng cảnh.
            File.Copy(Path.Combine(src, "deck_zones.json"), Path.Combine(dst, "deck_zones.json"), true);
        }

        [MenuItem("HaiBanTay/Art/Import Staging")]
        public static void Run()
        {
            int made = 0, failed = 0;
            try
            {
                foreach (var folder in Entries.Select(e => e.Folder).Distinct())
                {
                    var src = Entries.First(e => e.Folder == folder);
                    string srcDir = Path.Combine(Staging, src.Source);
                    if (!Directory.Exists(srcDir))
                    {
                        Debug.LogWarning($"ArtImporter: thiếu {srcDir}");
                        continue;
                    }
                    CopyFiltered(srcDir, Path.Combine(Application.dataPath, "_Project", "Art", "Models", folder));
                }
                // Bề mặt PBR: Staging/art_hq/Surfaces/{vật liệu}/ → Art/Surfaces/{vật liệu}/ (AssetFactory.Lit tự gắn).
                CopyShip();
                string surfaces = Path.Combine(StagingHqRoot, "Surfaces");
                if (Directory.Exists(surfaces))
                    foreach (var dir in Directory.GetDirectories(surfaces))
                        CopyFiltered(dir, Path.Combine(Application.dataPath, "_Project", "Art", "Surfaces", Path.GetFileName(dir)));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                foreach (var e in Entries)
                {
                    if (MakePrefab(e))
                        made++;
                    else
                        failed++;
                }
                AssetDatabase.SaveAssets();
            }
            catch (Exception ex)
            {
                Debug.LogError("ArtImporter thất bại: " + ex);
                failed++;
            }
            Debug.Log($"ArtImporter: {made} prefab, {failed} lỗi");
            if (Application.isBatchMode)
                EditorApplication.Exit(failed == 0 ? 0 : 1);
        }

        static void CopyFiltered(string srcDir, string dstDir)
        {
            foreach (var file in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
            {
                string lower = Path.GetFileName(file).ToLowerInvariant();
                // Ảnh preview/kiểm tra dựng từ Blender chỉ để duyệt; thư mục "_scratch" là bản nháp các lượt dựng.
                if (SkipMap.IsMatch(lower) || lower.StartsWith("preview") || lower.StartsWith("check_") || lower.StartsWith("compare_"))
                    continue;
                if (Path.GetRelativePath(srcDir, file).Split(Path.DirectorySeparatorChar).Any(part => part.StartsWith("_")))
                    continue;
                string ext = Path.GetExtension(lower);
                if (ext != ".fbx" && ext != ".png" && ext != ".jpg" && ext != ".exr")
                    continue;
                string dst = Path.Combine(dstDir, Path.GetRelativePath(srcDir, file));
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                if (!File.Exists(dst) || new FileInfo(dst).Length != new FileInfo(file).Length)
                    File.Copy(file, dst, true);
            }
        }

        static bool MakePrefab(Entry e)
        {
            string prefabPath = $"{ImportBudget.PrefabArtRoot}{e.Slot}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null && !AssetDatabase.GetLabels(existing).Contains(GeneratedLabel))
            {
                Debug.Log($"ArtImporter: {e.Slot} giữ prefab nhóm làm tay");
                return true;
            }

            string modelPath = $"{ModelRoot}{e.Folder}/{e.File}";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogError($"ArtImporter: không nạp được {modelPath}");
                return false;
            }

            var root = new GameObject(e.Slot);
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                inst.transform.SetParent(root.transform, false);
                if (!string.IsNullOrEmpty(e.Child))
                {
                    PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    var keep = inst.transform.Find(e.Child);
                    if (keep == null)
                    {
                        Debug.LogError($"ArtImporter: {modelPath} không có object {e.Child}");
                        return false;
                    }
                    foreach (Transform c in inst.transform.Cast<Transform>().ToArray())
                        if (c != keep)
                            UnityEngine.Object.DestroyImmediate(c.gameObject);
                    keep.localPosition = Vector3.zero;
                }
                foreach (var c in inst.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(c);
                if (e.SlotMaterial != null)
                    foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                        r.sharedMaterials = r.sharedMaterials.Select(m => (m != null ? e.SlotMaterial(m.name) : null) ?? m).ToArray();
                if (e.Material != null)
                {
                    var mat = e.Material();
                    foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                        r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                }

                var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool ok);
                if (!ok)
                {
                    Debug.LogError($"ArtImporter: không lưu được {prefabPath}");
                    return false;
                }
                var labels = new System.Collections.Generic.List<string> { GeneratedLabel };
                if (e.Stretch)
                    labels.Add(StretchLabel);
                if (e.Upright)
                    labels.Add(UprightLabel);
                AssetDatabase.SetLabels(saved, labels.ToArray());

                long tris = 0;
                var bounds = new Bounds();
                bool any = false;
                foreach (var mf in saved.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null)
                        continue;
                    tris += mf.sharedMesh.triangles.Length / 3;
                    var b = TransformBounds(mf.sharedMesh.bounds, saved.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix);
                    if (any) bounds.Encapsulate(b); else bounds = b;
                    any = true;
                }
                Debug.Log($"ArtImporter: {e.Slot} OK · {tris} tam giác · kích thước {bounds.size.x:0.00}×{bounds.size.y:0.00}×{bounds.size.z:0.00} m");
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        public static Bounds TransformBounds(Bounds b, Matrix4x4 m)
        {
            var c = b.center;
            var x = b.extents;
            var result = new Bounds(m.MultiplyPoint3x4(c), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var p = c + new Vector3((i & 1) == 0 ? -x.x : x.x, (i & 2) == 0 ? -x.y : x.y, (i & 4) == 0 ? -x.z : x.z);
                result.Encapsulate(m.MultiplyPoint3x4(p));
            }
            return result;
        }
    }

    /// <summary>Import settings cho model trong Art/Models: UV2 cho lightmap, vật liệu từ FBX, không animation/camera/đèn.</summary>
    public sealed class ArtModelImport : AssetPostprocessor
    {
        const string ModelRootShip = ArtImporter.ModelRoot + "Ship/";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ArtImporter.ModelRoot))
                return;
            // Tay FP do HandsImporter cấu hình (đọc được mesh để test khung hình, không UV2, remap Gen_FP_*).
            if (assetPath.StartsWith(HandsImporter.Folder + "/"))
                return;
            var importer = (ModelImporter)assetImporter;
            // Tàu (~600 nghìn tam giác) không bake lightmap: nhận sáng từ light probe.
            importer.generateSecondaryUV = !assetPath.StartsWith(ModelRootShip);
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtImporter.ModelRoot) && !assetPath.StartsWith(ArtImporter.SurfaceRoot))
                return;
            string name = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            if (name.Contains("_nor") || name.Contains("normal"))
                ((TextureImporter)assetImporter).textureType = TextureImporterType.NormalMap;
        }
    }
}
