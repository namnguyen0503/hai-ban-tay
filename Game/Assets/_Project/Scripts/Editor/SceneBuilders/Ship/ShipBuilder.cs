using System;
using System.Collections.Generic;
using System.Linq;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Performance;
using HaiBanTay.Ship;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace HaiBanTay.Editor.SceneBuilders.Ship
{
    /// <summary>Thông số dựng tàu cho từng cảnh.</summary>
    [Serializable]
    public struct ShipConfig
    {
        /// <summary>Gốc tàu (mặt nước giữa tàu) trong thế giới.</summary>
        public Vector3 position;
        /// <summary>Hướng mũi tàu: 0 = mũi +X.</summary>
        public float yaw;
        public WaterKind water;
        public float hour;
        /// <summary>Giờ tự trôi (giờ game mỗi giây) và mốc dừng; 0 = giờ đứng yên.</summary>
        public float hoursPerSecond;
        public float stopAtHour;
        /// <summary>Cảnh có bộ điều khiển mật độ sương riêng (FogLift).</summary>
        public bool sceneOwnsFog;
        /// <summary>Thế giới lắc ngược khi ShipMotion lắc (tàu đứng yên).</summary>
        public bool rolling;
        public float intensity;
        /// <summary>Thả cầu thang tàu từ mép mở mạn phải xuống tới gangwayEnd (tọa độ thế giới).</summary>
        public bool gangway;
        public Vector3 gangwayEnd;
        /// <summary>Dây chắn ở mép mở cầu thang tàu bật từ đầu cảnh.</summary>
        public bool barrierActive;
        /// <summary>Dựng khối bếp, hành lang, hầm thực phẩm.</summary>
        public bool interior;

        /// <summary>02: khối bếp ở gốc thế giới (tọa độ trạm việc và tuyến NPC giữ nguyên), mũi tàu hướng −X; biển, 4:00.</summary>
        public static ShipConfig Galley => new ShipConfig
        {
            position = new Vector3(-0.2f, -5.6f, -0.85f),
            yaw = 180f,
            water = WaterKind.Sea,
            hour = 4f,
            rolling = true,
            intensity = 0.15f,
            barrierActive = true,
            interior = true,
        };

        /// <summary>01: mặt trước thượng tầng ở x = −10 như cảnh cũ, mũi +X; mạn phải (−Z) cập kè; sông, trưa, neo không lắc.</summary>
        public static ShipConfig NhaRong => new ShipConfig
        {
            position = new Vector3(-14.964f, -5.6f, 0f),
            water = WaterKind.River,
            hour = 12f,
            rolling = true,
            intensity = 0f,
            gangway = true,
            gangwayEnd = new Vector3(-1.5f, -4.1f, -13.5f),
            interior = true,
        };

        /// <summary>03: lối cầu thang tàu ở x ∈ [−1; 1] như cảnh cũ (mép mở x 12,464…14,464 của model); cảng, rạng sáng 5:30 → 6:00.</summary>
        public static ShipConfig Marseille => new ShipConfig
        {
            position = new Vector3(-13.464f, -5.6f, 4.17f),
            water = WaterKind.Harbor,
            hour = 5.5f,
            hoursPerSecond = 0.5f / 150f,
            stopAtHour = 6f,
            sceneOwnsFog = true,
            rolling = false,
            intensity = 0f,
            gangway = true,
            gangwayEnd = new Vector3(0f, -2.5f, -9.2f),
            barrierActive = true,
            interior = true,
        };
    }

    /// <summary>
    /// Một con tàu Amiral Latouche-Tréville cho cả ba cảnh (M10b): model v4 LOD0/1/2, va chạm đi lại theo deck_zones.json,
    /// khối bếp và hầm thực phẩm (ShipInterior), thế giới riêng (WorldBuilder) lắc ngược quanh trục dọc tàu,
    /// cầu thang tàu và dây chắn, khói ống bếp, light probe trên boong.
    /// </summary>
    public static class ShipBuilder
    {
        public const string PrefabFolder = "Assets/_Project/Prefabs/Art/";
        public static readonly string[] LodPrefabs = { "Ship", "Ship_LOD1", "Ship_LOD2" };
        /// <summary>Ngưỡng chiều cao trên màn hình của từng mức LOD (theo bounds từng object).</summary>
        static readonly float[] LodHeights = { 0.25f, 0.06f, 0.006f };
        /// <summary>Mép mở cầu thang tàu (deck_zones fore_well, cạnh "Marseille gangway"): Blender x 12,464…14,464, y −7,38.</summary>
        public static readonly Vector3 GangwayGap = ShipCoordinates.FromBlender(new Vector3(13.464f, -7.38f, 5.6f));
        public const float GangwayGapWidth = 2f;
        /// <summary>
        /// Ống bếp GalleyChimney của model (Blender x 2,4; y −3,6) có chụp ở z 11,87, nằm lọt trong buồng lái (bridge_house, mái
        /// z 13,69; validation.json: "Cap remains inside existing bridge volume"). Unity nối thêm một đoạn ống sắt xuyên mái buồng
        /// lái tới z 14,4 và nhả khói ở đó, để khói không sinh ra trong buồng lái.
        /// </summary>
        public static readonly Vector3 ChimneyTop = ShipCoordinates.FromBlender(new Vector3(2.4f, -3.6f, 14.4f));
        const float ChimneyRoof = 13.55f;
        const float ChimneyRadius = 0.2f;

        public static Material Planks => AssetFactory.Lit("DeckPlanks", new Color(0.52f, 0.4f, 0.28f), 0.2f);
        public static Material Rail => AssetFactory.Lit("Rail", new Color(0.35f, 0.25f, 0.16f), 0.3f);
        public static Material Iron => AssetFactory.Lit("Iron", new Color(0.22f, 0.22f, 0.23f), 0.4f, 0.6f);

        public static ShipLayout Build(Transform parent, ShipConfig cfg)
        {
            var data = DeckZones.Read();
            var ship = BuilderUtils.Group("Ship", parent).transform;
            // Đặt gốc tàu trước: mọi phần dựng sau theo tọa độ cục bộ, các điểm thế giới (cầu thang tàu) tính đúng ngay.
            ship.localPosition = cfg.position;
            ship.localRotation = Quaternion.Euler(0f, cfg.yaw, 0f);
            var layout = ship.gameObject.AddComponent<ShipLayout>();

            BuildModel(ship);
            ShipWalkable.Build(ship, data);
            layout.stairs = ShipWalkable.BuildStairs(ship, data, Planks, Rail);
            if (cfg.interior)
                ShipInterior.Build(layout, data);

            layout.motion = BuilderUtils.Group("ShipMotion", ship).AddComponent<ShipMotion>();
            layout.motion.intensity = cfg.intensity;
            // Gia tốc ngang (LateralAccel = trục +X của ShipMotion) theo phương ngang tàu: lắc dương làm đồ trượt về +Z của tàu.
            layout.motion.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            // Trục lắc dọc tàu (+X của gốc tàu) qua mặt nước giữa tàu.
            var pivot = BuilderUtils.Group("RollPivot", ship).transform;
            pivot.localRotation = Quaternion.Euler(0f, 90f, 0f);

            var world = WorldConfig.Sea(cfg.hour);
            world.water = cfg.water;
            world.surfaceHeight = ship.position.y;
            world.ship = layout.motion;
            world.rollPivot = pivot;
            layout.world = WorldBuilder.Build(parent, world);
            layout.world.GetComponent<WorldMotion>().enabled = cfg.rolling;
            var time = layout.world.GetComponent<TimeOfDay>();
            time.hoursPerSecond = cfg.hoursPerSecond;
            time.stopAtHour = cfg.hoursPerSecond > 0f ? cfg.stopAtHour : 24f;
            time.driveFogDensity = !cfg.sceneOwnsFog;

            layout.gangwayBarrier = BuildGangwayBarrier(ship);
            layout.gangwayBarrier.SetActive(cfg.barrierActive);
            if (cfg.gangway)
                layout.gangway = BuildGangway(layout.world, ship.TransformPoint(GangwayGap), cfg.gangwayEnd);
            BuildSmoke(ship);
            BuildDeckProbes(ship, data);
            return layout;
        }

        /// <summary>
        /// Model LOD0/1/2: mỗi object (Hull, Superstructure, GalleyChimney…) một LODGroup gồm ba mức cùng tên, để phần
        /// xa tàu tự hạ mức. Không bake lightmap (không Contribute GI), lấy ambient của TimeOfDay; lớp Exterior; tĩnh cho
        /// occlusion culling. Object có kính (thượng tầng, buồng lái) không làm vật che: kính trong suốt không được che cảnh.
        /// </summary>
        public static GameObject BuildModel(Transform ship)
        {
            var model = BuilderUtils.Group("ShipModel", ship);
            var byName = new SortedDictionary<string, List<Renderer>[]>(StringComparer.Ordinal);
            var instances = new List<GameObject>();
            for (int lod = 0; lod < LodPrefabs.Length; lod++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + LodPrefabs[lod] + ".prefab");
                if (prefab == null)
                    throw new InvalidOperationException("Thiếu prefab tàu " + LodPrefabs[lod] + "; chạy ArtImporter.RunShip.");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, model.transform);
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                // Importer FBX quay mũi về −X, port về −Z: xoay đúng một lần, gốc mặt nước giữ nguyên.
                instance.transform.localRotation = ShipCoordinates.ModelRotation * instance.transform.localRotation;
                instances.Add(instance);
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (!byName.TryGetValue(renderer.name, out var levels))
                    {
                        levels = new[] { new List<Renderer>(), new List<Renderer>(), new List<Renderer>() };
                        byName.Add(renderer.name, levels);
                    }
                    levels[lod].Add(renderer);
                }
            }

            foreach (var pair in byName)
            {
                var group = BuilderUtils.Group(pair.Key, model.transform);
                var lods = new LOD[LodPrefabs.Length];
                for (int lod = 0; lod < lods.Length; lod++)
                {
                    foreach (var renderer in pair.Value[lod])
                    {
                        renderer.transform.SetParent(group.transform, true);
                        renderer.gameObject.name = pair.Key + "_LOD" + lod;
                        ConfigureModelRenderer(renderer);
                    }
                    lods[lod] = new LOD(LodHeights[lod], pair.Value[lod].ToArray());
                }
                var lodGroup = group.AddComponent<LODGroup>();
                lodGroup.SetLODs(lods);
                lodGroup.RecalculateBounds();
            }
            foreach (var instance in instances)
                UnityEngine.Object.DestroyImmediate(instance);
            return model;
        }

        static void ConfigureModelRenderer(Renderer renderer)
        {
            // Sơn đỏ bên trong chụp phải đọc thành hốc tối, tránh đĩa đỏ sáng như vật treo ngoài miệng.
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] != null && materials[i].name == "VentInside")
                    materials[i] = AssetFactory.Lit("ShipVentInside", new Color(0.055f, 0.016f, 0.011f), 0.08f);
            renderer.sharedMaterials = materials;
            bool glass = renderer.sharedMaterials.Any(m => m != null && m.renderQueue >= (int)RenderQueue.GeometryLast);
            var flags = StaticEditorFlags.OccludeeStatic;
            if (!glass)
                flags |= StaticEditorFlags.OccluderStatic;
            GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, flags);
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.renderingLayerMask = WorldRenderingLayers.Exterior;
        }

        /// <summary>Dây chắn ở mép mở mạn phải: hai thanh gỗ và xích, collider kín cao 1,2 m. Tắt khi thả cầu thang tàu.</summary>
        static GameObject BuildGangwayBarrier(Transform ship)
        {
            var barrier = BuilderUtils.Group("GangwayBarrier", ship);
            barrier.transform.localPosition = GangwayGap;
            float half = GangwayGapWidth * 0.5f;
            foreach (float x in new[] { -half + 0.04f, half - 0.04f })
                BuilderUtils.Block("Stanchion", barrier.transform, new Vector3(x, 0.55f, 0f), new Vector3(0.06f, 1.1f, 0.06f), Iron, false, false);
            BuilderUtils.Block("TopBar", barrier.transform, new Vector3(0f, 1.02f, 0f), new Vector3(GangwayGapWidth, 0.06f, 0.06f), Rail, false, false);
            // Xích võng giữa hai trụ: chuỗi mắt xích nhỏ.
            const int links = 14;
            for (int i = 0; i < links; i++)
            {
                float t = (i + 0.5f) / links;
                float x = Mathf.Lerp(-half + 0.08f, half - 0.08f, t);
                float y = 0.62f - 0.12f * Mathf.Sin(t * Mathf.PI);
                BuilderUtils.Prim(PrimitiveType.Capsule, "ChainLink", barrier.transform, new Vector3(x, y, 0f), new Vector3(0.03f, 0.065f, 0.03f),
                    Iron, false, false, new Vector3(0f, 0f, 90f));
            }
            var box = barrier.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.6f, 0f);
            box.size = new Vector3(GangwayGapWidth, 1.2f, 0.12f);
            BuilderUtils.SetRenderingLayer(barrier, WorldRenderingLayers.Exterior);
            return barrier;
        }

        /// <summary>
        /// Cầu thang tàu từ mép mở trên boong (top) xuống kè/cầu cảng (bottom), cả hai theo tọa độ thế giới. Đặt dưới
        /// WorldRoot để đi theo kè. Bậc gỗ nhìn thấy, dốc ẩn đi được, ván thành và tay vịn có collider.
        /// </summary>
        static Transform BuildGangway(Transform world, Vector3 top, Vector3 bottom)
        {
            var gangway = BuilderUtils.Group("Gangway", world).transform;
            var a = world.InverseTransformPoint(top);
            var b = world.InverseTransformPoint(bottom);
            const float width = 1.4f;
            var ramp = ShipWalkable.Ramp(gangway, "GangwayRamp", a, b, width);
            float length = Vector3.Distance(a, b);
            int steps = Mathf.Max(6, Mathf.CeilToInt(length / 0.32f));
            var run = (b - a) / steps;
            for (int i = 0; i < steps; i++)
            {
                var center = a + run * (i + 0.5f);
                var step = BuilderUtils.Block("Step_" + i, gangway, center + Vector3.down * 0.03f,
                    new Vector3(width, 0.05f, new Vector2(run.x, run.z).magnitude * 0.92f), Planks, false, false);
                step.transform.localRotation = Quaternion.LookRotation(new Vector3(run.x, 0f, run.z));
            }
            foreach (float side in new[] { -1f, 1f })
            {
                var offset = ramp.transform.localRotation * new Vector3(side * (width * 0.5f + 0.04f), 0f, 0f);
                ShipWalkable.Beam(gangway, "GangwayStringer", a + offset + Vector3.down * 0.06f, b + offset + Vector3.down * 0.06f, 0.08f, Planks);
                ShipWalkable.Beam(gangway, "GangwayHandrail", a + offset + Vector3.up * 0.95f, b + offset + Vector3.up * 0.95f, 0.05f, Rail);
                for (int i = 0; i <= 4; i++)
                {
                    var foot = Vector3.Lerp(a, b, i / 4f) + offset;
                    BuilderUtils.Block("GangwayPost", gangway, foot + Vector3.up * 0.47f, new Vector3(0.05f, 0.95f, 0.05f), Rail, false, false);
                }
                // Thành chặn rơi khỏi mép thang (ẩn, theo mặt dốc).
                var guard = BuilderUtils.Group("GangwaySide", ramp.transform);
                guard.transform.localPosition = new Vector3(side * (width * 0.5f + 0.05f), 0.6f, 0f);
                guard.AddComponent<BoxCollider>().size = new Vector3(0.1f, 1.2f, length);
            }
            // Thuộc thế giới (có thể lắc, thu lên): không tĩnh, không bake; sáng theo mặt trời và probe.
            BuilderUtils.ClearStatic(gangway.gameObject);
            BuilderUtils.SetRenderingLayer(gangway.gameObject, WorldRenderingLayers.Exterior);
            return gangway;
        }

        /// <summary>Khói ở đỉnh ống bếp (GalleyChimney): hạt mờ bay lên, dạt theo gió; mật độ do GalleySmoke điều khiển.</summary>
        static void BuildSmoke(Transform ship)
        {
            // Đoạn ống nối từ dưới mái buồng lái lên tới miệng nhả khói.
            float pipeHeight = ChimneyTop.y - ChimneyRoof;
            var pipe = BuilderUtils.Prim(PrimitiveType.Cylinder, "GalleyChimneyExtension", ship,
                new Vector3(ChimneyTop.x, ChimneyRoof + pipeHeight * 0.5f, ChimneyTop.z), new Vector3(ChimneyRadius * 2f, pipeHeight * 0.5f, ChimneyRadius * 2f),
                Iron, true, false);
            GameObjectUtility.SetStaticEditorFlags(pipe, StaticEditorFlags.OccludeeStatic);
            pipe.GetComponent<Renderer>().renderingLayerMask = WorldRenderingLayers.Exterior;
            BuilderUtils.Prim(PrimitiveType.Cylinder, "GalleyChimneyCap", pipe.transform, new Vector3(0f, 1f, 0f), new Vector3(1.3f, 0.06f, 1.3f),
                Iron, true, false).GetComponent<Renderer>().renderingLayerMask = WorldRenderingLayers.Exterior;
            GameObjectUtility.SetStaticEditorFlags(pipe.transform.GetChild(0).gameObject, StaticEditorFlags.OccludeeStatic);

            var root = BuilderUtils.Group("GalleySmoke", ship);
            root.transform.localPosition = ChimneyTop;
            root.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var particles = root.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = 5f;
            main.startSpeed = 0.7f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startColor = new Color(0.2f, 0.19f, 0.18f, 0.6f);
            main.maxParticles = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = particles.emission;
            emission.rateOverTime = 1f;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.15f;
            var velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0.35f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.25f);
            velocity.z = new ParticleSystem.MinMaxCurve(0.1f);
            var color = particles.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 3f));
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat")
                ?? AssetFactory.Transparent("ChimneySmoke", new Color(0.35f, 0.34f, 0.3f, 0.3f));
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            BuilderUtils.SetRenderingLayer(root, WorldRenderingLayers.Exterior);
            var smoke = root.AddComponent<GalleySmoke>();
            smoke.smoke = particles;
        }

        /// <summary>
        /// Light probe ngoài trời: lưới thưa trên mọi đa giác đi được (ngang thắt lưng và ngang đầu), không đặt điểm nào
        /// trong khối bếp hay hầm; NPC, bao, đồ cầm theo trên boong lấy sáng từ đây.
        /// </summary>
        static void BuildDeckProbes(Transform ship, JObject data)
        {
            const float spacing = 4f;
            var points = new List<Vector3>();
            foreach (var pair in DeckZones.Zones(data))
            {
                if ((string)pair.Value["geometry_owner"] == "Unity")
                    continue;
                var xr = pair.Value["x_range"];
                var yr = pair.Value["y_range"];
                for (float x = (float)xr[0] + spacing * 0.5f; x < (float)xr[1]; x += spacing)
                    for (float y = (float)yr[0] + 1f; y < (float)yr[1]; y += spacing * 0.75f)
                    {
                        var p = new Vector2(x, y);
                        if (!DeckZones.OnWalkable(pair.Value, p))
                            continue;
                        float surface = DeckZones.Surface(pair.Value, x);
                        points.Add(new Vector3(x, surface + 0.9f, y));
                        points.Add(new Vector3(x, surface + 2.2f, y));
                    }
            }
            var go = BuilderUtils.Group("LightProbes_Deck", ship);
            go.AddComponent<LightProbeGroup>().probePositions = points.ToArray();
        }

        /// <summary>
        /// Ánh sáng realtime của cảnh: mặt trời/trăng của WorldRoot là nhóm ngoài; đèn realtime trong khoang (lửa lò, đèn
        /// treo, đèn dầu, theo thứ tự ưu tiên) là nhóm trong. LightBudget đổi bóng theo vùng người chơi đứng. Mọi renderer
        /// và đèn trong khối bếp, hầm vào lớp Interior. Trạng thái lưu trong scene đã đúng ngân sách (đèn trong không bóng).
        /// </summary>
        public static LightBudget ConfigureLights(ShipLayout layout, params Light[] interiorRealtime) =>
            ConfigureLights(layout, null, interiorRealtime);

        /// <summary>
        /// Như trên, thêm đèn bến/đèn đường ngoài trời (exteriorLamps): vào nhóm ngoài sau mặt trời, LightBudget chỉ bật những
        /// đèn gần camera nhất trong số suất còn lại (không vượt 1 đèn có bóng + 2 đèn không bóng).
        /// </summary>
        public static LightBudget ConfigureLights(ShipLayout layout, System.Collections.Generic.IEnumerable<Light> exteriorLamps,
            params Light[] interiorRealtime)
        {
            foreach (var root in new[] { layout.galley, layout.foodHold })
                if (root != null)
                {
                    BuilderUtils.SetRenderingLayer(root.gameObject, WorldRenderingLayers.Interior);
                    foreach (var light in root.GetComponentsInChildren<Light>(true))
                        BuilderUtils.SetLightLayer(light, WorldRenderingLayers.Interior);
                }
            // Cánh cửa là mặt ngoài thượng tầng khi nhìn từ boong: nhận cả mặt trời lẫn đèn trong bếp.
            foreach (var door in new[] { layout.galleyDoor, layout.corridorDoor })
                if (door != null && door.hinge != null)
                    BuilderUtils.SetRenderingLayer(door.hinge.gameObject, WorldRenderingLayers.Interior | WorldRenderingLayers.Exterior);
            var sun = layout.world.GetComponent<TimeOfDay>().mainLight;
            RenderSettings.sun = sun;
            var budget = layout.gameObject.AddComponent<LightBudget>();
            var outside = new System.Collections.Generic.List<Light> { sun };
            if (exteriorLamps != null)
                outside.AddRange(exteriorLamps);
            budget.exterior = outside.ToArray();
            budget.sortExteriorByDistance = outside.Count > 2;
            budget.exteriorPinned = 1;
            budget.interior = interiorRealtime.Where(l => l != null).ToArray();
            foreach (var light in budget.interior)
                light.shadows = LightShadows.None;
            return budget;
        }

        /// <summary>Mọi thứ dựng dưới WorldRoot (phông xa, kè, phố) thuộc lớp Exterior; gọi sau khi dựng xong cảnh.</summary>
        public static void FinishWorld(ShipLayout layout)
        {
            BuilderUtils.SetRenderingLayer(layout.world.gameObject, WorldRenderingLayers.Exterior);
        }
    }
}
