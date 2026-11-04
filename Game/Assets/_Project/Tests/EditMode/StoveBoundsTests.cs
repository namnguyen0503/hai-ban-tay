using System.Linq;
using HaiBanTay.Editor.Import;
using HaiBanTay.Editor.SceneBuilders;
using HaiBanTay.Shift;
using HaiBanTay.Ship;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>Hồi quy tấm than chìa ra ngoài và lửa trên mặt bếp. Không nới sai số 2 cm.</summary>
    public class StoveBoundsTests
    {
        [SetUp]
        public void OpenGalley() => EditorSceneManager.OpenScene(BuilderUtils.ScenePath("02_Galley"), OpenSceneMode.Single);

        static Bounds InRange(Transform range, Renderer renderer)
        {
            var mesh = renderer.GetComponent<MeshFilter>();
            return ArtImporter.TransformBounds(mesh != null ? mesh.sharedMesh.bounds : renderer.localBounds,
                range.worldToLocalMatrix * renderer.transform.localToWorldMatrix);
        }

        [Test]
        public void NoStoveRendererProtrudesBeyondBodyByMoreThanTwoCentimetres()
        {
            var roots = new[] { GameObject.Find("ArtSlot_Stove_A").transform.Find("CastRange"), GameObject.Find("ArtSlot_PotRange").transform };
            foreach (var range in roots)
            {
                var body = range.GetComponent<BoxCollider>();
                var allowed = new Bounds(body.center, body.size);
                allowed.Expand(0.04f); // 2 cm mỗi phía, không cộng thành 4 cm mỗi phía.
                var renderers = range.GetComponentsInChildren<Renderer>(true);
                Assert.Greater(renderers.Length, 40, "Không được bỏ hình học để né test.");
                foreach (var renderer in renderers)
                {
                    var path = renderer.transform;
                    bool excepted = false;
                    while (path != range)
                    {
                        if (path.name == "Chimney" || path.name == "Handle") excepted = true;
                        path = path.parent;
                    }
                    if (excepted) continue;
                    var bounds = InRange(range, renderer);
                    Assert.IsTrue(allowed.Contains(bounds.min) && allowed.Contains(bounds.max),
                        $"{range.name}/{renderer.name}: bounds {bounds} ra ngoài thân {allowed}");
                }
            }
            Assert.IsNull(GameObject.Find("ArtSlot_Stove_B"), "Lò phụ đã gộp, không được dựng lại hộp đen thừa.");
        }

        [Test]
        public void NorthwestCornerRouteClearsFurnitureAtPlayerHeight()
        {
            Physics.SyncTransforms();
            // Rà cả đoạn thoát góc với capsule thật; NPC di chuyển không phải đồ đạc chặn lối cố định.
            for (int i = 0; i <= 20; i++)
            {
                var stand = Vector3.Lerp(new Vector3(-4.36f, 0f, 1.24f), new Vector3(-3.05f, 0f, 1.15f), i / 20f);
                var blockers = Physics.OverlapCapsule(stand + Vector3.up * 0.4f, stand + Vector3.up * 1.45f, 0.3f,
                    ~(1 << BuilderUtils.IgnoreRaycastLayer), QueryTriggerInteraction.Ignore)
                    .Where(c => c.GetComponentInParent<HaiBanTay.Player.PlayerRig>() == null &&
                                c.GetComponentInParent<HaiBanTay.Npc.NpcAgent>() == null).ToArray();
                Assert.IsEmpty(blockers, "Lối thoát góc bị đồ đạc chặn tại " + stand + ": " + string.Join(", ", blockers.Select(c => c.name)));
            }
        }

        [Test]
        public void CoalAndFlamesStayInDeepHearthBelowHotplate()
        {
            foreach (var range in new[] { GameObject.Find("ArtSlot_Stove_A").transform.Find("CastRange"), GameObject.Find("ArtSlot_PotRange").transform })
            {
                float front = range.GetComponent<BoxCollider>().size.z * 0.5f;
                var back = InRange(range, range.Find("BackWall").GetComponent<Renderer>());
                Assert.GreaterOrEqual(front - back.max.z, 0.25f);
                var embers = range.GetComponentsInChildren<MeshRenderer>().Where(r => r.name == "Ember").ToArray();
                Assert.GreaterOrEqual(embers.Length, 20);
                foreach (var ember in embers)
                {
                    var bounds = InRange(range, ember);
                    Assert.Greater(bounds.size.y, 0.03f, "Than phải có độ dày.");
                    Assert.Less(bounds.max.z, front - 0.10f, "Than phải ở trong cửa.");
                    Assert.Less(bounds.max.y, 0.54f);
                    Assert.Greater(bounds.min.y, 0.40f, "Than phải nằm trên ghi.");
                }
                foreach (var flame in range.GetComponentsInChildren<ParticleSystem>())
                {
                    var bounds = InRange(range, flame.GetComponent<Renderer>());
                    Assert.Less(bounds.max.z, front);
                    Assert.Less(bounds.max.y, 0.85f, "Lửa không xuyên hotplate.");
                    Assert.Greater(bounds.min.z, -front, "Lửa không xuyên vách sau.");
                }
            }
        }

        [Test]
        public void NpcWorkPointsClearRangeBody()
        {
            var range = GameObject.Find("ArtSlot_Stove_A").transform.Find("CastRange").GetComponent<BoxCollider>().bounds;
            var cook = Object.FindAnyObjectByType<HaiBanTay.Npc.CookRoutine>();
            var helper = Object.FindAnyObjectByType<HaiBanTay.Npc.HelperRoutine>();
            var meals = Object.FindAnyObjectByType<HaiBanTay.Shift.MealPots>();
            // Chỗ đứng khuấy trước từng ô bếp lò bắc (CookRoutine.StandFor), chỗ trông bếp, chỗ xúc than, chỗ nạp than từng cửa.
            helper.ScoopStand(out var scoop, out _);
            var points = new System.Collections.Generic.List<Vector3> { cook.watchSpot, scoop };
            foreach (var hob in meals.hobs.Where(h => h.position.z > 0f))
                points.Add(new Vector3(hob.position.x, 0f, hob.position.z - 0.5f - cook.standOff));
            foreach (var cell in helper.coal.cells)
            {
                helper.FeedStand(cell.mouth.transform, out var stand, out _);
                points.Add(stand);
            }
            foreach (var point in points.Where(p => p.z > 0f))
            {
                var center = point + Vector3.up * 0.45f;
                Assert.GreaterOrEqual(Mathf.Sqrt(range.SqrDistance(center)), 0.24f, $"Capsule NPC ở {point} không xuyên thân bếp.");
            }
        }

        [Test]
        public void SweepSpotTwoHasAnOpenStandingPlaceAndUnobstructedView()
        {
            var sweep = Object.FindAnyObjectByType<SweepStation>();
            var spot = sweep.spots[2];
            var stand = spot.transform.position + new Vector3(0f, 0f, 0.75f);
            Physics.SyncTransforms();
            var blockers = Physics.OverlapCapsule(stand + Vector3.up * 0.4f, stand + Vector3.up * 1.45f, 0.3f,
                ~(1 << BuilderUtils.IgnoreRaycastLayer), QueryTriggerInteraction.Ignore)
                .Where(c => c.GetComponentInParent<HaiBanTay.Player.PlayerRig>() == null).ToArray();
            Assert.IsEmpty(blockers, "Chỗ đứng quét phải đủ cho capsule người chơi thật: " + string.Join(", ", blockers.Select(c => c.name)));
            var eye = stand + Vector3.up * 1.62f;
            var aim = spot.GetComponent<Collider>().bounds.center;
            var hits = Physics.RaycastAll(eye, (aim - eye).normalized, 2f,
                ~(1 << BuilderUtils.IgnoreRaycastLayer), QueryTriggerInteraction.Collide)
                .Where(h => h.collider.GetComponentInParent<HaiBanTay.Player.PlayerRig>() == null).OrderBy(h => h.distance).ToArray();
            Assert.IsNotEmpty(hits, "Phải thấy collider vết quét trong tầm tương tác 2 m.");
            Assert.IsTrue(hits[0].collider.transform.IsChildOf(spot.transform), "Vết quét bị che bởi " + hits[0].collider.name);
        }

        [Test]
        public void WorkTargetsMatchFireAndAshDoorsAndPotsRestOnLids()
        {
            var coal = Object.FindAnyObjectByType<CoalStation>();
            var ash = Object.FindAnyObjectByType<AshStation>();
            Assert.Less(Vector3.Distance(coal.stove.transform.position, new Vector3(-3f, 0.57f, 1.935f)), 0.002f);
            Assert.IsTrue(ash.door.transform.IsChildOf(GameObject.Find("ArtSlot_Stove_A").transform));
            Assert.IsFalse(ash.door.GetComponent<Collider>().bounds.Intersects(ash.bucket.GetComponent<Collider>().bounds), "Vùng cửa tro không che xô.");
            foreach (var lid in GameObject.Find("ArtSlot_PotRange").GetComponentsInChildren<MeshFilter>().Where(m => m.name == "StoveLid"))
            {
                Assert.IsNotNull(lid.GetComponent<MeshCollider>(), "Nắp gang phải đỡ nồi bằng collider thật.");
                Assert.AreEqual(lid.sharedMesh, lid.GetComponent<MeshCollider>().sharedMesh);
            }
            Assert.Less(Vector3.Distance(ash.door.transform.position, new Vector3(-3f, 0.3f, 1.962f)), 0.002f);
            var rangeRoot = GameObject.Find("ArtSlot_PotRange").transform;
            // Ánh lửa theo từng ô (FireCellState, 15/10), không còn đèn đỏ bake cố định trong lò (LightFixtureTests kiểm phần đèn).
            Assert.IsNull(rangeRoot.Find("RangeLight"), "Không bake ánh đỏ cố định của lò.");
            Assert.AreEqual(4, rangeRoot.GetComponentsInChildren<Collider>().Count(c => c.name == "SeaRim"));
            for (int i = 0; i < 3; i++)
            {
                var pot = GameObject.Find("LoosePot_" + i);
                var bounds = pot.GetComponentsInChildren<MeshRenderer>().Select(r => r.bounds).ToArray();
                float bottom = bounds.Min(b => b.min.y);
                var lids = GameObject.Find("ArtSlot_PotRange").GetComponentsInChildren<MeshCollider>().Where(c => c.name == "StoveLid").OrderBy(c => c.bounds.center.x).ToArray();
                Assert.That(bottom, Is.InRange(lids[i].bounds.max.y - 0.001f, lids[i].bounds.max.y + 0.001f), "Đáy nồi phải chạm collider nắp, sai số tối đa 1 mm.");
                Assert.AreEqual(1, pot.GetComponentsInChildren<ParticleSystem>().Length);
                Assert.IsFalse(pot.GetComponentsInChildren<MeshRenderer>().Any(r => r.sharedMaterials.Any(m => m.IsKeywordEnabled("_EMISSION"))), "Không có lửa dán lên nồi.");
            }
        }
    }
}
