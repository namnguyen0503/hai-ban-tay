using System.Diagnostics;
using HaiBanTay.Core;
using HaiBanTay.Editor.SceneBuilders;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HaiBanTay.Editor.Lighting
{
    /// <summary>
    /// M6: bake lightmap + light probe + reflection probe cho 00_Prologue và 01–03 bằng Progressive CPU
    /// (GPU lightmapper không chạy được trên MI50/ROCm); M10b thêm occlusion culling. Tools/bake_lighting.sh gọi BakeAll.
    /// Phải bake lại sau mỗi lần rebuild_scenes.sh vì scene dựng lại mất tham chiếu LightingData.
    /// </summary>
    public static class LightingBaker
    {
        [MenuItem("HaiBanTay/Lighting/Bake All")]
        public static void BakeAll()
        {
            bool ok = true;
            var total = Stopwatch.StartNew();
            ok &= Bake(SceneFlow.PrologueScene);
            foreach (var name in SceneFlow.GameScenes)
                ok &= Bake(name);
            AssetDatabase.SaveAssets();
            Debug.Log($"LightingBaker: {(ok ? "OK" : "LỖI")} sau {total.Elapsed.TotalMinutes:0.0} phút");
            if (Application.isBatchMode)
                EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>Chỉ bake 00_Prologue (đi cùng SceneBuildAll.RunPrologue).</summary>
        [MenuItem("HaiBanTay/Lighting/Bake Prologue Only")]
        public static void BakePrologue()
        {
            var total = Stopwatch.StartNew();
            bool ok = Bake(SceneFlow.PrologueScene);
            AssetDatabase.SaveAssets();
            Debug.Log($"LightingBaker: {(ok ? "OK" : "LỖI")} Prologue sau {total.Elapsed.TotalMinutes:0.0} phút");
            if (Application.isBatchMode)
                EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Bake(string sceneName)
        {
            var scene = EditorSceneManager.OpenScene(BuilderUtils.ScenePath(sceneName), OpenSceneMode.Single);
            var settings = Lightmapping.lightingSettings;
            if (settings == null)
            {
                Debug.LogError($"LightingBaker: {sceneName} chưa gán LightingSettings (chạy lại rebuild_scenes.sh)");
                return false;
            }
            if (settings.lightmapper != LightingSettings.Lightmapper.ProgressiveCPU)
            {
                settings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
                EditorUtility.SetDirty(settings);
            }

            var sw = Stopwatch.StartNew();
            bool baked = Lightmapping.Bake();
            int maps = LightmapSettings.lightmaps != null ? LightmapSettings.lightmaps.Length : 0;
            int probes = LightmapSettings.lightProbes != null ? LightmapSettings.lightProbes.count : 0;
            Debug.Log($"LightingBaker: {sceneName} {(baked ? "OK" : "LỖI")} sau {sw.Elapsed.TotalSeconds:0} s · " +
                      $"{maps} lightmap · {probes} light probe");
            if (!baked)
                return false;

            // M10b: occlusion culling cho cả con tàu (vách thượng tầng, khối bếp che phần còn lại của tàu và phố).
            sw.Restart();
            StaticOcclusionCulling.smallestOccluder = 1f;
            StaticOcclusionCulling.smallestHole = 0.25f;
            StaticOcclusionCulling.backfaceThreshold = 100f;
            bool culled = StaticOcclusionCulling.Compute();
            Debug.Log($"LightingBaker: {sceneName} occlusion {(culled ? "OK" : "LỖI")} sau {sw.Elapsed.TotalSeconds:0} s");
            if (!culled)
                return false;
            if (!EditorSceneManager.SaveScene(scene))
            {
                Debug.LogError("LightingBaker: không lưu được " + sceneName);
                return false;
            }
            return true;
        }
    }
}
