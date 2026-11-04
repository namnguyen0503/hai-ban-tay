using UnityEngine;
using UnityEngine.AI;

namespace HaiBanTay.Npc
{
    /// <summary>
    /// NavMesh của cảnh (W5.2): dữ liệu do scene builder nướng từ collider tĩnh (bán kính thân 0,25 m) và lưu thành asset; bật cảnh
    /// thì nạp vào hệ NavMesh, tắt cảnh thì gỡ. NPC có NpcAgent.useNavMesh tính đường trên đó.
    /// </summary>
    public sealed class NavMeshLoader : MonoBehaviour
    {
        public NavMeshData data;
        NavMeshDataInstance _instance;

        void OnEnable()
        {
            if (data != null && !_instance.valid)
                _instance = NavMesh.AddNavMeshData(data, transform.position, transform.rotation);
        }

        void OnDisable()
        {
            if (_instance.valid)
                _instance.Remove();
        }
    }
}
