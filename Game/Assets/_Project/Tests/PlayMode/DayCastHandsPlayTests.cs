using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HaiBanTay.Shift;
using HaiBanTay.Npc;
using HaiBanTay.Ship;
using HaiBanTay.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Người theo ngày trên tàu (phản hồi 14/10: "NavMesh cho những nhân vật quan trọng trên tàu", "thao tác chẳng vào đâu cả"):
    /// lính, Jacques, ông Mai đi theo NavMesh từ boong / hành lang vào bếp; Jacques ngày bão nắm hai quai nồi rời thật (không vung
    /// tay giữa không trung), Ba chằng xong nồi nào thì sang nồi khác; ở nồi cao thì hai tay úp lên vành nồi.
    /// </summary>
    public sealed class DayCastHandsPlayTests
    {
        [TearDown] public void CleanUp() => PlayTestUtil.ExitSim();

        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        [UnityTest, Timeout(180000)]
        public IEnumerator DayPeopleHaveNavMeshRoutesIntoTheGalley()
        {
            yield return ChoreTestUtil.Galley();
            var cast = Object.FindAnyObjectByType<DayCast>();
            foreach (var a in cast.All)
                Assert.IsTrue(a.useNavMesh, a.name + " đi theo NavMesh");
            var legs = new List<(string, Vector3, Vector3)>();
            void Chain(string name, Vector3[] route, Vector3 end)
            {
                for (int i = 0; i + 1 < route.Length; i++) legs.Add((name + " " + i, route[i], route[i + 1]));
                legs.Add((name + " cuối", route[route.Length - 1], end));
            }
            foreach (var spot in cast.greensSpots) Chain("boong→rau", cast.deckToGalley, spot.position);
            Chain("boong→nồi bão", cast.deckToGalley, cast.jacquesPots.position);
            Chain("hành lang→ông Mai", cast.corridorToGalley, cast.maiSpot.position);
            legs.Add(("nồi bão→nồi cao", cast.jacquesPots.position, cast.jacquesTallPot.position));
            var path = new NavMeshPath();
            var broken = new List<string>();
            foreach (var (name, from, to) in legs)
            {
                bool ok = NavMesh.SamplePosition(from, out var a, 0.6f, NavMesh.AllAreas) && NavMesh.SamplePosition(to, out var b, 0.6f, NavMesh.AllAreas)
                          && NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                if (!ok) broken.Add($"{name}: {from} → {to}");
            }
            Debug.Log($"DAYCAST_NAV {legs.Count} chặng, hỏng {broken.Count}: {string.Join("; ", broken)}");
            Assert.IsEmpty(broken, "mọi chặng của người theo ngày có đường NavMesh trọn vẹn (cửa bếp không chặn lối)");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator JacquesHoldsTheStormPotByItsHandlesUntilBaTiesIt()
        {
            yield return ChoreTestUtil.Galley();
            var cast = Object.FindAnyObjectByType<DayCast>();
            var hands = cast.JacquesHands;
            Assert.IsNotNull(hands, "Jacques có tay IK");
            Assert.AreEqual(3, cast.stormPots.Length);
            foreach (var p in cast.stormPots) p.Secure(false);
            var jacques = cast.jacques;
            jacques.gameObject.SetActive(true);
            jacques.Place(cast.jacquesPots.position, cast.jacquesPots.eulerAngles.y);
            var motion = jacques.gameObject.AddComponent<MotionProbe>();
            motion.hands = hands;
            motion.WatchWrists();
            cast.StartCoroutine(cast.HoldStormPots());
            yield return PlayTestUtil.WaitUntil(() => cast.HeldPot != null && hands.Activity == "hold" && hands.arms.leftWeight > 0.99f, 30f);
            var first = cast.HeldPot;
            Assert.IsNotNull(first, "Jacques nắm một nồi rời");
            Assert.IsTrue(first.Held, "nồi đang được giữ thì đứng yên");
            float worst = 0f;
            bool crossed = false;
            int samples = 0;
            for (float t = 0f; t < 1.5f; t += Time.deltaTime)
            {
                yield return null;
                var handles = first.GetComponentsInChildren<Transform>().Where(x => x.name == "Handle").Select(x => x.position).ToArray();
                Assert.AreEqual(2, handles.Length);
                foreach (bool left in new[] { true, false })
                {
                    var wrist = hands.Wrist(left).position;
                    var nearest = handles.OrderBy(h => Vector3.Distance(h, wrist)).First();
                    worst = Mathf.Max(worst, Vector3.Distance(nearest, wrist));
                    float side = Vector3.Dot(nearest - first.transform.position, jacques.transform.right);
                    if (left ? side > 0f : side < 0f) crossed = true;
                }
                samples++;
            }
            Debug.Log($"KITCHEN_HANDS jacques storm: {samples} khung, cổ tay–quai xa nhất {worst * 100f:F1} cm, chéo tay {crossed}");
            Assert.LessOrEqual(worst, 0.13f, "hai tay ở hai quai nồi (cổ tay cách quai một lòng bàn tay)");
            Assert.IsFalse(crossed, "tay trái nắm quai bên trái, tay phải quai bên phải");

            // Ba chằng nồi đó: Jacques buông, sang nồi kế tiếp; chằng hết thì đứng trông.
            first.Secure(true);
            yield return PlayTestUtil.WaitUntil(() => cast.HeldPot != null && cast.HeldPot != first && hands.Activity == "hold", 30f);
            Assert.AreNotEqual(first, cast.HeldPot, "chằng xong nồi đó thì Jacques sang giữ nồi khác");
            foreach (var p in cast.stormPots) p.Secure(true);
            yield return PlayTestUtil.WaitUntil(() => cast.HeldPot == null && hands.Activity == "", 30f);
            Assert.IsNull(cast.HeldPot);
            Assert.Less(hands.arms.leftWeight + hands.arms.rightWeight, 0.01f, "hết nồi thì buông tay");
            Debug.Log("KITCHEN_MOTION jacques storm: " + motion.Report);
            Assert.Less(motion.Worst, MotionProbe.PopStep, "tay Jacques không giật/nhảy chỗ (vươn, giữ, buông, sang nồi khác): " + motion.Report);
            Assert.Less(motion.WorstWalkAngle, KitchenHandsPlayTests.MaxWalkAngle, "đi về phía trước, không giật lùi hay đi ngang: " + motion.Report);
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator JacquesSteadiesTheTallPotWithBothPalms()
        {
            yield return ChoreTestUtil.Galley();
            var cast = Object.FindAnyObjectByType<DayCast>();
            var hands = cast.JacquesHands;
            var pot = Object.FindAnyObjectByType<TallPotStation>().potRoot;
            pot.gameObject.SetActive(true);
            var jacques = cast.jacques;
            jacques.gameObject.SetActive(true);
            jacques.Place(cast.jacquesTallPot.position, cast.jacquesTallPot.eulerAngles.y);
            var motion = jacques.gameObject.AddComponent<MotionProbe>();
            motion.hands = hands;
            motion.WatchWrists();
            cast.StartCoroutine(cast.HoldTallPot());
            yield return PlayTestUtil.WaitUntil(() => hands.Activity == "hold" && hands.arms.leftWeight > 0.99f, 30f);
            Assert.AreEqual("hold", hands.Activity);
            var b = NpcHandWork.LocalBounds(pot);
            var top = pot.TransformPoint(new Vector3(b.center.x, b.max.y, b.center.z));
            float radius = Mathf.Min(b.extents.x * pot.lossyScale.x, b.extents.z * pot.lossyScale.z);
            float worstGrip = 0f, worstSide = 0f, lowest = float.MaxValue, highest = float.MinValue;
            bool crossed = false;
            for (float t = 0f; t < 1f; t += Time.deltaTime)
            {
                yield return null;
                foreach (bool left in new[] { true, false })
                {
                    var wrist = hands.Wrist(left).position;
                    worstGrip = Mathf.Max(worstGrip, Vector3.Distance(wrist, hands.Grip(left).position));
                    // Cổ tay sát thân nồi (lòng bàn tay áp thân), ở nửa trên nồi, không giơ giữa không trung.
                    worstSide = Mathf.Max(worstSide, Mathf.Abs(Flat(wrist, top) - radius));
                    lowest = Mathf.Min(lowest, wrist.y);
                    highest = Mathf.Max(highest, wrist.y);
                    float side = Vector3.Dot(wrist - jacques.Position, jacques.transform.right);
                    if (left ? side > 0f : side < 0f) crossed = true;
                }
            }
            Debug.Log($"KITCHEN_HANDS jacques tall pot: cổ tay–chỗ cầm {worstGrip * 100f:F1} cm, cổ tay–thân nồi {worstSide * 100f:F1} cm, " +
                      $"cổ tay cao {lowest:F2}–{highest:F2} m (đỉnh nồi {top.y:F2}), chéo tay {crossed}");
            Assert.LessOrEqual(worstGrip, 0.04f, "IK với tới chỗ áp tay");
            Assert.LessOrEqual(worstSide, 0.1f, "hai lòng bàn tay áp vào thân nồi cao");
            Assert.That(lowest, Is.GreaterThan(top.y - 0.45f), "tay ở nửa trên thân nồi");
            Assert.That(highest, Is.LessThan(top.y), "tay không giơ cao hơn đỉnh nồi");
            Assert.IsFalse(crossed, "tay trái bên trái, tay phải bên phải");
            Debug.Log("KITCHEN_MOTION jacques tall pot: " + motion.Report);
            Assert.Less(motion.Worst, MotionProbe.PopStep, "tay Jacques không giật/nhảy chỗ khi vươn tới, áp tay vào nồi cao: " + motion.Report);
            Assert.Less(motion.WorstWalkAngle, KitchenHandsPlayTests.MaxWalkAngle, "đi về phía trước, không giật lùi hay đi ngang: " + motion.Report);
        }
    }
}
