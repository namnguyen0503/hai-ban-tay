using System.Collections;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Lỗi kẹt tiến trình trong cảnh thật (Docs/QA/bugs.md, PRODUCTION_PLAN W0): thao tác bị ngắt giữa chừng phải trả lại
    /// quyền điều khiển; NPC không tới được chỗ thì lượt phục vụ vẫn xong.
    /// </summary>
    public class ProgressRegressionPlayTests
    {
        [TearDown]
        public void TearDown() => PlayTestUtil.ExitSim();

        [UnityTest]
        public IEnumerator LoadedShovelIsGuidedToAMouthEvenWhenTheBinRanOut()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            var coal = d.coal;
            coal.Fire.Set(0f, 0f);
            var who = PlayerRig.Current.interactor;
            var input = GameRoot.Instance.Input;
            Assert.IsTrue(who.TakeTool(Tool.Shovel));
            d.Board.Clear();
            d.Board.Add(OrderKinds.Fire, 2);
            coal.OnOrder(2);
            coal.Bin.Set(1);
            // Xúc nhát cuối trong thùng: thùng hết than, xẻng đầy, vẫn giữ chuột như người chơi đang bưng xẻng ra lò.
            ChoreTestUtil.Look(new Vector3(0.65f, 0f, 0.95f), coal.bin.GetComponentInChildren<Collider>().bounds.center);
            yield return null;
            input.SimUse = true;
            try
            {
                for (float t = 0f; t < 5f && !coal.Shovel.Loaded; t += Time.deltaTime)
                    yield return null;
                Assert.IsTrue(coal.Shovel.Loaded, "xúc được nhát cuối");
                Assert.IsTrue(coal.Bin.Empty);
                Assert.IsTrue(coal.Guide(who, out var target, out var key));
                Assert.AreEqual("guide.pour_coal", key, "QA-0026: xẻng đầy thì chỉ đổ vào lò trước, chưa đi lấy sọt than");
                Assert.IsTrue(System.Array.Exists(coal.cells, c => c.mouth != null && c.mouth.transform == target), "chỉ tới một miệng lò");
            }
            finally { input.SimUse = false; }
        }

        [UnityTest]
        public IEnumerator GuideLeavesTheBucketTheFrameItIsPicked()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            var ash = Object.FindAnyObjectByType<AshStation>();
            d.coal.CellState(0).Set(0f, 0f, 0.6f);
            var who = PlayerRig.Current.interactor;
            var guide = GameRoot.Instance.UI.Guide;
            d.Board.Clear();
            d.Board.Add(OrderKinds.Ash, 1);
            yield return PlayTestUtil.WaitUntil(() => guide.Target == ash.bucket.transform, 2f);
            Assert.AreEqual(ash.bucket.transform, guide.Target, "lệnh cào tro: hướng dẫn chỉ tới xô cất trong hầm than");
            // Nhặt ngay sau một nhịp làm mới hướng dẫn (0,2 s): khung sau phải chỉ chỗ đặt xô, không còn chỉ cái xô đang cầm.
            Assert.IsTrue(ash.bucket.CanInteract(who));
            ash.bucket.Interact(who);
            yield return null;
            Assert.AreEqual(ash.bucket, who.Holding);
            Assert.AreNotEqual(ash.bucket.transform, guide.Target, "QA-0070: nhặt xô xong, hướng dẫn đổi ngay sang bước sau");
            Assert.AreEqual(ash.spot.transform, guide.Target, "bước sau: đặt xô trước cửa tro");
        }

        [UnityTest]
        public IEnumerator ClosingTheSackChoreLeavesAnotherChoresViewAlone()
        {
            yield return ChoreTestUtil.Galley();
            var sack = Object.FindAnyObjectByType<SackStation>();
            var rig = PlayerRig.Current;
            Assert.AreEqual(TaskState.Active, sack.State);
            // Việc khác đang cúi người (rửa nồi, gọt…): đầu hạ, camera khóa; việc bao hết ca mà chưa dốc bao nào.
            rig.motor.ExtraHeadHeight = -0.3f;
            rig.look.Locked = true;
            try
            {
                sack.Close();
                Assert.AreEqual(-0.3f, rig.motor.ExtraHeadHeight, 1e-4f, "QA-0008: hết việc bao (không dốc bao) không ghi đè chiều cao đầu");
                Assert.IsTrue(rig.look.Locked, "không mở khóa camera của việc khác");
            }
            finally
            {
                rig.look.Locked = false;
                rig.motor.ExtraHeadHeight = 0f;
            }
        }

        [UnityTest]
        public IEnumerator InterruptedMoveToGivesControlBack()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            var rig = PlayerRig.Current;
            var cc = rig.motor.GetComponent<CharacterController>();
            var to = rig.Position + rig.transform.forward * 0.5f;
            var move = d.StartCoroutine(rig.MoveTo(to, rig.look.Yaw, 0f, 2f));
            yield return new WaitForSeconds(0.3f);
            d.StopCoroutine(move);
            for (int i = 0; i < 4; i++)
                yield return null;
            Assert.IsTrue(cc.enabled, "QA-0003: MoveTo bị dừng giữa chừng không được để người chơi mất CharacterController");
        }

        [UnityTest]
        public IEnumerator ServeFinishesWhenThePorterCannotWalk()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            var meals = d.meals;
            var porter = meals.helper != null ? meals.helper.agent : meals.porter;
            Assert.IsNotNull(porter);
            porter.walkSpeed = 0f;
            porter.carrySpeed = 0f;
            meals.BeginMeal(new PhaseData { Id = "qa_meal", Dirty = 1 });
            bool done = false;
            d.StartCoroutine(Serve(meals, () => done = true));
            yield return PlayTestUtil.WaitUntil(() => done, 60f);
            Assert.IsTrue(done, "QA-0004: lượt phục vụ không được chờ mãi một NPC không tới được chỗ nồi");
            Assert.AreEqual(1, meals.Pending, "nồi vẫn về chồng nồi bẩn (đặt ngoài tầm nhìn)");
        }

        static IEnumerator Serve(MealPots meals, System.Action finished)
        {
            yield return meals.Serve();
            finished();
        }
    }
}
