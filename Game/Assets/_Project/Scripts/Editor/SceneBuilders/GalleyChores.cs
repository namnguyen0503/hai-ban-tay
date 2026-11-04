using HaiBanTay.Editor.Setup;
using HaiBanTay.Interaction;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Ship;
using HaiBanTay.Tasks;
using UnityEngine;
using Mats = HaiBanTay.Editor.SceneBuilders.Ship.ShipInterior.Mats;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>
    /// 02_Galley, việc thêm ở M9: cào lò và đổ tro xuống máng (hầm than), quét bếp, nhặt rau (sọt rau ở hầm thực phẩm),
    /// kéo chảo đồng, sọt than, giữ thăng bằng khi vác bao trên thang hầm. Các phòng (phòng rửa, hầm than, ô chuyển món,
    /// biển tên tàu, hầm thực phẩm) dựng chung ở ShipInterior.
    /// </summary>
    public static partial class GalleySceneBuilder
    {
        /// <summary>Vùng mang xô tro, sọt than (theo thế giới; khối bếp của 02 nằm ở gốc thế giới).</summary>
        static readonly Bounds CarryArea = new Bounds(new Vector3(2.5f, 1f, -1f), new Vector3(16f, 6f, 10f));

        /// <summary>Dụng cụ cán dài treo móc (cào lò, chổi). Gốc ở chân tường; dụng cụ dựng đứng, đầu dụng cụ ở dưới.</summary>
        static ToolHook BuildToolHook(Transform env, string name, Vector3 position, float yaw, Mats m, Tool tool, string slot,
            System.Func<Transform, GameObject> visual)
        {
            var root = new GameObject(name);
            root.transform.SetParent(env, false);
            root.transform.localPosition = position;
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            BuilderUtils.Block("Hook", root.transform, new Vector3(0f, 1.36f, 0.06f), new Vector3(0.05f, 0.05f, 0.08f), m.Iron, true, false);
            var art = BuilderUtils.ArtSlot(slot, root.transform, Vector3.zero, Vector3.zero, visual);
            BuilderUtils.ClearStatic(art);
            root.layer = BuilderUtils.Layer("Interactable");
            root.tag = "Interactable";
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.8f, 0f);
            col.size = new Vector3(0.4f, 1.3f, 0.3f);
            var hook = root.AddComponent<ToolHook>();
            hook.tool = tool;
            hook.visual = art;
            string key = tool == Tool.Rake ? "rake" : "broom";
            hook.takeKey = "prompt.take_" + key;
            hook.hangKey = "prompt.hang_" + key;
            return hook;
        }

        static GameObject RakeVisual(Transform t, Mats m)
        {
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Handle", t, new Vector3(0f, 0.8f, 0f), new Vector3(0.03f, 0.6f, 0.03f), m.Iron, false, false);
            BuilderUtils.Block("Blade", t, new Vector3(0f, 0.17f, 0.03f), new Vector3(0.2f, 0.1f, 0.012f), m.Iron, false, false);
            return t.gameObject;
        }

        static GameObject BroomVisual(Transform t, Mats m)
        {
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Handle", t, new Vector3(0f, 0.85f, 0f), new Vector3(0.03f, 0.55f, 0.03f), m.Wood, false, false);
            BuilderUtils.Block("Bristles", t, new Vector3(0f, 0.16f, 0f), new Vector3(0.28f, 0.3f, 0.07f), m.Bristle, false, false);
            return t.gameObject;
        }

        static AshStation BuildAsh(Transform env, Transform tasks, Mats m, CoalStation coal, PlayerArea hold)
        {
            var task = new GameObject("AshStation").AddComponent<AshStation>();
            task.transform.SetParent(tasks, false);
            task.coal = coal;
            coal.ashStation = task;

            for (int i = 0; i < coal.cells.Length; i++)
            {
                var cell = coal.cells[i];
                var door = cell.ashDoorFrame;
                var col = door.gameObject.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.size = new Vector3(0.62f / door.localScale.x, 0.22f / door.localScale.y, 0.05f / door.localScale.z);
                cell.ashDoor = AddTarget(door.gameObject, task, "door" + i);
                var outward = door.position - cell.hob.position;
                outward.y = 0f;
                var spot = BuilderUtils.Group(i == 0 ? "AshBucketSpot" : "AshBucketSpot_" + i, env);
                var p = env.InverseTransformPoint(door.position + outward.normalized * 0.34f);
                p.y = 0f;
                spot.transform.localPosition = p;
                var spotCol = spot.AddComponent<BoxCollider>();
                spotCol.isTrigger = true;
                spotCol.center = new Vector3(0f, 0.2f, 0f);
                spotCol.size = new Vector3(0.4f, 0.4f, 0.4f);
                cell.bucketSpot = AddTarget(spot, task, "spot" + i);
            }
            task.door = coal.cells[0].ashDoor;
            task.spot = coal.cells[0].bucketSpot;

            // Móc cào trên tường bắc, giữa cánh cửa bếp khi mở (áp tường bắc tới x −4,18) và bếp lò A (từ x −3,63).
            task.hook = BuildToolHook(env, "RakeHook", new Vector3(-3.85f, 0f, 2.9f), 0f, m, Tool.Rake, "Rake", t => RakeVisual(t, m));

            // Chỗ cất xô trong hầm than, giữa thùng gỗ cạnh cửa và máng tro trên vách tây.
            var store = BuilderUtils.Group("AshBucketStore", hold.transform.parent);
            var storePos = new Vector3(0.12f, 0f, -4.14f);
            store.transform.localPosition = storePos;
            var storeCol = store.AddComponent<BoxCollider>();
            storeCol.isTrigger = true;
            storeCol.center = new Vector3(0f, 0.2f, 0f);
            storeCol.size = new Vector3(0.4f, 0.4f, 0.4f);
            task.store = AddTarget(store, task, "store");
            var storeWorld = store.transform.position;

            var bucket = BuilderUtils.ArtSlot("WoodenBucket", env, env.InverseTransformPoint(storeWorld), Vector3.zero, t =>
            {
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Body", t, new Vector3(0f, 0.18f, 0f), new Vector3(0.32f, 0.18f, 0.32f), m.Wood, false, false);
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Ash", t, new Vector3(0f, 0.33f, 0f), new Vector3(0.28f, 0.01f, 0.28f), m.Ash, false, false);
                return t.gameObject;
            });
            bucket.name = "AshBucket";
            BuilderUtils.ClearStatic(bucket);
            bucket.layer = BuilderUtils.Layer("Holdable");
            bucket.tag = "Holdable";
            var bucketCol = bucket.AddComponent<BoxCollider>();
            // Cao 0,55 m (xô thật 0,36): Ba leo được lên hộp cao 0,36 và cả 0,44 m (bot ngày 1 và 5, y 0,19–0,27 ở lối lấy chổi; test
            // AshBucketClimbPlayTests nâng Ba tới 0,91 m), hộp 0,55 m thì không.
            bucketCol.center = new Vector3(0f, 0.275f, 0f);
            bucketCol.size = new Vector3(0.34f, 0.55f, 0.34f);
            var rb = bucket.AddComponent<Rigidbody>();
            rb.mass = 6f;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var h = bucket.AddComponent<Holdable>();
            h.kind = "ash_bucket";
            h.hang = true;
            h.hangGrip = HangGrip(bucket.transform, "AshBucketBail");
            var back = bucket.AddComponent<ReturnToStart>();
            back.useArea = true;
            back.area = CarryArea;
            task.bucket = h;
            var ash = bucket.transform.Find("Ash");
            // Xô thật thấp hơn khối xám (quai xách tính vào chiều cao khi khớp): đặt lớp tro theo vành xô; xô thon dần
            // xuống đáy nên đĩa nhỏ hơn miệng để khi tro vơi (AshStation hạ đĩa) không lòi ra thành xô.
            BuilderUtils.FitDiscToRim(bucket.transform, ash, 0.03f, 0.72f);
            BuilderUtils.PourRimMarkers(bucket.transform);
            task.bucketAsh = ash;
            task.ashFull = BuilderUtils.Group("AshFull", bucket.transform).transform;
            task.ashFull.localPosition = ash.localPosition;
            task.ashBottom = BuilderUtils.Group("AshBottom", bucket.transform).transform;
            task.ashBottom.localPosition = new Vector3(ash.localPosition.x, 0.035f, ash.localPosition.z);
            if (ash != null)
                BuilderUtils.ClearStatic(ash.gameObject);

            // Máng tro trên vách tây hầm than (đổ tro ra biển).
            var chute = BuilderUtils.Group("AshChute", hold.transform.parent);
            chute.transform.localPosition = new Vector3(-0.1f, 0f, -4.6f);
            BuilderUtils.Block("Hopper", chute.transform, new Vector3(0.08f, 0.85f, 0f), new Vector3(0.24f, 0.5f, 0.6f), m.Iron);
            BuilderUtils.Block("Mouth", chute.transform, new Vector3(0.205f, 0.98f, 0f), new Vector3(0.01f, 0.2f, 0.46f), m.Shadow, true, false);
            BuilderUtils.Block("Lid", chute.transform, new Vector3(0.22f, 1.14f, 0f), new Vector3(0.04f, 0.06f, 0.6f), m.Iron, true, false);
            task.chute = AddTarget(chute, task, "chute");
            var chuteCol = chute.AddComponent<BoxCollider>();
            // Vùng nhắm rộng hơn cái máng một chút (tay đang xách xô, khó nhắm trúng miệng máng nhỏ) nhưng không lấn
            // sang chỗ cất xô ngay phía bắc.
            chuteCol.center = new Vector3(0.2f, 0.9f, 0f);
            chuteCol.size = new Vector3(0.5f, 1.0f, 0.7f);
            return task;
        }

        /// <summary>Chỗ bàn tay nắm khi xách: giữa, trên đỉnh quai (đỉnh khối hình của vật, bỏ lớp tro/than bên trong).</summary>
        internal static Transform HangGrip(Transform item, string name)
        {
            var bounds = new Bounds(item.position, Vector3.zero);
            bool any = false;
            foreach (var r in item.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "Ash" || r.name == "Load" || r is ParticleSystemRenderer)
                    continue;
                if (any) bounds.Encapsulate(r.bounds);
                else { bounds = r.bounds; any = true; }
            }
            var grip = new GameObject(name).transform;
            grip.SetParent(item, false);
            grip.position = new Vector3(item.position.x, any ? bounds.max.y - 0.015f : item.position.y + 0.4f, item.position.z);
            // Bàn tay úp, ngón quặp qua quai; cổ tay thõng theo cánh tay buông.
            grip.localRotation = Quaternion.Euler(82f, -8f, 8f);
            grip.position -= grip.rotation * new Vector3(0f, -HandsRig.GripBelow, HandsRig.GripForward);
            return grip;
        }

        static readonly Vector3[] SweepSpots =
        {
            // Vết thứ ba trên sàn trống phía nam bàn; cả vết và chỗ đứng quét cách bàn/lò đủ cho người thật.
            new Vector3(-2.2f, 0f, -0.9f), new Vector3(1.6f, 0f, 1.5f), new Vector3(-0.5f, 0f, -1.75f), new Vector3(2.3f, 0f, -1.2f),
            new Vector3(-3.7f, 0f, 0.1f), new Vector3(0.6f, 0f, -1.7f), new Vector3(2.9f, 0f, 1.7f), new Vector3(-1.9f, 0f, 1.1f),
        };

        static SweepStation BuildSweep(Transform env, Transform tasks, Mats m)
        {
            var task = new GameObject("SweepStation").AddComponent<SweepStation>();
            task.transform.SetParent(tasks, false);
            // Móc chổi trên mặt bếp của vách ngăn bếp–phòng rửa (mặt vách ở x 3,25), cách vách 7 cm.
            task.hook = BuildToolHook(env, "BroomHook", new Vector3(3.18f, 0f, -1.3f), -90f, m, Tool.Broom, "Broom", t => BroomVisual(t, m));
            var dirt = AssetFactory.FloorGrime();
            var targets = new TaskTarget[SweepSpots.Length];
            var renderers = new Renderer[SweepSpots.Length];
            for (int i = 0; i < SweepSpots.Length; i++)
            {
                var spot = new GameObject("SweepSpot_" + i);
                spot.transform.SetParent(env, false);
                spot.transform.localPosition = SweepSpots[i];
                spot.transform.localRotation = Quaternion.Euler(0f, 53f * i, 0f);
                // Vệt loang nằm phẳng trên sàn (quad xoay úp xuống sàn), kích thước hơi khác nhau từng vệt.
                float k = 0.85f + 0.1f * (i % 3);
                var mark = BuilderUtils.Prim(PrimitiveType.Quad, "Dirt", spot.transform, new Vector3(0f, 0.004f, 0f),
                    new Vector3(1.3f * k, 1.05f * k, 1f), dirt, false, false, new Vector3(90f, 0f, 0f));
                var mr = mark.GetComponent<Renderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderers[i] = mr;
                var col = spot.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.center = new Vector3(0f, 0.1f, 0f);
                col.size = new Vector3(0.65f, 0.2f, 0.5f);
                targets[i] = AddTarget(spot, task, "spot" + i);
            }
            task.spots = targets;
            task.spotRenderers = renderers;
            return task;
        }

        /// <summary>Nhặt rau ở nửa tây bàn sơ chế: sọt rau ở hầm thực phẩm, thớt, rổ lá tốt trên bàn, xô lá úa dưới đất.</summary>
        static GreensStation BuildGreens(Transform env, Transform hold, Transform tasks, Mats m)
        {
            var task = new GameObject("GreensStation").AddComponent<GreensStation>();
            task.transform.SetParent(tasks, false);

            var board = BuilderUtils.Group("GreensBoard", env);
            // Đầu tây bàn sơ chế: thớt nửa nam, sọt rau mang từ hầm lên đặt nửa bắc (rawTablePoint), cả hai nằm trên mặt bàn.
            board.transform.localPosition = new Vector3(-0.55f, 0.9f, -0.22f);
            BuilderUtils.Block("Board", board.transform, new Vector3(0f, 0.012f, 0f), new Vector3(0.56f, 0.024f, 0.36f), m.CuttingBoard, true, false);
            var leaves = new Transform[5];
            for (int i = 0; i < leaves.Length; i++)
            {
                var leaf = BuilderUtils.Group("Leaf_" + i, board.transform);
                leaf.transform.localPosition = new Vector3(-0.2f + 0.1f * i, 0.032f, 0.02f * (i % 2));
                leaf.transform.localRotation = Quaternion.Euler(0f, -10f + 5f * i, 0f);
                ChoreProps.Leaf(leaf.transform, "Blade", m.Leaf);
                leaves[i] = leaf.transform;
            }
            task.leaves = leaves;
            task.board = AddTarget(board, task, "board");
            var boardCol = board.AddComponent<BoxCollider>();
            boardCol.center = new Vector3(0f, 0.05f, 0f);
            boardCol.size = new Vector3(0.6f, 0.12f, 0.42f);

            // Rổ lá tốt sát mép đông thớt rau; đầu đông bàn để chỗ đặt khay phục vụ/cốc cà phê và đĩa vỏ của ông Mai (QA-0040).
            var basket = BuilderUtils.ArtSlot("WickerBasket", env, new Vector3(0.02f, 0.9f, 0.1f), Vector3.zero, t =>
            {
                BuilderUtils.Block("Body", t, new Vector3(0f, 0.05f, 0f), new Vector3(0.38f, 0.1f, 0.2f), m.Wood, true, false);
                return t.gameObject;
            });
            task.basket = new GameObject("BasketDrop").transform;
            task.basket.SetParent(basket.transform, false);
            task.basket.localPosition = new Vector3(0f, 0.08f, 0f);

            // Thùng lá úa nép đầu bàn, không nằm trên lối kéo chảo từ phòng rửa tới trước lò.
            var bin = BuilderUtils.ArtSlot("WasteBucket", env, new Vector3(-1.35f, 0f, 0.15f), Vector3.zero, t =>
            {
                // Cao 0,56 m với hộp va chạm vuông: capsule người chơi leo được vật thấp hơn ~0,45 m, thùng 0,40 m với collider tròn bị Ba
                // bước lên mép (QA-0125).
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Body", t, new Vector3(0f, 0.28f, 0f), new Vector3(0.34f, 0.28f, 0.34f), m.Iron, true, false);
                var box = t.gameObject.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.28f, 0f);
                box.size = new Vector3(0.34f, 0.56f, 0.34f);
                return t.gameObject;
            });
            task.bin = new GameObject("BinDrop").transform;
            task.bin.SetParent(bin.transform, false);
            task.bin.localPosition = new Vector3(0f, 0.52f, 0f);

            // Sọt rau ở hầm thực phẩm (S16: xuống hầm lấy rau), cạnh chân thang.
            var crate = BuilderUtils.Group("GreensCrate", hold);
            crate.transform.localPosition = new Vector3(10.2f, 2.8f, 1.8f);
            BuilderUtils.ArtSlot("WickerBasket", crate.transform, Vector3.zero, Vector3.zero, t =>
            {
                BuilderUtils.Block("Bottom", t, new Vector3(0f, 0.14f, 0f), new Vector3(0.5f, 0.28f, 0.4f), m.Wicker, true, false);
                return t.gameObject;
            });
            var crateCollider = crate.AddComponent<BoxCollider>();
            crateCollider.center = new Vector3(0f, 0.22f, 0f); crateCollider.size = new Vector3(0.55f, 0.45f, 0.45f);
            var bunches = new GameObject[6];
            for (int i = 0; i < bunches.Length; i++)
            {
                bunches[i] = ChoreProps.Bunch(crate.transform, "Bunch_" + i, m.Leaf);
                bunches[i].transform.localPosition = new Vector3(-0.14f + 0.14f * (i % 3), 0.2f + 0.04f * (i / 3), -0.12f + 0.14f * (i / 3));
            }
            task.crateBunches = bunches;
            BuilderUtils.ClearStatic(crate);
            BuilderUtils.ClearStatic(basket);
            task.crate = AddTarget(crate, task, "crate");
            task.rawBasket = crate.transform;
            task.cleanBasket = basket.transform;
            task.rawTablePoint = new GameObject("RawBasketTablePoint").transform;
            task.rawTablePoint.SetParent(env, false);
            task.rawTablePoint.localPosition = new Vector3(-0.72f, 0.9f, 0.2f);
            var cleanCollider = basket.AddComponent<BoxCollider>();
            cleanCollider.center = new Vector3(0f, 0.1f, 0f); cleanCollider.size = new Vector3(0.45f, 0.25f, 0.3f);
            task.cleanTarget = AddTarget(basket, task, "cleanBasket");
            var sink = BuilderUtils.Group("GreensWashTarget", env);
            sink.transform.localPosition = new Vector3(6.62f, 0.89f + WashStation.BasinLift, -1.6f);
            var sinkCollider = sink.AddComponent<BoxCollider>();
            sinkCollider.isTrigger = true; sinkCollider.size = new Vector3(0.6f, 0.25f, 1.05f);
            sinkCollider.enabled = false;
            task.sink = AddTarget(sink, task, "greensSink");
            var kept = new Transform[30];
            for (int i = 0; i < kept.Length; i++)
            {
                kept[i] = ChoreProps.Leaf(basket.transform, "KeptLeaf_" + i, m.Leaf).transform;
                kept[i].localPosition = new Vector3(-0.14f + i % 4 * 0.07f, 0.12f + i / 4 * 0.01f, -0.09f);
                kept[i].localRotation = Quaternion.Euler(0f, -25f + i % 5 * 12f, 0f);
                kept[i].gameObject.SetActive(false);
            }
            task.keptLeaves = kept;
            return task;
        }

        const string CopperPanFbx = "Assets/_Project/Art/Models/CopperPan/CopperPan.fbx";

        static PanStation BuildPan(Transform env, Transform tasks, Mats m)
        {
            var task = new GameObject("PanStation").AddComponent<PanStation>();
            task.transform.SetParent(tasks, false);
            // [0] trước Stove_B, [1] góc bắc phòng rửa. Tường đông phòng rửa chặn người ở x ≈ 6,6 mà tay kéo dài 0,95 m,
            // nên chảo kéo từ cửa tây chỉ tới x ≈ 5,65: chỗ đặt [1] ở x 5,8 để chảo tới sát tâm và người kéo (x 6,55) vẫn cách tâm
            // ≥ 0,72 m (PanStation.PlayerClear), không đứng trong hộp chảo. Không vẽ vòng phấn dưới sàn: mốc chỉ dẫn trên màn hình chỉ chỗ.
            var stops = new[] { new Vector3(-1.4f, 0f, 1.45f), new Vector3(5.8f, 0f, 0.35f) };
            task.arriveRadius = 0.65f;
            task.stops = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                var stop = new GameObject("PanStop_" + i).transform;
                stop.SetParent(env, false);
                stop.localPosition = stops[i];
                task.stops[i] = stop;
            }

            var pan = BuilderUtils.Group("CopperPan", env);
            pan.transform.localPosition = stops[1];
            // Chảo đồng lớn thật (QA-0146, dựng lại bằng Blender: Tools/blender/make_copper_pan.py): đáy phẳng sát sàn, thành cong loe,
            // vành cuộn, lòng tráng thiếc sâu 11 cm, hai quai sắt rèn tán đinh. Số đo theo PanStation; tên "Body"/"Inside" PanStation dùng.
            var meshes = new System.Collections.Generic.Dictionary<string, Mesh>();
            foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(CopperPanFbx))
                if (asset is Mesh mesh)
                    meshes[mesh.name] = mesh;
            if (!meshes.ContainsKey("Body") || !meshes.ContainsKey("Inside") || !meshes.ContainsKey("Handles"))
                throw new System.InvalidOperationException($"Thiếu mesh chảo trong {CopperPanFbx}; chạy Tools/blender/make_copper_pan.py.");
            // Đơn vị FBX tùy importer: quy về mét theo bán kính vành ngoài.
            float unit = PanStation.RimOuter / meshes["Body"].bounds.max.x;
            var tin = AssetFactory.Lit("PanTin", new Color(0.7f, 0.7f, 0.68f), 0.5f, 0.55f);
            foreach (var (name, mat) in new[] { ("Body", m.Copper), ("Inside", tin), ("Handles", m.Iron) })
            {
                var part = BuilderUtils.Group(name, pan.transform);
                part.transform.localScale = Vector3.one * unit;
                part.AddComponent<MeshFilter>().sharedMesh = meshes[name];
                part.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            var col = pan.AddComponent<BoxCollider>();
            // Đỉnh hộp 0,56 m: capsule người chơi leo được vật cao hơn tính toán (stepOffset 0,3 + đáy capsule 0,05 + skinWidth: hộp xô tro
            // cao 0,44 m vẫn bị Ba leo lên, 0,55 m thì không; AshBucketClimbPlayTests). Chảo thấp (mép 0,21 m) mà để hộp sát hình thì Ba
            // bước lên chảo, camera nhô 0,24 m. Đáy hộp cách sàn 3 cm: chạm mặt sàn thì quét chảo vướng mép ghép các khối sàn và chảo
            // kẹt cứng giữa phòng rửa.
            col.center = new Vector3(0f, 0.295f, 0f);
            col.size = new Vector3(0.8f, 0.53f, 0.8f);
            var rb = pan.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.mass = 40f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            FireBuilder.PotSteam(pan.transform);
            task.dirtMaterial = AssetFactory.DirtMasked();
            task.pan = rb;
            task.handle = AddTarget(pan, task, "pan");
            return task;
        }

        /// <summary>Giữ thăng bằng khi vác bao lên thang hầm 14 bậc (vùng thang và chỗ bao rơi do ShipInterior dựng).</summary>
        static void BuildStairsBalance(ShipLayout layout)
        {
            var balance = BuilderUtils.Group("StairsBalance", layout.foodHold).AddComponent<StairsBalance>();
            balance.stairs = layout.holdStairs;
            balance.slipSpot = layout.holdSlipSpot;
        }

        // ---------- M11: kéo rổ rau bằng dây sắt, sóng phủ boong (F05b) ----------

        /// <summary>
        /// Đường dây sắt (thế giới): sàn hầm thực phẩm (giữa lối, phía mũi chân thang) → chân thang 14 bậc → đầu thang trong
        /// buồng thang → ra cửa buồng thang lên boong giếng → dọc vách thượng tầng tới cửa bếp → vào bếp, cạnh lò A.
        /// Hầm theo hệ gốc tàu (ux, uy, uz) → thế giới (−0,2 − ux, −5,6 + uy, −0,85 − uz).
        /// </summary>
        static readonly Vector3[] HaulPath =
        {
            new Vector3(-11.2f, -2.8f, -0.85f), new Vector3(-9.8f, -2.8f, -0.85f), new Vector3(-6.3f, 0f, -0.85f),
            new Vector3(-5.67f, 0f, -0.85f), new Vector3(-5.67f, 0f, 2.6f), new Vector3(-4.55f, 0f, 2.6f), new Vector3(-4.25f, 0f, 2.05f),
        };

        /// <summary>Chỗ người chơi bị sóng cuốn tới: phía lái cột buồm trước, sợi xích chéo ở mạn trái (thế giới).</summary>
        static readonly Vector3 MastSpot = new Vector3(-21.5f, 0f, -0.95f);

        /// <summary>Kéo rổ rau ngày bão (F05b): rổ ở sàn hầm thực phẩm (thuộc FoodHold), dây sắt, chỗ sóng cuốn tới.</summary>
        static HaulStation BuildHaul(Transform env, Transform foodHold, Transform tasks, Mats m)
        {
            var task = new GameObject("HaulStation").AddComponent<HaulStation>();
            task.transform.SetParent(tasks, false);
            task.points = HaulPath;
            task.deckFrom = 2;
            task.wellDeck = new Bounds(new Vector3(-5.67f, 1f, 0.9f), new Vector3(1.4f, 2.4f, 4.8f));

            // Rổ mây đầy rau, quai sắt để buộc dây; gốc ở đáy rổ, mặt trước hướng theo đường dây.
            var basket = BuilderUtils.ArtSlot("HaulBasket", foodHold, foodHold.InverseTransformPoint(HaulPath[0]), Vector3.zero, t =>
            {
                BuilderUtils.Block("BasketBottom", t, new Vector3(0f, .018f, 0f), new Vector3(.6f, .036f, .42f), m.Wicker, false, false);
                for (int row = 0; row < 5; row++)
                {
                    float y = .055f + row * .06f;
                    foreach (float z in new[] { -.21f, .21f }) BuilderUtils.Block("WovenBand", t, new Vector3(0f, y, z), new Vector3(.6f, .024f, .016f), m.Wicker, false, false);
                    foreach (float x in new[] { -.3f, .3f }) BuilderUtils.Block("WovenBand", t, new Vector3(x, y, 0f), new Vector3(.016f, .024f, .42f), m.Wicker, false, false);
                }
                for (int i = 0; i < 10; i++) foreach (float z in new[] { -.21f, .21f })
                    BuilderUtils.Block("BasketStake", t, new Vector3(-.28f + i * .062f, .16f, z), new Vector3(.012f, .32f, .022f), m.Wicker, false, false);
                for (int i = 0; i < 7; i++) foreach (float x in new[] { -.3f, .3f })
                    BuilderUtils.Block("BasketStake", t, new Vector3(x, .16f, -.19f + i * .063f), new Vector3(.022f, .32f, .012f), m.Wicker, false, false);
                BuilderUtils.Block("Handle", t, new Vector3(0f, .38f, .22f), new Vector3(.3f, .04f, .03f), m.Iron, false, false);
                // Dùng lá có bề dày và cuống của rau bếp, đặt chéo qua miệng rổ mở.
                for (int i = 0; i < 4; i++)
                {
                    var bunch = ChoreProps.Bunch(t, "BasketGreens_" + i, m.Leaf);
                    bunch.transform.localPosition = new Vector3(-.18f + i * .12f, .29f, i % 2 == 0 ? -.02f : .05f);
                    bunch.transform.localRotation = Quaternion.Euler(-25f, -25f + i * 18f, 0f);
                }
                return t.gameObject;
            });
            basket.name = "HaulBasket";
            BuilderUtils.ClearStatic(basket);
            var col = basket.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.25f, 0f);
            col.size = new Vector3(0.7f, 0.5f, 0.5f);
            task.basket = basket.transform;
            task.basketTarget = AddTarget(basket, task, "basket");

            var ropeGo = new GameObject("HaulRope");
            ropeGo.transform.SetParent(tasks, false);
            var rope = ropeGo.AddComponent<LineRenderer>();
            rope.useWorldSpace = true;
            rope.widthMultiplier = 0.025f;
            rope.numCapVertices = 2;
            rope.sharedMaterial = m.Iron;
            rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rope.positionCount = 0;
            rope.enabled = false;
            task.rope = rope;

            var spot = new GameObject("WaveMastSpot").transform;
            spot.SetParent(env, false);
            spot.localPosition = MastSpot;
            spot.localRotation = Quaternion.Euler(0f, 220f, 0f);
            task.mastSpot = spot;
            return task;
        }

        /// <summary>
        /// Dây xích chằng chéo từ cột buồm trước xuống khuy sắt trên boong (luôn có), và dây cứu sinh ngày bão căng từ cửa
        /// bếp tới cột buồm trên boong giếng trước (DayScenery bật). Tọa độ thế giới, lớp ánh sáng ngoài trời.
        /// </summary>
        static GameObject BuildStormRigging(Transform parent, Mats m)
        {
            var root = BuilderUtils.Group("MastRigging", parent);
            // Xích: 18 mắt xích từ cổ cột buồm (độ cao 1,8 m) xuống khuy boong phía lái, mạn trái chỗ người chơi bị sóng xô tới.
            var top = new Vector3(-22.0f, 1.8f, -1.55f);
            var eye = new Vector3(-20.6f, 0.05f, -1.95f);
            const int links = 18;
            var dir = (eye - top).normalized;
            for (int i = 0; i < links; i++)
            {
                var p = Vector3.Lerp(top, eye, (i + 0.5f) / links);
                var link = BuilderUtils.Prim(PrimitiveType.Capsule, "ChainLink", root.transform, p, new Vector3(0.045f, 0.06f, 0.025f), m.Iron, false, false);
                link.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90f, i % 2 == 0 ? 0f : 90f, 0f);
            }
            BuilderUtils.Block("DeckEye", root.transform, eye, new Vector3(0.14f, 0.06f, 0.14f), m.Iron, false, false);

            var lifeline = new GameObject("StormLifeline");
            lifeline.transform.SetParent(parent, false);
            var line = lifeline.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = 0.035f;
            line.sharedMaterial = m.Wicker;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // Hai đầu cao 1,35–1,4 m: chỗ võng nhất vẫn cao hơn thành miệng hầm hàng số 2 (1,0 m).
            var a = new Vector3(-5.25f, 1.35f, 3.05f);
            var b = new Vector3(-21.95f, 1.4f, -0.45f);
            const int points = 12;
            line.positionCount = points;
            for (int i = 0; i < points; i++)
            {
                float t = i / (points - 1f);
                line.SetPosition(i, Vector3.Lerp(a, b, t) + Vector3.down * (0.25f * Mathf.Sin(t * Mathf.PI)));
            }
            lifeline.SetActive(false);
            BuilderUtils.SetRenderingLayer(root, WorldRenderingLayers.Exterior);
            BuilderUtils.SetRenderingLayer(lifeline, WorldRenderingLayers.Exterior);
            return lifeline;
        }

        static void BuildCoalBaskets(Transform env, CoalStation coal, PlayerArea hold, Mats m)
        {
            var baskets = new Holdable[2];
            var homes = new Transform[baskets.Length];
            var spots = new[] { new Vector3(0.55f, 0f, -4.75f), new Vector3(2.55f, 0f, -4.2f) };
            for (int i = 0; i < baskets.Length; i++)
            {
                homes[i] = BuilderUtils.Group("BasketSupport_" + i, hold.transform.parent).transform;
                homes[i].localPosition = spots[i];
                var go = BuilderUtils.Group("CoalBasket_" + i, hold.transform.parent);
                go.transform.localPosition = spots[i];
                // Sọt hở miệng thật (thành, mép, đáy trong): tay nắm vành nhìn thấy được, sọt rỗng nhìn thấy lòng sọt.
                ChoreProps.OpenVessel(go.transform, "Body", Vector3.zero, 0.2f, 0.23f, 0.34f, 0.018f, 0.03f, m.Wicker);
                // Than chất có ngọn trong sọt: lõi dẹt và các cục than góc cạnh; ẩn cả khối khi đã đổ.
                var load = BuilderUtils.Group("Load", go.transform);
                load.transform.localPosition = new Vector3(0f, 0.33f, 0f);
                BuilderUtils.Prim(PrimitiveType.Sphere, "LoadBulk", load.transform, Vector3.zero, new Vector3(0.42f, 0.1f, 0.42f), m.Coal, false, false);
                var lumpMesh = AssetFactory.CoalLumpMesh();
                for (int k = 0; k < 9; k++)
                {
                    float a = k * 2.39996f, r = k == 0 ? 0f : 0.07f + 0.05f * (k % 3);
                    var lump = BuilderUtils.Prim(PrimitiveType.Cube, "LoadLump", load.transform,
                        new Vector3(Mathf.Cos(a) * r, 0.045f - r * 0.25f, Mathf.Sin(a) * r), Vector3.one * (0.07f + 0.012f * (k % 4)), m.Coal, false, false,
                        new Vector3(31f * k, 57f * k, 13f * k));
                    lump.GetComponent<MeshFilter>().sharedMesh = lumpMesh;
                }
                // Hai tay nắm hai bên vành sọt (tay theo sọt cả khi nghiêng đổ).
                var leftGrip = new GameObject("BasketLeftGrip").transform;
                leftGrip.SetParent(go.transform, false);
                leftGrip.localPosition = new Vector3(-0.24f, 0.3f, 0f);
                leftGrip.localRotation = HandsRig.SideGrip(true);
                var rightGrip = new GameObject("BasketRightGrip").transform;
                rightGrip.SetParent(go.transform, false);
                rightGrip.localPosition = new Vector3(0.24f, 0.3f, 0f);
                rightGrip.localRotation = HandsRig.SideGrip(false);
                go.layer = BuilderUtils.Layer("Holdable");
                go.tag = "Holdable";
                var col = go.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, 0.2f, 0f);
                col.size = new Vector3(0.48f, 0.4f, 0.48f);
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 20f;
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                var h = go.AddComponent<Holdable>();
                h.twoHanded = true;
                h.kind = "coal";
                h.leftGrip = leftGrip;
                h.rightGrip = rightGrip;
                h.heldOffset = new Vector3(0f, -0.12f, 0.05f);
                var back = go.AddComponent<ReturnToStart>();
                back.useArea = true;
                back.area = CarryArea;
                baskets[i] = h;
            }
            coal.baskets = baskets;
            coal.basketHomes = homes;
            coal.hold = hold;
            // Vùng sàn trước đống than, nơi để sọt: mang sọt rỗng về, nhắm vào đây để đặt sọt lại đúng chỗ của nó.
            var place = BuilderUtils.Group("CoalBasketPlace", hold.transform.parent);
            place.transform.localPosition = new Vector3(1.55f, 0f, -4.45f);
            var placeCol = place.AddComponent<BoxCollider>();
            placeCol.isTrigger = true;
            placeCol.center = new Vector3(0f, 0.25f, 0f);
            placeCol.size = new Vector3(2.6f, 0.5f, 0.9f);
            coal.basketReturn = AddTarget(place, coal, "basketPlace");
        }
    }
}
