using System.Collections;
using System.Linq;
using HaiBanTay.Core;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>Ngày 1 (yêu cầu 14/10): bữa nhân viên, Ba ngồi ăn đối diện ông Mai và hai người nói chuyện (S16: "mới đầu anh
    /// không ăn được bánh mì và bơ. Ăn xúp thì anh dùng nĩa"); ăn xong Ba đứng dậy đi tiếp.</summary>
    public class MealWithMaiPlayTests
    {
        static GameRoot Root => GameRoot.Instance;
        static PlayerRig Rig => PlayerRig.Current;

        [TearDown]
        public void TearDown() => PlayTestUtil.ExitSim();

        [UnityTest, Timeout(240000)]
        public IEnumerator BaSitsDownToEatWithMaiAndStandsUpAfter()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            d.people.crewMeal.SetActive(true);
            var soup = d.storyStations.OfType<VoyageEncounter>().First(x => x.Kind == OrderKinds.Soup);
            soup.OnOrder(1);
            ChoreTestUtil.Look(soup.stand, soup.target.transform.position);
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(soup.CanInteract(soup.target, Rig.interactor));
            Assert.AreEqual("prompt.soup_sit", soup.PromptFor(soup.target, Rig.interactor), "lần bấm đầu là ngồi xuống ăn");
            soup.OnInteract(soup.target, Rig.interactor);
            yield return new WaitForSeconds(1.2f);
            var sit = Rig.GetComponent<SitController>();
            Assert.IsTrue(sit.StudySeated, "ngồi xuống ghế đối diện ông Mai");
            Assert.Less(Vector3.Distance(Rig.Position, soup.seat.position), SitController.SeatForward + SitController.SeatLift + 0.15f, "ngồi ngay tại ghế (SitController.SeatForward)");
            var eye = Rig.cam.transform.position;
            var mai = soup.speaker.Position + Vector3.up;
            Assert.Less(Vector3.Distance(new Vector3(eye.x, 0f, eye.z), new Vector3(mai.x, 0f, mai.z)), 2f, "ngồi cùng bàn với ông Mai");
            Assert.Less(eye.y - Rig.Position.y, 1.3f, "tầm mắt người ngồi");
            Assert.Less(Vector3.Angle(Rig.cam.transform.forward, mai - eye), 25f, "ngồi xuống thì nhìn về phía ông Mai");
            Assert.AreEqual("mai_meal_1", Root.Subtitles.CurrentLineId, "ông Mai mời ăn");
            Assert.Less(Vector3.Distance(Rig.cam.transform.position, soup.target.transform.position), 0.9f, "bát ở ngay trước chỗ ngồi");
            yield return PlayTestUtil.WaitUntil(() => soup.Stage == 1 && !soup.Busy, 60f);
            Assert.IsTrue(sit.StudySeated, "vẫn ngồi khi chờ cầm thìa");
            Assert.IsTrue(Rig.interactor.enabled, "ngồi vẫn với được bát");
            Assert.IsTrue(soup.CanInteract(soup.target, Rig.interactor));
            // Đang ngồi, người chơi cúi nhìn bát thì bát là vật đang nhắm và hiện lời nhắc cầm thìa (không chỉ gọi thẳng OnInteract).
            var toBowl = soup.target.transform.position - Rig.cam.transform.position;
            Rig.look.SetAngles(Mathf.Atan2(toBowl.x, toBowl.z) * Mathf.Rad2Deg,
                -Mathf.Atan2(toBowl.y, new Vector2(toBowl.x, toBowl.z).magnitude) * Mathf.Rad2Deg);
            yield return null;
            yield return null;
            Assert.AreSame(soup.target, Rig.interactor.Current as TaskTarget, "ngồi nhìn bát thì bát là vật đang nhắm");
            Assert.AreEqual("prompt.soup_spoon", Rig.interactor.Current.GetPromptKey(Rig.interactor));
            soup.OnInteract(soup.target, Rig.interactor);
            yield return PlayTestUtil.WaitUntil(() => soup.Pending == 0 && !soup.Busy, 60f);
            Assert.AreEqual(0, soup.Pending);
            Assert.IsFalse(sit.StudySeated, "ăn xong, nói chuyện xong thì đứng dậy");
            Assert.IsFalse(Rig.motor.MovementLocked, "đứng dậy đi được");
            Assert.IsTrue(Rig.interactor.enabled);
        }
    }
}
