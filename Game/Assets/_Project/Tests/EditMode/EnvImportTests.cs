using System.IO;
using System.Linq;
using HaiBanTay.Editor.Import;
using HaiBanTay.Editor.SceneBuilders;
using HaiBanTay.Editor.SceneBuilders.Ship;
using HaiBanTay.Ship;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace HaiBanTay.Tests
{
    public class EnvImportTests
    {
        [TestCaseSource(typeof(EnvImporter), nameof(EnvImporter.Areas))]
        public void PrefabAndUnchangedLayoutAreAvailable(string area)
        {
            var layout = EnvLayout.Load(area);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(layout.PrefabPath));
            string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Staging/art_env", area, "layout.json"));
            string copied = Path.Combine(Application.dataPath, "_Project/Art/Models/Env", area, "layout.json");
            CollectionAssert.AreEqual(File.ReadAllBytes(source), File.ReadAllBytes(copied));
            Assert.IsNotEmpty(layout.Anchors);
            Assert.Greater(layout.CrowdCapacity, 0);
            foreach (var path in layout.Paths)
            {
                CollectionAssert.AreEqual(path.Points, layout.Path(path.Name));
                Assert.GreaterOrEqual(path.Points.Length, 2);
            }
            foreach (var spot in layout.Spots) Assert.IsNotEmpty(spot.Points);
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => layout.Anchor("missing_anchor"));
        }

        [TestCaseSource(typeof(EnvImporter), nameof(EnvImporter.Areas))]
        public void AnchorsAreFiniteAndGameplayAnchorsBelongToTheirRegions(string area)
        {
            var layout = EnvLayout.Load(area);
            foreach (var anchor in layout.Anchors)
            {
                Assert.IsTrue(Finite(anchor.Value), area + "/" + anchor.Key);
                Assert.AreEqual(anchor.Value, layout.Anchor(anchor.Key));
                switch (layout.Kind(anchor.Key))
                {
                    case EnvAnchorKind.Ground:
                        Assert.IsTrue(layout.WalkBounds.Any(b => b.Contains(anchor.Value, 1f)
                            && Mathf.Abs(anchor.Value.y - b.Bounds.center.y) <= 1f), area + "/" + anchor.Key);
                        break;
                    case EnvAnchorKind.Ship:
                        Assert.IsTrue(OnShip(anchor.Value, Config(area), false),
                            area + "/" + anchor.Key + ": outside deck regions " + anchor.Value);
                        break;
                    // Existing anchor_roles classify object/light/tram/background as decoration, including basilica_silhouette.
                    case EnvAnchorKind.Decoration: break;
                }
            }
        }

        [TestCase("NhaRong")]
        [TestCase("Marseille")]
        public void GangwayFootMatchesShipConfig(string area)
        {
            Assert.LessOrEqual(Vector3.Distance(EnvLayout.Load(area).Anchor("gangway_foot"), Config(area).gangwayEnd), 0.3f);
        }

        [TestCaseSource(typeof(EnvImporter), nameof(EnvImporter.Areas))]
        public void CollisionMeshesAreHiddenDefaultLayerAndMatchWorldLayout(string area)
        {
            var layout = EnvLayout.Load(area);
            var instance = layout.Place(null);
            try
            {
                var colliders = instance.GetComponentsInChildren<MeshCollider>(true);
                var records = layout.Blockers.Concat(layout.CollisionSurfaces).ToArray();
                Assert.Greater(colliders.Length, 0);
                Assert.AreEqual(records.Length, colliders.Length);
                foreach (var record in records)
                {
                    var collider = colliders.Single(c => c.name == record.MeshName);
                    Assert.IsTrue(collider.enabled && !collider.isTrigger && collider.convex, record.Name);
                    Assert.AreEqual(0, collider.gameObject.layer);
                    Assert.IsFalse(collider.GetComponent<Renderer>().enabled);
                    var rotation = Matrix4x4.TRS(record.Center, Quaternion.Euler(0, record.Yaw, 0), Vector3.one);
                    var expected = ArtImporter.TransformBounds(new Bounds(Vector3.zero, record.Size), rotation);
                    Assert.Less(Vector3.Distance(expected.center, collider.bounds.center), 0.02f, area + "/" + record.Name);
                    Assert.Less(Vector3.Distance(expected.size, collider.bounds.size), 0.03f, area + "/" + record.Name);
                }
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [TestCaseSource(typeof(EnvImporter), nameof(EnvImporter.Areas))]
        public void GroundSpawnHitsEnvironmentColliderAndShipSpawnIsOnDeck(string area)
        {
            var layout = EnvLayout.Load(area);
            if (layout.PlayerSpawnKind == EnvAnchorKind.Ship)
            {
                Assert.IsTrue(OnShip(layout.PlayerSpawn, Config(area), true), area + ": ship spawn");
                return;
            }
            var instance = layout.Place(null);
            try
            {
                Physics.SyncTransforms();
                var ray = new Ray(layout.PlayerSpawn + Vector3.up * 0.5f, Vector3.down);
                bool hit = false;
                float nearest = float.MaxValue;
                RaycastHit floor = default;
                foreach (var collider in instance.GetComponentsInChildren<MeshCollider>())
                    if (collider.Raycast(ray, out var candidate, 1f) && candidate.distance < nearest)
                    { floor = candidate; nearest = candidate.distance; hit = true; }
                Assert.IsTrue(hit, area + ": no COL floor under spawn");
                Assert.IsTrue(floor.collider.name.StartsWith("COL_"));
                Assert.Greater(floor.normal.y, 0.9f);
                Assert.AreEqual(layout.PlayerSpawn.y, floor.point.y, 0.03f);
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [TestCaseSource(typeof(EnvImporter), nameof(EnvImporter.Areas))]
        public void VisualsUseUrpSurfacesAndProbesWithoutBakedLighting(string area)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnvLayout.Load(area).PrefabPath);
            foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true).Where(r => !r.name.StartsWith("COL_")))
            {
                var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
                Assert.AreEqual((StaticEditorFlags)0, flags & StaticEditorFlags.ContributeGI, renderer.name);
                Assert.AreNotEqual((StaticEditorFlags)0, flags & StaticEditorFlags.OccludeeStatic, renderer.name);
                Assert.AreEqual(LightProbeUsage.BlendProbes, renderer.lightProbeUsage);
                Assert.AreEqual(ReceiveGI.LightProbes, renderer.receiveGI);
                Assert.AreEqual(WorldRenderingLayers.Exterior, renderer.renderingLayerMask);
                foreach (var material in renderer.sharedMaterials)
                {
                    Assert.IsNotNull(material);
                    Assert.AreEqual("Universal Render Pipeline/Lit", material.shader.name);
                    Assert.IsTrue(material.name.StartsWith("Gen_Env_"));
                    string slot = material.name.Substring("Gen_Env_".Length);
                    var surface = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/Gen_" + slot + ".mat");
                    if (surface == null) continue;
                    Assert.AreSame(surface.GetTexture("_BaseMap"), material.GetTexture("_BaseMap"));
                    Assert.AreSame(surface.GetTexture("_BumpMap"), material.GetTexture("_BumpMap"));
                    if (surface.GetTexture("_BaseMap") == null) continue; // Flat colors have no texture repetition.
                    float expected = 1f / HaiBanTay.Editor.Setup.AssetFactory.SurfaceTile(surface);
                    Assert.AreEqual(Vector2.one * expected, material.GetTextureScale("_BaseMap"), material.name);
                    Assert.AreEqual(Vector2.one * expected, material.GetTextureScale("_BumpMap"), material.name);
                }
            }
        }

        [Test]
        public void PlacePreservesWorldGeometryUnderTransformedParent()
        {
            var layout = EnvLayout.Load("NhaRong");
            var instance = layout.Place(null);
            var reference = instance.GetComponentsInChildren<MeshCollider>().Single(c => c.name == "COL_QuayFoundation").bounds;
            Object.DestroyImmediate(instance);
            var parent = new GameObject("EnvPlacementTest");
            try
            {
                parent.transform.SetPositionAndRotation(new Vector3(15, 2, 8), Quaternion.Euler(0, 47, 0));
                parent.transform.localScale = Vector3.one * 2f;
                instance = layout.Place(parent.transform);
                var result = instance.GetComponentsInChildren<MeshCollider>().Single(c => c.name == "COL_QuayFoundation").bounds;
                Assert.Less(Vector3.Distance(reference.center, result.center), 0.001f);
                Assert.Less(Vector3.Distance(reference.size, result.size), 0.001f);
            }
            finally { Object.DestroyImmediate(parent); }
        }

        static ShipConfig Config(string area) => area == "NhaRong" ? ShipConfig.NhaRong : ShipConfig.Marseille;
        static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z)
            && !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z);

        static bool OnShip(Vector3 world, ShipConfig config, bool feet)
        {
            var local = Quaternion.Inverse(Quaternion.Euler(0, config.yaw, 0)) * (world - config.position);
            var point = ShipCoordinates.ToBlender(local);
            var planar = new Vector2(point.x, point.y);
            var data = DeckZones.Read();
            foreach (var pair in DeckZones.Zones(data))
            {
                var zone = pair.Value;
                // Polygon inset is 0.11m at the gangway opening. Allow 0.15m around outer edges, never across deck holes.
                bool inPolygon = zone["walkable_polygons"].Any(p => InsideOrBeside(DeckZones.Polygon(p), planar));
                bool inHole = zone["deck_holes"] != null && zone["deck_holes"].Any(p => DeckZones.Contains(DeckZones.Polygon(p), planar));
                float height = point.z - DeckZones.Surface(zone, point.x);
                if (inPolygon && !inHole && height >= -0.15f && height <= (feet ? 0.15f : 1.5f)) return true;
            }
            // The galley is an interior volume, deliberately excluded from the exterior promenade polygons.
            if (DeckZones.Volume(data, "galley_block").Contains(local))
            {
                float floor = (float)data["interior_volumes"]["galley_block"]["coordinate_transform"]["translation"][2];
                if (!feet || Mathf.Abs(point.z - floor) <= 0.15f) return true;
            }
            // Stair landings can sit over a deck hole; their explicit endpoints are also part of deck_zones.
            return data["connections"].Any(c => Vector3.Distance(local, DeckZones.Point(c["top"])) <= 0.3f
                || Vector3.Distance(local, DeckZones.Point(c["bottom"])) <= 0.3f);
        }

        static bool InsideOrBeside(Vector2[] polygon, Vector2 point)
        {
            if (DeckZones.Contains(polygon, point)) return true;
            for (int i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i];
                var edge = polygon[(i + 1) % polygon.Length] - a;
                float t = edge.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude) : 0f;
                if (Vector2.Distance(a + edge * t, point) <= 0.15f) return true;
            }
            return false;
        }
    }
}
