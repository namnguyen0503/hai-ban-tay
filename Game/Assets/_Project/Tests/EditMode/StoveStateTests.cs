using HaiBanTay.Editor.SceneBuilders;
using HaiBanTay.Shift;
using HaiBanTay.Tasks;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HaiBanTay.Tests
{
    [Category("STOVE")]
    public class StoveStateTests
    {
        GameObject _root;

        CoalStation Station()
        {
            _root = new GameObject("StoveStateFixture");
            var coal = _root.AddComponent<CoalStation>();
            coal.cells = new CoalStation.FireCell[2];
            for (int i = 0; i < coal.cells.Length; i++)
            {
                var mouth = new GameObject("Mouth_" + i);
                mouth.transform.SetParent(_root.transform);
                coal.cells[i] = new CoalStation.FireCell { mouth = mouth.AddComponent<TaskTarget>(), stoveIndex = i };
            }
            coal.Fire.Set(0f, 0f);
            Tick(coal, 0f);
            return coal;
        }

        static void Tick(CoalStation coal, float dt) => coal.Fire.Tick(dt);

        [TearDown]
        public void Close()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void EmptyBinCannotProduceFuelThroughTheSharedWorkCommand()
        {
            var coal = Station();
            coal.Bin.Set(0);
            coal.SimulateWork();
            Tick(coal, 0f);
            Assert.AreEqual(0f, coal.Fire.Fuel, 0.0001f, "STOVE: không tạo than khi thùng rỗng");
        }

        [Test]
        public void FuellingBeforeTheOrderKeepsItsCredit()
        {
            var coal = Station();
            coal.Fire.Set(FireLogic.MaxFuel, 1f);
            Tick(coal, 0f);
            coal.OnOrder(2);
            Assert.AreEqual(0, coal.PendingPours, "STOVE: ô đã đủ than thì không đòi xúc lại");
        }

        [Test]
        public void AshRemainsInTheCellThatBurnedTheFuel()
        {
            var coal = Station();
            coal.SimulateWork();
            Tick(coal, 20f);
            Tick(coal, 20f);
            Assert.AreEqual(0f, coal.CellState(1).Ash, 0.0001f, "STOVE: ô nguội không nhận tro từ ô khác");
            Assert.AreEqual((coal.fuelPerPour - coal.CellState(0).Fuel) * coal.Fire.AshPerFuel, coal.CellState(0).Ash, 0.0001f,
                "STOVE: tro của các lần đốt trước không bị ghi đè");
        }

        [TestCase(30)]
        [TestCase(60)]
        public void FuelBurnAndRemovedAshAreConserved(int fps)
        {
            var cell = new FireCellState { AshPerFuel = 0.3f };
            float added = cell.AddCoal(4.5f);
            for (int i = 0; i < 300 * fps; i++)
            {
                cell.Tick(1f / fps);
                if (i % fps == 0) cell.Rake(0.04f);
            }
            Assert.AreEqual(added, cell.Fuel + cell.BurnedFuel, 0.002f, "STOVE: than nạp = than còn + than đã cháy");
            Assert.AreEqual(cell.BurnedFuel * cell.AshPerFuel, cell.Ash + cell.RemovedAsh, 0.002f, "STOVE: tro sinh ra = tro còn + tro đã cào");
            Assert.AreEqual(0f, cell.Heat, 0.001f, "STOVE: hết than thì nguội");
            cell.AddCoal(6f);
            cell.Tick(240f);
            Assert.Greater(cell.Ash, 1f, "STOVE: không mất tro khi quá đầy");
        }

        [Test]
        public void RakingTransfersOnlyExistingAshAndCannotTouchAnotherCell()
        {
            var coal = Station();
            coal.CellState(0).Set(0f, 0f, 0.03f);
            coal.CellState(1).Set(0f, 0f, 0.4f);
            var rake = new RakeLogic();
            Assert.IsTrue(rake.Tick(true, true, -240f, availableAsh: coal.CellState(0).Ash));
            coal.CellState(0).Rake(rake.LastTransfer);
            Assert.AreEqual(0.03f, rake.AshAmount, 0.0001f);
            Assert.AreEqual(0f, coal.CellState(0).Ash, 0.0001f);
            Assert.AreEqual(0.4f, coal.CellState(1).Ash, 0.0001f);
            Assert.IsFalse(rake.Tick(true, true, -240f, availableAsh: 0f), "STOVE: ô rỗng không tạo tro trong xô");
        }

        [Test]
        public void FireOrderCannotDemandMoreCellsThanExist()
        {
            var coal = Station();
            coal.OnOrder(99);
            Assert.AreEqual(coal.cells.Length, coal.PendingPours);
            coal.CellState(0).Set(4.5f, 1f);
            coal.CellState(1).Set(4.5f, 1f);
            Assert.AreEqual(0, coal.PendingPours);
            var board = new OrderBoardLogic();
            board.Add(OrderKinds.Fire, coal.PoursNeeded(99));
            Assert.IsTrue(board.AllDone, "STOVE: việc làm sớm chiếu thẳng lên bảng");
        }

        [Test]
        public void EmptyAshNeedsNoBucketOrder()
        {
            var coal = Station();
            var ash = _root.AddComponent<AshStation>();
            ash.coal = coal;
            Assert.AreEqual(0, ash.BucketsNeeded(3), "STOVE: không yêu cầu cào tro không tồn tại");
        }

        [Test]
        public void AshOrderCountsOnlyTheAshPresentWhenItWasGiven()
        {
            var coal = Station();
            var ash = _root.AddComponent<AshStation>();
            ash.coal = coal;
            coal.CellState(0).Set(3f, 1f, 0.9f);
            ash.PinGoal();
            int need = ash.BucketsNeeded();
            Assert.AreEqual(2, need, "STOVE: 0,9 tro = 2 xô");
            // Lò cháy tiếp sinh thêm tro trong lúc cào: lệnh vẫn chỉ đòi phần tro lúc ra lệnh (bot ngày 3 17/10 lặp 102 lượt).
            coal.CellState(0).Rake(0.48f);
            for (int i = 0; i < 400; i++) coal.CellState(0).Tick(1f);
            Assert.Greater(coal.TotalAsh, 0.48f, "tro mới đã sinh");
            Assert.AreEqual(1, ash.BucketsNeeded(), "STOVE: tro sinh sau lúc ra lệnh không cộng vào lệnh");
            coal.CellState(0).Rake(0.42f);
            Assert.AreEqual(0, ash.BucketsNeeded(), "STOVE: cào hết phần đã chốt thì lệnh xong dù ghi còn tro mới");
            ash.PinGoal();
            Assert.Greater(ash.BucketsNeeded(), 0, "STOVE: lệnh sau chốt lại theo tro mới");
        }

        [TestCase("02_Galley")]
        [TestCase("03_Marseille")]
        public void EveryFireCellHasItsOwnAshDoor(string scene)
        {
            EditorSceneManager.OpenScene(BuilderUtils.ScenePath(scene), OpenSceneMode.Single);
            var coal = Object.FindAnyObjectByType<CoalStation>(FindObjectsInactive.Include);
            var ash = Object.FindAnyObjectByType<AshStation>(FindObjectsInactive.Include);
            Assert.IsNotNull(coal);
            Assert.IsNotNull(ash);
            int doors = 0;
            foreach (var target in Object.FindObjectsByType<TaskTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (target.task == ash && target.name == "AshDoor") doors++;
            Assert.AreEqual(coal.cells.Length, doors, scene + ": STOVE: nối cửa tro cho cả hai lò, từng ô");
        }

        [Test]
        public void APartlyBurnedCellDoesNotRequestAShovelThatCannotFit()
        {
            var coal = Station();
            coal.Fire.Set(2f, 0.65f);
            coal.OnOrder(2);
            Assert.AreEqual(0, coal.PendingPours, "STOVE: còn đủ nóng, chưa chứa được một xẻng thì không tạo việc nạp");
            coal.CellState(0).Set(1f, 0.3f);
            Assert.AreEqual(1, coal.PendingPours);
            coal.SimulateWork();
            Assert.AreEqual(5.5f, coal.CellState(0).Fuel, 0.0001f);
            Assert.AreEqual(0, coal.PendingPours);
        }

        [TestCase("02_Galley")]
        [TestCase("03_Marseille")]
        public void ShovelLoadAndBasketHaveExplicitSupportAnchors(string scene)
        {
            EditorSceneManager.OpenScene(BuilderUtils.ScenePath(scene), OpenSceneMode.Single);
            var coal = Object.FindAnyObjectByType<CoalStation>(FindObjectsInactive.Include);
            Assert.AreEqual(coal.baskets.Length, coal.basketHomes.Length);
            for (int i = 0; i < coal.baskets.Length; i++)
            {
                Assert.IsNotNull(coal.basketHomes[i]);
                Assert.Less(Vector3.Distance(coal.baskets[i].transform.position, coal.basketHomes[i].position), 0.001f);
            }
            var hands = Object.FindAnyObjectByType<HaiBanTay.Player.HandsRig>(FindObjectsInactive.Include);
            var shovel = hands.shovelProp.transform;
            Assert.IsNotNull(shovel.Find("GripL"));
            Assert.IsNotNull(shovel.Find("GripR"));
            Assert.AreEqual(shovel.Find("LoadSupport"), hands.shovelLoad.transform.parent);
            foreach (var lump in hands.shovelLoad.GetComponentsInChildren<MeshFilter>(true))
            {
                var bounds = HaiBanTay.Editor.Import.ArtImporter.TransformBounds(lump.sharedMesh.bounds,
                    shovel.worldToLocalMatrix * lump.transform.localToWorldMatrix);
                Assert.Less(Mathf.Abs(bounds.max.z), 0.012f, "STOVE: than chạm mặt lưỡi, không nổi cao hay lọt dưới");
                Assert.Less(Mathf.Abs(bounds.center.x), 0.12f);
                Assert.That(bounds.center.y, Is.InRange(0.37f, 0.65f));
            }
        }
    }
}
