using System;
using HaiBanTay.Editor.Setup;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    public static class SceneBuildAll
    {
        public static readonly string[] BuildScenes = { "00_Boot", "00_Menu", "00_Prologue", "01_NhaRong", "02_Galley", "03_Marseille" };

        [MenuItem("HaiBanTay/Scenes/Build All")]
        public static void Run()
        {
            try
            {
                ProjectSetup.RunAll();
                BootSceneBuilder.Build();
                MenuSceneBuilder.Build();
                PrologueSceneBuilder.Build();
                NhaRongSceneBuilder.Build();
                GalleySceneBuilder.Build();
                MarseilleSceneBuilder.Build();
                SandboxSceneBuilder.Build();
                ApplyBuildSettings();
                AssetDatabase.SaveAssets();
                Debug.Log("SceneBuildAll: xong.");
            }
            catch (Exception e)
            {
                Debug.LogError("SceneBuildAll thất bại: " + e);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
                throw;
            }
        }

        /// <summary>Chỉ dựng lại 00_Prologue (sửa phố Sài Gòn không cần dựng lại cả game); bake bằng LightingBaker.BakePrologue.</summary>
        [MenuItem("HaiBanTay/Scenes/Build Prologue Only")]
        public static void RunPrologue()
        {
            try
            {
                ProjectSetup.RunAll();
                PrologueSceneBuilder.Build();
                AssetDatabase.SaveAssets();
                Debug.Log("SceneBuildAll: Prologue xong.");
            }
            catch (Exception e)
            {
                Debug.LogError("SceneBuildAll.RunPrologue thất bại: " + e);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
                throw;
            }
        }

        public static void ApplyBuildSettings()
        {
            var scenes = new EditorBuildSettingsScene[BuildScenes.Length];
            for (int i = 0; i < scenes.Length; i++)
                scenes[i] = new EditorBuildSettingsScene(BuilderUtils.ScenePath(BuildScenes[i]), true);
            EditorBuildSettings.scenes = scenes;
        }
    }
}
