using System.Collections.Generic;
using System.Linq;
using HaiBanTay.Core;
using HaiBanTay.Editor.SceneBuilders;
using HaiBanTay.Editor.SceneBuilders.Ship;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Ship;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Bố cục tàu đã dựng trong ba cảnh (M10b), mở thẳng file scene: khối bếp nằm trong interior_volumes.galley_block, ô cửa
    /// sổ và cửa bếp khớp ô mở của model, hầm nằm trong thân tàu, cầu thang không dốc quá CharacterController, mọi điểm xuất
    /// hiện đứng trên sàn đi được, mọi renderer có lớp ánh sáng Interior/Exterior. Cần chạy rebuild_scenes trước.
    /// </summary>
    public class ShipLayoutTests
    {
        /// <summary>Vỏ thượng tầng trước/mạn phải dày 0,04 m được phép lấn (interior_volumes.galley_block.allowed).</summary>
        const float ShellTolerance = 0.045f;
        const float PlaneTolerance = 0.02f;

        static readonly string[] Scenes = SceneFlow.GameScenes;

        [OneTimeTearDown]
        public void CloseScenes()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static ShipLayout Open(string scene)
        {
            EditorSceneManager.OpenScene(BuilderUtils.ScenePath(scene), OpenSceneMode.Single);
            Physics.SyncTransforms();
            var layout = Object.FindAnyObjectByType<ShipLayout>();
            Assert.IsNotNull(layout, scene + ": thiếu ShipLayout (chạy Tools/rebuild_scenes.sh)");
            return layout;
        }

        /// <summary>Bounds thế giới đổi sang hệ cục bộ của gốc tàu (tàu chỉ quay bội 90° quanh Y nên vẫn khít).</summary>
        static Bounds ShipLocal(Transform ship, Bounds world)
        {
            var local = new Bounds(ship.InverseTransformPoint(world.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? world.min.x : world.max.x,
                    (i & 2) == 0 ? world.min.y : world.max.y,
                    (i & 4) == 0 ? world.min.z : world.max.z);
                local.Encapsulate(ship.InverseTransformPoint(corner));
            }
            return local;
        }

        static IEnumerable<Renderer> Visible(Transform root) =>
            root.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer));

        static void AssertInside(Bounds volume, Bounds part, string label)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                Assert.GreaterOrEqual(part.min[axis], volume.min[axis], $"{label}: min[{axis}] {part.min[axis]:F3} < {volume.min[axis]:F3}");
                Assert.LessOrEqual(part.max[axis], volume.max[axis], $"{label}: max[{axis}] {part.max[axis]:F3} > {volume.max[axis]:F3}");
            }
        }

        [Test]
        public void GalleyBlockStaysInsideInteriorVolume([ValueSource(nameof(Scenes))] string scene)
        {
            var layout = Open(scene);
            var volume = DeckZones.Volume(DeckZones.Read(), "galley_block");
            volume.Expand(ShellTolerance * 2f);
            int count = 0;
            foreach (var renderer in Visible(layout.galley))
            {
                AssertInside(volume, ShipLocal(layout.transform, renderer.bounds), scene + "/" + Path(renderer.transform));
                count++;
            }
            Assert.Greater(count, 50, "khối bếp phải có đủ phòng");
        }

        [Test]
        public void FoodHoldStaysInsideHoldVolumeAndHull([ValueSource(nameof(Scenes))] string scene)
        {
            var layout = Open(scene);
            var volume = DeckZones.Volume(DeckZones.Read(), "hold");
            var padded = volume;
            padded.Expand(0.1f);
            foreach (var renderer in Visible(layout.foodHold))
                AssertInside(padded, ShipLocal(layout.transform, renderer.bounds), scene + "/" + Path(renderer.transform));
            var hull = layout.GetComponentsInChildren<Renderer>().First(r => r.name == "Hull_LOD0");
            var hullLocal = ShipLocal(layout.transform, hull.bounds);
            AssertInside(hullLocal, volume, "hầm trong thân tàu");
            Assert.LessOrEqual(volume.max.y, 5.6f, "trần hầm dưới mặt boong giếng trước");
        }

        [Test]
        public void PortholeMatchesModelOpening([ValueSource(nameof(Scenes))] string scene)
        {
            var layout = Open(scene);
            var opening = DeckZones.Opening(DeckZones.Read(), "galley_porthole");
            var center = DeckZones.Point(opening["center"]);
            var glass = DeckZones.Point(opening["glass_center"]);
            var porthole = Find(layout.galley, "ArtSlot_Porthole");
            var local = layout.transform.InverseTransformPoint(porthole.position);
            // Ô cửa ở mặt tường x = const: lệch trong mặt tường (y, z) ≤ 2 cm; theo pháp tuyến là bề dày tường.
            Assert.AreEqual(center.y, local.y, PlaneTolerance, "cao độ tâm ô cửa");
            Assert.AreEqual(center.z, local.z, PlaneTolerance, "vị trí ngang tâm ô cửa");
            float inner = InnerWallX();
            Assert.AreEqual(inner, local.x, 0.005f, "ô cửa đặt ở mặt trong tường bếp");
            Assert.Greater(glass.x, inner, "kính nằm ngoài mặt trong tường bếp");
            Assert.Less(glass.x, center.x, "kính nằm trong mặt ngoài vỏ");
            Assert.AreEqual(0, Object.FindObjectsByType<PortholeView>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                "không còn tranh biển giả sau ô cửa");
        }

        [Test]
        public void GalleyDoorMatchesModelOpeningAndSwingsInward([ValueSource(nameof(Scenes))] string scene)
        {
            var layout = Open(scene);
            var data = DeckZones.Read();
            var opening = DeckZones.Opening(data, "galley_door");
            var center = DeckZones.Point(opening["center"]);
            var size = ShipCoordinates.SizeFromBlender(DeckZones.Vector(opening["size"]));
            var door = layout.galleyDoor;
            Assert.IsNotNull(door);
            Assert.AreEqual("ArtSlot_GalleyDoor", door.name);
            var root = layout.transform.InverseTransformPoint(door.transform.position);
            Assert.AreEqual(center.z, root.z, PlaneTolerance, "giữa ô cửa theo phương ngang");
            Assert.AreEqual(center.y, root.y + size.y * 0.5f, PlaneTolerance, "giữa ô cửa theo chiều cao");
            Assert.That(root.x, Is.InRange(InnerWallX(), center.x), "gốc cửa trong bề dày tường");

            // Lối qua ô cửa thông: không collider rắn nào (trừ cánh cửa) trong lòng ô cửa.
            var aperture = new Bounds(new Vector3((InnerWallX() + center.x) * 0.5f, center.y + 0.05f, center.z),
                new Vector3(center.x - InnerWallX() - 0.02f, size.y - 0.2f, size.z - 0.1f));
            AssertClear(layout, aperture, door.transform, "cửa bếp");

            // Mở hết thì cánh quay vào trong bếp (x nhỏ hơn mặt trong tường) và nằm sát tường bắc bếp, không lấn vào tường.
            var leaf = door.hinge.GetComponentInChildren<Renderer>();
            var closed = door.hinge.localRotation;
            door.hinge.localRotation = closed * Quaternion.Euler(0f, door.openAngle, 0f);
            float openX = ShipLocal(layout.transform, leaf.bounds).center.x;
            var inGalley = layout.galley.InverseTransformPoint(leaf.bounds.center);
            float halfThickness = Mathf.Min(leaf.bounds.extents.x, leaf.bounds.extents.z);
            door.hinge.localRotation = closed;
            Assert.Less(openX, InnerWallX() - 0.2f, "cánh cửa mở vào trong");
            Assert.LessOrEqual(inGalley.z + halfThickness, ShipInterior.NorthWallInner, "cánh mở không lấn vào tường bắc");
            Assert.Greater(inGalley.z, ShipInterior.NorthWallInner - 0.1f, "cánh mở áp sát tường bắc, không chắn lối vào");
            Assert.AreEqual("prompt.galley_door", door.target.promptKey);
        }

        [Test]
        public void CorridorDoorMatchesStarboardOpening([ValueSource(nameof(Scenes))] string scene)
        {
            var layout = Open(scene);
            var opening = DeckZones.Opening(DeckZones.Read(), "corridor_starboard_door");
            var center = DeckZones.Point(opening["center"]);
            var size = ShipCoordinates.SizeFromBlender(DeckZones.Vector(opening["size"]));
            var door = layout.corridorDoor;
            Assert.IsNotNull(door);
            var root = layout.transform.InverseTransformPoint(door.transform.position);
            Assert.AreEqual(center.x, root.x, PlaneTolerance);
            Assert.AreEqual(center.y, root.y + size.y * 0.5f, PlaneTolerance);
            Assert.AreEqual(center.z, root.z, 0.05f);
            var aperture = new Bounds(new Vector3(center.x, center.y + 0.05f, center.z), new Vector3(size.x - 0.1f, size.y - 0.2f, 0.12f));
            AssertClear(layout, aperture, door.transform, "cửa hông hành lang");
        }

        [Test]
        public void StairsStayWithinCharacterSlopeLimit([ValueSource(nameof(Scenes))] string scene)
        {
            var layout = Open(scene);
            var cc = Object.FindAnyObjectByType<PlayerRig>().GetComponent<CharacterController>();
            var stairs = layout.stairs.GetComponentsInChildren<ShipStair>();
            var data = DeckZones.Read();
            Assert.AreEqual(data["connections"].Count(), stairs.Length, "mỗi lối nối một ShipStair");
            foreach (var stair in stairs)
            {
                TestContext.WriteLine($"{stair.id}: {stair.SlopeDegrees:F1}°, {stair.steps} bậc");
                Assert.Less(stair.SlopeDegrees, cc.slopeLimit - 2f, stair.id);
                if (stair.steps > 0)
                    Assert.LessOrEqual((stair.top.y - stair.bottom.y) / stair.steps, cc.stepOffset, stair.id + ": bậc cao quá stepOffset");
            }
            if (layout.gangway != null)
            {
                var ramp = layout.gangway.Find("GangwayRamp");
                Assert.IsNotNull(ramp);
                float slope = Vector3.Angle(ramp.forward, Vector3.ProjectOnPlane(ramp.forward, Vector3.up));
                TestContext.WriteLine($"Gangway: {slope:F1}°");
                Assert.Less(slope, cc.slopeLimit - 2f, "cầu thang tàu");
            }
        }

        /// <summary>
        /// Người chơi, chỗ tối, chỗ đứng cầu cảng: chân trên sàn, capsule người chơi (0,26 m) không lấn vật cản. NPC đứng
        /// sát mặt lò theo lộ trình M10 (giữ nguyên): chỉ kiểm lõi thân (0,08 m).
        /// </summary>
        [Test]
        public void SpawnPointsStandOnWalkableFloor([ValueSource(nameof(Scenes))] string scene)
        {
            var layout = Open(scene);
            var rig = Object.FindAnyObjectByType<PlayerRig>();
            var points = new List<(string name, Transform t, float radius)> { ("Player", rig.transform, 0.26f) };
            if (layout.eveningSpawn != null)
                points.Add(("EveningSpawn", layout.eveningSpawn, 0.26f));
            foreach (var npc in Object.FindObjectsByType<NpcAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                points.Add((npc.name, npc.transform, 0.08f));
            var quay = GameObject.Find("QuayPoint");
            if (quay != null)
                points.Add(("QuayPoint", quay.transform, 0.26f));
            foreach (var (name, t, radius) in points)
            {
                var p = t.position;
                bool hit = Physics.Raycast(p + Vector3.up * 0.6f, Vector3.down, out var floor, 1.2f, ~0, QueryTriggerInteraction.Ignore);
                Assert.IsTrue(hit, $"{scene}/{name} không có sàn dưới chân ở {p}");
                Assert.Greater(floor.normal.y, 0.7f, $"{scene}/{name} đứng trên mặt dốc {floor.collider.name}");
                Assert.AreEqual(p.y, floor.point.y, 0.15f, $"{scene}/{name}: sàn {floor.collider.name} ở {floor.point.y:F2}");
                var blockers = Physics.OverlapCapsule(p + Vector3.up * 0.4f, p + Vector3.up * 1.45f, radius, ~0, QueryTriggerInteraction.Ignore)
                    .Where(c => !c.transform.IsChildOf(t) && !(c is CharacterController))
                    .Select(c => Path(c.transform)).ToArray();
                Assert.IsEmpty(blockers, $"{scene}/{name} kẹt trong vật cản");
            }
        }

        [Test]
        public void EveryRendererHasALightLayer([ValueSource(nameof(Scenes))] string scene)
        {
            Open(scene);
            const uint layers = WorldRenderingLayers.Interior | WorldRenderingLayers.Exterior;
            var missing = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(r => !(r is ParticleSystemRenderer) && (r.renderingLayerMask & layers) == 0)
                .Select(r => Path(r.transform)).Take(20).ToArray();
            Assert.IsEmpty(missing, scene + ": renderer không thuộc Interior/Exterior thì không đèn nào chiếu");
        }

        [Test]
        public void ShipZonesMarkGalleyAndHoldAsInterior([ValueSource(nameof(Scenes))] string scene)
        {
            var layout = Open(scene);
            Assert.AreEqual(ShipRegion.Interior, layout.galleyZone.region);
            Assert.AreEqual(ShipRegion.Interior, layout.holdZone.region);
            var galley = ShipLocal(layout.transform, layout.galleyZone.GetComponent<Collider>().bounds);
            var door = DeckZones.Point(DeckZones.Opening(DeckZones.Read(), "galley_door")["center"]);
            // Đứng sát cửa ngoài boong (tâm capsule cách mặt ngoài vỏ 0,3 m) là ngoài trời.
            Assert.Less(galley.max.x, door.x + 0.02f, "vùng trong nhà không lấn ra boong giếng");
        }

        static float InnerWallX() => ShipCoordinates.FromGalley(new Vector3(ShipInterior.WestWallInner, 0f, 0f)).x;

        static void AssertClear(ShipLayout layout, Bounds shipLocalBox, Transform door, string label)
        {
            var center = layout.transform.TransformPoint(shipLocalBox.center);
            var hits = Physics.OverlapBox(center, shipLocalBox.extents, layout.transform.rotation, ~0, QueryTriggerInteraction.Ignore)
                .Where(c => !c.transform.IsChildOf(door))
                .Select(c => Path(c.transform)).ToArray();
            Assert.IsEmpty(hits, label + " bị chắn");
        }

        static Transform Find(Transform root, string name)
        {
            var t = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(c => c.name == name);
            Assert.IsNotNull(t, "thiếu " + name);
            return t;
        }

        static string Path(Transform t)
        {
            var names = new List<string>();
            for (var c = t; c != null; c = c.parent)
                names.Insert(0, c.name);
            return string.Join("/", names);
        }
    }

    /// <summary>deck_zones.json v2: đồ thị lối nối, ô mở, phép đổi hệ bếp ↔ tàu.</summary>
    public class DeckZonesTests
    {
        static JObject Data => DeckZones.Read();

        [Test]
        public void ConnectionsFromForeWellReachEveryZone()
        {
            var zones = DeckZones.Zones(Data).Select(z => z.Key).ToArray();
            var reached = DeckZones.Reachable(Data, "fore_well");
            Assert.That(zones, Is.SubsetOf(reached), "từ boong giếng trước phải tới được mọi vùng");
            Assert.AreEqual(8, zones.Length);
        }

        [Test]
        public void ConnectionsJoinKnownZonesAndClimbUpward()
        {
            var zones = new HashSet<string>(DeckZones.Zones(Data).Select(z => z.Key));
            foreach (var link in Data["connections"])
            {
                string id = (string)link["id"];
                Assert.IsTrue(zones.Contains((string)link["from"]), id);
                Assert.IsTrue(zones.Contains((string)link["to"]), id);
                var bottom = DeckZones.Point(link["bottom"]);
                var top = DeckZones.Point(link["top"]);
                if ((int)link["steps"] > 0)
                {
                    Assert.Greater(top.y, bottom.y, id);
                    float slope = Mathf.Atan2(top.y - bottom.y, new Vector2(top.x - bottom.x, top.z - bottom.z).magnitude) * Mathf.Rad2Deg;
                    Assert.Less(slope, 48f, id);
                }
                else
                {
                    Assert.AreEqual(bottom.y, top.y, 0.001f, id);
                }
            }
        }

        [Test]
        public void OpeningsAndVolumesExist()
        {
            foreach (string id in new[] { "galley_door", "galley_porthole", "corridor_starboard_door", "hold_companion_door", "hold_deck_opening" })
                Assert.IsNotNull(DeckZones.Opening(Data, id), id);
            Assert.IsNotNull(DeckZones.Opening(Data, "galley_porthole")["glass_center"]);
            Assert.Greater(DeckZones.Volume(Data, "galley_block").size.x, 10f);
            Assert.Greater(DeckZones.Volume(Data, "hold").size.x, 9f);
        }

        /// <summary>Công thức trong JSON: x = −lx − 0,20; y = −lz − 0,85; z = ly + 5,60 (Blender).</summary>
        [Test]
        public void GalleyTransformMatchesJsonFormula()
        {
            var samples = new[] { Vector3.zero, new Vector3(-5f, 1.55f, 0.6f), new Vector3(6f, 0f, 4.36f), new Vector3(1.5f, 2.6f, -4.35f) };
            foreach (var p in samples)
            {
                var blender = ShipCoordinates.ToBlender(ShipCoordinates.FromGalley(p));
                Assert.AreEqual(-p.x - 0.2f, blender.x, 1e-4f);
                Assert.AreEqual(-p.z - 0.85f, blender.y, 1e-4f);
                Assert.AreEqual(p.y + 5.6f, blender.z, 1e-4f);
                Assert.Less(Vector3.Distance(p, ShipCoordinates.ToGalley(ShipCoordinates.FromGalley(p))), 1e-4f);
            }
        }

        [Test]
        public void WalkableFloorsHaveArea()
        {
            foreach (var pair in DeckZones.Zones(Data))
            {
                Assert.Greater(pair.Value["walkable_polygons"].Count(), 0, pair.Key);
                var mesh = ShipWalkable.FloorMesh(DeckZones.Polygon(pair.Value["walkable_polygons"][0]),
                    pair.Value["deck_holes"]?.Select(DeckZones.Polygon).ToArray() ?? new Vector2[0][], pair.Value);
                try
                {
                    Assert.Greater(mesh.triangles.Length, 0, pair.Key);
                    Assert.IsTrue(mesh.normals.All(n => n.y > 0.99f), pair.Key + ": sàn quay lên");
                }
                finally
                {
                    Object.DestroyImmediate(mesh);
                }
            }
        }
    }

    public class ShipMotionTests
    {
        [Test]
        public void MixedSwayStaysBoundedAndDoesNotRepeatAfterOneRollPeriod()
        {
            var owner = new GameObject("Kiểm lắc tàu");
            try
            {
                var ship = owner.AddComponent<ShipMotion>();
                float calmPeak = 0f, difference = 0f;
                for (int i = 0; i < 6000; i++)
                {
                    float time = i * 0.02f;
                    float sample = ShipMotion.SampleSway(time, ship.rollPeriod);
                    Assert.LessOrEqual(Mathf.Abs(sample), 1f);
                    difference += Mathf.Abs(sample - ShipMotion.SampleSway(time + ship.rollPeriod, ship.rollPeriod));
                    ship.Intensity = 0.15f;
                    ship.Step(0.02f);
                    calmPeak = Mathf.Max(calmPeak, Mathf.Abs(ship.Roll));
                    Assert.LessOrEqual(Mathf.Abs(ship.Roll), 2f);
                    Assert.LessOrEqual(Mathf.Abs(ship.Pitch), 0.5f);
                    Assert.LessOrEqual(Mathf.Abs(ship.HeaveFraction * 0.08f), 0.012f);
                }
                Assert.Greater(calmPeak, 1f);
                Assert.Greater(difference / 6000f, 0.08f, "Không lặp lại như sin đơn sau một chu kỳ roll");
                foreach (float intensity in new[] { 0f, 0.08f, 0.35f, 0.6f, 1f })
                {
                    ship.Intensity = intensity;
                    for (int i = 0; i < 1000; i++)
                    {
                        ship.Step(0.02f);
                        Assert.LessOrEqual(Mathf.Abs(ship.Roll), ship.maxRollDegrees);
                        Assert.LessOrEqual(Mathf.Abs(ship.Pitch), 1.5f);
                        Assert.LessOrEqual(Mathf.Abs(ship.HeaveFraction), intensity);
                        Assert.LessOrEqual(ship.LateralAccel.magnitude, ship.maxLateralAccel);
                        if (intensity == 0f)
                        {
                            Assert.AreEqual(0f, ship.Roll);
                            Assert.AreEqual(0f, ship.Pitch);
                            Assert.AreEqual(0f, ship.HeaveFraction);
                        }
                    }
                }
            }
            finally { Object.DestroyImmediate(owner); }
        }
    }

}
