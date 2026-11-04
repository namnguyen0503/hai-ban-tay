using HaiBanTay.Editor.Setup;
using HaiBanTay.Interaction;
using HaiBanTay.Narrative;
using HaiBanTay.Shift;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders.Ship
{
    /// <summary>
    /// Các phòng của khối bếp, chung cho ba cảnh; tọa độ theo hệ cục bộ bếp cũ nên trạm việc và lộ trình NPC giữ nguyên.
    /// Bếp 10 × 6 m (trần 2,6 m); phòng rửa sau cửa tường đông; hầm than sau cửa tường nam; ô chuyển món tường bắc.
    /// </summary>
    public static partial class ShipInterior
    {
        /// <summary>
        /// Phòng rửa: x 3,3…7,05, z −3,1…1,0 (3,75 × 4,1 m), cùng cao độ với bếp. Vách ngăn với bếp ở x 3,3 có cửa rộng 1,2 m (z −0,3…0,9,
        /// sát tường bắc phòng): nồi cao Ø 0,8 m kéo lê và người đi lùi lọt. Phần tây (x 3,3…5,05) dùng sàn, trần, tường nam của bếp.
        /// </summary>
        public static readonly Vector3 WashRoomCenter = new Vector3(5.175f, 0f, -1.05f);
        /// <summary>Tâm phần đông (x 5,05…7,05) có sàn và trần riêng của phòng rửa.</summary>
        static readonly Vector3 WashEastCenter = new Vector3(6.05f, 0f, -1.05f);
        /// <summary>Khối sàn bếp (hệ bếp): phủ x −5,1…5,2, z −3,1…3,1.</summary>
        static readonly Vector3 GalleyFloorCenter = new Vector3(0.05f, -0.05f, 0f), GalleyFloorSize = new Vector3(10.3f, 0.1f, 6.2f);
        /// <summary>Mặt phẳng giữa của vách ngăn bếp–phòng rửa.</summary>
        public const float WashPartitionX = 3.3f;
        /// <summary>Hầm than: x −0,25…3,25, z −5,65…−3,05; cửa ở tường nam của bếp, x 1,0…1,9.</summary>
        public static readonly Vector3 CoalHoldCenter = new Vector3(1.5f, 0f, -4.35f);

        public sealed class Mats
        {
            public readonly Material Floor = AssetFactory.Lit("GalleyFloor", new Color(0.33f, 0.24f, 0.17f), 0.15f);
            public readonly Material Wall = AssetFactory.Lit("GalleyWall", new Color(0.55f, 0.5f, 0.42f), 0.1f);
            public readonly Material Ceiling = AssetFactory.Lit("GalleyCeiling", new Color(0.38f, 0.33f, 0.27f), 0.1f);
            public readonly Material Iron = AssetFactory.Lit("Iron", new Color(0.22f, 0.22f, 0.23f), 0.4f, 0.6f);
            public readonly Material Wood = AssetFactory.Lit("Wood", new Color(0.4f, 0.27f, 0.16f), 0.15f);
            public readonly Material Coal = AssetFactory.Lit("LooseCoal", new Color(0.075f, 0.075f, 0.08f), 0.32f);
            public readonly Material Copper = AssetFactory.Lit("Copper", new Color(0.72f, 0.42f, 0.25f), 0.65f, 0.8f);
            public readonly Material CopperInside = AssetFactory.Lit("CopperInside", new Color(0.45f, 0.26f, 0.15f), 0.4f, 0.7f);
            public readonly Material Brass = AssetFactory.Lit("Brass", new Color(0.7f, 0.58f, 0.3f), 0.55f, 0.8f);
            public readonly Material Fire = AssetFactory.Embers("Fire", new Color(1.5f, 0.85f, 0.45f), 0.025f, 0.22f);
            public readonly Material Cold = AssetFactory.Lit("ColdWall", new Color(0.55f, 0.62f, 0.68f), 0.2f);
            public readonly Material Cloth = AssetFactory.Lit("Sleeve", new Color(0.42f, 0.38f, 0.3f), 0.1f);
            public readonly Material SailorShirt = AssetFactory.Lit("SailorShirt", new Color(0.8f, 0.8f, 0.78f), 0.1f);
            public readonly Material SailorCap = AssetFactory.Lit("SailorCap", new Color(0.1f, 0.12f, 0.2f), 0.1f);
            public readonly Material Shadow = AssetFactory.Lit("Silhouette", new Color(0.04f, 0.04f, 0.05f), 0.05f);
            public readonly Material Paper = AssetFactory.Lit("Paper", new Color(0.86f, 0.8f, 0.66f), 0.05f);
            public readonly Material Skin = AssetFactory.Lit("Skin", new Color(0.78f, 0.6f, 0.47f), 0.35f);
            public readonly Material Soot = AssetFactory.Lit("Soot", new Color(0.13f, 0.11f, 0.1f), 0.2f, 0.3f);
            public readonly Material Water = AssetFactory.Lit("Water", new Color(0.16f, 0.2f, 0.2f), 0.92f);
            // Phấn hơi phát sáng (ChalkboardView gán RenderTexture vào cả _EmissionMap) để đọc được trong bếp tối.
            public readonly Material Chalkboard = AssetFactory.Emissive("Chalkboard", Color.white, new Color(0.45f, 0.45f, 0.45f),
                bakeGlow: false, smoothness: 0.05f);
            public readonly Material ClockFace = AssetFactory.Lit("ClockFace", new Color(0.9f, 0.86f, 0.76f), 0.3f);
            public readonly Material Hull = AssetFactory.Lit("Hull", new Color(0.24f, 0.22f, 0.2f), 0.25f, 0.5f);
            public readonly Material Nameplate = AssetFactory.Emissive("Nameplate", Color.white, new Color(0.3f, 0.3f, 0.3f),
                bakeGlow: false, smoothness: 0.6f);
            public readonly Material LampGlass = Lamps.Glow();
            public readonly Material Bristle = AssetFactory.Lit("Bristle", new Color(0.48f, 0.38f, 0.22f), 0.05f);
            public readonly Material Ash = AssetFactory.Lit("Ash", new Color(0.42f, 0.4f, 0.38f), 0.05f);
            public readonly Material CuttingBoard = AssetFactory.Lit("CuttingBoard", new Color(0.66f, 0.54f, 0.38f), 0.15f);
            public readonly Material Leaf = AssetFactory.Lit("Leaf", new Color(0.25f, 0.5f, 0.18f), 0.25f);
            public readonly Material Wicker = AssetFactory.Lit("Wicker", new Color(0.5f, 0.38f, 0.22f), 0.1f);
            // Vòng phấn trên sàn chỉ chỗ đặt chảo: hơi sáng để thấy trong bếp tối.
            public readonly Material ChalkMark = AssetFactory.Emissive("ChalkMark", new Color(0.85f, 0.84f, 0.78f), new Color(0.25f, 0.25f, 0.23f),
                bakeGlow: false, smoothness: 0.05f);
        }

        /// <summary>Phòng bếp chính (trừ tường tây do BuildWestWall dựng); trả về nhóm "Galley".</summary>
        static Transform BuildRoom(Transform env, Mats m)
        {
            var room = BuilderUtils.Group("Galley", env).transform;
            BuilderUtils.Block("Floor", room, GalleyFloorCenter, GalleyFloorSize, m.Floor);
            BuilderUtils.Block("Ceiling", room, new Vector3(0f, 2.65f, 0f), new Vector3(10.2f, 0.1f, 6.2f), m.Ceiling);
            // Tường bắc có ô chuyển món (x 3,75…4,65, cao 0,95…1,75).
            // Tường bắc bắt đầu từ mặt trong tường tây để ô cửa bếp (z 2,15…3,05) thông suốt bề dày tường.
            BuilderUtils.Block("Wall_N", room, new Vector3(-0.625f, 1.3f, 3.05f), new Vector3(8.75f, 2.6f, 0.1f), m.Wall);
            BuilderUtils.Block("Wall_N_East", room, new Vector3(4.875f, 1.3f, 3.05f), new Vector3(0.45f, 2.6f, 0.1f), m.Wall);
            BuilderUtils.Block("Wall_N_Sill", room, new Vector3(4.2f, 0.475f, 3.05f), new Vector3(0.9f, 0.95f, 0.1f), m.Wall);
            BuilderUtils.Block("Wall_N_Head", room, new Vector3(4.2f, 2.175f, 3.05f), new Vector3(0.9f, 0.85f, 0.1f), m.Wall);
            // Tường nam có cửa xuống hầm than (x 1,0…1,9).
            // Cửa phòng ăn nhân viên ở x −1,35…−0,45 (story11b), đông bếp gang (x −4,6…−1,4) để lối vào không bị lò chắn; độc lập cửa hầm than.
            BuilderUtils.Block("Wall_S", room, new Vector3(-3.175f, 1.3f, -3.05f), new Vector3(3.65f, 2.6f, 0.1f), m.Wall);
            BuilderUtils.Block("Wall_S_DiningEast", room, new Vector3(0.275f, 1.3f, -3.05f), new Vector3(1.45f, 2.6f, 0.1f), m.Wall);
            BuilderUtils.Block("Wall_S_DiningHead", room, new Vector3(-0.9f, 2.35f, -3.05f), new Vector3(0.9f, 0.5f, 0.1f), m.Wall);
            BuilderUtils.Block("Wall_S_East", room, new Vector3(3.5f, 1.3f, -3.05f), new Vector3(3.2f, 2.6f, 0.1f), m.Wall);
            BuilderUtils.Block("Wall_S_Lintel", room, new Vector3(1.45f, 2.325f, -3.05f), new Vector3(0.9f, 0.55f, 0.1f), m.Wall);
            // Vách ngăn bếp–phòng rửa ở x 3,3 (thay tường đông cũ ở x 5,05): cửa rộng 1,2 m ở z −0,3…0,9; tường bắc phòng rửa nối vách
            // với tường hành lang (z 0,9…1,0, x 3,3…5,1), cũng là mép nam của cửa hành lang.
            BuilderUtils.Block("Wall_Wash_W", room, new Vector3(WashPartitionX, 1.3f, -1.7f), new Vector3(0.1f, 2.6f, 2.8f), m.Wall);
            BuilderUtils.Block("Wall_Wash_Lintel", room, new Vector3(WashPartitionX, 2.325f, 0.3f), new Vector3(0.1f, 0.55f, 1.2f), m.Wall);
            BuilderUtils.Block("Wall_Wash_N", room, new Vector3(4.2f, 1.3f, 0.95f), new Vector3(1.8f, 2.6f, 0.1f), m.Wall);
            // Tường đông của bếp còn lại từ cửa hành lang (z 1…2) lên phía bắc.
            BuilderUtils.Block("Wall_E_North", room, new Vector3(5.05f, 1.3f, 2.5f), new Vector3(0.1f, 2.6f, 1f), m.Wall);
            BuilderUtils.Block("Wall_E_Lintel", room, new Vector3(5.05f, 2.35f, 1.5f), new Vector3(0.1f, 0.5f, 1f), m.Wall);
            // Dầm trần cho cảm giác thân tàu.
            for (int i = -2; i <= 2; i++)
                BuilderUtils.Block("Beam_" + (i + 2), room, new Vector3(i * 2f, 2.52f, 0f), new Vector3(0.15f, 0.16f, 6f), m.Wood);

            // Khoang nấu của Stove_B đã gộp vào dãy bắc trong BuildCoal; giữ nguyên vị trí NPC.

            // Bàn sơ chế giữa phòng.
            BuilderUtils.ArtSlot("PrepTable", room, Vector3.zero, Vector3.zero, t =>
            {
                BuilderUtils.Block("Top", t, new Vector3(0f, 0.86f, 0f), new Vector3(2f, 0.08f, 0.9f), m.Wood);
                foreach (var x in new[] { -0.9f, 0.9f })
                    foreach (var z in new[] { -0.38f, 0.38f })
                        BuilderUtils.Block("Leg", t, new Vector3(x, 0.41f, z), new Vector3(0.08f, 0.82f, 0.08f), m.Wood);
                return t.gameObject;
            });

            // Giá treo nồi (trang trí) trên tường tây.
            BuilderUtils.ArtSlot("PotRack", room, new Vector3(-4.85f, 1.9f, -1.2f), Vector3.zero, t =>
            {
                BuilderUtils.Block("Rail", t, Vector3.zero, new Vector3(0.06f, 0.06f, 1.6f), m.Iron);
                for (int i = 0; i < 4; i++)
                    BuilderUtils.Prim(PrimitiveType.Cylinder, "HangingPot", t, new Vector3(0.12f, -0.25f, -0.6f + i * 0.4f),
                        new Vector3(0.24f, 0.1f, 0.24f), m.Copper, true, false);
                return t.gameObject;
            });

            // Đèn treo phía đông bếp (bake): thay hai đèn fill vô hình cũ. Góc cọ nồi (B2.5) nằm dưới đèn này: đủ sáng để thấy lớp bẩn đổi màu.
            Props.HangingLamp(room, new Vector3(2.4f, 2.6f, -0.8f), realtime: false, intensity: 12f, range: 7.5f).name = "Lamp_Prep";
            Props.HangingLamp(room, new Vector3(-1.4f, 2.6f, -0.6f), realtime: false, intensity: 18f, range: 7f).name = "Lamp_RangeFill";
            return room;
        }

        /// <summary>Góc buổi tối cạnh ô cửa sổ: bàn nhỏ, đèn dầu, Jacques thật ngồi nghỉ, sách, ghế đẩu.</summary>
        static GameObject[] BuildEveningCorner(Transform env, Mats m, out Transform spawn, out InteractTarget restSeat, out Light oilLamp)
        {
            var corner = BuilderUtils.Group("EveningCorner", env).transform;
            BuilderUtils.ArtSlot("SmallTable", corner, new Vector3(-4.4f, 0f, 0.6f), Vector3.zero, t =>
            {
                BuilderUtils.Block("Top", t, new Vector3(0f, 0.74f, 0f), new Vector3(0.7f, 0.04f, 0.6f), m.Wood);
                BuilderUtils.Block("Leg", t, new Vector3(0f, 0.36f, 0f), new Vector3(0.1f, 0.72f, 0.1f), m.Wood);
                return t.gameObject;
            });
            var lamp = Props.OilLamp(corner, new Vector3(-4.55f, 0.76f, 0.75f), true, 2.4f, 5f, out var lampLight);
            oilLamp = lampLight;
            // Dùng cùng prefab Rocketbox và NpcAgent như DayCast; giữ tên để ẩn khi có người dạy ngồi.
            // Quay về bàn đèn; chân tránh xô tro và ghế nghỉ của người chơi.
            var silhouette = CrowdBuilder.AddPose(corner, new Vector3(-3.85f, 0f, 2f), 235f, 711, "SitIdle", role: "Jacques").gameObject;
            silhouette.name = "ArtSlot_JacquesSilhouette";
            silhouette.tag = "NPC";
            silhouette.layer = BuilderUtils.Layer("NPC");
            // Mũ rời của FBX không theo xương đầu; Jacques bỏ mũ khi nghỉ trong bếp.
            foreach (var part in silhouette.GetComponentsInChildren<Transform>(true))
                if (part.name == "Jacques_SoftCap")
                    part.gameObject.SetActive(false);
            // Clip ngồi lệch hông khỏi gốc: lấy pose thật để ghế đỡ mông, cùng ẩn với NPC khi có người dạy.
            var animator = silhouette.GetComponentInChildren<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                throw new System.InvalidOperationException("Jacques thiếu avatar humanoid.");
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.Update(0f);
            if (!animator.HasState(0, Animator.StringToHash("SitIdle")))
                throw new System.InvalidOperationException("Jacques thiếu clip SitIdle.");
            animator.Play("SitIdle", 0, 0f);
            animator.Update(0f);
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips).position;
            float seatHeight = hips.y - silhouette.transform.position.y - 0.1f;
            if (seatHeight <= 0.08f)
                throw new System.InvalidOperationException("Pose Jacques không có độ cao ghế hợp lệ: " + seatHeight);
            var stoolPosition = silhouette.transform.InverseTransformPoint(new Vector3(hips.x, silhouette.transform.position.y, hips.z));
            BuilderUtils.ArtSlot("Stool", silhouette.transform, stoolPosition, Vector3.zero, t =>
            {
                BuilderUtils.Block("Seat", t, new Vector3(0f, seatHeight - 0.02f, 0f), new Vector3(0.38f, 0.04f, 0.38f), m.Wood, false, false);
                foreach (float x in new[] { -0.15f, 0.15f })
                    foreach (float z in new[] { -0.15f, 0.15f })
                        BuilderUtils.Block("Leg", t, new Vector3(x, (seatHeight - 0.04f) * 0.5f, z), new Vector3(0.04f, seatHeight - 0.04f, 0.04f), m.Wood, false, false);
                return t.gameObject;
            });
            var book = BuilderUtils.ArtSlot("Book", corner, new Vector3(-4.35f, 0.78f, 0.5f), new Vector3(0f, 20f, 0f), t =>
            {
                BuilderUtils.Block("Pages", t, Vector3.zero, new Vector3(0.28f, 0.03f, 0.2f), m.Paper, false, false);
                return t.gameObject;
            });
            // Ghế nghỉ sát bàn đèn, chừa lối thoát góc tây bắc trước xô tro.
            var seat = BuilderUtils.ArtSlot("Stool", corner, new Vector3(-3.8f, 0f, 0.4f), Vector3.zero, t =>
            {
                BuilderUtils.Block("Seat", t, new Vector3(0f, 0.43f, 0f), new Vector3(0.38f, 0.04f, 0.38f), m.Wood, true, false);
                foreach (var x in new[] { -0.15f, 0.15f })
                    foreach (var z in new[] { -0.15f, 0.15f })
                        BuilderUtils.Block("Leg", t, new Vector3(x, 0.21f, z), new Vector3(0.04f, 0.42f, 0.04f), m.Wood, true, false);
                return t.gameObject;
            });
            seat.name = "RestSeat";
            seat.layer = BuilderUtils.Layer("Interactable");
            seat.tag = "Interactable";
            var seatCol = seat.AddComponent<BoxCollider>();
            seatCol.center = new Vector3(0f, 0.25f, 0f);
            seatCol.size = new Vector3(0.45f, 0.5f, 0.45f);
            restSeat = seat.AddComponent<InteractTarget>();
            restSeat.promptKey = "prompt.rest";

            spawn = new GameObject("EveningSpawn").transform;
            spawn.SetParent(corner, false);
            spawn.localPosition = new Vector3(-3.35f, 0f, 1.0f);
            spawn.localRotation = Quaternion.Euler(0f, -105f, 0f);
            return new[] { lamp, silhouette, book };
        }

        static void BuildWashRoom(Transform env, Mats m, out PlayerArea area)
        {
            var r = BuilderUtils.Group("WashRoom", env).transform;
            var c = WashEastCenter;
            // Sàn phần đông nối tiếp mép đông sàn bếp (x 5,2) không chồng lên nó, và lấy pha UV theo gốc sàn bếp: hai khối cùng mặt phẳng
            // chồng 15 cm, mỗi khối bắt đầu ô gạch ở tâm riêng thì thành vệt nối thấy rõ (QA-0142).
            float floorWest = GalleyFloorCenter.x + GalleyFloorSize.x * 0.5f, floorEast = c.x + 1f;
            var floorCentre = new Vector3((floorWest + floorEast) * 0.5f, -0.05f, c.z);
            var floorSize = new Vector3(floorEast - floorWest, 0.1f, 4.1f);
            var floor = BuilderUtils.Block("Floor", r, floorCentre, floorSize, m.Floor);
            var floorMesh = floor.GetComponent<MeshFilter>();
            floorMesh.sharedMesh = SurfaceMesh.For(PrimitiveType.Cube, floorMesh.sharedMesh, floorSize,
                AssetFactory.SurfaceTile(m.Floor), floorCentre - GalleyFloorCenter);
            BuilderUtils.Block("Ceiling", r, c + new Vector3(0f, 2.65f, 0f), new Vector3(2f, 0.1f, 4.1f), m.Ceiling);
            BuilderUtils.Block("Wall_E", r, new Vector3(7.0f, 1.3f, c.z), new Vector3(0.1f, 2.6f, 4.1f), m.Wall);
            BuilderUtils.Block("Wall_S", r, new Vector3(c.x, 1.3f, -3.05f), new Vector3(2f, 2.6f, 0.1f), m.Wall);
            Props.HangingLamp(r, new Vector3(6.0f, 2.6f, -1.4f), realtime: false, intensity: 12f, range: 6.5f).name = "Lamp_Wash";
            // Góc nồi cao ở phía tây phòng: đèn riêng ngay trên nồi để thấy lớp bẩn trong lòng nồi.
            Props.HangingLamp(r, new Vector3(4.35f, 2.6f, -1.7f), realtime: false, intensity: 12f, range: 6.5f).name = "Lamp_WashWest";
            // Đèn gas gắn vách đông (mặt trong vách ở x 6,95): sáng thật, thay đèn fill vô hình cũ của phòng rửa.
            Props.WallLantern(r, "Lantern_Wash", new Vector3(6.95f, 1.75f, -0.2f), -90f, realtime: false, intensity: 7f, range: 5f);
            area = BuilderUtils.Trigger<PlayerArea>("WashRoomArea", r, WashRoomCenter + new Vector3(0f, 1.2f, 0f), new Vector3(3.5f, 2.4f, 3.8f));
        }

        static void BuildCoalHold(Transform env, Mats m, out PlayerArea area)
        {
            var r = BuilderUtils.Group("CoalHold", env).transform;
            var c = CoalHoldCenter;
            BuilderUtils.Block("Floor", r, c + new Vector3(0f, -0.05f, 0f), new Vector3(3.5f, 0.1f, 2.6f), m.Floor);
            BuilderUtils.Block("Ceiling", r, c + new Vector3(0f, 2.65f, 0f), new Vector3(3.5f, 0.1f, 2.6f), m.Hull);
            BuilderUtils.Block("Wall_W", r, new Vector3(-0.25f, 1.3f, c.z), new Vector3(0.1f, 2.6f, 2.6f), m.Hull);
            BuilderUtils.Block("Wall_E", r, new Vector3(3.25f, 1.3f, c.z), new Vector3(0.1f, 2.6f, 2.6f), m.Hull);
            BuilderUtils.Block("Wall_S", r, new Vector3(c.x, 1.3f, -5.65f), new Vector3(3.6f, 2.6f, 0.1f), m.Hull);
            // Đống than dồn ở vách sau.
            // Đống than lún tối đa 0,15 m dưới sàn, không xuyên vách sau: khối bếp nằm trọn trong interior_volumes.galley_block.
            BuildCoalHeap(r, "CoalHeap", new Vector3(1.6f, 0.3f, -5.1f), new Vector3(2.6f, 0.9f, 1.1f), m.Coal);
            BuildCoalHeap(r, "CoalHeap_Small", new Vector3(2.6f, 0.15f, -4.9f), new Vector3(0.9f, 0.6f, 0.8f), m.Coal);
            BuilderUtils.ArtSlot("Ladder", r, new Vector3(3.05f, 0f, -3.65f), new Vector3(0f, -90f, 0f), t =>
            {
                foreach (var x in new[] { -0.22f, 0.22f })
                    BuilderUtils.Block("Rail", t, new Vector3(x, 0.86f, 0f), new Vector3(0.05f, 1.72f, 0.06f), m.Wood);
                for (int i = 0; i < 5; i++)
                    BuilderUtils.Block("Rung", t, new Vector3(0f, 0.25f + i * 0.33f, 0f), new Vector3(0.44f, 0.04f, 0.04f), m.Wood);
                return t.gameObject;
            });
            BuilderUtils.ArtSlot("Crate", r, new Vector3(0.35f, 0f, -3.55f), new Vector3(0f, 90f, 0f), t =>
            {
                BuilderUtils.Block("Body", t, new Vector3(0f, 0.2f, 0f), new Vector3(0.83f, 0.4f, 0.4f), m.Wood);
                return t.gameObject;
            });
            // Một đèn treo duy nhất (không còn đèn fill vô hình): đủ mạnh để trần và đống than quanh đèn hiện vũng sáng.
            Props.HangingLamp(r, new Vector3(1.5f, 2.6f, -4.3f), realtime: false, intensity: 12f, range: 6.5f).name = "Lamp_Hold";
            area = BuilderUtils.Trigger<PlayerArea>("CoalHoldArea", r, c + new Vector3(0f, 1.2f, 0f), new Vector3(3.4f, 2.4f, 2.5f));
        }

        /// <summary>
        /// Đống than đổ dồn: ụ thấp sườn gồ ghề (mesh nhận sáng, không phải khối đen phẳng) phủ dày cục than góc cạnh to nhỏ
        /// khác nhau nằm đúng trên mặt ụ, vài cục lăn ra chân đống. center.y cũ là tâm khối; đáy ụ đặt ở sàn.
        /// </summary>
        static void BuildCoalHeap(Transform parent, string name, Vector3 center, Vector3 size, Material coal)
        {
            var root = BuilderUtils.Group(name, parent).transform;
            root.localPosition = new Vector3(center.x, 0f, center.z);
            float rx = size.x * 0.5f, rz = size.z * 0.5f, h = Mathf.Max(0.2f, center.y + size.y * 0.5f) * 0.85f;
            var mound = BuilderUtils.Group("CoalBulk", root);
            mound.transform.localScale = new Vector3(rx, h, rz);
            mound.AddComponent<MeshFilter>().sharedMesh = AssetFactory.CoalMoundMesh();
            mound.AddComponent<MeshRenderer>().sharedMaterial = coal;
            var box = root.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, h * 0.4f, 0f);
            box.size = new Vector3(size.x * 0.85f, h * 0.8f, size.z * 0.85f);
            var mesh = AssetFactory.CoalLumpMesh();
            var rng = new System.Random(name.Length * 7919 + (int)(size.x * 100f));
            int count = Mathf.CeilToInt(size.x * size.z * 55f);
            for (int i = 0; i < count; i++)
            {
                // Rải đều theo diện tích đĩa; một phần nhỏ lăn ra ngoài chân đống.
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Sqrt((float)rng.NextDouble()) * (i % 9 == 0 ? 1.12f : 0.96f);
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                float y = r < 1f ? AssetFactory.CoalMoundHeight(x, z) * h : 0f;
                float chunk = 0.05f + 0.11f * (float)Mathf.Pow((float)rng.NextDouble(), 1.8f);
                var lump = BuilderUtils.Prim(PrimitiveType.Cube, "CoalLump", root,
                    new Vector3(x * rx, y + chunk * 0.15f, z * rz),
                    new Vector3(chunk, chunk * (0.55f + 0.3f * (float)rng.NextDouble()), chunk * (0.8f + 0.4f * (float)rng.NextDouble())),
                    coal, true, false,
                    new Vector3((float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f));
                lump.GetComponent<MeshFilter>().sharedMesh = mesh;
            }
            root.gameObject.isStatic = true;
            mound.isStatic = true;
        }

        /// <summary>Ô chuyển món ở tường bắc (x 3,75…4,65): bậu gỗ, chuông phục vụ, phía sau là lối đi tối sang phòng ăn.</summary>
        static void BuildServingHatch(Transform env, Mats m)
        {
            var r = BuilderUtils.Group("ServingHatch", env).transform;
            BuilderUtils.Block("Sill", r, new Vector3(4.2f, 0.97f, 3.0f), new Vector3(1.0f, 0.05f, 0.34f), m.Wood);
            BuilderUtils.Block("Frame_W", r, new Vector3(3.72f, 1.35f, 2.98f), new Vector3(0.06f, 0.8f, 0.14f), m.Wood);
            BuilderUtils.Block("Frame_E", r, new Vector3(4.68f, 1.35f, 2.98f), new Vector3(0.06f, 0.8f, 0.14f), m.Wood);
            BuilderUtils.Block("Frame_Top", r, new Vector3(4.2f, 1.77f, 2.98f), new Vector3(1.02f, 0.06f, 0.14f), m.Wood);
            // Lối đi phía sau.
            BuilderUtils.Block("Passage_Floor", r, new Vector3(4.2f, 0.9f, 3.5f), new Vector3(1.1f, 0.06f, 0.8f), m.Wood);
            BuilderUtils.Block("Passage_Back", r, new Vector3(4.2f, 1.35f, 3.95f), new Vector3(1.2f, 1.0f, 0.1f), m.Wall);
            BuilderUtils.Block("Passage_W", r, new Vector3(3.62f, 1.35f, 3.5f), new Vector3(0.1f, 1.0f, 0.9f), m.Wall);
            BuilderUtils.Block("Passage_E", r, new Vector3(4.78f, 1.35f, 3.5f), new Vector3(0.1f, 1.0f, 0.9f), m.Wall);
            BuilderUtils.Block("Passage_Top", r, new Vector3(4.2f, 1.83f, 3.5f), new Vector3(1.2f, 0.06f, 0.9f), m.Ceiling);
            // Đèn gas nhỏ gắn vách sau lối đi (mặt trong vách ở z 3,90), thay đèn vô hình cũ.
            Props.WallLantern(r, "Lantern_Passage", new Vector3(4.2f, 1.52f, 3.9f), 180f, realtime: false, intensity: 1.6f, range: 3.5f);
            var bell = BuilderUtils.Group("ServiceBell", r);
            bell.transform.localPosition = new Vector3(4.5f, 0.995f, 2.92f);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Base", bell.transform, new Vector3(0f, 0.005f, 0f), new Vector3(0.1f, 0.005f, 0.1f), m.Wood, true, false);
            BuilderUtils.Prim(PrimitiveType.Sphere, "Dome", bell.transform, new Vector3(0f, 0.03f, 0f), new Vector3(0.08f, 0.05f, 0.08f), m.Brass, true, false);
        }

        /// <summary>Biển tên tàu bằng đồng trên vách đông hành lang (đoạn rẽ ra cửa hông), ngang tầm mắt, mặt quay về phía tây.</summary>
        static void BuildNameplate(Transform env, Mats m)
        {
            var root = BuilderUtils.Group("ShipNameplate", env);
            root.transform.localPosition = new Vector3(6.49f, 1.45f, 2.9f);
            root.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            BuilderUtils.Block("Frame", root.transform, Vector3.zero, new Vector3(0.96f, 0.22f, 0.02f), m.Brass, true, false);
            var surface = BuilderUtils.Prim(PrimitiveType.Quad, "Surface", root.transform, new Vector3(0f, 0f, -0.011f),
                new Vector3(0.9f, 0.169f, 1f), m.Nameplate, false, false);
            root.layer = BuilderUtils.Layer("Interactable");
            root.tag = "Interactable";
            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(0.96f, 0.22f, 0.06f);
            var plate = root.AddComponent<ShipNameplate>();
            plate.surface = surface.GetComponent<Renderer>();
        }
    }
}
