using System.Collections;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Ending;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    [Category("STOVE")]
    public sealed class StovePlayTests
    {
        [TearDown]
        public void Close()
        {
            Time.captureDeltaTime = 0f;
            if (GameRoot.Instance != null)
            {
                GameRoot.Instance.Input.SimUse = false;
                GameRoot.Instance.Input.SimLook = Vector2.zero;
            }
            PlayTestUtil.ExitSim();
        }

        // MotionProbe đo theo nhịp 60 Hz: đặt đồng hồ chuẩn ngay trước khi đo (không phụ thuộc test chạy trước); khi rớt in trạng thái đồng hồ.
        static void FixClock()
        {
            Sim.Enabled = false;
            Time.timeScale = 1f;
            Time.captureDeltaTime = 1f / 60f;
        }

        static string Clock() => $"timeScale {Time.timeScale}, captureDeltaTime {Time.captureDeltaTime}, Sim {Sim.Enabled}, dt {Time.deltaTime}";

        public static IEnumerator Galley()
        {
            yield return ChoreTestUtil.Galley();
            var coal = Object.FindAnyObjectByType<CoalStation>();
            // Tách người phụ bếp khỏi phép đo xẻng người chơi; routine sản xuất không bị sửa.
            if (coal.helper != null)
            {
                coal.helper.StopAllCoroutines();
                coal.helper.enabled = false;
                coal.helper.shovelLoad.SetActive(false);
            }
            coal.Fire.Set(0f, 0f);
            coal.SetStock(coal.cells.Length);
            Time.captureDeltaTime = 1f / 60f;
            yield return null;
        }

        public static IEnumerator LoadShovel(CoalStation coal)
        {
            var who = PlayerRig.Current.interactor;
            if (who.Tool != Tool.Shovel) coal.hook.Interact(who);
            ChoreTestUtil.Look(new Vector3(0.65f, 0f, 0.95f), coal.bin.GetComponentInChildren<Collider>().bounds.center);
            yield return PlayTestUtil.WaitUntil(() => who.IsLookingAt(coal.bin), 3f);
            Assert.IsTrue(who.IsLookingAt(coal.bin), "STOVE: nhìn trúng thùng than");
            GameRoot.Instance.Input.SimUse = true;
            yield return PlayTestUtil.WaitUntil(() => coal.Shovel.Loaded, 5f);
            Assert.IsTrue(coal.Shovel.Loaded, "STOVE: xúc than thật qua input");
            Assert.IsTrue(PlayerRig.Current.hands.shovelLoad.activeInHierarchy);
        }

        public static void LookAtCell(CoalStation coal, int cell, bool ash = false)
        {
            var c = coal.cells[cell];
            var target = ash ? c.ashDoor.transform : c.mouth.transform;
            var feet = target.position + target.forward * 0.8f;
            feet.y = coal.transform.position.y;
            ChoreTestUtil.Look(feet, target.position);
        }

        [UnityTest]
        public IEnumerator CoalFeedsOnlyTheAimedCellAndTheCorrectPotNeedsHeat()
        {
            yield return Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            var coal = d.coal;
            var who = PlayerRig.Current.interactor;
            d.Board.Clear();
            d.Board.Add(OrderKinds.Fire, coal.cells.Length);
            coal.OnOrder(coal.cells.Length);
            for (int cell = 0; cell < coal.cells.Length; cell++)
            {
                yield return LoadShovel(coal);
                if (cell == 0)
                {
                    yield return new WaitForSeconds(0.4f);
                    FixClock();
                    var probe = new GameObject("StoveShovelMotion").AddComponent<MotionProbe>()
                        .Watch("xẻng đầy", PlayerRig.Current.hands.shovelProp.transform)
                        .Watch("than trên lưỡi", PlayerRig.Current.hands.shovelLoad.transform);
                    for (int i = 0; i < 90; i++) yield return null;
                    Debug.Log("STOVE_SHOVEL " + probe.Report);
                    Assert.Greater(probe.Samples, 20);
                    Assert.Greater(probe.Samples - probe.Hitches, 20, "STOVE: phải có mẫu 60 Hz hợp lệ, không chỉ mẫu bị bỏ; " + Clock());
                    Assert.LessOrEqual(probe.Worst, MotionProbe.PopStep, probe.Report);
                    Object.Destroy(probe.gameObject);
                }
                LookAtCell(coal, cell);
                yield return PlayTestUtil.WaitUntil(() => who.IsLookingAt(coal.cells[cell].mouth), 3f);
                Assert.IsTrue(who.IsLookingAt(coal.cells[cell].mouth), "STOVE: miệng nạp ô " + cell);
                int poured = coal.Shovel.Poured;
                GameRoot.Instance.Input.SimUse = false;
                yield return PlayTestUtil.WaitUntil(() => coal.Shovel.Poured > poured, 2f);
                Assert.AreEqual(poured + 1, coal.Shovel.Poured);
                Assert.Greater(coal.CellState(cell).Fuel, coal.fuelPerPour - 0.1f);
                for (int other = cell + 1; other < coal.cells.Length; other++)
                    Assert.AreEqual(0f, coal.CellState(other).Fuel, "STOVE: ô khác không nhận than");
            }
            yield return null;
            Assert.IsTrue(d.Board.AllDone, "STOVE: credit đọc từ than đã nạp, không đòi làm lại");
            Assert.IsTrue(coal.Bin.Empty);
            coal.Fire.Tick(300f);
            float burnedAsh = coal.CellState(0).Ash;
            Assert.Greater(burnedAsh, 0f, "STOVE: than vừa nạp đã cháy thành tro tại ô đó");
            yield return RakeCell(d.ash, 0);
            Assert.AreEqual(0f, coal.CellState(0).Ash, 0.001f);
            Assert.AreEqual(burnedAsh, d.ash.Rake.AshAmount, 0.001f, "STOVE: chuỗi than → nhiệt → tro → xô không tạo thêm tro");

            d.meals.BeginMeal(new PhaseData { Id = "stove_heat", Dirty = 1 });
            var pot = d.meals.CookingPots[0];
            int hob = coal.CellForHob(d.meals.hobs[d.meals.HobOf(pot)]);
            coal.Fire.Set(0f, 0f);
            coal.CellState((hob + 1) % coal.cells.Length).Set(4.5f, 1f);
            d.meals.TickCooking(10f);
            Assert.IsFalse(d.meals.ReadyToServe, "STOVE: lửa ô bên cạnh không nấu chín nồi");
            Assert.AreEqual(0f, d.meals.HotSeconds(pot));
            coal.CellState(hob).Set(4.5f, 1f);
            d.meals.TickCooking(MealPots.CookingSeconds);
            Assert.IsTrue(d.meals.ReadyToServe);
            coal.Fire.Tick(300f);
            Assert.AreEqual(0f, coal.CellState(hob).Fuel, 0.001f);
            Assert.Greater(coal.CellState(hob).Ash, 0f);
        }

        public static IEnumerator RakeCell(AshStation ash, int cell)
        {
            var who = PlayerRig.Current.interactor;
            if (!ash.bucket.IsHeld) ash.bucket.Interact(who);
            Assert.AreEqual(ash.bucket, who.Holding);
            ash.OnInteract(ash.coal.cells[cell].bucketSpot, who);
            Assert.AreEqual(cell, ash.BucketCell);
            ash.hook.Interact(who);
            LookAtCell(ash.coal, cell, true);
            yield return PlayTestUtil.WaitUntil(() => who.IsLookingAt(ash.coal.cells[cell].ashDoor), 3f);
            Assert.IsTrue(who.IsLookingAt(ash.coal.cells[cell].ashDoor), "STOVE: nhìn trúng cửa tro ô " + cell);
            var input = GameRoot.Instance.Input;
            input.SimUse = true;
            float end = Time.realtimeSinceStartup + 10f;
            while (!ash.Rake.BucketFull && ash.coal.CellState(cell).Ash > 0.001f && Time.realtimeSinceStartup < end)
            {
                input.SimLook = new Vector2(0f, -24f);
                yield return null;
            }
            input.SimUse = false;
            input.SimLook = Vector2.zero;
            yield return null;
            Assert.Greater(ash.Rake.AshAmount, 0f, "STOVE: cào tro thật qua input");
        }

        [UnityTest]
        public IEnumerator BothRangesTransferAshThenRequireDumpAndExactStorage()
        {
            yield return Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            var ash = d.ash;
            var who = PlayerRig.Current.interactor;
            foreach (int cell in new[] { 0, 3 })
            {
                ash.coal.Fire.Set(0f, 0f);
                ash.coal.CellState(cell).Set(0f, 0f, 0.48f);
                d.Board.Clear();
                d.Board.Add(OrderKinds.Ash, 1);
                yield return RakeCell(ash, cell);
                Assert.AreEqual(0f, ash.coal.CellState(cell).Ash, 0.001f);
                Assert.AreEqual(0.48f, ash.Rake.AshAmount, 0.001f, "STOVE: ô mất bao nhiêu, xô nhận bấy nhiêu");
                for (int other = 0; other < ash.coal.cells.Length; other++)
                    if (other != cell) Assert.AreEqual(0f, ash.coal.CellState(other).Ash, 0.001f);
                Assert.LessOrEqual(ash.bucketAsh.localPosition.y, ash.ashFull.localPosition.y + 0.001f);
                Assert.GreaterOrEqual(ash.bucketAsh.localPosition.y, ash.ashBottom.localPosition.y);
                Assert.IsTrue(d.Board.HasOpen(OrderKinds.Ash), "STOVE: cào xong chưa được credit");
                ash.bucket.Interact(who);
                Assert.AreEqual(Tool.None, who.Tool, "STOVE: cào tự về móc khi xách xô");
                var stand = ash.chute.transform.position + new Vector3(1.3f, 0f, 0.3f);
                ChoreTestUtil.Look(stand, ash.chute.transform.position + Vector3.up);
                ash.OnInteract(ash.chute, who);
                yield return PlayTestUtil.WaitUntil(() => ash.AwaitingStore && !ash.bucket.Busy, 8f);
                Assert.IsTrue(ash.AwaitingStore);
                Assert.AreEqual(0f, ash.Rake.AshAmount);
                Assert.AreEqual(ash.bucket, who.Holding);
                Assert.IsTrue(d.Board.HasOpen(OrderKinds.Ash), "STOVE: đổ xong vẫn phải cất xô");
                who.ReleaseHeld();
                ash.bucket.Detach(ash.store.transform.position + Vector3.right * 0.12f, ash.store.transform.rotation, false);
                yield return null;
                Assert.IsTrue(ash.AwaitingStore, "STOVE: lệch bệ 12 cm chưa được credit");
                ash.bucket.Interact(who);
                ash.OnInteract(ash.store, who);
                yield return null;
                Assert.IsFalse(d.Board.HasOpen(OrderKinds.Ash), "STOVE: cất đúng chỗ mới xong lệnh tro");
                Assert.IsTrue(ash.BucketStored);
                FixClock();
                var probe = new GameObject("StoveBucketMotion").AddComponent<MotionProbe>().Watch("xô đã cất", ash.bucket.transform);
                for (int i = 0; i < 60; i++) yield return null;
                Debug.Log("STOVE_BUCKET " + probe.Report);
                Assert.Greater(probe.Samples, 20);
                Assert.Greater(probe.Samples - probe.Hitches, 20, "STOVE: phải có mẫu 60 Hz hợp lệ, không chỉ mẫu bị bỏ; " + Clock());
                Assert.LessOrEqual(probe.Worst, MotionProbe.PopStep, probe.Report);
                Assert.Less(Vector3.Distance(ash.bucket.transform.position, ash.store.transform.position), 0.005f);
                Assert.Less(Vector3.Distance(ash.bucket.Body.position, ash.store.transform.position), 0.005f);
                Object.Destroy(probe.gameObject);
            }
            ash.bucket.Interact(who);
            Assert.IsTrue(ash.Guide(who, out var target, out var key));
            Assert.AreEqual("guide.ash_store", key, "STOVE: không có tro thì cất xô rỗng, không dẫn tới cửa không cào được");
            Assert.AreEqual(ash.store.transform, target);
        }

        [UnityTest]
        public IEnumerator BasketWaitsForPlayerToLeaveAndLoadedShovelNeverPointsAtAnEmptyBin()
        {
            yield return Galley();
            var coal = Object.FindAnyObjectByType<CoalStation>();
            var who = PlayerRig.Current.interactor;
            coal.SetStock(0);
            var basket = coal.baskets[0];
            ChoreTestUtil.Look(coal.basketHomes[0].position + Vector3.right * 0.7f, basket.transform.position);
            Assert.IsTrue(coal.RefillForMontage());
            // Phép tua chỉ dùng sọt có thật. Để cả hai sọt rỗng, không có mục tiêu giả "sọt đầy".
            coal.SetStock(0);
            Assert.IsTrue(coal.RefillForMontage());
            yield return new WaitForSeconds(1f);
            Assert.IsTrue(coal.IsEmptyBasket(basket));
            Assert.IsFalse(coal.CanRestockBasket(basket));
            coal.SetStock(0);
            Assert.IsTrue(coal.Guide(who, out var target, out var key));
            Assert.AreEqual("guide.coal_restock_wait", key);
            Assert.AreEqual(coal.basketReturn.transform, target);
            ChoreTestUtil.Look(new Vector3(0f, 0f, -1f), new Vector3(0f, 1f, -5f));
            yield return PlayTestUtil.WaitUntil(() => !coal.IsEmptyBasket(basket), 4f);
            Assert.IsFalse(coal.IsEmptyBasket(basket), "STOVE: tiếp sọt sau khi người chơi ra ngoài tầm nhìn");
            Assert.IsTrue(coal.Guide(who, out target, out key));
            Assert.AreEqual("guide.coal_basket", key);
            Assert.IsTrue(System.Array.Exists(coal.baskets, b => b.transform == target && !coal.IsEmptyBasket(b)));

            coal.SetStock(1);
            yield return LoadShovel(coal);
            Assert.IsTrue(coal.Bin.Empty);
            Assert.IsTrue(coal.Guide(who, out target, out key));
            Assert.AreEqual("guide.pour_coal", key);
            coal.Fire.Set(FireLogic.MaxFuel, 1f);
            Assert.IsTrue(coal.Guide(who, out target, out key));
            Assert.AreEqual("guide.return_coal", key, "STOVE: mọi ô đầy thì trả than, không dẫn tới mục tiêu không nhận được");
            coal.OnInteract(coal.bin, who);
            Assert.AreEqual(1, coal.Bin.Stock);
            Assert.IsFalse(coal.Shovel.Loaded);
            GameRoot.Instance.Input.SimUse = false;
        }

        [UnityTest]
        public IEnumerator ExistingHelperTransfersRealStockToTheCellAtItsShovel()
        {
            yield return ChoreTestUtil.Galley();
            var coal = Object.FindAnyObjectByType<CoalStation>();
            coal.Fire.Set(0f, 0f);
            coal.SetStock(6);
            ChoreTestUtil.Look(new Vector3(4.5f, 0f, -0.3f), Vector3.up);
            Time.captureDeltaTime = 1f / 60f;
            int feeds = coal.helper.Feeds;
            yield return PlayTestUtil.WaitUntil(() => coal.helper.Feeds > feeds, 90f);
            Assert.Greater(coal.helper.Feeds, feeds, "STOVE: routine phụ bếp thật đã nạp một ô");
            yield return null;
            int fed = 0;
            for (int i = 0; i < coal.cells.Length; i++) if (coal.CellState(i).Fuel > 0f) fed++;
            Assert.Greater(fed, 0, "STOVE: cạnh đổ xẻng của NPC phải chuyển than vào trạng thái ô");
            Assert.Less(coal.Bin.Stock, 6, "STOVE: NPC lấy than thật trong thùng");
        }

        [UnityTest]
        public IEnumerator MarseilleUsesTheSameLiveCellsAndUsableAshDoors()
        {
            PlayTestUtil.Root().ResetProgress();
            SceneManager.LoadScene(SceneFlow.GameScenes[2]);
            yield return PlayTestUtil.WaitUntil(() => Object.FindAnyObjectByType<EndingDirector>() != null, 10f);
            yield return null;
            var ending = Object.FindAnyObjectByType<EndingDirector>();
            var coal = ending.finalMeals.coal;
            Assert.AreEqual(6, coal.cells.Length);
            Assert.AreEqual(HaiBanTay.Tasks.TaskState.Active, coal.State);
            Assert.AreEqual(HaiBanTay.Tasks.TaskState.Active, coal.ashStation.State);
            Assert.IsFalse(ending.finalMeals.ReadyToServe, "STOVE: nồi cảnh cuối cũng cần đủ thời gian trên ô nóng");
            yield return new WaitForSeconds(MealPots.CookingSeconds + 0.2f);
            Assert.IsTrue(ending.finalMeals.ReadyToServe);
            float ash = coal.TotalAsh;
            coal.Fire.Tick(10f);
            Assert.Greater(coal.TotalAsh, ash, "STOVE: bếp cảnh cuối cháy thành tro qua cùng model");
        }
    }
}
