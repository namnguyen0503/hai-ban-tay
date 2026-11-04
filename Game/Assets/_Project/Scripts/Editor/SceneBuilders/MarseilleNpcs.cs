using HaiBanTay.Editor.SceneBuilders.Ship;
using System.Linq;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Ending;
using HaiBanTay.Interaction;
using HaiBanTay.Npc;
using HaiBanTay.Shift;
using HaiBanTay.Ship;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>Nhân vật chính và 25 người nền theo layout; một slot Animator riêng cho bồi bàn, còn lại do CrowdLod quản.</summary>
    public static class MarseilleNpcs
    {
        public static void Build(ShipLayout ship, EnvLayout env, EndingDirector director)
        {
            var cast = CrowdBuilder.Root(ship.transform.parent, "MarseilleCast");
            var deck = CrowdBuilder.Root(cast, "DeckCast");
            director.deckCast = deck.gameObject;
            var roles = new[] { "Cook", "Helper", "Jacques", "Mai", "SoldierA", "SoldierB" };
            var anchors = new[] { "goodbye_chef", "goodbye_french_kitchenhand", "goodbye_jacques", "goodbye_mai", "goodbye_two_soldiers", "goodbye_two_soldiers" };
            director.crew = new NpcAgent[roles.Length];
            director.farewellTargets = new InteractTarget[roles.Length];
            for (int i = 0; i < roles.Length; i++)
            {
                var point = env.Anchor(anchors[i]);
                // Anchor cửa bếp nằm ngay ô cửa; lính đứng sát miệng hầm. Lùi ra làn trống của model tàu.
                if (i == 0) point += new Vector3(1.3f, 0f, -0.95f);
                if (i == 2) point += Vector3.back * 2.7f; // Tránh tời trước, không chiếm làn hàng lương.
                if (i >= 4) point += Vector3.back * 0.8f;
                if (i == 5) point += Vector3.right * 1.1f;
                var actor = Spawn(deck, roles[i], point, 180f, 310 + i);
                director.crew[i] = actor;
                director.farewellTargets[i] = Target(actor.transform, roles[i] + "Farewell", Vector3.zero, new Vector3(0.9f, 1.8f, 0.9f), "mrs.prompt.bye");
            }
            director.captain = Spawn(deck, "Captain", new Vector3(-5.8f, 0f, -2.7f), 90f, 320);
            director.sailor = Spawn(deck, "SailorFR", new Vector3(1.5f, 0f, -2.5f), 180f, 321);
            var clerk = Spawn(deck, "Purser", env.Anchor("salary_table") + Vector3.right * 0.9f, 90f, 322);
            director.purser = clerk.gameObject.AddComponent<PayClerkRoutine>();
            director.purser.agent = clerk;
            director.purser.queue = CrowdBuilder.AddQueue(deck, "Pay", env.Anchor("salary_queue"), Vector3.right, 2, 1.25f,
                new Vector3(-0.9f, 0f, -2.1f), null, 330, 0f, false, "Helper");
            director.purser.exits = new[] {
                CrowdBuilder.Waypoint(deck, "PayExitA", new Vector3(-0.7f, 0f, -1.2f)),
                CrowdBuilder.Waypoint(deck, "PayExitB", new Vector3(-0.7f, 0f, -2.5f)) };
            director.purser.envelopes = new Transform[2];
            var paper = AssetFactory.Lit("MrsEnvelope", new Color(0.82f, 0.76f, 0.57f), 0.1f);
            for (int i = 0; i < 2; i++)
            {
                var envelope = BuilderUtils.Block("PayEnvelope_" + i, deck, env.Anchor("salary_table") + Vector3.up * 0.8f,
                    new Vector3(0.25f, i == 0 ? 0.065f : 0.018f, 0.15f), paper, false, false);
                envelope.SetActive(false);
                director.purser.envelopes[i] = envelope.transform;
            }

            var crowd = CrowdBuilder.Root(cast, "QuayCrowd");
            var porters = env.Path("porters_loop");
            // Sáu người chia ba làn hàng ở phía tây; không bước lên cầu thang hoặc vào làn x ±1,2.
            for (int i = 0; i < 3; i++)
            {
                var route = new[] { porters[0] + Vector3.right * (i * 1.5f), porters[1] + Vector3.right * (i * 1.5f), porters[2] + Vector3.back * (i * 0.7f) };
                CrowdBuilder.AddLoop(crowd, "Porters" + i, route, null, 2, 350 + i * 10, 4f, "DockerMRS");
            }
            var arrivals = (Vector3[])env.Path("arrivals").Clone();
            for (int i = 0; i < arrivals.Length; i++) arrivals[i].x = Mathf.Max(2.4f, arrivals[i].x);
            CrowdBuilder.AddWander(crowd, "Arrivals", arrivals, 3, 390, role: "PassengerFR_M1");
            CrowdBuilder.AddWander(crowd, "ArrivalsExtra", new[] { arrivals[0] + Vector3.right * 2f, arrivals[2] + Vector3.right * 2f }, 2, 395, true, "PassengerFR_M2");
            var promenade = env.Path("promenade");
            var west = new[] { promenade[0], Vector3.Lerp(promenade[0], promenade[1], 0.5f), promenade[1] + Vector3.left * 3f };
            CrowdBuilder.AddWander(crowd, "Promenade", west, 3, 400, role: "PassengerFR_F");
            // Hai nửa hiên quán; không ai đi xuyên qua tuyến x = 0,5 của người chơi.
            var promenadeExtra = new[] { promenade[2] + Vector3.back * 0.85f, promenade[3] + Vector3.back * 0.85f };
            CrowdBuilder.AddWander(crowd, "PromenadeExtra", promenadeExtra, 2, 405, true, "PassengerFR_M1");
            CrowdBuilder.AddGroup(crowd, "PoorWorkers", env.Anchor("poor_porters"), 3, 0.65f, 0f, 410, role: "DockerMRS");
            CrowdBuilder.AddGroup(crowd, "QuayConversation", env.Anchor("mai_poverty_conversation"), 2, 0.55f, 90f, 420, role: "PassengerFR_M1");
            var seats = new[] { new Vector3(3.1f, -2.4f, -23.2f), new Vector3(4.1f, -2.4f, -24f), new Vector3(5.7f, -2.4f, -23.2f), new Vector3(6.7f, -2.4f, -24f) };
            for (int i = 0; i < seats.Length; i++)
                CrowdBuilder.AddSit(crowd, seats[i], i % 2 == 0 ? 180f : -90f, 430 + i, i % 2 == 0, role: "PassengerFR_M2", seatHeight: .475f);

            BuildStory(cityParent: cast, env: env, director: director);

            // Chỗ cai phu tuyển người khuân vác (DIRECTOR §6): người lính trẻ ở ngày 2 xin việc và bị gạt đi ("Pas de travail"), cạnh
            // nhóm phu nghèo; Ba xem từ chỗ đứng quan sát, không ai lại gần ai.
            var hiring = CrowdBuilder.Root(cast.parent, "MarseilleHiring");
            var hireAt = env.Anchor("poor_porters");
            director.foreman = Spawn(hiring, "PassengerFR_M2", hireAt + new Vector3(0f, 0f, 2.0f), 90f, 450);
            director.quaySoldier = Spawn(hiring, "SoldierA", hireAt + new Vector3(1.15f, 0f, 2.0f), 270f, 451);
            director.quaySoldierExit = CrowdBuilder.Waypoint(hiring, "QuaySoldierExit", hireAt + new Vector3(-4f, 0f, 1.6f));
            BuilderUtils.SetRenderingLayer(hiring.gameObject, WorldRenderingLayers.Exterior);

            // Nhân vật mang khay cần xương sống, không dùng pose LOD tĩnh của đám đông.
            var service = CrowdBuilder.Root(cast.parent, "MarseilleService");
            var waiter = Spawn(service, "Waiter", env.Anchor("waiter_start"), 30f, 440);
            waiter.carryPoint.localPosition = new Vector3(0f, 1.3f, 0.4f);
            director.waiter = waiter.gameObject.AddComponent<WaiterRoutine>();
            director.waiter.agent = waiter;
            director.waiter.counter = CrowdBuilder.Waypoint(cast, "WaiterCounter", env.Anchor("waiter_start"));
            director.waiter.servePoint = CrowdBuilder.Waypoint(cast, "WaiterServe", new Vector3(-0.5f, -2.4f, -24.7f), 42f);
            director.waiter.tablePoint = director.waiterTablePoint;
            director.waiter.table = director.table;
            director.waiter.cup = director.cup;
            var animator = waiter.GetComponentInChildren<Animator>();
            // Khay đọc xương tay cả khi camera không render (PlayMode headless / ngoài frustum).
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            director.waiter.carryHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            director.waiter.upperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            director.waiter.lowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            if (director.waiter.carryHand == null || director.waiter.upperArm == null || director.waiter.lowerArm == null)
                throw new System.InvalidOperationException("Marseille: bồi bàn thiếu xương tay phải");
            // Root scale 1: tách gắn khay không thừa hưởng scale dẹt của mesh.
            var tray = BuilderUtils.Group("WaiterTray", cast);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "TraySurface", tray.transform, Vector3.zero,
                new Vector3(0.42f, 0.006f, 0.32f),
                AssetFactory.Lit("MrsTrayMetal", new Color(0.55f, 0.52f, 0.46f), 0.45f, 0.45f), false, false);
            director.waiter.tray = tray.transform;
            tray.transform.SetParent(waiter.carryPoint, false);
            tray.transform.localPosition = Vector3.zero;
            director.cup.SetParent(tray.transform, false);
            director.cup.localPosition = Vector3.up * 0.008f;
            // Cộng một bồi bàn luôn animate: tổng vẫn 16 PC / 10 Laptop.
            CrowdBuilder.AddLod(cast, 15, 9);
            BuilderUtils.SetRenderingLayer(cast.gameObject, WorldRenderingLayers.Exterior);
            BuilderUtils.SetRenderingLayer(service.gameObject, WorldRenderingLayers.Exterior);
        }

        static void BuildStory(Transform cityParent, EnvLayout env, EndingDirector director)
        {
            var story = CrowdBuilder.Root(cityParent, "MarseilleStory");
            director.visitingWomen = new NpcAgent[2];
            for (int i = 0; i < 2; i++)
            {
                var woman = Spawn(story, "PassengerFR_F", env.Anchor("gangway_foot"), 0f, 860 + i);
                woman.name = "VisitorWoman_" + i;
                // Váy dài kín kiểu 1911; đổi sắc vải, không thay hình nhân vật nguồn.
                int dressSlots = 0;
                foreach (var renderer in woman.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (int slot = 0; slot < materials.Length; slot++)
                    {
                        var original = materials[slot];
                        string materialName = original.name.ToLowerInvariant();
                        if (!materialName.Contains("_shirt") && !materialName.Contains("_skirt") && !materialName.Contains("_cloth")) continue;
                        dressSlots++;
                        var bright = AssetFactory.Lit("VisitorDress_" + i + "_" + slot,
                            i == 0 ? new Color(0.48f, 0.12f, 0.23f) : new Color(0.2f, 0.28f, 0.51f), 0.18f);
                        bright.SetTexture("_BaseMap", original.GetTexture(original.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex"));
                        materials[slot] = bright;
                    }
                    renderer.sharedMaterials = materials;
                }
                // Không để bảng màu đám đông ghi đè màu váy riêng khi Awake.
                var variation = woman.GetComponent<CrowdVariation>();
                if (variation != null)
                    variation.slots = System.Array.FindAll(variation.slots, slot => slot.kind != CrowdTintKind.Clothes && slot.kind != CrowdTintKind.Cotton);
                if (dressSlots == 0) throw new System.InvalidOperationException("Không tìm thấy material áo/váy PassengerFR_F");
                director.visitingWomen[i] = woman;
                woman.gameObject.SetActive(false);
            }
            director.visitorPath = new[] {
                CrowdBuilder.Waypoint(story, "VisitorFoot", env.Anchor("gangway_foot")),
                CrowdBuilder.Waypoint(story, "VisitorTop", new Vector3(0f, 1.12f, -3.21f)),
                CrowdBuilder.Waypoint(story, "VisitorDeck", new Vector3(0f, 0f, -1.15f)),
                CrowdBuilder.Waypoint(story, "VisitorSailors", new Vector3(1.5f, 0f, -2.5f)) };
            director.maiShorePath = new[] {
                CrowdBuilder.Waypoint(story, "MaiDeck", new Vector3(0f, 0f, -1.15f)),
                CrowdBuilder.Waypoint(story, "MaiLanding", new Vector3(0f, 1.12f, -3.21f)),
                CrowdBuilder.Waypoint(story, "MaiFoot", env.Anchor("gangway_foot")),
                CrowdBuilder.Waypoint(story, "MaiQuay", new Vector3(0f, -2.5f, -14.5f)) };
            director.maiCafeSeat = CrowdBuilder.Waypoint(story, "MaiCafeSeat", new Vector3(1.5f, -2.4f, -24f), 270f);
            // Ghế bên bàn Ba đã có trong model, bỏ collider để ông Mai ngồi đúng chỗ.
            foreach (var collider in cityParent.parent.GetComponentsInChildren<Collider>(true))
                if (collider.name == "COL_CafeChair_0_side") collider.enabled = false;
            director.borrowedClothes = BuilderUtils.Block("BorrowedClothes", director.crew[3].carryPoint,
                Vector3.zero, new Vector3(0.36f, 0.08f, 0.28f), PlayerBodyBuilder.OrdinaryShirtMaterial(), false, false);
            director.borrowedClothes.SetActive(false);
            // Quyển sách người lính trẻ (SoldierA, câu mrs_soldier_a_bye) cho mượn ngày 6: bìa vải sẫm, trong tay anh lúc Ba trả.
            director.soldierBook = BuilderUtils.Block("SoldierBook", director.crew[4].carryPoint, Vector3.zero, new Vector3(0.14f, 0.035f, 0.2f),
                AssetFactory.Lit("SoldierBookCloth", new Color(0.24f, 0.16f, 0.12f), 0.25f), false, false);
            director.soldierBook.SetActive(false);

            var car = BuilderUtils.Group("MotorCar1911", story).transform;
            // Thân xe dài 3,2 m theo z: tâm ở z −19,2 thì đuôi xe cách lối dạo z −21,4 hơn một thân người (bot 23/10: khách dạo đi xuyên xe).
            car.position = new Vector3(-4.2f, -2.5f, -19.2f);
            var iron = AssetFactory.Lit("StoryCarIron", new Color(0.08f, 0.1f, 0.08f), 0.4f, 0.55f);
            var brass = AssetFactory.Lit("StoryCarBrass", new Color(0.5f, 0.36f, 0.1f), 0.6f, 0.7f);
            MotorCarBuilder.Build(car, iron, brass);
            var news = CrowdBuilder.AddPose(story, new Vector3(-2.5f, -2.5f, -22f), 0f, 874, "Documents", role: "PassengerFR_M1").agent;
            news.name = "NewspaperSeller";
            BuilderUtils.Block("Newspapers", news.carryPoint, Vector3.zero, new Vector3(0.3f, 0.03f, 0.22f), AssetFactory.Lit("StoryNewsPaper", new Color(0.69f, 0.65f, 0.52f), 0.05f), false, false);
            // Cậu bé bán báo dừng ở mép hè nhìn vào bàn Ba (DIRECTOR §7/§9): ở Sài Gòn Ba đứng ngoài nhìn vào, ở đây có người nhìn Ba.
            // Đứng sau ghế ông Mai, lệch về phía phố, nên Ba quay sang ông Mai là thấy; cách lối dạo z −21,4 hơn một thân người.
            var newsBoy = CrowdBuilder.AddPose(story, new Vector3(3.9f, -2.4f, -22.5f), 259f, 875, "Documents", "Idle", "PassengerFR_M2").agent;
            newsBoy.name = "NewsBoy";
            newsBoy.transform.localScale = Vector3.one * 0.8f;
            // Tờ báo gập trước ngực (điểm bê của tư thế Documents nằm ngang đầu gối, ảnh 23/10): gắn thẳng vào thân.
            BuilderUtils.Block("Newspapers", newsBoy.transform, new Vector3(0f, 1.0f, 0.24f), new Vector3(0.28f, 0.03f, 0.2f), AssetFactory.Lit("StoryNewsPaper", new Color(0.69f, 0.65f, 0.52f), 0.05f), false, false, new Vector3(35f, 0f, 0f));
            director.newDiscoveries = new[] {
                Target(story, "TramDiscovery", new Vector3(0f, -2.5f, -15.5f), new Vector3(1f, 1.4f, 0.3f), "prompt.story.look"),
                Target(story, "CarDiscovery", new Vector3(-3.3f, -2.5f, -19.2f), new Vector3(0.5f, 1.4f, 1.4f), "prompt.story.look"),
                Target(story, "GlassShopDiscovery", new Vector3(-2f, -2.5f, -26.7f), new Vector3(0.5f, 2f, 0.5f), "prompt.story.look"),
                Target(news.transform, "NewsDiscovery", Vector3.zero, new Vector3(0.8f, 1.6f, 0.8f), "prompt.story.look") };
            var shop = BuilderUtils.Group("GlassShop1911", story).transform;
            shop.position = new Vector3(-2f, -2.5f, -27.2f);
            var glass = AssetFactory.Transparent("StoryShopGlass", new Color(0.32f, 0.48f, 0.48f, 0.28f));
            BuilderUtils.Block("Window", shop, Vector3.up * 1.3f, new Vector3(1.4f, 1.8f, 0.02f), glass, true, false);
            foreach (float x in new[] { -0.73f, 0.73f }) BuilderUtils.Block("Frame", shop, new Vector3(x, 1.3f, 0f), new Vector3(0.06f, 1.9f, 0.1f), iron, true, false);
            foreach (float y in new[] { 0.36f, 2.24f }) BuilderUtils.Block("Frame", shop, new Vector3(0f, y, 0f), new Vector3(1.5f, 0.06f, 0.1f), iron, true, false);
            BuilderUtils.SetRenderingLayer(story.gameObject, WorldRenderingLayers.Exterior);
        }

        static NpcAgent Spawn(Transform parent, string role, Vector3 point, float yaw, int seed)
        {
            // Thiếu vai chính phải báo lỗi, không âm thầm nhận khối xám là model thật.
            if (AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/NPC/" + role + ".prefab") == null)
                throw new System.InvalidOperationException("Marseille thiếu NPC " + role);
            var actor = CrowdBuilder.Spawn(parent, role, point, yaw, seed);
            actor.name = role;
            return actor;
        }

        public static InteractTarget Target(Transform parent, string name, Vector3 point, Vector3 size, string prompt)
        {
            var go = BuilderUtils.Group(name, parent);
            go.transform.localPosition = point;
            go.layer = BuilderUtils.Layer("Interactable");
            go.tag = "Interactable";
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = Vector3.up * size.y * 0.5f;
            box.size = size;
            var target = go.AddComponent<InteractTarget>();
            target.promptKey = prompt;
            return target;
        }
    }

    // Dùng đúng builder trạm bếp hiện có, không sửa GalleyChores hay các routine của Track F.
    public static partial class GalleySceneBuilder
    {
        public static void BuildMarseilleFinalWork(ShipLayout layout, EnvLayout env, EndingDirector director)
        {
            var tasks = BuilderUtils.Group("FinalShift", layout.galley).transform;
            var m = new ShipInterior.Mats();
            var sweep = BuildSweep(layout.galley, tasks, m);
            for (int i = 0; i < sweep.spots.Length; i++)
                if (i != 1) Object.DestroyImmediate(sweep.spots[i].gameObject);
            sweep.spots = new[] { sweep.spots[1] };
            sweep.spotRenderers = new[] { sweep.spotRenderers[1] };
            sweep.spots[0].transform.position = env.Anchor("last_shift_sweep");
            director.sweep = sweep;
            director.wash = BuildWash(layout.galley, tasks, m);
            // Cảnh cuối dùng cùng bồn kẽm, bơm và dụng cụ thật của cảnh bếp.
            var coal = BuildCoal(layout.galley, tasks, m);
            BuildCoalBaskets(layout.galley, coal, layout.coalHold, m);
            var range = BuildPotRange(layout.galley, m);
            BindRangeCells(coal, range.transform, 1);
            BuildAsh(layout.galley, tasks, m, coal, layout.coalHold);
            // Ca cuối đã nhóm lò trước lúc tàu cập bến; từ đây vẫn cháy và sinh tro như cảnh 02.
            coal.initialFuel = FireCellState.MaxFuel;
            coal.initialHeat = 1f;
            var meals = BuilderUtils.Group("FinalMealPots", layout.galley).AddComponent<MealPots>();
            // Ô bếp (W3.2) như cảnh bếp: anchor Hob trên nắp gang từng ô; nồi cuối đặt đúng ô gần chỗ cất nhất (ô giữa).
            meals.hobs = coal.cells.Where(c => c.stoveIndex == 1).Select(c => c.hob).ToArray();
            meals.coal = coal;
            coal.meals = meals;
            var cooked = ChoreProps.Pot(meals.transform, "FinalCookingPot", 0.4f, 0.28f, m.Copper);
            cooked.transform.localPosition = new Vector3(-3f, 0.913f, -2.65f);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "MealLid", cooked.transform, new Vector3(0.035f, 0.29f, 0f),
                new Vector3(0.4f, 0.008f, 0.4f), m.Copper, false, false, new Vector3(0f, 0f, 12f));
            BuilderUtils.Prim(PrimitiveType.Sphere, "LidKnob", cooked.transform, new Vector3(0.035f, 0.32f, 0f), new Vector3(0.045f, 0.025f, 0.045f), m.Iron, false, false);
            FireBuilder.PotSteam(cooked.transform);
            meals.pots = new[] { cooked }; meals.wash = director.wash;
            meals.porter = MakeNpc(layout.galley, "Helper", new Vector3(-3.4f, 0f, -1.85f), 180f, m.Paper.color, m.Wood.color);
            director.finalMeals = meals;

        }
    }
}
