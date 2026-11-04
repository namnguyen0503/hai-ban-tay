using HaiBanTay.Editor.Setup;
using HaiBanTay.Narrative;
using HaiBanTay.Ship;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>00_Menu: góc bếp đêm (bàn, đèn dầu, cuốn sách, ô cửa sổ biển đêm lắc nhẹ) làm nền cho menu chính.
    /// Không bake: chỉ đèn dầu realtime và ambient tối.</summary>
    public static class MenuSceneBuilder
    {
        public const string SceneName = "00_Menu";

        public static void Build()
        {
            var scene = BuilderUtils.NewScene();
            var profile = AssetFactory.LightingProfile("Galley");
            var cam = BuilderUtils.AddMainCamera(profile.background, brain: false);
            cam.transform.SetPositionAndRotation(new Vector3(-0.55f, 1.25f, -1.25f), Quaternion.Euler(14f, 18f, 0f));
            cam.fieldOfView = 50f;
            BuilderUtils.ApplyLighting(profile);
            BuilderUtils.AddPostVolume();

            var wood = AssetFactory.Lit("Wood", new Color(0.4f, 0.27f, 0.16f), 0.15f);
            var wall = AssetFactory.Lit("GalleyWall", new Color(0.55f, 0.5f, 0.42f), 0.1f);
            var floor = AssetFactory.Lit("GalleyFloor", new Color(0.33f, 0.24f, 0.17f), 0.15f);
            var brass = AssetFactory.Lit("Brass", new Color(0.7f, 0.58f, 0.3f), 0.55f, 0.8f);
            var paper = AssetFactory.Lit("Paper", new Color(0.86f, 0.8f, 0.66f), 0.05f);

            var env = BuilderUtils.Group("Environment").transform;
            BuilderUtils.Block("Floor", env, new Vector3(0f, -0.05f, 0.5f), new Vector3(4f, 0.1f, 3.5f), floor, false);
            BuilderUtils.Block("Wall_Back", env, new Vector3(0f, 1.3f, 1.25f), new Vector3(4f, 2.6f, 0.1f), wall, false);
            BuilderUtils.Block("Wall_Right", env, new Vector3(1.5f, 1.3f, 0f), new Vector3(0.1f, 2.6f, 3f), wall, false);
            BuilderUtils.Block("Beam", env, new Vector3(0f, 2.45f, 0.4f), new Vector3(4f, 0.16f, 0.15f), wood, false);

            var table = BuilderUtils.ArtSlot("SmallTable", env, new Vector3(0.45f, 0f, 0.7f), Vector3.zero, t =>
            {
                BuilderUtils.Block("Top", t, new Vector3(0f, 0.74f, 0f), new Vector3(0.7f, 0.04f, 0.6f), wood, false);
                BuilderUtils.Block("Leg", t, new Vector3(0f, 0.36f, 0f), new Vector3(0.1f, 0.72f, 0.1f), wood, false);
                return t.gameObject;
            });
            BuilderUtils.ClearStatic(table);
            Props.OilLamp(env, new Vector3(0.62f, 0.76f, 0.82f), true, 2.2f, 5f, out _);
            var book = BuilderUtils.ArtSlot("Book", env, new Vector3(0.36f, 0.78f, 0.62f), new Vector3(0f, 25f, 0f), t =>
            {
                BuilderUtils.Block("Pages", t, Vector3.zero, new Vector3(0.28f, 0.03f, 0.2f), paper, false, false);
                return t.gameObject;
            });
            BuilderUtils.ClearStatic(book);

            // Ô cửa sổ tròn: biển đêm nghiêng theo tàu.
            var ring = BuilderUtils.ArtSlot("Porthole", env, new Vector3(0.15f, 1.55f, 1.2f), new Vector3(90f, 0f, 0f), t =>
            {
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Ring", t, Vector3.zero, new Vector3(0.6f, 0.03f, 0.6f), brass, false, false);
                return t.gameObject;
            });
            BuilderUtils.ClearStatic(ring);
            var view = new GameObject("PortholeView_Night");
            view.transform.SetParent(env, false);
            view.transform.position = new Vector3(0.15f, 1.55f, 1.195f);
            view.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            view.transform.localScale = new Vector3(0.5f, 0.5f, 1f);
            view.AddComponent<MeshFilter>().sharedMesh = SurfaceMesh.Disk();
            var r = view.AddComponent<MeshRenderer>();
            r.sharedMaterial = AssetFactory.PortholeView(true);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            view.AddComponent<PortholeView>();

            var ship = new GameObject("ShipMotion").AddComponent<ShipMotion>();
            ship.intensity = 0.12f;
            new GameObject("MenuDirector").AddComponent<MenuDirector>();
            BuilderUtils.Save(scene, SceneName);
        }
    }
}
