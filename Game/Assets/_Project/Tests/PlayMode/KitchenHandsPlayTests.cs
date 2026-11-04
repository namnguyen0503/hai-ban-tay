using System.Collections;
using System.Linq;
using HaiBanTay.Content;
using HaiBanTay.Npc;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// W5.3 (phản hồi 14/10: "bọn bếp vẫn khuấy không khí, thao tác chẳng vào đâu cả"): tay NPC trong bếp phải chạm vật thật.
    /// Quản bếp khuấy bằng muôi nằm trong lòng một nồi đang nấu trên ô bếp, tay trái cầm núm nắp đã nhấc; phụ bếp xúc than có than
    /// trên lưỡi xẻng, đưa lưỡi vào cửa lò; bê nồi thì hai tay ở hai quai. Đo cổ tay thật (xương sau IK) tới chỗ cầm, mỗi khung.
    /// </summary>
    public sealed class KitchenHandsPlayTests
    {
        const float HandTolerance = 0.04f;
        /// <summary>Góc lớn nhất giữa hướng đi và hướng mặt khi NPC đang đi (độ): quá thì là đi giật lùi, đi ngang.</summary>
        public const float MaxWalkAngle = 80f;
        /// <summary>Hai NPC chồng thân lâu nhất (s): chỉ cho lướt qua khi lách (đi xuyên là đường lui cuối, sau 4 s kẹt).</summary>
        public const float MaxOverlapSeconds = 0.3f;

        [TearDown] public void CleanUp() => PlayTestUtil.ExitSim();

        static float Reach(NpcHandWork hands, bool left, Transform grip) =>
            grip == null || hands.Wrist(left) == null ? float.MaxValue : Vector3.Distance(hands.Wrist(left).position, grip.position);

        /// <summary>Hai tay đang bám đủ (không tính lúc đang vươn tới / buông): cổ tay xa chỗ cầm nhất; 0 nếu chưa bám.</summary>
        static float Holding(NpcHandWork hands, Transform leftGrip, Transform rightGrip) =>
            hands.Grip(true) == leftGrip && hands.Grip(false) == rightGrip
                ? Mathf.Max(Reach(hands, true, leftGrip), Reach(hands, false, rightGrip)) : 0f;

        [UnityTest, Timeout(240000)]
        public IEnumerator CookStirsInsideTheRealPotWithTheLidInHisOtherHand()
        {
            yield return ChoreTestUtil.Galley();
            Assert.IsTrue(NavMesh.SamplePosition(new Vector3(0f, 0f, -0.9f), out _, 0.6f, NavMesh.AllAreas), "cảnh bếp có NavMesh");
            var meals = Object.FindAnyObjectByType<MealPots>();
            var cook = Object.FindAnyObjectByType<CookRoutine>();
            var hands = cook.GetComponent<NpcHandWork>();
            Assert.IsNotNull(hands, "quản bếp có tay IK");
            Assert.IsTrue(cook.agent.useNavMesh, "quản bếp đi theo NavMesh");
            meals.BeginMeal(new PhaseData { Id = "kiểm_khuấy", Dirty = 2 });
            var motion = cook.gameObject.AddComponent<MotionProbe>();
            motion.trace = true;
            motion.hands = hands;
            motion.WatchWrists().Watch("muôi", cook.ladle);
            foreach (var p in meals.CookingPots)
                motion.Watch("nắp " + p.name, p.GetComponentsInChildren<Transform>(true).First(t => t.name == "MealLid"));
            // Đo khi muôi đã vào lòng nồi và đang khuấy vòng (không tính lúc đưa muôi vào, lúc gõ miệng nồi).
            yield return PlayTestUtil.WaitUntil(() => hands.Activity == "stir" && hands.Step == "circle" && cook.StirringPot != null, 60f);
            Assert.AreEqual("stir", hands.Activity, "quản bếp phải tới khuấy một nồi đang nấu");
            var pot = cook.StirringPot;
            Assert.IsTrue(meals.CookingPots.Contains(pot.gameObject), "nồi đang khuấy là nồi đang nấu");
            Assert.GreaterOrEqual(meals.HobOf(pot.gameObject), 0, "nồi nằm trên một ô bếp");
            NpcHandWork.MeasurePot(pot, out var center, out float rim, out float radius);
            var lid = pot.GetComponentsInChildren<Transform>(true).First(t => t.name == "MealLid");
            Assert.AreNotEqual(pot, lid.parent, "nắp đã nhấc khỏi miệng nồi (đang trong tay trái)");
            float bowlOut = float.MinValue, bowlHigh = float.MinValue, right = 0f, left = 0f;
            int samples = 0;
            for (float t = 0f; t < 2f && hands.Step == "circle"; t += Time.deltaTime)
            {
                yield return null;
                var bowl = cook.ladle.position;
                bowlOut = Mathf.Max(bowlOut, new Vector2(bowl.x - center.x, bowl.z - center.z).magnitude - radius);
                bowlHigh = Mathf.Max(bowlHigh, bowl.y - rim);
                right = Mathf.Max(right, Reach(hands, false, cook.ladleGrip));
                left = Mathf.Max(left, Reach(hands, true, hands.Grip(true)));
                samples++;
            }
            Debug.Log($"KITCHEN_HANDS stir: {samples} khung, lòng muôi ra ngoài thành {bowlOut * 100f:F1} cm, cao hơn miệng {bowlHigh * 100f:F1} cm, " +
                      $"tay phải–cán {right * 100f:F1} cm, tay trái–núm {left * 100f:F1} cm");
            Assert.Greater(samples, 30, "đủ khung khuấy để đo");
            Assert.LessOrEqual(bowlOut, 0f, "lòng muôi luôn trong lòng nồi");
            Assert.Less(bowlHigh, -0.05f, "lòng muôi ngập dưới miệng nồi khi khuấy");
            Assert.LessOrEqual(right, HandTolerance, "tay phải nắm đúng cán muôi");
            Assert.LessOrEqual(left, HandTolerance, "tay trái cầm đúng núm nắp đã nhấc");
            yield return PlayTestUtil.WaitUntil(() => hands.Activity == "", 20f);
            Assert.AreEqual(pot, lid.parent, "khuấy xong đậy nắp lại");
            Assert.Less(Vector3.Distance(lid.position, new Vector3(center.x, lid.position.y, center.z)), 0.06f, "nắp về đúng miệng nồi");
            Debug.Log("KITCHEN_MOTION stir: " + motion.Report);
            Assert.Less(motion.Worst, MotionProbe.PopStep, "tay, muôi, nắp không giật/nhảy chỗ (đi tới nồi, nhấc nắp, khuấy, đậy nắp): " + motion.Report);
            Assert.Less(motion.WorstWalkAngle, MaxWalkAngle, "đi về phía trước, không giật lùi hay đi ngang: " + motion.Report);
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator HelperScoopsRealCoalAndFeedsItThroughTheFireDoor()
        {
            yield return ChoreTestUtil.Galley();
            var helper = Object.FindAnyObjectByType<HelperRoutine>();
            var hands = helper.GetComponent<NpcHandWork>();
            Assert.IsNotNull(hands);
            helper.enabled = false;
            helper.enabled = true;
            var probe = helper.gameObject.AddComponent<HandProbe>();
            probe.hands = hands; probe.left = helper.shovelGripLeft; probe.right = helper.shovelGripRight;
            var motion = helper.gameObject.AddComponent<MotionProbe>();
            motion.trace = true;
            motion.hands = hands;
            motion.WatchWrists().Watch("xẻng", helper.shovel);
            probe.Reset("scoop");
            yield return PlayTestUtil.WaitUntil(() => hands.Activity == "scoop", 60f);
            Assert.AreEqual("scoop", hands.Activity, "phụ bếp tới thùng than xúc");
            yield return PlayTestUtil.WaitUntil(() => hands.Activity != "scoop", 30f);
            float scoopWorst = probe.Worst; string scoopAt = probe.WorstAt;
            Assert.IsTrue(helper.shovelLoad.activeSelf, "xúc xong có than trên lưỡi xẻng");
            var pile = helper.coal.binPile.GetComponentInChildren<Renderer>().bounds;
            probe.Reset("carry");
            // Bê xẻng tới lò: đi về phía trước (hướng đi trùng hướng mặt), lưỡi xẻng ở phía trước thân, không kéo lê sau lưng.
            float worstWalkAngle = 0f, worstBladeBehind = float.MaxValue;
            var lastPos = helper.agent.Position;
            while (hands.Activity != "feed" && helper.Feeds == 0)
            {
                yield return null;
                var pos = helper.agent.Position;
                var moved = pos - lastPos; moved.y = 0f;
                lastPos = pos;
                if (!helper.agent.Walking || moved.magnitude < 0.3f * helper.agent.walkSpeed * Time.deltaTime) continue;
                worstWalkAngle = Mathf.Max(worstWalkAngle, Vector3.Angle(moved, helper.transform.forward));
                var blade = helper.transform.InverseTransformPoint(helper.shovel.TransformPoint(NpcHandWork.ShovelBladeMid));
                worstBladeBehind = Mathf.Min(worstBladeBehind, blade.z);
            }
            Debug.Log($"KITCHEN_HANDS shovel walk: hướng đi lệch hướng mặt tối đa {worstWalkAngle:F0}°, lưỡi xẻng trước thân ít nhất {worstBladeBehind:F2} m");
            Assert.Less(worstWalkAngle, MaxWalkAngle, "bê xẻng đi về phía trước, không giật lùi");
            Assert.Greater(worstBladeBehind, 0.2f, "lưỡi xẻng ở trước thân, không kéo lê sau lưng");
            Assert.AreEqual("feed", hands.Activity, "phụ bếp mang xẻng than tới cửa lò");
            float carryWorst = probe.Worst; string carryAt = probe.WorstAt;
            probe.Reset("feed");
            var mouths = helper.coal.cells.Select(c => c.mouth.transform.position).ToArray();
            float nearestMouth = float.MaxValue;
            int samples = 0;
            while (hands.Activity == "feed")
            {
                yield return null;
                var blade = helper.shovel.TransformPoint(NpcHandWork.ShovelBladeMid);
                nearestMouth = Mathf.Min(nearestMouth, mouths.Min(m => Vector3.Distance(m, blade)));
                samples++;
            }
            float feedWorst = probe.Worst; string feedAt = probe.WorstAt;
            float worst = Mathf.Max(scoopWorst, Mathf.Max(carryWorst, feedWorst));
            Debug.Log($"KITCHEN_HANDS shovel: {samples} khung, tay–cán xẻng xa nhất: xúc {scoopWorst * 100f:F1} cm ({scoopAt}), bê {carryWorst * 100f:F1} cm ({carryAt}), " +
                      $"nạp {feedWorst * 100f:F1} cm ({feedAt}); lưỡi gần cửa lò nhất {nearestMouth * 100f:F1} cm, đống than {pile.center}");
            Assert.LessOrEqual(worst, HandTolerance, "hai tay nắm đúng cán xẻng lúc xúc và lúc nạp");
            Assert.Less(nearestMouth, helper.feedDepth + 0.05f, "lưỡi xẻng đi vào cửa lò");
            Assert.IsFalse(helper.shovelLoad.activeSelf, "than đã đổ vào lò");
            yield return PlayTestUtil.WaitUntil(() => helper.Feeds >= 1, 10f);
            Assert.AreEqual(1, helper.Feeds, "một lượt nạp đã xong");
            Debug.Log("KITCHEN_MOTION shovel: " + motion.Report);
            Assert.Less(motion.Worst, MotionProbe.PopStep, "tay và xẻng không giật/nhảy chỗ (xúc, bê, nạp): " + motion.Report);
            Assert.Less(motion.WorstWalkAngle, MaxWalkAngle, "đi về phía trước, không giật lùi hay đi ngang: " + motion.Report);
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator HelperCarriesTheDirtyPotByItsTwoHandles()
        {
            yield return ChoreTestUtil.Galley();
            var meals = Object.FindAnyObjectByType<MealPots>();
            var hands = meals.helper.GetComponent<NpcHandWork>();
            meals.BeginMeal(new PhaseData { Id = "kiểm_bê", Dirty = 1 });
            var pot = meals.CookingPots[0];
            var motion = meals.helper.gameObject.AddComponent<MotionProbe>();
            motion.trace = true;
            motion.hands = hands;
            motion.WatchWrists().Watch("nồi", pot.transform);
            bool done = false;
            meals.StartCoroutine(Run(meals.Serve(), () => done = true));
            yield return PlayTestUtil.WaitUntil(() => meals.helper.agent.Carrying == pot.transform, 60f);
            Assert.AreEqual(pot.transform, meals.helper.agent.Carrying, "phụ bếp bê đúng nồi vừa nấu");
            float worst = 0f;
            int samples = 0;
            for (float t = 0f; t < 1.5f && meals.helper.agent.Carrying == pot.transform; t += Time.deltaTime)
            {
                yield return null;
                var handles = pot.GetComponentsInChildren<Transform>().Where(x => x.name == "Handle").Select(x => x.position).ToArray();
                Assert.AreEqual(2, handles.Length);
                foreach (bool left in new[] { true, false })
                    worst = Mathf.Max(worst, handles.Min(h => Vector3.Distance(h, hands.Wrist(left).position)));
                samples++;
            }
            Debug.Log($"KITCHEN_HANDS carry: {samples} khung, cổ tay–quai nồi xa nhất {worst * 100f:F1} cm");
            Assert.Greater(samples, 20);
            Assert.LessOrEqual(worst, 0.13f, "hai tay ở hai quai nồi (cổ tay cách quai một lòng bàn tay)");
            yield return PlayTestUtil.WaitUntil(() => done, 90f);
            // Nồi đặt xuống chồng rồi buông: nồi không thuộc tay nữa, chỉ tính tới lúc dọn xong.
            Debug.Log("KITCHEN_MOTION carry pot: " + motion.Report);
            Assert.Less(motion.Worst, MotionProbe.PopStep, "tay và nồi không giật/nhảy chỗ (nhấc, bê, đặt): " + motion.Report);
            Assert.Less(motion.WorstWalkAngle, MaxWalkAngle, "đi về phía trước, không giật lùi hay đi ngang: " + motion.Report);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator HelperReachesEveryPotWhileTheCookIsStirring()
        {
            // Bot cả game 14/10 19:30: quản bếp đứng khuấy ngay trước nồi, đúng chỗ phụ bếp đứng bưng nồi đó; dọn bữa thì phụ bếp đứng
            // chờ quá hạn rồi bị đặt thẳng vào chỗ (MealPots.TimedOutWalks).
            yield return ChoreTestUtil.Galley();
            var meals = Object.FindAnyObjectByType<MealPots>();
            var cook = Object.FindAnyObjectByType<CookRoutine>();
            var hands = cook.GetComponent<NpcHandWork>();
            meals.BeginMeal(new PhaseData { Id = "kiểm_nhường", Dirty = 2 });
            yield return PlayTestUtil.WaitUntil(() => cook.StirringPot != null && hands.Step == "circle", 60f);
            Assert.IsNotNull(cook.StirringPot, "quản bếp đang khuấy một nồi");
            int timedOut = meals.TimedOutWalks;
            var overlap = meals.gameObject.AddComponent<NpcOverlapProbe>().Watch(cook.agent, meals.helper.agent);
            bool done = false;
            meals.StartCoroutine(Run(meals.Serve(), () => done = true));
            yield return PlayTestUtil.WaitUntil(() => done, 150f);
            Assert.IsTrue(done, "dọn bữa xong");
            Debug.Log($"KITCHEN_HANDS serve: quá hạn {meals.TimedOutWalks - timedOut} đoạn, quản bếp ở {cook.agent.Position}; {overlap.Report}");
            Assert.Less(overlap.Longest, MaxOverlapSeconds, "quản bếp, phụ bếp không đi xuyên người nhau: " + overlap.Report);
            Assert.AreEqual(timedOut, meals.TimedOutWalks, "phụ bếp tự đi tới từng nồi (quản bếp nhường chỗ), không bị đặt thẳng vào chỗ");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CookParksTheLadleOnTheStoveAndTipsThePotatoesIntoThePot()
        {
            yield return ChoreTestUtil.Galley();
            var meals = Object.FindAnyObjectByType<MealPots>();
            var cook = Object.FindAnyObjectByType<CookRoutine>();
            var hands = cook.GetComponent<NpcHandWork>();
            var sacks = Object.FindAnyObjectByType<SackStation>();
            meals.BeginMeal(new PhaseData { Id = "kiểm_đổ", Dirty = 2 });
            for (int i = 0; i < 4 && sacks.InTub < sacks.potatoesPerSack; i++) sacks.SimulateWork();
            Assert.GreaterOrEqual(sacks.InTub, sacks.potatoesPerSack, "thùng đầy một bao");
            int inTub = sacks.InTub;
            var motion = cook.gameObject.AddComponent<MotionProbe>();
            motion.trace = true;
            motion.hands = hands;
            motion.WatchWrists().Watch("muôi", cook.ladle).Watch("thùng", cook.tub);
            var overlap = meals.gameObject.AddComponent<NpcOverlapProbe>().Watch(cook.agent, meals.helper.agent);
            // Ba đứng giữa lối nam (bàn rau) đúng trên đường quản bếp đi tới thùng: quản bếp vòng qua, không đứng chờ mãi.
            ChoreTestUtil.Look(new Vector3(0f, 0f, -1.2f), new Vector3(0f, 1f, -3f));
            cook.agent.ResetStats();
            yield return PlayTestUtil.WaitUntil(() => hands.Activity == "carry" && cook.agent.Carrying == cook.tub, 120f);
            Debug.Log($"KITCHEN_HANDS cook to tub: chờ lâu nhất {cook.agent.LongestWait:F1} s, quản bếp ở {cook.agent.Position}");
            Assert.AreEqual(cook.tub, cook.agent.Carrying, "quản bếp bê thùng khoai (Ba đứng chắn lối thì vòng qua)");
            Assert.Less(cook.agent.LongestWait, 3f, "người chơi đứng chắn lối: quản bếp vòng qua trên NavMesh, không chầu chực");

            // Muôi gác trên mặt bếp gang (không lơ lửng trước mặt lò), cán không đè lên nồi ô bên cạnh.
            Assert.IsFalse(hands.InHand(cook.ladle, false), "muôi đã gác, tay phải rảnh để bê thùng");
            var bowl = cook.ladle.position;
            Assert.IsTrue(Physics.Raycast(bowl + Vector3.up * 0.25f, Vector3.down, out var hit, 0.6f, ~0, QueryTriggerInteraction.Ignore), "dưới muôi có mặt đỡ");
            Debug.Log($"KITCHEN_HANDS ladle rest: lòng muôi {bowl}, mặt đỡ {hit.collider.name} y={hit.point.y:F3}");
            Assert.Greater(hit.point.y, 0.8f, "muôi nằm trên mặt bếp, không rơi xuống sàn");
            Assert.Less(Mathf.Abs(bowl.y - 0.02f - hit.point.y), 0.015f, "lòng muôi nằm sát mặt bếp");
            var handleEnd = cook.ladle.TransformPoint(new Vector3(0f, 0.45f, -0.012f));
            foreach (var other in meals.CookingPots)
            {
                NpcHandWork.MeasurePot(other.transform, out var c, out float rim, out float r);
                float flat = new Vector2(handleEnd.x - c.x, handleEnd.z - c.z).magnitude;
                Assert.IsTrue(flat > r / 0.88f + 0.02f || handleEnd.y > rim, $"cán muôi không xuyên {other.name}");
            }

            // Đổ khoai: từng củ rời thùng rơi vào lòng nồi đang nấu; thùng rỗng; nồi có khoai.
            yield return PlayTestUtil.WaitUntil(() => hands.Activity == "pour", 60f);
            Assert.AreEqual("pour", hands.Activity);
            var pot = meals.CookingPots.OrderBy(p => Vector3.Distance(p.transform.position, cook.tub.position)).First().transform;
            NpcHandWork.MeasurePot(pot, out var center, out float potRim, out float radius);
            var last = new Vector3[sacks.tubPotatoes.Length];
            var was = sacks.tubPotatoes.Select(p => p.gameObject.activeSelf).ToArray();
            int landed = 0, outside = 0;
            for (float t = 0f; t < 4f && sacks.InTub > 0; t += Time.deltaTime)
            {
                for (int i = 0; i < last.Length; i++)
                {
                    bool on = sacks.tubPotatoes[i].gameObject.activeSelf;
                    if (was[i] && !on)
                    {
                        landed++;
                        if (new Vector2(last[i].x - center.x, last[i].z - center.z).magnitude > radius || last[i].y > potRim) outside++;
                    }
                    was[i] = on;
                    last[i] = sacks.tubPotatoes[i].position;
                }
                yield return null;
            }
            Debug.Log($"KITCHEN_HANDS pour: {landed}/{inTub} củ rơi vào nồi {pot.name}, {outside} củ ngoài lòng nồi");
            Assert.AreEqual(0, sacks.InTub, "đổ xong thùng rỗng");
            Assert.AreEqual(inTub, landed, "mọi củ trong thùng đều rơi xuống (không biến mất tại chỗ)");
            Assert.AreEqual(0, outside, "củ nào cũng rơi vào lòng nồi");
            var food = pot.Find("CookingPotatoes");
            Assert.IsTrue(food == null || food.gameObject.activeSelf, "nồi có khoai");

            // Trả thùng về chỗ cũ rồi cầm muôi lại.
            yield return PlayTestUtil.WaitUntil(() => hands.Activity == "stir" || (cook.TubsCarried > 0 && hands.InHand(cook.ladle, false)), 90f);
            Assert.AreEqual(1, cook.TubsCarried, "thùng đã trả chỗ");
            Assert.IsTrue(hands.InHand(cook.ladle, false), "cầm muôi lại");
            Debug.Log("KITCHEN_MOTION tub: " + motion.Report + "; " + overlap.Report);
            Assert.Less(motion.Worst, MotionProbe.PopStep, "tay, muôi, thùng không giật/nhảy chỗ (gác muôi, bê, đổ, trả thùng, cầm muôi): " + motion.Report);
            Assert.Less(motion.WorstWalkAngle, MaxWalkAngle, "đi về phía trước, không giật lùi hay đi ngang: " + motion.Report);
            Assert.Less(overlap.Longest, MaxOverlapSeconds, "quản bếp, phụ bếp không đi xuyên người nhau: " + overlap.Report);
        }

        /// <summary>
        /// QA-0154 (bot 01/11, D1.breakfast): MealPots dọn bữa tắt routine quản bếp đúng lúc ông đang hạ thùng khoai về chỗ (đã PutDown,
        /// chưa tính là bê). NpcHandWork.Move dừng giữa chừng, thùng lơ lửng trước bụng, rồi ông đi ra chỗ trông bếp xuyên thùng (chồng
        /// Stave 0,3 s). Dừng routine lúc nào thì thùng cũng về đúng chỗ, đúng cha; đi ra chỗ trông bếp không chồng vật tĩnh.
        /// </summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator CookPausedWhileSettingTheTubDownLeavesItHome()
        {
            yield return ChoreTestUtil.Galley();
            var meals = Object.FindAnyObjectByType<MealPots>();
            var cook = Object.FindAnyObjectByType<CookRoutine>();
            var sacks = Object.FindAnyObjectByType<SackStation>();
            var home = cook.tub.position;
            var homeParent = cook.tub.parent;
            meals.BeginMeal(new PhaseData { Id = "kiểm_dừng_thùng", Dirty = 2 });
            for (int i = 0; i < 4 && sacks.InTub < sacks.potatoesPerSack; i++) sacks.SimulateWork();
            ChoreTestUtil.Look(new Vector3(-3.6f, 0f, -1.2f), new Vector3(-3.6f, 1f, -3f));
            yield return PlayTestUtil.WaitUntil(() => cook.agent.Carrying == cook.tub, 120f);
            Assert.AreEqual(cook.tub, cook.agent.Carrying, "quản bếp bê thùng");
            // Đổ xong, về chỗ thùng, buông khỏi điểm bê: thùng đang hạ xuống.
            yield return PlayTestUtil.WaitUntil(() => cook.agent.Carrying == null && Vector3.Distance(cook.tub.position, home) > 0.1f, 120f);
            Assert.IsNull(cook.agent.Carrying, "đã buông thùng khỏi điểm bê");
            float lift = Vector3.Distance(cook.tub.position, home);
            Assert.Greater(lift, 0.1f, "thùng đang hạ xuống, chưa về chỗ");
            Assert.IsTrue(cook.TubOut, "thùng đang hạ tính là đang rời chỗ (MealPots chờ)");

            cook.enabled = false;
            yield return null;
            float off = Vector3.Distance(cook.tub.position, home);
            Debug.Log($"KITCHEN_HANDS tub pause: thùng cách chỗ {lift:F2} m lúc dừng, sau dừng {off:F3} m, cha {cook.tub.parent?.name}, TubAway {sacks.TubAway}");
            Assert.Less(off, 0.01f, "dừng routine giữa lúc hạ thùng: thùng về đúng chỗ, không lơ lửng");
            Assert.AreEqual(homeParent, cook.tub.parent, "thùng về đúng cha");
            Assert.IsFalse(sacks.TubAway, "thùng đã về, bao đổ được vào");
            Assert.IsFalse(cook.TubOut);

            // Như MealPots: quản bếp đi ra chỗ trông bếp, thân không chồng vật tĩnh quá 0,3 s.
            cook.agent.ClearWalkState();
            var walk = cook.StartCoroutine(cook.agent.Go(cook.watchSpot));
            float inside = 0f, longest = 0f;
            Collider worst = null;
            for (float t = 0f; t < 12f && Vector3.Distance(cook.agent.Position, cook.watchSpot) > 0.15f; t += Time.deltaTime)
            {
                var c = cook.agent.Walking ? InsideStatic(cook.agent) : null;
                inside = c != null ? inside + Time.deltaTime : 0f;
                if (inside > longest) { longest = inside; worst = c; }
                yield return null;
            }
            cook.StopCoroutine(walk);
            Debug.Log($"KITCHEN_HANDS tub pause walk: chồng vật tĩnh lâu nhất {longest:F2} s ({(worst != null ? worst.name : "-")}), tới {cook.agent.Position}");
            Assert.Less(longest, MaxOverlapSeconds, $"ra chỗ trông bếp không chồng {(worst != null ? worst.name : "vật")}");
        }

        /// <summary>Vật tĩnh thân NPC đang chồng lên (đo như observer của bot, PlaythroughLog); null nếu không chồng.</summary>
        static Collider InsideStatic(NpcAgent npc)
        {
            var p = npc.Position;
            float r = Mathf.Max(0.05f, npc.bodyRadius - 0.02f);
            foreach (var c in Physics.OverlapCapsule(p + Vector3.up * (0.32f + r), p + Vector3.up * (1.38f - r), r, ~(1 << 2), QueryTriggerInteraction.Ignore))
                if (NpcPathing.IsObstacle(c, npc.transform, p.y)) return c;
            return null;
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator HelperCarryingAPotGoesAroundBaNotThroughTheTable()
        {
            // Bot cả game 14/10 (ngày 5, bữa trưa): Ba đứng ở mép bắc bàn sơ chế, đúng lối phụ bếp bê nồi bẩn ra chồng nồi; phụ bếp
            // lách người rồi đi xuyên mặt bàn, chân bàn, rổ (npc_inside MAJOR, `red-20261014-bot-whole-npc.jsonl`).
            yield return ChoreTestUtil.Galley();
            var meals = Object.FindAnyObjectByType<MealPots>();
            meals.BeginMeal(new PhaseData { Id = "kiểm_lối", Dirty = 2 });
            ChoreTestUtil.Look(new Vector3(0.5f, 0f, 0.95f), new Vector3(0.5f, 0.9f, 0f));
            var helper = meals.helper.agent;
            bool done = false;
            meals.StartCoroutine(Run(meals.Serve(), () => done = true));
            float stuck = 0f, worst = 0f, stillSince = 0f, nextDiag = 0f;
            string what = "";
            var anchor = helper.Position;
            for (float t = 0f; t < 150f && !done; t += Time.deltaTime)
            {
                yield return null;
                // Chẩn đoán (CI 01/11: phụ bếp đứng yên ở (5.45, -0.38) tới chồng nồi, không chờ ai): đứng yên > 2 s thì in trạng thái đi mỗi giây.
                if ((helper.Position - anchor).sqrMagnitude > 0.1f * 0.1f) { anchor = helper.Position; stillSince = t; }
                else if (helper.Walking && t - stillSince > 2f && t >= nextDiag)
                {
                    nextDiag = t + 1f;
                    Debug.Log($"KITCHEN_HANDS stall {t - stillSince:F1} s @({helper.Position.x:F2}, {helper.Position.z:F2}): {helper.WalkDiag()}");
                }
                var c = helper.Walking ? InsideStatic(helper) : null;
                stuck = c != null ? stuck + Time.deltaTime : 0f;
                if (c != null && stuck > worst)
                {
                    worst = stuck;
                    what = $"{c.name} @({helper.Position.x:F2}, {helper.Position.z:F2})";
                }
            }
            Assert.IsTrue(done, "dọn bữa xong");
            Debug.Log($"KITCHEN_HANDS aisle: phụ bếp chồng vật tĩnh lâu nhất {worst:F2} s {what}, quá hạn {meals.TimedOutWalks} đoạn");
            Assert.Less(worst, 0.3f, "phụ bếp bê nồi vòng qua Ba, không đi xuyên bàn/rổ: " + what);
            Assert.AreEqual(0, meals.TimedOutWalks, "phụ bếp tự tới chồng nồi, không bị đặt thẳng vào chỗ");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator BaStopsBeforeTheLadleInTheCooksHand()
        {
            // Bot 01/11 (QA-0152): hông Ba lún 10,4 cm vào cán muôi quản bếp lúc kéo nồi cao lùi qua lò; muôi trong tay chưa tính là đồ NPC
            // cầm nên Ba không bị chặn. Quản bếp đứng giữa lối (routine dừng, muôi vẫn trên tay), Ba đi thẳng vào: thân dừng trước muôi.
            yield return ChoreTestUtil.Galley();
            var cook = Object.FindAnyObjectByType<CookRoutine>();
            yield return null;
            yield return null;
            cook.enabled = false; // Stopped(): muôi về tay, routine không dời quản bếp
            var spot = new Vector3(1.6f, 0f, 0.6f);
            cook.agent.Place(spot, 270f);
            yield return null;
            var rig = HaiBanTay.Player.PlayerRig.Current;
            var ladleAt = cook.ladle.position;
            var toward = new Vector3(ladleAt.x - spot.x, 0f, ladleAt.z - spot.z);
            if (toward.sqrMagnitude < 0.01f) toward = Vector3.left;
            toward.Normalize();
            // Ba đứng trước muôi 1,2 m, nhìn vào muôi, rồi đi thẳng tới.
            var start = new Vector3(ladleAt.x, 0f, ladleAt.z) + toward * 1.2f;
            ChoreTestUtil.Look(start, new Vector3(ladleAt.x, 1.4f, ladleAt.z));
            yield return null;
            float minGap = float.PositiveInfinity;
            var from = rig.Position;
            HaiBanTay.Core.GameRoot.Instance.Input.SimMove = new Vector2(0f, 1f);
            // Đo tới lúc quản bếp nhường (Ba ép quá NpcAgent.ItemYieldAfter thì Ba đi xuyên, luật không kẹt người chơi), tối đa 3 s.
            float pressedFor = 0f;
            for (float t = 0f; t < 3f && !cook.agent.YieldingToPlayer; t += Time.deltaTime)
            {
                yield return null;
                float gap = cook.agent.ReachGapNow();
                if (gap < NpcAgent.PlayerReachMargin + 0.02f) pressedFor += Time.deltaTime;
                if (!cook.agent.YieldingToPlayer) minGap = Mathf.Min(minGap, gap);
            }
            Debug.Log($"KITCHEN_HANDS ladle press: ép {pressedFor:F2} s, nhường {cook.agent.YieldingToPlayer}");
            Assert.Greater(pressedFor, 0.5f, "Ba phải đứng ép vào muôi một lúc trước khi quản bếp nhường");
            Assert.IsTrue(cook.agent.YieldingToPlayer, "Ba ép lâu thì quản bếp nhường");

            // Bot 01/11 lượt 3: nhường bằng tắt va chạm là Ba đi xuyên đồ (bao vai 12 cm, nồi 11 cm, lưỡi xẻng 13 cm). Nhường thì quản bếp
            // lùi khỏi Ba, muôi vẫn chặn: Ba đi tiếp theo sau, thân không lún vào muôi (trừ đường lui cuối khi lùi không được).
            var baAtYield = rig.Position;
            var cookAtYield = cook.agent.Position;
            float minAfter = float.PositiveInfinity;
            for (float t = 0f; t < 2.5f && !cook.agent.GiveWayCornered; t += Time.deltaTime)
            {
                yield return null;
                minAfter = Mathf.Min(minAfter, cook.agent.ReachGapNow());
            }
            HaiBanTay.Core.GameRoot.Instance.Input.SimMove = Vector2.zero;
            float baOn = Vector3.Distance(rig.Position, baAtYield), cookBack = Vector3.Distance(cook.agent.Position, cookAtYield);
            Debug.Log($"KITCHEN_HANDS ladle give way: khe nhỏ nhất sau nhường {minAfter * 100f:F1} cm, quản bếp lùi {cookBack:F2} m " +
                      $"(GaveWay {cook.agent.GaveWayDistance:F2}), Ba đi tiếp {baOn:F2} m, dồn góc {cook.agent.GiveWayCornered}");
            Assert.Greater(cookBack, 0.05f, "quản bếp nhường thì lùi khỏi Ba (còn chỗ phía sau)");
            Assert.Greater(baOn, 0.05f, "Ba đi tiếp được khi quản bếp lùi (không kẹt)");
            Assert.GreaterOrEqual(minAfter, 0f, $"nhường mà Ba vẫn không lún vào muôi (khe {minAfter * 100f:F1} cm)");
            Debug.Log($"KITCHEN_HANDS ladle: khe nhỏ nhất {minGap * 100f:F1} cm, Ba đi {(rig.Position - from).magnitude:F2} m, muôi ở {ladleAt}");
            Assert.IsTrue(float.IsFinite(minGap), "muôi trong tay quản bếp phải được tính là đồ NPC cầm (ReachGapNow hữu hạn)");
            Assert.Less(minGap, 0.5f, "Ba phải đi tới sát muôi (kịch bản có ý nghĩa)");
            Assert.GreaterOrEqual(minGap, 0.015f, $"thân Ba lún vào cán muôi quản bếp (khe {minGap * 100f:F1} cm)");
        }

        static IEnumerator Run(IEnumerator inner, System.Action finished)
        {
            yield return inner;
            finished();
        }
    }
}
