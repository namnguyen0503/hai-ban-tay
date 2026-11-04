using System.Collections.Generic;
using System.Linq;
using HaiBanTay.Audio;
using HaiBanTay.Core;
using HaiBanTay.Editor.SceneBuilders.Ship;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Interaction;
using HaiBanTay.Narrative;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Ship;
using HaiBanTay.Tasks;
using UnityEngine;
using Mats = HaiBanTay.Editor.SceneBuilders.Ship.ShipInterior.Mats;
using static HaiBanTay.Editor.SceneBuilders.Ship.ShipInterior;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>
    /// 02_Galley: một ngày trên tàu Amiral Latouche-Tréville đang chạy trên biển (M10b). Cả con tàu là model thật; khối bếp
    /// đặt ở gốc thế giới (tọa độ trạm việc, lộ trình NPC và test cũ giữ nguyên), mũi tàu hướng −X. Bếp 10 × 6 m: hai lò
    /// gang tường bắc, thùng than, đồng hồ, bảng phấn, ô chuyển món; bàn sơ chế giữa phòng; cửa nam xuống hầm than (máng tro);
    /// tường đông: phòng rửa và hành lang (biển tên tàu) ra mạn phải; tường tây: ô cửa sổ tròn và cửa bếp ra boong giếng
    /// trước, từ đó xuống 14 bậc tới hầm thực phẩm (bao khoai, sọt rau). Đi tự do trong ca, đồng hồ vẫn chạy.
    /// Thế giới (biển, trời) lắc ngược theo ShipMotion, giờ trời theo đồng hồ ca. Việc thêm ở GalleyChores.cs.
    /// M11: sáu ngày cùng một cảnh; khác nhau theo ngày do runtime đọc days.vi.json: NPC của ngày (DayCast, GalleyNpcs.cs),
    /// kéo rổ rau bằng dây sắt và sóng phủ boong (HaulStation, GalleyChores.cs), phông cảng Singapore, đám đông trên boong,
    /// dây cứu sinh ngày bão (DayScenery), học buổi tối (StudyTask).
    /// </summary>
    public static partial class GalleySceneBuilder
    {
        public const string SceneName = "02_Galley";
        /// <summary>Bao khoai ở sàn hầm (thế giới y ≈ −2,5): ngưỡng rơi thấp hơn sàn hầm.</summary>
        const float SackKillY = -4.5f;

        public static void Build()
        {
            var scene = BuilderUtils.NewScene();
            var profile = AssetFactory.LightingProfile("Galley");
            var cam = BuilderUtils.AddMainCamera(profile.background);
            // Mặt trời của hồ sơ ánh sáng bỏ đi: mặt trời/trăng thật là của WorldRoot.
            var profileSun = BuilderUtils.ApplyLighting(profile);
            if (profileSun != null)
                Object.DestroyImmediate(profileSun.gameObject);
            BuilderUtils.AddPostVolume();

            var m = new Mats();
            var environment = BuilderUtils.Group("Environment").transform;
            var layout = ShipBuilder.Build(environment, ShipConfig.Galley);
            var env = layout.galley;
            var ship = layout.motion;
            var storm = ship.gameObject.AddComponent<StormController>();
            storm.ship = ship;
            var engine = new GameObject("EngineHum").AddComponent<EngineHum>();
            engine.volume = 0.2f;
            layout.galleyDoor.audioEnabled = true;
            layout.corridorDoor.audioEnabled = true;

            var tasks = BuilderUtils.Group("Tasks", env).transform;
            var coal = BuildCoal(env, tasks, m);
            BuildCoalBaskets(env, coal, layout.coalHold, m);
            var sacks = BuildSacks(env, layout.foodHold, tasks, layout.coldZone, m);
            var wash = BuildWash(env, tasks, m);
            var sweep = BuildSweep(env, tasks, m);
            var greens = BuildGreens(env, layout.foodHold, tasks, m);
            var pan = BuildPan(env, tasks, m);
            BuildStairsBalance(layout);
            var npcs = BuildNpcs(env, sacks, sacks.tubPotatoes[0].parent.parent, coal, m);
            coal.helper = npcs.GetComponentInChildren<HelperRoutine>();
            var pots = BuildPots(env, tasks, m, coal);
            var ash = BuildAsh(env, tasks, m, coal, layout.coalHold);
            var scrub = BuildTallPot(env, tasks, m);
            pan.longBrushHook = scrub.longBrushHook;
            var meals = BuildMealPots(env, pots, wash, scrub, pan, npcs, coal, m);
            var haul = BuildHaul(env, layout.foodHold, tasks, m);
            var study = new GameObject("StudyTask").AddComponent<StudyTask>();
            study.transform.SetParent(tasks, false);
            study.doneEvent = EventIds.S2WordsDone;
            study.chair = layout.restSeat.transform;
            study.chairClip = System.Array.Find(UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Characters/Anims/SitChair.fbx"), a => a is AnimationClip && !a.name.StartsWith("__preview")) as AnimationClip;
            if (study.chairClip == null) throw new System.InvalidOperationException("Thiếu clip ngồi ghế SitChair.");
            study.studyLookAt = Beacon(env, "StudyLookAt", new Vector3(-4.25f, 1.06f, 1.25f));
            var cast = BuildCast(env, layout, greens, scrub, pots, m);
            var lifeline = BuildStormRigging(environment, m);
            var crowd = BuildDeckCrowd(environment);
            var port = BuildPortSingapore(layout.world);
            var clock = BuildWallClock(env, m);
            var board = BuildChalkboard(env, m);

            // Đèn treo: đèn chính realtime không bóng (lắc theo tàu), đèn gần lò chỉ phát sáng (bake).
            var lampMain = Props.HangingLamp(env, new Vector3(0f, 2.6f, 0f), realtime: true, intensity: 4.5f);
            lampMain.name = "Lamp_Main";
            var lampStove = Props.HangingLamp(env, new Vector3(-2.3f, 2.6f, 1.3f), realtime: false, intensity: 10f);
            lampStove.name = "Lamp_Stove";
            var dayLamp = lampMain.GetComponentInChildren<Light>();
            dayLamp.color = new Color(1f, .9f, .78f);
            lampStove.GetComponentInChildren<Light>().color = new Color(1f, .9f, .78f);

            var director = new GameObject("ShiftDirector").AddComponent<ShiftDirector>();
            director.ship = ship;
            director.storm = storm;
            director.coal = coal;
            director.sacks = sacks;
            director.wash = wash;
            director.meals = meals;
            director.ash = ash;
            director.sweep = sweep;
            director.greens = greens;
            director.pan = pan;
            director.pots = pots;
            director.tall = scrub;
            director.haul = haul;
            director.study = study;
            director.cast = cast;
            director.storyStations = BuildVoyageStory(env, tasks, director, m, layout);
            // Ấm nước sôi (đích giao nước, ngày không có chảo) đứng mép trước ô chảo: chảo Ø 0,79 đè lên nếu không dẹp đi (QA-0148).
            var kettle = env.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "BoilingWaterPot");
            if (kettle != null)
                pan.clearWhileOnRange = new[] { kettle.gameObject };
            director.scenery = BuildScenery(environment, layout, port, crowd, lifeline, engine);
            var ambience = new GameObject("ShipAmbience").AddComponent<ShipAmbience>();
            ambience.galley = layout.galleyZone;
            ambience.hold = layout.holdZone;
            ambience.scenery = director.scenery;
            director.galleyBeacon = Beacon(env, "Beacon_Galley", new Vector3(0f, 0f, 0f));
            director.galleyDoorBeacon = Beacon(env, "Beacon_GalleyDoor", new Vector3(-5.06f, 0f, 2.6f));
            director.companionBeacon = Beacon(env, "Beacon_Companion", new Vector3(-6.0f, 0f, -0.85f));
            director.holdBeacon = Beacon(env, "Beacon_Hold", new Vector3(-12.0f, -2.8f, -1.5f));
            director.restSeat = layout.restSeat;
            director.galleyZone = layout.galleyZone;
            director.holdZone = layout.holdZone;
            director.wallClock = clock;
            director.board = board;
            director.eveningSpawn = layout.eveningSpawn;
            director.dayOnly = new[] { npcs };
            director.eveningOnly = layout.eveningObjects;
            director.colomboLights = BuildColomboLights(env);
            // Tiếng hò khuân vác bến Colombo vọng qua mặt nước, chỉ khi đèn bến bật (tối ngày 3) và Ba ở ngoài boong (DIRECTOR §6, §9).
            ambience.farShore = director.colomboLights;

            var time = layout.world.GetComponent<TimeOfDay>();
            time.followShiftClock = true;
            time.shift = director;
            layout.GetComponentInChildren<GalleySmoke>().fire = coal;
            // Lửa lò và đèn treo (ban ngày) hoặc đèn dầu (buổi tối): ShiftDirector đổi đèn thứ hai qua LightBudget.
            var mastLamp = Props.HangingLamp(environment, new Vector3(-21.6f, 2.65f, -1.45f), true,
                intensity: 4.5f, range: 14f, lightColor: new Color(1f, 0.88f, 0.7f));
            mastLamp.name = "Lamp_StormMast";
            var deckLamp = Props.HangingLamp(environment, new Vector3(-14f, 2.65f, 5.65f), true,
                intensity: 4.5f, range: 14f, lightColor: new Color(1f, 0.88f, 0.7f));
            deckLamp.name = "Lamp_StormWalkway";
            // Đèn bão ngoài trời là đèn treo: phải có chỗ treo, không thì dây đèn lơ lửng giữa trời (người dùng 04/11, trailer). Đèn cột
            // buồm treo vào cần sắt chìa ra từ cột buồm (cổ cột ở x −22,0); đèn lối đi treo vào cột đèn dựng sát mạn.
            LampBracket(environment, mastLamp.transform.localPosition, new Vector3(-0.6f, 0f, -0.05f), "MastLampArm");
            LampBracket(environment, deckLamp.transform.localPosition, new Vector3(0f, 0f, 0.4f), "WalkwayLampPost");
            foreach (var lamp in new[] { mastLamp, deckLamp })
            {
                BuilderUtils.SetRenderingLayer(lamp.gameObject, WorldRenderingLayers.Exterior);
                BuilderUtils.SetLightLayer(lamp.GetComponentInChildren<Light>(), WorldRenderingLayers.Exterior);
            }
            director.lights = ShipBuilder.ConfigureLights(layout,
                new[] { mastLamp.GetComponentInChildren<Light>(), deckLamp.GetComponentInChildren<Light>() }, coal.fireLight, dayLamp);
            director.dayLamp = dayLamp;
            director.eveningLamp = layout.oilLamp;
            ShipBuilder.FinishWorld(layout);

            PlayerBuilder.Spawn(cam, new Vector3(0f, 0f, -1.2f), 0f);
            // NavMesh cho nhân vật quan trọng (W5.2): bếp, hầm, hành lang và boong giếng quanh bếp.
            var area = new Bounds(env.position, Vector3.zero);
            foreach (var r in env.GetComponentsInChildren<Renderer>()) area.Encapsulate(r.bounds);
            area.Expand(new Vector3(16f, 4f, 8f));
            NavMeshBake.Bake(SceneName, area);
            BuilderUtils.Save(scene, SceneName);
        }

        static Transform Beacon(Transform env, string name, Vector3 position)
        {
            var t = new GameObject(name).transform;
            t.SetParent(env, false);
            t.localPosition = position;
            return t;
        }

        /// <summary>Khung cảnh theo ngày: phông cảng, đám đông boong, dây cứu sinh, nước cảng, máy êm, dây chắn cầu thang tàu.</summary>
        static DayScenery BuildScenery(Transform environment, ShipLayout layout, GameObject port, GameObject crowd, GameObject lifeline,
            EngineHum engine)
        {
            var scenery = BuilderUtils.Group("DayScenery", environment).AddComponent<DayScenery>();
            scenery.ports = new[] { new DayScenery.Port { id = "singapore", root = port } };
            scenery.deckCrowd = crowd;
            scenery.stormRigging = lifeline;
            var time = layout.world.GetComponent<TimeOfDay>();
            scenery.ocean = time != null ? time.ocean : null;
            scenery.harborWater = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(WorldBuilder.MaterialFolder + "/Ocean_Harbor.mat");
            scenery.engine = engine;
            scenery.gangwayBarrier = layout.gangwayBarrier;
            port.SetActive(false);
            return scenery;
        }

        /// <summary>
        /// Cảng Keppel, Singapore (F02: tàu tới ngày 8/6/1911) nhìn từ boong mạn phải (+Z thế giới): kè đá dài, dãy kho mái
        /// dốc, cần cẩu, hai tàu khác neo, đồi xanh phía sau. Khối thấp đa giác, phông xa (không bake, không đổ bóng), dưới
        /// WorldRoot. Không lên bờ được: tàu đỗ ngoài kè, dây chắn ở cầu thang tàu.
        /// </summary>
        static GameObject BuildPortSingapore(Transform world) => KeppelPortBuilder.Build(world);

        static CoalStation BuildCoal(Transform env, Transform tasks, Mats m)
        {
            var task = new GameObject("CoalStation").AddComponent<CoalStation>();
            task.transform.SetParent(tasks, false);

            var stove = BuilderUtils.Group("ArtSlot_Stove_A", env);
            stove.transform.localPosition = new Vector3(-3f, 0f, 2.45f);
            BuilderUtils.Block("StoveWallShield", env, new Vector3(-2.2f, 1.25f, 2.985f),
                new Vector3(2.8f, 1.5f, 0.02f), AssetFactory.RangeIron(), true, false);
            var body = BuilderUtils.Group("CastRange", stove.transform).transform;
            body.localPosition = new Vector3(0.8f, 0f, 0f);
            body.localRotation = Quaternion.Euler(0f, 180f, 0f);
            // Ba ô bếp, mỗi ô một buồng đốt dưới lưới (thứ tự từ cửa gần tường tây): mỗi ô có cửa nạp than riêng.
            var lids = new[] { 0.8f, 0f, -0.8f };
            var flickers = FireBuilder.CastRange(body, 2.8f, 1f, lids);
            task.flicker = flickers[0];
            BindRangeCells(task, body, 0);
            task.fireGlow = body.Find("Ember").GetComponent<Renderer>();
            task.fireLight = BuilderUtils.PointLight("FireLight", stove.transform, new Vector3(0f, 0.58f, -0.25f),
                new Color(1f, 0.5f, 0.2f), 1.2f, 5f, realtime: true, shadows: true);
            foreach (var f in flickers) f.lightIntensity = task.maxFireIntensity;
            // CoalStation cấp đúng một đèn realtime cho ô nóng; flicker chỉ điều khiển phần nhìn thấy của từng ô.
            var heat = new GameObject("StoveHeat").AddComponent<HeatSource>();
            heat.transform.SetParent(stove.transform, false);
            heat.transform.localPosition = new Vector3(0f, 0.6f, -0.6f);
            heat.radius = 3.2f;
            heat.strength = 0.95f;
            task.heat = heat;

            var bin = BuilderUtils.ArtSlot("CoalBin", env, new Vector3(0.3f, 0f, 2.5f), Vector3.zero, t =>
            {
                BuilderUtils.Block("Box", t, new Vector3(0f, 0.3f, 0f), new Vector3(0.9f, 0.6f, 0.7f), m.Wood);
                CoalPile(t, new Vector3(0f, 0.56f, 0f), 0.38f, 0.28f, 0.16f, m.Coal);
                return t.gameObject;
            });
            task.bin = AddTarget(bin, task, "bin");
            // Ngọn than trong thùng: thấp dần theo số xẻng còn lại, ẩn khi cạn (CoalStation.ApplyPile).
            var pile = bin.transform.Find("Pile");
            BuilderUtils.ClearStatic(pile.gameObject);
            task.binPile = pile;
            task.hook = BuildShovelHook(env, m);
            return task;
        }

        /// <summary>Ngọn than chất trong thùng: ụ gồ ghề và cục than trên mặt ụ. Mọi renderer tên "Pile" (bỏ qua khi khớp art).</summary>
        static void CoalPile(Transform parent, Vector3 position, float rx, float rz, float height, Material coal)
        {
            var pile = BuilderUtils.Group("Pile", parent);
            pile.transform.localPosition = position;
            var mound = BuilderUtils.Group("Pile", pile.transform);
            mound.transform.localScale = new Vector3(rx, height, rz);
            mound.AddComponent<MeshFilter>().sharedMesh = AssetFactory.CoalMoundMesh();
            mound.AddComponent<MeshRenderer>().sharedMaterial = coal;
            var mesh = AssetFactory.CoalLumpMesh();
            var rng = new System.Random(57);
            for (int i = 0; i < 26; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt((float)rng.NextDouble()) * 0.95f;
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                float chunk = 0.045f + 0.06f * (float)rng.NextDouble();
                var lump = BuilderUtils.Prim(PrimitiveType.Cube, "Pile", pile.transform,
                    new Vector3(x * rx, AssetFactory.CoalMoundHeight(x, z) * height + chunk * 0.1f, z * rz),
                    new Vector3(chunk, chunk * 0.7f, chunk * 0.9f), coal, false, false,
                    new Vector3((float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f));
                lump.GetComponent<MeshFilter>().sharedMesh = mesh;
            }
        }

        /// <summary>Móc treo xẻng trên tường bắc, cạnh thùng than.</summary>
        static ToolHook BuildShovelHook(Transform env, Mats m)
        {
            var root = new GameObject("ShovelHook");
            root.transform.SetParent(env, false);
            root.transform.localPosition = new Vector3(1.1f, 0f, 2.9f);
            BuilderUtils.Block("Hook", root.transform, new Vector3(0f, 1.36f, 0.06f), new Vector3(0.05f, 0.05f, 0.08f), m.Iron, true, false);
            var shovel = ChoreProps.Shovel(root.transform, new Vector3(0f, 0.97f, 0f), new Vector3(0f, 0f, 180f));
            BuilderUtils.ClearStatic(shovel);
            root.layer = BuilderUtils.Layer("Interactable");
            root.tag = "Interactable";
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.8f, 0f);
            col.size = new Vector3(0.4f, 1.3f, 0.3f);
            var hook = root.AddComponent<ToolHook>();
            hook.visual = shovel;
            return hook;
        }

        static TaskTarget AddTarget(GameObject go, TaskBase task, string id)
        {
            go.layer = BuilderUtils.Layer("Interactable");
            go.tag = "Interactable";
            var t = go.AddComponent<TaskTarget>();
            t.task = task;
            t.id = id;
            return t;
        }

        /// <summary>Bao khoai trên sàn hầm thực phẩm, góc mạn trái phía mũi (hệ Unity của gốc tàu, cha là FoodHold).</summary>
        static readonly Vector3[] SackSpots =
        {
            new Vector3(13.9f, 2.8f, 1.7f),
            new Vector3(14f, 2.8f, 2.35f),
            new Vector3(13.3f, 2.8f, 2.1f),
        };

        static SackStation BuildSacks(Transform env, Transform hold, Transform tasks, ColdZone coldZone, Mats m)
        {
            var task = new GameObject("SackStation").AddComponent<SackStation>();
            task.transform.SetParent(tasks, false);
            // Vùng giữ bao (thế giới): cả con tàu; rơi khỏi tàu thì bao tự về chỗ cũ.
            var area = new Bounds(new Vector3(0f, 2f, 0f), new Vector3(130f, 30f, 30f));
            task.sacks = SackSpots.Select(p => Props.PotatoSack(hold, p, area)).ToArray();
            foreach (var sack in task.sacks)
            {
                sack.GetComponent<ReturnToStart>().killY = SackKillY;
                ChoreProps.OpenSack(sack.transform);
            }
            // Bao đặt cạnh thùng sàn, bên đầu đông bàn sơ chế; kê cao SackLift để Ba đứng thẳng đỡ miệng và đáy bao (luật camera 17/10).
            const float sackLift = SackStation.SackLift;
            var zone = BuilderUtils.Trigger<DropZone>("SackDropZone", env, new Vector3(1.32f, 0.65f + sackLift, 0f), new Vector3(0.9f, 0.95f, 0.85f));
            zone.gameObject.layer = BuilderUtils.Layer("Interactable");
            zone.acceptKind = "sack";
            zone.anchor = new GameObject("Anchor").transform;
            zone.anchor.SetParent(zone.transform, false);
            zone.anchor.localPosition = new Vector3(0f, -0.05f, 0f);
            task.zone = zone;
            task.coldRoom = coldZone;

            // Miệng bao: vùng nhắm phủ bao đang đặt cạnh thùng (collider bao tắt).
            var mouth = new GameObject("SackMouth");
            mouth.transform.SetParent(env, false);
            mouth.transform.localPosition = new Vector3(1.32f, 0.85f + sackLift, 0f);
            mouth.AddComponent<BoxCollider>().size = new Vector3(0.5f, 0.62f, 0.4f);
            task.mouth = AddTarget(mouth, task, "mouth");
            // Bao nghiêng quanh mép dưới phía đông, đổ sang thùng đặt dưới sàn cạnh đầu bàn.
            var pivot = new GameObject("SackTipPivot").transform;
            pivot.SetParent(env, false);
            pivot.localPosition = new Vector3(1.32f, 0.9f + sackLift, 0f);
            task.tipPivot = pivot;

            var tub = BuilderUtils.ArtSlot("PotatoTub", env, new Vector3(1.32f, 0f, 0f), Vector3.zero, t =>
            {
                // Chậu gỗ hở miệng cao 0,3 m: đáy + 14 thanh ván quanh vòng, hai đai sắt.
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Bottom", t, new Vector3(0f, 0.02f, 0f), new Vector3(0.48f, 0.02f, 0.48f), m.Wood);
                for (int k = 0; k < 14; k++)
                {
                    float a = k * Mathf.PI * 2f / 14f;
                    BuilderUtils.Block("Stave", t, new Vector3(Mathf.Sin(a) * 0.235f, 0.15f, Mathf.Cos(a) * 0.235f), new Vector3(0.11f, 0.3f, 0.025f), m.Wood,
                        true, true, new Vector3(0f, a * Mathf.Rad2Deg, 0f));
                }
                foreach (var y in new[] { 0.07f, 0.26f })
                    for (int k = 0; k < 14; k++)
                    {
                        float a = k * Mathf.PI * 2f / 14f;
                        BuilderUtils.Block("Hoop", t, new Vector3(Mathf.Sin(a) * 0.25f, y, Mathf.Cos(a) * 0.25f), new Vector3(0.115f, 0.03f, 0.006f), m.Iron,
                            true, false, new Vector3(0f, a * Mathf.Rad2Deg, 0f));
                    }
                return t.gameObject;
            });
            BuilderUtils.ClearStatic(tub);
            var pile = new GameObject("TubPotatoes").transform;
            pile.SetParent(tub.transform, false);
            var potatoMat = AssetFactory.Lit("Potato", new Color(0.62f, 0.48f, 0.3f), 0.25f);
            var rng = new System.Random(11);
            var potatoes = new List<Transform>();
            for (int i = 0; i < 40; i++)
            {
                // Lớp dưới trước, rải trong lòng thùng, cao dần.
                int layer = i / 10;
                float a = (float)(rng.NextDouble() * Mathf.PI * 2), r = 0.04f + (float)rng.NextDouble() * 0.15f;
                var p = BuilderUtils.ArtSlot("Potato_" + (i % 6), pile, new Vector3(Mathf.Cos(a) * r, 0.07f + layer * 0.065f, Mathf.Sin(a) * r),
                    new Vector3(rng.Next(360), rng.Next(360), 0f), t =>
                    {
                        BuilderUtils.Prim(PrimitiveType.Sphere, "Body", t, Vector3.zero, new Vector3(0.085f, 0.06f, 0.065f), potatoMat, false, false);
                        return t.gameObject;
                    });
                p.name = "Potato_" + i;
                BuilderUtils.ClearStatic(p);
                potatoes.Add(p.transform);
            }
            task.tubPotatoes = potatoes.ToArray();

            // Bao rỗng gấp lại, chồng dưới gầm bàn đầu đông.
            var burlap = AssetFactory.Lit("Burlap", new Color(0.55f, 0.45f, 0.3f), 0.05f);
            var empties = new List<GameObject>();
            for (int i = 0; i < 6; i++)
            {
                var e = BuilderUtils.ArtSlot("PotatoSackEmpty", env, new Vector3(0.7f, i * 0.05f, 0.05f), new Vector3(0f, (i % 2) * 14f - 7f, 0f), t =>
                {
                    BuilderUtils.Block("Body", t, new Vector3(0f, 0.025f, 0f), new Vector3(0.45f, 0.05f, 0.35f), burlap, false, false);
                    return t.gameObject;
                });
                e.name = "EmptySack_" + i;
                empties.Add(e);
            }
            task.emptySacks = empties.ToArray();
            return task;
        }

        /// <summary>Tâm lớp nước trong chậu (hệ chậu, chưa cộng kê): mặt trên 0,762, dưới đáy nồi 0,77.</summary>
        public const float WaterY = 0.556f;
        /// <summary>Tâm và bề sâu (z) kệ dụng cụ cạnh đầu nam chậu.</summary>
        public const float ToolShelfZ = -2.345f, ToolShelfDepth = 0.3f;

        /// <summary>Phòng rửa: chậu nước sát tường đông, chồng nồi bẩn ở góc tây nam, giá úp nồi trên tường trên chậu.</summary>
        static WashStation BuildWash(Transform env, Transform tasks, Mats m)
        {
            var task = new GameObject("WashStation").AddComponent<WashStation>();
            task.transform.SetParent(tasks, false);

            var basin = BuilderUtils.Group("WashBasin", env);
            basin.transform.localPosition = new Vector3(6.62f, 0f, -1.6f);
            basin.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            // Bồn gỗ lót kẽm hở lòng, trên bốn chân cao; nước dưới miệng nồi. Lòng nông (vành 0,95 m, kê cao hơn bồn thường 0,2 m): Ba cọ
            // nồi đứng thẳng, camera không cúi (17/10), nên nồi phải trong tầm tay đứng; vành nồi (0,97 m) nhô trên vành chậu để hai tay
            // nắm vành nồi ở phía trên thành chậu, cẳng tay không cắt qua thành (PlayerClipProbe, 17/10 "tay xuyên chậu xuyên nồi").
            var zinc = AssetFactory.Lit("WashZinc", new Color(0.38f, 0.42f, 0.43f), 0.28f, 0.7f);
            const float lift = WashStation.BasinLift;
            foreach (float x in new[] { -0.48f, 0.48f })
                foreach (float z in new[] { -0.24f, 0.24f })
                    BuilderUtils.Block("Leg", basin.transform, new Vector3(x, (0.52f + lift) * 0.5f, z), new Vector3(0.065f, 0.52f + lift, 0.065f), m.Wood);
            BuilderUtils.Block("Bottom", basin.transform, new Vector3(0f, 0.53f + lift, 0f), new Vector3(1.1f, 0.035f, 0.6f), zinc);
            foreach (float x in new[] { -0.55f, 0.55f })
                BuilderUtils.Block("End", basin.transform, new Vector3(x, 0.64f + lift, 0f), new Vector3(0.035f, 0.22f, 0.64f), zinc);
            foreach (float z in new[] { -0.3f, 0.3f })
                BuilderUtils.Block("Side", basin.transform, new Vector3(0f, 0.64f + lift, z), new Vector3(1.1f, 0.22f, 0.035f), zinc);
            // Lớp nước mỏng dưới đáy nồi (mặt trên 0,762 + kê < đáy nồi 0,77 + kê): cao hơn đáy nồi thì thấy mặt nước xanh trong lòng nồi (QA-0134).
            BuilderUtils.Block("Water", basin.transform, new Vector3(0f, WaterY + lift, 0f), new Vector3(1.03f, 0.012f, 0.53f), m.Water, true, false);
            // Kệ dụng cụ cách mặt ngoài đầu chậu (z −2,1675) 2,75 cm, không xuyên vào thành chậu (QA-0135).
            var tools = BuilderUtils.Group("WashTools", env).transform;
            tools.localPosition = new Vector3(6.35f, 0.87f, ToolShelfZ);
            BuilderUtils.Block("ToolShelf", env, new Vector3(6.35f, 0.82f, ToolShelfZ), new Vector3(0.9f, 0.055f, ToolShelfDepth), m.Wood);
            // Bàn chải ngắn nằm trên kệ (brush17): E để cầm, kệ trống khi đang cầm; hết lượt rửa tự về kệ (ToolHook như xẻng, chổi).
            var brushSpot = BuilderUtils.Group("BrushHook", tools);
            // Đầu tây của kệ (x 5,89–6,31), không nằm sau thành chậu (x ≥ 6,32): ở giữa kệ thì tia nhìn từ chỗ đứng trước chậu chạm
            // chậu trước, không nhắm được bàn chải (bot ngày 1 brush17b: BLOCKER take_BrushHook).
            brushSpot.transform.localPosition = new Vector3(-0.38f, 0f, 0f);
            var shelfBrush = ChoreProps.Brush(brushSpot.transform);
            shelfBrush.transform.localPosition = new Vector3(-0.08f, BrushShape.Short.HeadRadius * 0.95f, 0.02f);
            shelfBrush.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            brushSpot.layer = BuilderUtils.Layer("Interactable");
            brushSpot.tag = "Interactable";
            var brushCol = brushSpot.AddComponent<BoxCollider>();
            brushCol.center = new Vector3(0.12f, 0.04f, 0.02f);
            brushCol.size = new Vector3(0.46f, 0.1f, 0.14f);
            var brushHook = brushSpot.AddComponent<ToolHook>();
            brushHook.tool = Tool.Brush;
            brushHook.visual = shelfBrush;
            brushHook.takeKey = "prompt.take_brush";
            brushHook.hangKey = "prompt.put_brush";
            task.brushHook = brushHook;
            BuilderUtils.Block("Rag", tools, new Vector3(-0.36f, 0f, 0.07f), new Vector3(0.16f, 0.015f, 0.12f), m.Paper, false, false);
            BuilderUtils.Prim(PrimitiveType.Sphere, "StrawScourer", tools, new Vector3(-0.36f, 0.025f, -0.08f), new Vector3(0.1f, 0.045f, 0.08f), m.Bristle, false, false);
            BuilderUtils.Block("AshBox", tools, new Vector3(-0.18f, 0f, 0f), new Vector3(0.15f, 0.08f, 0.14f), m.Wood, false, false);
            BuilderUtils.Block("ScouringAsh", tools, new Vector3(-0.18f, 0.045f, 0f), new Vector3(0.12f, 0.008f, 0.11f), m.Ash, false, false);
            Props.Dress("WoodenBucket", env, new Vector3(6.63f, 0f, -2.43f), 0f, new Vector3(0.3f, 0.32f, 0.3f), m.Wood, true).name = "HotWaterBucket";
            task.basin = AddTarget(basin, task, "basin");
            var basinCol = basin.AddComponent<BoxCollider>();
            basinCol.center = new Vector3(0f, 0.55f + lift, 0f);
            basinCol.size = new Vector3(1.14f, 0.6f, 0.64f);
            basinCol.isTrigger = true;
            var pot = ChoreProps.Pot(basin.transform, "BasinPot", 0.4f, 0.2f, m.Copper);
            // Nồi sát thành gần (phía Ba đứng) để vành nồi trong tầm tay.
            pot.transform.localPosition = new Vector3(0f, 0.57f + lift, WashStation.PotNear);
            task.basinPot = pot;
            // Lớp bẩn có mặt nạ cọ: dựng lúc chạy theo cỡ nồi đang ở chậu (WashStation.PrepareScrub).
            task.dirtMaterial = AssetFactory.DirtMasked();
            // Bàn chải đặt tạm trên mép chậu (góc gần, bên phải) khi Ba bê nồi bằng hai tay.
            var rimBrush = ChoreProps.Brush(basin.transform);
            rimBrush.name = "BrushOnRim";
            // Nằm dọc trên mép thành đầu phải của chậu (mặt trên ở 0,75 + kê), mũi lông về phía Ba, cán ra sau.
            rimBrush.transform.localPosition = new Vector3(0.55f, 0.75f + lift + BrushShape.Short.HeadRadius, -0.27f);
            rimBrush.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            task.brushOnRim = rimBrush;

            // Chồng nồi bẩn: ba cột, mỗi cột bốn chiếc; hiện theo số nồi chờ rửa.
            var pile = BuilderUtils.Group("DirtyPots", env);
            pile.transform.localPosition = new Vector3(5.55f, 0f, -2.5f);
            var pileVisuals = new GameObject[12];
            for (int i = 0; i < pileVisuals.Length; i++)
            {
                int column = i % 3, level = i / 3;
                var p = BuilderUtils.Group("DirtyPot_" + i, pile.transform);
                p.transform.localPosition = new Vector3(-0.22f + 0.22f * column, 0.08f + 0.16f * level, (column == 1 ? 0.1f : 0f));
                p.transform.localRotation = Quaternion.Euler(0f, 37f * i, 0f);
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Body", p.transform, Vector3.zero, new Vector3(0.28f, 0.08f, 0.28f), m.Copper, false, false);
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Soot", p.transform, new Vector3(0f, 0.081f, 0f), new Vector3(0.25f, 0.002f, 0.25f), m.Soot, false, false);
                pileVisuals[i] = p;
            }
            task.pileVisuals = pileVisuals;
            task.pile = AddTarget(pile, task, "pile");
            var pileCol = pile.AddComponent<BoxCollider>();
            pileCol.center = new Vector3(0f, 0.35f, 0.05f);
            pileCol.size = new Vector3(0.75f, 0.7f, 0.5f);

            // Giá úp nồi trên tường đông, hai tầng × bốn chỗ (QA-0133): nồi úp đặt vành lên mặt kệ, quai chéo 45° để hai nồi cạnh nhau
            // không chạm quai, tâm nồi cách mặt vách 0,23 m (quai sau không lấn vách). Kệ dài 1,6 m (z −2,4…−0,8), sâu 0,37 m.
            var rack = BuilderUtils.Group("DryingRack", env);
            rack.transform.localPosition = RackOrigin;
            rack.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            for (int tier = 0; tier < RackTiers; tier++)
                BuilderUtils.Block("Shelf_" + tier, rack.transform, new Vector3(0f, tier * RackTierGap - RackShelfThickness * 0.5f, RackShelfZ),
                    new Vector3(RackWidth, RackShelfThickness, RackShelfDepth), m.Wood);
            // Hai cột đỡ ngay ngoài hai đầu kệ (không đứng chỗ nồi đầu hàng).
            foreach (float x in new[] { -RackWidth * 0.5f - 0.02f, RackWidth * 0.5f + 0.02f })
                BuilderUtils.Block("Upright", rack.transform, new Vector3(x, (RackTierGap - 0.1f) * 0.5f, RackShelfZ + RackShelfDepth * 0.5f - 0.025f),
                    new Vector3(0.04f, RackTierGap + 0.1f, 0.04f), m.Wood, true, false);
            var slots = new Transform[RackTiers * RackPerTier];
            for (int i = 0; i < slots.Length; i++)
            {
                int tier = i / RackPerTier, col = i % RackPerTier;
                var slot = new GameObject("RackSlot_" + i).transform;
                slot.SetParent(rack.transform, false);
                // Gốc chỗ đặt: điểm giữa vành nồi úp trên mặt kệ; quai chéo 45° so với vách.
                slot.localPosition = new Vector3((col - (RackPerTier - 1) * 0.5f) * RackSpacing, tier * RackTierGap, RackPotZ);
                slot.localRotation = Quaternion.Euler(0f, 45f, 0f);
                slots[i] = slot;
            }
            task.rackSlots = slots;
            var rackVisuals = new GameObject[6];
            for (int i = 0; i < rackVisuals.Length; i++)
            {
                var c = BuilderUtils.Prim(PrimitiveType.Cylinder, "CleanPot_" + i, rack.transform,
                    slots[i].localPosition + new Vector3(0f, 0.08f, 0f), new Vector3(0.17f, 0.08f, 0.17f), m.Copper, false, false);
                rackVisuals[i] = c;
            }
            task.rackVisuals = rackVisuals;
            task.rack = AddTarget(rack, task, "rack");
            var rackCol = rack.AddComponent<BoxCollider>();
            rackCol.center = new Vector3(0f, RackTierGap * 0.5f + 0.1f, RackShelfZ);
            rackCol.size = new Vector3(RackWidth, RackTierGap + 0.4f, RackShelfDepth);
            // Chỉ để nhắm (E úp nồi); không là khối đặc trên đầu người đứng sát vách.
            rackCol.isTrigger = true;

            BuildWashTap(env, task, m);
            return task;
        }

        /// <summary>Giá úp nồi: gốc ở tâm hàng nồi, mặt kệ tầng dưới (hệ bếp); trục z cục bộ chĩa vào vách đông.</summary>
        public static readonly Vector3 RackOrigin = new Vector3(6.72f, 1.52f, -1.6f);
        public const int RackTiers = 2, RackPerTier = 4;
        public const float RackSpacing = 0.4f, RackTierGap = 0.42f, RackWidth = 1.6f;
        public const float RackShelfThickness = 0.03f, RackShelfDepth = 0.37f, RackShelfZ = 0.035f, RackPotZ = 0f;
        /// <summary>Vòi trên chậu: gốc ống ở mặt vách đông; vòi chảy xuống điểm (x 6,78; z −1,95), trong lòng chậu, xa nồi.</summary>
        public static readonly Vector3 TapOrigin = new Vector3(6.95f, 1.22f, -1.95f);

        /// <summary>
        /// Vòi nước gắn vách trên đầu nam chậu (QA-0132): đế, ống ngang, thân van đồng, mỏ vòi chúc xuống và cần gạt trên thân van.
        /// Dòng nước (trụ mảnh + vệt bắn) chỉ bật khi đang chảy (WashTap).
        /// </summary>
        static void BuildWashTap(Transform env, WashStation task, Mats m)
        {
            var root = BuilderUtils.Group("WashTap", env);
            root.transform.localPosition = TapOrigin;
            var t = root.transform;
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Flange", t, new Vector3(-0.006f, 0f, 0f), new Vector3(0.07f, 0.006f, 0.07f), m.Iron, true, false,
                new Vector3(0f, 0f, 90f));
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Pipe", t, new Vector3(-0.08f, 0f, 0f), new Vector3(0.026f, 0.075f, 0.026f), m.Iron, true, false,
                new Vector3(0f, 0f, 90f));
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Valve", t, new Vector3(-0.12f, 0.012f, 0f), new Vector3(0.05f, 0.03f, 0.05f), m.Brass, true, false);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Elbow", t, new Vector3(-0.17f, -0.005f, 0f), new Vector3(0.03f, 0.016f, 0.03f), m.Brass, true, false,
                new Vector3(0f, 0f, 90f));
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Spout", t, new Vector3(-0.17f, -0.045f, 0f), new Vector3(0.024f, 0.045f, 0.024f), m.Brass, true, false);
            var lever = new GameObject("LeverPivot").transform;
            lever.SetParent(t, false);
            lever.localPosition = new Vector3(-0.12f, 0.045f, 0f);
            BuilderUtils.Block("Lever", lever, new Vector3(0f, 0.05f, 0f), new Vector3(0.016f, 0.1f, 0.016f), m.Iron, false, false);
            BuilderUtils.Prim(PrimitiveType.Sphere, "Knob", lever, new Vector3(0f, 0.1f, 0f), new Vector3(0.026f, 0.026f, 0.026f), m.Wood, false, false);
            // Dòng nước từ mỏ vòi (y 1,13) xuống mặt nước trong chậu (0,762).
            var stream = BuilderUtils.Group("Stream", t);
            float nozzle = TapOrigin.y - 0.09f, surface = WaterY + WashStation.BasinLift + 0.006f;
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Flow", stream.transform, new Vector3(-0.17f, (nozzle + surface) * 0.5f - TapOrigin.y, 0f),
                new Vector3(0.014f, (nozzle - surface) * 0.5f, 0.014f), m.Water, false, false);
            BuilderUtils.Prim(PrimitiveType.Sphere, "Splash", stream.transform, new Vector3(-0.17f, surface + 0.003f - TapOrigin.y, 0f),
                new Vector3(0.07f, 0.008f, 0.07f), m.Water, false, false);
            foreach (var r in stream.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            BuilderUtils.ClearStatic(stream);
            BuilderUtils.ClearStatic(lever.gameObject);
            var tap = root.AddComponent<WashTap>();
            tap.lever = lever;
            tap.stream = stream;
            task.tap = tap;
            task.tapTarget = AddTarget(root, task, "tap");
            var col = root.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = new Vector3(-0.1f, 0.03f, 0f);
            col.size = new Vector3(0.22f, 0.3f, 0.14f);
        }

        /// <summary>Đồng hồ treo tường bắc (giữa lò thứ hai và thùng than), mặt quay vào phòng.</summary>
        static WallClock BuildWallClock(Transform env, Mats m)
        {
            var root = BuilderUtils.Group("WallClock", env);
            root.transform.localPosition = new Vector3(-0.5f, 1.95f, 2.985f);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Rim", root.transform, Vector3.zero, new Vector3(0.4f, 0.012f, 0.4f), m.Brass, true, false,
                new Vector3(90f, 0f, 0f));
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Face", root.transform, new Vector3(0f, 0f, -0.006f), new Vector3(0.36f, 0.006f, 0.36f), m.ClockFace,
                true, false, new Vector3(90f, 0f, 0f));
            var clock = root.AddComponent<WallClock>();
            clock.hourHand = Hand(root.transform, "HourHand", 0.1f, 0.026f, -0.016f, m.Iron);
            clock.minuteHand = Hand(root.transform, "MinuteHand", 0.15f, 0.016f, -0.02f, m.Iron);
            return clock;
        }

        static Transform Hand(Transform parent, string name, float length, float width, float z, Material mat)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = new Vector3(0f, 0f, z);
            BuilderUtils.Block("Blade", pivot, new Vector3(0f, length * 0.45f, 0f), new Vector3(width, length, 0.005f), mat, false, false);
            return pivot;
        }

        /// <summary>Bảng phấn ghi lệnh của lượt (ChalkboardView vẽ vào RenderTexture) trên tường bắc, phía cửa.</summary>
        static ChalkboardView BuildChalkboard(Transform env, Mats m)
        {
            var root = BuilderUtils.Group("Chalkboard", env);
            root.transform.localPosition = new Vector3(2.75f, 1.45f, 2.98f);
            BuilderUtils.Block("Frame", root.transform, Vector3.zero, new Vector3(1.42f, 0.92f, 0.04f), m.Wood, true, false);
            var surface = BuilderUtils.Prim(PrimitiveType.Quad, "Surface", root.transform, new Vector3(0f, 0f, -0.022f),
                new Vector3(1.3f, 0.8f, 1f), m.Chalkboard, false, false);
            var view = root.AddComponent<ChalkboardView>();
            view.surface = surface.GetComponent<Renderer>();
            return view;
        }

        static SecurePotsTask BuildPots(Transform env, Transform tasks, Mats m, CoalStation coal)
        {
            // Bếp lò phụ bằng gang thay quầy gỗ: nồi đặt trên ba lưới nướng, mỗi ô có buồng đốt và cửa nạp than riêng (GalleyRange.cs).
            var counter = BuildPotRange(env, m);
            BindRangeCells(coal, counter.transform, 1);

            var task = new GameObject("SecurePotsTask").AddComponent<SecurePotsTask>();
            task.transform.SetParent(tasks, false);
            task.doneEvent = EventIds.S2SecureDone;
            var area = new Bounds(new Vector3(0f, 1.3f, 0f), new Vector3(10f, 2.6f, 6f));
            var pots = new LooseObjectSway[3];
            var targets = new TaskTarget[3];
            for (int i = 0; i < 3; i++)
            {
                // Đáy mesh rỗng nằm tại y = 0; đặt sát mặt trên nắp gang.
                var pos = counter.transform.position + new Vector3(-1f + i, 0.908f, 0f);
                var pot = ChoreProps.Pot(env, "LoosePot_" + i, 0.36f, 0.3f, m.Copper);
                pot.transform.position = pos;
                // Nồi thật rỗng để thấy đáy đang cọ sau khi tháo nắp.
                var copper = AssetFactory.Lit("RangePotCopper", new Color(0.27f, 0.125f, 0.065f), 0.3f, 0.65f);
                foreach (var renderer in pot.GetComponentsInChildren<MeshRenderer>())
                {
                    var materials = renderer.sharedMaterials;
                    for (int slot = 0; slot < materials.Length; slot++) materials[slot] = copper;
                    renderer.sharedMaterials = materials;
                }
                FireBuilder.PotSteam(pot.transform);
                pot.name = "LoosePot_" + i;
                var col = pot.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, 0.16f, 0f);
                col.size = new Vector3(0.36f, 0.3f, 0.36f);
                var rb = pot.AddComponent<Rigidbody>();
                rb.mass = 4f;
                rb.constraints = RigidbodyConstraints.FreezeRotation;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                pots[i] = pot.AddComponent<LooseObjectSway>();
                var back = pot.AddComponent<ReturnToStart>();
                back.useArea = true;
                back.area = area;
                targets[i] = AddTarget(pot, task, "pot" + i);
            }
            task.pots = pots;
            task.targets = targets;
            return task;
        }

        /// <summary>
        /// Đèn bến Colombo xa phía mạn bắc (tối ngày 3, tàu đậu ngoài Colombo; DIRECTOR §6): một dải đốm sáng vàng ngoài khơi, không
        /// đèn thật (không tốn suất đèn), không collider. Chỉ bật tối ngày 3.
        /// </summary>
        static GameObject BuildColomboLights(Transform env)
        {
            var root = BuilderUtils.Group("ColomboLights", env);
            var glow = AssetFactory.Emissive("ColomboShoreDots", new Color(0.1f, 0.08f, 0.05f), new Color(1f, 0.72f, 0.38f) * 6f, false);
            var rng = new System.Random(1911);
            for (int i = 0; i < 46; i++)
            {
                float x = -170f + i * 7.4f + (float)rng.NextDouble() * 4f;
                float z = 150f + (float)rng.NextDouble() * 35f;
                float y = 1.5f + (float)rng.NextDouble() * (i % 7 == 0 ? 14f : 4f);
                float size = 0.6f + (float)rng.NextDouble() * 0.7f;
                BuilderUtils.Prim(PrimitiveType.Sphere, "ShoreDot", root.transform, new Vector3(x, y, z), Vector3.one * size, glow, false, false);
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            BuilderUtils.SetRenderingLayer(root, WorldRenderingLayers.Exterior);
            root.SetActive(false);
            return root;
        }

        static MealPots BuildMealPots(Transform env, SecurePotsTask secure, WashStation wash, TallPotStation tall, PanStation pan, GameObject npcs, CoalStation coal, Mats m)
        {
            var root = BuilderUtils.Group("MealPots", env).AddComponent<MealPots>();
            root.wash = wash; root.tall = tall; root.pan = pan;
            // Ô bếp (W3.2): mặt nắp gang của từng ô trên hai lò (lò nam giữ nồi, lò bắc nấu). Nồi nấu đặt đúng ô, mỗi ô một nồi.
            root.hobs = coal.cells.Select(c => c.hob).OrderBy(t => t.position.z).ThenBy(t => t.position.x).ToArray();
            root.coal = coal;
            coal.meals = root;
            FireBuilder.PotSteam(tall.body);
            // Ô của nồi cao: ô giữa lò bắc; chỗ đặt nồi trên sàn trước ô đó (tâm cách mép trước lò 0,45 m, nồi cách lò 5 cm).
            tall.hob = root.hobs.OrderBy(h => (env.InverseTransformPoint(h.position) - new Vector3(-2.2f, 0.9f, 2.45f)).sqrMagnitude).First();
            var hobLocal = env.InverseTransformPoint(tall.hob.position);
            tall.stoveSpot = Beacon(env, "TallPot_StoveSpot", new Vector3(hobLocal.x, 0f, TallPotStoveZ));
            if (pan != null && pan.stops.Length > 0 && pan.stops[0] != null)
            {
                // Chảo trên bếp (QA-0148): ô gần chỗ kéo tới nhất (không phải ô nồi cao), lệch 5 cm ra xa nồi cao, quai chĩa trước–sau
                // (dọc z): chảo Ø 0,79 + nồi cao Ø 0,8 trên hai ô cách 0,8 m mà quai chĩa ngang thì quai lún 10 cm vào nồi cao.
                var near = new Vector3(pan.stops[0].localPosition.x, 0.9f, 2.45f);
                var panHob = root.hobs.Where(h => h != tall.hob).OrderBy(h => (env.InverseTransformPoint(h.position) - near).sqrMagnitude).First();
                var hl = env.InverseTransformPoint(panHob.position);
                float away = Mathf.Sign(hl.x - hobLocal.x);
                pan.rangeSpot = Beacon(env, "Pan_RangeSpot", new Vector3(hl.x + away * PanRangeOffset, hl.y - 0.001f, hl.z));
                pan.rangeSpot.localRotation = Quaternion.Euler(0f, 90f, 0f);
            }
            root.helper = npcs.GetComponentInChildren<HelperRoutine>();
            root.cook = npcs.GetComponentInChildren<CookRoutine>();
            root.cook.meals = root;
            // Mẩu bánh mì ngày 5 (DIRECTOR §6, hư cấu): nằm dọc trên vành đầu bắc chậu rửa (mặt trên 0,95 m, z −1,05), cạnh chỗ Ba đứng;
            // góc tây nam phòng rửa đã kín chồng nồi bẩn và kệ dụng cụ (bot 23/10: quản bếp đứng chồng DirtyPots). Ẩn tới khi quản bếp đặt.
            var bread = BuilderUtils.Prim(PrimitiveType.Capsule, "ChefBread", env, new Vector3(6.6f, 0.978f, -1.05f), new Vector3(0.055f, 0.075f, 0.05f),
                AssetFactory.Lit("ChefBread", new Color(0.66f, 0.42f, 0.2f), 0.2f), false, false, new Vector3(0f, 0f, 90f));
            bread.SetActive(false);
            root.chefBread = bread;
            var pots = new List<GameObject>();
            foreach (var p in secure.pots) pots.Add(p.gameObject);
            for (int i = 3; i < 8; i++)
            {
                var p = ChoreProps.Pot(root.transform, "CookingPot_" + i, 0.34f, 0.27f, m.Copper);
                p.transform.localPosition = new Vector3(-3.36f + (i - 3) * 0.58f, 0.898f, 2.45f);
                FireBuilder.PotSteam(p.transform);
                pots.Add(p);
            }
            foreach (var p in pots)
            {
                BuilderUtils.ClearStatic(p);
                BuilderUtils.Prim(PrimitiveType.Cylinder, "MealLid", p.transform, new Vector3(0.04f, 0.32f, 0f),
                    new Vector3(0.32f, 0.008f, 0.32f), m.Copper, false, false, new Vector3(0f, 0f, 15f));
                BuilderUtils.Prim(PrimitiveType.Sphere, "LidKnob", p.transform, new Vector3(0.04f, 0.35f, 0f), new Vector3(0.045f, 0.025f, 0.045f), m.Iron, false, false);
                // Nước dùng trong nồi đang nấu (muôi của quản bếp khuấy ngập trong đó); nồi bẩn, nồi sạch thì ẩn.
                var broth = BuilderUtils.Prim(PrimitiveType.Cylinder, "Broth", p.transform, new Vector3(0f, 0.19f, 0f), new Vector3(0.3f, 0.004f, 0.3f),
                    AssetFactory.Lit("Broth", new Color(0.5f, 0.38f, 0.2f), 0.75f), false, false);
                broth.SetActive(false);
                var food = BuilderUtils.Group("CookingPotatoes", p.transform);
                for (int i = 0; i < 8; i++)
                    BuilderUtils.Prim(PrimitiveType.Sphere, "Potato", food.transform,
                        new Vector3(-0.09f + i % 3 * 0.09f, 0.26f, -0.07f + i / 3 * 0.07f),
                        new Vector3(0.065f, 0.045f, 0.05f), AssetFactory.Lit("Potato", new Color(0.62f, 0.48f, 0.3f), 0.25f), false, false);
                food.SetActive(false);
            }
            root.pots = pots.ToArray();
            return root;
        }

        /// <summary>Chảo trên bếp lệch khỏi tâm ô chừng này, ra xa nồi cao (m).</summary>
        public const float PanRangeOffset = 0.05f;

        /// <summary>Chỗ nồi cao (Ø 0,8 m) trong phòng rửa mới: góc tây nam, cách vách ngăn 1,05 m và tường nam 1,2 m.</summary>
        public static readonly Vector3 TallPotSpot = new Vector3(4.4f, 0f, -1.8f);

        /// <summary>z (hệ bếp) của tâm nồi cao đặt trên sàn trước lò bắc: mép trước lò ở 1,945, nồi bán kính 0,4 cách lò 5 cm.</summary>
        public const float TallPotStoveZ = 1.5f;
        /// <summary>
        /// Ghế đẩu cạnh nồi cao (phía đông), tâm ghế cách tâm nồi 0,62 m: mép ghế sát thân nồi (bán kính 0,4) còn 2 cm. Đứng trên ghế cúi
        /// nhìn vào lòng nồi thì thân lùi sau mắt ~0,2 m (PlayerBody.KeepBodyBehindEye), vai cách tâm nồi ~0,87 m; ghế ở 0,72 m thì vai cách
        /// 0,97 m, bàn chải cán dài chỉ với tới 78% lòng nồi (brush17b, Brush17PlayTests) nên không bao giờ cọ đủ 90%.
        /// </summary>
        public static readonly Vector3 TallStoolSpot = new Vector3(TallPotSpot.x + 0.62f, 0f, TallPotSpot.z);
        /// <summary>Móc bàn chải cán dài: mặt trong vách ngăn, gần cửa phòng rửa (không vướng đường kéo nồi và chỗ Jacques).</summary>
        public static readonly Vector3 LongBrushHookSpot = new Vector3(3.43f, 0f, -0.62f);

        /// <summary>Quai nồi cao ở độ cao này (hệ thân nồi, m) và ở các góc 45°, 225° (không quay vào ghế ở phía đông dù xoay từng 90°).</summary>
        public const float TallHandleY = 0.86f;

        /// <summary>
        /// Hai quai vòng bằng sắt bên thân nồi cao (QA-0137): bán kính thân ở độ cao quai đo từ mesh thật của nồi (art có thể thu/loe), mỗi
        /// quai là thanh nắm nằm ngang song song thành, hai chân bắt vào thành.
        /// </summary>
        static void TallPotHandles(Transform body, Mats m)
        {
            float radius = 0f;
            foreach (var mf in body.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.GetComponent<MeshRenderer>() == null) continue;
                var toBody = body.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var p = toBody.MultiplyPoint3x4(v);
                    if (Mathf.Abs(p.y - TallHandleY) < 0.04f) radius = Mathf.Max(radius, new Vector2(p.x, p.z).magnitude);
                }
            }
            if (radius <= 0.1f) radius = TallPotStation.Radius;
            foreach (float angle in new[] { 45f, 225f })
            {
                var h = new GameObject("Handle").transform;
                h.SetParent(body, false);
                h.localRotation = Quaternion.Euler(0f, angle, 0f);
                // +x cục bộ của quai chĩa ra ngoài thân.
                foreach (float z in new[] { -0.07f, 0.07f })
                    BuilderUtils.Block("HandleLug", h, new Vector3(radius + 0.022f, TallHandleY, z), new Vector3(0.05f, 0.022f, 0.022f), m.Iron, false, false);
                BuilderUtils.Block("HandleGrip", h, new Vector3(radius + 0.05f, TallHandleY, 0f), new Vector3(0.024f, 0.03f, 0.17f), m.Iron, false, false);
            }
        }

        static TallPotStation BuildTallPot(Transform env, Transform tasks, Mats m)
        {
            var task = new GameObject("TallPotStation").AddComponent<TallPotStation>();
            task.transform.SetParent(tasks, false);

            // Gốc nồi (kéo lê, nhấc): Rigidbody kinematic + hộp va chạm; hình nồi là con để nghiêng khi kéo và chao khi sóng.
            var root = BuilderUtils.Group("TallPot", env);
            root.transform.localPosition = TallPotSpot;
            var art = BuilderUtils.ArtSlot("CopperPotTall", root.transform, Vector3.zero, Vector3.zero, t =>
            {
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Body", t, new Vector3(0f, 0.5f, 0f), new Vector3(0.8f, 0.5f, 0.8f), m.Copper, false, false);
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Inside", t, new Vector3(0f, 0.995f, 0f), new Vector3(0.7f, 0.004f, 0.7f), m.CopperInside, false, false);
                return t.gameObject;
            });
            art.name = "PotBody";
            TallPotHandles(art.transform, m);
            // Va chạm hình trụ lồi (không phải hộp): góc hộp lòi ra 0,17 m ở đường chéo chạm vào người chơi đang kéo (người bị đẩy 23 cm
            // một khung) và vào ghế đẩu. Con không nghiêng theo thân nồi; vẫn ở lớp Interactable để nhắm nồi.
            var colGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            colGo.name = "PotCollider";
            var cylMesh = colGo.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(colGo.GetComponent<Collider>());
            Object.DestroyImmediate(colGo.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(colGo.GetComponent<MeshFilter>());
            colGo.transform.SetParent(root.transform, false);
            colGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            colGo.transform.localScale = new Vector3(2f * TallPotStation.Radius, 0.5f * TallPotStation.Height, 2f * TallPotStation.Radius);
            var potCol = colGo.AddComponent<MeshCollider>();
            potCol.sharedMesh = cylMesh;
            potCol.convex = true;
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.mass = 60f;
            rb.interpolation = RigidbodyInterpolation.None;
            BuilderUtils.ClearStatic(root);
            task.pot = rb;
            task.body = art.transform;
            task.potTarget = AddTarget(root, task, "pot");
            colGo.layer = root.layer;
            colGo.tag = root.tag;
            task.home = Beacon(env, "TallPot_Home", TallPotSpot);
            task.dirtMaterial = AssetFactory.DirtMasked();

            // Ghế đẩu sát nồi, phía đông (phía bắc là đường kéo nồi qua cửa vách ngăn): đứng trên ghế tâm nồi cách người 0,65 m, vành gần ở
            // tầm đùi, cán dài với tới đáy.
            var stool = BuilderUtils.ArtSlot("Stool", env, TallStoolSpot, Vector3.zero, t =>
            {
                BuilderUtils.Block("Seat", t, new Vector3(0f, 0.33f, 0f), new Vector3(0.4f, 0.04f, 0.4f), m.Wood, true, false);
                foreach (var x in new[] { -0.16f, 0.16f })
                    foreach (var z in new[] { -0.16f, 0.16f })
                        BuilderUtils.Block("Leg", t, new Vector3(x, 0.16f, z), new Vector3(0.04f, 0.32f, 0.04f), m.Wood, true, false);
                return t.gameObject;
            });
            var stoolCol = stool.AddComponent<BoxCollider>();
            stoolCol.center = new Vector3(0f, 0.2f, 0f);
            stoolCol.size = new Vector3(0.45f, 0.4f, 0.45f);
            stoolCol.isTrigger = true; // người chơi đứng "lên" ghế bằng ExtraHeadHeight
            task.stool = AddTarget(stool, task, "stool");
            task.standPoint = stool.transform;

            // Móc bàn chải cán dài trên mặt trong vách ngăn (vách tây phòng rửa), cạnh góc nồi cao: bàn chải dựng đầu lông lên.
            var hookRoot = BuilderUtils.Group("LongBrushHook", env);
            hookRoot.transform.localPosition = LongBrushHookSpot;
            BuilderUtils.Block("Peg", hookRoot.transform, new Vector3(-0.05f, 1.62f, 0f), new Vector3(0.08f, 0.04f, 0.05f), m.Iron, true, false);
            var hung = ChoreProps.LongBrush(hookRoot.transform);
            hung.transform.localPosition = new Vector3(0f, BrushShape.Long.Length + 0.01f, 0f);
            hung.transform.localRotation = Quaternion.Euler(180f, 0f, 3f);
            hookRoot.layer = BuilderUtils.Layer("Interactable");
            hookRoot.tag = "Interactable";
            var hookCol = hookRoot.AddComponent<BoxCollider>();
            hookCol.center = new Vector3(0f, 0.95f, 0f);
            hookCol.size = new Vector3(0.22f, 1.8f, 0.22f);
            var hook = hookRoot.AddComponent<ToolHook>();
            hook.tool = Tool.LongBrush;
            hook.visual = hung;
            hook.takeKey = "prompt.take_long_brush";
            hook.hangKey = "prompt.hang_long_brush";
            task.longBrushHook = hook;

            // Thủy thủ Jacques (cánh tay giữ chỗ khi Jacques không có mặt): ẩn tới lúc nồi chao, đưa tay giữ vành nồi.
            var hidden = new GameObject("JacquesArm_Hidden").transform;
            hidden.SetParent(env, false);
            hidden.position = new Vector3(TallPotSpot.x - 0.95f, 1.25f, TallPotSpot.z - 1.05f);
            hidden.rotation = Quaternion.Euler(0f, 90f, 0f);
            var hold = new GameObject("JacquesArm_Hold").transform;
            hold.SetParent(env, false);
            hold.position = new Vector3(TallPotSpot.x - 0.42f, 1.3f, TallPotSpot.z - 0.1f);
            hold.rotation = Quaternion.Euler(0f, 90f, 0f);
            task.sailorArm = NpcArm(env, "JacquesArm", hidden, m.SailorShirt, m.Skin, m.SailorCap);
            task.sailorArm.hideWhenRetracted = true;
            task.sailorHoldPoint = hold;
            return task;
        }

        /// <summary>Cánh tay NPC giữ chỗ: tay áo + bàn tay, trục +Z hướng về bàn tay. cap != null: thêm thân, đầu, mũ
        /// (người đứng sau vai phải của cánh tay, nhìn theo +Z) để NPC hiện cả người.</summary>
        public static NpcArm NpcArm(Transform parent, string name, Transform hidden, Material sleeve, Material skin, Material cap = null)
        {
            var go = BuilderUtils.ArtSlot(name, parent, hidden.position, hidden.eulerAngles, t =>
            {
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Sleeve", t, new Vector3(0f, 0f, -0.35f), new Vector3(0.11f, 0.35f, 0.11f),
                    sleeve, false, false, new Vector3(90f, 0f, 0f));
                BuilderUtils.Block("Hand", t, new Vector3(0f, 0f, 0.05f), new Vector3(0.09f, 0.03f, 0.12f), skin, false, false);
                if (cap != null)
                {
                    // Gốc cánh tay ở độ cao bàn tay (~1,25 m); thân lùi 0,5 m sau bàn tay, lệch 0,22 m sang phải.
                    var body = new Vector3(0.22f, 0f, -0.5f);
                    BuilderUtils.Prim(PrimitiveType.Capsule, "Body", t, body + new Vector3(0f, -0.45f, 0f), new Vector3(0.42f, 0.6f, 0.28f),
                        sleeve, false, false);
                    BuilderUtils.Prim(PrimitiveType.Cylinder, "Legs", t, body + new Vector3(0f, -0.95f, 0f), new Vector3(0.3f, 0.32f, 0.22f),
                        cap, false, false);
                    BuilderUtils.Prim(PrimitiveType.Cylinder, "OtherSleeve", t, body + new Vector3(0.24f, -0.3f, 0.02f), new Vector3(0.1f, 0.3f, 0.1f),
                        sleeve, false, false);
                    BuilderUtils.Prim(PrimitiveType.Sphere, "Head", t, body + new Vector3(0f, 0.36f, 0f), new Vector3(0.21f, 0.25f, 0.22f),
                        skin, false, false);
                    BuilderUtils.Prim(PrimitiveType.Cylinder, "Cap", t, body + new Vector3(0f, 0.5f, 0f), new Vector3(0.25f, 0.035f, 0.25f),
                        cap, false, false);
                }
                return t.gameObject;
            });
            go.transform.position = hidden.position;
            go.transform.rotation = hidden.rotation;
            var arm = go.AddComponent<NpcArm>();
            arm.hiddenPoint = hidden;
            // Điểm lòng bàn tay không bị scale (đồng xu, tách cà phê gắn vào đây).
            var palm = new GameObject("Palm").transform;
            palm.SetParent(go.transform, false);
            palm.localPosition = new Vector3(0f, 0.02f, 0.05f);
            arm.hand = palm;
            return arm;
        }

        /// <summary>
        /// Cột sắt đứng từ sàn lên quá điểm treo đèn một chút, cần ngang từ đỉnh cột tới điểm treo, móc nhỏ ở đầu cần. Không collider
        /// (cột nằm trong cột buồm / sát mạn, ngoài lối đi), lớp ánh sáng ngoài trời như đèn.
        /// </summary>
        static void LampBracket(Transform parent, Vector3 hook, Vector3 postOffset, string name)
        {
            var iron = AssetFactory.Lit("Iron", new Color(0.22f, 0.22f, 0.23f), 0.4f, 0.6f);
            var root = BuilderUtils.Group(name, parent).transform;
            var foot = new Vector3(hook.x + postOffset.x, 0f, hook.z + postOffset.z);
            float top = hook.y + 0.1f;
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Post", root, foot + Vector3.up * (top * 0.5f), new Vector3(0.07f, top * 0.5f, 0.07f), iron, false, false);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "PostFoot", root, foot + Vector3.up * 0.03f, new Vector3(0.16f, 0.03f, 0.16f), iron, false, false);
            var arm = new Vector3(hook.x - foot.x, 0f, hook.z - foot.z);
            var mid = new Vector3((hook.x + foot.x) * 0.5f, top - 0.03f, (hook.z + foot.z) * 0.5f);
            var bar = BuilderUtils.Prim(PrimitiveType.Cube, "Arm", root, mid, new Vector3(0.04f, 0.04f, arm.magnitude + 0.04f), iron, false, false);
            bar.transform.localRotation = Quaternion.LookRotation(arm.normalized, Vector3.up);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Hook", root, hook + Vector3.up * 0.04f, new Vector3(0.03f, 0.05f, 0.03f), iron, false, false);
            BuilderUtils.SetRenderingLayer(root.gameObject, WorldRenderingLayers.Exterior);
        }
    }
}
