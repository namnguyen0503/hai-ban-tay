using System.Collections.Generic;
using System.IO;
using HaiBanTay.Interaction;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Ship;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>
    /// Nướng NavMesh cho cảnh (W5.2) từ collider tĩnh: bỏ trigger, thân NPC, người chơi, đồ cầm được và cánh cửa bản lề (NPC đi
    /// qua thì cửa mở). Thân NPC bán kính 0,25 m, cao
    /// 1,75 m, bước 0,3 m. Lưu thành asset cạnh scene (ghi đè nội dung asset cũ, giữ GUID) và gắn NavMeshLoader nạp lúc chạy.
    /// </summary>
    public static class NavMeshBake
    {
        public const float AgentRadius = 0.25f;

        public static NavMeshData Bake(string sceneName, Bounds area)
        {
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = AgentRadius;
            settings.agentHeight = 1.75f;
            settings.agentClimb = 0.3f;
            settings.agentSlope = 40f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = 0.05f;
            settings.minRegionArea = 0.5f;

            var sources = new List<NavMeshBuildSource>();
            var markups = new List<NavMeshBuildMarkup>();
            NavMeshBuilder.CollectSources(area, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
            sources.RemoveAll(s =>
            {
                var c = s.component as Collider;
                if (c == null) return false;
                return c.isTrigger || !c.enabled || !c.gameObject.activeInHierarchy
                       || c.GetComponentInParent<NpcAgent>() != null || c.GetComponentInParent<PlayerRig>() != null
                       || c.GetComponentInParent<Holdable>() != null || c.GetComponentInParent<HingedDoor>() != null;
            });
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, area, Vector3.zero, Quaternion.identity);
            if (data == null)
                throw new System.InvalidOperationException("Không nướng được NavMesh cho " + sceneName);
            data.name = "NavMesh_" + sceneName;

            string folder = BuilderUtils.ScenesFolder + "/" + sceneName;
            Directory.CreateDirectory(folder);
            string path = folder + "/NavMesh.asset";
            var existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(data, existing);
                existing.name = data.name;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(data);
                data = existing;
            }
            else AssetDatabase.CreateAsset(data, path);
            AssetDatabase.SaveAssets();

            // Gốc cảnh, không xoay: dữ liệu nướng theo tọa độ thế giới.
            var loader = new GameObject("NavMesh").AddComponent<NavMeshLoader>();
            loader.data = data;
            return data;
        }
    }
}
