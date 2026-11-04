using UnityEngine;

namespace HaiBanTay.Core
{
    /// <summary>Đặt trong 00_Boot: nạp menu chính sau khi GameRoot sẵn sàng.</summary>
    public sealed class BootLoader : MonoBehaviour
    {
        public string firstScene = SceneFlow.MenuScene;

        void Start()
        {
            var root = GameRoot.EnsureExists();
            if (root != null && root.Scenes != null)
                root.Scenes.Load(firstScene);
        }
    }
}
