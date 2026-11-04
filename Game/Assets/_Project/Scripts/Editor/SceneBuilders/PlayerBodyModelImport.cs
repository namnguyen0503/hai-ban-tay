using HaiBanTay.Editor.Import;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>Nhập clip Run riêng của anh Ba, Avatar canonical sẵn có; không đổi importer Art gốc.</summary>
    public sealed class PlayerBodyModelImport : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (assetPath != PlayerBodyBuilder.Folder + "/Run.fbx") return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(CharacterImporter.AvatarModelPath);
            importer.preserveHierarchy = true;
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
        }

        void OnPreprocessAnimation()
        {
            if (assetPath != PlayerBodyBuilder.Folder + "/Run.fbx") return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.name = "Run";
                clip.loopTime = true;
                clip.lockRootRotation = true;
                clip.lockRootPositionXZ = true;
                clip.lockRootHeightY = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }
    }
}
