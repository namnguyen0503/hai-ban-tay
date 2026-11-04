using System.Collections.Generic;
using System.IO;
using HaiBanTay.Player;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>Đo sáng trên silhouette da chiếu từ mesh thật; không lấy cả nền trong bounding box.</summary>
    public static class HandsScreenshotCheck
    {
        public static void Verify(string name, Camera cam, HandsRig hands)
        {
            HandsShotPlayTests.AssertPalms(cam, hands);
            var texture = new Texture2D(2, 2);
            try
            {
                texture.LoadImage(File.ReadAllBytes(Path.Combine(ScreenshotCapture.Folder, name + ".png")));
                var pixels = texture.GetPixels32();
                int w = texture.width, h = texture.height;
                float oldAspect = cam.aspect;
                cam.aspect = (float)w / h;
                try
                {
                    foreach (var renderer in hands.skinRenderers)
                    {
                        if (!renderer.gameObject.activeInHierarchy) continue;
                        var mf = renderer.GetComponent<MeshFilter>();
                        var verts = mf.sharedMesh.vertices;
                        var projected = new Vector2[verts.Length];
                        for (int i = 0; i < verts.Length; i++)
                        {
                            var p = cam.WorldToViewportPoint(mf.transform.TransformPoint(verts[i]));
                            projected[i] = new Vector2(p.x*w, p.y*h);
                        }
                        var mask = new bool[w*h];
                        var tris = mf.sharedMesh.triangles;
                        for (int t = 0; t < tris.Length; t += 3)
                        {
                            var a = projected[tris[t]]; var b = projected[tris[t+1]]; var c = projected[tris[t+2]];
                            int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x,b.x,c.x)),0,w-1);
                            int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x,b.x,c.x)),0,w-1);
                            int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y,b.y,c.y)),0,h-1);
                            int y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y,b.y,c.y)),0,h-1);
                            float area = Cross(b-a,c-a);
                            if (Mathf.Abs(area) < 0.01f) continue;
                            for (int y=y0; y<=y1; y++) for (int x=x0; x<=x1; x++)
                            {
                                var p = new Vector2(x+0.5f,y+0.5f);
                                float u = Cross(b-p,c-p)/area, v = Cross(c-p,a-p)/area;
                                if (u>=0 && v>=0 && u+v<=1) mask[y*w+x] = true;
                            }
                        }
                        double sum = 0; int count = 0;
                        for (int i=0;i<mask.Length;i++) if(mask[i])
                        {
                            sum += (0.2126*pixels[i].r+0.7152*pixels[i].g+0.0722*pixels[i].b)/255;
                            count++;
                        }
                        Assert.Greater(count, 2000, "tay quá nhỏ để đọc hình");
                        double mean = sum/count;
                        Debug.Log($"HandsShot QA {name}/{renderer.transform.parent.name}: {count} pixels, luminance {mean:F3}");
                        Assert.GreaterOrEqual(mean, 0.20, "lòng tay quá tối trên ảnh hậu kỳ 1080p");
                    }
                    if (name == "03c_pay_coins")
                    {
                        var centers = new List<Vector2>();
                        foreach (var t in hands.GetComponentsInChildren<Transform>())
                            if (t.name.StartsWith("Coin_"))
                            {
                                var p = cam.WorldToViewportPoint(t.position);
                                centers.Add(new Vector2(p.x*w,p.y*h));
                            }
                        Assert.AreEqual(10,centers.Count);
                        for(int i=0;i<centers.Count;i++) for(int j=i+1;j<centers.Count;j++)
                            Assert.Greater(Vector2.Distance(centers[i],centers[j]), 14f, "xu chồng lên nhau, không đếm được");
                    }
                }
                finally { cam.aspect = oldAspect; }
            }
            finally { Object.Destroy(texture); }
        }
        static float Cross(Vector2 a, Vector2 b) => a.x*b.y-a.y*b.x;
    }
}
