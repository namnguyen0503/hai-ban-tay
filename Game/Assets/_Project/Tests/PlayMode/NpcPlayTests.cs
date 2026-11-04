using System.Collections;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>NPC trong bếp làm việc thật: quản bếp bê thùng khoai đi đổ rồi trả chỗ cũ, phụ bếp đi hết vòng việc.</summary>
    public class NpcPlayTests
    {
        [TearDown]
        public void TearDown()
        {
            PlayTestUtil.ExitSim();
        }

        [UnityTest]
        public IEnumerator CookCarriesTheFullTubToTheStoveAndBack()
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            CookRoutine cook = null;
            HelperRoutine helper = null;
            yield return PlayTestUtil.WaitUntil(() => (cook = Object.FindAnyObjectByType<CookRoutine>()) != null &&
                                                      (helper = Object.FindAnyObjectByType<HelperRoutine>()) != null, 10f);
            Assert.IsNotNull(cook, "thiếu quản bếp");
            Assert.IsNotNull(helper, "thiếu phụ bếp");
            // Người chơi đứng ngoài các lộ trình.
            PlayerRig.Current.motor.Teleport(new Vector3(4f, 0f, -2.2f), 0f);
            var tubHome = cook.tub.position;
            Time.timeScale = 8f;

            // Lượt dốc bao nào cũng có nồi bữa đang nấu (days.vi.json: "dirty" ≥ 1): quản bếp đổ khoai vào nồi đang nấu.
            Object.FindAnyObjectByType<MealPots>().BeginMeal(new PhaseData { Id = "kiểm_thùng", Dirty = 2 });
            cook.sacks.SimulateWork();
            Assert.AreEqual(cook.sacks.potatoesPerSack, cook.sacks.InTub);
            yield return PlayTestUtil.WaitUntil(() => cook.sacks.TubAway, 40f);
            Assert.IsTrue(cook.sacks.TubAway, "quản bếp phải đi lấy thùng khi thùng đầy một bao");
            yield return PlayTestUtil.WaitUntil(() => cook.TubsCarried >= 1, 60f);
            Assert.AreEqual(1, cook.TubsCarried);
            Assert.AreEqual(0, cook.sacks.InTub, "khoai đã đổ vào nồi");
            Assert.IsFalse(cook.sacks.TubAway);
            Assert.Less(Vector3.Distance(cook.tub.position, tubHome), 0.01f, "thùng trả về chỗ cũ");
            yield return PlayTestUtil.WaitUntil(() => helper.Loops >= 1, 60f);
            Assert.GreaterOrEqual(helper.Loops, 1, "phụ bếp đi hết một vòng việc");
        }
    }
}
