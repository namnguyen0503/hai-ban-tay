using UnityEngine;
using UnityEngine.SceneManagement;

namespace HaiBanTay.Ship
{
    /// <summary>
    /// Ô cửa sổ tròn (đường kính 0,5 m) nhỏ hơn độ phân giải của dữ liệu occlusion đã bake (smallestOccluder 1 m), nên bake coi
    /// cả mảng tường là đặc và ẩn mọi thứ phía sau: nhìn qua ô tròn từ gần thì thủy thủ ngoài boong biến mất, đứng xa lại thấy.
    /// Khi người chơi ở gần ô và đang quay về phía nó, tắt occlusion culling của camera chính; ngoài trường hợp đó vẫn bật.
    /// Tự gắn vào mọi "ArtSlot_Porthole" khi cảnh nạp, nên không cần dựng lại cảnh.
    /// </summary>
    public sealed class PortholeOcclusionGuard : MonoBehaviour
    {
        const string SlotName = "ArtSlot_Porthole";

        /// <summary>Khoảng cách tối đa (m) từ mắt tới ô để tắt occlusion.</summary>
        public float range = 8f;
        /// <summary>Cos góc tối thiểu giữa hướng nhìn và hướng tới ô.</summary>
        public float minDot = 0.3f;

        Camera _cam;
        bool _forced;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                Attach(SceneManager.GetSceneAt(i));
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach(scene);

        static void Attach(Scene scene)
        {
            if (!scene.isLoaded)
                return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == SlotName && t.GetComponent<PortholeOcclusionGuard>() == null)
                        t.gameObject.AddComponent<PortholeOcclusionGuard>();
        }

        void LateUpdate()
        {
            if (_cam == null || !_cam.isActiveAndEnabled)
            {
                Restore();
                _cam = Camera.main;
                if (_cam == null)
                    return;
            }
            var to = transform.position - _cam.transform.position;
            float d = to.magnitude;
            bool near = d < range && d > 1e-3f && Vector3.Dot(_cam.transform.forward, to / d) > minDot;
            if (near == _forced)
                return;
            _forced = near;
            _cam.useOcclusionCulling = !near;
        }

        void OnDisable() => Restore();

        void Restore()
        {
            if (_forced && _cam != null)
                _cam.useOcclusionCulling = true;
            _forced = false;
        }
    }
}
