using System.Collections.Generic;
using HaiBanTay.Editor.Setup;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>Xe du lịch mui mở đầu thế kỷ XX: két đồng, nắp máy thuôn, khoang ghế, bánh nan và đèn dầu.</summary>
    public static class MotorCarBuilder
    {
        public static void Build(Transform car, Material iron, Material brass)
        {
            var rubber = AssetFactory.Lit("StoryCarRubber", new Color(.035f,.035f,.032f), .12f);
            var leather = AssetFactory.Lit("StoryCarLeather", new Color(.17f,.10f,.065f), .24f);
            var wood = AssetFactory.Lit("StoryCarSpokes", new Color(.45f,.31f,.14f), .22f);
            var lens = AssetFactory.Lit("StoryCarLens", new Color(.78f,.82f,.72f), .8f);
            var parts = BuilderUtils.Group("CarBody", car).transform;
            Block(parts,"Chassis",new Vector3(0,.53f,0),new Vector3(1.28f,.16f,3.1f),iron);
            Block(parts,"PassengerFloor",new Vector3(0,.70f,-.62f),new Vector3(1.20f,.12f,1.52f),iron);
            foreach (float x in new[] { -.62f, .62f })
            {
                Block(parts,"PassengerSide",new Vector3(x,.91f,-.66f),new Vector3(.10f,.42f,1.43f),iron);
                Block(parts,"RunningBoard",new Vector3(x*1.28f,.50f,-.1f),new Vector3(.34f,.055f,1.25f),wood);
                Beam(parts,new Vector3(x,.93f,.27f),new Vector3(x,1.63f,.21f),.035f,brass);
            }
            Block(parts,"SeatCushion",new Vector3(0,.89f,-.77f),new Vector3(1.1f,.17f,.60f),leather);
            Block(parts,"SeatBack",new Vector3(0,1.12f,-1.14f),new Vector3(1.13f,.51f,.16f),leather,new Vector3(-10,0,0));
            Block(parts,"RearTub",new Vector3(0,.90f,-1.38f),new Vector3(1.3f,.48f,.13f),iron);
            // Nắp máy sáu mặt có vai vát, hẹp hơn ở đầu két; không dùng hộp nguyên khối.
            var v = new List<Vector3>(); var tr = new List<int>();
            for (int ring=0; ring<2; ring++)
            {
                float w = ring==0 ? .55f : .45f, z = ring==0 ? .27f : 1.42f;
                foreach (var p in new[] { new Vector2(-w,.73f),new Vector2(w,.73f),new Vector2(w,1.12f),
                    new Vector2(w*.76f,1.27f),new Vector2(-w*.76f,1.27f),new Vector2(-w,1.12f) })
                    v.Add(new Vector3(p.x,p.y,z));
            }
            for (int i=0;i<6;i++)
            {
                int j=(i+1)%6;
                tr.AddRange(new[] { i,j,i+6,j,j+6,i+6 });
            }
            for (int i=1;i<5;i++) tr.AddRange(new[] { 0,i+1,i,6,6+i,6+i+1 });
            Mesh(parts,"TaperedBonnet",v,tr,iron);
            Block(parts,"BrassRadiator",new Vector3(0,1.02f,1.46f),new Vector3(.98f,.84f,.12f),brass);
            Block(parts,"RadiatorCore",new Vector3(0,1.02f,1.529f),new Vector3(.82f,.67f,.015f),rubber);
            for(int i=0;i<15;i++)
                Block(parts,"RadiatorSlat",new Vector3(-.39f+i*.056f,1.02f,1.544f),new Vector3(.012f,.67f,.014f),brass);
            Block(parts,"RadiatorCap",new Vector3(0,1.47f,1.46f),new Vector3(.09f,.08f,.09f),brass);
            for (int side=-1;side<=1;side+=2)
            {
                for(int i=0;i<7;i++)
                    Block(parts,"BonnetVent",new Vector3(side*(.55f-.10f*(.4f+i*.12f-.27f)/1.15f),1.01f,.4f+i*.12f),
                        new Vector3(.012f,.18f,.025f),rubber,new Vector3(-12,0,0));
                foreach (float z in new[] { -1.05f,1.05f })
                {
                    var wheel = BuilderUtils.Group("SpokedWheel",parts).transform;
                    wheel.localPosition = new Vector3(side*.80f,.46f,z);
                    Ring(wheel,"Tyre",.40f,.065f,rubber);
                    Ring(wheel,"WheelRim",.335f,.025f,wood);
                    BuilderUtils.Prim(PrimitiveType.Cylinder,"Hub",wheel,Vector3.zero,new Vector3(.14f,.10f,.14f),brass,false,false,new Vector3(0,0,90));
                    for(int spoke=0;spoke<12;spoke++)
                    {
                        float a=spoke*Mathf.PI*2/12;
                        Beam(wheel,Vector3.zero,new Vector3(0,Mathf.Cos(a)*.33f,Mathf.Sin(a)*.33f),.024f,wood);
                    }
                    Fender(parts,side*.80f,z,iron);
                }
                BuilderUtils.Prim(PrimitiveType.Cylinder,"OilHeadlamp",parts,new Vector3(side*.58f,1.10f,1.41f),new Vector3(.25f,.13f,.25f),brass,false,false,new Vector3(90,0,0));
                BuilderUtils.Prim(PrimitiveType.Cylinder,"LampLens",parts,new Vector3(side*.58f,1.10f,1.545f),new Vector3(.20f,.008f,.20f),lens,false,false,new Vector3(90,0,0));
                Block(parts,"LampChimney",new Vector3(side*.58f,1.29f,1.41f),new Vector3(.075f,.13f,.075f),brass);
                Beam(parts,new Vector3(side*.58f,.72f,1.40f),new Vector3(side*.58f,1.04f,1.40f),.035f,iron);
            }
            Beam(parts,new Vector3(-.62f,1.63f,.21f),new Vector3(.62f,1.63f,.21f),.035f,brass);
            Beam(parts,new Vector3(-.62f,1.17f,.25f),new Vector3(.62f,1.17f,.25f),.035f,brass);
            var steering = BuilderUtils.Group("SteeringWheel",parts).transform;
            steering.localPosition = new Vector3(.30f,1.26f,.03f);
            steering.localRotation = Quaternion.FromToRotation(Vector3.right,new Vector3(0,.7f,.7f));
            Ring(steering,"SteeringRim",.18f,.017f,wood);
            Beam(parts,new Vector3(.30f,.75f,.28f),steering.localPosition,.028f,iron);
            for(int i=0;i<3;i++)
            {
                float a=i*Mathf.PI*2/3;
                Beam(steering,Vector3.zero,new Vector3(0,Mathf.Cos(a)*.18f,Mathf.Sin(a)*.18f),.012f,brass);
            }
            BuilderUtils.Prim(PrimitiveType.Capsule,"FoldedHood",parts,new Vector3(0,1.24f,-1.37f),new Vector3(.20f,.62f,.20f),leather,false,false,new Vector3(0,0,90));
            // Nan/đèn/chi tiết gộp theo sáu vật liệu: không tạo một draw cho mỗi chi tiết.
            KeppelPortBuilder.Combine(parts,"Env13_MotorCar1911");
            var col=car.gameObject.AddComponent<BoxCollider>();
            col.center=new Vector3(0,.78f,0); col.size=new Vector3(1.65f,1.55f,3.18f);
        }

        static void Block(Transform p,string name,Vector3 at,Vector3 size,Material mat,Vector3? rotation=null) =>
            BuilderUtils.Block(name,p,at,size,mat,false,false,rotation);

        static void Beam(Transform p,Vector3 a,Vector3 b,float width,Material mat)
        {
            var go=BuilderUtils.Block("Beam",p,(a+b)*.5f,new Vector3(width,(b-a).magnitude,width),mat,false,false);
            go.transform.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);
        }

        static GameObject Mesh(Transform p,string name,List<Vector3> v,List<int> tr,Material mat)
        {
            var mesh=new Mesh { vertices=v.ToArray(), triangles=tr.ToArray() };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go=BuilderUtils.Group(name,p);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial=mat;
            return go;
        }

        static void Ring(Transform p,string name,float radius,float tube,Material mat)
        {
            const int sides=24, section=6;
            var v=new List<Vector3>(); var tr=new List<int>();
            for(int i=0;i<sides;i++) for(int j=0;j<section;j++)
            {
                float a=i*Mathf.PI*2/sides,b=j*Mathf.PI*2/section,r=radius+tube*Mathf.Cos(b);
                v.Add(new Vector3(tube*Mathf.Sin(b),r*Mathf.Cos(a),r*Mathf.Sin(a)));
                int next=(i+1)%sides*section,cur=i*section,k=(j+1)%section;
                tr.AddRange(new[] { cur+j,next+j,cur+k,cur+k,next+j,next+k });
            }
            Mesh(p,name,v,tr,mat);
        }

        static void Fender(Transform p,float x,float z,Material mat)
        {
            var v=new List<Vector3>(); var tr=new List<int>();
            for(int i=0;i<=12;i++)
            {
                float a=Mathf.Lerp(-.25f,Mathf.PI+.25f,i/12f);
                foreach(float side in new[] { -.15f,.15f })
                    v.Add(new Vector3(x+side,.46f+Mathf.Sin(a)*.56f,z+Mathf.Cos(a)*.56f));
            }
            // Hai mặt ngược chiều dùng chung đỉnh thì RecalculateNormals cộng hai pháp tuyến đối nhau thành 0 (pháp tuyến 0 → NaN
            // trong shader → bloom bùng thành vầng sáng trắng quanh xe, lúc nào nhìn xe cũng có). Mặt sau dùng bản sao đỉnh riêng.
            int front=v.Count;
            for(int i=0;i<front;i++) v.Add(v[i]);
            // Tấm bùn hai phía đọc rõ cả góc thấp và góc cao.
            for(int i=0;i<12;i++)
            {
                int k=i*2;
                tr.AddRange(new[] { k,k+2,k+1,k+1,k+2,k+3 });
                tr.AddRange(new[] { front+k+1,front+k+2,front+k,front+k+3,front+k+2,front+k+1 });
            }
            Mesh(p,"CurvedMudguard",v,tr,mat);
        }
    }
}
