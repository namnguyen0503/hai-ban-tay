using System;
using System.Collections.Generic;
using System.Linq;
using HaiBanTay.Editor.Setup;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>Phông cảng Keppel đầu thế kỷ XX: kho mái dốc, bến cọc gỗ, tàu hơi nước, tam bản, cẩu và đồi nhiệt đới.</summary>
    public static class KeppelPortBuilder
    {
        const string Folder = "Assets/_Project/Art/Generated/World10";
        static Material stone, wall, roof, wood, iron, hull, green, dark;

        public static GameObject Build(Transform world)
        {
            Materials();
            var root = BuilderUtils.Group("Port_Singapore", world);
            Block(root.transform, "Wharf", new Vector3(-10, -5, 64), new Vector3(280, 2.4f, 22), stone);
            BuildPort(root);
            BuilderUtils.MakeDistantBackdrop(root);
            BuilderUtils.SetRenderingLayer(root, HaiBanTay.Ship.WorldRenderingLayers.Exterior);
            return root;
        }

        static void Materials()
        {
            stone = AssetFactory.Lit("QuayStone", new Color(.5f, .48f, .44f), .1f);
            wall = AssetFactory.Lit("GodownWall", new Color(.67f, .61f, .47f), .1f);
            roof = AssetFactory.Lit("GodownRoof", new Color(.32f, .17f, .095f), .08f);
            wood = AssetFactory.Lit("Wood", new Color(.4f, .27f, .16f), .15f);
            iron = AssetFactory.Lit("Iron", new Color(.22f, .22f, .23f), .4f, .6f);
            hull = AssetFactory.Lit("FarHull", new Color(.055f, .065f, .06f), .2f);
            green = AssetFactory.Lit("KeppelFoliage", new Color(.095f, .23f, .055f), .03f);
            dark = AssetFactory.Lit("KeppelOpenings", new Color(.06f, .045f, .03f), .02f);
        }

        static void BuildPort(GameObject root)
        {
            for (int i = 0; i < 9; i++)
            {
                int index = i;
                Lod(root.transform, "Godown_" + i, new Vector3(-118 + 27 * i, -3.8f, 78 + i % 2 * 3),
                    (t, detail) => Warehouse(t, index, detail), .28f);
            }
            foreach (float x in new[] { -95f, -30f, 38f, 100f })
            {
                Lod(root.transform, "TimberPier_" + x, new Vector3(x, -3.6f, 42), Pier, .28f);
                Lod(root.transform, "Crane_" + x, new Vector3(x + 6, -3.8f, 59), Crane, .12f);
            }
            Lod(root.transform, "Steamer_West", new Vector3(-63, -5.6f, 40), (t, d) => Ship(t, 54, d), .22f);
            Lod(root.transform, "Steamer_East", new Vector3(45, -5.6f, 45), (t, d) => Ship(t, 43, d), .22f);
            for (int i = 0; i < 5; i++)
                Lod(root.transform, "Sampan_" + i, new Vector3(-43 + i * 21, -5.45f, 24 + i % 2 * 9), Sampan, .1f);
            for (int i = 0; i < 22; i++)
                Lod(root.transform, "Palm_" + i, new Vector3(-135 + i * 12.5f, -3.8f, 91 + i % 3 * 8),
                    (t, d) => Palm(t, d), .12f);
            Lod(root.transform, "GreenRidge", new Vector3(-20, -5.8f, 125), Ridge, .6f);
        }

        /// <summary>Thay thân hộp FBX tại đúng neo cũ; dùng tàu mũi nhọn chung với Keppel, hai biến thể.</summary>
        public static void ReplaceDistantSteamers(GameObject environment)
        {
            Materials();
            var originals = environment.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.name.StartsWith("DistantSteamer_")).ToArray();
            if (originals.Length != 2) throw new InvalidOperationException("Phông cảng phải có hai tàu xa để thay.");
            for (int i = 0; i < originals.Length; i++)
            {
                var original = originals[i];
                var renderer = original.GetComponent<Renderer>();
                float length = Mathf.Max(renderer.bounds.size.x, renderer.bounds.size.z);
                var axis = original.transform.TransformDirection(Vector3.right);
                float yaw = -Mathf.Atan2(axis.z, axis.x) * Mathf.Rad2Deg;
                var root = BuilderUtils.Group("Env13_" + original.name, environment.transform.parent);
                root.transform.position = original.transform.position;
                root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                Ship(root.transform, length, true, i);
                Combine(root.transform, environment.name + "_Steamer" + i);
                renderer.enabled = false;
                BuilderUtils.MakeDistantBackdrop(root);
                BuilderUtils.SetRenderingLayer(root, HaiBanTay.Ship.WorldRenderingLayers.Exterior);
            }
        }

        static void Block(Transform t, string name, Vector3 p, Vector3 s, Material m) =>
            BuilderUtils.Block(name, t, p, s, m, false, false);

        static void Beam(Transform t, Vector3 a, Vector3 b, float width, Material m)
        {
            var go = BuilderUtils.Block("Beam", t, (a + b) * .5f, new Vector3(width, (b - a).magnitude, width), m, false, false);
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
        }

        // Mỗi kho/tàu/cây chọn LOD riêng; gộp theo vật liệu để chi tiết phông không tạo hàng trăm draw call.
        static void Lod(Transform parent, string name, Vector3 p, Action<Transform, bool> build, float threshold)
        {
            var root = BuilderUtils.Group(name, parent);
            root.transform.localPosition = p;
            var levels = new LOD[2];
            for (int i = 0; i < 2; i++)
            {
                var g = BuilderUtils.Group("LOD" + i, root.transform);
                build(g.transform, i == 0);
                levels[i] = new LOD(i == 0 ? threshold : .008f, Combine(g.transform, name + "_LOD" + i));
            }
            var lod = root.AddComponent<LODGroup>();
            lod.SetLODs(levels);
            lod.RecalculateBounds();
        }

        public static Renderer[] Combine(Transform group, string name)
        {
            var filters = group.GetComponentsInChildren<MeshFilter>();
            var parts = filters.GroupBy(f => f.GetComponent<Renderer>().sharedMaterial).ToArray();
            var renderers = new List<Renderer>();
            foreach (var part in parts)
            {
                var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(part.Select(f => new CombineInstance { mesh = f.sharedMesh,
                    transform = group.worldToLocalMatrix * f.transform.localToWorldMatrix }).ToArray(), true, true);
                var go = BuilderUtils.Group(part.Key.name, group);
                go.AddComponent<MeshFilter>().sharedMesh = Save(mesh, name + "_" + part.Key.name);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = part.Key;
                renderers.Add(r);
            }
            foreach (var f in filters) UnityEngine.Object.DestroyImmediate(f.gameObject);
            return renderers.ToArray();
        }

        public static Mesh Save(Mesh mesh, string name)
        {
            AssetFactory.EnsureFolder(Folder);
            mesh.name = name;
            string path = Folder + "/" + name.Replace('.', '_') + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, old);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(old);
            return old;
        }

        static void Shape(Transform t, string name, Vector3[] vertices, int[] triangles, Material m)
        {
            var mesh = new Mesh { vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = BuilderUtils.Group(name, t);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
        }

        static void Warehouse(Transform t, int index, bool detail)
        {
            float depth = 15 + index % 3 * 2;
            Block(t, "GodownWall", new Vector3(0, 2.8f, 0), new Vector3(24, 5.6f, depth), wall);
            // Mái chữ A dốc theo chiều sâu, hai đầu hồi tam giác thật.
            Shape(t, "PitchedRoof", new[] { new Vector3(-12.7f,5.6f,-depth/2-.7f),new Vector3(12.7f,5.6f,-depth/2-.7f),
                new Vector3(-12.7f,8.5f,0),new Vector3(12.7f,8.5f,0),
                new Vector3(-12.7f,5.6f,depth/2+.7f),new Vector3(12.7f,5.6f,depth/2+.7f) },
                new[] { 0,2,1,1,2,3,2,4,3,3,4,5,0,4,2,1,3,5 }, roof);
            for (int j = 0; j < 3; j++)
            {
                float x = -7.5f + j * 7.5f;
                Block(t, "LoadingDoor", new Vector3(x, 1.7f, -depth/2-.015f), new Vector3(4, 3.4f, .05f), dark);
                if (detail)
                {
                    for (int s = -1; s <= 1; s += 2)
                        Block(t, "DoorFrame", new Vector3(x + s * 2.1f, 1.8f, -depth/2-.1f), new Vector3(.18f,3.6f,.15f), wood);
                    Block(t, "Lintel", new Vector3(x, 3.6f, -depth/2-.1f), new Vector3(4.4f,.18f,.15f), wood);
                    Block(t, "Vent", new Vector3(x, 4.6f, -depth/2-.03f), new Vector3(1.4f,.5f,.08f), dark);
                    Block(t, "Cargo", new Vector3(x + 2.5f, .55f, -depth/2-2), new Vector3(1.6f,1.1f,1.3f), wood);
                }
            }
            if (detail)
            {
                Block(t, "Eaves", new Vector3(0, 5.6f, -depth/2-.65f), new Vector3(25.4f,.16f,.15f), wood);
                for (int i = -11; i <= 11; i += 2)
                    Beam(t,new Vector3(i,5.62f,-depth/2-.7f),new Vector3(i,8.52f,0),.08f,wood);
            }
        }

        static void Pier(Transform t, bool detail)
        {
            Block(t, "TimberDeck", new Vector3(0,0,0), new Vector3(9,.4f,31), wood);
            for (int z = -14; z <= 14; z += detail ? 4 : 8)
                foreach (float x in new[] { -3.8f, 3.8f })
                    Beam(t,new Vector3(x,-4,z),new Vector3(x,.6f,z),.32f,wood);
            if (detail)
            {
                for (int z = -14; z <= 14; z++)
                    Block(t,"PlankSeam",new Vector3(0,.205f,z),new Vector3(9,.02f,.035f),dark);
                foreach(float x in new[]{-3.7f,3.7f})
                    Beam(t,new Vector3(x,.45f,-14),new Vector3(x,.45f,14),.18f,wood);
            }
        }

        static void Crane(Transform t, bool detail)
        {
            Block(t,"Base",new Vector3(0,.4f,0),new Vector3(3,.8f,3),iron);
            Beam(t,new Vector3(0,.5f,0),new Vector3(0,11,0),.48f,iron);
            Beam(t,new Vector3(0,10,0),new Vector3(0,14,-9),.35f,iron);
            Beam(t,new Vector3(0,5,0),new Vector3(0,14,-9),.12f,iron);
            Beam(t,new Vector3(0,14,-9),new Vector3(0,3,-9),.045f,iron);
            if(detail)
            {
                Beam(t,new Vector3(0,9,0),new Vector3(0,9,3),.6f,iron);
                Block(t,"Counterweight",new Vector3(0,9,3),new Vector3(1.6f,1.6f,1.8f),iron);
                Block(t,"HoistLoad",new Vector3(0,2.5f,-9),new Vector3(1.5f,1,1.4f),wood);
            }
        }

        static void Hull(Transform t, float length, float width, float height)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            var outline = new[]{new Vector2(-length*.5f,0),new Vector2(-length*.38f,-width*.5f),
                new Vector2(length*.36f,-width*.5f),new Vector2(length*.5f,0),
                new Vector2(length*.36f,width*.5f),new Vector2(-length*.38f,width*.5f)};
            for(int ring=0;ring<2;ring++) foreach(var p in outline)
                vertices.Add(new Vector3(p.x*(ring==0?.92f:1),ring==0?-.4f:height,p.y*(ring==0?.7f:1)));
            for(int i=0;i<6;i++)
            {
                int j=(i+1)%6;
                triangles.AddRange(new[]{i,j,i+6,j,j+6,i+6});
            }
            for(int i=1;i<5;i++) triangles.AddRange(new[]{6,6+i+1,6+i});
            Shape(t,"PointedHull",vertices.ToArray(),triangles.ToArray(),hull);
        }

        static void Ship(Transform t, float length, bool detail, int variant = 0)
        {
            Hull(t,length,8,2.8f);
            Block(t,"Deckhouse",new Vector3(-2,4.5f,0),new Vector3(length*.32f,3,6),wall);
            Block(t,"DeckhouseRoof",new Vector3(-2,6.1f,0),new Vector3(length*.35f,.25f,6.6f),wood);
            for (int i = 0; i <= variant; i++)
            {
                float x = variant == 0 ? 0f : -5f + i * 8f;
                BuilderUtils.Prim(PrimitiveType.Cylinder,"Funnel",t,new Vector3(x,8,0),new Vector3(1.7f,2.4f,1.7f),roof,false,false);
                BuilderUtils.Prim(PrimitiveType.Cylinder,"FunnelCap",t,new Vector3(x,10.2f,0),new Vector3(1.8f,.3f,1.8f),hull,false,false);
            }
            Block(t,"Forecastle",new Vector3(length*.34f,3.2f,0),new Vector3(length*.17f,.8f,6),wood);
            Block(t,"Quarterdeck",new Vector3(-length*.34f,3.3f,0),new Vector3(length*.16f,1f,6),wood);
            foreach(float x in new[]{-length*.3f,length*.3f})
            {
                Beam(t,new Vector3(x,2.8f,0),new Vector3(x,17,0),.16f,wood);
                Beam(t,new Vector3(x-4,13,0),new Vector3(x+4,13,0),.12f,wood);
                if(detail)
                {
                    Beam(t,new Vector3(x,16,0),new Vector3(x-7,3.1f,-3.5f),.035f,iron);
                    Beam(t,new Vector3(x,16,0),new Vector3(x+7,3.1f,3.5f),.035f,iron);
                }
            }
            if(detail) for(int x=-12;x<=8;x+=3)
                Block(t,"Porthole",new Vector3(x,1.9f,-4.02f),new Vector3(.45f,.45f,.04f),wall);
        }

        static void Sampan(Transform t, bool detail)
        {
            Hull(t,6.5f,1.8f,.55f);
            Block(t,"Bench",new Vector3(0,.62f,0),new Vector3(1,.15f,1.5f),wood);
            Block(t,"WovenCanopy",new Vector3(-.5f,1.4f,0),new Vector3(2.8f,.18f,1.8f),roof);
            if(detail)
            {
                foreach(float x in new[]{-1.8f,.8f}) foreach(float z in new[]{-.65f,.65f})
                    Beam(t,new Vector3(x,.6f,z),new Vector3(x,1.4f,z),.05f,wood);
                Beam(t,new Vector3(2.7f,.8f,0),new Vector3(4.4f,.15f,1.7f),.06f,wood);
            }
        }

        static void Palm(Transform t, bool detail)
        {
            Beam(t,Vector3.zero,new Vector3(.9f,15.5f,0),.28f,wood);
            var v=new List<Vector3>(); var tr=new List<int>();
            int fronds=detail?10:6, segments=detail?6:3;
            for(int f=0;f<fronds;f++)
            {
                float angle=f*Mathf.PI*2/fronds;
                var direction=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                var side=new Vector3(-direction.z,0,direction.x);
                int start=v.Count;
                for(int j=0;j<=segments;j++)
                {
                    float s=(float)j/segments;
                    var p=new Vector3(.9f,15.5f,0)+direction*s*4.5f+Vector3.up*(Mathf.Sin(s*Mathf.PI)*1.1f-s*s*2.4f);
                    float width=Mathf.Sin(s*Mathf.PI)*.7f+.025f;
                    v.Add(p-side*width);v.Add(p+side*width);
                    if(j<segments) {int a=start+j*2;tr.AddRange(new[]{a,a+2,a+1,a+1,a+2,a+3});}
                }
            }
            // Mặt sau có đỉnh riêng để tính pháp tuyến không triệt tiêu mặt trước.
            int back = v.Count;
            v.AddRange(v.ToArray());
            int front = tr.Count;
            for (int i = 0; i < front; i += 3)
                tr.AddRange(new[] { tr[i] + back, tr[i + 2] + back, tr[i + 1] + back });
            Shape(t,"PalmFronds",v.ToArray(),tr.ToArray(),green);
        }

        static void Ridge(Transform t, bool detail)
        {
            int nx=detail?60:20,nz=detail?18:6;
            var v=new List<Vector3>();var tr=new List<int>();
            for(int z=0;z<=nz;z++) for(int x=0;x<=nx;x++)
            {
                float xx=-180+360f*x/nx,zz=140f*z/nz;
                float ridge=(18+10*Mathf.Sin(xx*.029f)+5*Mathf.Cos(xx*.067f));
                float y=Mathf.Sin((float)z/nz*Mathf.PI)*ridge + Mathf.Sin(xx*.13f+zz*.07f)*1.1f;
                v.Add(new Vector3(xx,y,zz));
            }
            for(int z=0;z<nz;z++) for(int x=0;x<nx;x++)
            {int a=z*(nx+1)+x;tr.AddRange(new[]{a,a+nx+1,a+1,a+1,a+nx+1,a+nx+2});}
            Shape(t,"ForestedRidge",v.ToArray(),tr.ToArray(),green);
        }
    }
}
