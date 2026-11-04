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
    public class ChoreFlowTests
    {
        [TearDown] public void CleanUp() => PlayTestUtil.ExitSim();

        [UnityTest, Timeout(300000)]
        public IEnumerator TheSameCookedPotsMustBePlacedScrubbedRinsedAndRacked()
        {
            yield return ChoreTestUtil.Galley();
            var meals = Object.FindAnyObjectByType<MealPots>();
            var wash = meals.wash;
            var rig = PlayerRig.Current;
            Assert.AreEqual(0, meals.Pending);
            Assert.AreEqual(0, wash.Logic.Pending);
            var first = meals.pots[0];
            meals.BeginMeal(new PhaseData { Id = "kiểm_nấu", Dirty = 2 });
            Assert.AreEqual(2, meals.Cooking);
            Assert.AreEqual(0, wash.Logic.Pending, "Đang nấu chưa có nồi bẩn.");
            ChoreTestUtil.Look(new Vector3(1f, 0f, -1f), first.transform.position);
            yield return meals.Serve();
            Assert.AreEqual(2, meals.Pending);
            Assert.AreEqual(2, wash.Logic.Pending);
            wash.OnInteract(wash.pile, rig.interactor);
            Assert.AreSame(first, wash.basinPot, "Giữ danh tính nồi qua cả bữa và lượt rửa.");
            Assert.IsTrue(wash.CarryingPot);
            Assert.IsFalse(wash.CanInteract(wash.rack, rig.interactor));
            wash.OnInteract(wash.basin, rig.interactor); wash.OpenTap(); // nước chảy rồi mới cọ (QA-0132)
            Assert.IsFalse(wash.CarryingPot);
            ChoreTestUtil.Look(new Vector3(5.85f, 0f, -1.6f), wash.basin.transform.position + Vector3.up * 0.68f);
            yield return null;
            Assert.AreEqual(TaskState.Active, wash.State);
            Assert.IsTrue(rig.interactor.HandsFree);
            wash.brushHook.Interact(rig.interactor); // cọ bằng bàn chải lấy từ giá cạnh chậu (brush17)
            yield return ChoreTestUtil.Scrub(wash.Logic.Scrub, () => wash.Scrubbing, 150f); // phủ kín mặt cọ mất ~55 s, sát mức 60 s cũ
            Assert.IsFalse(wash.Rinsed);
            Assert.IsFalse(wash.CanInteract(wash.rack, rig.interactor), "Cọ xong phải tráng mới úp.");
            GameRoot.Instance.Input.SimUse = true;
            yield return PlayTestUtil.WaitUntil(() => wash.Rinsed, 5f);
            GameRoot.Instance.Input.SimUse = false;
            Assert.IsTrue(wash.Rinsed);
            wash.OnInteract(wash.rack, rig.interactor);
            Assert.AreEqual(1, wash.Logic.Washed);
            Assert.AreEqual(1, meals.Pending);
            Assert.Greater(Vector3.Distance(first.transform.position, wash.basin.transform.position), 0.3f);
        }

        [UnityTest]
        public IEnumerator GreensAreCreditedOnlyAfterSortingWashingAndDelivery()
        {
            yield return ChoreTestUtil.Galley();
            var greens = Object.FindAnyObjectByType<GreensStation>();
            var rig = PlayerRig.Current;
            int units = 0;
            greens.Worked += (_, n) => units += n;
            greens.OnOrder(1);
            greens.OnInteract(greens.board, rig.interactor);
            Assert.IsFalse(greens.Logic.HasBunch, "Chưa đưa rổ A tới bàn thì chưa lấy bó được.");
            greens.OnInteract(greens.crate, rig.interactor);
            Assert.IsTrue(greens.CarryingRaw);
            greens.OnInteract(greens.board, rig.interactor);
            greens.OnInteract(greens.board, rig.interactor);
            ChoreTestUtil.Look(new Vector3(-0.5f, 0f, -0.85f), greens.board.transform.position + Vector3.up * 0.05f);
            yield return null;
            while (greens.Logic.HasBunch)
            {
                int before = greens.Logic.Index;
                GameRoot.Instance.Input.SimUse = true;
                GameRoot.Instance.Input.SimLook = new Vector2(greens.Logic.CurrentWilted ? -120f : 120f, 0f);
                yield return null;
                GameRoot.Instance.Input.SimUse = false; GameRoot.Instance.Input.SimLook = Vector2.zero;
                yield return null;
                Assert.Greater(greens.Logic.Index, before);
            }
            Assert.IsTrue(greens.ReadyToWash);
            Assert.AreEqual(0, units, "Nhặt xong chưa phải rau đã rửa.");
            greens.OnInteract(greens.cleanTarget, rig.interactor);
            greens.OnInteract(greens.board, rig.interactor);
            Assert.IsTrue(greens.CarryingClean, "Không nhận rau chưa rửa cho đầu bếp.");
            greens.OnInteract(greens.sink, rig.interactor);
            ChoreTestUtil.Look(new Vector3(5.85f, 0f, -1.6f), greens.sink.transform.position);
            yield return null;
            GameRoot.Instance.Input.SimUse = true;
            yield return PlayTestUtil.WaitUntil(() => greens.Washed, 5f);
            GameRoot.Instance.Input.SimUse = false;
            Assert.IsTrue(greens.Washed);
            Assert.AreEqual(0, units);
            greens.OnInteract(greens.sink, rig.interactor);
            greens.OnInteract(greens.board, rig.interactor);
            Assert.AreEqual(1, units);
            Assert.IsFalse(greens.CarryingClean);
        }

        [UnityTest]
        public IEnumerator EveryLessonSitsOnTheChairAndStandsAfterwards()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            var rig = PlayerRig.Current;
            var sit = rig.GetComponent<SitController>();
            foreach (int day in new[] { 2, 3, 5, 6 })
            {
                var original = rig.Position;
                var lesson = new GameObject("Kiểm_học_" + day).AddComponent<StudyTask>();
                lesson.chair = d.study.chair; lesson.studyLookAt = d.study.studyLookAt; lesson.chairClip = d.study.chairClip;
                var plan = StudyPlanner.Make(GameRoot.Instance.Content, new GameState(), GameRoot.Instance.Content.GetDay(day).Evening, 22f * 60f);
                lesson.Begin(plan); lesson.Activate();
                yield return new WaitForSeconds(1f);
                Assert.IsTrue(sit.Seated && sit.StudySeated, plan.Kind);
                Assert.IsTrue(BodyState.Current.Logic.Seated, plan.Kind);
                // Gốc ngồi cách tâm ghế SeatForward về phía trước và SeatLift lên để bắp chân không xuyên ván ghế (SitController).
                var seatGap = rig.Position - lesson.chair.position;
                Assert.AreEqual(SitController.SeatForward, new Vector2(seatGap.x, seatGap.z).magnitude, 0.03f, "chỗ ngồi theo tâm ghế");
                Assert.AreEqual(SitController.SeatLift, seatGap.y, 0.1f, "độ cao chỗ ngồi theo mặt ghế");
                Assert.AreEqual(1.12f + SitController.SeatLift + 0.05f, rig.cam.transform.position.y - lesson.chair.position.y, 0.09f);
                Assert.IsFalse(rig.GetComponent<CharacterController>().enabled);
                var body = rig.GetComponentInChildren<PlayerBody>();
                Assert.IsInstanceOf<AnimatorOverrideController>(body.animator.runtimeAnimatorController);
                lesson.ForceComplete();
                yield return new WaitForSeconds(0.3f);
                Assert.IsFalse(sit.Seated);
                Assert.IsTrue(rig.GetComponent<CharacterController>().enabled);
                Assert.Less(Vector3.Distance(rig.Position, original), 0.1f);
                Object.Destroy(lesson.gameObject);
            }
        }
    }
}
