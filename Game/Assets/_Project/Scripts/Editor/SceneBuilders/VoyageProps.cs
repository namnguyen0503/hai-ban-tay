using System.Collections.Generic;
using HaiBanTay.Editor.Setup;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>Đạo cụ hầm thực phẩm có dáng riêng; mesh/vật liệu dùng chung, không physics bổ sung.</summary>
    public static class VoyageProps
    {
        static GameObject MeshObject(Transform parent, string name, List<Vector3> vertices, List<int> triangles, Material material)
        {
            string path = SurfaceMesh.Folder + "/Gen_" + name + "_v1.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = name };
                AssetFactory.EnsureFolder(SurfaceMesh.Folder); AssetDatabase.CreateAsset(mesh, path);
            }
            // Cập nhật tại chỗ để sửa hình học mà không đổi GUID.
            mesh.Clear(); mesh.vertices = vertices.ToArray(); mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
            var go = BuilderUtils.Group(name, parent);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            material.enableInstancing = true;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        static void Join(List<int> triangles, int offset, int rings, int sides)
        {
            for (int j = 0; j < rings - 1; j++) for (int k = 0; k < sides; k++)
            {
                int a = offset + j * sides + k, b = offset + j * sides + (k + 1) % sides;
                triangles.AddRange(new[] { a, a + sides, b, b, a + sides, b + sides });
            }
            for (int k = 1; k < sides - 1; k++)
            {
                triangles.AddRange(new[] { offset, offset + k, offset + k + 1 });
                int end = offset + (rings - 1) * sides;
                triangles.AddRange(new[] { end, end + k + 1, end + k });
            }
        }

        public static void Meat(Transform parent, Material meat, Material bone)
        {
            var v = new List<Vector3>(); var tr = new List<int>();
            const int rings = 10, sides = 18;
            for (int j = 0; j < rings; j++) for (int k = 0; k < sides; k++)
            {
                float t = j / (float)(rings - 1), a = k * Mathf.PI * 2f / sides;
                float width = .035f + .12f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI)), .7f);
                float fold = 1f + .09f * Mathf.Sin(a * 3f + t * 8f);
                v.Add(new Vector3(Mathf.Cos(a) * width * fold + .035f * t * t, .035f + t * .38f,
                    Mathf.Sin(a) * width * .76f * fold));
            }
            Join(tr, 0, rings, sides); MeshObject(parent, "ButcherLegCut", v, tr, meat);
            BuilderUtils.Prim(PrimitiveType.Capsule, "ExposedBone", parent, new Vector3(.035f,.435f,0), new Vector3(.037f,.095f,.037f), bone, false, false, new Vector3(0,0,-12));
            foreach (float x in new[] { .015f,.05f })
                BuilderUtils.Prim(PrimitiveType.Sphere, "BoneKnuckle", parent, new Vector3(x,.52f,0), new Vector3(.05f,.035f,.045f), bone, false, false);
            // Gân/mỡ nổi ngắn ở mặt cắt, không phủ trắng cả tảng thịt.
            BuilderUtils.Prim(PrimitiveType.Capsule, "FatSeam", parent, new Vector3(-.045f,.22f,.103f), new Vector3(.012f,.085f,.008f), bone, false, false, new Vector3(0,0,-25));
        }

        public static GameObject Fish(Transform parent, Material silver, Material dark)
        {
            var v = new List<Vector3>(); var tr = new List<int>();
            var widths = new[] { .006f,.023f,.040f,.044f,.039f,.026f,.006f };
            const int sides = 12;
            for (int j = 0; j < widths.Length; j++) for (int k = 0; k < sides; k++)
            {
                float a = k * Mathf.PI * 2f / sides;
                v.Add(new Vector3(Mathf.Cos(a) * widths[j], Mathf.Sin(a) * widths[j] * .82f, -.14f + j * .052f));
            }
            // Trục thân Z: đảo winding của ống theo trục Y để mặt da hướng ra ngoài.
            Join(tr, 0, widths.Length, sides);
            for (int i = 0; i < tr.Count; i += 3) (tr[i + 1], tr[i + 2]) = (tr[i + 2], tr[i + 1]);
            // Vây hai lớp tách theo pháp tuyến của vây, mỗi lớp quay mặt ra phía mình (bản cũ tách theo trục Y: vây lưng đứng hai lớp
            // trùng mặt phẳng, vây ngực lớp trên quay xuống; bộ dò SeeThroughProbe 15/10 thấy mặt sau ở vây).
            void Fin(params Vector3[] points)
            {
                int start = v.Count;
                var normal = Vector3.Cross(points[1] - points[0], points[2] - points[0]).normalized;
                for (int side = 0; side < 2; side++) foreach (var p in points) v.Add(p + normal * (side == 0 ? .001f : -.001f));
                for (int i = 1; i < points.Length - 1; i++)
                    tr.AddRange(new[] { start, start+i, start+i+1, start+points.Length, start+points.Length+i+1, start+points.Length+i });
                // Bịt chu vi vây dày 2 mm; hai mặt rời vẫn lộ mặt sau khi nhìn sát mép.
                for (int i = 0; i < points.Length; i++)
                {
                    int a = start + i, b = start + (i + 1) % points.Length, n = points.Length;
                    tr.AddRange(new[] { a, a+n, b, b, a+n, b+n });
                }
            }
            Fin(new Vector3(0,0,-.13f),new Vector3(-.065f,0,-.21f),new Vector3(0,0,-.185f),new Vector3(.065f,0,-.21f));
            Fin(new Vector3(0,.028f,-.055f),new Vector3(0,.076f,-.01f),new Vector3(0,.025f,.07f));
            foreach (float sign in new[] { -1f,1f })
                Fin(new Vector3(sign*.025f,0,.075f),new Vector3(sign*.075f,-.014f,.008f),new Vector3(sign*.028f,0,-.018f));
            var go = MeshObject(parent,"WholeFish",v,tr,silver);
            foreach (float sign in new[] { -1f,1f })
            {
                BuilderUtils.Prim(PrimitiveType.Sphere,"FishEye",go.transform,new Vector3(sign*.029f,.013f,.118f),new Vector3(.009f,.009f,.009f),dark,false,false);
                BuilderUtils.Prim(PrimitiveType.Capsule,"Gill",go.transform,new Vector3(sign*.037f,0,.075f),new Vector3(.0025f,.024f,.004f),dark,false,false,new Vector3(20,0,0));
            }
            return go;
        }

        public static void Ice(Transform parent, Material ice, Material straw)
        {
            // Vỏ bóng mỏng quanh lõi đục: đá vẫn có chiều sâu, không nhìn xuyên cả khối xuống bếp.
            ice.SetFloat("_BlendModePreserveSpecular",1); ice.SetFloat("_Surface",1); ice.SetFloat("_Blend",0); ice.SetFloat("_ZWrite",0);
            ice.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            ice.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            ice.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); ice.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            ice.SetColor("_BaseColor",new Color(.67f,.84f,.88f,.48f)); ice.SetFloat("_Smoothness", .92f); ice.renderQueue=3000;
            ice.SetShaderPassEnabled("ShadowCaster",false); EditorUtility.SetDirty(ice);
            var v=new List<Vector3>(); var tr=new List<int>();
            var outline=new[] { new Vector2(-.14f,-.15f),new Vector2(.14f,-.15f),new Vector2(.17f,-.12f),new Vector2(.17f,.12f),
                new Vector2(.14f,.15f),new Vector2(-.14f,.15f),new Vector2(-.17f,.12f),new Vector2(-.17f,-.12f) };
            for(int j=0;j<4;j++) foreach(var p in outline)
            {
                float bevel=j==0||j==3?.9f:1f;
                v.Add(new Vector3(p.x*bevel,new[] { .025f,.045f,.315f,.335f }[j],p.y*bevel));
            }
            Join(tr,0,4,8); MeshObject(parent,"BevelledIceBlock",v,tr,ice);
            var core = AssetFactory.Lit("StoryIceCore", new Color(.58f,.72f,.76f), .62f);
            var coreVertices = v.ConvertAll(p => new Vector3(p.x * .88f, .18f + (p.y - .18f) * .88f, p.z * .88f));
            MeshObject(parent,"CloudyIceCore",coreVertices,tr,core);
            var bubbles = AssetFactory.Lit("StoryIceBubbles", new Color(.85f,.90f,.89f), .35f);
            var foam = BuilderUtils.Group("IceBubbles", parent);
            for (int i = 0; i < 9; i++)
                BuilderUtils.Prim(PrimitiveType.Sphere,"Bubble",foam.transform,
                    new Vector3(-.08f + (i%3)*.075f, .08f + (i/3)*.085f, -.132f + (i%2)*.26f),
                    Vector3.one * (.012f + (i%3)*.006f), bubbles, false, false);
            KeppelPortBuilder.Combine(foam.transform, "Env13_IceBubbles");
            var fibers=BuilderUtils.Group("PackingStraw",parent);
            for(int i=0;i<12;i++)
            {
                var a=new Vector3(-.18f+(i%4)*.11f,.02f+(i%3)*.013f,-.14f+(i/4)*.12f);
                var b=a+new Vector3(.11f*Mathf.Sin(i*2f),.008f,.16f*Mathf.Cos(i*2f));
                BuilderUtils.Prim(PrimitiveType.Cylinder,"Straw",fibers.transform,(a+b)*.5f,new Vector3(.004f,(b-a).magnitude*.5f,.004f),straw,false,false,
                    Quaternion.FromToRotation(Vector3.up,b-a).eulerAngles);
            }
        }
    }
}
