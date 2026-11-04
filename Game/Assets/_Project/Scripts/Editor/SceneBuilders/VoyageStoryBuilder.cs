using System.Collections.Generic;
using HaiBanTay.Editor.SceneBuilders.Ship;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Interaction;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Ship;
using UnityEngine;
using UnityEditor;
using Mats = HaiBanTay.Editor.SceneBuilders.Ship.ShipInterior.Mats;

namespace HaiBanTay.Editor.SceneBuilders
{
    public static partial class GalleySceneBuilder
    {
        /// <summary>Đạo cụ story11b sinh bằng code; không ghi vào prefab mỹ thuật hay Staging.</summary>
        static WorkStation[] BuildVoyageStory(Transform env, Transform tasks, ShiftDirector d, Mats m, ShipLayout layout)
        {
            var list = new List<WorkStation>();
            var fetch = BuilderUtils.Group("CoalFetchStation", tasks).AddComponent<CoalFetchStation>();
            fetch.coal = d.coal; list.Add(fetch);
            var people = BuilderUtils.Group("VoyagePeople", env).AddComponent<VoyagePeople>();
            d.people = people;
            // Vòi bơm nước ngọt đứng trên sàn ở đầu bắc bồn rửa (bồn: x 6,32–6,92, z −2,15…−1,05): thân bơm sát vách đông,
            // cổ vòi cong chĩa xuống lòng bồn, cần bơm quay ra lối đi; xô nước đặt dưới đất ngay cạnh chân bơm.
            var water = Delivery(env, tasks, "water", new Vector3(6.45f, 0f, -0.72f), new Vector3(-1.35f, 0.94f, 2.1f), m);
            water.pickupStand = new Vector3(5.85f, 0f, -0.6f); water.deliveryStand = new Vector3(-1.35f, 0f, 1.4f); water.pour = true;
            var pump = BuilderUtils.Group("FreshWaterPump", env).transform;
            pump.localPosition = new Vector3(6.78f, 0f, -0.82f);
            BuilderUtils.Block("PumpBase", pump, new Vector3(0f, 0.04f, 0f), new Vector3(0.24f, 0.08f, 0.24f), m.Iron, true, false);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "PumpColumn", pump, new Vector3(0f, 0.55f, 0f), new Vector3(0.11f, 0.5f, 0.11f), m.Iron, true, false);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "PumpHead", pump, new Vector3(0f, 1.08f, 0f), new Vector3(0.14f, 0.06f, 0.14f), m.Iron, true, false);
            BuilderUtils.Block("SpoutNeck", pump, new Vector3(0f, 0.98f, -0.17f), new Vector3(0.05f, 0.05f, 0.34f), m.Iron, true, false);
            BuilderUtils.Block("SpoutMouth", pump, new Vector3(0f, 0.92f, -0.33f), new Vector3(0.05f, 0.12f, 0.05f), m.Iron, true, false);
            BuilderUtils.Block("PumpLever", pump, new Vector3(-0.17f, 1.17f, 0.02f), new Vector3(0.34f, 0.025f, 0.03f), m.Wood, true, false,
                new Vector3(0f, 0f, -18f));
            BuilderUtils.SetRenderingLayer(pump.gameObject, WorldRenderingLayers.Interior);
            var bucket = ChoreProps.Pot(water.load.transform, "WaterBucket", 0.33f, 0.33f, m.Iron);
            BuilderUtils.Block("BucketBail", bucket.transform, new Vector3(0f, 0.51f, 0f), new Vector3(0.3f, 0.025f, 0.025f), m.Iron, false, false);
            foreach (float x in new[] { -0.15f, 0.15f }) BuilderUtils.Block("BailSide", bucket.transform, new Vector3(x, 0.39f, 0f), new Vector3(0.022f, 0.24f, 0.022f), m.Iron, false, false);
            // Xách xô nước bằng tay phải, nắm giữa quai.
            water.load.hang = true;
            water.load.hangGrip = Beacon(water.load.transform, "WaterBucketBail", new Vector3(0f, 0.52f, 0f));
            water.load.hangGrip.localRotation = Quaternion.Euler(82f, -8f, 8f);
            water.load.hangGrip.localPosition -= water.load.hangGrip.localRotation * new Vector3(0f, -HandsRig.GripBelow, HandsRig.GripForward);
            water.waterSurface = BuilderUtils.Prim(PrimitiveType.Cylinder, "SloshingWater", bucket.transform, new Vector3(0f, 0.27f, 0f), new Vector3(0.29f, 0.005f, 0.29f), AssetFactory.Lit("StoryWater", new Color(0.22f, 0.48f, 0.59f), 0.9f), false, false).transform;
            var kettle = ChoreProps.Pot(water.destination.transform, "BoilingWaterPot", 0.36f, 0.28f, m.Copper);
            water.placed.localPosition += new Vector3(0.20f, 0.22f, -0.25f);
            list.Add(water);
            var meatMat = AssetFactory.Lit("StoryMeat", new Color(0.4f, 0.1f, 0.08f), 0.35f);
            var iceMat = AssetFactory.Lit("StoryIce", new Color(0.65f, 0.84f, 0.87f), 0.9f);
            var fishMat = AssetFactory.Lit("StoryFish", new Color(0.38f, 0.48f, 0.49f), 0.7f);
            int index = 0;
            foreach (var kind in new[] { "meat", "fish", "ice" })
            {
                var pos = new Vector3(-12.4f - index * 0.65f, -2.8f, 0.7f);
                var task = Delivery(env, tasks, kind, pos, new Vector3(0.65f, 0.91f, 0.1f), m);
                task.fromHold = true; task.pickupStand = new Vector3(pos.x, -2.8f, -0.35f); task.deliveryStand = new Vector3(1.6f, 0f, -0.7f);
                // Kẹp khối đá trong hầm (DIRECTOR §6): Ba nhớ cốc cà-rem ở Sài Gòn.
                if (kind == "ice") task.pickupLine = "ba_ice_saigon";
                if (kind == "meat")
                {
                    var rack = BuilderUtils.Group("MeatTrayRack", layout.foodHold);
                    rack.transform.position = pos;
                    BuilderUtils.Block("Tray", rack.transform, new Vector3(0f, .58f, 0f), new Vector3(.5f, .025f, .42f), m.Iron);
                    foreach (float x in new[] { -.22f, .22f }) foreach (float z in new[] { -.18f, .18f })
                        BuilderUtils.Block("RackLeg", rack.transform, new Vector3(x, .28f, z), new Vector3(.035f, .56f, .035f), m.Wood);
                    foreach (float x in new[] { -.24f, .24f }) BuilderUtils.Block("TrayRim", rack.transform, new Vector3(x, .61f, 0f), new Vector3(.02f, .06f, .42f), m.Iron);
                    foreach (float z in new[] { -.20f, .20f }) BuilderUtils.Block("TrayRim", rack.transform, new Vector3(0f, .61f, z), new Vector3(.5f, .06f, .02f), m.Iron);
                    task.load.transform.position += Vector3.up * .56f; task.home.position = task.load.transform.position;
                    VoyageProps.Meat(task.load.transform, meatMat, m.Paper);
                }
                else if (kind == "fish")
                {
                    OpenCrate(task.load.transform, 0.55f, 0.28f);
                    task.load.GetComponent<BoxCollider>().size = new Vector3(.58f, .48f, .56f);
                    // Bưng thấp, nghiêng mặt thùng về phía người: thấy cá nằm trên đá, không chỉ thấy vách gỗ.
                    task.load.heldOffset = new Vector3(0f, -0.2f, 0.06f);
                    task.load.heldEuler = new Vector3(-32f, 0f, 0f);
                    // Hai tay ôm hai góc gần người, ra ngoài vách 4 cm: nắm giữa vách thì thùng nghiêng 32° đè lên cẳng tay (lún vách 4,9 cm,
                    // ClipFit17.FishCrateGrips 17/10; góc gần 0,5 cm).
                    task.load.leftGrip = Beacon(task.load.transform, "FishCrateLeftGrip", new Vector3(-0.33f, 0.2f, -0.24f));
                    task.load.rightGrip = Beacon(task.load.transform, "FishCrateRightGrip", new Vector3(0.33f, 0.2f, -0.24f));
                    task.load.leftGrip.localRotation = HandsRig.SideGrip(true);
                    task.load.rightGrip.localRotation = HandsRig.SideGrip(false);
                    for (int i = 0; i < 4; i++)
                    {
                        BuilderUtils.Block("IceChips", task.load.transform, new Vector3(-0.14f + i * 0.09f, 0.13f, 0.05f), new Vector3(0.12f, 0.06f, 0.14f), iceMat, false, false);
                        var fish = VoyageProps.Fish(task.load.transform, fishMat, m.Iron);
                        fish.transform.localPosition = new Vector3(-0.14f + i * 0.09f, 0.19f, 0f);
                        fish.transform.localRotation = Quaternion.Euler(0f, -8f + i * 5f, 0f);
                    }
                }
                else
                {
                    VoyageProps.Ice(task.load.transform, iceMat, AssetFactory.Lit("StoryPackingStraw", new Color(.48f,.35f,.15f), .05f));
                    foreach (float x in new[] { -0.15f, 0.15f }) BuilderUtils.Block("IronTongs", task.load.transform, new Vector3(x, 0.39f, 0f), new Vector3(0.025f, 0.32f, 0.025f), m.Iron, false, false, new Vector3(0f, 0f, x > 0f ? 15f : -15f));
                    BuilderUtils.Block("TongsGrip", task.load.transform, new Vector3(0f, 0.55f, 0f), new Vector3(0.2f, 0.02f, 0.04f), m.Iron, false, false);
                    // Xách tảng đá bằng kẹp sắt một tay bên hông: tay phải nắm thanh ngang của kẹp, đá thõng dưới kẹp.
                    task.load.hang = true;
                    task.load.hangGrip = Beacon(task.load.transform, "TongsGrip", new Vector3(0f, .55f, 0f));
                    task.load.hangGrip.localRotation = Quaternion.Euler(82f, -8f, 8f);
                    task.load.hangGrip.localPosition -= task.load.hangGrip.localRotation * new Vector3(0f, -HandsRig.GripBelow, HandsRig.GripForward);
                    var mist = task.load.gameObject.AddComponent<ParticleSystem>();
                    var main = mist.main; main.startLifetime = 0.8f; main.startSize = 0.07f; main.startSpeed = 0.03f; main.maxParticles = 18;
                    var emission = mist.emission; emission.rateOverTime = 7f;
                    var shape = mist.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(0.3f, 0.3f, 0.25f);
                    var renderer = mist.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = AssetFactory.Transparent("StoryColdVapor", new Color(0.8f, 0.88f, 0.92f, 0.12f));
                }
                task.load.transform.SetParent(layout.foodHold, true); task.home.SetParent(layout.foodHold, true);
                list.Add(task); index++;
            }
            people.cooks = CrowdBuilder.Root(env, "FrenchCooksMeal").gameObject;
            // Hai đầu bếp ăn ở bàn phòng ăn nhân viên (ghế hai đầu bàn, dùng lại ghế cố định của phòng), không ở giữa bếp.
            // Khay ở góc đông bắc bàn sơ chế (0,44 × 0,30 m, đủ đặt nồi canh): không đè rổ lá tốt, đĩa vỏ của ông Mai, nồi nước gọt
            // khoai (QA-0040).
            var serve = Delivery(env, tasks, "serve", new Vector3(0.77f, 0.93f, 0.25f), new Vector3(-3.45f, 0.83f, -4.3f), m);
            var serveBox = serve.load.GetComponent<BoxCollider>(); serveBox.size = new Vector3(0.46f, 0.48f, 0.32f);
            serve.pickupStand = new Vector3(1.6f, 0f, -0.7f); serve.deliveryStand = new Vector3(-3.45f, 0f, -3.6f);
            BuilderUtils.Block("ServingTray", serve.load.transform, Vector3.zero, new Vector3(0.44f, 0.025f, 0.3f), m.Wood, false, false);
            // Hai lòng bàn tay đỡ đúng mép khay; grip là gốc cổ tay, ngón hướng dọc mép về phía trước.
            serve.load.leftGrip = Beacon(serve.load.transform, "TrayLeftGrip", new Vector3(-0.23f, 0f, -0.13f));
            serve.load.rightGrip = Beacon(serve.load.transform, "TrayRightGrip", new Vector3(0.23f, 0f, -0.13f));
            serve.load.leftGrip.localRotation = HandsRig.SideGrip(true);
            serve.load.rightGrip.localRotation = HandsRig.SideGrip(false);
            var servingPot = ChoreProps.Pot(serve.load.transform, "ServingPot", 0.21f, 0.15f, m.Copper);
            // Nồi có canh (mặt canh dưới miệng nồi); khay không bê nồi rỗng.
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Stew", servingPot.transform, new Vector3(0f, 0.115f, 0f), new Vector3(0.175f, 0.004f, 0.175f), AssetFactory.Lit("StorySoup", new Color(0.55f, 0.36f, 0.13f), 0.5f), false, false);
            serve.servedTable = BuilderUtils.Group("LaidPlates", people.cooks.transform);
            var bread = AssetFactory.Lit("StoryBread", new Color(0.66f, 0.42f, 0.2f), 0.2f);
            var potato = AssetFactory.Lit("StoryPotato", new Color(0.61f, 0.43f, 0.25f), 0.2f);
            for (int i = 0; i < 2; i++)
            {
                var plateAt = new Vector3(i == 0 ? -3.85f : -3.05f, 0.84f, -4.3f);
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Plate", serve.servedTable.transform, plateAt, new Vector3(0.27f, 0.012f, 0.27f), m.Paper, false, false);
                // Mỗi đĩa có phần ăn: hai củ khoai và lát bánh mì.
                BuilderUtils.Prim(PrimitiveType.Sphere, "PlatePotato", serve.servedTable.transform, plateAt + new Vector3(-0.045f, 0.03f, 0.02f), new Vector3(0.07f, 0.05f, 0.06f), potato, false, false);
                BuilderUtils.Prim(PrimitiveType.Sphere, "PlatePotato", serve.servedTable.transform, plateAt + new Vector3(0.015f, 0.03f, -0.03f), new Vector3(0.06f, 0.045f, 0.06f), potato, false, false);
                BuilderUtils.Prim(PrimitiveType.Capsule, "PlateBread", serve.servedTable.transform, plateAt + new Vector3(0.06f, 0.028f, 0.04f), new Vector3(0.05f, 0.05f, 0.05f), bread, false, false, new Vector3(0f, 25f, 90f));
            }
            serve.servedTable.SetActive(false); list.Add(serve);
            var peel = Peeling(env, tasks, false, d.cast, m); list.Add(peel);
            var asp = Peeling(env, tasks, true, d.cast, m); list.Add(asp);
            // Phòng ăn nhân viên sau vách nam, phía tây hầm than; chừa nguyên các lối làm bếp cũ.
            var mess = BuildCrewDiningRoom(env, m);
            // Ghế phía đông sát đầu bàn (gối lọt dưới mặt bàn): Ba ngồi ăn đối diện ông Mai (ngày 1), ngồi viết thư (ngày 6).
            var letterChair = Beacon(env, "LetterWritingSeat", new Vector3(-2.62f, 0f, -4.3f));
            letterChair.localRotation = Quaternion.Euler(0f, -90f, 0f);
            Seat(mess, letterChair.localPosition, m);
            // Hai đầu bếp ngồi hai đầu bàn, mặt hướng vào bàn; ghế đã có (ghế Mai phía tây, ghế viết thư phía đông).
            var cookWest = Seated(people.cooks.transform, new Vector3(-4.3f, 0f, -4.3f), 90f, 4110, "Cook", m, seatParent: mess, buildSeat: false);
            float dropWest = LastSeatDrop;
            var cookEast = Seated(people.cooks.transform, new Vector3(-2.62f, 0f, -4.3f), -90f, 4111, "Cook", m, seatParent: mess, buildSeat: false);
            people.diners = new[] { cookWest, cookEast };
            people.cast = d.cast;
            people.dinerDrop = new[] { dropWest, LastSeatDrop };
            // Ăn xong đi ra boong, lúc tới cũng từ boong vào (QA-0131): dọc mép bắc bàn, qua cửa phòng ăn (x −1,35…−0,45),
            // rồi theo lối boong–bếp của lính (GalleyNpcs.DeckToGalley, ngược chiều). Điểm cuối ngoài cửa bếp, khuất tầm nhìn. Qua góc tây
            // lò (x −3,6, z 1,95) cách ≥ 0,49 m: điểm cũ (−3,95; 1,95) cho thân chồng CastRange (bot 22/10).
            var exitRoot = BuilderUtils.Group("CooksExit", env).transform;
            var exit = new[]
            {
                new Vector3(-3.45f, 0f, -3.62f), new Vector3(-0.9f, 0f, -3.55f), new Vector3(-0.9f, 0f, -2.6f), new Vector3(-1.5f, 0f, -0.85f),
                new Vector3(-2.2f, 0f, 0.8f), new Vector3(-3.35f, 0f, 1.2f), new Vector3(-4.2f, 0f, 1.75f), new Vector3(-4.55f, 0f, 2.6f),
                new Vector3(-5.7f, 0f, 2.6f), new Vector3(-6.6f, 0f, 2.2f),
            };
            people.dinerExit = new Transform[exit.Length];
            for (int i = 0; i < exit.Length; i++)
                people.dinerExit[i] = Beacon(exitRoot, "Exit" + i, exit[i]);
            d.letterChair = letterChair; d.letterLook = Beacon(env, "LetterWritingLook", new Vector3(-4.3f, 1.3f, -4.3f));
            // Tờ giấy trắng và cây bút sau bức thư ngày 6 (DIRECTOR §6): cầm bút được, không bắt buộc; ẩn tới tối ngày 6.
            var blank = BuilderUtils.Group("BlankPaper", env);
            blank.transform.localPosition = new Vector3(-3.15f, 0.832f, -4.5f);
            BuilderUtils.Block("Sheet", blank.transform, new Vector3(0f, 0.001f, 0f), new Vector3(0.21f, 0.002f, 0.297f), m.Paper, false, false,
                new Vector3(0f, 8f, 0f));
            var pen = BuilderUtils.Group("Pen", blank.transform).transform;
            pen.localPosition = new Vector3(0.05f, 0.006f, 0.06f);
            pen.localRotation = Quaternion.Euler(0f, 35f, 0f);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Holder", pen, Vector3.zero, new Vector3(0.008f, 0.07f, 0.008f), m.Wood, false, false,
                new Vector3(90f, 0f, 0f));
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Nib", pen, new Vector3(0f, 0f, 0.075f), new Vector3(0.004f, 0.008f, 0.004f), m.Iron, false, false,
                new Vector3(90f, 0f, 0f));
            var paper = blank.AddComponent<BlankPaper>();
            paper.pen = pen;
            paper.target = MarseilleNpcs.Target(blank.transform, "PenTarget", Vector3.zero, new Vector3(0.3f, 0.12f, 0.36f), "prompt.blank_paper");
            paper.target.available = false;
            blank.SetActive(false);
            d.blankPaper = paper;
            people.crewMeal = CrowdBuilder.Root(env, "CrewBreadButterSoup").gameObject;
            // Phòng ăn có hai ghế: ghế phía tây (ông Mai ngồi bữa nhân viên) đặt cố định trong phòng, không ẩn theo bữa ăn.
            var friend = Seated(people.crewMeal.transform, new Vector3(-4.3f, 0f, -4.3f), 90f, 4130, "Mai", m, seatParent: mess);
            var soup = BuilderUtils.Group("SoupEncounter", tasks).AddComponent<VoyageEncounter>(); soup.orderKind = "soup"; soup.speaker = friend;
            // Bát của Ba trước ghế phía đông, cách mép bàn một gang: ngồi xuống là với tới.
            var soupTarget = BuilderUtils.Group("SoupBowl", people.crewMeal.transform); soupTarget.transform.localPosition = new Vector3(-3.1f, 0.83f, -4.3f);
            ChoreProps.Pot(soupTarget.transform, "Soup", 0.2f, 0.1f, m.Paper);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "SoupSurface", soupTarget.transform, new Vector3(0f, 0.075f, 0f), new Vector3(0.18f, 0.005f, 0.18f), AssetFactory.Lit("StorySoup", new Color(0.55f, 0.36f, 0.13f), 0.5f), false, false);
            // Mỗi chỗ ngồi có đĩa lót dưới bát: chỗ người chơi (bát canh) và chỗ ông Mai (đĩa, bát canh riêng).
            BuilderUtils.Prim(PrimitiveType.Cylinder, "SoupPlate", soupTarget.transform, new Vector3(0f, 0.006f, 0f), new Vector3(0.42f, 0.012f, 0.42f), m.Paper, false, false);
            var maiAt = new Vector3(-3.95f, 0.83f, -4.3f);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "MaiPlate", people.crewMeal.transform, maiAt + new Vector3(0f, 0.006f, 0f), new Vector3(0.38f, 0.012f, 0.38f), m.Paper, false, false);
            ChoreProps.Pot(people.crewMeal.transform, "MaiBowl", 0.2f, 0.1f, m.Paper).transform.localPosition = maiAt + new Vector3(0f, 0.012f, 0f);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "MaiSoupSurface", people.crewMeal.transform, maiAt + new Vector3(0f, 0.087f, 0f), new Vector3(0.18f, 0.005f, 0.18f), AssetFactory.Lit("StorySoup", new Color(0.55f, 0.36f, 0.13f), 0.5f), false, false);
            BuilderUtils.Prim(PrimitiveType.Capsule, "MaiBread", people.crewMeal.transform, maiAt + new Vector3(0.36f, 0.03f, 0f), new Vector3(0.17f, 0.06f, 0.12f), AssetFactory.Lit("StoryBread", new Color(0.66f, 0.42f, 0.2f), 0.2f), false, false);
            soupTarget.AddComponent<BoxCollider>().size = new Vector3(0.4f, 0.25f, 0.4f); soup.target = AddTarget(soupTarget, soup, "soup"); soup.stand = new Vector3(-2.2f, 0f, -4.3f);
            soup.seat = letterChair; soup.seatClip = d.study.chairClip;
            soup.seatLook = Beacon(env, "MealLook", new Vector3(-4.05f, 1.02f, -4.3f));
            soup.fork = Cutlery(soupTarget.transform, m, false); soup.spoon = Cutlery(soupTarget.transform, m, true); soup.spoon.SetActive(false);
            soup.forkGrip = Beacon(soup.fork.transform, "ForkRightGrip", new Vector3(0f, 0f, -0.075f));
            soup.spoonGrip = Beacon(soup.spoon.transform, "SpoonRightGrip", new Vector3(0f, 0f, -0.075f));
            soup.forkGrip.localRotation = soup.spoonGrip.localRotation = Quaternion.Euler(20f, 0f, -70f);
            // Bánh mì bên trái bát, bơ bên phải (Ba ngồi quay mặt về phía tây).
            BuilderUtils.Prim(PrimitiveType.Capsule, "Bread", soupTarget.transform, new Vector3(-0.02f, 0.055f, -0.22f), new Vector3(0.12f, 0.06f, 0.17f), AssetFactory.Lit("StoryBread", new Color(0.66f, 0.42f, 0.2f), 0.2f), false, false);
            BuilderUtils.Block("Butter", soupTarget.transform, new Vector3(-0.06f, 0.02f, 0.21f), new Vector3(0.08f, 0.04f, 0.06f), AssetFactory.Lit("StoryButter", new Color(0.95f, 0.83f, 0.48f), 0.3f), false, false); list.Add(soup);
            people.firstClass = CrowdBuilder.Root(env, "ChieuFirstClassFamily").gameObject;
            var chieu = CrowdBuilder.AddStand(people.firstClass.transform, new Vector3(-3f, 2.85f, 4.8f), 180f, 4140, role: "Chieu").agent;
            CrowdBuilder.AddStand(people.firstClass.transform, new Vector3(-1.8f, 2.85f, 4.8f), 180f, 4141, role: "PassengerFR_F");
            var child = CrowdBuilder.AddStand(people.firstClass.transform, new Vector3(-3.9f, 2.85f, 4.8f), 180f, 4142, role: "ChieuChild");
            var childCollider = child.agent.GetComponent<CapsuleCollider>();
            childCollider.height = 1.24f; childCollider.center = Vector3.up * .62f; childCollider.radius = .16f;
            CrowdBuilder.GiveBag(people.firstClass.transform, chieu, "FirstClassSuitcase", 4140);
            var encounter = BuilderUtils.Group("ChieuEncounter", tasks).AddComponent<VoyageEncounter>(); encounter.orderKind = "chieu"; encounter.speaker = chieu;
            var ct = BuilderUtils.Group("ChieuTalkTarget", people.firstClass.transform); ct.transform.localPosition = new Vector3(-3f, 4.35f, 4.65f);
            ct.AddComponent<BoxCollider>().size = new Vector3(0.8f, 1f, 0.2f); encounter.target = AddTarget(ct, encounter, "chieu"); encounter.stand = new Vector3(-3f, 2.85f, 4.25f); list.Add(encounter);
            people.seasick = CrowdBuilder.Root(env, "SeasickPassengers").gameObject;
            foreach (var p in new[] { new Vector3(-19f, 0f, 5.8f), new Vector3(-15f, 0f, 5.8f), new Vector3(-12f, 0f, 5.8f) })
            {
                // Người ngồi có ghế thật được đo theo SitIdle; không giả tư thế ngồi bệt bằng cách hạ hông.
                var sick = p.x == -15f ? Seated(people.seasick.transform, p, 180f, 4165, "SoldierB", m)
                    : CrowdBuilder.AddPose(people.seasick.transform, p, 0f, 4150 + Mathf.RoundToInt(-p.x), "LeanRail", role: "SoldierB").agent;
                sick.gameObject.AddComponent<SeasickPose>();
            }
            people.cardPlayers = CrowdBuilder.Root(env, "EveningCards").gameObject;
            Table(people.cardPlayers.transform, new Vector3(2.2f, 0f, 0.2f), m, 0.8f);
            for (int i = 0; i < 2; i++)
            {
                var pos = new Vector3(2.2f, 0f, i == 0 ? -0.45f : 0.85f);
                Seated(people.cardPlayers.transform, pos, i == 0 ? 0f : 180f, 4160 + i, "Helper", m);
            }
            for (int i = 0; i < 6; i++)
            {
                BuilderUtils.Block("PlayingCard", people.cardPlayers.transform, new Vector3(1.97f + i * 0.07f, 0.835f, 0.2f), new Vector3(0.06f, 0.002f, 0.09f), m.Paper, false, false, new Vector3(0f, i * 15f, 0f));
                BuilderUtils.Prim(PrimitiveType.Sphere, "CardPip", people.cardPlayers.transform, new Vector3(1.97f + i * 0.07f, 0.84f, 0.2f), new Vector3(0.01f, 0.002f, 0.01f), meatMat, false, false);
            }
            var zone = people.cardPlayers.AddComponent<BoxCollider>(); zone.isTrigger = true; zone.center = new Vector3(0f, 1f, 0f); zone.size = new Vector3(10f, 3f, 6f);
            var laugh = people.cardPlayers.AddComponent<HaiBanTay.Audio.AmbienceZone>();
            laugh.clipKey = "SFX/story_chuckle"; laugh.volume = 0.12f;
            // Tối ngày 1 (S16: "trong khi chúng tôi nghỉ hoặc đánh bài, anh Ba đọc hay viết"): ông Mai ngồi đầu bàn bài phía đông.
            var cardMai = CrowdBuilder.Root(env, "EveningCardsMai").gameObject;
            Seated(cardMai.transform, new Vector3(2.95f, 0f, 0.2f), -90f, 4163, "Mai", m);
            BuilderUtils.ClearStatic(cardMai);
            BuilderUtils.SetRenderingLayer(cardMai, WorldRenderingLayers.Interior);
            cardMai.SetActive(false);
            d.cast.cardMai = cardMai;
            people.letterFriend = CrowdBuilder.Root(env, "VietnameseLetterFriend").gameObject;
            // Bạn mù chữ của ông Mai (S16: "anh giúp những người bạn mù chữ của tôi viết thư"): người khác, không mượn mặt ông Mai.
            // DIRECTOR §5/§9: chính là nhân viên thì thầm ở bến Nhà Rồng (NhaRongSceneBuilder jobStaff[0]: vai Helper, seed 851 cùng màu).
            Seated(people.letterFriend.transform, new Vector3(-4.3f, 0f, -4.3f), 90f, 851, "Helper", m);
            var coffee = Delivery(env, tasks, "coffee", new Vector3(0.77f, 0.92f, 0.25f), new Vector3(-7f, 0.85f, 1.8f), m);
            coffee.recipient = d.cast.soldierA;
            coffee.pickupStand = new Vector3(1.6f, 0f, -0.7f); coffee.deliveryStand = new Vector3(-6f, 0f, 1.8f);
            ChoreProps.Pot(coffee.load.transform, "SecretCoffeeCup", 0.11f, 0.13f, m.Paper); list.Add(coffee);
            foreach (var group in new[] { people.cooks, people.cardPlayers, people.seasick, people.firstClass, people.letterFriend, people.crewMeal })
            {
                BuilderUtils.ClearStatic(group);
                BuilderUtils.SetRenderingLayer(group, group == people.firstClass || group == people.seasick ? WorldRenderingLayers.Exterior : WorldRenderingLayers.Interior);
                group.SetActive(false);
            }
            people.firstClass.transform.SetParent(layout.transform, true);
            people.seasick.transform.SetParent(layout.transform, true);
            return list.ToArray();
        }
        static VoyageDelivery Delivery(Transform env, Transform tasks, string kind, Vector3 a, Vector3 b, Mats m)
        {
            var task = BuilderUtils.Group("Story_" + kind, tasks).AddComponent<VoyageDelivery>(); task.orderKind = kind;
            var go = BuilderUtils.Group("StoryLoad_" + kind, env); go.transform.localPosition = a;
            go.layer = BuilderUtils.Layer("Holdable"); go.tag = "Holdable";
            var col = go.AddComponent<BoxCollider>(); col.center = new Vector3(0f, 0.24f, 0f); col.size = new Vector3(0.52f, 0.48f, 0.4f);
            go.AddComponent<Rigidbody>().isKinematic = true; task.load = go.AddComponent<Holdable>(); task.load.kind = kind; task.load.twoHanded = kind != "coffee";
            var back = go.AddComponent<ReturnToStart>(); back.killY = -4.5f; back.useArea = true; back.area = new Bounds(new Vector3(0f, 2f, 0f), new Vector3(130f, 30f, 25f));
            task.home = Beacon(env, "StoryHome_" + kind, a); task.placed = Beacon(env, "StoryPlaced_" + kind, b);
            var target = BuilderUtils.Group("StoryDestination_" + kind, env); target.transform.localPosition = b;
            var tc = target.AddComponent<BoxCollider>(); tc.size = new Vector3(0.52f, 0.28f, 0.4f); tc.isTrigger = true;
            task.destination = AddTarget(target, task, kind);
            BuilderUtils.SetRenderingLayer(go, WorldRenderingLayers.Interior);
            go.SetActive(false);
            return task;
        }
        static PeelStation Peeling(Transform env, Transform tasks, bool asparagus, DayCast cast, Mats m)
        {
            var task = BuilderUtils.Group(asparagus ? "AsparagusStation" : "PeelStation", tasks).AddComponent<PeelStation>(); task.asparagus = asparagus; task.cast = cast;
            task.nearbyCrew = System.Array.FindAll(env.GetComponentsInChildren<NpcRoutine>(), x => x is CookRoutine || x is HelperRoutine);
            var b = BuilderUtils.Group(asparagus ? "AsparagusBoard" : "PeelBoard", env); b.transform.localPosition = new Vector3(0.35f, 0.93f, -0.27f);
            BuilderUtils.Block("Board", b.transform, Vector3.zero, new Vector3(0.55f, 0.02f, 0.4f), m.CuttingBoard, false, false);
            var box = b.AddComponent<BoxCollider>();
            if (asparagus) box.size = new Vector3(0.56f, 0.25f, 0.42f);
            // Khoai: nhìn vào xô, chậu vỏ hay nồi nước đều là chỗ gọt.
            else { box.center = new Vector3(0f, 0.1f, 0.02f); box.size = new Vector3(1.02f, 0.3f, 0.46f); }
            task.board = AddTarget(b, task, "peel");
            // Ông Mai đứng bên kia bàn, đối diện Ba. Gọt khoai làm mẫu thì ông đứng sát mép bàn (tay ông cách mắt Ba chừng một
            // mét); măng tây thì lùi lại, chừa lối đi ra boong bỏ măng gọt hỏng.
            task.maiSpot = Beacon(env, "MaiPeelingSpot", new Vector3(0.4f, 0f, asparagus ? 1f : 0.68f)); task.maiSpot.localRotation = Quaternion.Euler(0f, 180f, 0f); task.stand = new Vector3(0.35f, 0f, -0.69f);
            if (asparagus) AsparagusProps(task, b.transform, m); else PotatoProps(task, b.transform, m);
            BuilderUtils.ClearStatic(b); BuilderUtils.SetRenderingLayer(b, WorldRenderingLayers.Interior); b.SetActive(false);
            return task;
        }
        static Transform PeelingKnife(Transform parent, Mats m)
        {
            // Dao gọt: lưỡi thép hình nêm, sống cong xuống mũi (ChoreProps.PeelingBlade), khâu chặn, cán gỗ tròn dẹt; cán dọc +X
            // (về tay phải), lưỡi nằm mặt phẳng XZ, lưỡi cắt ở −Z.
            var knife = BuilderUtils.Group("PeelingKnife", parent).transform;
            var steel = AssetFactory.Lit("KnifeSteel", new Color(0.6f, 0.61f, 0.63f), 0.78f, 0.9f);
            BuilderUtils.Prim(PrimitiveType.Capsule, "WoodHandle", knife, new Vector3(0.077f, 0f, 0.0005f), new Vector3(0.016f, 0.047f, 0.021f), m.Wood, false, false, new Vector3(0f, 0f, 90f));
            BuilderUtils.Block("Bolster", knife, new Vector3(0.028f, 0f, 0.0005f), new Vector3(0.006f, 0.012f, 0.019f), steel, false, false);
            var blade = new GameObject("SteelBlade");
            blade.transform.SetParent(knife, false);
            blade.AddComponent<MeshFilter>().sharedMesh = ChoreProps.PeelingBlade();
            blade.AddComponent<MeshRenderer>().sharedMaterial = steel;
            return knife;
        }
        /// <summary>Chỗ cầm củ (z theo thớt, âm = phía Ba): sát mép thớt gần người, trong tầm tay đứng thẳng.</summary>
        const float HeldNear = -0.1f;

        static void AsparagusProps(PeelStation task, Transform board, Mats m)
        {
            // Cọng măng cầm nâng khỏi thớt, sát mép phía Ba (đứng thẳng, camera không cúi: 17/10); dao, tay, dải vỏ theo cùng khung.
            var b = BuilderUtils.Group("AsparagusHeld", board).transform;
            b.localPosition = new Vector3(0f, 0.1f, HeldNear - 0.07f);
            task.vegetable = BuilderUtils.Group("Vegetable", b).transform;
            task.asparagusVisual = BuilderUtils.Group("Asparagus", task.vegetable);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Stalk", task.asparagusVisual.transform, new Vector3(0f, 0.04f, 0f), new Vector3(0.025f, 0.15f, 0.025f), m.Leaf, false, false).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < 5; i++) BuilderUtils.Prim(PrimitiveType.Capsule, "TipScale", task.asparagusVisual.transform, new Vector3(0.008f * Mathf.Sin(i * 2f), 0.04f, 0.13f + i * 0.009f), new Vector3(0.02f, 0.022f, 0.02f), m.Leaf, false, false);
            task.knife = PeelingKnife(b, m);
            task.leftGrip = Beacon(b, "PeelLeftGrip", new Vector3(-0.04f, 0.08f, 0.1f));
            task.rightGrip = Beacon(task.knife, "KnifeRightGrip", new Vector3(0.1f, 0f, 0f));
            task.peel = BuilderUtils.Group("PeelRibbon", b).transform;
            var ribbon = task.peel.gameObject.AddComponent<LineRenderer>(); ribbon.useWorldSpace = false; ribbon.sharedMaterial = m.Wicker; ribbon.widthMultiplier = 0.015f; ribbon.positionCount = 32;
            for (int i = 0; i < 32; i++) ribbon.SetPosition(i, new Vector3(-0.1f + 0.025f * Mathf.Sin(i * 0.6f), 0.05f + 0.025f * Mathf.Cos(i * 0.6f), -0.15f + i * 0.009f));
        }
        /// <summary>
        /// Gọt khoai như thật (D1): xô gỗ đầy khoai bên trái, chậu men hứng vỏ dưới đôi tay, nồi nước bên phải, dao nằm trên
        /// thớt. Củ khoai sinh lúc chạy (PotatoPeeling); builder chỉ đặt chỗ (xô, nồi, tay cầm) theo toạ độ của thớt.
        /// </summary>
        static void PotatoProps(PeelStation task, Transform b, Mats m)
        {
            const float table = -0.01f; // mặt bàn dưới thớt (thớt dày 2 cm, tâm ở gốc)
            var p = BuilderUtils.Group("Potatoes", b).AddComponent<PotatoPeeling>();
            task.potatoes = p;
            p.skin = AssetFactory.PotatoSkin();
            p.flesh = AssetFactory.Lit("PotatoFlesh", new Color(0.9f, 0.82f, 0.56f), 0.55f);
            p.turnipSkin = AssetFactory.TurnipSkin();
            p.turnipFlesh = AssetFactory.Lit("TurnipFlesh", new Color(0.95f, 0.94f, 0.88f), 0.5f);
            // Chậu men hứng vỏ, đặt trên thớt ngay dưới chỗ cầm củ.
            var enamel = AssetFactory.Lit("Enamel", new Color(0.84f, 0.84f, 0.8f), 0.6f);
            // Củ cầm trước bụng, sát mép bàn phía Ba (đứng thẳng, camera không cúi: 17/10); chậu hứng vỏ ngay dưới.
            var bowlAt = new Vector3(-0.04f, 0.01f, HeldNear);
            ChoreProps.OpenVessel(b, "PeelBowl", bowlAt, 0.07f, 0.11f, 0.06f, 0.005f, 0.006f, enamel);
            p.bowl = Beacon(b, "PeelBowlFloor", bowlAt + Vector3.up * 0.007f); p.bowlRadius = 0.06f;
            // Tư thế cầm củ: trên chậu, cách thân chừng 40 cm (tay duỗi vừa), trục củ chúi ra trước, dải đang gọt ngửa lên.
            p.hold = Beacon(b, "PotatoHold", new Vector3(-0.04f, 0.2f, HeldNear)); p.hold.localRotation = Quaternion.Euler(18f, -8f, 0f);
            p.palm = BuilderUtils.Group("PotatoPalm", b).transform; p.palm.SetLocalPositionAndRotation(p.hold.localPosition, p.hold.localRotation);
            // Tay trái ôm củ từ dưới, ngón vòng sang phải. Đích của tay là cổ tay (+Z hướng ngón, +Y mu bàn tay), cổ tay lùi một gang
            // về phía người. Lật 180° quanh trục ngón so với bản đầu (17/10 "Lúc gọt cũng thế": cẳng tay trái vặn ngược +72…+111°, lật
            // rồi −77°, ClipFit17.FlipFit), giữ nguyên điểm lòng bàn tay chạm củ.
            var leftFingers = new Vector3(0.75f, 0.35f, 0.55f).normalized;
            var leftBack = new Vector3(0.3f, 0.95f, 0f).normalized;
            p.leftGrip = Beacon(p.palm, "PotatoLeftGrip", new Vector3(-0.012f, -0.045f, 0f) - leftFingers * PotatoPeeling.HandReach + leftBack * 0.01f);
            p.leftGrip.localRotation = Quaternion.LookRotation(leftFingers, leftBack);
            p.knife = PeelingKnife(b, m);
            // Tay phải nắm cán (mesh Grip): cán xuyên qua tâm nắm tay, mu bàn tay ngửa theo mặt lưỡi.
            p.rightGrip = Beacon(p.knife, "KnifeRightGrip", new Vector3(0.075f, HandsRig.GripBelow, -HandsRig.GripForward));
            p.knifeRest = Beacon(b, "KnifeRest", new Vector3(0.17f, 0.0105f, -0.13f)); p.knifeRest.localRotation = Quaternion.Euler(0f, -15f, 0f);
            p.knife.SetLocalPositionAndRotation(p.knifeRest.localPosition, p.knifeRest.localRotation);
            p.rest = Beacon(b, "PotatoRest", new Vector3(0.17f, 0.04f, 0.12f)); p.rest.localRotation = Quaternion.Euler(0f, 35f, 0f);
            // Ông Mai gọt mẫu: củ ngang ngực dưới, chìa ra trước người cho Ba thấy, trục củ chúi xuống như tay Ba; vỏ rơi xuống đĩa men
            // trên bàn ngay dưới tay ông.
            p.demoHold = Beacon(task.maiSpot, "MaiDemoHold", new Vector3(-0.04f, 1.26f, 0.4f)); p.demoHold.localRotation = Quaternion.Euler(18f, 0f, 0f);
            var plateAt = b.InverseTransformPoint(p.demoHold.position); plateAt.y = table;
            ChoreProps.OpenVessel(b, "MaiPeelPlate", plateAt, 0.065f, 0.1f, 0.03f, 0.004f, 0.005f, enamel);
            p.demoBowl = Beacon(b, "MaiPeelPlateFloor", plateAt + Vector3.up * 0.006f); p.demoBowlRadius = 0.075f;
            // Xô gỗ có hai đai sắt; khoai xếp ba lớp, lớp trên thấp hơn miệng xô.
            var bucketAt = new Vector3(-0.42f, table, 0.12f);
            ChoreProps.OpenVessel(b, "PotatoBucket", bucketAt, 0.1f, 0.115f, 0.17f, 0.012f, 0.015f, m.Wood);
            foreach (float y in new[] { 0.024f, 0.134f })
            {
                float r = 0.1f + 0.015f * y / 0.17f;
                ChoreProps.Collar(b, "BucketHoop", bucketAt + Vector3.up * y, r - 0.001f, r + 0.004f, 0.014f, m.Iron);
            }
            var rng = new System.Random(51);
            float R(float a, float c) => a + (c - a) * (float)rng.NextDouble();
            var slots = new List<Transform>();
            void Layer(float y, float radius, int count, float phase)
            {
                for (int i = 0; i < count; i++)
                {
                    float a = (phase + i * 360f / count) * Mathf.Deg2Rad;
                    var slot = Beacon(b, "BucketSlot" + slots.Count, bucketAt + new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius));
                    slot.localRotation = Quaternion.Euler(R(-15f, 15f), R(0f, 360f), R(0f, 360f));
                    slots.Add(slot);
                }
            }
            Layer(0.045f, 0.048f, 4, 20f); Layer(0.09f, 0.052f, 4, 65f); Layer(0.13f, 0.03f, 2, 0f);
            p.bucketSlots = slots.ToArray();
            // Nồi nước bên phải: củ trắng chìm dưới mặt nước đục.
            var potAt = new Vector3(0.43f, table, 0.06f);
            // Quai nồi quay ra trước, ra sau (không chìa sang thớt).
            ChoreProps.Pot(b, "PotatoWaterPot", 0.26f, 0.15f, m.Iron).transform.SetLocalPositionAndRotation(potAt, Quaternion.Euler(0f, 90f, 0f));
            BuilderUtils.Prim(PrimitiveType.Cylinder, "PotatoPotWater", b, potAt + Vector3.up * 0.115f, new Vector3(0.235f, 0.001f, 0.235f),
                AssetFactory.LitTransparent("PotatoWater", new Color(0.2f, 0.23f, 0.2f, 0.5f), 0.92f), false, false);
            p.potMouth = Beacon(b, "PotatoPotMouth", potAt + Vector3.up * 0.115f);
            var potSlots = new List<Transform>();
            for (int i = 0; i < 9; i++)
            {
                float a = (i * 137.5f) * Mathf.Deg2Rad, r = i == 0 ? 0f : 0.035f + 0.025f * (i % 3) / 2f;
                var slot = Beacon(b, "PotSlot" + i, potAt + new Vector3(Mathf.Cos(a) * r, 0.05f + 0.022f * (i / 4), Mathf.Sin(a) * r));
                slot.localRotation = Quaternion.Euler(R(-10f, 10f), R(0f, 360f), R(0f, 360f));
                potSlots.Add(slot);
            }
            p.potSlots = potSlots.ToArray();
            var ripple = BuilderUtils.Group("PotatoRipple", b);
            ripple.transform.localPosition = potAt + Vector3.up * 0.117f;
            ripple.AddComponent<MeshFilter>();
            var rippleRenderer = ripple.AddComponent<MeshRenderer>();
            rippleRenderer.sharedMaterial = AssetFactory.Transparent("WaterRipple", new Color(0.86f, 0.9f, 0.9f, 0.5f));
            rippleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            p.ripple = rippleRenderer;
        }
        static void OpenCrate(Transform p, float w, float h)
        {
            // Vân nhỏ cho thùng sát camera; không đổi mật độ gỗ của phông cảng nhập sẵn.
            var wood = AssetFactory.Lit("PropWood", new Color(.4f, .27f, .16f), .15f);
            BuilderUtils.Block("CrateBottom", p, new Vector3(0f, 0.02f, 0f), new Vector3(w, 0.04f, 0.52f), wood, false, false);
            foreach (float x in new[] { -w / 2f, w / 2f }) BuilderUtils.Block("CrateSide", p, new Vector3(x, h / 2f, 0f), new Vector3(0.025f, h, 0.52f), wood, false, false);
            foreach (float z in new[] { -0.26f, 0.26f }) BuilderUtils.Block("CrateEnd", p, new Vector3(0f, h / 2f, z), new Vector3(w, h, 0.025f), wood, false, false);
        }
        /// <summary>
        /// Phòng ăn nhân viên sau vách nam, phía tây hầm than: sàn, trần, ba vách, bàn, đèn và light probe. Vách nam của khối bếp
        /// (ShipInterior) luôn chừa cửa vào phòng này, nên cảnh nào dựng khối bếp cũng phải dựng phòng (01 và 03 từng để cửa mở ra khoảng
        /// trống: bước vào là rơi xuống). Chỗ ngồi và đồ ăn của câu chuyện do BuildVoyageStory thêm riêng cho cảnh 02.
        /// </summary>
        public static Transform BuildCrewDiningRoom(Transform env, Mats m)
        {
            var mess = BuilderUtils.Group("CrewDiningRoom", env).transform;
            BuilderUtils.Block("DiningFloor", mess, new Vector3(-2.65f, -0.05f, -4.35f), new Vector3(4.5f, 0.1f, 2.5f), m.Floor);
            BuilderUtils.Block("DiningCeiling", mess, new Vector3(-2.65f, 2.65f, -4.35f), new Vector3(4.5f, 0.1f, 2.5f), m.Ceiling);
            BuilderUtils.Block("DiningWest", mess, new Vector3(-4.95f, 1.3f, -4.35f), new Vector3(0.1f, 2.6f, 2.5f), m.Wall);
            BuilderUtils.Block("DiningSouth", mess, new Vector3(-2.65f, 1.3f, -5.6f), new Vector3(4.5f, 2.6f, 0.1f), m.Wall);
            BuilderUtils.Block("DiningEast", mess, new Vector3(-0.4f, 1.3f, -4.35f), new Vector3(0.1f, 2.6f, 2.5f), m.Wall);
            Table(mess, new Vector3(-3.45f, 0f, -4.3f), m, 1.1f);
            Props.HangingLamp(mess, new Vector3(-3.1f, 2.5f, -4.3f), realtime: false, intensity: 10f, range: 4f);
            BuilderUtils.ProbeGrid("LightProbes_Dining", mess, new Vector3(-2.7f, 1.2f, -4.3f), new Vector3(4f, 2.2f, 2f), new Vector3(1f, 1.1f, 1f));
            BuilderUtils.SetRenderingLayer(mess.gameObject, WorldRenderingLayers.Interior);
            return mess;
        }
        static void Table(Transform p, Vector3 pos, Mats m, float width)
        {
            BuilderUtils.Block("StoryTableTop", p, pos + Vector3.up * 0.81f, new Vector3(width, 0.04f, 0.65f), m.Wood, false, false);
            foreach (float x in new[] { -width * 0.4f, width * 0.4f }) foreach (float z in new[] { -0.24f, 0.24f })
                BuilderUtils.Block("StoryTableLeg", p, pos + new Vector3(x, 0.4f, z), new Vector3(0.04f, 0.8f, 0.04f), m.Wood, false, false);
        }
        /// <summary>Độ hạ thân của lần gọi Seated gần nhất (VoyagePeople cần để đứng dậy, ngồi xuống).</summary>
        static float LastSeatDrop;
        // Clip ngồi lệch hông khỏi gốc; đặt mông đúng ghế, thay vì đặt ghế dưới gốc nhân vật.
        static HaiBanTay.Npc.NpcAgent Seated(Transform parent, Vector3 pos, float yaw, int seed, string role, Mats m, Transform seatParent = null, bool buildSeat = true)
        {
            var routine = CrowdBuilder.AddPose(parent, pos, yaw, seed, "SitIdle", "SitTalk", role);
            var animator = routine.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman || !animator.HasState(0, Animator.StringToHash("SitIdle")))
                throw new System.InvalidOperationException(role + " thiếu pose ngồi humanoid.");
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind(); animator.Update(0f); animator.Play("SitIdle", 0, 0f); animator.Update(0.01f);
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips).position;
            var intended = parent.TransformPoint(pos);
            routine.transform.position += new Vector3(intended.x - hips.x, 0f, intended.z - hips.z);
            // Clip có root height gốc: hạ đế giày xuống sàn trước khi đo mặt ghế.
            float bottom = float.PositiveInfinity;
            var baked = new Mesh();
            var lod = routine.GetComponentInChildren<LODGroup>(true);
            if (lod == null) throw new System.InvalidOperationException(role + " thiếu LODGroup.");
            foreach (var renderer in lod.GetLODs()[0].renderers)
            {
                if (renderer is not SkinnedMeshRenderer skin) continue;
                skin.BakeMesh(baked);
                foreach (var vertex in baked.vertices) bottom = Mathf.Min(bottom, skin.transform.TransformPoint(vertex).y);
            }
            Object.DestroyImmediate(baked);
            if (float.IsInfinity(bottom)) throw new System.InvalidOperationException(role + " thiếu mesh để đo đế giày.");
            // Chỉ hạ phần thân (Body, có Animator); gốc NPC vẫn ở mặt sàn để điểm xuất phát đúng sàn.
            animator.transform.position += Vector3.up * (intended.y - bottom);
            LastSeatDrop = intended.y - bottom;
            float height = hips.y - bottom - 0.1f;
            Debug.Log($"Ghế {role}: hông={hips.y:F3}, đế={bottom:F3}, mặt ghế={height:F3}");
            if (height < 0.08f) throw new System.InvalidOperationException(role + " có độ cao ghế không hợp lệ: " + height);
            if (!buildSeat) { }
            else if (seatParent != null)
                Seat(seatParent, seatParent.InverseTransformPoint(parent.TransformPoint(pos)), m, height);
            else
                Seat(parent, pos, m, height);
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            return routine.agent;
        }
        static void Seat(Transform p, Vector3 pos, Mats m, float height = 0.45f)
        {
            BuilderUtils.Block("StorySeat", p, pos + Vector3.up * (height - 0.02f), new Vector3(0.38f, 0.04f, 0.38f), m.Wood, false, false);
            // Một trụ giữa và hai thanh chân chữ thập: bốn chân ở góc nằm đúng chỗ cẳng chân người ngồi (bot 20/10: cẳng chân Ba lún chân
            // ghế 6,5 cm ở bàn ăn thủy thủ, NPC ngồi cũng lún 6 cm).
            BuilderUtils.Block("StorySeatPost", p, pos + new Vector3(0f, (height - 0.04f) * 0.5f, 0f), new Vector3(0.07f, height - 0.04f, 0.07f), m.Wood, false, false);
            BuilderUtils.Block("StorySeatFootX", p, pos + new Vector3(0f, 0.0175f, 0f), new Vector3(0.3f, 0.035f, 0.045f), m.Wood, false, false);
            BuilderUtils.Block("StorySeatFootZ", p, pos + new Vector3(0f, 0.0175f, 0f), new Vector3(0.045f, 0.035f, 0.3f), m.Wood, false, false);
        }
        static GameObject Cutlery(Transform p, Mats m, bool spoon)
        {
            var go = BuilderUtils.Group(spoon ? "Spoon" : "Fork", p); go.transform.localPosition = new Vector3(0.15f, 0.14f, -0.1f);
            BuilderUtils.Block("Handle", go.transform, new Vector3(0f, 0f, -0.06f), new Vector3(0.015f, 0.006f, 0.15f), m.Iron, false, false);
            if (spoon) BuilderUtils.Prim(PrimitiveType.Sphere, "SpoonBowl", go.transform, new Vector3(0f, 0f, 0.045f), new Vector3(0.04f, 0.009f, 0.055f), m.Iron, false, false);
            else for (int i = 0; i < 4; i++) BuilderUtils.Block("ForkTine", go.transform, new Vector3(-0.012f + i * 0.008f, 0f, 0.045f), new Vector3(0.004f, 0.004f, 0.045f), m.Iron, false, false);
            return go;
        }
    }
}
