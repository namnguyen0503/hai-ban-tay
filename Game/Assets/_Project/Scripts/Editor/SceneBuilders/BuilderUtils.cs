using System.IO;
using HaiBanTay.Core;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Performance;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace HaiBanTay.Editor.SceneBuilders
{
    public static class BuilderUtils
    {
        public const string ScenesFolder = "Assets/_Project/Scenes";
        public const string ArtPrefabFolder = "Assets/_Project/Prefabs/Art";
        public const int IgnoreRaycastLayer = 2;

        public static string ScenePath(string sceneName) => $"{ScenesFolder}/{sceneName}.unity";

        public static Scene NewScene()
        {
            return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        public static void Save(Scene scene, string sceneName)
        {
            Directory.CreateDirectory(ScenesFolder);
            if (!EditorSceneManager.SaveScene(scene, ScenePath(sceneName)))
                throw new IOException("Không lưu được scene " + sceneName);
        }

        public static int Layer(string name)
        {
            int l = LayerMask.NameToLayer(name);
            return l < 0 ? 0 : l;
        }

        public static GameObject Group(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>Camera chính: CinemachineBrain, FXAA, hậu kỳ, AudioListener.</summary>
        public static Camera AddMainCamera(Color background, bool brain = true)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 600f;
            cam.fieldOfView = 65f;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            data.renderPostProcessing = true;
            go.AddComponent<AudioListener>();
            if (brain)
                go.AddComponent<CinemachineBrain>();
            return cam;
        }

        // Giữ cho BootSceneBuilder và cảnh giữ chỗ.
        public static Camera AddCamera(Vector3 position, Vector3 euler)
        {
            var cam = AddMainCamera(new Color(0.05f, 0.05f, 0.06f), brain: false);
            cam.transform.SetPositionAndRotation(position, Quaternion.Euler(euler));
            return cam;
        }

        /// <summary>Ambient, sương, mặt trời theo SceneLightingProfile.</summary>
        public static Light ApplyLighting(SceneLightingProfile p)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = p.ambientSky;
            RenderSettings.ambientEquatorColor = p.ambientEquator;
            RenderSettings.ambientGroundColor = p.ambientGround;
            RenderSettings.skybox = null;
            RenderSettings.fog = p.fog;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = p.fogColor;
            RenderSettings.fogDensity = p.fogDensity;
            Lightmapping.lightingSettings = LightingSettingsAsset();
            if (!p.sun)
                return null;
            var go = new GameObject("Sun");
            go.transform.rotation = Quaternion.Euler(p.sunEuler);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = p.sunColor;
            light.intensity = p.sunIntensity;
            light.shadows = p.sunShadows ? LightShadows.Hard : LightShadows.None;
            // Mixed (Baked Indirect): ánh sáng trực tiếp realtime không bóng (rẻ), gián tiếp bake.
            light.lightmapBakeType = LightmapBakeType.Mixed;
            RenderSettings.sun = light;
            return light;
        }

        // Một LightingSettings dùng chung cho mọi cảnh: Progressive CPU, lightmap nhỏ (PLAN mục 3.3, M6).
        static LightingSettings LightingSettingsAsset()
        {
            const string path = AssetFactory.ConfigFolder + "/LightingSettings.lighting";
            var s = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (s != null)
                return s;
            s = new LightingSettings
            {
                bakedGI = true,
                realtimeGI = false,
                lightmapper = LightingSettings.Lightmapper.ProgressiveCPU,
                lightmapResolution = 12f,
                lightmapMaxSize = 1024,
                directSampleCount = 16,
                indirectSampleCount = 128,
                environmentSampleCount = 64,
                maxBounces = 2,
                mixedBakeMode = MixedLightingMode.IndirectOnly,
                lightmapCompression = LightmapCompression.NormalQuality,
            };
            AssetDatabase.CreateAsset(s, path);
            return s;
        }

        /// <summary>Volume hậu kỳ toàn cục, đổi profile theo tầng.</summary>
        public static Volume AddPostVolume(string laptopProfile = "Post_Laptop", string pcProfile = "Post_PC")
        {
            var go = new GameObject("PostVolume");
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.sharedProfile = AssetFactory.VolumeFor(laptopProfile);
            var tier = go.AddComponent<TierVolume>();
            tier.laptop = AssetFactory.VolumeFor(laptopProfile);
            tier.pc = AssetFactory.VolumeFor(pcProfile);
            return v;
        }

        /// <summary>Khối hộp có vật liệu. isStatic: tham gia bake lightmap.</summary>
        public static GameObject Block(string name, Transform parent, Vector3 position, Vector3 size, Material mat,
            bool isStatic = true, bool collider = true, Vector3? euler = null)
        {
            return Prim(PrimitiveType.Cube, name, parent, position, size, mat, isStatic, collider, euler);
        }

        public static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 size,
            Material mat, bool isStatic = true, bool collider = true, Vector3? euler = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.transform.localScale = size;
            if (mat != null)
            {
                go.GetComponent<Renderer>().sharedMaterial = mat;
                if (AssetFactory.IsSurface(mat))
                {
                    var mf = go.GetComponent<MeshFilter>();
                    // Các mảnh vách dùng chung gốc trong phòng, không khởi động lại pha tại từng tâm khối.
                    bool wall = mat.name == "Gen_GalleyWall" || mat.name == "Gen_ColdWall";
                    mf.sharedMesh = SurfaceMesh.For(type, mf.sharedMesh, size, AssetFactory.SurfaceTile(mat), wall ? position : Vector3.zero);
                }
            }
            if (!collider)
                Object.DestroyImmediate(go.GetComponent<Collider>());
            if (isStatic)
            {
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic
                                                           | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                // Khối lớn (sàn cảng, mặt phố) giảm mật độ lightmap để bake CPU nhanh và lightmap ≤ 1K.
                float extent = Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
                if (extent > LightmapFullScaleExtent)
                    go.GetComponent<MeshRenderer>().scaleInLightmap = Mathf.Max(0.1f, LightmapFullScaleExtent / extent);
            }
            return go;
        }

        const float LightmapFullScaleExtent = 12f;

        /// <summary>Lưới light probe phủ vùng chơi (vật động, đôi tay, NPC lấy ánh sáng bake từ đây).</summary>
        public static LightProbeGroup ProbeGrid(string name, Transform parent, Vector3 center, Vector3 size, Vector3 spacing)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            var group = go.AddComponent<LightProbeGroup>();
            int nx = Mathf.Max(2, Mathf.RoundToInt(size.x / spacing.x) + 1);
            int ny = Mathf.Max(2, Mathf.RoundToInt(size.y / spacing.y) + 1);
            int nz = Mathf.Max(2, Mathf.RoundToInt(size.z / spacing.z) + 1);
            var points = new Vector3[nx * ny * nz];
            int i = 0;
            for (int x = 0; x < nx; x++)
                for (int y = 0; y < ny; y++)
                    for (int z = 0; z < nz; z++)
                        points[i++] = new Vector3(
                            -size.x * 0.5f + size.x * x / (nx - 1),
                            -size.y * 0.5f + size.y * y / (ny - 1),
                            -size.z * 0.5f + size.z * z / (nz - 1));
            group.probePositions = points;
            return group;
        }

        /// <summary>Reflection probe bake (128 px, tầng Laptop).</summary>
        public static ReflectionProbe BakedReflectionProbe(string name, Transform parent, Vector3 center, Vector3 size,
            bool boxProjection)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked;
            probe.size = size;
            probe.boxProjection = boxProjection;
            probe.resolution = 128;
            probe.hdr = true;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ReflectionProbeStatic);
            return probe;
        }

        /// <summary>Phông xa: không bake, không dùng probe (chỉ ambient + mặt trời), không đổ bóng.</summary>
        public static void MakeDistantBackdrop(GameObject root)
        {
            ClearStatic(root);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        /// <summary>
        /// Có art: đặt đĩa (lớp tro, lớp bẩn — child chức năng của khối xám) ngay dưới vành miệng thật của vật chứa (xô, nồi).
        /// Vành = đỉnh của thành ngoài (đỉnh cao nhất trong số đỉnh xa trục nhất), nên quai xách vòng qua miệng không tính.
        /// Trả false khi không có art.
        /// </summary>
        public static bool FitDiscToRim(Transform slot, Transform disc, float below, float radiusFraction)
        {
            if (disc == null || !RimOf(slot, out var center, out float maxR))
                return false;
            disc.localPosition = center - new Vector3(0f, below, 0f);
            float d = 2f * maxR * radiusFraction;
            disc.localScale = new Vector3(d, disc.localScale.y, d);
            return true;
        }

        /// <summary>Tâm miệng (tọa độ slot) và bán kính lớn nhất quanh trục của art vật chứa; vành như FitDiscToRim (quai không tính).</summary>
        public static bool RimOf(Transform slot, out Vector3 center, out float maxR)
        {
            center = Vector3.zero;
            maxR = 0f;
            var art = slot.Find("Art");
            if (art == null)
                return false;
            var pts = new System.Collections.Generic.List<Vector3>();
            foreach (var mf in art.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null)
                    foreach (var v in mf.sharedMesh.vertices)
                        pts.Add(slot.InverseTransformPoint(mf.transform.TransformPoint(v)));
            if (pts.Count == 0)
                return false;
            float cx = 0f, cz = 0f;
            foreach (var p in pts) { cx += p.x; cz += p.z; }
            cx /= pts.Count;
            cz /= pts.Count;
            foreach (var p in pts)
                maxR = Mathf.Max(maxR, new Vector2(p.x - cx, p.z - cz).magnitude);
            float rim = float.MinValue;
            foreach (var p in pts)
                if (new Vector2(p.x - cx, p.z - cz).magnitude > 0.9f * maxR)
                    rim = Mathf.Max(rim, p.y);
            center = new Vector3(cx, rim, cz);
            return true;
        }

        /// <summary>
        /// Mốc cho PourMotion trên vật chứa có art: "PourRim" ở tâm miệng thật, "PourRimL"/"PourRimR" là chỗ hai cổ tay khi nắm hai bên
        /// thành ngay dưới vành (lòng bàn tay áp thành, ngón ôm qua vành). Khối xám cao cả quai nên miệng 0,34 m của khối xám nằm trên
        /// miệng thật: úp xô thì hai tay lơ lửng dưới miệng (PlayerViewSweep 15/10).
        /// </summary>
        public static void PourRimMarkers(Transform slot)
        {
            if (!RimOf(slot, out var center, out float radius))
                return;
            void Mark(string name, Vector3 local, Vector3 euler)
            {
                var t = slot.Find(name);
                if (t == null)
                {
                    t = new GameObject(name).transform;
                    t.SetParent(slot, false);
                }
                t.SetLocalPositionAndRotation(local, Quaternion.Euler(euler));
            }
            Mark("PourRim", center, Vector3.zero);
            // Hai tay lật quanh trục ngón (trái z −100 thay 80, phải z 70 thay −80), cùng chiều HandsRig.SideGrip: cẳng tay vặn ngược lúc đổ
            // xô tro (phải −129°, trái +44°). Phải dò 55…130° (ClipFit17.AshBucketRim 17/10): 55–85° không ngược, từ 100° vượt 180°.
            Mark("PourRimL", center + new Vector3(-(radius + 0.012f), -0.05f, -0.04f), new Vector3(0f, 0f, -100f));
            Mark("PourRimR", center + new Vector3(radius + 0.012f, -0.05f, -0.04f), new Vector3(0f, 0f, 70f));
        }

        /// <summary>Child khối xám vẫn hiển thị khi đã có art: lửa lò (cửa lò, cục than hồng), lớp bẩn, đống than, dây treo đèn.</summary>
        static readonly string[] FunctionalParts = { "Mouth", "Ember", "Dirt", "Pile", "Rope", "Ash" };

        /// <summary>
        /// ArtSlot (D1): luôn dựng khối xám (collider, child chức năng, kích thước chuẩn). Có prefab Prefabs/Art/{slot}.prefab
        /// thì gắn làm child "Art", co giãn vừa khung khối xám (nhãn hbt-fit-stretch: khớp từng trục) và bỏ renderer khối xám.
        /// </summary>
        public static GameObject ArtSlot(string slot, Transform parent, Vector3 position, Vector3 euler,
            System.Func<Transform, GameObject> fallback)
        {
            var go = new GameObject();
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler);
            fallback(go.transform);
            go.name = "ArtSlot_" + slot;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{ArtPrefabFolder}/{slot}.prefab");
            if (prefab != null)
                AttachArt(go.transform, prefab);
            return go;
        }

        static void AttachArt(Transform slot, GameObject prefab)
        {
            var parts = slot.GetComponentsInChildren<MeshRenderer>(true);
            // Khung để khớp: bỏ child chức năng và gờ chặn (art phải có mặt trên đúng độ cao đặt đồ).
            var body = System.Array.FindAll(parts, r => System.Array.IndexOf(FunctionalParts, r.gameObject.name) < 0
                                                        && !r.gameObject.name.StartsWith("Stop_") && !r.gameObject.name.StartsWith("Lip_"));
            if (!LocalBounds(slot, body.Length > 0 ? body : parts, out var target))
                return;
            StaticEditorFlags flags = 0;
            foreach (var r in parts)
                flags |= GameObjectUtility.GetStaticEditorFlags(r.gameObject);

            var art = new GameObject("Art").transform;
            art.SetParent(slot, false);
            var turn = new GameObject("Orient").transform;
            turn.SetParent(art, false);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, turn);
            var renderers = inst.GetComponentsInChildren<MeshRenderer>(true);

            // Chọn hướng (giữ nguyên / quay 90° quanh Y, X, Z) có tỉ lệ ba chiều gần khối xám nhất.
            Quaternion[] options = { Quaternion.identity, Quaternion.Euler(0f, 90f, 0f), Quaternion.Euler(90f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f) };
            // Vật chứa (nồi, xô): chỉ giữ nguyên hoặc quay quanh trục đứng.
            if (System.Array.IndexOf(AssetDatabase.GetLabels(prefab), Import.ArtImporter.UprightLabel) >= 0)
                options = new[] { Quaternion.identity, Quaternion.Euler(0f, 90f, 0f) };
            float best = float.MaxValue;
            Quaternion bestRot = Quaternion.identity;
            for (int i = 0; i < options.Length; i++)
            {
                turn.localRotation = options[i];
                if (!LocalBounds(slot, renderers, out var b))
                    return;
                // Quay khỏi tư thế gốc chỉ khi khớp hơn rõ rệt.
                float score = ShapeMismatch(target.size, b.size) + (i == 0 ? 0f : 0.15f);
                if (score < best)
                {
                    best = score;
                    bestRot = options[i];
                }
            }
            turn.localRotation = bestRot;
            LocalBounds(slot, renderers, out var src);

            bool stretch = System.Array.IndexOf(AssetDatabase.GetLabels(prefab), Import.ArtImporter.StretchLabel) >= 0;
            var ratio = new Vector3(target.size.x / Mathf.Max(src.size.x, 1e-4f), target.size.y / Mathf.Max(src.size.y, 1e-4f),
                target.size.z / Mathf.Max(src.size.z, 1e-4f));
            // Vật mỏng (lưỡi xẻng, cánh cửa): chiều mỏng của khối xám không giới hạn tỉ lệ co đều.
            var t = target.size;
            float thin = Mathf.Min(t.x, Mathf.Min(t.y, t.z));
            float mid = t.x + t.y + t.z - thin - Mathf.Max(t.x, Mathf.Max(t.y, t.z));
            float uniform = float.MaxValue;
            for (int a = 0; a < 3; a++)
                if (!(t[a] == thin && thin < 0.25f * mid))
                    uniform = Mathf.Min(uniform, ratio[a]);
            var scale = stretch ? ratio : Vector3.one * uniform;
            art.localScale = scale;
            // Canh giữa theo X/Z, canh đáy theo Y.
            art.localPosition = new Vector3(target.center.x - src.center.x * scale.x, target.min.y - src.min.y * scale.y,
                target.center.z - src.center.z * scale.z);

            foreach (var r in renderers)
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags);
            foreach (var r in parts)
            {
                if (System.Array.IndexOf(FunctionalParts, r.gameObject.name) >= 0)
                    continue;
                var filter = r.GetComponent<MeshFilter>();
                Object.DestroyImmediate(r);
                if (filter != null)
                    Object.DestroyImmediate(filter);
            }
        }

        static float ShapeMismatch(Vector3 a, Vector3 b)
        {
            float lx = Mathf.Log(Mathf.Max(a.x, 1e-3f) / Mathf.Max(b.x, 1e-3f));
            float ly = Mathf.Log(Mathf.Max(a.y, 1e-3f) / Mathf.Max(b.y, 1e-3f));
            float lz = Mathf.Log(Mathf.Max(a.z, 1e-3f) / Mathf.Max(b.z, 1e-3f));
            float mean = (lx + ly + lz) / 3f;
            return (lx - mean) * (lx - mean) + (ly - mean) * (ly - mean) + (lz - mean) * (lz - mean);
        }

        public static bool LocalBounds(Transform space, MeshRenderer[] renderers, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            var toLocal = space.worldToLocalMatrix;
            foreach (var r in renderers)
            {
                var filter = r.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                    continue;
                var b = Import.ArtImporter.TransformBounds(mesh.bounds, toLocal * r.transform.localToWorldMatrix);
                if (any) bounds.Encapsulate(b); else bounds = b;
                any = true;
            }
            return any;
        }

        /// <summary>Overload cũ cho cảnh giữ chỗ.</summary>
        public static GameObject ArtSlot(string slot, PrimitiveType fallback, Vector3 position, Vector3 scale, Transform parent = null)
        {
            return ArtSlot(slot, parent, position, Vector3.zero, t => Prim(fallback, "Mesh", t, Vector3.zero, scale, null));
        }

        /// <summary>Vùng trigger (layer Ignore Raycast để không chặn tia tương tác).</summary>
        public static T Trigger<T>(string name, Transform parent, Vector3 position, Vector3 size) where T : Component
        {
            var go = new GameObject(name) { layer = IgnoreRaycastLayer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            return go.AddComponent<T>();
        }

        public static Light PointLight(string name, Transform parent, Vector3 position, Color color, float intensity,
            float range, bool realtime, bool shadows = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            l.lightmapBakeType = realtime ? LightmapBakeType.Realtime : LightmapBakeType.Baked;
            return l;
        }

        /// <summary>Bỏ cờ static cho vật sẽ di chuyển lúc chạy (phông lùi, tàu điện).</summary>
        public static void ClearStatic(GameObject root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform)
                SetLayerRecursive(c.gameObject, layer);
        }

        /// <summary>Mask Rendering Layers, độc lập physics layer; chỉ đổi renderer được yêu cầu.</summary>
        public static void SetRenderingLayer(GameObject go, uint layer, bool recursive = true)
        {
            var renderers = recursive ? go.GetComponentsInChildren<Renderer>(true) : go.GetComponents<Renderer>();
            foreach (var renderer in renderers)
                renderer.renderingLayerMask = layer;
        }

        /// <summary>Ánh sáng và bóng cùng mask để mặt trời không rọi vào khoang.</summary>
        public static void SetLightLayer(Light light, uint layer)
        {
            var data = light.GetUniversalAdditionalLightData();
            data.customShadowLayers = false;
            data.renderingLayers = layer;
            light.renderingLayerMask = unchecked((int)layer);
        }

        /// <summary>Cảnh giữ chỗ: sàn + một khối.</summary>
        public static void BuildPlaceholder(string sceneName)
        {
            var scene = NewScene();
            AddCamera(new Vector3(0f, 1.7f, -6f), new Vector3(10f, 0f, 0f));
            var sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            ArtSlot("Floor", PrimitiveType.Plane, Vector3.zero, new Vector3(2f, 1f, 2f));
            ArtSlot("Marker", PrimitiveType.Cube, new Vector3(0f, 0.5f, 0f), Vector3.one);
            Save(scene, sceneName);
        }
    }
}
