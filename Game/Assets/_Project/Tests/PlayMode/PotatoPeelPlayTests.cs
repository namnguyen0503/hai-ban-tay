using System.Collections;
using System.Linq;
using HaiBanTay.Core;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Gọt khoai như thật (yêu cầu 14/10): ông Mai gọt mẫu một củ, rồi người chơi gọt hết xô bằng đầu vào thật (giữ chuột,
    /// rê dọc). Mỗi nhát một dải vỏ rơi xuống chậu, củ trắng vào nồi nước; nhả tay thì củ gọt dở nằm trên thớt, cầm lại gọt tiếp.
    /// </summary>
    public class PotatoPeelPlayTests
    {
        static GameRoot Root => GameRoot.Instance;
        static PlayerRig Rig => PlayerRig.Current;

        [TearDown]
        public void TearDown()
        {
            if (Root != null) { Root.Input.SimUse = false; Root.Input.SimLook = Vector2.zero; }
            PlayTestUtil.ExitSim();
        }

        static IEnumerator Lesson(PeelStation task, int count)
        {
            task.OnOrder(count);
            Assert.AreEqual(count + 1, task.potatoes.InBucket, "xô đủ khoai cho lượt, thêm một củ ông Mai gọt mẫu");
            if (count >= 3)
            {
                // S16: "đống củ cải và khoai tây" — xô có cả hai thứ.
                Assert.Greater(task.potatoes.TurnipsInBucket, 0, "trong xô có củ cải");
                Assert.Greater(task.potatoes.InBucket - task.potatoes.TurnipsInBucket, task.potatoes.TurnipsInBucket, "khoai vẫn nhiều hơn");
            }
            ChoreTestUtil.Look(task.stand, task.board.transform.position);
            yield return new WaitForSeconds(0.3f);
            task.OnInteract(task.board, Rig.interactor);
            yield return PlayTestUtil.WaitUntil(() => task.ShowingDemo, 40f);
            Assert.IsTrue(task.potatoes.Demonstrating, "ông Mai cầm củ và dao trong tay mình");
            var demo = task.potatoes.DemoPotato;
            var eye = Rig.cam.transform;
            Assert.Less(Vector3.Distance(eye.position, demo.position), 1.25f, "ông Mai gọt mẫu sát mép bàn, tay ông cách mắt Ba chừng một mét");
            // 17/10: game không tự xoay camera (bỏ cái liếc tự động); người chơi tự nhìn, camera không khóa.
            var forward = eye.forward;
            yield return new WaitForSeconds(0.6f);
            Assert.Less(Vector3.Angle(forward, eye.forward), 0.5f, "mắt Ba không bị kéo đi khi ông bắt đầu gọt");
            Assert.IsFalse(Rig.look.Locked, "xem ông gọt mẫu không khóa camera");
            yield return PlayTestUtil.WaitUntil(() => task.Ready, 40f);
            Assert.IsTrue(task.Ready);
            Assert.AreEqual(1, task.potatoes.InPot, "củ ông Mai gọt mẫu đã nằm trong nồi nước");
            Assert.AreEqual(count, task.potatoes.InBucket);
            yield return PlayTestUtil.WaitUntil(() => task.potatoes.StripsOnDemoPlate >= PotatoPeelLogic.Bands, 3f);
            Assert.AreEqual(PotatoPeelLogic.Bands, task.potatoes.StripsOnDemoPlate, "mỗi nhát của ông một dải vỏ rơi xuống đĩa trước mặt ông");
            Assert.AreEqual(0, task.potatoes.StripsInBowl, "vỏ của ông không rơi vào chậu của Ba");
        }

        static IEnumerator Drag(PeelStation task, System.Func<bool> until, float seconds)
        {
            Root.Input.SimUse = true;
            for (float t = 0f; t < seconds && !until(); t += Time.deltaTime)
            {
                Root.Input.SimLook = new Vector2(0f, Time.frameCount % 16 < 8 ? 12f : -12f);
                if (!task.Working) ChoreTestUtil.Look(task.stand, task.board.transform.position);
                yield return null;
            }
            Root.Input.SimLook = Vector2.zero;
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator PeelTheWholeBucketThroughPlayerInput()
        {
            yield return ChoreTestUtil.Galley();
            var task = Object.FindAnyObjectByType<ShiftDirector>().storyStations.OfType<PeelStation>().First(x => !x.asparagus);
            yield return Lesson(task, 3);
            bool turned = false, dropped = false;
            int strips = task.potatoes.StripsInBowl;
            yield return Drag(task, () =>
            {
                turned |= task.potatoes.Logic.Band > 0;
                dropped |= task.potatoes.StripsInBowl > strips;
                return task.Pending == 0;
            }, 150f);
            Root.Input.SimUse = false;
            Assert.AreEqual(0, task.Pending, "gọt hết lượt bằng đầu vào thật");
            Assert.IsTrue(turned, "xoay củ sang dải kế sau mỗi nhát");
            yield return PlayTestUtil.WaitUntil(() => !task.Working, 5f);
            Assert.AreEqual(0, task.potatoes.InBucket, "hết xô");
            Assert.AreEqual(4, task.potatoes.InPot, "mọi củ trắng nằm trong nồi nước");
            yield return PlayTestUtil.WaitUntil(() => task.potatoes.StripsInBowl >= 3 * PotatoPeelLogic.Bands, 3f);
            Assert.IsTrue(dropped);
            Assert.GreaterOrEqual(task.potatoes.StripsInBowl, 3 * PotatoPeelLogic.Bands, "mỗi nhát một dải vỏ rơi xuống chậu");
            Assert.IsFalse(Rig.look.Locked, "xong việc thì trả camera");
            Assert.IsFalse(Rig.hands.Working, "xong việc thì tay buông dao");
            Assert.IsTrue(task.board.gameObject.activeInHierarchy, "nồi khoai và chậu vỏ còn trên bàn sau khi gọt xong");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator ReleasingPutsTheHalfPeeledPotatoDownAndResumes()
        {
            yield return ChoreTestUtil.Galley();
            var task = Object.FindAnyObjectByType<ShiftDirector>().storyStations.OfType<PeelStation>().First(x => !x.asparagus);
            yield return Lesson(task, 1);
            yield return Drag(task, () => task.potatoes.Logic.Band >= 2 && task.potatoes.Logic.Stroke > 0.3f && !task.potatoes.Busy, 30f);
            Root.Input.SimUse = false;
            float progress = task.potatoes.Logic.Progress;
            Assert.Greater(progress, 0.3f);
            yield return PlayTestUtil.WaitUntil(() => !task.Working, 5f);
            Assert.IsFalse(task.potatoes.Engaged, "nhả tay lâu thì đặt củ xuống");
            Assert.IsTrue(task.potatoes.HasPotato, "củ gọt dở vẫn nằm trên thớt");
            Assert.Less(Vector3.Distance(task.potatoes.Current.position, task.potatoes.rest.position), 0.01f);
            Assert.IsFalse(Rig.look.Locked);
            Assert.IsFalse(Rig.hands.Working);
            Assert.AreEqual(progress, task.potatoes.Logic.Progress, 1e-4f, "đặt xuống không mất phần đã gọt");
            Assert.AreEqual(0, task.potatoes.InBucket, "không lấy thêm củ mới khi củ cũ chưa xong");
            yield return Drag(task, () => task.potatoes.Held, 10f);
            Assert.AreEqual(progress, task.potatoes.Logic.Progress, 1e-4f, "cầm lại thì gọt tiếp từ chỗ dừng");
            yield return Drag(task, () => task.Pending == 0, 60f);
            Root.Input.SimUse = false;
            Assert.AreEqual(0, task.Pending);
            yield return PlayTestUtil.WaitUntil(() => !task.Working, 5f);
            Assert.AreEqual(2, task.potatoes.InPot);
        }

        /// <summary>QA-0012: dạy gọt xong, sang lượt rửa ông Mai ghé nói chuyện thì đi tiếp từ chỗ đang đứng trong bếp, không biến
        /// mất rồi hiện lại ở cửa hành lang.</summary>
        [UnityTest, Timeout(240000)]
        public IEnumerator MaiWalksOnFromThePeelingSpotWithoutReappearingAtTheDoor()
        {
            yield return ChoreTestUtil.Galley();
            var director = Object.FindAnyObjectByType<ShiftDirector>();
            var task = director.storyStations.OfType<PeelStation>().First(x => !x.asparagus);
            yield return Lesson(task, 1);
            var cast = task.cast;
            var mai = cast.mai;
            Assert.IsTrue(mai.gameObject.activeSelf, "ông Mai còn đứng cạnh bàn sau buổi dạy");
            task.SimulateWork();
            Assert.AreEqual(0, task.Pending);
            cast.PhaseStarted(director.Day.Phases.First(x => x.Id == "dishes"));
            var door = cast.corridorToGalley[0];
            var last = mai.Position;
            float maxStep = 0f, nearestDoor = float.MaxValue;
            for (float t = 0f; t < 25f; t += Time.deltaTime)
            {
                yield return null;
                Assert.IsTrue(mai.gameObject.activeSelf, "ông Mai không biến mất giữa chừng");
                maxStep = Mathf.Max(maxStep, Vector3.Distance(mai.Position, last));
                nearestDoor = Mathf.Min(nearestDoor, Vector3.Distance(mai.Position, door));
                last = mai.Position;
                if (!mai.Walking && Vector3.Distance(mai.Position, cast.maiSpot.position) < 0.3f)
                    break;
            }
            Assert.Less(maxStep, 0.5f, "không nhảy chỗ (không hiện lại ở cửa)");
            Assert.Greater(nearestDoor, 1.5f, "đi thẳng từ chỗ đứng, không vòng ra cửa hành lang");
            Assert.Less(Vector3.Distance(mai.Position, cast.maiSpot.position), 0.3f, "tới chỗ nói chuyện");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator TappingDoesNotMakeThePotatoJumpUpAndDown()
        {
            yield return ChoreTestUtil.Galley();
            var task = Object.FindAnyObjectByType<ShiftDirector>().storyStations.OfType<PeelStation>().First(x => !x.asparagus);
            yield return Lesson(task, 1);
            yield return Drag(task, () => task.potatoes.Held && !task.potatoes.Busy, 10f);
            // Nhả rồi bấm lại trong chốc lát (dưới releaseDelay): tay vẫn cầm củ, không đặt xuống rồi nhấc lên.
            for (int i = 0; i < 6; i++)
            {
                Root.Input.SimUse = false;
                yield return new WaitForSeconds(task.releaseDelay * 0.4f);
                Assert.IsTrue(task.potatoes.Held, "nhả chốc lát thì tay vẫn cầm củ");
                Root.Input.SimUse = true;
                yield return new WaitForSeconds(0.1f);
            }
            Assert.IsTrue(Rig.look.Locked);
        }
    }
}
