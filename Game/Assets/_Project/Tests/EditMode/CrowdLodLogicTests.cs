using System.Collections.Generic;
using HaiBanTay.Npc;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>CrowdLodLogic: chọn tối đa N người gần nhất trong frustum, ổn định khi người chơi đứng yên.</summary>
    public sealed class CrowdLodLogicTests
    {
        const float Radius = 25f;

        static List<CrowdLodCandidate> Candidates(params float[] distances)
        {
            var list = new List<CrowdLodCandidate>();
            foreach (var d in distances)
                list.Add(new CrowdLodCandidate { Distance = d, InFrustum = true });
            return list;
        }

        static HashSet<int> Pick(List<CrowdLodCandidate> c, int max, float radius = Radius)
        {
            var chosen = new bool[c.Count];
            int n = CrowdLodLogic.SelectAnimated(c, max, radius, chosen);
            var set = new HashSet<int>();
            for (int i = 0; i < chosen.Length; i++)
                if (chosen[i])
                    set.Add(i);
            Assert.AreEqual(n, set.Count);
            return set;
        }

        static void Remember(List<CrowdLodCandidate> c, HashSet<int> chosen)
        {
            for (int i = 0; i < c.Count; i++)
            {
                var x = c[i];
                x.WasAnimated = chosen.Contains(i);
                c[i] = x;
            }
        }

        [Test]
        public void PicksTheNearestMaxInsideTheFrustum()
        {
            // Khoảng cách xáo thứ tự; chỉ số 1 (2 m) và 5 (6 m) nằm ngoài frustum.
            var c = Candidates(12f, 2f, 18f, 4f, 8f, 6f, 14f, 10f, 20f, 16f);
            c[1] = new CrowdLodCandidate { Distance = 2f, InFrustum = false };
            c[5] = new CrowdLodCandidate { Distance = 6f, InFrustum = false };
            var set = Pick(c, 4);
            CollectionAssert.AreEquivalent(new[] { 3, 4, 7, 0 }, set, "4 người gần nhất trong frustum: 4, 8, 10, 12 m");
        }

        [Test]
        public void NoMoreThanMaxEvenWhenAllAreClose()
        {
            var c = Candidates(1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f, 11f, 12f, 13f, 14f, 15f, 16f, 17f, 18f);
            Assert.AreEqual(10, Pick(c, 10).Count);
            Assert.AreEqual(16, Pick(c, 16).Count);
        }

        [Test]
        public void OutsideTheRadiusIsNeverAnimated()
        {
            var c = Candidates(5f, 24.9f, 25.5f, 40f);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, Pick(c, 16));
        }

        [Test]
        public void EmptyAndZeroMaxAreSafe()
        {
            Assert.AreEqual(0, Pick(new List<CrowdLodCandidate>(), 5).Count);
            Assert.AreEqual(0, Pick(Candidates(1f, 2f), 0).Count);
        }

        [Test]
        public void SelectionIsStableWhenThePlayerStandsStill()
        {
            // 30 người rải đều 3–60 m, trùng khoảng cách từng cặp; chọn lặp 50 lần như 50 nhịp LOD.
            var distances = new List<float>();
            for (int i = 0; i < 30; i++)
                distances.Add(3f + (i / 2) * 3.7f);
            var c = Candidates(distances.ToArray());
            var first = Pick(c, 10);
            for (int tick = 0; tick < 50; tick++)
            {
                Remember(c, first);
                CollectionAssert.AreEquivalent(first, Pick(c, 10), $"nhịp {tick} đổi tập được chọn");
            }
        }

        [Test]
        public void SmallDistanceJitterDoesNotSwapIncumbents()
        {
            // Rung camera ±0,4 m quanh các khoảng cách gần nhau: tập được chọn không đổi sau lần chọn đầu.
            var rng = new System.Random(7);
            var baseDistances = new[] { 10f, 10.3f, 10.6f, 10.9f, 11.2f, 11.5f, 11.8f, 12.1f };
            var c = Candidates(baseDistances);
            var chosen = Pick(c, 4);
            for (int tick = 0; tick < 300; tick++)
            {
                for (int i = 0; i < c.Count; i++)
                {
                    var x = c[i];
                    x.Distance = baseDistances[i] + (float)(rng.NextDouble() * 0.8 - 0.4);
                    x.WasAnimated = chosen.Contains(i);
                    c[i] = x;
                }
                CollectionAssert.AreEquivalent(chosen, Pick(c, 4), $"nhịp {tick}: nhấp nháy khi chỉ rung nhẹ");
            }
        }

        [Test]
        public void ClearlyNearerNewcomerDoesDisplaceAnIncumbent()
        {
            var c = Candidates(10f, 11f, 12f, 5f);
            c[0] = new CrowdLodCandidate { Distance = 10f, InFrustum = true, WasAnimated = true };
            c[1] = new CrowdLodCandidate { Distance = 11f, InFrustum = true, WasAnimated = true };
            c[2] = new CrowdLodCandidate { Distance = 12f, InFrustum = true, WasAnimated = true };
            // Người mới ở 5 m: gần hơn xa mức ưu tiên giữ chỗ, đẩy người xa nhất (12 m) ra.
            CollectionAssert.AreEquivalent(new[] { 3, 0, 1 }, Pick(c, 3));
        }

        [Test]
        public void IncumbentKeepsItsSeatSlightlyBeyondTheRadius()
        {
            var c = Candidates(26f, 26f);
            c[0] = new CrowdLodCandidate { Distance = 26f, InFrustum = true, WasAnimated = true };
            CollectionAssert.AreEquivalent(new[] { 0 }, Pick(c, 5), "chỉ người đang chạy được nới bán kính");
        }

        [Test]
        public void ClassifyHasHysteresisAtTheStaticBoundary()
        {
            // staticRadius 80, margin 3: đang Static thì giữ tới 83 m; đang Hidden thì chỉ hiện lại khi vào trong 77 m.
            Assert.AreEqual(CrowdLodMode.Static, CrowdLodLogic.Classify(82f, CrowdLodMode.Static, 80f, 160f, false, 3f));
            Assert.AreEqual(CrowdLodMode.Hidden, CrowdLodLogic.Classify(84f, CrowdLodMode.Static, 80f, 160f, false, 3f));
            Assert.AreEqual(CrowdLodMode.Hidden, CrowdLodLogic.Classify(78f, CrowdLodMode.Hidden, 80f, 160f, false, 3f));
            Assert.AreEqual(CrowdLodMode.Static, CrowdLodLogic.Classify(76f, CrowdLodMode.Hidden, 80f, 160f, false, 3f));
        }

        [Test]
        public void SilhouetteStaysStaticOutToItsOwnRadius()
        {
            Assert.AreEqual(CrowdLodMode.Static, CrowdLodLogic.Classify(120f, CrowdLodMode.Static, 80f, 160f, true, 3f));
            Assert.AreEqual(CrowdLodMode.Hidden, CrowdLodLogic.Classify(120f, CrowdLodMode.Static, 80f, 160f, false, 3f));
            Assert.AreEqual(CrowdLodMode.Hidden, CrowdLodLogic.Classify(170f, CrowdLodMode.Static, 80f, 160f, true, 3f));
        }

        [Test]
        public void FrustumTestAcceptsAheadAndRejectsBehindOrFarSide()
        {
            // Camera ở gốc nhìn về +Z, FOV dọc 60°, tỉ lệ 16:9, xa 100 m.
            var view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.identity.inverse;
            var proj = Matrix4x4.Perspective(60f, 16f / 9f, 0.1f, 100f);
            var planes = GeometryUtility.CalculateFrustumPlanes(proj * view);
            Assert.IsTrue(CrowdLodLogic.InFrustum(planes, new Vector3(0f, 0f, 10f), 0.5f), "ngay trước mặt");
            Assert.IsFalse(CrowdLodLogic.InFrustum(planes, new Vector3(0f, 0f, -10f), 0.5f), "sau lưng");
            Assert.IsFalse(CrowdLodLogic.InFrustum(planes, new Vector3(30f, 0f, 10f), 0.5f), "lệch rất xa sang bên");
            Assert.IsFalse(CrowdLodLogic.InFrustum(planes, new Vector3(0f, 0f, 150f), 0.5f), "quá mặt phẳng xa");
            // Nửa bề rộng frustum ở 10 m là 10 * tan(30°) * 16/9 ≈ 10,26: tâm lệch 11 m vẫn thấy nếu bán kính ≥ 1.
            Assert.IsFalse(CrowdLodLogic.InFrustum(planes, new Vector3(11.5f, 0f, 10f), 0.5f));
            Assert.IsTrue(CrowdLodLogic.InFrustum(planes, new Vector3(11.5f, 0f, 10f), 2.4f), "hình cầu rộng hơn chạm mép");
        }

        [Test]
        public void PoseOfMapsEveryNpcStateToABakedPose()
        {
            Assert.AreEqual(0, CrowdLodLogic.PoseOf("Idle"));
            Assert.AreEqual(0, CrowdLodLogic.PoseOf("Talk"));
            Assert.AreEqual(0, CrowdLodLogic.PoseOf(null));
            Assert.AreEqual(1, CrowdLodLogic.PoseOf("Walk"));
            Assert.AreEqual(2, CrowdLodLogic.PoseOf("CarryWalk"));
            Assert.AreEqual(3, CrowdLodLogic.PoseOf("HoldCarry"));
            Assert.AreEqual(4, CrowdLodLogic.PoseOf("SitRead"));
            Assert.AreEqual(5, CrowdLodLogic.PoseOf("Stir"));
            Assert.AreEqual(6, CrowdLodLogic.PoseOf("ShoulderCarry"));
            Assert.AreEqual(7, CrowdLodLogic.PoseOf("BagIdle"));
            Assert.AreEqual(8, CrowdLodLogic.PoseOf("Squat"));
            Assert.AreEqual(9, CrowdLodLogic.PoseOf("LeanRail"));
            Assert.AreEqual(CrowdLod.PoseStates.Length, 10, "đủ 10 tư thế bake cho 10 chỉ số của PoseOf");
        }
    }
}
