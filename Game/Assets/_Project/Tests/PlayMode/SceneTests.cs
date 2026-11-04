using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Player;
using HaiBanTay.Ship;
using HaiBanTay.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    public class SceneTests
    {
        [TearDown]
        public void TearDown()
        {
            PlayTestUtil.ExitSim();
        }

        static void Require(params string[] names)
        {
            foreach (var n in names)
                Assert.IsNotNull(GameObject.Find(n), "thiếu đối tượng bắt buộc: " + n);
        }

        static void RequirePlayer()
        {
            Assert.IsNotNull(PlayerRig.Current, "thiếu PlayerRig");
            Assert.IsNotNull(Camera.main, "thiếu Main Camera");
            Assert.IsNotNull(PlayerRig.Current.hands);
            Assert.IsNotNull(PlayerRig.Current.interactor.cam);
        }

        [UnityTest]
        public IEnumerator NhaRongHasRequiredObjects()
        {
            PlayTestUtil.Root();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[0]);
            RequirePlayer();
            Require("NhaRongDirector", "ArtSlot_GalleyDoor", "RailingZone", "Backdrop_NhaRong", "ShipMotion", "EngineHum", "ArtSlot_PortBarrels");
        }

        [UnityTest]
        public IEnumerator GalleyHasRequiredObjects()
        {
            PlayTestUtil.Root();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            RequirePlayer();
            Require("ShiftDirector", "ArtSlot_Stove_A", "ArtSlot_CoalBin", "ArtSlot_PotatoSack", "SackDropZone",
                "PotBody", "ArtSlot_Stool", "LoosePot_0", "LoosePot_1", "LoosePot_2", "ColdZone",
                "ArtSlot_JacquesArm", "EveningCorner", "ShipMotion", "ShovelHook", "WashBasin", "DirtyPots",
                "DryingRack", "WallClock", "Chalkboard", "RestSeat", "CoalStation", "SackStation", "WashStation",
                "AshStation", "SweepStation", "GreensStation", "PanStation", "StairsBalance", "StairsArea", "RakeHook", "BroomHook",
                "AshChute", "WashRoom", "WashRoomArea", "CoalHold", "CoalHoldArea", "ServingHatch", "ServiceBell", "ShipNameplate",
                "GreensCrate", "CopperPan", "SackMouth", "SackTipPivot", "ArtSlot_PotatoTub", "TubPotatoes");
        }

        [UnityTest]
        public IEnumerator MarseilleHasRequiredObjects()
        {
            PlayTestUtil.Root();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[2]);
            RequirePlayer();
            Require("EndingDirector", "GangwayBarrier", "LowPassZone", "QuayStep", "StreetEdge", "ArtSlot_Tram",
                "MarseilleEnvironment", "SalaryDesk", "SalaryLedger", "CafeSeatTarget", "MarseilleCast",
                "Purser", "Waiter", "FinalShift", "MistLayers", "CoinDrop");
        }

        /// <summary>PLAN 3.3: 1 đèn realtime có bóng + tối đa 2 đèn realtime không bóng; còn lại bake.</summary>
        [UnityTest]
        public IEnumerator RealtimeLightsWithinBudget([ValueSource(nameof(GameScenes))] string scene)
        {
            PlayTestUtil.Root();
            yield return PlayTestUtil.LoadAndSettle(scene);
            int shadowed = 0, plain = 0;
            var names = new System.Collections.Generic.List<string>();
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (!l.isActiveAndEnabled || (l.bakingOutput.isBaked && l.bakingOutput.lightmapBakeType == LightmapBakeType.Baked))
                    continue;
                if (l.shadows != LightShadows.None) shadowed++; else plain++;
                names.Add($"{l.name}({l.type}, {l.shadows})");
            }
            string list = string.Join(", ", names);
            Assert.LessOrEqual(shadowed, 1, "quá nhiều đèn realtime có bóng: " + list);
            Assert.LessOrEqual(plain, 2, "quá nhiều đèn realtime không bóng: " + list);
        }

        static readonly string[] GameScenes = new[] { SceneFlow.PrologueScene, SceneFlow.GameScenes[0], SceneFlow.GameScenes[1], SceneFlow.GameScenes[2] };

        [UnityTest]
        public IEnumerator PrologueHasRequiredObjects()
        {
            PlayTestUtil.Root();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.PrologueScene);
            RequirePlayer();
            Require("PrologueDirector", "SaigonStreet", "Anchors", "player_spawn", "le_walk_start", "conversation_ba", "conversation_le",
                "electric_light_bulb", "cafe_seat", "TalkZone", "Le", "Crowd", "StreetLights", "StreetLight_2", "StreetLight_3",
                "COL_EndConstruction_-1", "COL_EndConstruction_1", "WorldRoot");
            // Hai đầu phố chặn bằng vật thật của model (hàng rào công trình + xe thồ), không có tường vô hình.
            foreach (var end in new[] { "COL_EndConstruction_-1", "COL_EndConstruction_1" })
                Assert.IsNotNull(GameObject.Find(end).GetComponent<Collider>(), end + " cần collider");
        }

        [UnityTest]
        public IEnumerator StormAtFullIntensityKeepsLooseObjectsInRoom()
        {
            PlayTestUtil.Root();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            var pots = Object.FindAnyObjectByType<SecurePotsTask>();
            var ship = ShipMotion.Current;
            Assert.IsNotNull(pots);
            Assert.IsNotNull(ship);
            pots.Activate();
            Time.timeScale = 10f;
            float simulated = 0f;
            float maxTravel = 0f;
            var start = new Vector3[pots.pots.Length];
            for (int i = 0; i < start.Length; i++)
                start[i] = pots.pots[i].transform.position;
            var room = new Bounds(new Vector3(0f, 1.3f, 0f), new Vector3(10f, 2.6f, 6f));
            while (simulated < 30f)
            {
                ship.Intensity = 1f;
                simulated += Time.deltaTime;
                for (int i = 0; i < pots.pots.Length; i++)
                {
                    var p = pots.pots[i];
                    Assert.IsTrue(room.Contains(p.transform.position), $"{p.name} văng khỏi phòng: {p.transform.position}");
                    maxTravel = Mathf.Max(maxTravel, Vector3.Distance(start[i], p.transform.position));
                }
                yield return null;
            }
            Time.timeScale = 1f;
            foreach (var p in pots.pots)
                Assert.AreEqual(0, p.GetComponent<ReturnToStart>().Returns, p.name + " đã phải trả về chỗ");
            Assert.Greater(maxTravel, 0.05f, "nồi phải trượt khi có bão");
        }
    }
}
