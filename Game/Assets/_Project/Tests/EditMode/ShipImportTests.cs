using System.Collections.Generic;
using System.Linq;
using HaiBanTay.Editor.SceneBuilders.Ship;
using HaiBanTay.Ship;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Model tàu v4 nhập vào Unity (M10b): ba mức LOD cùng bộ object và 14 khe vật liệu, deck_zones.json là TextAsset
    /// schema 2, và phép đổi trục Blender → Unity (kể cả xoay 180° của FBX) được chứng minh trên hai object bất đối xứng.
    /// </summary>
    public class ShipImportTests
    {
        const float Tolerance = 0.02f;

        static GameObject Prefab(string name) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(ShipBuilder.PrefabFolder + name + ".prefab");

        [Test]
        public void ThreeLodsShareObjectsAndMaterialSlots()
        {
            var names = new List<HashSet<string>>();
            var triangles = new List<long>();
            foreach (string lod in ShipBuilder.LodPrefabs)
            {
                var prefab = Prefab(lod);
                Assert.IsNotNull(prefab, "thiếu prefab " + lod);
                var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
                names.Add(new HashSet<string>(renderers.Select(r => r.name)));
                long count = 0;
                foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                    for (int i = 0; i < filter.sharedMesh.subMeshCount; i++)
                        count += filter.sharedMesh.GetIndexCount(i) / 3;
                triangles.Add(count);
                var materials = new HashSet<string>(renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name));
                Assert.AreEqual(14, materials.Count, lod + ": phải giữ đủ 14 khe vật liệu, có " + string.Join(", ", materials));
                Assert.IsTrue(materials.Any(m => m.Contains("Glass")), lod + ": thiếu vật liệu kính");
            }
            Assert.AreEqual(20, names[0].Count, "model v4 có 20 object");
            Assert.IsTrue(names[0].SetEquals(names[1]) && names[0].SetEquals(names[2]), "ba mức LOD phải cùng bộ object");
            Assert.Greater(triangles[0], triangles[1]);
            Assert.Greater(triangles[1], triangles[2]);
            TestContext.WriteLine($"Tam giác LOD0/1/2: {triangles[0]} / {triangles[1]} / {triangles[2]}");
        }

        [Test]
        public void DeckZonesIsSchemaTwoTextAsset()
        {
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<TextAsset>(DeckZones.AssetPath));
            Assert.AreEqual(DeckZones.Schema, (int)DeckZones.Read()["schema_version"]);
        }

        /// <summary>
        /// HoldCompanion (Blender x 5,95…8,4, y ±0,7, z 5,56…7,8) chứng minh gốc và trục dọc; GalleyChimney (y −3,94…−3,26,
        /// lệch hẳn sang mạn phải) chứng minh chiều mạn: sai một phép lật là ống khói sang mạn trái.
        /// </summary>
        [Test]
        public void ModelMatchesBlenderAxesAfterImport()
        {
            var parent = new GameObject("ShipImportTest");
            try
            {
                ShipBuilder.BuildModel(parent.transform);
                AssertBounds(parent, "HoldCompanion", new Vector3(5.95f, -0.7f, 5.56f), new Vector3(8.4f, 0.7f, 7.8f));
                AssertBounds(parent, "GalleyChimney", new Vector3(2.06f, -3.94f, 8.45f), new Vector3(2.74f, -3.26f, 11.87f));
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void ModelRenderersAreUnbakedExteriorWithLodGroups()
        {
            var parent = new GameObject("ShipImportTest");
            try
            {
                var model = ShipBuilder.BuildModel(parent.transform);
                var groups = model.GetComponentsInChildren<LODGroup>();
                Assert.AreEqual(20, groups.Length, "mỗi object một LODGroup");
                foreach (var group in groups)
                    Assert.AreEqual(3, group.lodCount, group.name);
                foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                {
                    var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
                    Assert.AreEqual((StaticEditorFlags)0, flags & StaticEditorFlags.ContributeGI, renderer.name + " không bake lightmap");
                    Assert.AreEqual((StaticEditorFlags)0, flags & StaticEditorFlags.BatchingStatic, renderer.name);
                    Assert.AreNotEqual((StaticEditorFlags)0, flags & StaticEditorFlags.OccludeeStatic, renderer.name);
                    bool glass = renderer.sharedMaterials.Any(m => m != null && m.renderQueue >= (int)RenderQueue.GeometryLast);
                    Assert.AreEqual(!glass, (flags & StaticEditorFlags.OccluderStatic) != 0, renderer.name + ": có kính thì không che");
                    Assert.AreEqual(LightProbeUsage.Off, renderer.lightProbeUsage, renderer.name);
                    Assert.AreEqual(WorldRenderingLayers.Exterior, renderer.renderingLayerMask, renderer.name);
                }
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        /// <summary>Kính ô cửa sổ bếp là của model: các đỉnh kính quanh tâm ô cửa nằm trong bề dày vỏ, đúng glass_center.</summary>
        [Test]
        public void ModelGlassSitsInPortholeOpening()
        {
            var data = DeckZones.Read();
            var porthole = DeckZones.Opening(data, "galley_porthole");
            var center = DeckZones.Point(porthole["center"]);
            var glassCenter = DeckZones.Point(porthole["glass_center"]);
            float radius = (float)porthole["radius"];
            var parent = new GameObject("ShipImportTest");
            try
            {
                var model = ShipBuilder.BuildModel(parent.transform);
                float minX = float.MaxValue;
                float maxX = float.MinValue;
                int found = 0;
                foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
                {
                    if (!filter.name.EndsWith("_LOD0"))
                        continue;
                    var mesh = filter.sharedMesh;
                    var materials = filter.GetComponent<Renderer>().sharedMaterials;
                    var vertices = mesh.vertices;
                    for (int sub = 0; sub < mesh.subMeshCount && sub < materials.Length; sub++)
                    {
                        if (materials[sub] == null || !materials[sub].name.Contains("Glass"))
                            continue;
                        foreach (int index in mesh.GetIndices(sub))
                        {
                            var p = filter.transform.TransformPoint(vertices[index]);
                            if (new Vector2(p.y - center.y, p.z - center.z).magnitude > radius + 0.05f)
                                continue;
                            found++;
                            minX = Mathf.Min(minX, p.x);
                            maxX = Mathf.Max(maxX, p.x);
                        }
                    }
                }
                Assert.Greater(found, 0, "phải có kính của model ở ô cửa sổ bếp");
                TestContext.WriteLine($"Kính ô cửa: x {minX:F4}…{maxX:F4}, glass_center {glassCenter.x:F4}");
                Assert.AreEqual(glassCenter.x, (minX + maxX) * 0.5f, Tolerance);
                Assert.Less(maxX, center.x + 0.001f, "kính không lồi khỏi mặt ngoài vỏ");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        static void AssertBounds(GameObject root, string name, Vector3 blenderMin, Vector3 blenderMax)
        {
            var renderer = root.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name == name + "_LOD0");
            Assert.IsNotNull(renderer, "thiếu " + name);
            var bounds = renderer.bounds;
            var min = Vector3.Min(ShipCoordinates.FromBlender(blenderMin), ShipCoordinates.FromBlender(blenderMax));
            var max = Vector3.Max(ShipCoordinates.FromBlender(blenderMin), ShipCoordinates.FromBlender(blenderMax));
            TestContext.WriteLine($"{name}: min {bounds.min:F3} (cần {min:F3}), max {bounds.max:F3} (cần {max:F3})");
            for (int axis = 0; axis < 3; axis++)
            {
                Assert.AreEqual(min[axis], bounds.min[axis], Tolerance, $"{name} min[{axis}]");
                Assert.AreEqual(max[axis], bounds.max[axis], Tolerance, $"{name} max[{axis}]");
            }
        }
    }
}
