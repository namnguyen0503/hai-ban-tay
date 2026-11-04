using System.Collections.Generic;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Shift;
using HaiBanTay.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    public class TaskTests
    {
        const float Dt = 0.1f;

        static ShovelEvent Pour(ShovelLogic l)
        {
            for (int i = 0; i < 8; i++)
                l.Tick(true, atBin: true, atStove: false, Dt);
            l.Tick(true, atBin: false, atStove: true, Dt);
            return l.Tick(false, atBin: false, atStove: true, Dt);
        }

        [Test]
        public void ShovelCountsEveryPour()
        {
            var l = new ShovelLogic();
            for (int i = 0; i < 7; i++)
                Assert.AreEqual(ShovelEvent.Poured, Pour(l));
            Assert.AreEqual(7, l.Poured);
            Assert.IsFalse(l.Loaded);
        }

        [Test]
        public void PouringEmptyShovelDoesNotCount()
        {
            var l = new ShovelLogic();
            l.Tick(true, atBin: false, atStove: true, Dt);
            Assert.AreEqual(ShovelEvent.None, l.Tick(false, atBin: false, atStove: true, Dt));
            Assert.AreEqual(0, l.Poured);
        }

        [Test]
        public void ShortHoldDoesNotLoadShovel()
        {
            var l = new ShovelLogic();
            l.Tick(true, true, false, 0.3f);
            l.Tick(false, false, true, Dt);
            Assert.IsFalse(l.Loaded);
            Assert.AreEqual(0, l.Poured);
            Assert.AreEqual(0f, l.Fill);
        }

        [Test]
        public void ReleasingAwayFromStoveSpillsCoal()
        {
            var l = new ShovelLogic();
            for (int i = 0; i < 8; i++)
                l.Tick(true, true, false, Dt);
            Assert.IsTrue(l.Loaded);
            Assert.AreEqual(ShovelEvent.Spilled, l.Tick(false, false, false, Dt));
            Assert.IsFalse(l.Loaded);
            Assert.AreEqual(0, l.Poured);
        }

        [Test]
        public void TiredHandsFillShovelSlower()
        {
            var fresh = new ShovelLogic();
            var tired = new ShovelLogic();
            int freshTicks = 0, tiredTicks = 0;
            while (!fresh.Loaded && freshTicks < 100)
            {
                fresh.Tick(true, true, false, Dt, 1f);
                freshTicks++;
            }
            while (!tired.Loaded && tiredTicks < 100)
            {
                tired.Tick(true, true, false, Dt, 0.6f);
                tiredTicks++;
            }
            Assert.Greater(tiredTicks, freshTicks);
        }

        [Test]
        public void ScrubProgressClampedAndNeedsHoldLookAndMovement()
        {
            var s = new ScrubLogic();
            Assert.IsFalse(s.Tick(false, true, new Vector2(50f, 0f), Dt));
            Assert.IsFalse(s.Tick(true, false, new Vector2(50f, 0f), Dt));
            Assert.IsFalse(s.Tick(true, true, Vector2.zero, Dt));
            Assert.AreEqual(0f, s.Progress);
            for (int i = 0; i < 1000; i++)
                s.Tick(true, true, new Vector2(400f, 300f), Dt);
            Assert.AreEqual(1f, s.Progress);
            Assert.IsTrue(s.Done);
            Assert.LessOrEqual(s.Progress, 1f);
        }

        [Test]
        public void ScrubRespectsTemporaryCap()
        {
            var s = new ScrubLogic { Cap = 0.4f };
            for (int i = 0; i < 1000; i++)
                s.Tick(true, true, new Vector2(400f, 0f), Dt);
            Assert.AreEqual(0.4f, s.Progress, 1e-5f);
            s.Cap = 1f;
            Assert.IsTrue(s.Tick(true, true, new Vector2(400f, 0f), Dt));
        }

        [Test]
        public void SackStationCountsOnlyItsSacksAndFreesTheTable()
        {
            var spawned = new List<GameObject>();
            try
            {
                Holdable Make(string kind)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    spawned.Add(go);
                    go.AddComponent<Rigidbody>();
                    var h = go.AddComponent<Holdable>();
                    h.kind = kind;
                    return h;
                }

                var zoneGo = new GameObject("Zone");
                spawned.Add(zoneGo);
                var zone = zoneGo.AddComponent<DropZone>();
                var stray = Make("sack");
                var sack = Make("sack");

                var stationGo = new GameObject("Station");
                spawned.Add(stationGo);
                var station = stationGo.AddComponent<SackStation>();
                station.zone = zone;
                station.sacks = new[] { sack };
                int worked = 0;
                station.Worked += (_, units) => worked += units;

                station.Activate();
                Assert.AreEqual(TaskState.Active, station.State);
                Assert.IsTrue(zone.Place(stray));
                Assert.AreEqual(0, worked, "bao không thuộc trạm không tính");
                Assert.AreEqual(stray, zone.Take());
                Assert.IsFalse(stray.Locked, "lấy ra khỏi vùng thì mở khóa");
                Assert.IsTrue(zone.Place(sack));
                Assert.AreEqual(0, worked, "đặt lên bàn chưa tính: còn phải dốc khoai");
                Assert.AreEqual(sack, station.OnTable);
                station.FinishPour();
                Assert.AreEqual(1, worked);
                Assert.AreEqual(1, station.Delivered);
                Assert.IsNull(zone.Contents, "dốc xong thì bàn trống");
                station.Close();
                Assert.AreEqual(TaskState.Done, station.State);
            }
            finally
            {
                foreach (var go in spawned)
                    Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void MatchWordsNeedsCorrectPairs()
        {
            var pairs = new List<FrenchPair>
            {
                new FrenchPair { Fr = "la mer", Vi = "biển" },
                new FrenchPair { Fr = "le pain", Vi = "bánh mì" },
                new FrenchPair { Fr = "l'ami", Vi = "người bạn" },
            };
            var l = new MatchWordsLogic(pairs);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, l.ViOrder);
            Assert.IsFalse(l.TryMatch(0, 1));
            Assert.AreEqual(1, l.WrongAttempts);
            Assert.IsTrue(l.TryMatch(0, 0));
            Assert.IsFalse(l.TryMatch(0, 0), "không ghép lại cặp đã xong");
            Assert.IsTrue(l.TryMatch(1, 1));
            Assert.IsFalse(l.Done);
            Assert.IsTrue(l.TryMatch(2, 2));
            Assert.IsTrue(l.Done);
        }
    }
}
