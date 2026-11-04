using System;
using System.IO;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Player;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.Import
{
    /// <summary>
    /// Dựng bàn tay FP có xương ngón từ Hand_{L,R}_Rig.json (Tools/blender/make_fp_hand_rig.py): mesh da Rocketbox đã skin vào Wrist +
    /// 15 xương ngón, cùng khung với các mesh tư thế dựng sẵn (+Z ngón tay, −Y lòng bàn tay). Mọi xương xoay đơn vị lúc dựng, nên bindpose
    /// chỉ là phép tịnh tiến về đầu xương và <see cref="HandFingers"/> có thể xoay xương trực tiếp.
    /// </summary>
    public static class HandRigBuilder
    {
        [Serializable]
        class BoneData { public string name; public int parent; public float[] head, tail; }

        [Serializable]
        class RigData
        {
            public string side;
            public BoneData[] bones;
            public float[] vertices, normals, tangents, uv, boneWeight;
            public int[] triangles, boneIndex;
        }

        public static string JsonPath(string side) => $"{HandsImporter.Folder}/Hand_{side}_Rig.json";
        public static string MeshPath(string side) => $"{HandsImporter.Folder}/Gen_HandRig_{side}.asset";

        [MenuItem("HaiBanTay/Art/Import Hand Rig")]
        public static void Run()
        {
            try
            {
                foreach (var side in new[] { "L", "R" }) EnsureMesh(side);
                AssetDatabase.SaveAssets();
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogError("HandRigBuilder: " + error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        /// <summary>Có dữ liệu xương cho cả hai tay (đã nhập hoặc còn ở Staging).</summary>
        public static bool Available => Has("L") && Has("R");

        static bool Has(string side) => File.Exists(Path.Combine(Application.dataPath, "../" + JsonPath(side))) || File.Exists(Staging(side));

        static string Staging(string side) =>
            Path.GetFullPath(Path.Combine(Application.dataPath, $"../../Staging/art/hands/Hand_{side}_Rig.json"));

        static RigData Load(string side)
        {
            string assetFile = Path.GetFullPath(Path.Combine(Application.dataPath, "../" + JsonPath(side)));
            // Staging là nguồn mới nhất khi vừa chạy lại Blender.
            if (File.Exists(Staging(side)))
            {
                AssetFactory.EnsureFolder(HandsImporter.Folder);
                if (!File.Exists(assetFile) || File.GetLastWriteTimeUtc(Staging(side)) > File.GetLastWriteTimeUtc(assetFile))
                {
                    File.Copy(Staging(side), assetFile, true);
                    AssetDatabase.ImportAsset(JsonPath(side), ImportAssetOptions.ForceSynchronousImport);
                }
            }
            if (!File.Exists(assetFile)) throw new FileNotFoundException($"Thiếu {JsonPath(side)} (chạy Tools/blender/make_fp_hand_rig.py)");
            return JsonUtility.FromJson<RigData>(File.ReadAllText(assetFile));
        }

        static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);

        public static Mesh EnsureMesh(string side)
        {
            var data = Load(side);
            int count = data.vertices.Length / 3;
            var vertices = new Vector3[count];
            var normals = new Vector3[count];
            var tangents = new Vector4[count];
            var uv = new Vector2[count];
            var weights = new BoneWeight[count];
            for (int i = 0; i < count; i++)
            {
                vertices[i] = new Vector3(data.vertices[i * 3], data.vertices[i * 3 + 1], data.vertices[i * 3 + 2]);
                normals[i] = new Vector3(data.normals[i * 3], data.normals[i * 3 + 1], data.normals[i * 3 + 2]);
                tangents[i] = new Vector4(data.tangents[i * 4], data.tangents[i * 4 + 1], data.tangents[i * 4 + 2], data.tangents[i * 4 + 3]);
                uv[i] = new Vector2(data.uv[i * 2], data.uv[i * 2 + 1]);
                var w = new BoneWeight
                {
                    boneIndex0 = data.boneIndex[i * 4], boneIndex1 = data.boneIndex[i * 4 + 1],
                    boneIndex2 = data.boneIndex[i * 4 + 2], boneIndex3 = data.boneIndex[i * 4 + 3],
                    weight0 = data.boneWeight[i * 4], weight1 = data.boneWeight[i * 4 + 1],
                    weight2 = data.boneWeight[i * 4 + 2], weight3 = data.boneWeight[i * 4 + 3]
                };
                weights[i] = w;
            }
            int forearm = data.bones.Length;
            var triangles = AddForearm(data.triangles, ref vertices, ref normals, ref tangents, ref uv, ref weights, forearm);
            var bindposes = new Matrix4x4[data.bones.Length + 1];
            for (int i = 0; i < data.bones.Length; i++) bindposes[i] = Matrix4x4.Translate(-V(data.bones[i].head));
            bindposes[forearm] = Matrix4x4.identity;
            var mesh = new Mesh { name = $"Gen_HandRig_{side}" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.boneWeights = weights;
            mesh.bindposes = bindposes;
            mesh.RecalculateBounds();
            AssetFactory.EnsureFolder(HandsImporter.Folder);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(side));
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                UnityEngine.Object.DestroyImmediate(mesh);
                mesh = existing;
                EditorUtility.SetDirty(mesh);
            }
            else AssetDatabase.CreateAsset(mesh, MeshPath(side));
            return mesh;
        }

        // Đoạn da cẳng tay nối vào trong ống cổ tay: mép hở của mesh da (z ≈ 0) đùn lùi theo −Z, hơi to dần như cẳng tay thật, vẫn nằm
        // trong lòng ống tay FBX (miệng ống 2,9 × 2,0 cm, loe 4 cm sau 2,5 cm). Cổ tay gập thì nhìn vào miệng ống thấy da, không thấy lòng ống rỗng.
        static readonly Vector2[] ForearmRings = { new Vector2(-.006f, 1.04f), new Vector2(-.018f, 1.1f), new Vector2(-.035f, 1.15f), new Vector2(-.055f, 1.18f) };
        // Vùng cổ tay của da đi dần theo xương cẳng tay (gốc ở cổ tay, xoay theo ống tay áo): z ≥ 3 cm theo bàn tay, mép cổ tay ~85% theo cẳng tay,
        // đoạn đùn theo hẳn cẳng tay. Bàn tay gập ở cổ tay thì da uốn mềm như khớp thật thay vì gãy góc ở miệng ống.
        const float BlendFront = .03f, BlendBack = -.01f;

        static int[] AddForearm(int[] source, ref Vector3[] vertices, ref Vector3[] normals, ref Vector4[] tangents, ref Vector2[] uv,
            ref BoneWeight[] weights, int forearm)
        {
            // Đỉnh trùng vị trí (đường nối UV) hàn lại trước khi dò mép, không thì mép cổ tay đứt thành nhiều đoạn.
            var weld = new int[vertices.Length];
            var byPosition = new System.Collections.Generic.Dictionary<Vector3Int, int>();
            for (int i = 0; i < vertices.Length; i++)
            {
                var key = Vector3Int.RoundToInt(vertices[i] * 100000f);
                if (!byPosition.TryGetValue(key, out int first)) byPosition.Add(key, first = i);
                weld[i] = first;
            }
            // Cạnh biên: cạnh chỉ thuộc một tam giác, nằm ở mép cổ tay.
            var edges = new System.Collections.Generic.Dictionary<(int, int), int>();
            for (int t = 0; t < source.Length; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = weld[source[t + e]], b = weld[source[t + (e + 1) % 3]];
                    var key = a < b ? (a, b) : (b, a);
                    edges[key] = edges.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            var next = new System.Collections.Generic.Dictionary<int, int>();
            for (int t = 0; t < source.Length; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = weld[source[t + e]], b = weld[source[t + (e + 1) % 3]];
                    if (edges[a < b ? (a, b) : (b, a)] == 1 && vertices[a].z < .01f && vertices[b].z < .01f) next[a] = b;
                }
            if (next.Count < 8) throw new InvalidOperationException($"HandRigBuilder: không tìm thấy mép cổ tay ({next.Count} cạnh)");
            var loop = new System.Collections.Generic.List<int>();
            int start = -1;
            foreach (var k in next.Keys) if (start < 0 || vertices[k].x > vertices[start].x) start = k;
            for (int v = start; loop.Count <= next.Count; v = next[v])
            {
                loop.Add(v);
                if (!next.ContainsKey(next[v]) || next[v] == start) break;
            }
            if (loop.Count != next.Count) throw new InvalidOperationException($"HandRigBuilder: mép cổ tay không khép kín ({loop.Count}/{next.Count})");
            var center = Vector3.zero;
            foreach (int v in loop) center += new Vector3(vertices[v].x, vertices[v].y, 0f);
            center /= loop.Count;
            // Vòng mép cổ tay của mesh Rocketbox mang texture mép áo cũ (sọc ngà lộ ra ở miệng ống tay): lấy UV da cổ tay ngay phía trước
            // cùng góc quanh trục cổ tay cho mọi đỉnh z < 8 mm (và cho đoạn đùn).
            var shape = vertices;
            float Angle(Vector3 p) => Mathf.Atan2(p.y - center.y, p.x - center.x);
            var donors = new System.Collections.Generic.List<int>();
            for (int i = 0; i < vertices.Length; i++)
                if (vertices[i].z > .012f && vertices[i].z < .022f && weights[i].boneIndex0 == 0 && weights[i].weight0 > .9f) donors.Add(i);
            int Donor(Vector3 p)
            {
                int best = donors[0]; float bestDiff = float.MaxValue, angle = Angle(p);
                foreach (int d in donors)
                {
                    float diff = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, Angle(shape[d]) * Mathf.Rad2Deg));
                    if (diff < bestDiff) { bestDiff = diff; best = d; }
                }
                return best;
            }
            var sourceUv = (Vector2[])uv.Clone(); var sourceTangents = (Vector4[])tangents.Clone();
            for (int i = 0; i < vertices.Length; i++)
                if (vertices[i].z < .008f) { int d = Donor(vertices[i]); uv[i] = sourceUv[d]; tangents[i] = sourceTangents[d]; }
            // Trọng số: phần xương cổ tay (bone 0) của đỉnh gần cổ tay chuyển dần sang xương cẳng tay.
            for (int i = 0; i < weights.Length; i++)
            {
                float share = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(BlendBack, BlendFront, vertices[i].z));
                if (share > 0f) weights[i] = MoveShare(weights[i], 0, forearm, share);
            }
            int baseCount = vertices.Length, n0 = loop.Count;
            var vs = new System.Collections.Generic.List<Vector3>(vertices); var ns = new System.Collections.Generic.List<Vector3>(normals);
            var ts = new System.Collections.Generic.List<Vector4>(tangents); var us = new System.Collections.Generic.List<Vector2>(uv);
            var ws = new System.Collections.Generic.List<BoneWeight>(weights);
            var tris = new System.Collections.Generic.List<int>(source);
            foreach (var ring in ForearmRings)
                foreach (int v in loop)
                {
                    var p = vertices[v];
                    var radial = new Vector3(p.x - center.x, p.y - center.y, 0f);
                    vs.Add(new Vector3(center.x + radial.x * ring.y, center.y + radial.y * ring.y, ring.x));
                    ns.Add(radial.normalized);
                    ts.Add(tangents[v]);
                    us.Add(uv[v]);
                    ws.Add(new BoneWeight { boneIndex0 = forearm, weight0 = 1f });
                }
            // Hướng mặt theo tam giác gốc chứa cạnh biên (loop[k] → loop[k+1] đi cùng chiều tam giác đó): quad mới đi ngược lại.
            for (int r = 0; r < ForearmRings.Length; r++)
                for (int k = 0; k < n0; k++)
                {
                    int a = r == 0 ? loop[k] : baseCount + (r - 1) * n0 + k;
                    int b = r == 0 ? loop[(k + 1) % n0] : baseCount + (r - 1) * n0 + (k + 1) % n0;
                    int c = baseCount + r * n0 + k, d = baseCount + r * n0 + (k + 1) % n0;
                    tris.AddRange(new[] { b, a, c, b, c, d });
                }
            vertices = vs.ToArray(); normals = ns.ToArray(); tangents = ts.ToArray(); uv = us.ToArray(); weights = ws.ToArray();
            return tris.ToArray();
        }

        static BoneWeight MoveShare(BoneWeight w, int from, int to, float share)
        {
            var bones = new[] { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
            var values = new[] { w.weight0, w.weight1, w.weight2, w.weight3 };
            int slot = Array.IndexOf(bones, from);
            if (slot < 0 || values[slot] <= 0f) return w;
            float moved = values[slot] * share;
            values[slot] -= moved;
            // Ô trống (hoặc ô nhẹ nhất, khác ô cổ tay) nhận xương cẳng tay; trọng số bị thay trả về xương cổ tay để tổng vẫn bằng 1.
            int free = slot == 0 ? 1 : 0;
            for (int i = 0; i < 4; i++) if (i != slot && values[i] < values[free]) free = i;
            values[slot] += values[free];
            bones[free] = to; values[free] = moved;
            Array.Sort(values, bones);
            Array.Reverse(values); Array.Reverse(bones);
            return new BoneWeight
            {
                boneIndex0 = bones[0], boneIndex1 = bones[1], boneIndex2 = bones[2], boneIndex3 = bones[3],
                weight0 = values[0], weight1 = values[1], weight2 = values[2], weight3 = values[3]
            };
        }

        /// <summary>Dựng bàn tay có xương dưới <paramref name="parent"/> (khung tay của HandsRig); tắt sẵn. Áo tay lấy từ mesh Open cùng bên.</summary>
        public static HandFingers Build(Transform parent, string side, Material skin, Material sleeve)
        {
            var data = Load(side);
            var mesh = EnsureMesh(side);
            var root = new GameObject($"Hand_{side}_Rigged");
            root.transform.SetParent(parent, false);

            var bones = new Transform[data.bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = data.bones[i];
                var transform = new GameObject(bone.name).transform;
                transform.SetParent(bone.parent < 0 ? root.transform : bones[bone.parent], false);
                transform.localPosition = V(bone.head) - (bone.parent < 0 ? Vector3.zero : V(data.bones[bone.parent].head));
                bones[i] = transform;
            }

            // Xương cẳng tay (gốc ở cổ tay): PlayerBody xoay theo ống tay áo mỗi khung để vùng cổ tay của da uốn theo.
            var forearmBone = new GameObject("Forearm").transform;
            forearmBone.SetParent(root.transform, false);
            var skinBones = new Transform[bones.Length + 1];
            bones.CopyTo(skinBones, 0);
            skinBones[bones.Length] = forearmBone;

            var skinObject = new GameObject("RiggedSkin");
            skinObject.transform.SetParent(root.transform, false);
            var renderer = skinObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.sharedMaterial = skin;
            renderer.bones = skinBones;
            renderer.rootBone = bones[0];
            // Ngón gập vào lòng bàn tay không ra ngoài khối này; cho dư để mesh không bị cắt khi tay ở rìa khung hình.
            var bounds = mesh.bounds;
            bounds.Expand(0.1f);
            renderer.localBounds = bounds;
            renderer.updateWhenOffscreen = false;

            var fingers = root.AddComponent<HandFingers>();
            fingers.bones = bones;
            fingers.forearm = forearmBone;
            fingers.restDirections = new Vector3[HandFingers.Digits * HandFingers.Joints];
            for (int i = 1; i < data.bones.Length; i++)
                fingers.restDirections[i - 1] = (V(data.bones[i].tail) - V(data.bones[i].head)).normalized;
            fingers.restHeads = new Vector3[HandFingers.Digits];
            for (int d = 0; d < HandFingers.Digits; d++)
                fingers.restHeads[d] = V(data.bones[1 + d * HandFingers.Joints].head);

            AddSleeve(root.transform, side, sleeve);
            root.SetActive(false);
            return fingers;
        }

        static void AddSleeve(Transform root, string side, Material sleeve)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{HandsImporter.Folder}/Hand_{side}_Open.fbx");
            if (model == null) return;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.name.StartsWith("Sleeve")) continue;
                var go = new GameObject("Sleeve");
                go.transform.SetParent(root, false);
                go.transform.localPosition = model.transform.InverseTransformPoint(filter.transform.position);
                go.transform.localRotation = Quaternion.Inverse(model.transform.rotation) * filter.transform.rotation;
                go.transform.localScale = filter.transform.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = sleeve != null ? sleeve : filter.GetComponent<MeshRenderer>().sharedMaterial;
            }
        }
    }
}
