using HaiBanTay.Editor.Setup;
using HaiBanTay.Interaction;
using HaiBanTay.Narrative;
using HaiBanTay.Ship;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>90_Sandbox (không đưa vào build): sàn, bàn, một bao khoai, một DropZone, một đèn treo.</summary>
    public static class SandboxSceneBuilder
    {
        public const string SceneName = "90_Sandbox";

        public static void Build()
        {
            var scene = BuilderUtils.NewScene();
            var profile = AssetFactory.LightingProfile("Sandbox");
            var cam = BuilderUtils.AddMainCamera(profile.background);
            BuilderUtils.ApplyLighting(profile);
            BuilderUtils.AddPostVolume();

            var env = BuilderUtils.Group("Environment").transform;
            var floor = AssetFactory.Lit("Floor", new Color(0.45f, 0.4f, 0.34f));
            var wall = AssetFactory.Lit("Wall", new Color(0.6f, 0.57f, 0.5f));
            var wood = AssetFactory.Lit("Wood", new Color(0.4f, 0.27f, 0.16f), 0.15f);
            BuilderUtils.Block("Floor", env, new Vector3(0f, -0.05f, 0f), new Vector3(10f, 0.1f, 10f), floor);
            BuilderUtils.Block("Wall_N", env, new Vector3(0f, 1.3f, 5f), new Vector3(10f, 2.6f, 0.2f), wall);
            BuilderUtils.Block("Wall_S", env, new Vector3(0f, 1.3f, -5f), new Vector3(10f, 2.6f, 0.2f), wall);
            BuilderUtils.Block("Wall_E", env, new Vector3(5f, 1.3f, 0f), new Vector3(0.2f, 2.6f, 10f), wall);
            BuilderUtils.Block("Wall_W", env, new Vector3(-5f, 1.3f, 0f), new Vector3(0.2f, 2.6f, 10f), wall);

            var table = BuilderUtils.ArtSlot("PrepTable", env, new Vector3(0f, 0f, 2.5f), Vector3.zero, t =>
            {
                BuilderUtils.Block("Top", t, new Vector3(0f, 0.85f, 0f), new Vector3(1.8f, 0.08f, 0.9f), wood);
                BuilderUtils.Block("Base", t, new Vector3(0f, 0.4f, 0f), new Vector3(1.6f, 0.8f, 0.7f), wood);
                return t.gameObject;
            });

            var zone = BuilderUtils.Trigger<DropZone>("SackDropZone", table.transform, new Vector3(0f, 1.05f, 0f), new Vector3(1.6f, 0.4f, 0.8f));
            zone.gameObject.layer = BuilderUtils.Layer("Interactable");
            zone.acceptKind = "sack";
            zone.anchor = new GameObject("Anchor").transform;
            zone.anchor.SetParent(zone.transform, false);
            zone.anchor.localPosition = new Vector3(0f, 0.12f, 0f);

            Props.PotatoSack(env, new Vector3(-2f, 0f, -1f), new Bounds(Vector3.zero, new Vector3(10f, 6f, 10f)));
            Props.SmallCrate(env, new Vector3(2f, 0f, -1f));

            var ship = new GameObject("ShipMotion").AddComponent<ShipMotion>();
            ship.intensity = 0.3f;
            Props.HangingLamp(env, new Vector3(0f, 2.6f, 0f), realtime: true);
            SandboxCrowd.Build(env);

            new GameObject("SandboxDirector").AddComponent<SandboxDirector>();
            PlayerBuilder.Spawn(cam, new Vector3(0f, 0f, -3f), 0f);
            SandboxWorld.Build(env);
            BuilderUtils.Save(scene, SceneName);
        }
    }
}
