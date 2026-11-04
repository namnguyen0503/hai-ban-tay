using System.Collections.Generic;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Narrative;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>
    /// Chỗ chiếu phim 1911 ở phố Sài Gòn: màn vải căng trên khung gỗ đứng trước tường, ba bóng người đội mũ trong vùng sáng bo góc,
    /// máy chiếu cổ trên chân ba với hai cuộn phim, chùm sáng từ ống kính tới màn và quầng hắt trên nền.
    /// Mọi hình đều là hình tĩnh hoặc quad cộng sáng (không thêm đèn realtime); ProjectionFlicker lo nhấp nháy và cử động.
    /// </summary>
    static class ProjectionSetPiece
    {
        public static readonly Vector3 ScreenCenter = new Vector3(-9f, 1.95f, -9.3f);
        const float ClothDepth = 0.04f;
        static readonly Vector3 ProjectorFoot = new Vector3(-12f, 0.12f, -8.2f);
        const float HeadHeight = 1.42f;
        const float HeadScale = 1.25f;

        public static ProjectionFlicker Build(Transform g)
        {
            var wood = AssetFactory.Lit("PropWood", new Color(.4f, .27f, .16f), .15f);
            var bronze = AssetFactory.Lit("ProjectorBronze", new Color(0.46f, 0.3f, 0.15f), 0.45f, 0.8f);
            var body = AssetFactory.Lit("ProjectorBody", new Color(0.2f, 0.14f, 0.09f), 0.4f, 0.6f);
            var flicker = BuildScreen(g, wood);
            BuildFigures(g, flicker);
            BuildProjector(g, body, bronze, flicker);
            BuildSpill(g);
            return flicker;
        }

        static ProjectionFlicker BuildScreen(Transform g, Material wood)
        {
            float w = AssetFactory.ScreenWidth, h = AssetFactory.ScreenHeight;
            var cloth = BuilderUtils.Block("CinemaScreen1911", g, ScreenCenter, new Vector3(w, h, ClothDepth), AssetFactory.ProjectionScreen(), true, false);
            var flicker = cloth.AddComponent<ProjectionFlicker>();
            flicker.screen = cloth.GetComponent<Renderer>();
            float back = ScreenCenter.z - 0.045f, ground = 0.12f;
            float top = ScreenCenter.y + h * 0.5f, bottom = ScreenCenter.y - h * 0.5f, half = w * 0.5f + 0.06f;
            var frame = BuilderUtils.Group("ScreenFrame1911", g).transform;
            foreach (float side in new[] { -1f, 1f })
            {
                float x = ScreenCenter.x + side * half;
                float postTop = top + 0.12f;
                BuilderUtils.Block("Post", frame, new Vector3(x, (ground + postTop) * 0.5f, back), new Vector3(0.09f, postTop - ground, 0.07f), wood, true, false);
                // Chân chữ T chìa ra phía người xem để khung đứng vững.
                BuilderUtils.Block("Foot", frame, new Vector3(x, ground + 0.035f, back + 0.3f), new Vector3(0.11f, 0.07f, 0.7f), wood, true, false);
                BuilderUtils.Block("Brace", frame, new Vector3(x, ground + 0.45f, back + 0.2f), new Vector3(0.05f, 0.05f, 0.75f), wood, true, false, new Vector3(-35f, 0f, 0f));
            }
            BuilderUtils.Block("TopRail", frame, new Vector3(ScreenCenter.x, top + 0.06f, back), new Vector3(w + 0.2f, 0.09f, 0.07f), wood, true, false);
            BuilderUtils.Block("BottomRail", frame, new Vector3(ScreenCenter.x, bottom - 0.06f, back), new Vector3(w + 0.2f, 0.09f, 0.07f), wood, true, false);
            BuilderUtils.Block("LowerRail", frame, new Vector3(ScreenCenter.x, ground + 0.5f, back), new Vector3(w + 0.2f, 0.06f, 0.06f), wood, true, false);
            return flicker;
        }

        /// <summary>Ba bóng người đội mũ: dáng bo mềm (Chaikin), ba cỡ và ba mức chân để thấy chiều sâu như phim.</summary>
        static void BuildFigures(Transform g, ProjectionFlicker flicker)
        {
            var shadow = AssetFactory.Lit("StoryFilmShadow", new Color(0.025f, 0.022f, 0.018f), 0f);
            GameObject Cutout(string name, Transform parent, Vector2[] outline)
            {
                var go = BuilderUtils.Group(name, parent);
                string path = SurfaceMesh.Folder + "/Gen_Film2_" + name + ".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null)
                {
                    var smooth = Smooth(Smooth(outline));
                    var vertices = new Vector3[smooth.Length + 1];
                    for (int k = 0; k < smooth.Length; k++)
                    { vertices[k + 1] = new Vector3(smooth[k].x, smooth[k].y, 0f); vertices[0] += vertices[k + 1] / smooth.Length; }
                    var triangles = new int[smooth.Length * 6];
                    for (int k = 0; k < smooth.Length; k++)
                    {
                        int n = (k + 1) % smooth.Length + 1, at = k * 6;
                        triangles[at] = 0; triangles[at + 1] = k + 1; triangles[at + 2] = n;
                        triangles[at + 3] = 0; triangles[at + 4] = n; triangles[at + 5] = k + 1;
                    }
                    mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
                    var normals = new Vector3[vertices.Length];
                    for (int k = 0; k < normals.Length; k++) normals[k] = Vector3.forward;
                    mesh.normals = normals; mesh.RecalculateBounds();
                    AssetFactory.EnsureFolder(SurfaceMesh.Folder); AssetDatabase.CreateAsset(mesh, path);
                }
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = shadow;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return go;
            }
            flicker.limbs = new Transform[12];
            // x, độ cao chân, cỡ: người đầu to nhất đứng gần ống kính nhất, người cuối nhỏ và cao hơn như đứng xa.
            var stand = new[] { new Vector3(-8.3f, 1.4f, 1.05f), new Vector3(-9.05f, 1.43f, 1f), new Vector3(-9.85f, 1.52f, 0.84f) };
            for (int i = 0; i < 3; i++)
            {
                var figure = BuilderUtils.Group("FilmSilhouette", g).transform;
                figure.localPosition = new Vector3(stand[i].x, stand[i].y + 0.455f * stand[i].z, ScreenCenter.z + ClothDepth * 0.5f + 0.012f);
                figure.localScale = Vector3.one * stand[i].z;
                Cutout("Head1911", figure, new[] { new Vector2(-.065f,.26f), new Vector2(-.09f,.32f), new Vector2(-.08f,.40f),
                    new Vector2(-.045f,.44f), new Vector2(.045f,.44f), new Vector2(.085f,.40f), new Vector2(.095f,.35f),
                    new Vector2(.12f,.33f), new Vector2(.085f,.31f), new Vector2(.06f,.26f) });
                Cutout("HatBrim1911", figure, new[] { new Vector2(-.16f,.405f), new Vector2(-.14f,.44f),
                    new Vector2(.14f,.44f), new Vector2(.16f,.405f), new Vector2(.0f,.395f) });
                Cutout("HatCrown1911", figure, new[] { new Vector2(-.095f,.43f), new Vector2(-.075f,.50f),
                    new Vector2(-.045f,.515f), new Vector2(.06f,.515f), new Vector2(.085f,.49f), new Vector2(.095f,.43f) });
                Cutout("LongCoat1911", figure, new[] { new Vector2(-.05f,.29f), new Vector2(-.12f,.26f), new Vector2(-.145f,.20f),
                    new Vector2(-.12f,.05f), new Vector2(-.11f,-.05f), new Vector2(-.15f,-.22f), new Vector2(-.14f,-.245f),
                    new Vector2(.14f,-.245f), new Vector2(.15f,-.22f), new Vector2(.11f,-.05f), new Vector2(.12f,.05f),
                    new Vector2(.145f,.20f), new Vector2(.12f,.26f), new Vector2(.05f,.29f) });
                for (int j = 0; j < 4; j++)
                {
                    var limb = BuilderUtils.Group(j < 2 ? "Arm" : "Leg", figure).transform;
                    limb.localPosition = new Vector3((j % 2 == 0 ? -1f : 1f) * (j < 2 ? .125f : .06f), j < 2 ? .22f : -.16f, 0f);
                    if (j < 2)
                        Cutout("CoatSleeve1911", limb, new[] { new Vector2(-.04f,0), new Vector2(.04f,0), new Vector2(.03f,-.13f),
                            new Vector2(.025f,-.22f), new Vector2(.035f,-.265f), new Vector2(0,-.285f), new Vector2(-.025f,-.255f),
                            new Vector2(-.025f,-.22f), new Vector2(-.035f,-.12f) });
                    else
                        Cutout("TrouserShoe1911", limb, new[] { new Vector2(-.035f,0), new Vector2(.035f,0), new Vector2(.025f,-.24f),
                            new Vector2(.075f,-.27f), new Vector2(.075f,-.295f), new Vector2(-.03f,-.295f), new Vector2(-.025f,-.24f) });
                    flicker.limbs[i * 4 + j] = limb;
                }
            }
        }

        /// <summary>Hai vòng Chaikin: cắt mỗi góc ở 1/4 và 3/4 cạnh, đường viền thành đường cong kín.</summary>
        static Vector2[] Smooth(Vector2[] p)
        {
            var q = new Vector2[p.Length * 2];
            for (int i = 0; i < p.Length; i++)
            {
                var a = p[i]; var b = p[(i + 1) % p.Length];
                q[i * 2] = Vector2.Lerp(a, b, 0.25f);
                q[i * 2 + 1] = Vector2.Lerp(a, b, 0.75f);
            }
            return q;
        }

        static void BuildProjector(Transform g, Material iron, Material bronze, ProjectionFlicker flicker)
        {
            var wood = AssetFactory.Lit("PropWood", new Color(.4f, .27f, .16f), .15f);
            var lens = AssetFactory.ProjectorLens();
            var root = BuilderUtils.Group("Projector1911", g).transform;
            root.localPosition = ProjectorFoot;
            // Chân ba: một chân hướng ra phía màn, hai chân sau; mỗi chân là cột gỗ xiên từ bệ đầu xuống đất.
            var topPoint = new Vector3(0f, HeadHeight - 0.03f, 0f);
            for (int i = 0; i < 3; i++)
            {
                float angle = i * 120f * Mathf.Deg2Rad;
                var foot = new Vector3(Mathf.Sin(angle) * 0.58f, 0.02f, Mathf.Cos(angle) * 0.58f);
                var dir = foot - topPoint;
                BuilderUtils.Prim(PrimitiveType.Cylinder, "TripodLeg", root, (topPoint + foot) * 0.5f,
                    new Vector3(0.045f, dir.magnitude * 0.5f, 0.045f), wood, true, false,
                    Quaternion.FromToRotation(Vector3.up, dir.normalized).eulerAngles);
                BuilderUtils.Prim(PrimitiveType.Cylinder, "LegShoe", root, foot + Vector3.up * 0.04f, new Vector3(0.06f, 0.05f, 0.06f), bronze, true, false);
            }
            BuilderUtils.Prim(PrimitiveType.Cylinder, "TripodHead", root, new Vector3(0f, HeadHeight - 0.045f, 0f), new Vector3(0.2f, 0.035f, 0.2f), bronze, true, false);

            // Thân máy quay về phía màn: tính hướng từ tâm ống kính tới tâm vùng sáng.
            var target = new Vector3(ScreenCenter.x, ScreenCenter.y, ScreenCenter.z + ClothDepth * 0.5f);
            var headWorld = ProjectorFoot + Vector3.up * HeadHeight;
            var rotation = Quaternion.LookRotation((target - (headWorld + Vector3.up * 0.17f * HeadScale)).normalized);
            var head = BuilderUtils.Group("ProjectorHead", root).transform;
            head.localPosition = Vector3.up * HeadHeight;
            head.localRotation = rotation;
            head.localScale = Vector3.one * HeadScale;
            BuilderUtils.Block("BasePlate", head, new Vector3(0f, 0.015f, 0f), new Vector3(0.26f, 0.03f, 0.44f), bronze, true, false);
            BuilderUtils.Block("Body", head, new Vector3(0f, 0.165f, 0f), new Vector3(0.3f, 0.27f, 0.5f), iron, true, false);
            BuilderUtils.Block("TopCap", head, new Vector3(0f, 0.31f, 0f), new Vector3(0.26f, 0.035f, 0.46f), bronze, true, false);
            BuilderUtils.Block("Gate", head, new Vector3(0f, 0.17f, 0.285f), new Vector3(0.2f, 0.2f, 0.07f), bronze, true, false);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "LensBarrel", head, new Vector3(0f, 0.17f, 0.4f), new Vector3(0.11f, 0.1f, 0.11f), bronze, true, false, new Vector3(90f, 0f, 0f));
            BuilderUtils.Prim(PrimitiveType.Cylinder, "LensHood", head, new Vector3(0f, 0.17f, 0.53f), new Vector3(0.15f, 0.03f, 0.15f), bronze, true, false, new Vector3(90f, 0f, 0f));
            BuilderUtils.Prim(PrimitiveType.Cylinder, "LensGlass", head, new Vector3(0f, 0.17f, 0.559f), new Vector3(0.1f, 0.004f, 0.1f), lens, false, false, new Vector3(90f, 0f, 0f));
            // Khe thông gió bên phải thân và tay quay phía sau.
            for (int i = 0; i < 4; i++)
                BuilderUtils.Block("Vent", head, new Vector3(0.152f, 0.1f + i * 0.04f, -0.05f), new Vector3(0.006f, 0.014f, 0.22f), bronze, true, false);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "CrankRod", head, new Vector3(0.2f, 0.19f, -0.2f), new Vector3(0.02f, 0.05f, 0.02f), bronze, true, false, new Vector3(0f, 0f, 90f));
            BuilderUtils.Block("CrankArm", head, new Vector3(0.255f, 0.14f, -0.2f), new Vector3(0.02f, 0.11f, 0.02f), bronze, true, false);
            BuilderUtils.Prim(PrimitiveType.Sphere, "CrankKnob", head, new Vector3(0.255f, 0.085f, -0.2f), new Vector3(0.045f, 0.045f, 0.045f), wood, true, false);

            // Hai cuộn phim gắn bên trái thân (phía người xem đứng sau-trái máy), cuộn nạp trên cao, cuộn thu phía sau thấp hơn.
            var reelMesh = AssetFactory.FilmReelMesh();
            BuilderUtils.Block("ReelStrut", head, new Vector3(-0.17f, 0.4f, 0.1f), new Vector3(0.03f, 0.2f, 0.03f), bronze, true, false);
            var reels = new List<Transform>();
            var spots = new[] { new Vector3(-0.285f, 0.52f, 0.1f), new Vector3(-0.35f, 0.31f, -0.2f) };
            for (int i = 0; i < spots.Length; i++)
            {
                // Trục từ mặt thân (x = -0,15) tới hết ống giữa của cuộn.
                float axleEnd = spots[i].x - 0.02f;
                BuilderUtils.Prim(PrimitiveType.Cylinder, "ReelAxle", head, new Vector3((-0.15f + axleEnd) * 0.5f, spots[i].y, spots[i].z),
                    new Vector3(0.03f, (-0.15f - axleEnd) * 0.5f, 0.03f), bronze, true, false, new Vector3(0f, 0f, 90f));
                var pivot = BuilderUtils.Group("ReelPivot", head).transform;
                pivot.localPosition = spots[i];
                pivot.localRotation = Quaternion.Euler(0f, 0f, 90f);
                var reel = new GameObject("FilmReel");
                reel.transform.SetParent(pivot, false);
                reel.transform.localScale = Vector3.one * 0.17f;
                reel.AddComponent<MeshFilter>().sharedMesh = reelMesh;
                reel.AddComponent<MeshRenderer>().sharedMaterial = bronze;
                BuilderUtils.Prim(PrimitiveType.Cylinder, "FilmRoll", reel.transform, new Vector3(0f, 0.02f, 0f), new Vector3(1.38f, 0.045f, 1.38f), iron, false, false);
                reels.Add(reel.transform);
            }
            flicker.reels = reels.ToArray();

            // Dây điện nằm trên nền, ngoằn ngoèo ra sau máy.
            var cable = new[] { new Vector3(0f, 0.04f, -0.15f), new Vector3(-0.35f, 0.03f, -0.55f), new Vector3(-0.9f, 0.03f, -0.7f),
                new Vector3(-1.5f, 0.03f, -0.45f), new Vector3(-2.1f, 0.03f, -0.8f), new Vector3(-2.8f, 0.03f, -0.7f) };
            for (int i = 0; i + 1 < cable.Length; i++)
            {
                var mid = (cable[i] + cable[i + 1]) * 0.5f;
                var d = cable[i + 1] - cable[i];
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Cable", root, mid, new Vector3(0.022f, d.magnitude * 0.5f, 0.022f), iron, true, false,
                    Quaternion.FromToRotation(Vector3.up, d.normalized).eulerAngles);
            }

            BuildBeam(g, head, rotation, headWorld);
            // Điểm sáng ở ống kính: hai quad cộng sáng chứa trục chùm, chéo nhau nên nhìn từ bên nào cũng thấy.
            var glow = AssetFactory.ProjectorSpill();
            for (int i = 0; i < 2; i++)
            {
                var q = BuilderUtils.Prim(PrimitiveType.Quad, "LensGlow", head, new Vector3(0f, 0.17f, 0.6f), new Vector3(0.5f, 0.5f, 1f), glow, false, false, new Vector3(0f, 90f, i * 90f));
                var r = q.GetComponent<MeshRenderer>(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            }
        }

        /// <summary>Chùm sáng hình chóp cụt từ ống kính tới viền vùng sáng trên vải; cộng sáng, hai mặt.</summary>
        static void BuildBeam(Transform g, Transform head, Quaternion rotation, Vector3 headWorld)
        {
            var lens = headWorld + rotation * (new Vector3(0f, 0.17f, 0.562f) * HeadScale);
            var right = rotation * Vector3.right * 0.07f;
            var up = rotation * Vector3.up * 0.055f;
            var start = new[] { lens - right - up, lens + right - up, lens + right + up, lens - right + up };
            float z = ScreenCenter.z + ClothDepth * 0.5f + 0.004f;
            float hw = AssetFactory.LitHalfWidth - 0.04f, hh = AssetFactory.LitHalfHeight - 0.04f;
            var end = new[] { new Vector3(ScreenCenter.x - hw, ScreenCenter.y - hh, z), new Vector3(ScreenCenter.x + hw, ScreenCenter.y - hh, z),
                new Vector3(ScreenCenter.x + hw, ScreenCenter.y + hh, z), new Vector3(ScreenCenter.x - hw, ScreenCenter.y + hh, z) };
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                int n = (i + 1) % 4, at = vertices.Count;
                vertices.AddRange(new[] { start[i], start[n], end[n], end[i] });
                uv.AddRange(new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
                triangles.AddRange(new[] { at, at + 1, at + 2, at, at + 2, at + 3 });
            }
            var mesh = new Mesh { name = "ProjectorBeam" };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            var go = BuilderUtils.Group("ProjectorBeam", g);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = AssetFactory.ProjectorBeam();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>Quầng sáng ấm trên nền phố trước màn và quanh chân máy chiếu; quad cộng sáng, không đèn.</summary>
        static void BuildSpill(Transform g)
        {
            var mat = AssetFactory.ProjectorSpill();
            void Pool(string name, Vector3 center, Vector2 size, float yaw)
            {
                var q = BuilderUtils.Prim(PrimitiveType.Quad, name, g, center, new Vector3(size.x, size.y, 1f), mat, false, false, new Vector3(90f, yaw, 0f));
                var r = q.GetComponent<MeshRenderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            }
            Pool("ScreenSpill", new Vector3(ScreenCenter.x, 0.128f, ScreenCenter.z + 1.5f), new Vector2(6.4f, 3.4f), 0f);
            Pool("ProjectorSpill", ProjectorFoot + new Vector3(0.6f, 0.008f, -0.2f), new Vector2(1.6f, 1.4f), 0f);
        }
    }
}
