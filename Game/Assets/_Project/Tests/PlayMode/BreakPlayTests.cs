using System.Collections;
using System.Linq;
using HaiBanTay.Core;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// W1 thử phá (PRODUCTION_PLAN mục 4 S6): tạm dừng, mở sổ giữa thao tác rồi chơi tiếp. Thao tác đang giữ đứng yên trong lúc
    /// màn phủ lên (QA-0027), không khóa camera hay di chuyển sót, và việc vẫn làm xong. Đánh rơi đồ giữa đường, làm sai thứ tự:
    /// không mất công, hướng dẫn chỉ đúng chỗ, làm tiếp được.
    /// </summary>
    public class BreakPlayTests
    {
        static PlayerRig Rig => PlayerRig.Current;
        static GameRoot Root => GameRoot.Instance;

        [TearDown]
        public void TearDown()
        {
            if (Root != null)
            {
                Root.Input.SimUse = false;
                Root.Input.SimLook = Vector2.zero;
                Object.FindAnyObjectByType<PauseController>()?.Resume();
                if (Root.UI.Notebook.IsOpen) Root.UI.Notebook.Close();
            }
            PlayTestUtil.ExitSim();
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator PausingWithALoadedShovelKeepsTheCoal()
        {
            yield return ChoreTestUtil.Galley();
            var coal = Object.FindAnyObjectByType<ShiftDirector>().coal;
            var input = Root.Input;
            Assert.IsTrue(Rig.interactor.TakeTool(Tool.Shovel));
            coal.Bin.Set(5);
            ChoreTestUtil.Look(new Vector3(0.65f, 0f, 0.95f), coal.bin.GetComponentInChildren<Collider>().bounds.center);
            yield return null;
            input.SimUse = true;
            for (float t = 0f; t < 5f && !coal.Shovel.Loaded; t += Time.deltaTime) yield return null;
            Assert.IsTrue(coal.Shovel.Loaded, "xúc được một xẻng");
            var pause = Object.FindAnyObjectByType<PauseController>();
            pause.Pause();
            yield return Frames(20);
            Assert.IsTrue(coal.Shovel.Loaded, "QA-0027: tạm dừng khi xẻng đầy không làm đổ than");
            pause.Resume();
            yield return Frames(5);
            Assert.IsTrue(coal.Shovel.Loaded, "chơi tiếp, vẫn giữ chuột: xẻng vẫn đầy");
            // Mở sổ giữa chừng cũng vậy.
            Root.UI.Notebook.Toggle();
            yield return Frames(10);
            Assert.IsTrue(Root.UI.Notebook.IsOpen);
            Assert.IsTrue(coal.Shovel.Loaded, "mở sổ khi xẻng đầy không làm đổ than");
            Root.UI.Notebook.Close();
            yield return Frames(5);
            Assert.IsTrue(coal.Shovel.Loaded);
            Assert.IsFalse(Rig.motor.MovementLocked, "không khóa di chuyển sót sau khi đóng màn phủ");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator PausingMidPeelKeepsTheStrokeAndTheCameraFree()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            PeelStation task = null;
            foreach (var s in d.storyStations) if (s is PeelStation p && p.Kind == OrderKinds.Peel) task = p;
            Assert.IsNotNull(task);
            var input = Root.Input;
            task.OnOrder(1);
            ChoreTestUtil.Look(task.stand, task.board.transform.position);
            yield return new WaitForSeconds(0.3f);
            task.OnInteract(task.board, Rig.interactor);
            yield return PlayTestUtil.WaitUntil(() => task.Ready, 60f);
            ChoreTestUtil.Look(task.stand, task.board.transform.position);
            input.SimUse = true;
            var logic = task.potatoes.Logic;
            for (float t = 0f; t < 20f && !(logic.Band == 1 && logic.Stroke > 0.3f); t += Time.deltaTime)
            {
                input.SimLook = new Vector2(0f, Time.frameCount % 16 < 8 ? 9f : -9f);
                yield return null;
            }
            input.SimLook = Vector2.zero;
            Assert.AreEqual(1, logic.Band, "đang ở nhát thứ hai");
            float stroke = logic.Stroke;
            var pause = Object.FindAnyObjectByType<PauseController>();
            pause.Pause();
            yield return Frames(20);
            Assert.AreEqual(1, logic.Band, "QA-0027: tạm dừng không bỏ nhát đang kéo");
            Assert.GreaterOrEqual(logic.Stroke, stroke - 1e-4f);
            pause.Resume();
            yield return Frames(3);
            Assert.IsTrue(task.potatoes.Engaged, "chơi tiếp vẫn cầm củ và dao");
            for (float t = 0f; t < 40f && task.Pending > 0; t += Time.deltaTime)
            {
                input.SimLook = new Vector2(0f, Time.frameCount % 16 < 8 ? 9f : -9f);
                yield return null;
            }
            input.SimLook = Vector2.zero;
            input.SimUse = false;
            Assert.AreEqual(0, task.Pending, "gọt xong củ sau khi tạm dừng");
            // Củ cuối còn được thả vào nồi nước rồi tay mới buông dao (vài trăm ms); batchmode chạy nhanh nên đợi theo giây.
            yield return PlayTestUtil.WaitUntil(() => !task.Working, 5f);
            Assert.IsFalse(task.Working, "thả củ cuối xong thì thôi tư thế gọt");
            yield return Frames(3);
            Assert.IsFalse(Rig.look.Locked, "hết việc thì camera không bị khóa sót");
            Assert.IsFalse(Rig.motor.MovementLocked, "hết việc thì đi lại được");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator DroppingAFullAshBucketMidRouteKeepsTheAshAndTheGuide()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            var ash = Object.FindAnyObjectByType<AshStation>();
            var who = Rig.interactor;
            var guide = Root.UI.Guide;
            d.Board.Clear();
            d.Board.Add(OrderKinds.Ash, 1);
            // Đã đặt xô trước cửa tro và cào đầy; xách lên đi đổ.
            ash.bucket.Detach(ash.spot.transform.position, ash.spot.transform.rotation, physics: false);
            ash.Rake.ForceFull();
            Assert.IsTrue(ash.bucket.CanInteract(who));
            ash.bucket.Interact(who);
            yield return null;
            Assert.AreEqual(ash.bucket, who.Holding);
            // Giữa bếp thì đánh rơi (thả tay, xô rơi theo vật lý).
            ChoreTestUtil.Look(new Vector3(0.6f, 0f, -1.2f), new Vector3(-0.6f, 0.4f, -1.2f));
            yield return Frames(3);
            who.DropInFront();
            yield return new WaitForSeconds(2f);
            Assert.IsNull(who.Holding);
            Assert.AreEqual(ash.Rake.StrokesPerBucket, ash.Rake.InBucket, "xô rơi không mất tro (tro chỉ đổ ở máng)");
            Assert.AreEqual(ash.bucket.transform, guide.Target, "hướng dẫn chỉ lại cái xô vừa rơi");
            Assert.IsTrue(ash.bucket.GetComponentsInChildren<Collider>().Any(c => c.enabled), "xô rơi nhắm được để nhặt lại");
            Assert.Less(Mathf.Abs(ash.bucket.transform.position.y), 0.15f, "xô nằm trên sàn, không lơ lửng hay lọt sàn");
            var at = ash.bucket.GetComponentsInChildren<Collider>().First(c => c.enabled).bounds.center;
            ChoreTestUtil.Look(new Vector3(at.x + 0.8f, 0f, at.z), at);
            yield return PlayTestUtil.WaitUntil(() => ReferenceEquals(who.Current, ash.bucket), 3f);
            Root.Input.SimulateInteract();
            yield return PlayTestUtil.WaitUntil(() => who.Holding == ash.bucket, 3f);
            Assert.AreEqual(ash.bucket, who.Holding, "nhặt lại được");
            yield return null;
            Assert.AreEqual(ash.chute.transform, guide.Target, "bước sau: đổ ở máng tro");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator RakingBeforeTheBucketIsPlacedLosesNoAshAndSaysWhy()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            var ash = Object.FindAnyObjectByType<AshStation>();
            var who = Rig.interactor;
            var input = Root.Input;
            d.Board.Clear();
            d.Board.Add(OrderKinds.Ash, 1);
            d.coal.Fire.Set(FireLogic.MaxFuel, 1f, 0.6f);
            Assert.IsTrue(ash.BucketStored, "xô còn cất trong hầm than");
            // Sai thứ tự: lấy cào, cào cửa tro khi chưa đặt xô.
            ash.hook.Interact(who);
            Assert.AreEqual(Tool.Rake, who.Tool);
            ChoreTestUtil.Look(new Vector3(-3.05f, 0f, 1.1f), ash.door.transform.position);
            yield return PlayTestUtil.WaitUntil(() => who.IsLookingAt(ash.door), 3f);
            Assert.AreEqual("prompt.need_bucket", ash.PromptFor(ash.door, who), "nói rõ phải đặt xô trước");
            float before = d.coal.Fire.Ash;
            input.SimUse = true;
            for (float t = 0f; t < 1.5f; t += Time.deltaTime)
            {
                input.SimLook = new Vector2(0f, -12f);
                yield return null;
            }
            input.SimUse = false;
            input.SimLook = Vector2.zero;
            yield return Frames(3);
            Assert.AreEqual(0, ash.Rake.InBucket, "chưa có xô thì không có nhát cào nào");
            // Lửa vẫn cháy nên tro chỉ có thể tăng; cào không xô thì không được gạt mất tro nào.
            Assert.GreaterOrEqual(d.coal.Fire.Ash, before - 1e-4f, "tro không biến mất khi cào không xô");
            Assert.IsFalse(Rig.look.Locked, "cào hụt không khóa camera");
            Assert.IsTrue(d.GuideFor(who, out var target, out var key));
            Assert.AreEqual(ash.bucket.transform, target, "hướng dẫn chỉ đi lấy xô");
        }
    }
}
