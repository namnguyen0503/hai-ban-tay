using HaiBanTay.Audio;
using HaiBanTay.Core;
using HaiBanTay.Editor.SceneBuilders.Ship;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Ending;
using HaiBanTay.Narrative;
using HaiBanTay.Ship;
using UnityEngine;
using HaiBanTay.Player;
using System.Collections.Generic;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>03 Marseille: model cảng đã nhập, tàu chung, trạm bếp thật và các điểm tương tác lấy từ layout.</summary>
    public static class MarseilleSceneBuilder
    {
        public const string SceneName = "03_Marseille";
        const float QuayY = -2.5f;

        public static void Build()
        {
            var scene = BuilderUtils.NewScene();
            var profile = AssetFactory.LightingProfile("Marseille");
            var cam = BuilderUtils.AddMainCamera(profile.background);
            var profileSun = BuilderUtils.ApplyLighting(profile);
            if (profileSun != null) Object.DestroyImmediate(profileSun.gameObject);
            BuilderUtils.AddPostVolume("Post_Ending", "Post_Ending");
            var env = BuilderUtils.Group("Environment").transform;
            var layout = ShipBuilder.Build(env, ShipConfig.Marseille);
            layout.galleyDoor.audioEnabled = true;
            layout.corridorDoor.audioEnabled = true;
            ShipInterior.HideEveningCorner(layout);
            GalleySceneBuilder.BuildCrewDiningRoom(layout.galley, new ShipInterior.Mats());
            var harbour = EnvLayout.Load("Marseille");
            var port = harbour.Place(layout.world);
            port.name = "MarseilleEnvironment";
            KeppelPortBuilder.ReplaceDistantSteamers(port);
            // Ocean đã giữ sóng và phản chiếu; tấm nước FBX cũ cắt ngang đỉnh sóng như những mảng tuyết.
            foreach (var renderer in port.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.name == "HarborWater") renderer.enabled = false;
            foreach (var part in port.GetComponentsInChildren<Transform>(true))
                if (part.name == "PortCart")
                    part.gameObject.AddComponent<MovingCartAudio>();
            // Ghế của Ba và bốn khách: bỏ hộp đặc lấp chỗ ngồi; capsule khách vẫn giữ vật cản.
            foreach (var collider in port.GetComponentsInChildren<Collider>())
                if (collider.name == "COL_CafeChair_0" || collider.name.StartsWith("COL_CafeChair_1") || collider.name.StartsWith("COL_CafeChair_2"))
                    collider.enabled = false;
            var city = BuilderUtils.Group("City", layout.world).transform;
            var iron = ShipBuilder.Iron;
            var wood = AssetFactory.Lit("Wood", new Color(0.4f, 0.27f, 0.16f), 0.15f);

            var start = BuilderUtils.Group("LowPass_Start", layout.gangway).transform;
            start.position = harbour.Anchor("gangway_top");
            var end = BuilderUtils.Group("LowPass_End", layout.gangway).transform;
            end.position = harbour.Anchor("gangway_foot");
            var lowPass = BuilderUtils.Trigger<LowPassZone>("LowPassZone", layout.gangway, (start.position + end.position) * 0.5f, new Vector3(1.6f, 4f, 6.4f));
            lowPass.start = start;
            lowPass.end = end;
            var quay = BuilderUtils.Trigger<BeatTrigger>("QuayStep", city, harbour.Anchor("quay_step") + Vector3.up, new Vector3(3f, 2f, 1.6f));
            quay.GetComponent<Collider>().enabled = false;
            var observe = BuilderUtils.Trigger<BeatTrigger>("QuayObserved", city, harbour.Anchor("mai_poverty_conversation") + Vector3.up, new Vector3(4f, 2f, 3f));
            observe.GetComponent<Collider>().enabled = false;
            var street = BuilderUtils.Trigger<BeatTrigger>("StreetEdge", city, harbour.Anchor("street_edge") + Vector3.up, new Vector3(80f, 2f, 1.5f));
            var streetZone = BuilderUtils.Trigger<ShipZone>("StreetAmbience", city, new Vector3(-10f, QuayY + 1.5f, -27f), new Vector3(148f, 4f, 22f));
            streetZone.region = ShipRegion.Exterior;
            var ambience = new GameObject("ShipAmbience").AddComponent<ShipAmbience>();
            ambience.marseille = true;
            ambience.galley = layout.galleyZone;
            ambience.hold = layout.holdZone;
            ambience.street = streetZone;
            var streetBarrier = ShipDeck.Wall(city, new Vector3(-14f, QuayY + 1f, -16.3f), new Vector3(148f, 2f, 0.1f));
            streetBarrier.name = "StreetBarrier";
            var tram = BuildTram(city, iron);
            tram.from.position = harbour.Anchor("tram_from");
            tram.to.position = harbour.Anchor("tram_to");

            var director = new GameObject("EndingDirector").AddComponent<EndingDirector>();
            director.ship = layout.motion;
            director.engine = new GameObject("EngineHum").AddComponent<EngineHum>();
            director.fog = BuildMist(layout.world);
            director.gangwayBarrier = layout.gangwayBarrier;
            director.streetBarrier = streetBarrier;
            director.quayTrigger = quay;
            director.observeTrigger = observe;
            director.streetTrigger = street;
            director.quayPoint = Point(city, "QuayPoint", harbour.Anchor("quay_step"), 180f);
            director.tram = tram;
            director.payPoint = Point(env, "PayPoint", harbour.Anchor("salary_queue"), -90f);
            BuildSalaryDesk(env, harbour, director, wood);
            GalleySceneBuilder.BuildMarseilleFinalWork(layout, harbour, director);
            director.chair = MarseilleNpcs.Target(city, "CafeSeatTarget", harbour.Anchor("cafe_seat"), new Vector3(0.9f, 1.2f, 0.9f), "prompt.sit");
            director.seatPoint = Point(city, "SeatPoint", harbour.Anchor("cafe_seat"), 180f);
            director.chairClip = System.Array.Find(UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Characters/Anims/SitChair.fbx"), a => a is AnimationClip && !a.name.StartsWith("__preview")) as AnimationClip;
            if (director.chairClip == null) throw new System.InvalidOperationException("Thiếu clip ngồi ghế SitChair.");
            director.table = Point(city, "CafeTablePoint", harbour.Anchor("cafe_table") - Vector3.up * 0.78f);
            // Anchor thấp hơn mặt bàn 1 cm; đáy đĩa mới ở Y=0, đặt cách mặt bàn 1 mm.
            director.waiterTablePoint = Point(city, "WaiterTablePoint", harbour.Anchor("waiter_table") + Vector3.up * 0.011f);
            director.cup = BuildCup(city);
            MarseilleNpcs.Build(layout, harbour, director);

            var coins = new GameObject("CoinDrop").AddComponent<CoinDrop>();
            var template = BuilderUtils.Prim(PrimitiveType.Cylinder, "CoinTemplate", coins.transform, Vector3.zero,
                new Vector3(0.026f, 0.0018f, 0.026f), AssetFactory.Lit("CoinSilver", new Color(0.75f, 0.74f, 0.7f), 0.7f, 0.9f), false, false);
            template.GetComponent<Renderer>().sharedMaterial.EnableKeyword("_EMISSION");
            BuilderUtils.SetRenderingLayer(template, WorldRenderingLayers.Interior | WorldRenderingLayers.Exterior);
            template.SetActive(false);
            coins.coinTemplate = template;
            director.coins = coins;
            // Đèn realtime: mặt trời, fill khoang; fill tay E2 là HandsFill chung của PlayerRig (LightBudget nhường suất). Chỉ một bóng.
            // Đèn làm việc realtime của khoang bếp là một đèn treo thật (kính phát sáng, tối theo khi LightBudget tắt đèn).
            var workLamp = Props.HangingLamp(layout.galley, new Vector3(2.8f, 2.6f, 0f), realtime: true, intensity: 8f, range: 12f,
                lightColor: new Color(1f, 0.9f, 0.78f));
            workLamp.name = "Lamp_Work";
            // Đèn bến Marseille (PortLamp_*): đèn không bóng, chỉ hai đèn gần camera nhất được bật.
            var portLamps = Lamps.LightEnvLamps(port.transform, "PortLamp_", new Color(1f, 0.78f, 0.5f), 3.2f, 9f);
            ShipBuilder.ConfigureLights(layout, portLamps, director.finalMeals.coal.fireLight, workLamp.GetComponentInChildren<Light>());
            // Góc rửa/chuẩn bị riêng 03 sáng gấp đôi; không đổi ShipInterior dùng chung.
            foreach (var light in layout.galley.GetComponentsInChildren<Light>(true))
                if (light.transform.parent != null && (light.transform.parent.name == "Lamp_Wash" || light.transform.parent.name == "Lamp_Prep"
                                                       || light.transform.parent.name == "Lantern_Wash"))
                    light.intensity *= 1.5f;
            var probes = BuilderUtils.Group("QuayProbes", env).transform;
            BuilderUtils.ProbeGrid("LightProbes_Gangway", probes, new Vector3(0f, -1f, -6.2f), new Vector3(4f, 4f, 6f), new Vector3(2f, 1.3f, 2f));
            BuilderUtils.ProbeGrid("LightProbes_Quay", probes, new Vector3(-5f, QuayY + 1.2f, -18f), new Vector3(50f, 2.4f, 18f), new Vector3(4f, 1.2f, 3f));
            BuilderUtils.BakedReflectionProbe("Reflection_Quay", probes, new Vector3(2f, QuayY + 2f, -14f), new Vector3(80f, 20f, 40f), false);
            var player = PlayerBuilder.Spawn(cam, harbour.PlayerSpawn, harbour.PlayerYaw);
            // Tay FP thật dùng chung PlayerBuilder; chỉ gán lớp Exterior để đèn fill E2 chiếu tới.
            BuilderUtils.SetRenderingLayer(player.hands.gameObject, WorldRenderingLayers.Exterior);
            // Fill E2 là đèn fill tay chung của PlayerRig (bật/tắt cùng khung hai bàn tay).
            director.payFill = player.handsFill;
            // Rạng sáng đã có ánh sáng môi trường; fill nhẹ, ấm để da và áo không trắng bệch so với người phát lương.
            director.payFill.color = new Color(1f, 0.78f, 0.58f);
            director.payFill.intensity = 0.45f;
            ShipBuilder.FinishWorld(layout);
            BuilderUtils.Save(scene, SceneName);
        }

        static Transform Point(Transform parent, string name, Vector3 position, float yaw = 0f)
        {
            var point = BuilderUtils.Group(name, parent).transform;
            point.position = position;
            point.rotation = Quaternion.Euler(0f, yaw, 0f);
            return point;
        }

        static void BuildSalaryDesk(Transform parent, EnvLayout env, EndingDirector director, Material wood)
        {
            var desk = BuilderUtils.Group("SalaryDesk", parent).transform;
            desk.position = env.Anchor("salary_table");
            BuilderUtils.Block("DeskTop", desk, new Vector3(0f, 0.74f, 0f), new Vector3(1.25f, 0.08f, 1.5f), wood);
            foreach (float x in new[] { -0.5f, 0.5f })
                foreach (float z in new[] { -0.6f, 0.6f })
                    BuilderUtils.Block("DeskLeg", desk, new Vector3(x, 0.35f, z), new Vector3(0.08f, 0.7f, 0.08f), wood);
            var canvas = AssetFactory.Lit("MrsPayCanvas", new Color(0.76f, 0.68f, 0.48f), 0.1f);
            // Mép tây mái cũ xuyên chụp thông gió và che vỏ trắng, để lộ lòng đỏ như một đĩa treo.
            // Thu mái/phần chống về đúng bàn phát lương, chừa chụp tàu hoàn chỉnh bên ngoài.
            BuilderUtils.Block("PayCanopy", desk, new Vector3(0.6f, 2.6f, 0f), new Vector3(2.6f, 0.05f, 3.5f), canvas, true, false);
            foreach (float x in new[] { -0.65f, 1.85f })
                foreach (float z in new[] { -1.7f, 1.7f })
                    BuilderUtils.Block("PayCanopyPole", desk, new Vector3(x, 1.3f, z), new Vector3(0.06f, 2.6f, 0.06f), wood, true, false);
            var book = BuilderUtils.Group("SalaryLedger", desk);
            book.transform.localPosition = new Vector3(0.1f, 0.79f, 0f);
            book.layer = BuilderUtils.Layer("Interactable");
            book.tag = "Interactable";
            var aim = book.AddComponent<BoxCollider>();
            aim.isTrigger = true;
            aim.size = new Vector3(0.8f, 0.16f, 1.2f);
            var surface = BuilderUtils.Prim(PrimitiveType.Quad, "LedgerPages", book.transform, Vector3.up * 0.015f, new Vector3(1.05f, 0.7f, 1f), canvas, false, false, new Vector3(90f, 0f, 0f));
            director.ledger = book.AddComponent<MarseilleLedger>();
            director.ledger.surface = surface.GetComponent<Renderer>();
            BuilderUtils.SetRenderingLayer(desk.gameObject, WorldRenderingLayers.Exterior);
        }

        static Transform BuildCup(Transform parent)
        {
            // Tách/đĩa dựng riêng 03: giữ đáy và chiều cao rõ, không tự xoay/ép model theo khung ArtSlot.
            var porcelain = AssetFactory.Lit("MrsPorcelain", new Color(0.97f, 0.97f, 0.95f), 0.55f);
            var coffee = AssetFactory.Lit("MrsCoffee", new Color(0.07f, 0.025f, 0.01f), 0.5f);
            var cup = BuilderUtils.Group("CoffeeCup", parent);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Saucer", cup.transform, new Vector3(0f, 0.006f, 0f),
                new Vector3(0.17f, 0.006f, 0.17f), porcelain, false, false);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Cup", cup.transform, new Vector3(0f, 0.052f, 0f),
                new Vector3(0.09f, 0.04f, 0.09f), porcelain, false, false);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Coffee", cup.transform, new Vector3(0f, 0.093f, 0f),
                new Vector3(0.075f, 0.001f, 0.075f), coffee, false, false);
            // Quai chữ C từ ba đoạn bo tròn, mở giữa để đọc được cả khi nhìn gần ngang.
            foreach (float y in new[] { 0.03f, 0.074f })
                BuilderUtils.Prim(PrimitiveType.Capsule, "HandleArm", cup.transform, new Vector3(0.055f, y, 0f),
                    new Vector3(0.012f, 0.016f, 0.012f), porcelain, false, false, new Vector3(0f, 0f, 90f));
            BuilderUtils.Prim(PrimitiveType.Capsule, "Handle", cup.transform, new Vector3(0.07f, 0.052f, 0f),
                new Vector3(0.012f, 0.024f, 0.012f), porcelain, false, false);
            BuilderUtils.SetRenderingLayer(cup, WorldRenderingLayers.Exterior);
            return cup.transform;
        }

        static TramPass BuildTram(Transform city, Material iron)
        {
            var body = AssetFactory.Lit("TramBody", new Color(0.62f, 0.2f, 0.14f), 0.3f);
            var cream = AssetFactory.Lit("TramCream", new Color(0.9f, 0.85f, 0.7f), 0.3f);
            var glass = AssetFactory.Lit("TramGlass", new Color(0.2f, 0.25f, 0.28f), 0.8f);
            var tram = BuilderUtils.ArtSlot("Tram", city, new Vector3(-36f, QuayY, -18.5f), new Vector3(0f, 90f, 0f), t =>
            {
                BuilderUtils.Block("Lower", t, new Vector3(0f, 0.9f, 0f), new Vector3(2.2f, 1.2f, 9f), body, false, true);
                BuilderUtils.Block("Upper", t, new Vector3(0f, 2.1f, 0f), new Vector3(2.2f, 1.2f, 9f), cream, false, false);
                BuilderUtils.Block("WindowsL", t, new Vector3(-1.11f, 2.1f, 0f), new Vector3(0.02f, 0.8f, 8f), glass, false, false);
                BuilderUtils.Block("WindowsR", t, new Vector3(1.11f, 2.1f, 0f), new Vector3(0.02f, 0.8f, 8f), glass, false, false);
                BuilderUtils.Block("Roof", t, new Vector3(0f, 2.8f, 0f), new Vector3(2.4f, 0.15f, 9.4f), iron, false, false);
                BuilderUtils.Block("Pole", t, new Vector3(0f, 3.6f, 0f), new Vector3(0.05f, 1.5f, 0.05f), iron, false, false, new Vector3(-30f, 0f, 0f));
                return t.gameObject;
            });
            BuilderUtils.ClearStatic(tram);
            var from = new GameObject("Tram_From").transform;
            from.SetParent(city, false);
            from.position = new Vector3(-36f, QuayY, -18.5f);
            var to = new GameObject("Tram_To").transform;
            to.SetParent(city, false);
            to.position = new Vector3(36f, QuayY, -18.5f);
            var pass = tram.AddComponent<TramPass>();
            pass.tram = tram.transform;
            pass.from = from;
            pass.to = to;
            return pass;
        }

        static FogLift BuildMist(Transform env)
        {
            var mist = AssetFactory.Transparent("Mist", new Color(0.68f, 0.73f, 0.77f, 0.10f));
            var root = BuilderUtils.Group("MistLayers", env);
            var layers = new Renderer[3];
            for (int i = 0; i < 3; i++)
            {
                var q = BuilderUtils.Prim(PrimitiveType.Quad, "Mist_" + i, root.transform, new Vector3(0f, 6f, -110f - i * 35f),
                    new Vector3(260f, 30f, 1f), mist, false, false, new Vector3(0f, 180f, 0f));
                q.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                layers[i] = q.GetComponent<Renderer>();
            }
            var fog = root.AddComponent<FogLift>();
            fog.mistLayers = layers;
            return fog;
        }
    }
}
