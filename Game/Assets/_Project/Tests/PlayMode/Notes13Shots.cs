using System.Collections;
using System.Linq;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>Ảnh soát các ghi chú debug Android 12/10: xô tro trước cửa tro và đổ ở máng, xách xô/kẹp đá một tay,
    /// thùng cá nghiêng, sọt than đổ vào thùng, vệt bẩn, đống than, vòi bơm cạnh bồn rửa, phòng ăn hai ghế, cúi/ngẩng khi
    /// cầm đồ, chỉ dẫn từng bước, nút cảm ứng và màn Cách chơi.</summary>
    [Category("Screenshots"), Explicit]
    public sealed class Notes13Shots
    {
        static GameRoot Root => GameRoot.Instance;
        static PlayerRig Rig => PlayerRig.Current;
        static ShiftDirector Director => Object.FindAnyObjectByType<ShiftDirector>();

        [SetUp] public void SetUp() => Time.captureDeltaTime = 1f / 60f;

        [TearDown]
        public void TearDown()
        {
            InputGlyphs.Override = null;
            if (Root != null) { Root.Input.SimUse = false; Root.Input.SimLook = Vector2.zero; Root.Subtitles.StopAll(); }
            Time.captureDeltaTime = 0f;
            ScreenshotCapture.RestoreUi();
            PlayTestUtil.ExitSim();
        }

        static IEnumerator Shot(string name, float settle = 0.4f)
        {
            yield return new WaitForSeconds(settle);
            Root.UI.SetFade(0f);
            yield return ScreenshotCapture.Capture("notes13_" + name);
        }

        static void LookAngles(Vector3 feet, float yaw, float pitch)
        {
            Rig.motor.Teleport(feet, yaw);
            Rig.look.SetAngles(yaw, pitch);
        }

        static void Hold(Holdable h)
        {
            var who = Rig.interactor;
            if (who.Holding != null) who.ReleaseHeld();
            if (who.Tool != Tool.None) ToolHook.For(who.Tool)?.ReturnFrom(who);
            h.Locked = false;
            who.Pick(h);
            Assert.AreEqual(h, who.Holding);
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator AshCoalCarryAndRooms()
        {
            yield return ChoreTestUtil.Galley();
            var d = Director;
            var who = Rig.interactor;

            // 1. Chỉ dẫn đầu ca (quét bếp) và vệt bẩn mới trên sàn.
            var spot = d.sweep.spots.First(s => s.gameObject.activeSelf);
            ChoreTestUtil.Look(spot.transform.position + new Vector3(0.2f, 0f, -1.4f), spot.transform.position);
            yield return Shot("01_dirt_and_guide");
            Assert.IsNotNull(Root.UI.Guide.Text, "Có dòng chỉ dẫn việc tiếp theo.");

            // 2. Xô tro ở chỗ cất trong hầm than, cạnh máng tro.
            var ash = d.ash;
            Assert.IsTrue(ash.BucketStored);
            ChoreTestUtil.Look(new Vector3(1.3f, 0f, -3.6f), ash.store.transform.position + Vector3.up * 0.3f);
            yield return Shot("02_ash_bucket_store_and_coal_heap");
            ChoreTestUtil.Look(new Vector3(1.6f, 0f, -3.4f), new Vector3(1.6f, 0.3f, -5.1f));
            yield return Shot("03_coal_heap");

            // 3. Xách xô một tay: nhìn thẳng, cúi, ngẩng.
            Hold(ash.bucket);
            LookAngles(new Vector3(-1.5f, 0f, 0.6f), 0f, 10f);
            yield return Shot("04_hang_bucket_forward");
            LookAngles(new Vector3(-1.5f, 0f, 0.6f), 0f, 70f);
            yield return Shot("05_hang_bucket_down");
            LookAngles(new Vector3(-1.5f, 0f, 0.6f), 0f, -55f);
            yield return Shot("06_hang_bucket_up");

            // 4. Đặt xô giữa trước cửa tro.
            who.ReleaseHeld();
            ash.bucket.Detach(ash.spot.transform.position, ash.spot.transform.rotation, physics: false);
            Assert.IsTrue(ash.BucketReady);
            ChoreTestUtil.Look(new Vector3(-3.05f, 0f, 1.0f), ash.door.transform.position);
            yield return Shot("07_bucket_centered_at_ash_door");

            // 5. Đổ tro ở máng: chụp giữa lúc nghiêng.
            ash.Rake.ForceFull();
            Hold(ash.bucket);
            var chute = ash.chute.transform;
            var mouth = chute.TransformPoint(new Vector3(0.2f, 0.98f, 0f));
            var stand = mouth + new Vector3(1.0f, 0f, 0.15f);
            stand.y = chute.position.y;
            ChoreTestUtil.Look(stand, mouth);
            yield return new WaitForSeconds(0.3f);
            ash.OnInteract(ash.chute, who);
            yield return PlayTestUtil.WaitUntil(() => Vector3.Angle(ash.bucket.transform.up, Vector3.up) > 110f, 5f);
            yield return ScreenshotCapture.Capture("notes13_08_ash_pour_tilt");
            yield return PlayTestUtil.WaitUntil(() => !ash.bucket.Busy, 6f);
            Assert.IsTrue(ash.AwaitingStore);
            yield return Shot("09_ash_guide_store");
            who.ReleaseHeld();
            ash.bucket.Detach(ash.store.transform.position, ash.store.transform.rotation, physics: false);

            // 6. Sọt than: cầm, đổ vào thùng (giữa lúc nghiêng), sọt rỗng còn trên tay.
            var coal = d.coal;
            coal.SetStock(0);
            var basket = coal.baskets[0];
            Hold(basket);
            LookAngles(new Vector3(-0.4f, 0f, 0.6f), 0f, 15f);
            yield return Shot("10_coal_basket_carry");
            LookAngles(new Vector3(-0.4f, 0f, 0.6f), 0f, 75f);
            yield return Shot("11_coal_basket_look_down");
            ChoreTestUtil.Look(new Vector3(0.3f, 0f, 1.45f), coal.bin.transform.position + Vector3.up * 0.5f);
            yield return new WaitForSeconds(0.3f);
            coal.OnInteract(coal.bin, who);
            yield return PlayTestUtil.WaitUntil(() => Vector3.Angle(basket.transform.up, Vector3.up) > 120f, 5f);
            yield return ScreenshotCapture.Capture("notes13_12_coal_pour_tilt");
            yield return PlayTestUtil.WaitUntil(() => !coal.PouringBasket, 6f);
            Assert.IsTrue(coal.IsEmptyBasket(basket));
            Assert.AreEqual(coal.Bin.Capacity, coal.Bin.Stock);
            yield return Shot("13_coal_bin_full_basket_empty");
            who.ReleaseHeld();
            basket.gameObject.SetActive(false);

            // 7. Cầm chổi nhìn xuống chân: tay và tạp dề không xuyên bụng.
            d.sweep.hook.Interact(who);
            LookAngles(new Vector3(-1.2f, 0f, 0.4f), 0f, 78f);
            yield return Shot("14_broom_look_down");
            d.sweep.hook.ReturnFrom(who);

            // 8. Phòng rửa: bồn và vòi bơm; phòng ăn hai ghế.
            ChoreTestUtil.Look(new Vector3(5.6f, 0f, -0.4f), new Vector3(6.62f, 0.8f, -1.4f));
            yield return Shot("15_sink_and_pump");
            ChoreTestUtil.Look(new Vector3(-1.1f, 0f, -3.8f), new Vector3(-3.4f, 0.5f, -4.3f));
            yield return Shot("16_dining_room_two_chairs");

            // 9. Thùng cá, kẹp đá (đồ hầm thực phẩm, mang về bếp).
            var fish = d.storyStations.OfType<VoyageDelivery>().First(x => x.orderKind == OrderKinds.Fish);
            fish.OnOrder(1);
            Hold(fish.load);
            LookAngles(new Vector3(-1.5f, 0f, 0.3f), 0f, 20f);
            yield return Shot("17_fish_crate_carry");
            who.ReleaseHeld();
            fish.load.gameObject.SetActive(false);
            var ice = d.storyStations.OfType<VoyageDelivery>().First(x => x.orderKind == OrderKinds.Ice);
            ice.OnOrder(1);
            Hold(ice.load);
            LookAngles(new Vector3(-1.5f, 0f, 0.3f), 0f, 10f);
            yield return Shot("18_ice_tongs_forward");
            LookAngles(new Vector3(-1.5f, 0f, 0.3f), 0f, 72f);
            yield return Shot("19_ice_tongs_down");
            LookAngles(new Vector3(-1.5f, 0f, 0.3f), 0f, -55f);
            yield return Shot("20_ice_tongs_up");
            who.ReleaseHeld();
            ice.load.gameObject.SetActive(false);

            // 10. Nút cảm ứng (‖ góc trái), chữ phím đổi theo điện thoại, màn Cách chơi.
            InputGlyphs.Override = true;
            var touch = Root.GetComponent<TouchControls>() ?? Root.gameObject.AddComponent<TouchControls>();
            touch.Initialize(Root.Input, Root.UI, Root.Scenes, Root.Content, () => { }, () => false);
            ChoreTestUtil.Look(spot.transform.position + new Vector3(0.2f, 0f, -1.4f), spot.transform.position);
            d.sweep.hook.Interact(who);
            yield return Shot("21_touch_hud_guide");
            d.sweep.hook.ReturnFrom(who);
            Root.UI.Help.Open(null);
            yield return Shot("22_help_touch");
            Root.UI.Help.Close();
            Object.Destroy(touch);
        }
    }
}
