#if UNITY_EDITOR
using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Performance;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Ship;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    public class WorldPlayTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PlayTestUtil.Root();
            Time.captureDeltaTime = 1f / 60f;
            EditorSceneManager.LoadSceneInPlayMode("Assets/_Project/Scenes/90_Sandbox.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            if (GameRoot.Instance != null) GameRoot.Instance.Input.SimMove = Vector2.zero;
            Time.captureDeltaTime = 0f;
            PlayTestUtil.ExitSim();
        }

        static void WithinBudget()
        {
            int shadow = 0, plain = 0;
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.isActiveAndEnabled && light.lightmapBakeType != LightmapBakeType.Baked)
                {
                    if (light.shadows == LightShadows.None) plain++; else shadow++;
                }
            Assert.LessOrEqual(shadow, 1);
            Assert.LessOrEqual(plain, 2);
        }

        [UnityTest]
        public IEnumerator TenSecondsWorldRollIsOppositeAndShipPlayerStayStill()
        {
            var motion = Object.FindAnyObjectByType<WorldMotion>();
            var ship = ShipMotion.Current;
            var rig = PlayerRig.Current;
            Assert.IsNotNull(motion);
            Assert.IsNotNull(ship);
            Assert.AreEqual(0.6f, ship.intensity, 0.0001f);
            rig.SetControl(false);
            yield return new WaitForSeconds(0.5f);
            var playerPosition = rig.Position;
            var boatPosition = motion.pivot.position;
            var boatRotation = motion.pivot.rotation;
            var clock = motion.GetComponent<TimeOfDay>();
            var waterBlock = new MaterialPropertyBlock();
            float maxRoll = 0f;
            for (int frame = 0; frame < 600; frame++)
            {
                float previousRoll = ship.Roll;
                float previousPitch = ship.Pitch;
                yield return null;
                // UnityTest tiếp tục trước LateUpdate mới: world còn giữ roll/pitch của frame trước.
                var expected = Quaternion.AngleAxis(-previousRoll * motion.gain, motion.pivot.forward)
                    * Quaternion.AngleAxis(-previousPitch * motion.gain, motion.pivot.right);
                Assert.Less(Quaternion.Angle(expected, motion.transform.rotation), 0.001f);
                var p = motion.transform.position;
                Assert.IsTrue(float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z));
                Assert.LessOrEqual(Mathf.Abs(p.y), motion.bobAmplitude * ship.intensity + 0.001f);
                Assert.Less(Vector3.Distance(playerPosition, rig.Position), 0.03f);
                Assert.AreEqual(boatPosition, motion.pivot.position);
                Assert.Less(Quaternion.Angle(boatRotation, motion.pivot.rotation), 0.001f);
                maxRoll = Mathf.Max(maxRoll, Mathf.Abs(ship.Roll));
                clock.ocean.GetPropertyBlock(waterBlock);
                Assert.Less(Vector3.Distance((Vector3)waterBlock.GetVector("_SunDirection"),
                    -clock.mainLight.transform.forward), 0.0001f, "Vệt nắng theo đèn trời khi thế giới roll/pitch");
                WithinBudget();
            }
            Assert.Greater(maxRoll, 4f);
            motion.enabled = false;
            yield return null;
            motion.enabled = true;
            float reenabledRoll = ship.Roll;
            float reenabledPitch = ship.Pitch;
            yield return null;
            Assert.Less(Quaternion.Angle(Quaternion.AngleAxis(-reenabledRoll * motion.gain, motion.pivot.forward)
                * Quaternion.AngleAxis(-reenabledPitch * motion.gain, motion.pivot.right),
                motion.transform.rotation), 0.001f, "bật lại component không cộng dồn góc lắc");
        }

        [UnityTest]
        public IEnumerator WalkingThroughCabinDoorExchangesSunAndFireShadows()
        {
            var rig = PlayerRig.Current;
            var budget = Object.FindAnyObjectByType<LightBudget>();
            var input = GameRoot.Instance.Input;
            var sun = Object.FindAnyObjectByType<TimeOfDay>().mainLight;
            var fire = GameObject.Find("WorldTestFire").GetComponent<Light>();
            rig.motor.Teleport(new Vector3(-60f, 0f, -3f), 0f);
            rig.SetControl(true);
            input.ClearBlocks();
            yield return null;
            Assert.AreEqual(ShipRegion.Exterior, budget.Region);
            Assert.AreNotEqual(LightShadows.None, sun.shadows);
            Assert.AreEqual(LightShadows.None, fire.shadows);
            input.SimMove = Vector2.up;
            for (int i = 0; i < 220 && rig.Position.z < 3.5f; i++)
            {
                yield return null;
                WithinBudget();
            }
            input.SimMove = Vector2.zero;
            Assert.Greater(rig.Position.z, 3.4f, "CharacterController phải đi qua cửa thật");
            Assert.AreEqual(ShipRegion.Interior, budget.Region);
            Assert.AreEqual(LightShadows.None, sun.shadows);
            Assert.AreNotEqual(LightShadows.None, fire.shadows);
            input.SimMove = Vector2.down;
            for (int i = 0; i < 220 && rig.Position.z > -2f; i++)
            {
                yield return null;
                WithinBudget();
            }
            input.SimMove = Vector2.zero;
            Assert.Less(rig.Position.z, -1.9f);
            Assert.AreEqual(ShipRegion.Exterior, budget.Region);
            Assert.AreNotEqual(LightShadows.None, sun.shadows);
            Assert.AreEqual(LightShadows.None, fire.shadows);
        }

        [UnityTest]
        public IEnumerator TimeOfDayFollowsRealShiftClock()
        {
            GameRoot.Instance.ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            var shift = Object.FindAnyObjectByType<ShiftDirector>();
            Assert.IsNotNull(shift);
            yield return PlayTestUtil.WaitUntil(() => shift.Clock != null && shift.Clock.Running, 60f);
            Assert.IsNotNull(shift.Clock);
            Assert.IsTrue(shift.Clock.Running);
            var clock = new GameObject("ClockBindingTest").AddComponent<TimeOfDay>();
            clock.hour = 17f;
            clock.followShiftClock = true;
            clock.shift = shift;
            float start = shift.Clock.Minutes;
            // Đồng hồ chạy theo việc (quyết định 14/10): đứng không thì giờ không trôi.
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                clock.Apply();
            }
            Assert.AreEqual(start, shift.Clock.Minutes, 0.001f, "đứng không làm gì thì giờ không trôi");
            // Làm được nửa việc của lượt: kim đuổi tới giờ tương ứng, trời và đèn theo đúng giờ đó.
            shift.Clock.SetProgress(0.5f);
            for (int i = 0; i < 60; i++)
            {
                yield return null;
                clock.Apply();
                var expected = TimeOfDayLogic.Evaluate(shift.Clock.Minutes / 60f);
                Assert.Less(Vector3.Distance(new Vector3(expected.Sky.r, expected.Sky.g, expected.Sky.b),
                    new Vector3(clock.State.Sky.r, clock.State.Sky.g, clock.State.Sky.b)), 0.0001f);
                Assert.AreEqual(expected.LightIntensity, clock.State.LightIntensity, 0.0001f);
                Assert.AreEqual(expected.MoonIsMain, clock.State.MoonIsMain);
            }
            Assert.Greater(shift.Clock.Minutes, start + 0.5f, "làm việc thì đồng hồ ca chạy");
        }

        [UnityTest]
        public IEnumerator RuntimeAllocationHandlesExtraAndDuplicateLights()
        {
            Object.FindAnyObjectByType<LightBudget>().enabled = false;
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) light.enabled = false;
            var owner = new GameObject("ExtraLightBudget");
            owner.SetActive(false);
            var budget = owner.AddComponent<LightBudget>();
            var lights = new Light[8];
            for (int i = 0; i < lights.Length; i++)
            {
                lights[i] = new GameObject("Managed" + i).AddComponent<Light>();
                lights[i].transform.SetParent(owner.transform);
                lights[i].lightmapBakeType = LightmapBakeType.Realtime;
            }
            budget.exterior = new[] { lights[0], null, lights[1], lights[2], lights[3] };
            budget.interior = new[] { lights[0], lights[4], lights[5], lights[6], lights[7] };
            owner.SetActive(true);
            foreach (var region in new[] { ShipRegion.Exterior, ShipRegion.Interior, ShipRegion.Exterior })
            {
                budget.SetRegion(region);
                int shadow = 0, plain = 0;
                foreach (var light in lights)
                    if (light.enabled)
                    {
                        if (light.shadows == LightShadows.None) plain++; else shadow++;
                    }
                Assert.AreEqual(1, shadow);
                Assert.AreEqual(2, plain);
            }
            Object.Destroy(owner);
            yield return null;
        }
    }
}
#endif
