using HaiBanTay.Editor.SceneBuilders;
using HaiBanTay.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Tests
{
    public class PlayerCostumeTests
    {
        [Test]
        public void ApronIsClosedThickPleatedAndLongEnough()
        {
            var mesh = PlayerApron.CreateMesh();
            try
            {
                var points = mesh.vertices;
                int face = points.Length / 2;
                Assert.Greater(mesh.bounds.size.y, 0.69f);
                Assert.Less(mesh.bounds.min.y, 0.40f);
                float low = float.MaxValue, high = float.MinValue;
                for (int i = 0; i < face; i++)
                {
                    Assert.That(Vector3.Distance(points[i], points[i + face]), Is.EqualTo(PlayerApron.Thickness).Within(0.00001f));
                    low = Mathf.Min(low, points[i].z); high = Mathf.Max(high, points[i].z);
                }
                Assert.Greater(high - low, 0.02f, "nếp phải có hình học, không tấm phẳng");
                // Mỗi cạnh vô hướng xuất hiện đúng hai lần: cả gấu và mép đều đóng kín.
                var edges = new System.Collections.Generic.Dictionary<(int, int), int>();
                var triangles = mesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3) for (int k = 0; k < 3; k++)
                {
                    int a = triangles[i + k], b = triangles[i + (k + 1) % 3];
                    var edge = a < b ? (a, b) : (b, a);
                    edges.TryGetValue(edge, out int count); edges[edge] = count + 1;
                }
                foreach (var edge in edges) Assert.AreEqual(2, edge.Value, "mép hở: " + edge.Key);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void GeneratedBodyHasEveryCostumePartAndTwoButtonRows()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(PlayerBodyBuilder.Folder + "/BaBody.asset");
            Assert.IsNotNull(mesh);
            Assert.AreEqual(10, mesh.subMeshCount);
            foreach (int slot in new[] { 0, 1, 3, 4, 5, 6, 7, 8, 9 }) Assert.Greater(mesh.GetIndexCount(slot), 30);
            var shirt = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/Gen_BaShirt.mat");
            var cuff = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Models/Hands/Gen_FP_Sleeve.mat");
            Assert.AreEqual(shirt.GetColor("_BaseColor"), cuff.GetColor("_BaseColor"));
            Assert.AreSame(shirt.GetTexture("_BaseMap"), cuff.GetTexture("_BaseMap"));
            Assert.LessOrEqual(shirt.GetColor("_BaseColor").maxColorComponent, 0.71f);
            Assert.AreEqual(0f, shirt.GetFloat("_Metallic"));
        }
    }
}
