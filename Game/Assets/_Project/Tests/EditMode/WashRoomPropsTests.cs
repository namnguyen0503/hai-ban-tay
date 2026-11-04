using System.Linq;
using HaiBanTay.Editor.SceneBuilders;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Ghi chú debug 22/10 (QA-0132…0135, 0137, 0142, 0146, 0148): đo số trên cảnh bếp đã dựng (không render). Vật không chồng lên nhau
    /// quá 2 mm theo hộp bao; không nới sai số để né test.
    /// </summary>
    public class WashRoomPropsTests
    {
        const float Tol = 0.002f;

        [SetUp]
        public void OpenGalley() => EditorSceneManager.OpenScene(BuilderUtils.ScenePath("02_Galley"), OpenSceneMode.Single);

        static T Find<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single();

        static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToArray();
            Assert.IsNotEmpty(rs, go.name + " không có hình");
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static bool Overlap(Bounds a, Bounds b, float tol = Tol) =>
            a.min.x < b.max.x - tol && b.min.x < a.max.x - tol && a.min.y < b.max.y - tol && b.min.y < a.max.y - tol &&
            a.min.z < b.max.z - tol && b.min.z < a.max.z - tol;

        static Transform Child(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

        [Test]
        public void ToolShelfStaysOutsideTheBasinWalls()
        {
            var wash = Find<WashStation>();
            var basin = wash.basin.transform;
            var shelf = GameObject.Find("ToolShelf");
            Assert.IsNotNull(shelf);
            var s = shelf.GetComponent<Renderer>().bounds;
            foreach (var part in new[] { "End", "Side", "Bottom" })
                foreach (var r in basin.GetComponentsInChildren<Renderer>().Where(r => r.name == part))
                    Assert.IsFalse(Overlap(s, r.bounds), $"kệ dụng cụ {s} xuyên {part} chậu {r.bounds} (QA-0135)");
        }

        [Test]
        public void WaterSitsBelowTheBottomOfAnyPotInTheBasin()
        {
            var wash = Find<WashStation>();
            var water = Child(wash.basin.transform, "Water").GetComponent<Renderer>().bounds;
            var potBottom = wash.basin.transform.TransformPoint(new Vector3(0f, 0.57f + WashStation.BasinLift, 0f)).y;
            Assert.Less(water.max.y, potBottom, "mặt nước cao hơn đáy nồi: thấy nước xanh trong lòng nồi (QA-0134)");
            Assert.AreEqual(potBottom, wash.basinPot.transform.position.y, 1e-3f, "nồi trong chậu đặt đáy ở 0,77 m");
            var zinc = Child(wash.basin.transform, "Bottom").GetComponent<Renderer>().bounds;
            Assert.GreaterOrEqual(water.min.y, zinc.max.y - Tol, "nước nằm trên đáy kẽm");
        }

        [Test]
        public void RackSlotsHoldEveryMealPotWithoutClipping()
        {
            var wash = Find<WashStation>();
            var meals = Find<MealPots>();
            Assert.GreaterOrEqual(wash.rackSlots.Length, meals.pots.Length, "mỗi nồi một chỗ trên giá");
            var wall = GameObject.Find("Environment/Ship/GalleyBlock/WashRoom/Wall_E") ?? GameObject.Find("Wall_E");
            var shelves = wash.rack.GetComponentsInChildren<Renderer>().Where(r => r.name.StartsWith("Shelf_") || r.name == "Upright").ToArray();
            Assert.GreaterOrEqual(shelves.Length, 2);
            // Mọi nồi (lấy cỡ lớn nhất) úp vào mọi chỗ: không chồng kệ, vách, nồi khác, vòi.
            var placed = new System.Collections.Generic.List<(int, Bounds)>();
            var big = meals.pots.OrderByDescending(p => WashStation.PotSize(p).diameter).First();
            Debug.Log("RACK_POTS " + string.Join(", ", meals.pots.Select(p => $"{p.name} Ø{WashStation.PotSize(p).diameter:F3} h{WashStation.PotSize(p).height:F3}")));
            var keep = (big.transform.parent, big.transform.position, big.transform.rotation, big.activeSelf);
            big.SetActive(true);
            foreach (var r in big.GetComponentsInChildren<Renderer>(true))
                if (r.name == "MealLid" || r.name == "LidKnob" || r.name == "Broth" || r.name == "Potato") r.gameObject.SetActive(false);
            for (int i = 0; i < wash.rackSlots.Length; i++)
            {
                var slot = wash.rackSlots[i];
                var (_, h) = WashStation.PotSize(big);
                big.transform.SetPositionAndRotation(slot.position + slot.up * h, slot.rotation * Quaternion.Euler(180f, 0f, 0f));
                var b = RendererBounds(big);
                // Hộp bao của nồi úp quay 45° rộng hơn hình thật; đo thân (trụ) với kệ theo độ cao vành.
                var lowest = big.GetComponentsInChildren<Renderer>().Where(r => r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer))
                    .OrderBy(r => r.bounds.min.y).First();
                Assert.GreaterOrEqual(b.min.y, slot.position.y - Tol, $"chỗ {i}: vành nồi lún dưới mặt kệ (QA-0133), thấp nhất {lowest.name} {lowest.bounds}");
                // Đo theo đỉnh mesh (hộp bao của nồi tròn quay 45° phình 41%).
                float maxX = big.GetComponentsInChildren<MeshFilter>().Where(f => f.gameObject.activeInHierarchy && f.sharedMesh != null)
                    .SelectMany(f => f.sharedMesh.vertices.Select(v => f.transform.TransformPoint(v).x)).Max();
                Assert.Less(maxX, wall.GetComponent<Renderer>().bounds.min.x + Tol, $"chỗ {i}: nồi/quai lấn vách đông");
                // Kệ, cột là hộp thẳng trục: đỉnh mesh nào của nồi nằm trong hộp (thu 2 mm) là xuyên.
                foreach (var f in big.GetComponentsInChildren<MeshFilter>().Where(f => f.gameObject.activeInHierarchy && f.sharedMesh != null))
                    foreach (var shelf in shelves)
                    {
                        var box = shelf.bounds;
                        box.Expand(-2f * Tol);
                        foreach (var v in f.sharedMesh.vertices)
                            Assert.IsFalse(box.Contains(f.transform.TransformPoint(v)), $"chỗ {i}: {f.name} xuyên {shelf.name} {shelf.bounds}");
                    }
                foreach (var (j, other) in placed)
                {
                    // Hai nồi cạnh nhau: tâm cách ≥ đường kính + 2 cm (quai chéo không chạm, xem builder).
                    var d = slot.position - wash.rackSlots[j].position;
                    if (Mathf.Abs(d.y) > 0.05f) continue;
                    Assert.GreaterOrEqual(new Vector2(d.x, d.z).magnitude, WashStation.PotSize(big).diameter + 0.02f, $"chỗ {i} sát chỗ {j}");
                }
                placed.Add((i, b));
            }
            big.transform.SetParent(keep.parent, false);
            big.transform.SetPositionAndRotation(keep.position, keep.rotation);
            big.SetActive(keep.activeSelf);
        }

        [Test]
        public void TapPoursIntoTheBasinAwayFromThePot()
        {
            var wash = Find<WashStation>();
            Assert.IsNotNull(wash.tap, "chậu có vòi (QA-0132)");
            Assert.IsNotNull(wash.tapTarget);
            var flow = Child(wash.tap.transform, "Flow").GetComponent<Renderer>().bounds;
            var basin = wash.basin.transform;
            var lo = basin.InverseTransformPoint(flow.center);
            Assert.Less(Mathf.Abs(lo.x), 0.53f, "dòng nước rơi trong lòng chậu");
            Assert.Less(Mathf.Abs(lo.z), 0.28f, "dòng nước rơi trong lòng chậu");
            var pot = RendererBounds(wash.basinPot);
            Assert.IsFalse(Overlap(flow, pot), "dòng nước không xuyên nồi");
            var rack = wash.rack.GetComponentsInChildren<Renderer>().Where(r => r.name.StartsWith("Shelf_")).Min(r => r.bounds.min.y);
            Assert.Less(RendererBounds(wash.tap.gameObject).max.y, rack, "vòi dưới giá úp nồi");
        }

        [Test]
        public void WashRoomFloorDoesNotOverlapTheGalleyFloor()
        {
            var floors = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .Where(r => r.name == "Floor" && (r.transform.parent.name == "WashRoom" || r.transform.parent.name == "Galley")).ToArray();
            Assert.AreEqual(2, floors.Length);
            var a = floors[0].bounds; var b = floors[1].bounds;
            float overlapX = Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x);
            float overlapZ = Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z);
            Assert.IsFalse(overlapX > Tol && overlapZ > Tol, $"hai khối sàn chồng nhau {overlapX:F3} × {overlapZ:F3} m (QA-0142)");
            Assert.AreEqual(Mathf.Max(a.min.x, b.min.x), Mathf.Min(a.max.x, b.max.x), Tol, "hai khối sàn khít mép");
        }

        [Test]
        public void TallPotHasTwoHandlesOnItsWall()
        {
            var tall = Find<TallPotStation>();
            var grips = tall.body.GetComponentsInChildren<Renderer>().Where(r => r.name == "HandleGrip").ToArray();
            Assert.AreEqual(2, grips.Length, "nồi cao có hai quai (QA-0137)");
            foreach (var g in grips)
            {
                var local = tall.potRoot.InverseTransformPoint(g.bounds.center);
                float r = new Vector2(local.x, local.z).magnitude;
                Assert.Greater(r, TallPotStation.Radius - 0.05f, "quai ở ngoài thân");
                Assert.Less(r, TallPotStation.Radius + 0.12f, "quai sát thân");
                // Ghế ở phía đông; xoay từng 90° quai vẫn cách hướng ghế ≥ 40°.
                float angle = Mathf.Atan2(local.z, local.x) * Mathf.Rad2Deg;
                for (int k = 0; k < 4; k++)
                    Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(angle + 90f * k, 0f)), 40f, "quai quay vào chỗ ghế");
            }
        }

        [Test]
        public void PanOnTheRangeClearsTheTallPotAndNeighbourPots()
        {
            var pan = Find<PanStation>();
            var tall = Find<TallPotStation>();
            var meals = Find<MealPots>();
            Assert.IsNotNull(pan.rangeSpot, "chỗ đặt chảo trên bếp (QA-0148)");
            Assert.Greater(pan.pan.GetComponentsInChildren<Renderer>().Count(r => r.name == "Inside"), 0, "chảo có lòng (QA-0146)");
            pan.pan.transform.SetPositionAndRotation(pan.rangeSpot.position, pan.rangeSpot.rotation);
            var pb = RendererBounds(pan.pan.gameObject);
            tall.potRoot.SetPositionAndRotation(tall.hob.position, tall.hob.rotation);
            Assert.IsFalse(Overlap(pb, RendererBounds(tall.body.gameObject)), $"chảo {pb} chồng nồi cao trên ô bên");
            var cooking = meals.pots.Last();
            cooking.SetActive(true);
            foreach (var hob in meals.hobs)
            {
                var d = hob.position - pan.rangeSpot.position;
                d.y = 0f;
                if (d.magnitude < 0.45f) continue; // ô dưới chảo: MealPots không giao ô này khi chảo trên bếp
                cooking.transform.SetPositionAndRotation(hob.position, Quaternion.identity);
                Assert.IsFalse(Overlap(pb, RendererBounds(cooking)), $"chảo chồng nồi ở ô {hob.name}");
            }
            // Đáy chảo không lún dưới mặt ô bếp, quai không lấn tường chắn nhiệt.
            Assert.GreaterOrEqual(pb.min.y, pan.rangeSpot.position.y - Tol);
            var shield = GameObject.Find("StoveWallShield").GetComponent<Renderer>().bounds;
            Assert.IsFalse(Overlap(pb, shield), "quai chảo lấn tường chắn sau bếp");
            // Ống khói tròn ở góc sau: đo khoảng cách trục (hộp bao vuông của chảo tròn lấn góc trống).
            foreach (var flue in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.name == "Flue" || r.name == "Joint"))
            {
                if (flue.bounds.min.y > pb.max.y || flue.bounds.max.y < pb.min.y) continue;
                var d = flue.bounds.center - pan.rangeSpot.position;
                d.y = 0f;
                Assert.GreaterOrEqual(d.magnitude, PanStation.RimOuter + flue.bounds.extents.x, "chảo chạm ống khói " + flue.name);
            }
        }
    }
}
