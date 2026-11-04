using HaiBanTay.Editor.SceneBuilders;
using HaiBanTay.Ship;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    public sealed class Env13GeometryTests
    {
        [Test]
        public void WallPiecesShareUvAtEveryJoinCorner()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var source = cube.GetComponent<MeshFilter>().sharedMesh;
                var aSize = new Vector3(.1f,1f,3f); var aOrigin = new Vector3(-5f,.5f,0f);
                var bSize = new Vector3(.1f,1.6f,3f); var bOrigin = new Vector3(-5f,1.8f,0f);
                var a = SurfaceMesh.For(PrimitiveType.Cube,source,aSize,2.5f,aOrigin);
                var b = SurfaceMesh.For(PrimitiveType.Cube,source,bSize,2.5f,bOrigin);
                int matched = 0;
                for(int i=0;i<a.vertexCount;i++)
                {
                    if(a.normals[i].x < .99f) continue;
                    var position = Vector3.Scale(a.vertices[i],aSize)+aOrigin;
                    if(Mathf.Abs(position.y-1f) > .001f) continue;
                    for(int j=0;j<b.vertexCount;j++)
                    {
                        if(b.normals[j].x < .99f || Vector3.Distance(position,Vector3.Scale(b.vertices[j],bSize)+bOrigin) > .001f) continue;
                        Assert.Less(Vector2.Distance(a.uv[i],b.uv[j]),.0001f,"Pha texture phải liền tại seam");
                        matched++;
                    }
                }
                Assert.AreEqual(2,matched,"Phải đối chiếu cả hai đầu seam, không bỏ qua phép đo");
            }
            finally { Object.DestroyImmediate(cube); }
        }

        [Test]
        public void StormChangesSkyLightAndWaterAndCalmRestoresThem()
        {
            var root = new GameObject("Env13WeatherTest");
            try
            {
                var motion = root.AddComponent<ShipMotion>();
                var clock = root.AddComponent<TimeOfDay>();
                clock.ship=motion; clock.water=WaterKind.Sea; clock.hour=12f;
                motion.Intensity=.15f; clock.Apply(); var calm=clock.State;
                motion.Intensity=1f; clock.Apply(); var storm=clock.State;
                Assert.Less(storm.Sky.b,calm.Sky.b);
                Assert.Less(storm.LightIntensity,calm.LightIntensity);
                Assert.Less(storm.DeepWater.b,calm.DeepWater.b);
                motion.Intensity=.15f; clock.Apply();
                Assert.AreEqual(calm.Sky,clock.State.Sky);
                Assert.AreEqual(calm.LightIntensity,clock.State.LightIntensity);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
