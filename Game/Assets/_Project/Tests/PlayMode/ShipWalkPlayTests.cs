using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Narrative;
using HaiBanTay.Performance;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Ship;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Đi bộ thật trên con tàu M10b (CharacterController, input giả lập, 60 khung/giây game): lên xuống mọi cầu thang và
    /// lối đồng mức theo deck_zones.json; vác bao khoai từ hầm thực phẩm lên bếp (tàu êm và tàu lắc 0,6); từ kè Nhà Rồng
    /// lên cầu thang tàu; ngân sách đèn trong và ngoài bếp.
    /// </summary>
    public class ShipWalkPlayTests
    {
        /// <summary>Tốc độ mang đồ 1,3 m/s; cho dư gấp ba lần quãng đường.</summary>
        const float SlowestSpeed = 1.3f;

        GameInput Input => GameRoot.Instance.Input;
        static PlayerRig Rig => PlayerRig.Current;
        string _failure;

        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = 1f / 60f;
        }

        [TearDown]
        public void TearDown()
        {
            var root = GameRoot.Instance;
            if (root != null)
            {
                root.Input.SimMove = Vector2.zero;
                root.Input.SimLook = Vector2.zero;
            }
            Time.captureDeltaTime = 0f;
            PlayTestUtil.ExitSim();
        }

        /// <summary>Nạp 02, chờ điều khiển được, tắt NPC trong bếp (bài này chỉ kiểm hình học lối đi).</summary>
        IEnumerator LoadGalley()
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            yield return PlayTestUtil.WaitUntil(() => Input.GameplayEnabled, 60f);
            Assert.IsTrue(Input.GameplayEnabled, "người chơi phải điều khiển được");
            var director = Object.FindAnyObjectByType<ShiftDirector>();
            foreach (var go in director.dayOnly)
                if (go != null)
                    go.SetActive(false);
        }

        [UnityTest]
        public IEnumerator ShoulderSackKeepsViewClearAndBlendsPickupAndPlacement()
        {
            yield return LoadGalley();
            var director = Object.FindAnyObjectByType<ShiftDirector>();
            var sack = director.sacks.sacks[0];
            Rig.motor.Teleport(new Vector3(-2.6f, 0f, 1.4f), 0f);
            Rig.look.SetAngles(0f, 0f);
            yield return null;
            var originalInterpolation = sack.Body.interpolation;
            Rig.interactor.Pick(sack);
            Assert.AreEqual(RigidbodyInterpolation.None, sack.Body.interpolation, "Bao theo camera không được nội suy pose vật lý cũ");
            var start = sack.transform.localPosition;
            yield return null;
            Assert.Greater(sack.transform.localPosition.sqrMagnitude, 0.001f, "Nhặt không được nhảy ngay lên vai");
            Assert.Less(Vector3.Distance(sack.transform.localPosition, Vector3.zero), start.magnitude);
            yield return new WaitForSeconds(0.3f);
            Assert.Less(sack.transform.localPosition.sqrMagnitude, 0.000001f, "Nhặt hoàn tất sau 0,25 giây");
            Assert.Less(Quaternion.Angle(sack.transform.localRotation, Quaternion.identity), 0.01f);
            foreach (float tilt in new[] { -22f, 0f, 22f })
                foreach (float pitch in new[] { -35f, 0f, 35f })
                {
                    Rig.look.SetAngles(0f, pitch);
                    Rig.interactor.holdTwoHands.localRotation = Quaternion.Euler(0f, 0f, tilt);
                    yield return null;
                    TestContext.WriteLine($"Tư thế: nghiêng={tilt}, nhìn={pitch}, aspect={Rig.cam.aspect}, FOV={Rig.cam.fieldOfView}, bao local={sack.transform.localPosition}, điểm vai trong camera={Rig.cam.transform.InverseTransformPoint(sack.transform.position)}");
                    Assert.Less(sack.transform.localPosition.sqrMagnitude, 0.000001f, "Bao phải bám vai khi camera đổi góc");
                    AssertShoulderView(Rig.cam, sack);
                }
            var visual = sack.GetComponentInChildren<Renderer>().transform;
            var before = visual.position;
            var originalLocal = visual.localPosition;
            Rig.interactor.ReleaseHeld();
            var placed = Rig.Position + Vector3.forward;
            sack.Detach(placed, Quaternion.identity, physics: false);
            Assert.AreEqual(originalInterpolation, sack.Body.interpolation, "Thả khôi phục chế độ nội suy ban đầu");
            Assert.Less(Vector3.Distance(sack.transform.position, placed), 0.001f, "Luật đặt giữ nguyên, chỉ mesh nội suy");
            Assert.Less(Vector3.Distance(visual.position, before), 0.001f, "Đặt không được giật mesh khỏi vai");
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(Vector3.Distance(visual.position, before), 0.1f);
            Assert.Less(Vector3.Distance(originalLocal, visual.localPosition), 0.0001f, "Mesh trở về gốc sau chuyển tiếp");
            Rig.interactor.holdTwoHands.localRotation = Quaternion.identity;
        }

        internal static void AssertShoulderView(Camera cam, Holdable sack)
        {
            var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            foreach (var renderer in sack.GetComponentsInChildren<Renderer>())
            {
                Assert.Greater(cam.WorldToViewportPoint(renderer.bounds.center).x, 0.55f);
                Assert.Less(cam.WorldToViewportPoint(renderer.bounds.center).x, 1f,
                    $"Bao phải còn thấy trong khung hình: {renderer.name}, viewport={cam.WorldToViewportPoint(renderer.bounds.center)}, camera local={cam.transform.InverseTransformPoint(renderer.bounds.center)}, aspect={cam.aspect}, FOV={cam.fieldOfView}");
                Assert.IsFalse(renderer.bounds.IntersectRay(ray), "Renderer bao không được che tâm màn hình");
            }
        }

        /// <summary>
        /// Đi thẳng tới điểm (theo mặt phẳng ngang), mỗi khung quay người về phía điểm. Kẹt (gần như không nhích trong
        /// 1,5 s) hoặc hết giờ thì ghi lỗi.
        /// </summary>
        IEnumerator WalkTo(Vector3 target, string label, float radius = 0.25f, System.Action everyFrame = null)
        {
            if (_failure != null)
                yield break;
            float distance = Flat(target - Rig.Position).magnitude;
            float limit = distance / SlowestSpeed * 3f + 3f;
            float time = 0f;
            float stuck = 0f;
            var last = Rig.Position;
            while (Flat(target - Rig.Position).magnitude > radius)
            {
                var to = Flat(target - Rig.Position);
                float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                Rig.look.SetAngles(yaw, 0f);
                Input.SimMove = new Vector2(0f, 1f);
                everyFrame?.Invoke();
                yield return null;
                time += Time.deltaTime;
                stuck = Vector3.Distance(Rig.Position, last) < 0.01f ? stuck + Time.deltaTime : 0f;
                last = Rig.Position;
                if (stuck > 1.5f || time > limit)
                {
                    _failure = $"{label}: kẹt ở {Rig.Position:F2}, cần tới {target:F2}; chạm: {Touching()}";
                    break;
                }
            }
            Input.SimMove = Vector2.zero;
            everyFrame?.Invoke();
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>Collider đang chạm capsule người chơi (nới thêm 5 cm), để báo lỗi kẹt rõ chỗ.</summary>
        static string Touching()
        {
            var cc = Rig.GetComponent<CharacterController>();
            var center = Rig.transform.TransformPoint(cc.center);
            float half = cc.height * 0.5f - cc.radius;
            var hits = Physics.OverlapCapsule(center + Vector3.up * half, center - Vector3.up * half, cc.radius + cc.skinWidth + 0.05f,
                ~0, QueryTriggerInteraction.Ignore);
            return string.Join(", ", hits.Where(h => h != cc).Select(h =>
            {
                var p = h.ClosestPoint(center);
                return $"{h.transform.parent?.name}/{h.name} @{p:F2}";
            })) + $" | grounded {cc.isGrounded} flags {cc.collisionFlags} enabled {cc.enabled} v {cc.velocity:F2} speed {Rig.motor.CurrentSpeed:F2}" +
                $" locked {Rig.motor.MovementLocked} gameplay {GameRoot.Instance.Input.GameplayEnabled} move {GameRoot.Instance.Input.Move}";
        }

        void AssertWalked()
        {
            Assert.IsNull(_failure, _failure);
        }

        static ShipLayout Layout => Object.FindAnyObjectByType<ShipLayout>();

        static Vector3 Ship(Vector3 local) => Layout.transform.TransformPoint(local);

        static Vector3 Galley(Vector3 local) => Layout.galley.TransformPoint(local);

        /// <summary>
        /// Mọi lối nối: đứng trước chân thang trên sàn dưới, đi lên quá đỉnh, rồi quay xuống quá chân. Đi theo làn thông của
        /// thang (thang boong xuồng: nửa phía mạn, tránh la bàn của model).
        /// </summary>
        [UnityTest]
        public IEnumerator EveryStairAndPassageIsWalkableBothWays()
        {
            yield return LoadGalley();
            var stairs = Layout.stairs.GetComponentsInChildren<ShipStair>();
            Assert.AreEqual(13, stairs.Length);
            var failures = new List<string>();
            foreach (var stair in stairs)
            {
                var bottom = stair.LaneBottomWorld;
                var top = stair.LaneTopWorld;
                var along = Flat(top - bottom).normalized;
                // Thang: dừng 0,6 m sau đầu dốc (chiếu nghỉ boong xuồng có bánh lái cách đỉnh thang 0,75 m).
                float margin = stair.steps > 0 ? 0.6f : 1.0f;
                var below = bottom - along * margin;
                var above = top + along * margin;
                _failure = null;
                Rig.motor.Teleport(below, 0f);
                yield return null;
                yield return WalkTo(above, stair.id + " đi lên");
                if (_failure == null && Mathf.Abs(Rig.Position.y - top.y) > 0.35f)
                    _failure = $"{stair.id}: lên tới cao độ {Rig.Position.y:F2}, đỉnh {top.y:F2}";
                yield return WalkTo(below, stair.id + " đi xuống");
                if (_failure == null && Mathf.Abs(Rig.Position.y - bottom.y) > 0.35f)
                    _failure = $"{stair.id}: xuống tới cao độ {Rig.Position.y:F2}, chân {bottom.y:F2}";
                TestContext.WriteLine($"{stair.id}: {(_failure ?? "OK")}");
                if (_failure != null)
                    failures.Add(_failure);
            }
            _failure = null;
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>
        /// Bếp → cửa bếp → boong giếng trước → buồng thang → 14 bậc xuống hầm → nhặt bao khoai → lên thang → vào bếp →
        /// đặt bao lên bàn. Tàu lắc 0,6: trên thang phải rê chuột ngược chiều bao nghiêng (StairsBalance) như người chơi.
        /// </summary>
        [UnityTest]
        public IEnumerator SackIsCarriedFromHoldToGalley([Values(0f, 0.6f)] float intensity)
        {
            yield return LoadGalley();
            var director = Object.FindAnyObjectByType<ShiftDirector>();
            var balance = Object.FindAnyObjectByType<StairsBalance>();
            var sack = director.sacks.sacks[2];
            var who = Rig.interactor;
            System.Action hold = () =>
            {
                ShipMotion.Current.Intensity = intensity;
                // Giữ thăng bằng như người chơi: rê chuột ngang ngược chiều bao đang nghiêng.
                Input.SimLook = balance.Active ? new Vector2(-balance.Logic.Lean / balance.Logic.CounterPerPixel * 0.5f, 0f) : Vector2.zero;
            };
            Rig.motor.Teleport(Galley(new Vector3(0f, 0f, -1.2f)), 0f);
            yield return null;

            var outward = new[]
            {
                Galley(new Vector3(-1.6f, 0f, -1.0f)), Galley(new Vector3(-3.3f, 0f, 1.6f)), Galley(new Vector3(-4.3f, 0f, 2.25f)),
                Galley(new Vector3(-4.75f, 0f, 2.6f)), Galley(new Vector3(-5.6f, 0f, 2.6f)),
                Ship(new Vector3(5.45f, 5.6f, -2.2f)), Ship(new Vector3(5.45f, 5.6f, 0f)), Ship(new Vector3(6.35f, 5.6f, 0f)),
                Ship(new Vector3(9.9f, 2.8f, 0f)), Ship(new Vector3(12.3f, 2.8f, 1.2f)),
            };
            for (int i = 0; i < outward.Length; i++)
                yield return WalkTo(outward[i], "ra hầm #" + i, 0.2f, hold);
            AssertWalked();
            Assert.IsTrue(Layout.holdZone.PlayerInside, "đã xuống tới hầm thực phẩm");
            Assert.AreEqual(ShipRegion.Interior, ShipZone.CurrentRegion);

            // Nhìn vào bao, E để vác.
            var head = Rig.Position + Vector3.up * 1.62f;
            var to = sack.transform.position - head;
            Rig.look.SetAngles(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -Mathf.Atan2(to.y, Flat(to).magnitude) * Mathf.Rad2Deg);
            yield return PlayTestUtil.WaitUntil(() => ReferenceEquals(who.Current, sack), 5f);
            Assert.IsTrue(ReferenceEquals(who.Current, sack), $"phải nhắm trúng bao, đang trúng {(who.LookCollider != null ? who.LookCollider.name : "không gì")}");
            Input.SimulateInteract();
            yield return PlayTestUtil.WaitUntil(() => who.Holding == sack && sack.transform.localPosition.sqrMagnitude < 0.0025f, 10f);
            Assert.AreEqual(sack, who.Holding, "vác được bao");

            var back = new[]
            {
                Ship(new Vector3(10.4f, 2.8f, 0f)), Ship(new Vector3(9.9f, 2.8f, 0f)), Ship(new Vector3(6.3f, 5.6f, 0f)),
                Ship(new Vector3(5.45f, 5.6f, 0f)), Ship(new Vector3(5.45f, 5.6f, -2.2f)), Galley(new Vector3(-5.6f, 0f, 2.6f)),
                Galley(new Vector3(-4.75f, 0f, 2.6f)), Galley(new Vector3(-4.3f, 0f, 2.25f)), Galley(new Vector3(-3.3f, 0f, 1.6f)),
                Galley(new Vector3(-1.6f, 0f, -1.0f)), Galley(new Vector3(0.4f, 0f, -1.0f)),
            };
            for (int i = 0; i < back.Length; i++)
            {
                yield return WalkTo(back[i], "về bếp #" + i, 0.2f, hold);
                Assert.AreEqual(sack, who.Holding, $"bao tuột khỏi tay ở chặng {i} (tàu lắc {intensity})");
            }
            AssertWalked();
            Assert.IsTrue(Layout.galleyZone.PlayerInside, "đã về tới bếp");
            Assert.AreEqual(0, sack.GetComponent<ReturnToStart>().Returns, "bao không rơi khỏi tàu");

            // Đặt bao lên nửa đông bàn sơ chế.
            var zone = director.sacks.zone;
            head = Rig.Position + Vector3.up * 1.62f;
            to = zone.transform.position - head;
            Rig.look.SetAngles(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -Mathf.Atan2(to.y, Flat(to).magnitude) * Mathf.Rad2Deg);
            yield return PlayTestUtil.WaitUntil(() => ReferenceEquals(who.Current, zone), 5f);
            Assert.IsTrue(ReferenceEquals(who.Current, zone), "phải nhắm trúng chỗ đặt bao trên bàn");
            Input.SimulateInteract();
            yield return PlayTestUtil.WaitUntil(() => who.Holding == null, 5f);
            Assert.IsNull(who.Holding, "đặt được bao lên bàn");
        }

        /// <summary>Từ kè Nhà Rồng lên cầu thang tàu tới boong giếng trước (trước khi nhổ neo).</summary>
        [UnityTest]
        public IEnumerator PlayerClimbsGangwayFromNhaRongQuay()
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[0]);
            // Bỏ đoạn mở đầu: bài này chỉ kiểm lối lên tàu.
            Object.Destroy(Object.FindAnyObjectByType<NhaRongDirector>());
            yield return null;
            Input.ClearBlocks();
            Rig.SetControl(true);
            var gangway = Layout.gangway;
            Assert.IsNotNull(gangway);
            Assert.IsTrue(gangway.gameObject.activeInHierarchy);
            var ramp = gangway.Find("GangwayRamp");
            var foot = ramp.position + ramp.forward * (ramp.GetComponentInChildren<BoxCollider>().size.z * 0.5f);
            var top = ramp.position - ramp.forward * (ramp.GetComponentInChildren<BoxCollider>().size.z * 0.5f);
            var along = Flat(top - foot).normalized;
            Rig.motor.Teleport(foot - along * 1.5f, 0f);
            yield return null;
            yield return WalkTo(foot, "tới chân cầu thang");
            yield return WalkTo(top + along * 1.2f, "lên cầu thang tàu");
            AssertWalked();
            var aboard = GameObject.Find("AboardArea").GetComponent<PlayerArea>();
            yield return PlayTestUtil.WaitUntil(() => aboard.PlayerInside, 2f);
            Assert.IsTrue(aboard.PlayerInside, $"phải lên tới boong, đang ở {Rig.Position:F2}");
            Assert.AreEqual(Layout.transform.TransformPoint(new Vector3(0f, 5.6f, 0f)).y, Rig.Position.y, 0.2f, "đứng trên boong giếng trước");
        }

        /// <summary>
        /// Trong bếp: lửa lò có bóng, mặt trời không bóng; ngoài boong: ngược lại. Luôn ≤ 1 đèn realtime có bóng, ≤ 2 đèn không
        /// bóng (PLAN 3.3).
        /// </summary>
        [UnityTest]
        public IEnumerator LightBudgetFollowsInsideAndOutsideGalley()
        {
            yield return LoadGalley();
            var director = Object.FindAnyObjectByType<ShiftDirector>();
            var fire = director.coal.fireLight;
            var sun = Layout.world.GetComponent<TimeOfDay>().mainLight;

            Rig.motor.Teleport(Galley(new Vector3(0f, 0f, -1.2f)), 0f);
            yield return WalkTo(Galley(new Vector3(-1.0f, 0f, -1.2f)), "đi trong bếp");
            yield return null;
            Assert.AreEqual(ShipRegion.Interior, ShipZone.CurrentRegion);
            AssertBudget("trong bếp");
            Assert.AreNotEqual(LightShadows.None, fire.shadows, "lửa lò đổ bóng trong bếp");
            Assert.IsTrue(sun.enabled, "mặt trời vẫn sáng qua cửa");
            Assert.AreEqual(LightShadows.None, sun.shadows, "mặt trời không bóng khi ở trong bếp");

            Rig.motor.Teleport(Ship(new Vector3(14f, 5.6f, -4.5f)), 0f);
            yield return WalkTo(Ship(new Vector3(15f, 5.6f, -4.5f)), "đi trên boong");
            yield return null;
            Assert.AreEqual(ShipRegion.Exterior, ShipZone.CurrentRegion);
            AssertBudget("ngoài boong");
            Assert.AreNotEqual(LightShadows.None, sun.shadows, "mặt trời đổ bóng ngoài boong");
            Assert.AreEqual(LightShadows.None, fire.shadows, "lửa lò không bóng khi ở ngoài");
            AssertWalked();
        }

        [UnityTest]
        public IEnumerator AmbienceCrossfadesWhenWalkingFromGalleyToDeck()
        {
            yield return LoadGalley();
            var ambience = Object.FindAnyObjectByType<Audio.ShipAmbience>();
            Assert.IsNotNull(ambience);
            Rig.motor.Teleport(Galley(new Vector3(-3.3f, 0f, 1.6f)), 0f);
            // Lớp này dùng captureDeltaTime để đi bộ; crossfade dùng đồng hồ thực, kể cả khi pause.
            yield return new WaitForSecondsRealtime(2f);
            Assert.AreEqual("Ambience/galley", ambience.CurrentKey);
            var sources = ambience.GetComponents<AudioSource>();
            var kitchen = System.Array.Find(sources, s => s.clip.name == "galley");
            var deck = System.Array.Find(sources, s => s.clip.name == "sea_deck");
            Assert.IsNotNull(kitchen);
            Assert.IsNotNull(deck);
            Assert.Greater(kitchen.volume, 0f);
            Assert.AreEqual(0f, deck.volume, 0.001f);
            foreach (var point in new[]
            {
                new Vector3(-4.3f, 0f, 2.25f), new Vector3(-4.75f, 0f, 2.6f), new Vector3(-5.6f, 0f, 2.6f)
            })
                yield return WalkTo(Galley(point), "âm thanh ra boong", 0.15f);
            AssertWalked();
            yield return new WaitForSecondsRealtime(2f);
            Assert.AreEqual("Ambience/sea_deck", ambience.CurrentKey);
            Assert.AreEqual(0f, kitchen.volume, 0.001f);
            Assert.Greater(deck.volume, 0f);

            var settings = GameSettings.Current.Clone();
            settings.AmbienceVolume = 0f;
            GameSettings.Apply(settings);
            yield return null;
            foreach (var source in sources)
                Assert.AreEqual(0f, source.volume, 0.001f, source.clip.name);
            var engine = Object.FindAnyObjectByType<EngineHum>().GetComponent<AudioSource>();
            Assert.IsNotNull(engine);
            Assert.AreEqual(0f, engine.volume, 0.001f);
            GameSettings.ResetForTests();
        }

        static void AssertBudget(string where)
        {
            int shadowed = 0;
            int plain = 0;
            var names = new List<string>();
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (!light.isActiveAndEnabled || light.lightmapBakeType == LightmapBakeType.Baked)
                    continue;
                if (light.shadows != LightShadows.None)
                    shadowed++;
                else
                    plain++;
                names.Add($"{light.name}({light.shadows})");
            }
            string list = string.Join(", ", names);
            Assert.LessOrEqual(shadowed, 1, where + ": " + list);
            Assert.LessOrEqual(plain, 2, where + ": " + list);
            TestContext.WriteLine(where + ": " + list);
        }
    }
}
