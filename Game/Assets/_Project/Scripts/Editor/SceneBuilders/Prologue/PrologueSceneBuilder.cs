using System.Collections.Generic;
using HaiBanTay.Audio;
using HaiBanTay.Editor.Import;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Interaction;
using HaiBanTay.Narrative;
using HaiBanTay.Npc;
using HaiBanTay.Performance;
using HaiBanTay.Ship;
using UnityEngine;
using UnityEditor;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>
    /// 00_Prologue: phố Sài Gòn buổi tối 1911 (19:30, không biển), anh Ba (người chơi) đi cùng anh Lê tới quán cà phê Pháp có đèn
    /// điện. Hình phố là model thật (<see cref="SaigonStreet"/>: Env_SaigonStreet theo layout.json), mọi điểm neo ở
    /// <see cref="SaigonAnchors"/>. Ánh sáng: trăng realtime có bóng (1) + 2 đèn phố realtime không bóng ở chỗ nói chuyện và trước
    /// quán; các đèn phố còn lại là đèn bake (bóng đèn emissive của model). Đám đông CrowdBuilder (vai người Sài Gòn mới): người qua
    /// lại, một nhóm nói chuyện, bồi bàn, khách ngồi quán.
    /// </summary>
    public static class PrologueSceneBuilder
    {
        public const string SceneName = "00_Prologue";
        /// <summary>Vai người Sài Gòn 1911: dân thường, người Pháp, phu (Prefabs/NPC).</summary>
        static readonly string[] CrowdRoles = { "CivilianVN", "PassengerFR_M1", "PorterVN_B", "PassengerFR_F" };
        // Vàng ấm của bóng dây tóc nhưng không cam đậm (bản cam 1; 0,66; 0,32 nhuộm cả phố, 17/10).
        static readonly Color LampColor = new Color(1f, 0.78f, 0.54f);

        /// <summary>
        /// Hoàng hôn tím đỏ sau giờ tắt nắng: chân trời hồng cam, đỉnh trời tím sẫm, haze ấm; ánh sáng chính là tàn chiều tím nhạt
        /// (hơi mờ, không thành trăng lạnh). Hai keyframe giống nhau nên cảnh đứng yên ở một "giờ" (19:30).
        /// </summary>
        static WorldLightingKeyframe[] DuskKeyframes()
        {
            WorldLightingKeyframe K(float hour) => new WorldLightingKeyframe(hour,
                new Color(0.17f, 0.07f, 0.15f), new Color(0.78f, 0.34f, 0.28f), new Color(0.30f, 0.15f, 0.11f),
                new Color(0.78f, 0.52f, 0.78f), 0.5f, 0.009f, 0.3f,
                new Color(0.025f, 0.065f, 0.11f), new Color(0.13f, 0.16f, 0.17f));
            return new[] { K(19f), K(22f) };
        }

        /// <summary>Anh Lê: vai Le (áo trắng học trò). Chưa có prefab Le thì CrowdBuilder dựng khối xám, không đổi sang vai khác.</summary>
        public static string LeRole() => "Le";

        public static void Build()
        {
            var scene = BuilderUtils.NewScene();
            var profile = AssetFactory.LightingProfile("Prologue");
            var cam = BuilderUtils.AddMainCamera(profile.background);
            var profileSun = BuilderUtils.ApplyLighting(profile);
            if (profileSun != null)
                Object.DestroyImmediate(profileSun.gameObject);
            BuilderUtils.AddPostVolume("Post_Dusk_Laptop", "Post_Dusk_PC");

            var a = SaigonAnchors.Default();
            var env = BuilderUtils.Group("Environment").transform;
            // Trời đêm của WorldRoot (trăng là đèn chính); mặt nước tắt vì đây là phố.
            var worldConfig = WorldConfig.Sea(19.5f);
            worldConfig.keyframes = DuskKeyframes();
            var world = WorldBuilder.Build(env, worldConfig);
            world.Find("Ocean").gameObject.SetActive(false);
            var clock = world.GetComponent<TimeOfDay>();
            clock.ambientScale = 0.45f;
            clock.cloudLitTint = new Color(1.15f, 1.05f, 1.0f);
            clock.cloudShadeTint = new Color(1.0f, 0.75f, 0.8f);
            clock.Apply();
            SaigonStreet.Place(env);
            BuildRiverEnd(env);
            var lights = BuildLamps(env, a);
            var anchors = BuildAnchors(env, a);
            var crowd = BuildCrowd(env, a);
            var le = BuildLe(env, a);

            var zone = BuilderUtils.Trigger<PlayerArea>("TalkZone", anchors, a.conversationBa + new Vector3(-0.4f, 1.2f, 0f), new Vector3(3.6f, 2.5f, 3f));
            var ambience = BuilderUtils.Trigger<AmbienceZone>("StreetAmbience", env, new Vector3(0f, 4f, 0f), new Vector3(88f, 12f, 24f));
            ambience.clipKey = "Ambience/saigon_evening";
            ambience.volume = 0.65f;
            ambience.area = ambience.gameObject.AddComponent<PlayerArea>();
            var director = new GameObject("PrologueDirector").AddComponent<PrologueDirector>();
            director.ambience = ambience;
            director.le = le;
            director.lePath = new Transform[a.lePath.Length];
            for (int i = 1; i < a.lePath.Length; i++)
                director.lePath[i] = anchors.Find("LePath_" + i);
            director.lePath = System.Array.FindAll(director.lePath, t => t != null);
            director.talkLe = anchors.Find("conversation_le");
            director.talkPlayer = anchors.Find("conversation_ba");
            director.talkZone = zone;
            BuildDiscoveries(env, director, a);

            var moon = world.GetComponent<TimeOfDay>().mainLight;
            RenderSettings.sun = moon;
            var budget = env.gameObject.AddComponent<LightBudget>();
            var exterior = new List<Light> { moon };
            exterior.AddRange(lights);
            budget.exterior = exterior.ToArray();
            BuilderUtils.SetLightLayer(moon, WorldRenderingLayers.Exterior);
            // Mọi thứ trong cảnh là ngoài trời: hình và đèn cùng lớp Exterior (người chơi, tay do RegionLighting lo).
            BuilderUtils.SetRenderingLayer(env.gameObject, WorldRenderingLayers.Exterior);

            var probes = BuilderUtils.Group("Probes", env).transform;
            BuilderUtils.ProbeGrid("LightProbes_Street", probes, new Vector3(0f, 1.3f, 0f), new Vector3(84f, 2.6f, 20f), new Vector3(3f, 1.3f, 3f));
            BuilderUtils.BakedReflectionProbe("Reflection_Street", probes, new Vector3(0f, 3f, 0f), new Vector3(84f, 14f, 24f), false);
            BuilderUtils.SetRenderingLayer(crowd.gameObject, WorldRenderingLayers.Exterior);

            PlayerBuilder.Spawn(cam, a.playerSpawn, a.playerYaw);
            BuilderUtils.Save(scene, SceneName);
        }

        /// <summary>
        /// Khoảng sông cuối phố (DIRECTOR §9): sau rào công trường phía đông (x 42) là mặt sông và cột buồm tàu đậu, mỗi cột
        /// một ngọn đèn nhỏ chỉ phát sáng (không phải Light, không tốn ngân sách đèn). Người chơi đi về phía đông nên thấy cột buồm
        /// nhô trên rào; còi tàu xa do PrologueDirector phát.
        /// </summary>
        static void BuildRiverEnd(Transform env)
        {
            var river = BuilderUtils.Group("RiverEnd", env).transform;
            var water = AssetFactory.Lit("ProRiverWater", new Color(0.05f, 0.08f, 0.08f), 0.85f);
            var wood = AssetFactory.Lit("ProMastWood", new Color(0.16f, 0.12f, 0.09f), 0.2f);
            var hull = AssetFactory.Lit("ProShipHull", new Color(0.015f, 0.015f, 0.02f), 0.05f);
            var glow = AssetFactory.Emissive("ProMastBeacon", new Color(1f, 0.85f, 0.55f), new Color(3.2f, 2.3f, 1.2f), false);
            BuilderUtils.Block("RiverWater", river, new Vector3(78f, -1.2f, 0f), new Vector3(70f, 0.1f, 60f), water, false, false);
            var masts = new[] { new Vector3(54f, 0f, -5f), new Vector3(61f, 0f, 3f), new Vector3(70f, 0f, -1f) };
            float[] heights = { 13f, 16f, 11f };
            for (int i = 0; i < masts.Length; i++)
            {
                float h = heights[i];
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Mast_" + i, river, masts[i] + Vector3.up * (h * 0.5f - 1.2f),
                    new Vector3(0.22f, h * 0.5f, 0.22f), wood, false, false);
                BuilderUtils.Block("Yard_" + i, river, masts[i] + Vector3.up * (h * 0.7f - 1.2f), new Vector3(0.12f, 0.12f, 5f), wood, false, false);
                // Thân tàu dọc theo bến: đen sẫm nhô trên hàng rào, để cột buồm đứng trên một con tàu chứ không trơ giữa trời.
                BuilderUtils.Block("Hull_" + i, river, masts[i] + new Vector3(0f, 0.6f, 0f), new Vector3(16f, 3.6f, 3.0f), hull, false, false);
                BuilderUtils.Prim(PrimitiveType.Sphere, "MastBeacon_" + i, river, masts[i] + Vector3.up * (h - 1.0f),
                    Vector3.one * 0.3f, glow, false, false);
            }
        }

        static void BuildDiscoveries(Transform env, PrologueDirector d, SaigonAnchors a)
        {
            var g = BuilderUtils.Group("SaigonDiscoveries", env).transform;
            var iron = AssetFactory.Lit("StoryCastIron", new Color(0.10f, 0.13f, 0.12f), 0.25f, 0.6f);
            ProjectionSetPiece.Build(g);
            var pump = BuilderUtils.Group("PublicWaterPump", g).transform;
            pump.localPosition = new Vector3(-6f, 0.12f, -8.8f);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "CastIronColumn", pump, new Vector3(0f, 0.55f, 0f), new Vector3(0.24f, 0.55f, 0.24f), iron, true, false);
            BuilderUtils.Block("Spout", pump, new Vector3(0f, 0.92f, 0.2f), new Vector3(0.1f, 0.12f, 0.42f), iron, true, false);
            BuilderUtils.Block("PumpHandle", pump, new Vector3(0.16f, 1.15f, 0f), new Vector3(0.07f, 0.45f, 0.07f), iron, true, false, new Vector3(0f, 0f, -25f));
            BuilderUtils.Prim(PrimitiveType.Cylinder, "WaterStream", pump, new Vector3(0f, 0.62f, 0.38f), new Vector3(0.015f, 0.28f, 0.015f), AssetFactory.Transparent("StoryWater", new Color(0.55f, 0.71f, 0.78f, 0.65f)), true, false);
            var bucket = BuilderUtils.Prim(PrimitiveType.Cylinder, "WaterBucket", pump, new Vector3(0f, 0.15f, 0.38f), new Vector3(0.32f, 0.15f, 0.32f), iron, true, false);
            CrowdBuilder.AddPose(g, new Vector3(-5.35f, 0.12f, -8.55f), 270f, 813, "WorkMid", role: "CivilianVN");
            // Vùng bấm E của cảnh đèn ôm lấy bóng đèn thật của cột ở chỗ cảnh nhìn (lamps[1], x −13), không phải chân cột: phải nhìn vào đèn.
            // Gốc đặt ngay tại bóng để dấu chỉ dẫn trỏ đúng bóng; hộp rộng hơn chao đèn để tia nhắm chạm vùng này trước vỏ đèn.
            var bulbZone = MarseilleNpcs.Target(g, "ElectricLightDiscovery", a.lamps[1].bulb, new Vector3(1.3f, 1.3f, 1.3f), "prompt.story.look");
            bulbZone.GetComponent<BoxCollider>().center = Vector3.zero;
            d.discoveries = new[] {
                bulbZone,
                MarseilleNpcs.Target(g, "CinemaDiscovery", new Vector3(-9f, 0.12f, -9.1f), new Vector3(3f, 2.5f, 0.5f), "prompt.story.look"),
                MarseilleNpcs.Target(g, "WaterDiscovery", new Vector3(-6f, 0.12f, -8.7f), new Vector3(0.6f, 1.4f, 0.6f), "prompt.story.look") };
            d.discoveryLePoints = new Transform[3];
            var spots = new[] { new Vector3(-11.7f, 0.12f, -7.3f), new Vector3(-7.8f, 0.12f, -7.3f), new Vector3(-4.8f, 0.12f, -7.3f) };
            for (int i = 0; i < spots.Length; i++) d.discoveryLePoints[i] = Anchor(g, "DiscoveryLe_" + i, spots[i], 180f);
            d.iceTarget = MarseilleNpcs.Target(g, "IceCreamCounter", new Vector3(6f, 0.12f, -8.7f), new Vector3(0.8f, 1.4f, 0.5f), "prompt.story.ice");
            var wood = AssetFactory.Lit("StoryIceWood", new Color(0.31f, 0.18f, 0.1f), 0.12f);
            BuilderUtils.Block("IceCounter", g, new Vector3(6f, 0.67f, -9f), new Vector3(1.5f, 1.1f, 0.5f), wood);
            d.iceSeller = CrowdBuilder.Spawn(g, "VendorVN_F", new Vector3(6.8f, .12f, -9.5f), 0f, 814);
            d.iceSeller.Play("Idle");
            d.iceCup = IceCreamProps.BuildCup(g, "BaIceCup", IceEatMotion.CupScale);
            d.leIceCup = IceCreamProps.BuildCup(g, "LeIceCup", 1.2f);
            d.leIceCup.SetParent(d.le.carryPoint, false);
            d.leIceCup.localPosition = Vector3.zero;
            d.leIceCup.gameObject.SetActive(false);
            d.iceCup.gameObject.SetActive(false);
            BuilderUtils.SetRenderingLayer(g.gameObject, WorldRenderingLayers.Exterior);
        }

        /// <summary>Đèn của các cột đèn điện: realtime không bóng (tối đa 2) hoặc bake. Trả về các đèn realtime cho LightBudget.</summary>
        static List<Light> BuildLamps(Transform env, SaigonAnchors a)
        {
            // Thay phần hình của cột đèn phông; giữ collider và điểm neo của model, không sửa prefab nguồn.
            foreach (var renderer in env.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.name.StartsWith("ElectricLamp_", System.StringComparison.Ordinal))
                    Object.DestroyImmediate(renderer);
            var group = BuilderUtils.Group("StreetLights", env).transform;
            var realtime = new List<Light>();
            for (int i = 0; i < a.lamps.Length; i++)
            {
                var fixture = BuildStreetLamp(group, a.lamps[i].bulb, i);
                var l = BuilderUtils.PointLight("StreetLight_" + i, group, a.lamps[i].bulb + Vector3.down * 0.3f, LampColor,
                    a.lamps[i].realtime ? 15f : 18f, a.lamps[i].realtime ? 16f : 18f, a.lamps[i].realtime);
                BuilderUtils.SetLightLayer(l, WorldRenderingLayers.Exterior);
                Lamps.Bind(fixture, l, Lamps.Glow(), a.lamps[i].realtime);
                BrightenBulb(fixture);
                if (a.lamps[i].realtime)
                    realtime.Add(l);
            }
            return realtime;
        }

        /// <summary>Bóng điện phát sáng mạnh (HDR) để bloom của hoàng hôn tạo quầng quanh chụp đèn; cập nhật cả LampGlowSync để bật/tắt đèn không trả về vật liệu cũ.</summary>
        static void BrightenBulb(Transform fixture)
        {
            var bulb = fixture.Find("ElectricBulb");
            if (bulb == null)
                return;
            var renderer = bulb.GetComponent<Renderer>();
            var hot = AssetFactory.Emissive("PrologueBulb", new Color(1f, 0.7f, 0.35f), new Color(14f, 7.2f, 2.0f));
            renderer.sharedMaterial = hot;
            // Quầng mềm quanh chụp đèn (cầu ~1,5 m, sáng nhất ở tâm); bloom chỉ làm to điểm sáng nhỏ ở xa, không đủ thành quầng.
            var halo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            halo.name = "LampHalo";
            Object.DestroyImmediate(halo.GetComponent<Collider>());
            halo.transform.SetParent(fixture, false);
            halo.transform.position = bulb.position;
            halo.transform.localScale = Vector3.one * 1.5f;
            var haloRenderer = halo.GetComponent<MeshRenderer>();
            haloRenderer.sharedMaterial = AssetFactory.LampHalo("PrologueLampHalo", new Color(1.1f, 0.5f, 0.16f), 3f);
            haloRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            haloRenderer.receiveShadows = false;
            haloRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            haloRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            var sync = fixture.GetComponent<LampGlowSync>();
            if (sync == null)
                return;
            for (int k = 0; k < sync.parts.Length; k++)
                if (sync.parts[k].renderer == renderer)
                    sync.parts[k].lit = hot;
            var parts = new List<LampGlowSync.Part>(sync.parts)
            {
                new LampGlowSync.Part { renderer = haloRenderer, lit = haloRenderer.sharedMaterial, unlit = haloRenderer.sharedMaterial, toggleRenderer = true },
            };
            sync.parts = parts.ToArray();
        }

        /// <summary>Cột gang với chân đúc, đai, chụp kính bốn mặt và mái vòm; ánh sáng dùng suất đèn cũ.</summary>
        static Transform BuildStreetLamp(Transform parent, Vector3 bulb, int index)
        {
            var root = BuilderUtils.Group("StreetLamp_" + index, parent).transform;
            root.localPosition = new Vector3(bulb.x, 0.12f, bulb.z);
            var iron = AssetFactory.Lit("StreetLampIron", new Color(0.10f, 0.115f, 0.105f), 0.23f, 0.65f);
            var glass = Lamps.GlassWarm();
            float center = bulb.y - 0.12f;
            BuilderUtils.Prim(PrimitiveType.Cylinder, "CastBase", root, new Vector3(0f, 0.08f, 0f), new Vector3(0.38f, 0.08f, 0.38f), iron, true, false);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Pedestal", root, new Vector3(0f, 0.38f, 0f), new Vector3(0.23f, 0.3f, 0.23f), iron, true, false);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Column", root, new Vector3(0f, (center - 0.31f + 0.5f) * 0.5f, 0f),
                new Vector3(0.10f, (center - 0.31f - 0.5f) * 0.5f, 0.10f), iron, true, false);
            foreach (float y in new[] { 0.65f, 1.0f, center - 0.4f })
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Collar", root, new Vector3(0f, y, 0f), new Vector3(0.15f, 0.035f, 0.15f), iron, true, false);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "LanternBase", root, new Vector3(0f, center - 0.32f, 0f), new Vector3(0.44f, 0.04f, 0.44f), iron, true, false);
            foreach (float side in new[] { -1f, 1f })
            {
                BuilderUtils.Block("Glass", root, new Vector3(side * 0.18f, center, 0f), new Vector3(0.004f, 0.58f, 0.34f), glass, true, false);
                BuilderUtils.Block("Glass", root, new Vector3(0f, center, side * 0.18f), new Vector3(0.34f, 0.58f, 0.004f), glass, true, false);
                foreach (float z in new[] { -0.18f, 0.18f })
                    BuilderUtils.Block("LanternFrame", root, new Vector3(side * 0.18f, center, z), new Vector3(0.028f, 0.64f, 0.028f), iron, true, false);
            }
            BuilderUtils.Prim(PrimitiveType.Sphere, "LanternRoof", root, new Vector3(0f, center + 0.34f, 0f), new Vector3(0.52f, 0.18f, 0.52f), iron, true, false);
            BuilderUtils.Prim(PrimitiveType.Sphere, "Finial", root, new Vector3(0f, center + 0.47f, 0f), new Vector3(0.065f, 0.16f, 0.065f), iron, true, false);
            // Bóng điện ở giữa chụp; không để lõi tự sinh bám vào tấm kính mỏng bên hông.
            BuilderUtils.Prim(PrimitiveType.Sphere, "ElectricBulb", root, new Vector3(0f, center, 0f), new Vector3(0.085f, 0.12f, 0.085f), Lamps.Flame(), true, false);
            BuilderUtils.SetRenderingLayer(root.gameObject, WorldRenderingLayers.Exterior);
            return root;
        }

        /// <summary>Điểm neo theo tên layout.json, để builder khác, test và ảnh chụp tìm theo tên.</summary>
        static Transform BuildAnchors(Transform env, SaigonAnchors a)
        {
            var g = BuilderUtils.Group("Anchors", env).transform;
            Anchor(g, "player_spawn", a.playerSpawn, a.playerYaw);
            Anchor(g, "le_walk_start", a.leStart, a.playerYaw);
            Anchor(g, "conversation_ba", a.conversationBa, a.baYaw);
            Anchor(g, "conversation_le", a.conversationLe, a.leYaw);
            Anchor(g, "electric_light_bulb", a.lamps[1].bulb, 0f);
            Anchor(g, "cafe_seat", a.cafeSeat, 0f);
            for (int i = 1; i < a.lePath.Length; i++)
                Anchor(g, "LePath_" + i, a.lePath[i], 0f);
            // Các điểm neo còn lại của layout.json (cafe_door, hands_pose, screen_facade...) cho ảnh chụp và test tìm theo tên.
            foreach (var pair in SaigonStreet.Layout.Anchors)
                if (g.Find(pair.Key) == null)
                    Anchor(g, pair.Key, pair.Value, 0f);
            return g;
        }

        static Transform Anchor(Transform parent, string name, Vector3 pos, float yaw)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return t;
        }

        static NpcAgent BuildLe(Transform env, SaigonAnchors a)
        {
            var group = BuilderUtils.Group("Npcs_Story", env).transform;
            var le = CrowdBuilder.Spawn(group, LeRole(), a.leStart, a.playerYaw, 77);
            le.gameObject.name = "Le";
            le.walkSpeed = 1.7f;
            return le;
        }

        static Transform BuildCrowd(Transform env, SaigonAnchors a)
        {
            var crowd = CrowdBuilder.Root(env, "Crowd");
            // Người qua lại: vỉa hè bắc (3), đầu đông vỉa hè nam (2), một người băng đường; đi lệch pha nhau. Bớt ở tầng Laptop.
            for (int i = 0; i < 3; i++)
                CrowdBuilder.AddWander(crowd, "North" + i, Rotate(a.northWalk, i), 1, 900 + i * 10, optional: i == 2, role: CrowdRoles[i % CrowdRoles.Length]);
            for (int i = 0; i < 2; i++)
                CrowdBuilder.AddWander(crowd, "South" + i, Rotate(a.southWalk, i), 1, 950 + i * 10, optional: i == 1, role: CrowdRoles[(i + 1) % CrowdRoles.Length]);
            CrowdBuilder.AddWander(crowd, "Road", a.roadWalk, 1, 980, role: CrowdRoles[2]);
            // Nhóm ba người đứng nói chuyện trên vỉa hè bắc, một người đứng chờ.
            CrowdBuilder.AddGroup(crowd, "Talk", a.talkGroups[0], 3, 0.8f, 20f, 301, role: CrowdRoles[0] + "," + CrowdRoles[1]);
            foreach (var p in a.standers)
                CrowdBuilder.AddStand(crowd, p, 90f, 410, optional: true, role: CrowdRoles[3]);
            // Quán cà phê: bồi bàn đứng ở cửa, khách ngồi bàn (uống cà phê, đọc báo) quay vào bàn.
            CrowdBuilder.AddStand(crowd, new Vector3(10.4f, 0.12f, -9.2f), 0f, 420, role: "Waiter");
            for (int i = 0; i < a.cafeGuests.Length; i++)
            {
                var g = a.cafeGuests[i];
                if (i == 1)
                    CrowdBuilder.AddSit(crowd, g.pos, g.yaw, 500 + i, read: true, role: "PassengerFR_M1");
                else
                    CrowdBuilder.AddPose(crowd, g.pos, g.yaw, 500 + i, "Drink", "SitChair", i == 0 ? "PassengerFR_M1" : "PassengerFR_F");
            }
            CrowdBuilder.AddLod(crowd);
            return crowd;
        }

        static Vector3[] Rotate(Vector3[] pts, int shift)
        {
            var r = new Vector3[pts.Length];
            for (int i = 0; i < pts.Length; i++)
                r[i] = pts[(i + shift) % pts.Length];
            return r;
        }
    }
}
