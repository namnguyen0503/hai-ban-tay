using System.Collections;
using System.Linq;
using HaiBanTay.Interaction;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Hồi quy góc nhìn thứ nhất (ghi chú 15/10 chơi trên điện thoại 2760×1256): ôm sọt than (đầy và rỗng) cúi nhìn xuống, ngồi dốc bao
    /// khoai thấy lỗ cổ áo rỗng; đổ xô tro, đổ sọt than tay bám vật nhưng cánh tay không với tới (cổ tay xương cách bàn tay 20–69 cm).
    /// Đo bằng PlayerViewProbe phần hình học (chạy được -nographics): vòng cổ không vào khung cả 16:9 lẫn tỉ lệ điện thoại, cổ tay xương
    /// cách bàn tay góc nhìn thứ nhất không quá WristGapLimit.
    /// </summary>
    public class PlayerViewPlayTests
    {
        const float WristGapLimit = 0.035f;
        // Đồ đang cầm lún vào thân/đùi/bắp chân/tạp dề (PlayerViewProbe.ItemInBody, ngưỡng của bộ dò).
        static readonly float ItemLimit = PlayerViewProbe.Limit(PlayerViewProbe.Kind.ItemInBody);

        [SetUp] public void SetUp() => Time.captureDeltaTime = 1f / 60f;

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            PlayTestUtil.ExitSim();
        }

        static PlayerRig Rig => PlayerRig.Current;

        static IEnumerator Settle(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
        }

        static void Hold(Holdable h)
        {
            var who = Rig.interactor;
            if (who.Holding != null) who.ReleaseHeld();
            h.gameObject.SetActive(true);
            h.Locked = false;
            who.Pick(h);
            Assert.AreEqual(h, who.Holding, h.name);
        }

        static ShiftDirector Quiet()
        {
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            d.meals.cook.enabled = false;
            foreach (var npc in Object.FindObjectsByType<HaiBanTay.Npc.NpcAgent>(FindObjectsSortMode.None)) npc.gameObject.SetActive(false);
            return d;
        }

        [UnityTest]
        public IEnumerator ServingTrayUsesRimGripsAndKeepsWristsBelowSixtyDegrees()
        {
            yield return ChoreTestUtil.Galley();
            var d = Quiet();
            var tray = d.storyStations.OfType<VoyageDelivery>().Single(s => s.orderKind == "serve").load;
            Hold(tray);
            var probe = new PlayerViewProbe(Rig);
            float worst = 0f;
            foreach (float pitch in new[] { -30f, 0f, 45f, 60f, 75f, 85f })
            {
                Rig.motor.Teleport(new Vector3(-1.5f, 0f, 0.6f), 0f);
                Rig.look.SetAngles(0f, pitch);
                yield return Settle(0.6f);
                worst = Mathf.Max(worst, probe.WristBend(true), probe.WristBend(false));
            }
            Debug.Log($"PLAYER_VIEW serve: cổ tay {worst:F2}°; grip trái {tray.leftGrip != null}, phải {tray.rightGrip != null}");
            Assert.LessOrEqual(worst, 60f, "khay phục vụ phải có biên an toàn cổ tay");
            Assert.IsNotNull(tray.leftGrip, "tay trái bám mép khay thật");
            Assert.IsNotNull(tray.rightGrip, "tay phải bám mép khay thật");
        }

        [UnityTest]
        public IEnumerator HangingIceAndWaterKeepBothArmsDownWhenLookingAtEightyFiveDegrees()
        {
            yield return ChoreTestUtil.Galley();
            var d = Quiet();
            var probe = new PlayerViewProbe(Rig);
            foreach (var task in d.storyStations.OfType<VoyageDelivery>().Where(s => s.orderKind == "ice" || s.orderKind == "water"))
            {
                Hold(task.load);
                Rig.motor.Teleport(new Vector3(-1.5f, 0f, 0.6f), 0f);
                Rig.look.SetAngles(0f, 85f);
                yield return Settle(1f);
                float angle = Mathf.Max(probe.HangingArmAngle(true), probe.HangingArmAngle(false));
                float gap = Mathf.Max(probe.WristGap(true), probe.WristGap(false));
                float into = probe.ItemInBody(out string where);
                Debug.Log($"PLAYER_VIEW hang {task.orderKind}: tay {angle:F2}°, cổ tay {gap * 100f:F2} cm, lún {into * 100f:F2} cm ({where})");
                Assert.LessOrEqual(angle, PlayerViewProbe.Limit(PlayerViewProbe.Kind.HangingArmAngle), task.orderKind + ": tay xách buông tự nhiên bên hông");
                Assert.LessOrEqual(gap, WristGapLimit, task.orderKind);
                Assert.LessOrEqual(into, ItemLimit, task.orderKind + ": " + where);
                Rig.interactor.ReleaseHeld();
                task.load.Detach(task.home.position, task.home.rotation, false);
                task.load.gameObject.SetActive(false);
            }
        }

        IEnumerator LookDownAt(PlayerViewProbe probe, string label, params float[] pitches)
        {
            foreach (float pitch in pitches)
            {
                Rig.motor.Teleport(new Vector3(-1.5f, 0f, 0.6f), 0f);
                Rig.look.SetAngles(0f, pitch);
                yield return Settle(0.6f);
                Assert.AreEqual(0, probe.NeckInFrame(), $"{label}, cúi {pitch}°: vòng cổ áo (đầu ẩn) lọt vào khung");
                float into = probe.ItemInBody(out string where);
                Assert.LessOrEqual(into, ItemLimit, $"{label}, cúi {pitch}°: {where} lún {into * 100f:F1} cm");
                float gap = Mathf.Max(probe.WristGap(true), probe.WristGap(false));
                Assert.LessOrEqual(gap, WristGapLimit, $"{label}, cúi {pitch}°: cổ tay xương cách bàn tay {gap * 100f:F1} cm");
            }
        }

        // Ghi chú 15/10: "bụng xuyên xô". Mỗi loại đồ cầm được ở bếp (ôm, xách, vác, cầm một tay): ngẩng tới cúi, đồ không lún vào thân.
        [UnityTest]
        public IEnumerator EveryHeldItemStaysOutOfTheBodyFromLookingUpToLookingDown()
        {
            yield return ChoreTestUtil.Galley();
            var d = Quiet();
            var probe = new PlayerViewProbe(Rig);
            Assert.GreaterOrEqual(probe.BodyParts, 6, "dựng được vỏ thân, đùi, bắp chân");
            Assert.Greater(Rig.body.ShellPoints, 40, "thân người chơi có vỏ để giữ đồ ra ngoài");
            var seen = new System.Collections.Generic.HashSet<string>();
            int count = 0;
            foreach (var h in Object.FindObjectsByType<Holdable>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            {
                if (h.gameObject.scene != d.gameObject.scene) continue;
                string kind = $"{h.kind}{(h.hang ? "_xach" : h.ShoulderCarried ? "_vai" : h.twoHanded ? "_om" : "")}";
                if (!seen.Add(kind)) continue;
                Hold(h);
                yield return LookDownAt(probe, $"cầm {h.name} ({kind})", -30f, 0f, 45f, 60f, 75f, 85f);
                Rig.interactor.ReleaseHeld();
                h.Detach(h.transform.position, Quaternion.identity, physics: false);
                h.gameObject.SetActive(false);
                count++;
            }
            Assert.GreaterOrEqual(count, 6, "đủ các loại đồ cầm");
        }

        [UnityTest]
        public IEnumerator HoldingTheCoalBasketFullOrEmptyKeepsTheCollarOutOfFrame()
        {
            yield return ChoreTestUtil.Galley();
            var d = Quiet();
            var probe = new PlayerViewProbe(Rig);
            var coal = d.coal;
            var basket = coal.baskets[0];
            Hold(basket);
            Assert.IsFalse(coal.IsEmptyBasket(basket));
            yield return LookDownAt(probe, "ôm sọt đầy", 45f, 60f, 75f, 85f);

            // Đổ vào thùng: cả lúc nhấc, lật, đưa về, cổ tay bám sọt và cổ áo ngoài khung.
            coal.SetStock(0);
            ChoreTestUtil.Look(new Vector3(0.3f, 0f, 1.45f), coal.bin.transform.position + Vector3.up * 0.5f);
            yield return Settle(0.4f);
            coal.OnInteract(coal.bin, Rig.interactor);
            yield return Pour(probe, "đổ sọt than", () => coal.PouringBasket);
            Assert.IsTrue(coal.IsEmptyBasket(basket), "đổ hết than");
            Assert.AreEqual(basket, Rig.interactor.Holding, "đổ xong sọt rỗng còn trên tay");
            yield return LookDownAt(probe, "ôm sọt rỗng", 45f, 60f, 75f, 85f);
        }

        [UnityTest]
        public IEnumerator PouringTheAshBucketKeepsBothHandsOnItAndTheCollarOutOfFrame()
        {
            yield return ChoreTestUtil.Galley();
            var d = Quiet();
            var probe = new PlayerViewProbe(Rig);
            var ash = d.ash;
            ash.bucket.gameObject.SetActive(true);
            ash.Rake.ForceFull();
            Hold(ash.bucket);
            var chute = ash.chute.transform;
            var mouth = chute.TransformPoint(new Vector3(0.2f, 0.98f, 0f));
            var stand = mouth + new Vector3(1.0f, 0f, 0.15f);
            stand.y = chute.position.y;
            ChoreTestUtil.Look(stand, mouth);
            yield return Settle(0.4f);
            Assert.IsTrue(Rig.hands.Hanging, "xách xô bằng quai");
            ash.OnInteract(ash.chute, Rig.interactor);
            yield return Pour(probe, "đổ xô tro", () => ash.bucket.Busy);
            Assert.AreEqual(ash.bucket, Rig.interactor.Holding, "đổ xong xô còn trên tay");
            Assert.IsTrue(Rig.hands.Hanging, "đổ xong tay phải lại xách quai");
        }

        [UnityTest]
        public IEnumerator TippingThePotatoSackKeepsTheCollarOutOfFrame()
        {
            yield return ChoreTestUtil.Galley();
            var d = Quiet();
            var probe = new PlayerViewProbe(Rig);
            var sacks = d.sacks;
            ChoreTestUtil.Look(new Vector3(1.32f, 0f, -1.05f), sacks.mouth.transform.position);
            sacks.sacks[0].gameObject.SetActive(true);
            sacks.zone.Place(sacks.sacks[0]);
            sacks.OnInteract(sacks.mouth, Rig.interactor);
            yield return Settle(0.4f);
            float yaw = Rig.look.Yaw;
            foreach (float pitch in new[] { 30f, 55f, 70f, 85f })
            {
                Rig.look.SetAngles(yaw, pitch);
                yield return Settle(0.6f);
                Assert.AreEqual(0, probe.NeckInFrame(), $"ngồi dốc bao, cúi {pitch}°: vòng cổ áo lọt vào khung");
            }
        }

        IEnumerator Pour(PlayerViewProbe probe, string label, System.Func<bool> busy)
        {
            yield return PlayTestUtil.WaitUntil(busy, 2f);
            Assert.IsTrue(busy(), label + ": chưa bắt đầu");
            float start = Time.time, worstGap = 0f, worstInto = 0f, worstBend = 0f, worstContact = 0f, maxTilt = 0f;
            int worstNeck = 0;
            string when = "", intoAt = "", contactAt = "";
            var lastRot = Rig.interactor.Holding != null ? Rig.interactor.Holding.transform.rotation : Quaternion.identity;
            while (busy() && Time.time - start < 8f)
            {
                yield return null;
                int neck = probe.NeckInFrame();
                float gap = Mathf.Max(probe.WristGap(true), probe.WristGap(false));
                if (gap > worstGap) { worstGap = gap; when = $"{Time.time - start:F2} s"; }
                worstNeck = Mathf.Max(worstNeck, neck);
                float into = probe.ItemInBody(out string where);
                if (into > worstInto) { worstInto = into; intoAt = $"{where} lúc {Time.time - start:F2} s"; }
                worstBend = Mathf.Max(worstBend, probe.WristBend(true), probe.WristBend(false));
                var held = Rig.interactor.Holding;
                float tilt = held != null ? Vector3.Angle(held.transform.up, Vector3.up) : 0f;
                maxTilt = Mathf.Max(maxTilt, tilt);
                // Vật đã nghiêng đổ và đứng yên (giữ ở thế đổ): hai lòng bàn tay phải nằm đúng điểm nắm. Lúc nhấc lên và đưa về, tay có
                // pha trượt có chủ ý từ quai sang vành (PourMotion.Slide); lúc vật đang lật nhanh, mẫu đo lấy giữa lượt vật đã dời và
                // lượt tay bám theo trong cùng khung (lệch một khung, không thấy khi vẽ).
                bool still = held != null && Quaternion.Angle(lastRot, held.transform.rotation) < 1f;
                if (held != null) lastRot = held.transform.rotation;
                if (tilt > 30f && still)
                {
                    float contact = Mathf.Max(probe.GripContactGap(true), probe.GripContactGap(false));
                    if (contact > worstContact)
                    {
                        worstContact = contact;
                        var hands = Rig.hands;
                        contactAt = $"{Time.time - start:F2} s, nghiêng {tilt:F0}°, trái {probe.GripContactGap(true) * 100f:F1} cm ({hands.Pose}, " +
                            $"{hands.GripAnchor(true)?.name}), phải {probe.GripContactGap(false) * 100f:F1} cm ({hands.RightMeshPose}, " +
                            $"{hands.GripAnchor(false)?.name}), gập {probe.WristBend(true):F0}/{probe.WristBend(false):F0}°, " +
                            $"vai→nắm {Vector3.Distance(Rig.body.Shoulder(true).position, hands.GripAnchor(true) != null ? hands.GripAnchor(true).position : Vector3.zero):F2}/" +
                            $"{Vector3.Distance(Rig.body.Shoulder(false).position, hands.GripAnchor(false) != null ? hands.GripAnchor(false).position : Vector3.zero):F2} m, " +
                            $"tay {Rig.body.ArmLength(true):F2} m";
                    }
                }
            }
            Assert.IsFalse(busy(), label + ": quá 8 s");
            Assert.AreEqual(0, worstNeck, $"{label}: vòng cổ áo lọt vào khung");
            Assert.LessOrEqual(worstGap, WristGapLimit, $"{label}: cổ tay xương cách bàn tay {worstGap * 100f:F1} cm lúc {when}");
            Assert.LessOrEqual(worstInto, ItemLimit, $"{label}: đồ lún vào thân {worstInto * 100f:F1} cm ({intoAt})");
            Debug.Log($"PLAYER_VIEW {label}: cổ tay {worstBend:F2}°, hở {worstGap * 100f:F2} cm, lún {worstInto * 100f:F2} cm");
            Assert.LessOrEqual(worstBend, 60f, label + ": giữ biên an toàn cổ tay suốt lúc nhấc, lật 180°, đưa về");
            Assert.LessOrEqual(worstContact, PlayerViewProbe.Limit(PlayerViewProbe.Kind.GripContactGap), $"{label}: lòng bàn tay bám điểm nắm suốt chuyển động (lệch {worstContact * 100f:F1} cm lúc {contactAt})");
            Assert.GreaterOrEqual(maxTilt, PourMotion.TwoHandTilt - 0.1f, label + ": đo đủ nghiêng đổ hai tay (PourMotion.TwoHandTilt)");
        }
    }
}
