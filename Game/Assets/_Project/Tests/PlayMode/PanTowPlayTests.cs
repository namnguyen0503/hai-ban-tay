using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Hồi quy bot playthrough Ngày 2: chảo từ phòng rửa kéo tới gần lò thì kẹt cứng vào thùng lá úa cạnh bàn sơ chế,
    /// người chơi đi lùi mãi chảo không nhích. Chảo phải đi tiếp qua chỗ đó (thùng dời đi, chảo trượt dọc vật cản).
    /// </summary>
    public class PanTowPlayTests
    {
        [TearDown]
        public void TearDown()
        {
            var root = PlayTestUtil.Root();
            root.Input.SimMove = Vector2.zero;
            root.Input.SimUse = false;
            Time.captureDeltaTime = 0f;
            PlayTestUtil.ExitSim();
        }

        [UnityTest]
        public IEnumerator PanPassesThePrepTableCornerTowardTheStove() => Tow(towardStop: false);

        /// <summary>Kéo thẳng về vòng phấn trước lò: bếp trưởng, phụ bếp không đứng chắn vòng phấn, chảo được giao.</summary>
        [UnityTest]
        public IEnumerator PanReachesTheChalkRingInFrontOfTheStove() => Tow(towardStop: true);

        /// <summary>
        /// Chảo thấp (mép 0,21 m) nằm dưới stepOffset 0,3 của CharacterController: Ba đi thẳng vào chảo thì leo lên, camera nhô
        /// 0,24 m (bot ngày 3 quan sát y = 0,24 mỗi lần kéo chảo). Hộp va chạm của chảo phải chặn người đi tới.
        /// </summary>
        [UnityTest]
        public IEnumerator PlayerCannotWalkOntoThePan()
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            PanStation station = null;
            yield return PlayTestUtil.WaitUntil(() => (station = Object.FindAnyObjectByType<PanStation>()) != null, 10f);
            var input = PlayTestUtil.Root().Input;
            yield return PlayTestUtil.WaitUntil(() => input.GameplayEnabled, 30f);
            Assert.IsTrue(input.GameplayEnabled, "người chơi phải điều khiển được");

            var spot = station.stops[0].position;
            station.pan.transform.position = spot;
            station.pan.position = spot;
            var rig = PlayerRig.Current;
            Time.captureDeltaTime = 1f / 60f;
            float top = 0f, nearest = float.MaxValue;
            // Đi thẳng vào tâm từ tây và đông, rồi từ nam lệch 0,35 m để góc hộp chạm capsule (leo qua mép hộp). Không đi từ phía bắc: cách chảo
            // 1,6 m về phía đó là vách bắc bếp (Wall_N), người bị xô ra ngoài vỏ tàu rồi rơi. Hướng lệch tây và chéo đông nam gặp thùng lá úa
            // và phụ bếp đứng trên lối (QA-0125), không phải hộp chảo.
            foreach (var (yawDeg, offset) in new[] { (90f, 0f), (270f, 0f), (0f, 0.35f) })
            {
                var dir = Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward;
                var side = Quaternion.Euler(0f, yawDeg, 0f) * Vector3.right * offset;
                var feet = spot - dir * 1.6f + side;
                feet.y = rig.Position.y;
                rig.motor.Teleport(feet, yawDeg);
                rig.look.SetAngles(yawDeg, 0f);
                yield return null;
                float baseY = rig.Position.y;
                input.SimMove = new Vector2(0f, 1f);
                for (float t = 0f; t < 3f; t += Time.deltaTime)
                {
                    yield return null;
                    top = Mathf.Max(top, rig.Position.y - baseY);
                    var gap = rig.Position - station.pan.position; gap.y = 0f;
                    nearest = Mathf.Min(nearest, gap.magnitude);
                }
                input.SimMove = Vector2.zero;
            }
            Assert.Less(top, 0.1f, $"Ba leo lên chảo: cao thêm {top:0.00} m, gần nhất {nearest:0.00} m");
            Assert.GreaterOrEqual(nearest, 0.6f, "người dừng ở mép chảo, không vào trong hộp va chạm");
        }

        /// <summary>
        /// Hộp va chạm chảo (đỉnh 0,42 m) quét từ góc phòng rửa theo đường kéo ra cửa tây (z 0,3) không được vướng vật nào:
        /// bot ngày 3 thấy chảo kẹt cứng ở x 5,6 ngay sau khi nâng hộp lên.
        /// </summary>
        [UnityTest]
        public IEnumerator PanBoxSweepsFromTheWashCornerOutTheDoor()
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            PanStation station = null;
            yield return PlayTestUtil.WaitUntil(() => (station = Object.FindAnyObjectByType<PanStation>()) != null, 10f);
            var env = station.pan.transform.parent;
            var start = env.TransformPoint(new Vector3(6.27f, 0f, 0.3f));
            station.pan.transform.position = start;
            station.pan.position = start;
            yield return null;
            var hits = new System.Collections.Generic.List<string>();
            var dir = env.TransformDirection(Vector3.left);
            foreach (var h in station.pan.SweepTestAll(dir, 3.2f, QueryTriggerInteraction.Ignore))
                if (h.collider.GetComponentInParent<HaiBanTay.Npc.NpcAgent>() == null && h.collider.GetComponentInParent<PlayerRig>() == null)
                    hits.Add($"{h.collider.name} (cha {h.collider.transform.parent?.name}) cách {h.distance:0.00} m, cao {h.collider.bounds.min.y:0.00}…{h.collider.bounds.max.y:0.00}");
            Assert.IsEmpty(hits, "hộp chảo vướng: " + string.Join("; ", hits));
        }

        IEnumerator Tow(bool towardStop)
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            PanStation station = null;
            yield return PlayTestUtil.WaitUntil(() => (station = Object.FindAnyObjectByType<PanStation>()) != null, 10f);
            Assert.IsNotNull(station);
            var input = PlayTestUtil.Root().Input;
            yield return PlayTestUtil.WaitUntil(() => input.GameplayEnabled, 30f);
            Assert.IsTrue(input.GameplayEnabled, "người chơi phải điều khiển được");

            // Chảo nằm đúng chỗ bot bị kẹt (tọa độ trong env), lệnh kéo về trước lò đang chờ.
            var env = station.pan.transform.parent;
            var start = env.TransformPoint(new Vector3(-0.76f, 0f, 0.94f));
            station.pan.transform.position = start;
            station.pan.position = start;
            station.Activate();
            station.OnOrder(1);
            Assert.AreEqual(0, station.Next, "lệnh đầu kéo chảo tới trước lò");

            // Đứng phía tây chảo, nhìn vào tay cầm, giữ chuột trái và đi lùi về phía tây như bot.
            var rig = PlayerRig.Current;
            var handle = station.handle.transform.position;
            var away = towardStop ? (station.stops[0].position - start) : env.TransformDirection(Vector3.left);
            away.y = 0f;
            var feet = start + away.normalized * 0.9f;
            feet.y = rig.Position.y;
            var to = handle - (feet + Vector3.up * 1.62f);
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            rig.motor.Teleport(feet, yaw);
            rig.look.SetAngles(yaw, pitch);
            yield return PlayTestUtil.WaitUntil(() => rig.interactor.IsLookingAt(station.handle), 5f);
            input.SimUse = true;
            yield return PlayTestUtil.WaitUntil(() => station.Tow.Gripping, 5f);
            Assert.IsTrue(station.Tow.Gripping, $"bám được chảo (nhìn trúng {rig.interactor.LookCollider?.name}, tay rảnh {rig.interactor.HandsFree}, người {env.InverseTransformPoint(rig.Position)}, chảo {env.InverseTransformPoint(station.pan.position)})");

            // Batchmode chạy frame cực ngắn: bước di chuyển nhỏ hơn minMoveDistance của CharacterController. Cố định 60 khung/giây game.
            Time.captureDeltaTime = 1f / 60f;
            input.SimMove = new Vector2(0f, -1f);
            float Progress() => env.InverseTransformPoint(station.pan.position).x;
            if (towardStop)
            {
                yield return PlayTestUtil.WaitUntil(() => station.Delivered > 0 || !station.Tow.Gripping, 90f);
                input.SimMove = Vector2.zero;
                Assert.AreEqual(1, station.Delivered,
                    $"chảo phải tới vòng phấn, đang ở {env.InverseTransformPoint(station.pan.position)} (bám: {station.Tow.Gripping})");
                // Giao chảo không được đặt trùm lên chân người kéo: Ba bước lên mép chảo, camera nhô 0,24 m (bot ngày 3, 18/10).
                // PanStation.MakeRoom đẩy Ba lùi khỏi hộp chảo trong 0,25 s.
                yield return PlayTestUtil.WaitUntil(() => false, 0.8f);
                var gap = rig.Position - station.pan.position; gap.y = 0f;
                Assert.GreaterOrEqual(gap.magnitude, 0.68f, "chảo đặt xuống cách chân người kéo ít nhất nửa hộp chảo + bán kính người");
                Assert.Less(rig.Position.y - feet.y, 0.1f, "Ba không đứng trên chảo sau khi giao");
                yield break;
            }
            yield return PlayTestUtil.WaitUntil(() => Progress() < -1.3f || !station.Tow.Gripping, 90f);
            input.SimMove = Vector2.zero;
            var near = new System.Collections.Generic.List<string>();
            foreach (var c in Physics.OverlapSphere(rig.Position + Vector3.up * 0.9f, 0.9f, ~0, QueryTriggerInteraction.Ignore))
                near.Add($"{c.name}@{env.InverseTransformPoint(c.transform.position)}");
            TestContext.WriteLine("Gần người chơi: " + string.Join(", ", near));
            TestContext.WriteLine($"gameplay={input.GameplayEnabled} move={input.Move} locked={rig.motor.MovementLocked} " +
                                  $"cap={rig.motor.SpeedCap} dt={Time.deltaTime} t={Time.time} scale={Time.timeScale} card={PlayTestUtil.Root().UI.Cards}");
            // Chảo bắt đầu cách vòng phấn trước lò 0,8 m nên có thể tới vòng ngay (giao chảo xong thì đứng yên ở vòng).
            Assert.IsTrue(Progress() < -1.3f || station.Delivered > 0,
                $"chảo phải qua góc bàn sơ chế, đang ở x={Progress():0.00}, người ở {env.InverseTransformPoint(rig.Position)} (bám: {station.Tow.Gripping})");
        }
    }
}
