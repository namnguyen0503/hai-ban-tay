using HaiBanTay.Npc;
using HaiBanTay.Editor.Setup;
using UnityEngine;
using static HaiBanTay.Editor.SceneBuilders.CrowdBuilder;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>
    /// Đám đông bến Nhà Rồng (cảnh 01, khoảng 50 người) theo Staging/art_env/NhaRong/layout.json (crowd.paths, crowd.spots) và
    /// các điểm neo. Hai gốc đám đông, mọi vị trí là tọa độ thế giới (gốc không xoay, không co giãn):
    /// - <b>Quay</b> (con của phông bờ, dời đi khi nhổ neo): phu bốc hàng vác bao lên cầu thang tàu, hàng khách lên tàu, người soát
    ///   vé, ông Mai, người đưa tiễn, người bán hàng, phu kho, người qua đường.
    /// - <b>Deck</b> (con của tàu, ở lại): thủy thủ tựa lan can, cuộn dây, hành khách vẫy tay, quản bếp ở cửa bếp.
    /// Lối bắt buộc của người chơi: xe khoai → chân cầu thang tàu → boong → thang hầm → hầm thực phẩm (<see cref="MandatoryRoute"/>).
    /// Không người đứng yên nào nằm trong <see cref="ClearRadius"/> của lối này; người đi lại (phu, khách) đi trên cầu thang tàu theo
    /// làn riêng và nhường người chơi (NpcAgent.yieldToPlayer).
    /// </summary>
    public static class NhaRongCrowd
    {
        public const string Porters = "PorterVN_A,PorterVN_B,PorterVN_C";
        public const string Passengers = "PassengerFR_M1,PassengerFR_F,PassengerFR_M2,CivilianVN,PassengerFR_F";
        public const string Locals = "CivilianVN,PassengerFR_M1,VendorVN_F,PorterVN_B,PassengerFR_F";

        /// <summary>Các điểm của lối bắt buộc (thế giới, người chơi vác bao đi qua theo thứ tự). Kiểm tra giữ trống bằng ClearRadius.</summary>
        public static readonly Vector3[] MandatoryRoute =
        {
            new Vector3(3f, -4.1f, -18f),        // chỗ xuất hiện
            new Vector3(4.9f, -4.1f, -17.4f),    // bao khoai cạnh xe (bắc xe)
            new Vector3(0.9f, -4.1f, -15.7f),
            new Vector3(-1.5f, -4.1f, -13.5f),   // chân cầu thang tàu
            new Vector3(-1.5f, 0f, -7.38f),      // đỉnh cầu thang tàu
            new Vector3(-1.5f, 0f, -5f),
            new Vector3(-1.5f, 0f, -3.0f),       // lối hẹp (≈ 1 m) giữa nhà boong và hộp thông gió, chạy về phía tây
            new Vector3(-9.4f, 0f, -2.9f),
            new Vector3(-9.514f, 0f, -2.2f),     // buồng thang có mái (cạnh cửa bếp)
            new Vector3(-9.514f, 0f, 0f),
            new Vector3(-8.614f, 0f, 0f),        // đầu thang hầm
            new Vector3(-5.064f, -2.8f, 0f),     // chân thang hầm
            new Vector3(-2.66f, -2.8f, 1.2f),    // chỗ xếp bao
        };

        /// <summary>Lối từ đầu cầu thang tàu tới cửa bếp (sau khi nhổ neo, B1.4): người đứng yên cũng không được chắn.</summary>
        public static readonly Vector3[] GalleyDoorRoute =
        {
            new Vector3(-1.5f, 0f, -5f), new Vector3(-1.5f, 0f, -3.0f), new Vector3(-9.0f, 0f, -3.0f), new Vector3(-9.3f, 0f, -3.45f),
            new Vector3(-10.4f, 0f, -3.45f),
        };

        /// <summary>Người đứng yên không được nằm gần lối bắt buộc hơn mức này (mét, theo mặt phẳng).</summary>
        public const float ClearRadius = 1.2f;

        /// <summary>Quản bếp đứng cạnh cửa bếp, phía nam lối vào buồng thang (cửa ở (−10, 0, −3,45), mở về +X).</summary>
        public static readonly Vector3 ChefSpot = new Vector3(-9.0f, 0f, -4.9f);

        public sealed class Result
        {
            public Transform quay, deck;
            public NpcAgent mai, purser, chef;
            public CrowdQueue queue;
        }

        static void Bark(CrowdRoutine r, int seed, float radius, float cooldown, params string[] ids)
        {
            var bark = r.gameObject.AddComponent<AmbientBark>();
            bark.lineIds = ids;
            bark.radius = radius;
            bark.cooldown = cooldown;
            bark.seed = seed;
        }

        /// <summary>Dựng đám đông bến. quayParent: gốc phông bờ (tọa độ cục bộ = thế giới); deckParent: gốc tàu đã dời để tọa độ cục bộ = thế giới.</summary>
        public static Result Build(Transform quayParent, Transform deckParent, EnvLayout env)
        {
            var res = new Result();
            res.quay = BuildQuay(quayParent, env, res);
            res.deck = BuildDeck(deckParent, res);
            return res;
        }

        /// <summary>Xe đẩy bán cà-rem: thùng gỗ trên hai bánh, thùng kẽm đựng kem, cán đẩy và dù che nắng. Gốc ở giữa đáy, thùng dài theo x.</summary>
        static void IceCreamCart(Transform crowd, Vector3 at)
        {
            var root = BuilderUtils.Group("IceCreamCart", crowd).transform;
            root.localPosition = at;
            var wood = AssetFactory.Lit("IceCartWood", new Color(0.78f, 0.7f, 0.52f), 0.25f);
            var trim = AssetFactory.Lit("IceCartTrim", new Color(0.36f, 0.5f, 0.42f), 0.3f);
            var zinc = AssetFactory.Lit("IceCartZinc", new Color(0.66f, 0.68f, 0.68f), 0.55f, 0.6f);
            var iron = AssetFactory.Lit("IceCartIron", new Color(0.12f, 0.11f, 0.1f), 0.3f, 0.4f);
            var shade = AssetFactory.Lit("IceCartShade", new Color(0.86f, 0.82f, 0.7f), 0.1f);
            BuilderUtils.Block("Box", root, new Vector3(0f, 0.8f, 0f), new Vector3(1.3f, 0.6f, 0.75f), wood, false, true);
            BuilderUtils.Block("Band", root, new Vector3(0f, 1.0f, 0f), new Vector3(1.32f, 0.08f, 0.77f), trim, false, false);
            BuilderUtils.Block("Lid", root, new Vector3(0f, 1.12f, 0f), new Vector3(1.34f, 0.04f, 0.79f), trim, false, false);
            for (int s = -1; s <= 1; s += 2)
            {
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Wheel_" + (s < 0 ? "N" : "S"), root, new Vector3(0.25f, 0.38f, s * 0.42f),
                    new Vector3(0.76f, 0.03f, 0.76f), iron, false, false, new Vector3(90f, 0f, 0f));
                BuilderUtils.Block("Handle_" + (s < 0 ? "N" : "S"), root, new Vector3(-0.95f, 0.95f, s * 0.3f), new Vector3(0.7f, 0.04f, 0.04f), iron, false, false);
            }
            BuilderUtils.Block("Grip", root, new Vector3(-1.3f, 0.95f, 0f), new Vector3(0.04f, 0.04f, 0.64f), iron, false, false);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Churn", root, new Vector3(0.3f, 1.3f, 0f), new Vector3(0.36f, 0.16f, 0.36f), zinc, false, false);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "ChurnCap", root, new Vector3(0.3f, 1.47f, 0f), new Vector3(0.3f, 0.02f, 0.3f), zinc, false, false);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Pole", root, new Vector3(-0.35f, 1.7f, 0f), new Vector3(0.03f, 0.6f, 0.03f), iron, false, false);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Parasol", root, new Vector3(-0.35f, 2.3f, 0f), new Vector3(1.7f, 0.03f, 1.7f), shade, false, false);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "ParasolTop", root, new Vector3(-0.35f, 2.36f, 0f), new Vector3(0.9f, 0.05f, 0.9f), shade, false, false);
        }

        static Transform BuildQuay(Transform parent, EnvLayout env, Result res)
        {
            const float y = -4.1f;
            var crowd = Root(parent, "Crowd_Quay");

            // 1) Phu bốc hàng: vác bao từ đống hàng phía đông xe khoai, lên cầu thang tàu (làn lên x −1,15, làn xuống x −1,85: cầu thang rộng 1,4 m tâm x −1,5, mặt trong thành chặn ẩn ở −0,80 và −2,20; thân
            // phu bán kính 0,25 nên làn cách thành 0,1 m, ở ±0,4 chỉ còn 5 cm và lúc lách nhau thân lấn thành, bot 22–23/10), đặt ở
            // đầu cầu thang trên boong rồi về. Hai người cuối chỉ có ở tầng PC.
            var up = new[]
            {
                new Vector3(8.6f, y, -19.4f), new Vector3(8.0f, y, -16.6f), new Vector3(1.0f, y, -16.6f), new Vector3(-1.15f, y, -15.2f),
                new Vector3(-1.15f, y, -13.5f), new Vector3(-1.15f, 0f, -7.38f), new Vector3(-1.15f, 0f, -6.1f),
            };
            var down = new[]
            {
                new Vector3(-1.85f, 0f, -6.4f), new Vector3(-1.85f, 0f, -7.38f), new Vector3(-1.85f, y, -13.5f), new Vector3(-1.85f, y, -15.2f),
                // Về đống hàng vòng phía nam, cách làn lên ≥ 0,7 m: đi thẳng từ chân cầu thang về (8,6; −19,4) thì cắt quầy vé và xe khoai
                // (bot 23/10).
                new Vector3(-1.85f, y, -17.8f), new Vector3(9.2f, y, -17.8f),
            };
            var porters = AddLoop(crowd, "Porters", up, down, 8, 100, 7f, Porters, CarryStyle.Shoulder, slope: true);
            for (int i = 0; i < porters.Length; i++)
            {
                porters[i].optional = i >= 6;
                if (i % 3 == 0)
                    Bark(porters[i], 100 + i, 6f, 45f, "bark_porter_1", "bark_porter_2", "bark_porter_3");
            }

            // 2) Hàng khách xếp hàng lên tàu, nằm phía tây lối của người chơi (x −3,3): người đầu hàng trình vé cho người soát rồi
            // lên cầu thang tàu theo làn giữa, khuất vào khoang, ít giây sau hiện lại ở xa đuôi hàng và vào lại hàng.
            res.queue = AddQueue(crowd, "Gangway", new Vector3(-3.3f, y, -17.4f), new Vector3(-0.04f, 0f, -1f), 9, 1.05f,
                new Vector3(-1.5f, y, -13.5f), new Vector3[0], 200, 22f, recycle: false, Passengers,
                boardPath: new[]
                {
                    new Vector3(-1.5f, y, -14.6f), new Vector3(-1.5f, y, -13.5f), new Vector3(-1.5f, 0f, -7.38f), new Vector3(-1.5f, 0f, -5.4f),
                },
                respawn: new Vector3(-3.4f, y, -33f), bags: true);
            for (int i = 0; i < res.queue.members.Length; i += 3)
                Bark(res.queue.members[i], 200 + i, 5f, 50f, "bark_passenger_1", "bark_passenger_2", "bark_passenger_3", "bark_passenger_4");

            // 3) Người soát vé cạnh quầy vé, mặt hướng đầu hàng.
            var purser = AddPose(crowd, new Vector3(-3.3f, y, -15.6f), 180f, 300, "Documents", "HandOver", "Purser");
            purser.gameObject.name = "Npc_Purser";
            res.purser = purser.agent;
            Bark(purser, 300, 3.8f, 40f, "checker_pass", "bark_purser_1", "bark_purser_2");

            // 4) Ông Mai (S16, F01c) nói chuyện với hai phu, thỉnh thoảng chỉ về phía tàu; quay mặt về chỗ người chơi xuất hiện.
            var mai = AddPose(crowd, env.Anchor("mai_standing"), 80f, 310, "Talk", "Point", "Mai");
            mai.agent.gameObject.name = "Npc_Mai";
            mai.altSeconds = new Vector2(3.9f, 4.1f);
            mai.poseSeconds = new Vector2(7f, 12f);
            res.mai = mai.agent;
            AddPose(crowd, env.Anchor("mai_standing") + new Vector3(-0.9f, 0f, -0.95f), 55f, 311, "Listen", "Talk2", "PorterVN_C").startDelay = 1.5f;
            AddPose(crowd, env.Anchor("mai_standing") + new Vector3(-0.9f, 0f, 0.95f), 120f, 312, "Talk2", "Listen", "PorterVN_A").startDelay = 3f;

            // 5) Người đưa tiễn: nhóm năm người đứng nhìn lên tàu, ba người vẫy.
            var farewell = env.Spots[System.Array.FindIndex(env.Spots, s => s.Name == "farewell_group_1")].Position;
            var fare = new[]
            {
                (new Vector3(-0.9f, 0f, 0f), "CivilianVN"), (new Vector3(0.7f, 0f, 0.2f), "VendorVN_F"), (new Vector3(-0.2f, 0f, -1.2f), "PorterVN_C"),
                (new Vector3(1.7f, 0f, -1.0f), "CivilianVN"), (new Vector3(-1.9f, 0f, -0.9f), "PassengerFR_F"),
            };
            for (int i = 0; i < fare.Length; i++)
            {
                bool wave = i < 3;
                var r = AddPose(crowd, farewell + fare[i].Item1, 5f * i, 320 + i, wave ? "Wave" : "Talk", wave ? null : "Idle", fare[i].Item2,
                    optional: i >= 3, oneShot: wave);
                r.startDelay = i * 1.3f;
                r.restSeconds = new Vector2(1.5f, 4f);
                if (i == 0 || i == 3)
                    Bark(r, 320 + i, 6f, 45f, "bark_farewell_1", "bark_farewell_2", "bark_farewell_3");
            }

            // 6) Người bán hàng ngồi xổm cạnh quang gánh, hai người mua đứng nói chuyện.
            var vendor = env.Anchor("vendor");
            var v1 = AddPose(crowd, vendor, 90f, 340, "Squat", "Talk", "VendorVN_F");
            v1.poseSeconds = new Vector2(9f, 16f);
            Bark(v1, 340, 6f, 38f, "bark_vendor_1", "bark_vendor_2", "bark_vendor_3");
            var v2 = AddPose(crowd, vendor + new Vector3(-2.2f, 0f, 0.6f), 20f, 341, "Squat", "Idle", "VendorVN_F", optional: true);
            AddGroup(crowd, "Buyers", vendor + new Vector3(1.6f, 0f, 0.2f), 2, 0.8f, 270f, 342, role: "CivilianVN,PassengerFR_F");

            // 6b) Xe cà-rem dưới cột đèn bến QuayLamp_2 (18, −18,5), nhìn thấy từ lan can lúc nhổ neo (DIRECTOR §9).
            var cartAt = new Vector3(19.4f, y, -17.3f);
            IceCreamCart(crowd, cartAt);
            AddPose(crowd, cartAt + new Vector3(0.1f, 0f, -0.95f), 10f, 345, "Idle", "Wave", "VendorVN_F");
            AddPose(crowd, cartAt + new Vector3(-1.1f, 0f, 0.35f), 120f, 346, "Idle", "Talk", "CivilianVN", optional: true);

            // 7) Phu kho bên đông: vòng hàng ở sân trước kho tây, vác trên vai.
            AddLoop(crowd, "YardPorters", new[] { new Vector3(-34f, y, -23f), new Vector3(-25f, y, -23f), new Vector3(-16f, y, -23f) },
                new[] { new Vector3(-25f, y, -24.8f) }, 4, 400, 6f, Porters, CarryStyle.Shoulder);
            AddPose(crowd, new Vector3(-40f, y, -32f), 90f, 410, "WorkMid", null, "PorterVN_B");
            AddPose(crowd, new Vector3(-38.6f, y, -33f), 270f, 411, "WorkTable", null, "PorterVN_A", optional: true);
            AddPose(crowd, new Vector3(-41.5f, y, -30.4f), 160f, 412, "Pull", null, "PorterVN_C");

            // 8) Người qua đường dọc hàng me phía sau.
            var pass = env.Path("passersby");
            var passers = AddWander(crowd, "Passers", pass, 6, 500, role: Locals, midStart: true);
            for (int i = 2; i < passers.Length; i++)
                passers[i].optional = true;

            AddLod(crowd, 16, 10);
            return crowd;
        }

        static Transform BuildDeck(Transform parent, Result res)
        {
            var crowd = Root(parent, "Crowd_Deck");
            // Thủy thủ tựa lan can mạn phải nhìn xuống kè (z −6,55), hành khách vẫy tay chào người đưa tiễn.
            AddPose(crowd, new Vector3(5.2f, 0f, -6.55f), 180f, 600, "LeanRail", "IdleLook", "SailorFR");
            AddPose(crowd, new Vector3(8.3f, 0f, -6.55f), 180f, 601, "LeanRail", "Idle", "SailorFR", optional: true);
            var w1 = AddPose(crowd, new Vector3(2.6f, 0f, -6.5f), 175f, 602, "Wave", null, "PassengerFR_F", oneShot: true);
            w1.startDelay = 1f;
            var w2 = AddPose(crowd, new Vector3(3.5f, 0f, -6.5f), 185f, 603, "Wave", null, "PassengerFR_M1", optional: true, oneShot: true);
            w2.startDelay = 2.6f;
            Bark(w1, 602, 6f, 55f, "bark_passenger_4");
            // Thủy thủ cuộn dây ở boong giếng trước, phía mũi, ngoài lối đi.
            var rope = AddPose(crowd, new Vector3(12.5f, 0f, -3.4f), 90f, 604, "Pull", "Idle", "SailorFR");
            Bark(rope, 604, 5f, 40f, "bark_sailor_1", "bark_sailor_2", "bark_sailor_3");
            // Quản bếp đứng cạnh cửa bếp, gọi người chơi.
            var chef = AddPose(crowd, ChefSpot, 105f, 610, "Idle", "Point", "Cook");
            chef.agent.gameObject.name = "Npc_Chef";
            chef.altSeconds = new Vector2(3.9f, 4.1f);
            chef.poseSeconds = new Vector2(8f, 14f);
            res.chef = chef.agent;
            AddLod(crowd, 8, 6);
            return crowd;
        }
    }
}
