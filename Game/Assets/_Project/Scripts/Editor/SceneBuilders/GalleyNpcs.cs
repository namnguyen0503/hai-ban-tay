using HaiBanTay.Editor.Import;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Npc;
using HaiBanTay.Shift;
using HaiBanTay.Ship;
using HaiBanTay.Tasks;
using Mats = HaiBanTay.Editor.SceneBuilders.Ship.ShipInterior.Mats;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>
    /// NPC trong bếp (M10): quản bếp và phụ bếp Pháp (nhân vật hư cấu). Thân là Prefabs/NPC/{vai}.prefab (Rocketbox,
    /// Tools/import_characters.sh); chưa nhập thì khối xám. Lộ trình đi qua các lối trống quanh bàn sơ chế:
    /// tránh xô rác (-1,35; 0,62), sọt rau (-1,5; -0,3), thùng than (0,3; 2,5) và chỗ người chơi đứng xúc than ở lò thứ nhất.
    /// </summary>
    public static partial class GalleySceneBuilder
    {
        // Chỗ đứng cách thân bếp gộp ít nhất 0,3 m và tránh vòng phấn đặt chảo trước khoang nấu (-1,4; 1,45), đi vòng qua lối z 0,8 giữa bàn và lò.
        static readonly Vector3 CookStove = new Vector3(-0.72f, 0f, 1.65f);
        static readonly Vector3 HelperStove = new Vector3(-2.15f, 0f, 1.65f);
        static readonly Vector3 HelperCounter = new Vector3(-3.4f, 0f, -1.85f);

        static GameObject BuildNpcs(Transform env, SackStation sacks, Transform tub, CoalStation coal, Mats m)
        {
            var root = BuilderUtils.Group("Npcs", env).transform;

            var cook = MakeNpc(root, "Cook", CookStove, 0f, new Color(0.9f, 0.88f, 0.82f), new Color(0.32f, 0.24f, 0.18f));
            cook.useNavMesh = true;
            Hands(cook);
            var cr = cook.gameObject.AddComponent<CookRoutine>();
            cr.agent = cook;
            cr.sacks = sacks;
            cr.tub = tub;
            cr.watchSpot = CookStove;
            cr.watchYaw = 0f;
            cr.tubStand = new Vector3(1.93f, 0f, 0f);
            cr.tubYaw = -90f;
            cr.ladle = Ladle(root, CookStove + new Vector3(0.3f, 0.9f, 0.2f), m, out cr.ladleGrip);

            var helper = MakeNpc(root, "Helper", HelperCounter, 180f, new Color(0.78f, 0.74f, 0.64f), new Color(0.55f, 0.6f, 0.66f));
            helper.useNavMesh = true;
            Hands(helper);
            var hr = helper.gameObject.AddComponent<HelperRoutine>();
            hr.agent = helper;
            hr.coal = coal;
            hr.coalStand = new Vector3(0.3f, 0f, 1.75f);
            hr.coalYaw = 0f;
            hr.watchSpot = HelperCounter;
            hr.watchYaw = 180f;
            hr.shovel = Shovel(root, new Vector3(0.3f, 0.7f, 2.3f), m, out hr.shovelGripLeft, out hr.shovelGripRight, out hr.shovelLoad);
            return root.gameObject;
        }

        /// <summary>Tay IK bám vật thật (W5.3).</summary>
        static void Hands(NpcAgent npc)
        {
            var arms = npc.gameObject.AddComponent<NpcArmIK>();
            arms.leftWeight = arms.rightWeight = 0f;
            npc.gameObject.AddComponent<NpcHandWork>().arms = arms;
        }

        /// <summary>
        /// Muôi khuấy của quản bếp: gốc ở lòng muôi, +Y dọc cán (dài 0,46 m), lòng muôi quay về +Z; GripR là cổ tay phải nắm cuối cán
        /// (ngón vòng quanh cán, mu tay sang phải).
        /// </summary>
        static Transform Ladle(Transform parent, Vector3 position, Mats m, out Transform grip)
        {
            var ladle = BuilderUtils.Group("CookLadle", parent).transform;
            ladle.localPosition = position;
            BuilderUtils.Prim(PrimitiveType.Sphere, "Bowl", ladle, Vector3.zero, new Vector3(0.085f, 0.075f, 0.04f), m.Iron, false, false);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Shaft", ladle, new Vector3(0f, 0.2f, -0.012f), new Vector3(0.012f, 0.17f, 0.012f), m.Iron, false, false);
            BuilderUtils.Prim(PrimitiveType.Capsule, "Handle", ladle, new Vector3(0f, 0.41f, -0.012f), new Vector3(0.026f, 0.07f, 0.026f), m.Wood, false, false);
            grip = NpcHandWork.PlaceGrip(ladle, "GripR", ladle.TransformPoint(new Vector3(0f, 0.41f, -0.012f)), ladle.forward, ladle.right);
            BuilderUtils.ClearStatic(ladle.gameObject);
            return ladle;
        }

        /// <summary>
        /// Xẻng than của phụ bếp: gốc ở gót lưỡi, lưỡi vươn về −Z, cán dọc +Z dài 0,72 m; GripR cuối cán (úp tay), GripL giữa cán
        /// (ngửa tay); CoalLoad là than trên lưỡi (ẩn khi lưỡi trống).
        /// </summary>
        static Transform Shovel(Transform parent, Vector3 position, Mats m, out Transform left, out Transform right, out GameObject load)
        {
            var shovel = BuilderUtils.Group("HelperShovel", parent).transform;
            shovel.localPosition = position;
            BuilderUtils.Block("Blade", shovel, new Vector3(0f, 0f, -0.13f), new Vector3(0.24f, 0.008f, 0.26f), m.Iron, false, false);
            foreach (float x in new[] { -0.12f, 0.12f })
                BuilderUtils.Block("BladeLip", shovel, new Vector3(x, 0.02f, -0.12f), new Vector3(0.008f, 0.04f, 0.24f), m.Iron, false, false);
            BuilderUtils.Block("BladeBack", shovel, new Vector3(0f, 0.025f, 0f), new Vector3(0.24f, 0.05f, 0.008f), m.Iron, false, false);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Shaft", shovel, new Vector3(0f, 0.03f, 0.36f), new Vector3(0.032f, 0.36f, 0.032f), m.Wood, false, false, new Vector3(90f, 0f, 0f));
            BuilderUtils.Block("Grip", shovel, new Vector3(0f, 0.03f, 0.72f), new Vector3(0.11f, 0.025f, 0.025f), m.Wood, false, false);
            right = NpcHandWork.PlaceGrip(shovel, "GripR", shovel.TransformPoint(new Vector3(0f, 0.03f, 0.7f)), -shovel.right, shovel.up);
            left = NpcHandWork.PlaceGrip(shovel, "GripL", shovel.TransformPoint(new Vector3(0f, 0.03f, 0.42f)), shovel.right, -shovel.up);
            load = BuilderUtils.Group("CoalLoad", shovel);
            for (int i = 0; i < 7; i++)
            {
                float a = i * 2.39996f;
                BuilderUtils.Prim(PrimitiveType.Cube, "Lump", load.transform,
                    new Vector3(Mathf.Cos(a) * 0.06f * (i % 3) * 0.6f, 0.03f + 0.012f * (i % 2), -0.13f + Mathf.Sin(a) * 0.05f),
                    new Vector3(0.06f, 0.045f, 0.055f), m.Coal, false, false, new Vector3(17f * i, 31f * i, 11f * i));
            }
            BuilderUtils.ClearStatic(shovel.gameObject);
            return shovel;
        }

        // ---------- M11: nhân vật theo ngày (DayCast) ----------

        /// <summary>Từ boong giếng trước (cạnh cửa bếp) qua cửa bếp, vòng trước lò A, tới mép nam bàn sơ chế (thế giới). Góc tây lò
        /// (x −3,6, z 1,95) cách đường ≥ 0,49 m để thân không chồng CastRange (bot 22/10).</summary>
        static readonly Vector3[] DeckToGalley =
        {
            new Vector3(-6.6f, 0f, 2.2f), new Vector3(-5.7f, 0f, 2.6f), new Vector3(-4.55f, 0f, 2.6f), new Vector3(-4.2f, 0f, 1.75f),
            new Vector3(-3.35f, 0f, 1.2f), new Vector3(-2.2f, 0f, 0.8f), new Vector3(-1.5f, 0f, -0.85f),
        };

        /// <summary>Ông Mai: từ hành lang mạn phải (phía phòng ăn sĩ quan) qua cửa tường đông vào bếp.</summary>
        static readonly Vector3[] CorridorToGalley = { new Vector3(6.0f, 0f, 3.7f), new Vector3(6.0f, 0f, 1.5f), new Vector3(5.3f, 0f, 1.5f) };

        /// <summary>
        /// Hai lính Pháp giải ngũ (SoldierA, SoldierB), thủy thủ Jacques, ông Mai (prefab Mai; thiếu prefab thì mượn thân
        /// Jacques: hai người không bao giờ cùng xuất hiện trong một ngày). Ẩn lúc đầu; DayCast đặt theo days.vi.json.
        /// Chỗ đứng boong, bàn rau, quầy nồi, nồi cao, cửa hành lang; ghế đẩu người dạy ở góc đèn dầu.
        /// </summary>
        static DayCast BuildCast(Transform env, ShipLayout layout, GreensStation greens, TallPotStation scrub, SecurePotsTask storm, Mats m)
        {
            var root = BuilderUtils.Group("DayCast", env).transform;
            var cast = root.gameObject.AddComponent<DayCast>();
            // Jacques đứng cách vành nồi cao 0,25 m (thân NPC bán kính ~0,25): với tay tới thân nồi Ø 0,8 × cao 1 m (DayCast.HoldTallPot).
            cast.rimStandOff = 0.25f;
            cast.soldierA = CastNpc(root, "SoldierA", null, new Color(0.5f, 0.45f, 0.36f));
            cast.soldierB = CastNpc(root, "SoldierB", null, new Color(0.42f, 0.42f, 0.38f));
            foreach (var soldier in new[] { cast.soldierA, cast.soldierB })
            {
                var animator = soldier.GetComponentInChildren<Animator>();
                var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                if (hand == null) throw new System.InvalidOperationException("Lính thiếu xương bàn tay.");
                var leaf = ChoreProps.Leaf(hand, "SortingLeaf", m.Leaf);
                leaf.transform.localPosition = new Vector3(0f, -0.015f, 0.045f);
                leaf.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                leaf.transform.localScale = Vector3.one * 0.7f;
                BuilderUtils.ClearStatic(leaf);
                leaf.SetActive(false);
                var action = soldier.gameObject.AddComponent<GreensHands>();
                action.agent = soldier; action.greens = greens; action.leaf = leaf;
            }
            cast.jacques = CastNpc(root, "Jacques", null, new Color(0.8f, 0.8f, 0.78f));
            cast.mai = CastNpc(root, "Mai", "Jacques", new Color(0.86f, 0.84f, 0.78f));
            // Jacques giữ nồi bằng tay thật (quai nồi lúc bão, vành nồi cao); cả bốn người đi theo NavMesh của cảnh.
            Hands(cast.jacques);
            foreach (var a in cast.All) a.useNavMesh = true;
            cast.stormPots = storm.pots;
            cast.deckToGalley = DeckToGalley;
            cast.corridorToGalley = CorridorToGalley;
            cast.deckSpots = new[]
            {
                Spot(root, "Deck_A", new Vector3(-6.95f, 0f, 1.95f), 225f),
                Spot(root, "Deck_B", new Vector3(-7.55f, 0f, 1.35f), 45f),
            };
            // Nói chuyện xong thì vòng ra lan can phía bắc boong giếng (đo lưới chiếm chỗ 22/10: ô trống x −10,5…−5,5 ở z 2,5;
            // cột x −10,5 trống tới z 5,5; lan can ở z ≈ 6,2), khuất khỏi lối cửa bếp.
            cast.deckAwayPath = new[] { new Vector3(-7.2f, 0f, 2.5f), new Vector3(-10.5f, 0f, 2.6f), new Vector3(-10.5f, 0f, 5.0f) };
            cast.deckAwaySpots = new[]
            {
                Spot(root, "DeckAway_A", new Vector3(-12.0f, 0f, 5.75f), 0f),
                Spot(root, "DeckAway_B", new Vector3(-12.8f, 0f, 5.75f), 0f),
            };
            // Mép nam bàn sơ chế, nửa đông (nửa tây là thớt rau của người chơi).
            cast.greensSpots = new[]
            {
                Spot(root, "Greens_A", new Vector3(0.3f, 0f, -0.85f), 0f),
                Spot(root, "Greens_B", new Vector3(0.85f, 0f, -0.85f), 0f),
            };
            cast.jacquesPots = Spot(root, "Jacques_Pots", new Vector3(-1.9f, 0f, -2.0f), 180f);
            // Jacques đứng phía tây nồi cao, đối diện Ba trên ghế đẩu (phía đông).
            cast.jacquesTallPot = Spot(root, "Jacques_TallPot", new Vector3(GalleySceneBuilder.TallPotSpot.x - 0.78f, 0f, GalleySceneBuilder.TallPotSpot.z), 90f);
            cast.maiSpot = Spot(root, "Mai_Spot", new Vector3(4.7f, 0f, 1.6f), 200f);
            cast.teachSeats = new[]
            {
                Spot(root, "Teach_A", new Vector3(-4.45f, 0f, 1.45f), 180f),
                Spot(root, "Teach_B", new Vector3(-4.4f, 0f, -0.2f), 0f),
            };
            var stools = BuilderUtils.Group("TeachStools", root);
            foreach (var seat in cast.teachSeats)
                BuilderUtils.ArtSlot("Stool", stools.transform, seat.position, Vector3.zero, t =>
                {
                    BuilderUtils.Block("Seat", t, new Vector3(0f, 0.43f, 0f), new Vector3(0.38f, 0.04f, 0.38f), m.Wood, false, false);
                    foreach (var x in new[] { -0.15f, 0.15f })
                        foreach (var z in new[] { -0.15f, 0.15f })
                            BuilderUtils.Block("Leg", t, new Vector3(x, 0.21f, z), new Vector3(0.04f, 0.42f, 0.04f), m.Wood, false, false);
                    return t.gameObject;
                });
            BuilderUtils.ClearStatic(stools);
            stools.SetActive(false);
            cast.teachStools = stools;
            cast.silhouette = System.Array.Find(layout.eveningObjects, g => g != null && g.name.Contains("JacquesSilhouette"));
            cast.galleyDoor = layout.galleyDoor;
            cast.greens = greens;
            cast.scrub = scrub;
            // Ẩn sẵn ở chỗ đứng hợp lệ (chân trên sàn, không lấn vật cản); DayCast đặt lại theo ngày.
            cast.soldierA.transform.position = cast.deckSpots[0].position;
            cast.soldierB.transform.position = cast.deckSpots[1].position;
            cast.jacques.transform.position = CorridorToGalley[CorridorToGalley.Length - 1];
            cast.mai.transform.position = cast.maiSpot.position;
            return cast;
        }

        static Transform Spot(Transform parent, string name, Vector3 position, float yaw)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = position;
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return t;
        }

        /// <summary>NPC của ngày: thân theo vai (thiếu prefab thì vai dự phòng, rồi khối xám), nhường người chơi, ẩn lúc đầu.</summary>
        static NpcAgent CastNpc(Transform root, string role, string fallbackRole, Color clothes)
        {
            string body = role;
            if (fallbackRole != null && AssetDatabase.LoadAssetAtPath<GameObject>(CharacterImporter.PrefabRoot + role + ".prefab") == null)
                body = fallbackRole;
            var agent = MakeNpc(root, body, new Vector3(0f, 0f, -20f), 0f, clothes, clothes * 0.6f);
            agent.name = "Npc_" + role;
            agent.yieldToPlayer = true;
            agent.walkSpeed = 1.05f;
            agent.gameObject.SetActive(false);
            return agent;
        }

        /// <summary>
        /// Hành khách và thủy thủ trên boong giếng trước (phía mũi, quanh cột buồm trước), tọa độ thế giới: nhóm trò chuyện,
        /// người tựa lan can nhìn biển, hai thủy thủ đi lại. Một nửa là "optional" (tầng Laptop bớt). CrowdLod giới hạn số
        /// Animator. Lớp ánh sáng ngoài trời. Ngày bão DayScenery tắt cả nhóm.
        /// </summary>
        static GameObject BuildDeckCrowd(Transform parent)
        {
            var crowd = CrowdBuilder.Root(parent, "DeckCrowd");
            // Thủy thủ (thân Jacques, đổi màu theo seed) trò chuyện; hành khách là lính về nước (thân SoldierB) tựa lan can.
            CrowdBuilder.AddGroup(crowd, "Sailors", new Vector3(-18.6f, 0f, -3.6f), 3, 0.9f, 20f, 2101, role: "Jacques");
            CrowdBuilder.AddStand(crowd, new Vector3(-18.2f, 0f, 5.8f), 0f, 2111, lean: true, role: "SoldierB");
            CrowdBuilder.AddStand(crowd, new Vector3(-14.5f, 0f, -7.5f), 180f, 2112, lean: true, optional: true, role: "SoldierB");
            CrowdBuilder.AddStand(crowd, new Vector3(-20.9f, 0f, -7.4f), 180f, 2113, lean: true, optional: true, role: "Jacques");
            CrowdBuilder.AddWander(crowd, "DeckWalk", new[]
            {
                new Vector3(-17.2f, 0f, 2.9f), new Vector3(-20.6f, 0f, 3.6f), new Vector3(-20.6f, 0f, -4.6f), new Vector3(-17.2f, 0f, -5.2f),
            }, 2, 2121, optional: true, role: "Jacques");
            CrowdBuilder.AddLod(crowd, 8, 4, 22f, 70f, 140f);
            BuilderUtils.SetRenderingLayer(crowd.gameObject, WorldRenderingLayers.Exterior);
            return crowd.gameObject;
        }

        /// <summary>NPC đứng, cao 1,75 m; khối xám: thân, đầu, tạp dề. Collider capsule chặn người chơi đi xuyên.</summary>
        static NpcAgent MakeNpc(Transform parent, string role, Vector3 position, float yaw, Color apron, Color clothes)
        {
            var go = new GameObject("Npc_" + role);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            // Nhân vật đã nhập (Tools/import_characters.sh): đúng tỉ lệ mét, gốc ở chân, không co giãn như ArtSlot.
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterImporter.PrefabRoot + role + ".prefab");
            if (character != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(character, go.transform);
                inst.name = "Body";
            }
            else
            {
                var clothMat = AssetFactory.Lit("Npc" + role + "Clothes", clothes, 0.1f);
                var apronMat = AssetFactory.Lit("Npc" + role + "Apron", apron, 0.1f);
                var skin = AssetFactory.Lit("Skin", new Color(0.78f, 0.6f, 0.47f), 0.35f);
                var body = new GameObject("Body").transform;
                body.SetParent(go.transform, false);
                BuilderUtils.Prim(PrimitiveType.Capsule, "Torso", body, new Vector3(0f, 0.78f, 0f), new Vector3(0.44f, 0.78f, 0.3f), clothMat, false, false);
                BuilderUtils.Prim(PrimitiveType.Sphere, "Head", body, new Vector3(0f, 1.62f, 0f), new Vector3(0.22f, 0.26f, 0.24f), skin, false, false);
                BuilderUtils.Block("Apron", body, new Vector3(0f, 0.75f, 0.16f), new Vector3(0.36f, 0.75f, 0.02f), apronMat, false, false);
            }
            BuilderUtils.ClearStatic(go);
            var col = go.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.875f, 0f);
            col.height = 1.75f;
            col.radius = 0.24f;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            var carry = new GameObject("CarryPoint").transform;
            carry.SetParent(go.transform, false);
            // Trước bụng, trong tầm hai tay (vai cao ~1,42 m, tay dài ~0,55 m): tay IK bám quai nồi, thành thùng ở đây. Cách thân 0,42 m:
            // ở 0,34 m đùi vung lên khi đi (CarryWalk) lún vào đáy nồi Ø 0,36 m 6–7 cm (bot ngày 3 18/10, LoosePot).
            carry.localPosition = new Vector3(0f, 0.9f, 0.42f);
            var agent = go.AddComponent<NpcAgent>();
            agent.carryPoint = carry;
            // Quản bếp và phụ bếp đi lại trên lối z ~0,8 giữa bàn và lò: người chơi kẹp giữa hai người thì họ nhường (tắt collider)
            // thay vì cùng đứng chờ nhau vô hạn (playthrough D2.dawn: kẹt 5 phút).
            agent.yieldToPlayer = true;
            return agent;
        }
    }
}
