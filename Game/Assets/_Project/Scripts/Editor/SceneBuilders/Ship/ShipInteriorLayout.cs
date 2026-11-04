using System.Collections.Generic;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Interaction;
using HaiBanTay.Narrative;
using HaiBanTay.Ship;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders.Ship
{
    /// <summary>
    /// Khối bếp trong thượng tầng và hầm thực phẩm dưới boong giếng trước, chung cho ba cảnh (M10b).
    /// Khối bếp dựng theo hệ cục bộ bếp cũ (sàn y = 0, tường tây x = −5, tường bắc z = 3) rồi đặt vào tàu bằng
    /// ShipCoordinates.GalleyOrigin/GalleyRotation; hầm dựng thẳng theo hệ Unity của gốc tàu.
    /// </summary>
    public static partial class ShipInterior
    {
        /// <summary>Mặt trong tường tây bếp; mặt ngoài áp sát mặt trong vỏ thượng tầng (Blender x = 4,924).</summary>
        public const float WestWallInner = -5.0f;
        public const float WestWallOuter = -5.124f;
        /// <summary>Mặt trong tường bắc bếp (Wall_N dày 0,1 m, tâm z 3,05).</summary>
        public const float NorthWallInner = 3.0f;
        /// <summary>Tâm ô cửa bếp (galley_door) ở chân cửa, giữa bề dày tường; ô cửa rộng 0,9 m (z 2,15…3,05), cao 2 m.</summary>
        public static readonly Vector3 GalleyDoorCenter = new Vector3(-5.062f, 0f, 2.6f);
        public const float GalleyDoorWidth = 0.9f;
        public const float DoorHeight = 2f;
        /// <summary>Tâm ô cửa sổ tròn trên mặt trong tường tây (galley_porthole, bán kính 0,25 m).</summary>
        public static readonly Vector3 PortholeCenter = new Vector3(-5.0f, 1.55f, 0.6f);
        public const float PortholeRadius = 0.25f;
        /// <summary>Cửa hông hành lang ra lối đi mạn phải (corridor_starboard_door): rộng 1 m, mặt trong vách ở z 4,33.</summary>
        public static readonly Vector3 CorridorDoorCenter = new Vector3(6.0f, 0f, 4.36f);
        public const float CorridorDoorWidth = 1f;
        /// <summary>Vùng trong nhà của khối bếp (bếp, hành lang, phòng rửa, hầm than).</summary>
        static readonly Bounds GalleyRegion = new Bounds(new Vector3(0.975f, 1.3f, -0.66f), new Vector3(12.05f, 2.6f, 9.98f));
        /// <summary>Hầm thực phẩm theo hệ Unity của gốc tàu (interior_volumes.hold): x 5,9…15,4, sàn 2,8, trần 5,35.</summary>
        static readonly Bounds HoldRegion = new Bounds(new Vector3(10.65f, 4.05f, 0f), new Vector3(9.3f, 2.5f, 6.2f));
        const float HoldFloor = 2.8f;
        const float HoldCeiling = 5.35f;
        /// <summary>Lỗ trần trên thang hầm: dài tới x = 9,0 để đầu người chơi không vướng mép trần khi xuống thang.</summary>
        const float HoldOpeningEnd = 9.0f;
        const float HoldOpeningHalfWidth = 0.65f;

        public static void Build(ShipLayout layout, JObject data)
        {
            var m = new Mats();
            var root = BuilderUtils.Group("GalleyBlock", layout.transform).transform;
            root.localPosition = ShipCoordinates.GalleyOrigin;
            root.localRotation = ShipCoordinates.GalleyRotation;
            layout.galley = root;

            var room = BuildRoom(root, m);
            layout.galleyDoor = BuildWestWall(room, m);
            BuildPorthole(room, m);
            layout.corridorDoor = BuildCorridor(root, m);
            BuildWashRoom(root, m, out _);
            BuildCoalHold(root, m, out var coal);
            layout.coalHold = coal;
            BuildServingHatch(root, m);
            BuildNameplate(root, m);
            layout.eveningObjects = BuildEveningCorner(root, m, out var spawn, out var seat, out var lamp);
            layout.eveningSpawn = spawn;
            layout.restSeat = seat;
            layout.oilLamp = lamp;
            layout.galleyZone = BuilderUtils.Trigger<ShipZone>("GalleyZone", root, GalleyRegion.center, GalleyRegion.size);
            layout.galleyZone.region = ShipRegion.Interior;
            BuildGalleyProbes(root);

            BuildFoodHold(layout, m, DeckZones.Connection(data, ShipWalkable.HoldStairsId));
        }

        /// <summary>Cảnh không có buổi tối (01, 03): bỏ NPC nghỉ, tắt góc đèn dầu để không vào lightmap.</summary>
        public static void HideEveningCorner(ShipLayout layout)
        {
            foreach (var go in layout.eveningObjects)
                if (go != null)
                {
                    if (go.name == "ArtSlot_JacquesSilhouette")
                        Object.DestroyImmediate(go); // Không giữ NPC/routine không dùng trong cảnh bến/cảng.
                    else
                        go.SetActive(false);
                }
        }

        /// <summary>
        /// Tường tây bếp: dày 0,124 m, mặt ngoài áp mặt trong vỏ thượng tầng; ô cửa sổ tròn (lỗ vuông 0,5 m, ống viền tròn
        /// lấp góc) và ô cửa bếp ra boong giếng trước, cánh cửa bản lề thật.
        /// </summary>
        static HingedDoor BuildWestWall(Transform room, Mats m)
        {
            float doorSouth = GalleyDoorCenter.z - GalleyDoorWidth * 0.5f;
            float doorNorth = GalleyDoorCenter.z + GalleyDoorWidth * 0.5f;
            float holeSouth = PortholeCenter.z - PortholeRadius;
            float holeNorth = PortholeCenter.z + PortholeRadius;
            float holeBottom = PortholeCenter.y - PortholeRadius;
            float holeTop = PortholeCenter.y + PortholeRadius;
            WestWall(room, "Wall_W_Bottom", -3.1f, doorSouth, 0f, holeBottom, m);
            WestWall(room, "Wall_W_Top", -3.1f, doorSouth, holeTop, 2.6f, m);
            WestWall(room, "Wall_W_South", -3.1f, holeSouth, holeBottom, holeTop, m);
            WestWall(room, "Wall_W_Middle", holeNorth, doorSouth, holeBottom, holeTop, m);
            WestWall(room, "Wall_W_North", doorNorth, 3.1f, 0f, 2.6f, m);
            WestWall(room, "GalleyDoorHeader", doorSouth, doorNorth, DoorHeight, 2.6f, m);

            float depth = WestWallInner - WestWallOuter;
            float x = (WestWallInner + WestWallOuter) * 0.5f;
            foreach (float z in new[] { doorSouth + 0.015f, doorNorth - 0.015f })
                BuilderUtils.Block("GalleyDoorJamb", room, new Vector3(x, DoorHeight * 0.5f, z), new Vector3(depth, DoorHeight, 0.03f), m.Wood);
            BuilderUtils.Block("GalleyDoorSill", room, new Vector3(x, -0.02f, GalleyDoorCenter.z), new Vector3(depth, 0.04f, GalleyDoorWidth), m.Floor);
            // Gốc cửa quay 180°: trục +X của cửa hướng ra boong (−X của bếp), −Z cục bộ là phía bắc. Bản lề ở mép bắc, cánh
            // mở áp vào tường bắc bếp; trục bản lề cách mặt trong tường bắc nửa bề dày cánh cộng 5 mm (mép bắc ô cửa
            // nằm lấn 5 cm vào bề dày tường bắc).
            float hingeInset = doorNorth - NorthWallInner + 0.0275f;
            return Door(room, "ArtSlot_GalleyDoor", GalleyDoorCenter, 180f, GalleyDoorWidth, WestWallInner - GalleyDoorCenter.x, -1f, hingeInset, m);
        }

        static void WestWall(Transform room, string name, float z0, float z1, float y0, float y1, Mats m)
        {
            float x = (WestWallInner + WestWallOuter) * 0.5f;
            var center = new Vector3(x, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
            var size = new Vector3(WestWallInner - WestWallOuter, y1 - y0, z1 - z0);
            BuilderUtils.Block(name, room, center, size, m.Wall);
        }

        /// <summary>
        /// Cánh cửa gỗ bản lề, áp vào mặt trong vách (phía trong nhà), mở vào trong 90° cho cánh nằm sát vách bên cạnh. Gốc
        /// cửa ở chân ô cửa, giữa bề dày vách; trục +X cục bộ hướng ra ngoài. innerDepth: khoảng từ gốc tới mặt trong vách
        /// (theo −X cục bộ). hingeSide: +1 bản lề ở mép +Z cục bộ, −1 ở mép −Z. hingeInset: khoảng từ mép ô cửa tới trục
        /// bản lề. Cánh nhô 2,5 cm sau trục bản lề để che khe sáng phía bản lề. Cánh nằm trọn trong khối bếp, không lấn ra
        /// vỏ thượng tầng.
        /// </summary>
        static HingedDoor Door(Transform parent, string name, Vector3 position, float yaw, float width, float innerDepth, float hingeSide,
            float hingeInset, Mats m)
        {
            const float leaf = 0.045f;
            const float behind = 0.025f;
            const float latchGap = 0.03f;
            var go = BuilderUtils.Group(name, parent);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.layer = BuilderUtils.Layer("Interactable");
            go.tag = "Interactable";
            var target = go.AddComponent<InteractTarget>();
            target.available = true;
            target.promptKey = "prompt.galley_door";
            // Vùng nhắm phủ cả ô cửa, kể cả khi cánh đã mở.
            var aim = go.AddComponent<BoxCollider>();
            aim.isTrigger = true;
            aim.center = new Vector3(0f, DoorHeight * 0.5f, 0f);
            aim.size = new Vector3(0.3f, DoorHeight, width);

            var hinge = BuilderUtils.Group("Hinge", go.transform).transform;
            hinge.localPosition = new Vector3(-innerDepth - leaf * 0.5f, 0f, hingeSide * (width * 0.5f - hingeInset));
            float leafWidth = width - hingeInset - latchGap;
            float leafCenter = -hingeSide * (leafWidth - behind) * 0.5f;
            var panel = BuilderUtils.Block("DoorLeaf", hinge, new Vector3(0f, 0.98f, leafCenter), new Vector3(leaf, 1.96f, leafWidth + behind), m.Wood, false);
            GameObjectUtility.SetStaticEditorFlags(panel, 0);
            foreach (float side in new[] { -1f, 1f })
                BuilderUtils.Block("DoorHandle", hinge, new Vector3(side * 0.04f, 1f, -hingeSide * (leafWidth - 0.1f)), new Vector3(0.035f, 0.025f, 0.08f),
                    m.Brass, false, false);
            var door = go.AddComponent<HingedDoor>();
            door.hinge = hinge;
            door.target = target;
            door.leaf = panel.GetComponent<Collider>();
            door.openAngle = hingeSide * 90f;
            return door;
        }

        /// <summary>
        /// Ô cửa sổ tròn: ống viền xuyên bề dày tường bếp (ngoài vuông lấp lỗ tường, trong tròn bán kính 0,25 m) và vành đồng
        /// phía trong. Kính và vành sắt ngoài là của model (Blender x 4,934 và 4,974), không dựng kính thứ hai trùng mặt.
        /// </summary>
        static void BuildPorthole(Transform room, Mats m)
        {
            var root = BuilderUtils.Group("ArtSlot_Porthole", room).transform;
            root.localPosition = PortholeCenter;
            float depth = WestWallInner - WestWallOuter;
            PortholeTube(root, "PortholeReveal", PortholeRadius, true, 0f, -depth, m.Wall, true);
            PortholeTube(root, "InnerRing", PortholeRadius, false, 0.012f, -0.012f, m.Brass, false);
        }

        /// <summary>
        /// Ống tròn rỗng theo trục X cục bộ, từ front tới back. square: viền ngoài vuông cạnh 2 × radius (lấp góc lỗ tường),
        /// ngược lại viền ngoài tròn radius + 0,05. Mặt dựng hai phía để nhìn từ hai bên.
        /// </summary>
        static void PortholeTube(Transform parent, string name, float radius, bool square, float front, float back, Material mat, bool collider)
        {
            const int sides = 48;
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a = i * 2f * Mathf.PI / sides;
                float b = (i + 1) * 2f * Mathf.PI / sides;
                var innerA = TubePoint(a, radius, false, square);
                var innerB = TubePoint(b, radius, false, square);
                var outerA = TubePoint(a, radius, true, square);
                var outerB = TubePoint(b, radius, true, square);
                AddQuad(vertices, indices, At(front, innerA), At(front, innerB), At(front, outerB), At(front, outerA));
                AddQuad(vertices, indices, At(back, outerA), At(back, outerB), At(back, innerB), At(back, innerA));
                AddQuad(vertices, indices, At(front, innerA), At(back, innerA), At(back, innerB), At(front, innerB));
                AddQuad(vertices, indices, At(front, outerA), At(front, outerB), At(back, outerB), At(back, outerA));
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (mat.name == "Gen_GalleyWall")
            {
                var uv = new Vector2[vertices.Count];
                for (int i = 0; i < uv.Length; i++)
                    uv[i] = new Vector2(vertices[i].z + parent.localPosition.z, vertices[i].y + parent.localPosition.y) / AssetFactory.SurfaceTile(mat);
                mesh.uv = uv;
            }
            Unwrapping.GenerateSecondaryUVSet(mesh);
            mesh = ShipWalkable.SaveMesh(mesh, name);

            var go = BuilderUtils.Group(name, parent);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (collider)
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        }

        static Vector2 TubePoint(float angle, float radius, bool outer, bool square)
        {
            var p = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            if (!outer)
                return p * radius;
            if (square)
                return p * (radius / Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)));
            return p * (radius + 0.05f);
        }

        static Vector3 At(float x, Vector2 p) => new Vector3(x, p.x, p.y);

        /// <summary>
        /// Tứ giác hai mặt. Mỗi mặt có bộ đỉnh riêng: hai mặt ngược chiều dùng chung đỉnh thì RecalculateNormals cộng hai
        /// pháp tuyến triệt tiêu nhau, vành đồng hiện các tam giác tối.
        /// </summary>
        static void AddQuad(List<Vector3> vertices, List<int> indices, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3)
        {
            int k = vertices.Count;
            vertices.AddRange(new[] { v0, v1, v2, v3, v0, v1, v2, v3 });
            indices.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 });
            indices.AddRange(new[] { k + 6, k + 5, k + 4, k + 7, k + 6, k + 4 });
        }

        /// <summary>
        /// Hành lang sau cửa tường đông của bếp (z 1…2): đoạn A chạy đông tới x 5,5, đoạn B rẽ bắc (x 5,5…6,5) tới cửa hông
        /// ra lối đi mạn phải. Biển tên tàu trên vách đông đoạn B.
        /// </summary>
        static HingedDoor BuildCorridor(Transform env, Mats m)
        {
            var c = BuilderUtils.Group("Corridor", env).transform;
            float end = CorridorDoorCenter.z - 0.03f;
            float lengthB = end - 1.0f;
            float midB = (end + 1.0f) * 0.5f;
            BuilderUtils.Block("Floor_A", c, new Vector3(5.3f, -0.05f, 1.5f), new Vector3(0.4f, 0.1f, 1.0f), m.Floor);
            BuilderUtils.Block("Floor_B", c, new Vector3(6.0f, -0.05f, midB), new Vector3(1.0f, 0.1f, lengthB), m.Floor);
            BuilderUtils.Block("Wall_S", c, new Vector3(6.075f, 1.3f, 0.95f), new Vector3(1.95f, 2.6f, 0.1f), m.Wall);
            BuilderUtils.Block("Wall_E", c, new Vector3(6.55f, 1.3f, midB), new Vector3(0.1f, 2.6f, lengthB), m.Wall);
            BuilderUtils.Block("Wall_A_N", c, new Vector3(5.3f, 1.3f, 2.05f), new Vector3(0.4f, 2.6f, 0.1f), m.Wall);
            BuilderUtils.Block("Wall_B_W", c, new Vector3(5.45f, 1.3f, (2.1f + end) * 0.5f), new Vector3(0.1f, 2.6f, end - 2.1f), m.Wall);
            BuilderUtils.Block("Ceiling_A", c, new Vector3(5.3f, 2.65f, 1.5f), new Vector3(0.5f, 0.1f, 1.1f), m.Ceiling);
            BuilderUtils.Block("Ceiling_B", c, new Vector3(6.0f, 2.65f, midB), new Vector3(1.2f, 0.1f, lengthB), m.Ceiling);
            BuilderUtils.Block("SideDoorHeader", c, new Vector3(6.0f, (DoorHeight + 2.6f) * 0.5f, end + 0.015f),
                new Vector3(CorridorDoorWidth + 0.1f, 2.6f - DoorHeight, 0.03f), m.Wall);
            BuilderUtils.Block("SideDoorSill", c, new Vector3(6.0f, -0.02f, end + 0.02f), new Vector3(CorridorDoorWidth, 0.04f, 0.04f), m.Floor);
            // Đèn gas gắn vách đông (mặt trong ở x 6,5), phía trên biển tên tàu, thay đèn vô hình cũ.
            Props.WallLantern(c, "Lantern_Corridor", new Vector3(6.5f, 2.0f, 2.9f), -90f, realtime: false, intensity: 2.0f, range: 5f);
            // Gốc cửa quay −90°: trục +X của cửa hướng ra mạn phải (+Z của bếp).
            // Bản lề ở mép tây (+Z cục bộ), cánh mở áp vào vách tây đoạn B.
            return Door(c, "CorridorSideDoor", CorridorDoorCenter, -90f, CorridorDoorWidth, CorridorDoorCenter.z - end, 1f, 0.03f, m);
        }

        /// <summary>Probe ánh sáng và phản chiếu bake cho từng phòng của khối bếp (tay, NPC, đồ cầm lấy sáng từ đây).</summary>
        static void BuildGalleyProbes(Transform root)
        {
            var probes = BuilderUtils.Group("Probes", root).transform;
            BuilderUtils.ProbeGrid("LightProbes_Galley", probes, new Vector3(0f, 1.2f, 0f), new Vector3(9.6f, 2.2f, 5.6f), new Vector3(1.6f, 1.1f, 1.4f));
            BuilderUtils.ProbeGrid("LightProbes_Corridor", probes, new Vector3(5.9f, 1.2f, 2.6f), new Vector3(1f, 2.2f, 3f), new Vector3(0.5f, 1.1f, 1f));
            BuilderUtils.ProbeGrid("LightProbes_Wash", probes, WashRoomCenter + Vector3.up * 1.2f, new Vector3(3.4f, 2.2f, 3.7f), new Vector3(0.85f, 1.1f, 1.2f));
            BuilderUtils.ProbeGrid("LightProbes_CoalHold", probes, CoalHoldCenter + Vector3.up * 1.2f, new Vector3(3.2f, 2.2f, 2.2f), new Vector3(1.1f, 1.1f, 1.1f));
            BuilderUtils.BakedReflectionProbe("Reflection_Galley", probes, new Vector3(0f, 1.3f, 0f), new Vector3(10.2f, 2.7f, 6.2f), true);
            BuilderUtils.BakedReflectionProbe("Reflection_Wash", probes, WashRoomCenter + Vector3.up * 1.3f, new Vector3(3.75f, 2.7f, 4.1f), true);
            BuilderUtils.BakedReflectionProbe("Reflection_CoalHold", probes, CoalHoldCenter + Vector3.up * 1.3f, new Vector3(3.5f, 2.7f, 2.6f), true);
        }

        /// <summary>
        /// Hầm thực phẩm dưới boong giếng trước (interior_volumes.hold): sàn 2,8, vách lạnh, trần chừa lỗ trên thang 14 bậc,
        /// kệ rau, thịt, cá, đá; chỗ để bao khoai và sọt rau do GalleySceneBuilder đặt. Lạnh (ColdZone), trong nhà (ShipZone).
        /// </summary>
        static void BuildFoodHold(ShipLayout layout, Mats m, JToken stairs)
        {
            var hold = BuilderUtils.Group("FoodHold", layout.transform).transform;
            layout.foodHold = hold;
            float x0 = HoldRegion.min.x - 0.1f;
            float x1 = HoldRegion.max.x + 0.1f;
            float length = x1 - x0;
            float mid = (x0 + x1) * 0.5f;
            // Sàn đi được là của ShipWalkable (vùng hold); khối sàn chỉ để nhìn và bake.
            BuilderUtils.Block("HoldFloor", hold, new Vector3(mid, HoldFloor - 0.05f, 0f), new Vector3(length, 0.1f, 6.4f), m.Floor, true, false);
            float wallHeight = HoldCeiling - HoldFloor + 0.05f;
            float wallY = HoldFloor + wallHeight * 0.5f;
            foreach (float z in new[] { -3.15f, 3.15f })
                BuilderUtils.Block("HoldSide", hold, new Vector3(mid, wallY, z), new Vector3(length, wallHeight, 0.1f), m.Cold);
            foreach (float x in new[] { x0 + 0.05f, x1 - 0.05f })
                BuilderUtils.Block("HoldEnd", hold, new Vector3(x, wallY, 0f), new Vector3(0.1f, wallHeight, 6.3f), m.Cold);

            // Trần: chừa lỗ x 6,1…9,0, z ±0,55 trên thang.
            float ceilingY = HoldCeiling + 0.025f;
            float openStart = 6.1f;
            float aftLength = x1 - HoldOpeningEnd;
            BuilderUtils.Block("HoldCeiling_Fore", hold, new Vector3(HoldOpeningEnd + aftLength * 0.5f, ceilingY, 0f), new Vector3(aftLength, 0.05f, 6.3f), m.Cold,
                true, false);
            // Collider trần phía mũi chỉ bắt đầu từ chỗ đầu người xuống thang không còn chạm trần (x ≈ 9,5): đoạn trần
            // 9,0…9,5 chỉ có hình, camera đi qua vẫn thấp hơn trần.
            float colliderStart = ShipWalkable.HoldClearX(stairs, HoldCeiling);
            var foreCollider = BuilderUtils.Group("HoldCeiling_ForeCollider", hold);
            foreCollider.transform.localPosition = new Vector3((colliderStart + x1) * 0.5f, ceilingY, 0f);
            foreCollider.AddComponent<BoxCollider>().size = new Vector3(x1 - colliderStart, 0.05f, 6.3f);
            float sideWidth = 3.15f - HoldOpeningHalfWidth;
            foreach (float side in new[] { -1f, 1f })
                BuilderUtils.Block("HoldCeiling_Side", hold, new Vector3((x0 + HoldOpeningEnd) * 0.5f, ceilingY, side * (HoldOpeningHalfWidth + sideWidth * 0.5f)),
                    new Vector3(HoldOpeningEnd - x0, 0.05f, sideWidth), m.Cold);
            BuilderUtils.Block("HoldCeiling_Aft", hold, new Vector3((x0 + openStart) * 0.5f, ceilingY, 0f),
                new Vector3(openStart - x0, 0.05f, HoldOpeningHalfWidth * 2f), m.Cold);

            BuildFoodRacks(hold, m);
            DressFoodHold(hold, m);

            layout.coldZone = BuilderUtils.Trigger<ColdZone>("ColdZone", hold, HoldRegion.center, HoldRegion.size);
            layout.holdZone = BuilderUtils.Trigger<ShipZone>("HoldZone", hold, HoldRegion.center, HoldRegion.size);
            layout.holdZone.region = ShipRegion.Interior;
            // Thang hầm: vùng giữ thăng bằng khi vác bao (StairsBalance) và chỗ bao rơi xuống chân thang.
            layout.holdStairs = BuilderUtils.Trigger<PlayerArea>("StairsArea", hold, new Vector3(7.85f, 4.7f, 0f), new Vector3(3.8f, 4f, 1.05f));
            layout.holdSlipSpot = BuilderUtils.Group("SackSlipSpot", hold).transform;
            layout.holdSlipSpot.localPosition = new Vector3(10f, HoldFloor + 0.05f, 0f);

            Props.HangingLamp(hold, new Vector3(11.2f, HoldCeiling, 0f), realtime: false, intensity: 12f, range: 7f).name = "Lamp_FoodHold";
            // Đèn lạnh phía mũi (kính trắng xanh) và đèn gas ở chân thang: thay hai đèn vô hình cũ.
            Props.HangingLamp(hold, new Vector3(13.5f, HoldCeiling, 0f), realtime: false, intensity: 10f, range: 7f,
                glow: Lamps.ColdGlow(), lightColor: new Color(0.7f, 0.82f, 1f)).name = "Lamp_HoldCold";
            Props.WallLantern(hold, "Lantern_HoldStairs", new Vector3(9.6f, 4.55f, 3.1f), 180f, realtime: false, intensity: 14f, range: 8f);
            // Đèn chân thang không tới vách/trần buồng trên: đèn áp vách trong buồng, chỉ bake.
            var upperLamp = Props.WallLantern(layout.transform, "Lantern_HoldStairsUpper", new Vector3(6.8f, 6.75f, 0.5f), 180f, realtime: false, intensity: 28f, range: 5f);
            BuilderUtils.SetRenderingLayer(upperLamp, WorldRenderingLayers.Interior);
            BuilderUtils.SetLightLayer(upperLamp.GetComponentInChildren<Light>(), WorldRenderingLayers.Interior);
            BuilderUtils.ProbeGrid("LightProbes_FoodHold", hold, HoldRegion.center, new Vector3(9f, 2.1f, 5.8f), new Vector3(1.8f, 1.05f, 1.45f));
            BuilderUtils.ProbeGrid("LightProbes_HoldStairs", hold, new Vector3(7.6f, 4.9f, 0f), new Vector3(3f, 2f, 0.6f), new Vector3(1f, 1f, 0.6f));
            BuilderUtils.BakedReflectionProbe("Reflection_FoodHold", hold, HoldRegion.center, HoldRegion.size, true);
        }

        /// <summary>Bốn kệ dọc vách mạn phải (−Z): rau, thịt, cá, đá.</summary>
        static void BuildFoodRacks(Transform hold, Mats m)
        {
            var kinds = new[] { "Vegetables", "Meat", "Fish", "Ice" };
            for (int i = 0; i < kinds.Length; i++)
            {
                var rack = BuilderUtils.Group("Food_" + kinds[i], hold).transform;
                rack.localPosition = new Vector3(10.3f + i * 1.2f, HoldFloor, -2.7f);
                foreach (float y in new[] { 0.15f, 0.9f })
                    BuilderUtils.Block("Shelf", rack, new Vector3(0f, y, 0f), new Vector3(1f, 0.08f, 0.7f), m.Wood);
                foreach (float x in new[] { -0.46f, 0.46f })
                    BuilderUtils.Block("Post", rack, new Vector3(x, 0.6f, 0f), new Vector3(0.06f, 1.2f, 0.65f), m.Wood);
                // Hàng dự trữ đóng thùng gỗ, thay các khối thực phẩm màu giữ chỗ.
                foreach (var box in new[] { new Vector4(0.8f, 0.3f, 0.5f, 1.1f), new Vector4(0.7f, 0.26f, 0.45f, 0.32f) })
                    BuilderUtils.ArtSlot("Crate", rack, new Vector3(0f, box.w - box.y * 0.5f, 0f), Vector3.zero, t =>
                    {
                        BuilderUtils.Block("Body", t, new Vector3(0f, box.y * 0.5f, 0f), new Vector3(box.x, box.y, box.z), m.Wood, true, false);
                        return t.gameObject;
                    });
            }
        }

        /// <summary>Đồ bày trong hầm: kệ ở vách mũi, thùng gỗ, thùng rượu; không chắn lối từ chân thang tới bao khoai và sọt rau.</summary>
        static void DressFoodHold(Transform hold, Mats m)
        {
            BuilderUtils.ArtSlot("GalleyShelf", hold, new Vector3(15.0f, HoldFloor, 0f), new Vector3(0f, 90f, 0f), t =>
            {
                BuilderUtils.Block("Body", t, new Vector3(0f, 0.7f, 0f), new Vector3(1.6f, 1.4f, 0.5f), m.Wood);
                return t.gameObject;
            });
            foreach (var (pos, yaw) in new[] { (new Vector3(11.95f, HoldFloor, 2.75f), 0f), (new Vector3(7.6f, HoldFloor, -2.6f), 20f) })
                BuilderUtils.ArtSlot("Crate", hold, pos, new Vector3(0f, yaw, 0f), t =>
                {
                    BuilderUtils.Block("Body", t, new Vector3(0f, 0.2f, 0f), new Vector3(0.83f, 0.4f, 0.4f), m.Wood);
                    return t.gameObject;
                });
            BuilderUtils.ArtSlot("Barrel", hold, new Vector3(11.0f, HoldFloor, 2.6f), Vector3.zero, t =>
            {
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Body", t, new Vector3(0f, 0.43f, 0f), new Vector3(0.74f, 0.43f, 0.74f), m.Wood);
                return t.gameObject;
            });
        }
    }
}
