using System.Collections;
using System.Collections.Generic;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Bot ngày 5 (20/10) kẹt ở nồi bẩn thứ hai trong chậu: 170 s rê chuột vẫn dừng ở 0,936 (sạch 84%, cần 90%). Test này cọ từng nồi bữa ăn
    /// (LoosePot_0..2 cao 0,30 và CookingPot_3..7 cao 0,27) bằng chuột phủ kín mặt (ScrubCoverage), ghi thời gian và độ sạch,
    /// để biết nồi nào không cọ hết được.
    /// </summary>
    public class WashEveryPotPlayTests
    {
        const float ScrubSeconds = 90f;

        [TearDown] public void CleanUp() => PlayTestUtil.ExitSim();

        [UnityTest, Timeout(900000)]
        public IEnumerator EveryMealPotCanBeScrubbedCleanInTheBasin()
        {
            yield return ChoreTestUtil.Galley();
            var meals = Object.FindAnyObjectByType<MealPots>();
            var wash = meals.wash;
            var rig = PlayerRig.Current;
            meals.cook.enabled = false;
            var input = GameRoot.Instance.Input;
            var report = new List<string>();
            var failed = new List<string>();
            // Vòng 3 trở đi là nồi cần nấu trước (Serve chờ ReadyToServe mãi): chỉ kiểm ba nồi bê ra bằng tay.
            for (int round = 0; round < 3; round++)
            {
                meals.BeginMeal(new PhaseData { Id = "kiểm_nồi_" + round, Dirty = 1 }, instant: true);
                Debug.Log("WASH_EVERY_POT vòng " + round + ": Serve");
                yield return meals.Serve(instant: true);
                Assert.AreEqual(1, wash.Logic.Pending, "vòng " + round);
                wash.OnInteract(wash.pile, rig.interactor);
                var pot = wash.basinPot;
                wash.OnInteract(wash.basin, rig.interactor); wash.OpenTap(); // nước chảy rồi mới cọ (QA-0132)
                ChoreTestUtil.Look(new Vector3(5.85f, 0f, -1.6f), wash.basin.transform.position + Vector3.up * 0.68f);
                yield return null;
                if (rig.interactor.Tool != Tool.Brush && !wash.BrushOnRim)
                    wash.brushHook.Interact(rig.interactor);
                var (diameter, height) = WashStation.PotSize(pot);
                Debug.Log($"WASH_EVERY_POT vòng {round}: cọ {pot.name} (Ø {diameter:0.000} cao {height:0.000}), cọ có sẵn: {wash.Scrubbing != null}");
                float start = Time.realtimeSinceStartup;
                var mouse = ScrubCoverage.For(() => wash.Scrubbing);
                input.SimUse = true;
                if (round == 0)
                {
                    // QA-0126: con trỏ đang ngoài tầm (vai người chơi dời) không được đứng yên mãi trong lúc giữ chuột.
                    var scrub = wash.Scrubbing;
                    scrub.Motion.Reset(new Vector2(0.6f, 0.6f));
                    Assert.IsFalse(scrub.Reachable(scrub.Motion.Cursor, rig), "điểm thử phải ngoài tầm");
                    // 1,5 s giữ chuột theo thời gian game (90 khung ở batchmode ngắn hơn bước Ba sát chậu, StepToWork tới 0,9 s).
                    for (float held = 0f; held < 1.5f; held += Time.deltaTime) { input.SimLook = new Vector2(1f, 0f); yield return null; }
                    var why = scrub.Solve(scrub.Motion.Cursor, rig);
                    Assert.IsTrue(scrub.Reachable(scrub.Motion.Cursor, rig), "con trỏ vẫn kẹt ngoài tầm sau 1,5 s giữ chuột: " + scrub.Motion.Cursor
                        + $" (tầm thừa {why.ReachExcess * 100f:F1} cm, lún {why.Clash * 100f:F1} cm, cổ tay {why.WristBend:F0}°)");
                }
                float cleaned = 0f;
                while (!wash.Logic.Scrub.Done && Time.realtimeSinceStartup - start < ScrubSeconds)
                {
                    input.SimLook = mouse();
                    if (wash.Scrubbing != null) cleaned = wash.Scrubbing.Mask.Cleaned;
                    yield return null;
                }
                input.SimUse = false;
                input.SimLook = Vector2.zero;
                float took = Time.realtimeSinceStartup - start;
                bool done = wash.Logic.Scrub.Done;
                Debug.Log($"WASH_EVERY_POT vòng {round}: {pot.name} {(done ? "xong" : "KHÔNG xong")} sau {took:0.0} s, sạch {cleaned * 100f:0}%, con trỏ {wash.Scrubbing?.Motion.Cursor}");
                report.Add($"{pot.name}: Ø {diameter:0.000} cao {height:0.000}, {(done ? "xong" : "KHÔNG xong")} sau {took:0.0} s, sạch {cleaned * 100f:0}%");
                if (!done) failed.Add(pot.name);
                if (!done) break;
                yield return null;
                input.SimUse = true;
                yield return PlayTestUtil.WaitUntil(() => wash.Rinsed, 5f);
                input.SimUse = false;
                wash.OnInteract(wash.rack, rig.interactor);
                yield return null;
            }
            Debug.Log("WASH_EVERY_POT\n" + string.Join("\n", report));
            Assert.IsEmpty(failed, "nồi không cọ hết được:\n" + string.Join("\n", report));
        }
    }
}
