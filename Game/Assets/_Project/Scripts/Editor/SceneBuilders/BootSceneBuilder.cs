using HaiBanTay.Core;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>00_Boot: camera đen + BootLoader. GameRoot tự tạo trước khi nạp cảnh (GameRoot.Bootstrap).</summary>
    public static class BootSceneBuilder
    {
        public static void Build()
        {
            var scene = BuilderUtils.NewScene();
            BuilderUtils.AddCamera(new Vector3(0f, 1f, -10f), Vector3.zero);
            new GameObject("BootLoader").AddComponent<BootLoader>();
            BuilderUtils.Save(scene, "00_Boot");
        }
    }
}
