using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace HaiBanTay.Editor.Import
{
    /// <summary>
    /// Nhân vật NPC (M10): Staging/art_hq_fbx/Characters/{vai}/ (Microsoft Rocketbox, MIT, đã sửa trang phục 1911) và
    /// CharacterAnims/ (Rocketbox MIT + KayKit CC0) → Art/Characters/. Rig Humanoid, giữ hierarchy (README của bản chuẩn
    /// bị); clip dùng Avatar của Cook. Sinh NpcController.controller (mỗi clip một state cùng tên, NpcAgent.Play gọi theo
    /// tên) và Prefabs/NPC/{vai}.prefab. Tools/import_characters.sh gọi Run.
    /// </summary>
    public static class CharacterImporter
    {
        public const string CharRoot = "Assets/_Project/Art/Characters/";
        public const string AnimRoot = CharRoot + "Anims/";
        public const string PrefabRoot = "Assets/_Project/Prefabs/NPC/";
        public const string ControllerPath = CharRoot + "NpcController.controller";

        public static readonly string[] Roles =
        {
            "Cook", "Helper", "SoldierA", "SoldierB", "Jacques", "Mai", "Le", "Captain", "Purser",
            "PorterVN_A", "PorterVN_B", "PorterVN_C", "VendorVN_F", "CivilianVN", "PassengerFR_M1",
            "PassengerFR_M2", "PassengerFR_F", "SailorFR", "DockerMRS", "Waiter",
        };

        // Vai truyện riêng; giữ nguyên bộ 20 vai đám đông và hợp đồng test cũ.
        public static readonly string[] StoryRoles = { "Ba", "Chieu", "ChieuChild" };

        public static readonly string[] NewClips =
        {
            "ShoulderCarry", "ShoulderIdle", "BagWalk", "BagIdle", "Documents", "HandOver", "Wave", "LeanRail",
            "Squat", "SitChair", "Drink", "Talk2", "Listen", "TrayCarry", "Sweep", "Pull",
        };

        /// <summary>Clip phát một lần (còn lại lặp).</summary>
        public static readonly string[] OneShots = { "PickUp", "Point", "SitDown", "StandUp", "HandOver", "Wave" };

        public static readonly string[] Clips = new[]
        {
            "Idle", "IdleLook", "Walk", "WorkTable", "WorkMid", "Stir", "HoldCarry", "CarryWalk", "PickUp", "Point", "Talk",
            "SitIdle", "SitRead", "SitDown", "StandUp",
        }.Concat(NewClips).ToArray();

        static string Staging => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Staging", "art_hq_fbx"));
        static string Feet12 => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Tools", "blender", "generated", "Characters"));
        static string CharacterSource(string role) => File.Exists(Path.Combine(Feet12, role, role + ".fbx"))
            ? Path.Combine(Feet12, role) : Path.Combine(Staging, "Characters", role);
        static string ProjectPath(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));

        public static string AvatarModelPath => CharRoot + "Cook/Cook.fbx";

        [MenuItem("HaiBanTay/Art/Import Characters")]
        public static void Run() => RunRoles(Roles.Concat(StoryRoles).ToArray());

        [MenuItem("HaiBanTay/Art/Import Faces13")]
        public static void RunFaces13() => RunRoles(StoryRoles.Concat(new[] { "Captain" }).ToArray());

        static void RunRoles(string[] roles)
        {
            int failed = 0;
            try
            {
                var required = roles.SelectMany(role => new[] { "", "_LOD1", "_LOD2" }
                    .Select(suffix => Path.Combine(CharacterSource(role), role + suffix + ".fbx")))
                    .Concat(Clips.Select(clip => Path.Combine(Staging, "CharacterAnims", clip + ".fbx")))
                    .Concat(new[] { Path.Combine(Staging, "Characters", "LICENSE_Rocketbox_MIT.txt"),
                        Path.Combine(Staging, "CharacterAnims", "LICENSE_Rocketbox_MIT.txt"),
                        Path.Combine(Staging, "CharacterAnims", "LICENSE_KayKit_CC0.txt") });
                foreach (string file in required)
                    if (!File.Exists(file)) throw new FileNotFoundException("Thiếu nguồn nhân vật", file);
                foreach (var role in roles)
                    Copy(CharacterSource(role), ProjectPath(CharRoot + role));
                // Giấy phép MIT phải đi cùng bản phân phối (CREDITS.md trỏ tới file này).
                File.Copy(Path.Combine(Staging, "Characters", "LICENSE_Rocketbox_MIT.txt"),
                    ProjectPath(CharRoot + "LICENSE_Rocketbox_MIT.txt"), true);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                foreach (var role in roles)
                    foreach (var suffix in new[] { "", "_LOD1", "_LOD2" })
                        AssetDatabase.ImportAsset($"{CharRoot}{role}/{role}{suffix}.fbx",
                            ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                // Clip cần Avatar của Cook đã import xong.
                Copy(Path.Combine(Staging, "CharacterAnims"), ProjectPath(AnimRoot));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                foreach (var clip in Clips)
                {
                    string path = AnimRoot + clip + ".fbx";
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (importer == null)
                    {
                        Debug.LogError($"CharacterImporter: thiếu {path}");
                        failed++;
                        continue;
                    }
                    // Reapply loop/root settings on repeat imports, including pre-existing clips.
                    importer.SaveAndReimport();
                }

                var controller = BuildController(ref failed);
                foreach (var role in roles)
                    if (!MakePrefab(role, controller))
                        failed++;
                AssetDatabase.SaveAssets();
            }
            catch (Exception ex)
            {
                Debug.LogError("CharacterImporter thất bại: " + ex);
                failed++;
            }
            Debug.Log($"CharacterImporter: {roles.Length} nhân vật, {failed} lỗi");
            if (Application.isBatchMode)
                EditorApplication.Exit(failed == 0 ? 0 : 1);
        }

        static void Copy(string srcDir, string dstDir)
        {
            if (!Directory.Exists(srcDir))
                throw new DirectoryNotFoundException(srcDir);
            foreach (var file in Directory.GetFiles(srcDir))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".fbx" && ext != ".png" && ext != ".txt")
                    continue;
                // Ảnh xem trước chỉ để duyệt.
                if (Path.GetFileName(file).ToLowerInvariant().Contains("preview"))
                    continue;
                Directory.CreateDirectory(dstDir);
                string dst = Path.Combine(dstDir, Path.GetFileName(file));
                if (!File.Exists(dst) || !File.ReadAllBytes(dst).SequenceEqual(File.ReadAllBytes(file)))
                    File.Copy(file, dst, true);
            }
        }

        public static AnimationClip LoadClip(string name) =>
            AssetDatabase.LoadAllAssetsAtPath(AnimRoot + name + ".fbx").OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        static AnimatorController BuildController(ref int failed)
        {
            // Update in place: deleting the controller changes its GUID and breaks scene references.
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var sm = controller.layers[0].stateMachine;
            foreach (var name in Clips)
            {
                var clip = LoadClip(name);
                if (clip == null)
                {
                    Debug.LogError($"CharacterImporter: không có clip trong {name}.fbx");
                    failed++;
                    continue;
                }
                var state = sm.states.Select(s => s.state).SingleOrDefault(s => s.name == name) ?? sm.AddState(name);
                state.motion = clip;
                if (name == "Idle")
                    sm.defaultState = state;
                if (NewClips.Contains(name))
                {
                    if (!controller.parameters.Any(p => p.name == name))
                        controller.AddParameter(name, AnimatorControllerParameterType.Trigger);
                    if (!sm.anyStateTransitions.Any(t => t.destinationState == state
                        && t.conditions.Any(c => c.parameter == name)))
                    {
                        var transition = sm.AddAnyStateTransition(state);
                        transition.hasExitTime = false;
                        transition.duration = 0.15f;
                        transition.canTransitionToSelf = false;
                        transition.AddCondition(AnimatorConditionMode.If, 0f, name);
                    }
                }
                EditorUtility.SetDirty(state);
            }
            EditorUtility.SetDirty(controller);
            return controller;
        }

        static bool MakePrefab(string role, RuntimeAnimatorController controller)
        {
            string modelPath = $"{CharRoot}{role}/{role}.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogError($"CharacterImporter: không nạp được {modelPath}");
                return false;
            }
            Directory.CreateDirectory(ProjectPath(PrefabRoot));
            var root = new GameObject(role);
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                var animator = inst.GetComponent<Animator>();
                if (animator == null)
                    animator = inst.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                // Trẻ có bind tỉ lệ riêng; dùng Avatar của trẻ để retarget đúng chiều dài chi.
                animator.avatar = AssetDatabase.LoadAssetAtPath<Avatar>(role == "ChieuChild" ? modelPath : AvatarModelPath);
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                // All levels animate through LOD0's canonical bones; no extra armatures/Animators.
                var bones = inst.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name)
                    .ToDictionary(g => g.Key, g => g.First());
                var renderers = new Renderer[3][];
                renderers[0] = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                for (int level = 1; level < 3; level++)
                {
                    string path = $"{CharRoot}{role}/{role}_LOD{level}.fbx";
                    var lodModel = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (lodModel == null)
                        throw new InvalidOperationException("Thiếu " + path);
                    var other = UnityEngine.Object.Instantiate(lodModel);
                    try
                    {
                        var list = new System.Collections.Generic.List<Renderer>();
                        foreach (var skin in other.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        {
                            var child = new GameObject($"{role}_LOD{level}_{skin.name}");
                            child.transform.SetParent(inst.transform, false);
                            child.transform.localPosition = other.transform.InverseTransformPoint(skin.transform.position);
                            child.transform.localRotation = Quaternion.Inverse(other.transform.rotation) * skin.transform.rotation;
                            child.transform.localScale = skin.transform.localScale;
                            var target = child.AddComponent<SkinnedMeshRenderer>();
                            target.sharedMesh = skin.sharedMesh;
                            target.sharedMaterials = skin.sharedMaterials;
                            target.bones = skin.bones.Select(b => bones[b.name]).ToArray();
                            target.rootBone = bones[skin.rootBone.name];
                            target.localBounds = skin.localBounds;
                            list.Add(target);
                        }
                        if (list.Count == 0)
                            throw new InvalidOperationException("LOD không có skin: " + path);
                        renderers[level] = list.ToArray();
                    }
                    finally { UnityEngine.Object.DestroyImmediate(other); }
                }
                // Unity can infer an incomplete LODGroup from a standalone *_LOD FBX.
                foreach (var inherited in root.GetComponentsInChildren<LODGroup>(true))
                    UnityEngine.Object.DestroyImmediate(inherited);
                var group = root.AddComponent<LODGroup>();
                // Keep silhouettes visible through CrowdLod's default 160m radius.
                group.SetLODs(new[] { new LOD(0.50f, renderers[0]), new LOD(0.20f, renderers[1]), new LOD(0.008f, renderers[2]) });
                group.RecalculateBounds();
                foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    smr.updateWhenOffscreen = false;
                    smr.quality = SkinQuality.Bone4;
                }
                bool human = animator.avatar != null && animator.avatar.isHuman;
                var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabRoot + role + ".prefab", out bool ok);
                if (!ok)
                    return false;
                AssetDatabase.SetLabels(saved, new[] { ArtImporter.GeneratedLabel });
                var counts = renderers.Select(level => level.Cast<SkinnedMeshRenderer>()
                    .Sum(s => s.sharedMesh.triangles.Length / 3));
                Debug.Log($"CharacterImporter: {role} OK · LOD {string.Join("/", counts)} tam giác · avatar {(human ? "Humanoid" : "LỖI")}");
                return human;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }

    /// <summary>Import settings cho Art/Characters: Humanoid, giữ hierarchy; clip dùng Avatar của Cook, đặt lặp theo tên.</summary>
    public sealed class CharacterModelImport : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(CharacterImporter.CharRoot))
                return;
            var importer = (ModelImporter)assetImporter;
            bool anim = assetPath.StartsWith(CharacterImporter.AnimRoot);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.preserveHierarchy = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            // Mesh nhân vật phải đọc được: CrowdLod dùng SkinnedMeshRenderer.BakeMesh, bản build cần dữ liệu mesh trên CPU.
            importer.isReadable = !anim;
            importer.generateSecondaryUV = false;
            if (anim)
            {
                importer.importAnimation = true;
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(CharacterImporter.AvatarModelPath);
                if (avatar != null)
                {
                    importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                    importer.sourceAvatar = avatar;
                }
            }
            else
            {
                importer.importAnimation = false;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                if (assetPath.StartsWith(CharacterImporter.CharRoot + "ChieuChild/"))
                {
                    // Bind trẻ thay đổi chiều dài chi; bỏ snapshot T-pose cũ để Unity đọc lại rig hiện tại.
                    var description = importer.humanDescription;
                    description.skeleton = Array.Empty<SkeletonBone>();
                    importer.humanDescription = description;
                }
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            }
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(CharacterImporter.AnimRoot))
                return;
            var importer = (ModelImporter)assetImporter;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            bool loop = Array.IndexOf(CharacterImporter.OneShots, name) < 0;
            var clips = importer.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = name;
                c.loopTime = loop;
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(CharacterImporter.CharRoot))
                return;
            if (Path.GetFileNameWithoutExtension(assetPath).EndsWith("_Normal"))
                ((TextureImporter)assetImporter).textureType = TextureImporterType.NormalMap;
        }
    }
}
