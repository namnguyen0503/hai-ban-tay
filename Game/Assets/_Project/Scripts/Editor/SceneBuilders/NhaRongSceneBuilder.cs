using HaiBanTay.Core;
using HaiBanTay.Audio;
using HaiBanTay.Editor.SceneBuilders.Ship;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Interaction;
using HaiBanTay.Narrative;
using HaiBanTay.Shift;
using HaiBanTay.Ship;
using HaiBanTay.Npc;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>
    /// 01_NhaRong (M10c): tàu Amiral Latouche-Tréville đủ cả con neo ở Bến Nhà Rồng, mạn phải (−Z) áp kè. Bến là model thật
    /// (Art/Models/Env/NhaRong + layout.json, đặt theo tọa độ thế giới): kè, kho, Nhà Rồng, hàng me, cọc buộc dây, bờ đối diện;
    /// biên vùng đi bằng vật thật (hàng rào, kho, xe) có collider của model, không có tường vô hình. Người chơi bắt đầu trên kè
    /// cạnh xe khoai, vác bao lên cầu thang tàu, qua boong, xuống hầm thực phẩm (SackStack), rồi tàu nhổ neo: còi, cầu thang tàu
    /// thu lên, tháo dây, phông bờ cùng đám đông lùi dần (BackdropMover), thế giới bắt đầu lắc nhẹ. Vào bếp (hoặc hết giờ) thì sang
    /// cảnh 02. Đám đông ~50 người dựng bởi <see cref="NhaRongCrowd"/>.
    /// </summary>
    public static class NhaRongSceneBuilder
    {
        public const string SceneName = "01_NhaRong";
        /// <summary>Dải lan can mạn phải boong giếng trước (hệ gốc tàu): đứng nhìn bờ đủ lâu thì đặt cờ s1.lingered.</summary>
        static readonly Bounds RailingZone = new Bounds(new Vector3(17.4f, 6.6f, -6.8f), new Vector3(24.8f, 2f, 1.2f));
        /// <summary>Cả con tàu từ hầm thực phẩm trở lên (hệ gốc tàu), trừ cầu thang tàu ngoài mép mạn.</summary>
        static readonly Bounds AboardZone = new Bounds(new Vector3(0f, 9.5f, 0f), new Vector3(124f, 13f, 14.7f));
        /// <summary>Hướng nhìn lúc xuất hiện (độ): về phía bao khoai cạnh xe và cầu thang tàu; quay đủ khỏi hộp quầy vé để khung hai bàn tay B1.2 không bị che.</summary>
        const float SpawnYaw = 55f;
        /// <summary>Ba bao khoai nằm dọc mép bắc xe kéo (thế giới, mặt kè y = −4,1); thân xe x 3,4…7,3, z −21,1…−18,9.</summary>
        static readonly Vector3[] SackSpots =
        {
            new Vector3(4.5f, -4.1f, -18.45f), new Vector3(5.4f, -4.1f, -18.45f), new Vector3(6.3f, -4.1f, -18.45f),
        };
        /// <summary>Bao khoai ở sàn hầm thực phẩm (hệ gốc tàu), cùng chỗ với bao của cảnh 02 (anchor hold_potato_sack_i).</summary>
        static readonly Vector3[] HoldSpots =
        {
            new Vector3(13.9f, 2.8f, 1.7f), new Vector3(14f, 2.8f, 2.35f), new Vector3(13.3f, 2.8f, 2.1f),
        };

        public static void Build()
        {
            var scene = BuilderUtils.NewScene();
            var profile = AssetFactory.LightingProfile("NhaRong");
            var cam = BuilderUtils.AddMainCamera(profile.background);
            // Mặt trời của hồ sơ ánh sáng bỏ đi: mặt trời thật là của WorldRoot.
            var profileSun = BuilderUtils.ApplyLighting(profile);
            if (profileSun != null)
                Object.DestroyImmediate(profileSun.gameObject);
            BuilderUtils.AddPostVolume();

            var cfg = ShipConfig.NhaRong;
            var envLayout = EnvLayout.Load("NhaRong");
            var env = BuilderUtils.Group("Environment").transform;
            var layout = ShipBuilder.Build(env, cfg);
            layout.galleyDoor.audioEnabled = true;
            layout.corridorDoor.audioEnabled = true;
            ShipInterior.HideEveningCorner(layout);
            GalleySceneBuilder.BuildCrewDiningRoom(layout.galley, new ShipInterior.Mats());
            var backdrop = BuildBackdrop(layout.world, envLayout, out var ropes);
            // Đèn bến (QuayLamp_*) của model phông: mỗi cột một đèn không bóng, LightBudget chỉ bật hai đèn gần camera nhất.
            var quayLamps = Lamps.LightEnvLamps(backdrop.transform, "QuayLamp_", new Color(1f, 0.78f, 0.5f), 3.2f, 9f);
            ShipBuilder.ConfigureLights(layout, quayLamps);
            ShipBuilder.FinishWorld(layout);

            // Đám đông: gốc phông bờ và gốc tàu đều đặt ở gốc thế giới để tọa độ cục bộ = tọa độ thế giới của layout.json.
            var crowd = NhaRongCrowd.Build(backdrop.transform, layout.transform, envLayout);
            crowd.quay.position = Vector3.zero;
            crowd.deck.position = Vector3.zero;
            BuilderUtils.SetRenderingLayer(crowd.deck.gameObject, WorldRenderingLayers.Exterior);
            BuilderUtils.SetRenderingLayer(crowd.quay.gameObject, WorldRenderingLayers.Exterior);

            var sacks = BuildSacks(backdrop.transform, layout, out var stack);
            BuilderUtils.SetRenderingLayer(backdrop.transform.Find("Sacks").gameObject, WorldRenderingLayers.Exterior | WorldRenderingLayers.Interior);
            BuildStairsBalance(layout);
            var anchors = BuildAnchors(env, envLayout);

            var railing = BuilderUtils.Trigger<BeatTrigger>("RailingZone", layout.transform, RailingZone.center, RailingZone.size);
            railing.once = false;
            var aboard = BuilderUtils.Trigger<PlayerArea>("AboardArea", layout.transform, AboardZone.center, AboardZone.size);
            var ambience = new GameObject("ShipAmbience").AddComponent<ShipAmbience>();
            ambience.aboard = aboard;
            ambience.quayKey = "Ambience/nharong_quay";
            var engine = new GameObject("EngineHum").AddComponent<EngineHum>();
            engine.playOnStart = false;
            engine.volume = 0.2f;

            var director = new GameObject("NhaRongDirector").AddComponent<NhaRongDirector>();
            director.ship = layout.motion;
            director.backdrop = backdrop;
            director.railing = railing;
            director.aboard = aboard;
            director.ambience = ambience;
            director.engine = engine;
            director.galleyZone = layout.galleyZone;
            director.gangway = layout.gangway.gameObject;
            director.gangwayBarrier = layout.gangwayBarrier;
            director.sacks = sacks;
            director.stack = stack;
            director.ropes = ropes;
            director.quayCrowd = crowd.quay;
            director.mai = crowd.mai;
            director.purser = crowd.purser;
            director.chef = crowd.chef;
            // Nhân vật ông Mai của đám đông trở thành người dẫn đường ở B0.
            var routine = director.mai.GetComponent<CrowdRoutine>();
            if (routine != null) Object.DestroyImmediate(routine);
            director.mai.transform.SetParent(env, true);
            director.mai.transform.position = new Vector3(-1.5f, 0f, -5.1f);
            director.mai.walkSpeed = 1.1f;
            director.mai.avoidOthers = true;
            director.mai.yieldToPlayer = true;
            director.jobStaff = new[] {
                CrowdBuilder.Spawn(env, "Helper", new Vector3(-3f, 0f, -5.2f), 120f, 851),
                CrowdBuilder.Spawn(env, "Mai", new Vector3(-3.8f, 0f, -5.2f), 120f, 852) };
            foreach (var actor in director.jobStaff) actor.gameObject.AddComponent<StaffAside>();
            director.jobStaffTarget = MarseilleNpcs.Target(env, "JobStaffTarget", new Vector3(-2.7f, 0f, -5.3f), new Vector3(1.5f, 1.8f, 1f), "prompt.job.ask");
            director.jobCaptain = CrowdBuilder.Spawn(env, "Captain", new Vector3(-5.5f, 0f, -4.8f), 0f, 853);
            director.jobCaptainTarget = MarseilleNpcs.Target(director.jobCaptain.transform, "JobCaptainTarget", Vector3.zero, new Vector3(1f, 1.8f, 1f), "prompt.job.captain");
            director.jobMaiPath = new[] {
                // Vòng theo mạn phải, tránh tời cáp nằm giữa nhóm nhân viên và chủ tàu.
                CrowdBuilder.Waypoint(env, "JobMaiPath_0", new Vector3(-1.5f, 0f, -6.1f)),
                CrowdBuilder.Waypoint(env, "JobMaiPath_1", new Vector3(-4.1f, 0f, -6.1f)) };
            director.jobSpawn = CrowdBuilder.Waypoint(env, "JobReturnToQuay", envLayout.PlayerSpawn, SpawnYaw);
            BuilderUtils.SetRenderingLayer(director.jobCaptain.gameObject, WorldRenderingLayers.Exterior);
            foreach (var actor in director.jobStaff) BuilderUtils.SetRenderingLayer(actor.gameObject, WorldRenderingLayers.Exterior);
            director.boardPoint = anchors.Find("legacy_deck_spawn");
            director.gangwayFootPoint = anchors.Find("gangway_foot");
            director.gangwayTopPoint = anchors.Find("gangway_top");

            var probes = BuilderUtils.Group("Probes", env).transform;
            BuilderUtils.BakedReflectionProbe("Reflection_Deck", probes, new Vector3(0f, 2f, 0f), new Vector3(40f, 12f, 30f), false);

            PlayerBuilder.Spawn(cam, envLayout.PlayerSpawn, SpawnYaw);
            BuilderUtils.Save(scene, SceneName);
        }

        /// <summary>
        /// Phông bờ: model Env_NhaRong đặt đúng chỗ theo layout (không dịch), cùng đám đông và bao khoai là con của một gốc để
        /// BackdropMover dời cả bến khi nhổ neo. Mặt nước do WorldRoot lo nên bỏ tấm "River" của model (trùng độ cao với biển
        /// của thế giới). Dây buộc tàu (Mooring_0..3) trả về để tháo khi nhổ neo.
        /// </summary>
        static BackdropMover BuildBackdrop(Transform world, EnvLayout layout, out GameObject[] ropes)
        {
            var root = BuilderUtils.Group("Backdrop_NhaRong", world);
            var instance = layout.Place(root.transform);
            KeppelPortBuilder.ReplaceDistantSteamers(instance);
            NhaRongRoofBuilder.Enhance(instance);
            foreach (var part in instance.GetComponentsInChildren<Transform>(true))
                if (part.name == "PotatoCart")
                    part.gameObject.AddComponent<MovingCartAudio>().motionFrame = root.transform;
            var river = instance.transform.Find("NhaRong/River");
            if (river == null)
                river = FindDeep(instance.transform, "River");
            if (river != null)
                river.gameObject.SetActive(false);
            var list = new System.Collections.Generic.List<GameObject>();
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Mooring_") && !t.name.EndsWith("_coil"))
                    list.Add(t.gameObject);
            ropes = list.ToArray();
            // Phông xa: không probe, không đổ bóng (chỉ ambient và mặt trời).
            BuilderUtils.MakeDistantBackdrop(instance);

            // Vài thùng chờ bốc ở góc đông kè (ArtSlot thay được bằng art tay).
            var wood = AssetFactory.Lit("Wood", new Color(0.4f, 0.27f, 0.16f), 0.15f);
            Props.Dress("PortBarrels", root.transform, new Vector3(28f, -4.1f, -26f), 15f, new Vector3(4.4f, 0.96f, 3.3f), wood);

            var mover = root.AddComponent<BackdropMover>();
            mover.offset = new Vector3(-90f, 0f, -70f);
            mover.seconds = 50f;
            return mover;
        }

        static Transform FindDeep(Transform t, string name)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true))
                if (c.name == name)
                    return c;
            return null;
        }

        /// <summary>
        /// Ba bao khoai cạnh xe kéo (con của phông bờ, có ReturnToStart: rơi xuống sông thì về chỗ cũ) và chỗ xếp bao ở hầm thực
        /// phẩm (SackStack, đặt cùng chỗ với bao khoai của cảnh 02).
        /// </summary>
        static Holdable[] BuildSacks(Transform quay, ShipLayout layout, out SackStack stack)
        {
            // Vùng giữ bao: cả kè và con tàu; thấp hơn mặt sông 0,7 m thì coi là rơi mất.
            var area = new Bounds(new Vector3(-15f, -2f, -20f), new Vector3(170f, 24f, 80f));
            var group = BuilderUtils.Group("Sacks", quay).transform;
            var sacks = new Holdable[SackSpots.Length];
            for (int i = 0; i < sacks.Length; i++)
            {
                sacks[i] = Props.PotatoSack(group, SackSpots[i], area);
                sacks[i].gameObject.name = "PotatoSack_" + i;
                sacks[i].GetComponent<ReturnToStart>().killY = -6.3f;
            }
            var hold = layout.foodHold;
            stack = BuilderUtils.Trigger<SackStack>("SackStack", hold, new Vector3(13.7f, 3.5f, 2.1f), new Vector3(2.4f, 1.4f, 2.0f));
            stack.gameObject.layer = BuilderUtils.Layer("Interactable");
            stack.anchors = new Transform[HoldSpots.Length];
            for (int i = 0; i < HoldSpots.Length; i++)
            {
                var a = new GameObject("SackSlot_" + i).transform;
                a.SetParent(stack.transform, false);
                a.position = hold.TransformPoint(HoldSpots[i]) + Vector3.up * 0.3f;
                a.rotation = Quaternion.Euler(0f, 20f * i, 0f);
                stack.anchors[i] = a;
            }
            return sacks;
        }

        static void BuildStairsBalance(ShipLayout layout)
        {
            var balance = BuilderUtils.Group("StairsBalance", layout.foodHold).AddComponent<StairsBalance>();
            balance.stairs = layout.holdStairs;
            balance.slipSpot = layout.holdSlipSpot;
        }

        /// <summary>Điểm neo theo tên trong layout.json (Transform thế giới) để director, test và ảnh chụp tìm theo tên.</summary>
        static Transform BuildAnchors(Transform env, EnvLayout layout)
        {
            var group = BuilderUtils.Group("Anchors", env).transform;
            foreach (var pair in layout.Anchors)
            {
                var t = new GameObject(pair.Key).transform;
                t.SetParent(group, false);
                t.position = pair.Value;
            }
            return group;
        }
    }
}
