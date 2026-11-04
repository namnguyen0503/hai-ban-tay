using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HaiBanTay.Editor.Import;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Player;
using HaiBanTay.Ship;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>Trang phục anh Ba từ mẫu Ba riêng (Rocketbox MIT); giữ thân/khung xương feet12.</summary>
    public static class PlayerBodyBuilder
    {
        public const string Folder = "Assets/_Project/Art/PlayerBody";
        const string MeshPath = Folder + "/BaBody.asset";
        const string ControllerPath = Folder + "/BaController.controller";

        public static PlayerBody Build(PlayerRig rig)
        {
            AssetFactory.EnsureFolder(Folder);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterImporter.CharRoot + "Ba/Ba.fbx");
            if (source == null) throw new InvalidOperationException("Thiếu mẫu Ba faces13; chạy CharacterImporter.RunFaces13");
            var model = Object.Instantiate(source, rig.transform);
            model.name = "BaBody";
            model.transform.SetLocalPositionAndRotation(new Vector3(0f, 0f, -0.09f), Quaternion.identity);
            var animator = model.GetComponent<Animator>();
            if (animator == null || !animator.isHuman) throw new InvalidOperationException("Ba cần Avatar Humanoid hợp lệ");
            var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skins.Length != 1) throw new InvalidOperationException("Ba phải có đúng một renderer thân");
            var skin = skins[0];
            var ordinary = Dress(skin, animator, model.transform, true);
            string ordinaryPath = Folder + "/BaThuong.asset";
            var ordinarySaved = AssetDatabase.LoadAssetAtPath<Mesh>(ordinaryPath);
            if (ordinarySaved == null) { ordinarySaved = ordinary; AssetDatabase.CreateAsset(ordinarySaved, ordinaryPath); }
            else { EditorUtility.CopySerialized(ordinary, ordinarySaved); Object.DestroyImmediate(ordinary); }
            var mesh = Dress(skin, animator, model.transform);
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (saved == null) { saved = mesh; AssetDatabase.CreateAsset(saved, MeshPath); }
            else { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); }
            skin.sharedMesh = saved;
            skin.sharedMaterials = Materials();
            skin.shadowCastingMode = ShadowCastingMode.On;
            skin.receiveShadows = true;
            skin.renderingLayerMask = WorldRenderingLayers.Exterior;
            skin.lightProbeUsage = LightProbeUsage.Off;
            skin.updateWhenOffscreen = true;
            skin.quality = SkinQuality.Bone4;
            skin.localBounds = new Bounds(new Vector3(0f, 0.9f, 0f), new Vector3(2.6f, 2.4f, 2.6f));
            animator.runtimeAnimatorController = Controller();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            model.transform.localScale = Vector3.one * 0.95f; // Mắt khoảng 1,62 m, hợp capsule 1,75 m.
            var body = model.AddComponent<PlayerBody>();
            body.rig = rig;
            body.animator = animator;
            body.skin = skin;
            body.headSubmesh = 3;
            body.phuBepMesh = saved;
            body.thuongMesh = ordinarySaved;
            body.phuBepMaterials = skin.sharedMaterials;
            body.thuongMaterials = Materials();
            body.thuongMaterials[0] = OrdinaryShirtMaterial();
            body.thuongMaterials[1] = ClothMaterial("BaThuongTrousers", new Color(0.24f, 0.20f, 0.16f));
            BuildApron(body);
            FitHandSleeves(rig.hands);
            foreach (var collider in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            foreach (var lod in model.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
            EditorUtility.SetDirty(saved);
            return body;
        }

        // Slot cố định: áo, quần, da, đầu, dây eo, giày, mũ, nút, mũi giày, dây giày.
        static Material[] Materials()
        {
            var shirt = ShirtMaterial();
            var trousers = ClothMaterial("BaTrousers", new Color(0.31f, 0.32f, 0.32f));
            var skin = AssetFactory.Lit("BaForearm", new Color(0.60f, 0.43f, 0.31f), 0.28f);
            Material Shadow(string name, Texture texture, Color color)
            {
                string path = Folder + "/" + name + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    var shader = Shader.Find("HaiBanTay/PlayerHeadShadow");
                    if (shader == null) throw new InvalidOperationException("Thiếu shader bóng đầu người chơi");
                    m = new Material(shader);
                    AssetDatabase.CreateAsset(m, path);
                }
                m.SetTexture("_BaseMap", texture);
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Smoothness", 0.08f);
                EditorUtility.SetDirty(m);
                return m;
            }
            var head = Shadow("HeadShadow", AssetDatabase.LoadAssetAtPath<Texture2D>(CharacterImporter.CharRoot + "Ba/Ba_Head_BaseColor.png"), Color.white);
            var hat = Shadow("HatShadow", shirt.GetTexture("_BaseMap"), shirt.GetColor("_BaseColor"));
            var apron = ClothMaterial("BaApron", new Color(0.70f, 0.68f, 0.63f));
            var shoe = AssetFactory.Lit("BaShoes", new Color(0.075f, 0.063f, 0.052f), 0.24f);
            var toe = AssetFactory.Lit("BaShoeToes", new Color(0.18f, 0.16f, 0.135f), 0.32f);
            var button = AssetFactory.Lit("BaButtons", new Color(0.46f, 0.44f, 0.39f), 0.18f);
            var lace = AssetFactory.Lit("BaLaces", new Color(0.20f, 0.185f, 0.16f), 0.06f);
            return new[] { shirt, trousers, skin, head, apron, shoe, hat, button, toe, lace };
        }

        public static Material OrdinaryShirtMaterial() => ClothMaterial("BaThuongShirt", new Color(0.19f, 0.24f, 0.29f));

        public static Material ShirtMaterial() => ClothMaterial("BaShirt", new Color(0.70f, 0.68f, 0.63f));

        static Material ClothMaterial(string name, Color color)
        {
            AssetFactory.EnsureFolder(Folder);
            string path = Folder + "/BaCloth.png";
            if (!File.Exists(path))
            {
                const int size = 256;
                var t = new Texture2D(size, size, TextureFormat.RGB24, false);
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    // Vải dệt nhạt, nếp mềm; albedo tối đa vẫn dưới 0,70.
                    float n = 0.94f + 0.025f * Mathf.Sin(x * Mathf.PI * 2f / 32f + 0.8f * Mathf.Sin(y * Mathf.PI * 2f / 64f))
                        + 0.012f * ((x + y) % 2 == 0 ? 1f : -1f);
                    t.SetPixel(x, y, new Color(n, n, n));
                }
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.maxTextureSize = size;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.SaveAndReimport();
            }
            var m = AssetFactory.Lit(name, color, 0.08f);
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetTexture("_BumpMap", null);
            m.DisableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(m);
            return m;
        }

        static void BuildApron(PlayerBody body)
        {
            var go = new GameObject("BaLongApron", typeof(MeshFilter), typeof(MeshRenderer), typeof(PlayerApron));
            go.transform.SetParent(body.transform, false);
            var apron = go.GetComponent<PlayerApron>();
            apron.body = body;
            // Mép vải chồng đúng đai eo trong bind pose, giữ cùng offset hông khi animation chạy.
            var hip = body.animator.GetBoneTransform(HumanBodyBones.Hips);
            var hipBind = body.skin.sharedMesh.bindposes[Array.IndexOf(body.skin.bones, hip)].inverse.GetColumn(3);
            apron.waistOffset = new Vector3(0f, 1.055f, 0.17f)
                - body.transform.InverseTransformPoint(body.skin.transform.TransformPoint(hipBind));
            var mesh = PlayerApron.CreateMesh();
            string path = Folder + "/BaLongApron.asset";
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved == null) { saved = mesh; AssetDatabase.CreateAsset(saved, path); }
            else { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); }
            go.GetComponent<MeshFilter>().sharedMesh = saved;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = ClothMaterial("BaApron", new Color(0.70f, 0.68f, 0.63f));
            renderer.renderingLayerMask = body.skin.renderingLayerMask;
            body.apron = apron;
        }

        static RuntimeAnimatorController Controller()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (string name in new[] { "Idle", "Walk", "Run", "Strafe", "Backward", "Stairs", "Squat" })
            {
                var clip = name == "Run" ? AssetDatabase.LoadAllAssetsAtPath(Folder + "/Run.fbx").OfType<AnimationClip>()
                    .FirstOrDefault(c => !c.name.StartsWith("__preview__"))
                    : CharacterImporter.LoadClip(name == "Strafe" || name == "Backward" || name == "Stairs" ? "Walk" : name);
                if (clip == null) throw new InvalidOperationException("Thiếu clip thân: " + name);
                if (!clip.humanMotion) throw new InvalidOperationException("Clip cần Humanoid: " + name);
                var state = machine.states.Select(s => s.state).SingleOrDefault(s => s.name == name) ?? machine.AddState(name);
                state.motion = clip;
                state.speed = name == "Backward" ? -1f : 1f;
                if (name == "Idle") machine.defaultState = state;
                EditorUtility.SetDirty(state);
            }
            EditorUtility.SetDirty(controller);
            return controller;
        }

        static Mesh Dress(SkinnedMeshRenderer skin, Animator animator, Transform root, bool ordinary = false)
        {
            var source = skin.sharedMesh;
            var vertices = source.vertices.ToList();
            var uv = source.uv.ToList();
            var weights = source.boneWeights.ToList();
            var triangles = Enumerable.Range(0, 10).Select(_ => new List<int>()).ToArray();
            // Đỉnh có trọng số phải được sinh trong bind pose của mesh, không theo pose FBX đang hiển thị.
            Vector3 Local(Transform t) => source.bindposes[Array.IndexOf(skin.bones, t)].inverse.GetColumn(3);
            Vector3 RootBone(Transform t) => root.InverseTransformPoint(skin.transform.TransformPoint(Local(t)));
            Vector3 RootPoint(int i) => root.InverseTransformPoint(skin.transform.TransformPoint(vertices[i]));
            int Bone(Transform t) => Array.IndexOf(skin.bones, t);
            var leftElbow = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            var rightElbow = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var sleeveEdges = new[] { new List<int>(), new List<int>() };
            var cutVertices = new Dictionary<(int, int), int>();
            float elbowX = Mathf.Abs(RootBone(leftElbow).x);
            float waist = RootBone(animator.GetBoneTransform(HumanBodyBones.Hips)).y + 0.15f;
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                string material = skin.sharedMaterials[sub].name.ToLowerInvariant();
                var indices = source.GetTriangles(sub);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                    var center = (RootPoint(a) + RootPoint(b) + RootPoint(c)) / 3f;
                    bool head = material.Contains("head") || material.Contains("hair");
                    // Bỏ cúc nguồn quá lớn; sinh hai hàng nút dẹt đúng áo bếp bên dưới.
                    if (material.Contains("button") || (!ordinary && material.Contains("hair"))) continue;
                    // Cắt đúng mặt phẳng khuỷu, không bỏ cả tam giác rồi để lại mép nhọn.
                    if (!head && Mathf.Abs(center.x) > elbowX - 0.12f && center.y > 1.1f)
                    {
                        bool left = center.x < 0f;
                        float plane = (left ? -1f : 1f) * (elbowX - 0.08f);
                        int elbowBone = Bone(left ? leftElbow : rightElbow);
                        bool Inside(int index) => left ? RootPoint(index).x >= plane : RootPoint(index).x <= plane;
                        var polygon = new List<int>();
                        var corners = new[] { a, b, c };
                        for (int k = 0; k < 3; k++)
                        {
                            int previous = corners[(k + 2) % 3], current = corners[k];
                            bool previousInside = Inside(previous), currentInside = Inside(current);
                            if (previousInside != currentInside)
                            {
                                float t = (plane - RootPoint(previous).x) / (RootPoint(current).x - RootPoint(previous).x);
                                float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(elbowX - 0.10f, elbowX + 0.10f, Mathf.Abs(plane)));
                                var key = (Mathf.Min(previous, current), Mathf.Max(previous, current));
                                if (!cutVertices.TryGetValue(key, out int edge))
                                {
                                    edge = Add(Vector3.Lerp(vertices[previous], vertices[current], t), Vector2.Lerp(uv[previous], uv[current], t),
                                        Weight(Bone((left ? leftElbow : rightElbow).parent), elbowBone, blend));
                                    cutVertices.Add(key, edge);
                                }
                                polygon.Add(edge);
                                sleeveEdges[left ? 0 : 1].Add(edge);
                            }
                            if (currentInside)
                            {
                                // Chuyển dần từ tay trên sang khuỷu; đổi cứng cả đoạn cuối làm vải gập ngược khi xoay.
                                float elbowBlend = Mathf.SmoothStep(0f, 1f,
                                    Mathf.InverseLerp(elbowX - 0.10f, elbowX + 0.10f, Mathf.Abs(RootPoint(current).x)));
                                if (elbowBlend > 0f) weights[current] = Weight(Bone((left ? leftElbow : rightElbow).parent), elbowBone, elbowBlend);
                                polygon.Add(current);
                            }
                        }
                        for (int k = 1; k + 1 < polygon.Count; k++) triangles[0].AddRange(new[] { polygon[0], polygon[k], polygon[k + 1] });
                        continue;
                    }
                    int slot = head ? 3 : material.Contains("leather") ? (center.y > 0.095f ? 1 : center.z > 0.16f ? 8 : 5)
                        : center.y < waist ? 1 : 0;
                    triangles[slot].AddRange(new[] { a, b, c });
                }
            }
            int Add(Vector3 point, Vector2 tex, BoneWeight weight)
            {
                int index = vertices.Count;
                vertices.Add(point); uv.Add(tex); weights.Add(weight);
                return index;
            }
            void Quad(int slot, int a, int b, int c, int d)
            {
                triangles[slot].AddRange(new[] { a, b, c, a, c, d });
            }
            BoneWeight Weight(int first, int second = -1, float blend = 0f) => new BoneWeight
            {
                boneIndex0 = first, weight0 = 1f - blend, boneIndex1 = second < 0 ? first : second, weight1 = blend
            };

            foreach (bool left in new[] { true, false })
            {
                var elbow = left ? leftElbow : rightElbow;
                var wrist = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                var start = Local(elbow); var end = Local(wrist);
                var axis = (end - start).normalized;
                var up = skin.transform.InverseTransformDirection(root.up).normalized;
                var acrossDir = Vector3.Cross(axis, up).normalized;
                int first = vertices.Count;
                const int sides = 24, rings = 25;
                // Ống tay áo dài tới cổ tay: cùng vải/màu với cuff của FBX tay (cuff phủ 18 cm cuối, loe ra 5,2 cm).
                    for (int j = 0; j < rings; j++)
                {
                    float t = (float)j / (rings - 1);
                    var center = Vector3.Lerp(start, end - axis * 0.012f, t);
                    float d = Vector3.Distance(center, end) * 0.95f;
                    float across = SleeveRadius(d, 0.050f), thick = SleeveRadius(d, 0.038f);
                    float elbowDistance = Vector3.Dot(center - start, axis);
                    float fullness = 1f + 0.16f * Mathf.Exp(-elbowDistance * elbowDistance / 0.004f);
                    across *= fullness; thick *= fullness;
                    // Vòng cuối theo cổ tay: mối nối nằm dưới cuff FBX.
                    // Cuff runtime theo cẳng tay; chỉ vùng cổ tay ngắn theo góc gập bàn tay.
                    float wristBlend = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.012f, 0.035f, d));
                    // Vùng khớp dài 20 cm có vòng mỗi ~2 cm, không gom cả mép áo vào một xương.
                    float elbowBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.10f, 0f, elbowDistance));
                    var w = wristBlend > 0f ? Weight(Bone(elbow), Bone(wrist), wristBlend)
                        : Weight(Bone(elbow.parent), Bone(elbow), elbowBlend);
                    for (int k = 0; k < sides; k++)
                    {
                        float angle = k * Mathf.PI * 2f / sides;
                        Add(center + (thick * Mathf.Cos(angle)) * up * (1f / 0.95f) + (across * Mathf.Sin(angle)) * acrossDir * (1f / 0.95f),
                            new Vector2(0.3f + 0.4f * k / sides, 0.3f + 0.4f * t), w);
                    }
                }
                for (int j = 0; j < rings - 1; j++) for (int k = 0; k < sides; k++)
                {
                    int a = first + j * sides + k, b = first + j * sides + (k + 1) % sides;
                    Quad(0, a, b, b + sides, a + sides);
                }
                // Khâu mép cắt nguồn vào vòng đầu ống tay bằng chính các đỉnh có cùng trọng số khuỷu.
                // Không chèn khối cầu: hai mép phải liên tục cả khi IK kéo dài hoặc xoay cẳng tay.
                float Angle(int index)
                {
                    var offset = vertices[index] - start;
                    return Mathf.Repeat(Mathf.Atan2(Vector3.Dot(offset, acrossDir), Vector3.Dot(offset, up)), Mathf.PI * 2f);
                }
                var edgeRing = new List<int>();
                foreach (int index in sleeveEdges[left ? 0 : 1])
                {
                    int match = edgeRing.FindIndex(other => (vertices[index] - vertices[other]).sqrMagnitude < 0.000000001f);
                    if (match < 0) edgeRing.Add(index);
                    else if (edgeRing[match] != index)
                    {
                        int welded = edgeRing[match];
                        for (int k = 0; k < triangles[0].Count; k++) if (triangles[0][k] == index) triangles[0][k] = welded;
                        weights[index] = weights[welded];
                    }
                }
                edgeRing.Sort((a, b) => Angle(a).CompareTo(Angle(b)));
                if (edgeRing.Count < 3) throw new InvalidOperationException("Mép tay áo tại khuỷu không tạo được vòng nối");
                void Stitch(int a, int b, int c)
                {
                    var center = (vertices[a] + vertices[b] + vertices[c]) / 3f;
                    var radial = Vector3.ProjectOnPlane(center - start, axis);
                    if (Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]), radial) < 0f)
                        (b, c) = (c, b);
                    triangles[0].AddRange(new[] { a, b, c });
                }
                int sourceStep = 0, tubeStep = 0;
                while (sourceStep < edgeRing.Count || tubeStep < sides)
                {
                    int a = edgeRing[sourceStep % edgeRing.Count], b = first + tubeStep % sides;
                    float nextSource = sourceStep + 1 < edgeRing.Count ? Angle(edgeRing[sourceStep + 1]) : Mathf.PI * 2f;
                    float nextTube = (tubeStep + 1) * Mathf.PI * 2f / sides;
                    if (sourceStep < edgeRing.Count && (tubeStep == sides || nextSource <= nextTube))
                    {
                        Stitch(a, edgeRing[(sourceStep + 1) % edgeRing.Count], b);
                        sourceStep++;
                    }
                    else
                    {
                        Stitch(a, first + (tubeStep + 1) % sides, b);
                        tubeStep++;
                    }
                }
            }

            int hips = Bone(animator.GetBoneTransform(HumanBodyBones.Hips));
            int chest = Bone(animator.GetBoneTransform(HumanBodyBones.Chest));
            // Mặt áo hai vạt chồng và hàng nút: yếm chỉ buộc eo để ngực áo lộ rõ khi cúi.
            void Oval(int slot, Vector3 center, Vector3 radius, BoneWeight weight)
            {
                int first = vertices.Count;
                const int around = 12, levels = 6;
                for (int j = 0; j <= levels; j++) for (int k = 0; k < around; k++)
                {
                    float v = j * Mathf.PI / levels, angle = k * Mathf.PI * 2f / around;
                    var p = center + Vector3.Scale(radius, new Vector3(Mathf.Sin(v) * Mathf.Cos(angle), Mathf.Cos(v), Mathf.Sin(v) * Mathf.Sin(angle)));
                    Add(skin.transform.InverseTransformPoint(root.TransformPoint(p)), new Vector2((float)k / around, (float)j / levels), weight);
                }
                for (int j = 0; j < levels; j++) for (int k = 0; k < around; k++)
                {
                    int a = first + j * around + k, b = first + j * around + (k + 1) % around;
                    Quad(slot, a, b, b + around, a + around);
                }
            }
            // Dò bề mặt mesh gốc bằng collider tạm của editor: nút và dây không nằm chìm/lơ lửng.
            var surfaceCollider = skin.gameObject.AddComponent<MeshCollider>();
            surfaceCollider.sharedMesh = source;
            Physics.SyncTransforms();
            var sourceTriangles = source.triangles;
            Vector3 Surface(Vector3 from, Vector3 direction, out BoneWeight weight)
            {
                var ray = new Ray(root.TransformPoint(from), root.TransformDirection(direction));
                if (!surfaceCollider.Raycast(ray, out var hit, 0.8f))
                    throw new InvalidOperationException($"Không tìm thấy mặt trang phục tại {from}");
                var bary = hit.barycentricCoordinate;
                int corner = bary.x > bary.y && bary.x > bary.z ? 0 : bary.y > bary.z ? 1 : 2;
                weight = source.boneWeights[sourceTriangles[hit.triangleIndex * 3 + corner]];
                return root.InverseTransformPoint(hit.point);
            }
            for (int row = 0; row < 5; row++) foreach (float x in ordinary ? new[] { 0f } : new[] { -0.065f, 0.065f })
            {
                float y = 1.12f + row * 0.065f;
                var surface = Surface(new Vector3(x, y, 0.4f), Vector3.back, out var w);
                float z = surface.z + 0.003f;
                Oval(7, new Vector3(x, y, z), new Vector3(0.009f, 0.009f, 0.003f), w);
                // Khuy nhỏ nằm cạnh nút, chìm hơn nút để không thành viên sỏi.
                Oval(7, new Vector3(x + 0.014f, y, z - 0.002f), new Vector3(0.006f, 0.0015f, 0.001f), w);
            }
            // Cổ đứng thấp ôm chân cổ, không dựng cao che cằm.
            var neck = RootBone(animator.GetBoneTransform(HumanBodyBones.Neck));
            int collar = vertices.Count;
            for (int j = 0; j < 2; j++) for (int k = 0; k < 32; k++)
            {
                float a = k * Mathf.PI * 2f / 32f;
                var p = neck + new Vector3(0.080f * Mathf.Sin(a), -0.055f + j * 0.038f, 0.027f + 0.078f * Mathf.Cos(a));
                Add(skin.transform.InverseTransformPoint(root.TransformPoint(p)), new Vector2((float)k / 32, j), Weight(chest));
            }
            for (int k = 0; k < 32; k++) Quad(0, collar + k, collar + (k + 1) % 32, collar + (k + 1) % 32 + 32, collar + k + 32);
            // Mũ bonnet mềm, đỉnh thấp hơi xẹp, lệch trái; đi theo xương đầu, chung cơ chế ẩn camera chính.
            int headBone = Bone(animator.GetBoneTransform(HumanBodyBones.Head));
            var headPosition = RootBone(animator.GetBoneTransform(HumanBodyBones.Head));
            int hatStart = vertices.Count;
            var hatProfile = new[] { new Vector2(0f, 1f), new Vector2(0.018f, 1.02f), new Vector2(0.028f, 1.16f),
                new Vector2(0.052f, 1.23f), new Vector2(0.079f, 1.08f), new Vector2(0.103f, 0.65f), new Vector2(0.095f, 0f) };
            for (int j = 0; j < hatProfile.Length; j++) for (int k = 0; k < 48; k++)
            {
                float a = k * Mathf.PI * 2f / 48f;
                float r = hatProfile[j].y * (1f + 0.035f * Mathf.Sin(a * 7f + j * 0.7f));
                var p = headPosition + new Vector3(-0.015f + Mathf.Sin(a) * 0.104f * r,
                    0.135f + hatProfile[j].x + 0.018f * Mathf.Sin(a) * r, 0.003f + Mathf.Cos(a) * 0.117f * r);
                Add(skin.transform.InverseTransformPoint(root.TransformPoint(p)), new Vector2((float)k / 48, (float)j / 6), Weight(headBone));
            }
            for (int j = 0; j < hatProfile.Length - 1; j++) for (int k = 0; k < 48; k++)
            {
                int a = hatStart + j * 48 + k, b = hatStart + j * 48 + (k + 1) % 48;
                Quad(6, a, b, b + 48, a + 48);
            }
            // Dây lưng tạp dề: dải mỏng quanh eo đi theo hông, ôm sát bụng và lưng.
            int belt = vertices.Count;
            for (int j = 0; j < 2; j++) for (int i = 0; i < 32; i++)
            {
                float angle = i * Mathf.PI * 2f / 32f;
                var point = new Vector3(Mathf.Sin(angle) * 0.195f, 1.035f + j * 0.03f, 0.0145f + Mathf.Cos(angle) * 0.1555f);
                Add(skin.transform.InverseTransformPoint(root.TransformPoint(point)), new Vector2((float)i / 32f, j), Weight(hips));
            }
            for (int i = 0; i < 32; i++) Quad(4, belt + i, belt + (i + 1) % 32, belt + (i + 1) % 32 + 32, belt + i + 32);
            // Giữ giày kín nguồn; thêm dây buộc mảnh và đường da sáng ở mũi, tuyệt đối không quai dép.
            foreach (bool left in new[] { true, false })
            {
                var foot = animator.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                var origin = RootBone(foot);
                for (int row = 0; row < 4; row++)
                {
                    var surface = Surface(new Vector3(origin.x, 0.3f, origin.z + 0.080f + row * 0.018f),
                        Vector3.down, out var w);
                    Oval(9, surface + Vector3.up * 0.003f, new Vector3(0.030f, 0.0025f, 0.0025f), w);
                }
            }
            Object.DestroyImmediate(surfaceCollider);
            var mesh = new Mesh { name = "BaBody", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.boneWeights = weights.ToArray(); mesh.bindposes = source.bindposes;
            mesh.subMeshCount = triangles.Length;
            for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
            if (ordinary)
            {
                mesh.SetTriangles(Array.Empty<int>(), 4); // Áo thường không dây eo tạp dề.
                mesh.SetTriangles(Array.Empty<int>(), 6); // Không mũ phụ bếp.
            }
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }

        // Bán kính ống tay theo khoảng cách tới cổ tay, bám đường viền cuff FBX (bán kính 3,1 cm ở cổ tay loe 5,2 cm ở mép sau) nhưng nhỏ hơn 10% để nằm dưới cuff.
        static readonly Vector2[] CuffProfile =
        {
            new Vector2(0f, 0.60f), new Vector2(0.01f, 0.62f), new Vector2(0.02f, 0.73f), new Vector2(0.03f, 0.87f),
            new Vector2(0.05f, 0.88f), new Vector2(0.08f, 0.81f), new Vector2(0.12f, 0.90f), new Vector2(0.18f, 1f),
        };

        static float SleeveRadius(float distance, float baseRadius)
        {
            float f = 1f;
            if (distance < 0.18f)
                for (int i = 1; i < CuffProfile.Length; i++)
                    if (distance <= CuffProfile[i].x)
                    {
                        float k = Mathf.InverseLerp(CuffProfile[i - 1].x, CuffProfile[i].x, distance);
                        f = Mathf.Lerp(CuffProfile[i - 1].y, CuffProfile[i].y, k);
                        break;
                    }
            return baseRadius / 0.050f * 0.052f * f * 0.9f;
        }

        static void FitHandSleeves(HandsRig hands)
        {
            // Cuff FBX giữ nguyên chiều dài; chỉ đổi sang cùng vật liệu áo với ống tay của thân (Gen_BaShirt) để liền mạch.
            var shirt = ShirtMaterial();
            foreach (var pose in hands.leftPoseMeshes.Concat(hands.rightPoseMeshes))
                foreach (var renderer in pose.GetComponentsInChildren<MeshRenderer>(true))
                    if (renderer.name.StartsWith("Sleeve")) renderer.sharedMaterial = shirt;
        }
    }
}
