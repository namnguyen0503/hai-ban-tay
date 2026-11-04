using System.Linq;
using HaiBanTay.Editor.SceneBuilders;
using HaiBanTay.Editor.SceneBuilders.Ship;
using HaiBanTay.Ending;
using HaiBanTay.Npc;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HaiBanTay.Tests
{
    public class MarseilleSceneTests
    {
        [Test]
        public void PayCanopyClearsShipVentilators()
        {
            EditorSceneManager.OpenScene(BuilderUtils.ScenePath("03_Marseille"));
            try
            {
                var canopy = GameObject.Find("PayCanopy").GetComponent<Renderer>().bounds;
                var ship = GameObject.Find("Ship").transform;
                int checkedCount = 0;
                foreach (var zone in DeckZones.Zones(DeckZones.Read()))
                    foreach (var obstacle in zone.Value["obstacles"])
                    {
                        if ((string)obstacle["kind"] != "cowl_ventilator") continue;
                        var center = ship.TransformPoint(DeckZones.Point(obstacle["center_absolute"]));
                        var bounds = new Bounds(center, HaiBanTay.Ship.ShipCoordinates.SizeFromBlender(DeckZones.Vector(obstacle["size"])));
                        Assert.IsFalse(canopy.Intersects(bounds), "Mái phát lương xuyên chụp thông gió: " + (string)obstacle["id"]);
                        checkedCount++;
                    }
                Assert.Greater(checkedCount, 0, "Phải kiểm hình học chụp thật của tàu.");
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); }
        }

        [Test]
        public void RequiredFeetAndWorkAnchorsHaveWalkableFloor()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/03_Marseille.unity");
            try
            {
                var d = Object.FindAnyObjectByType<EndingDirector>();
                Assert.IsNotNull(d);
                Assert.AreEqual(6, d.crew.Length);
                Assert.AreEqual(6, d.farewellTargets.Length);
                Assert.AreEqual(1, d.sweep.spots.Length);
                var layout = EnvLayout.Load("Marseille");
                Assert.Less(Vector3.Distance(d.sweep.spots[0].transform.position, layout.Anchor("last_shift_sweep")), 0.01f);
                Assert.AreEqual(25, GameObject.Find("QuayCrowd").GetComponentsInChildren<NpcAgent>(true).Length);
                Assert.AreEqual(1, Object.FindObjectsByType<CrowdLod>(FindObjectsSortMode.None).Length);
                Physics.SyncTransforms();
                var blocked = new System.Collections.Generic.List<string>();
                foreach (var npc in Object.FindObjectsByType<NpcAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var feet = npc.Position;
                    var hits = Physics.OverlapCapsule(feet + Vector3.up * 0.4f, feet + Vector3.up * 1.45f, 0.08f, ~0,
                        QueryTriggerInteraction.Ignore).Where(c => !c.transform.IsChildOf(npc.transform) && !(c is CharacterController));
                    foreach (var hit in hits) blocked.Add(npc.name + " tại " + feet + ": " + hit.name);
                }
                Assert.IsEmpty(blocked, string.Join("\n", blocked));
                foreach (var exit in d.purser.exits)
                    Assert.Greater(Vector3.Distance(exit.position, d.crew[2].Position), 0.9f, "người rời hàng đứng chồng Jacques");
                Assert.Greater(Vector3.Distance(d.purser.exits[0].position, d.purser.exits[1].position), 1f);
                d.deckCast.SetActive(false);
                d.waiter.gameObject.SetActive(false);
                Physics.SyncTransforms();
                foreach (var feet in new[] { layout.PlayerSpawn, d.payPoint.position, d.quayPoint.position,
                    layout.Anchor("street_edge"), layout.Anchor("cafe_approach"), d.waiter.counter.position,
                    d.waiter.servePoint.position, d.sweep.spots[0].transform.position })
                {
                    bool floor = Physics.RaycastAll(feet + Vector3.up * 0.2f, Vector3.down, 0.5f, ~0,
                        QueryTriggerInteraction.Ignore).Any(h => h.normal.y > 0.9f && Mathf.Abs(h.point.y - feet.y) < 0.12f);
                    Assert.IsTrue(floor, "anchor không có sàn đi được: " + feet);
                }
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); }
        }
    }
}
