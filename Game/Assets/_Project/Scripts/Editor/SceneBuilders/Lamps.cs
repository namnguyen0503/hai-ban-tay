using System.Collections.Generic;
using HaiBanTay.Ship;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Performance;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>
    /// Vật đèn (wave4-light): kính là URP Lit trong suốt (tint hổ phách, bóng loáng, phát sáng rất nhẹ), bên trong có lõi lửa
    /// (cầu nhỏ phát sáng mạnh) nhìn xuyên qua kính; khung kim loại của art giữ nguyên. Builder thay vật liệu kính/lửa của bản
    /// thể hiện trong cảnh (prefab/.mat nhóm art không bị đụng); đèn realtime (LightBudget bật/tắt) có LampGlowSync: đèn tắt thì
    /// lửa ẩn, kính thành trong suốt không phát sáng (thấy bên trong tối).
    /// </summary>
    public static class Lamps
    {
        /// <summary>Phát sáng tối thiểu (kênh màu phát lớn nhất) để một vật liệu được coi là kính/ngọn lửa đang sáng.</summary>
        public const float MinGlow = 1f;

        public static Material Glow() => AssetFactory.Emissive("LampGlow", new Color(1f, 0.8f, 0.45f), new Color(1.9f, 1.15f, 0.4f));
        public static Material ColdGlow() => AssetFactory.Emissive("LampGlowCold", new Color(0.8f, 0.9f, 1f), new Color(1.9f, 2.1f, 2.4f));
        public static Material Flame() => AssetFactory.Emissive("LampFlame", new Color(1f, 0.55f, 0.2f), new Color(1.9f, 0.7f, 0.12f));
        /// <summary>Bản tối đặc cho bóng đèn của model phông (đèn đường, đèn bến): không phải kính đèn.</summary>
        public static Material Off() => AssetFactory.Lit("LampGlassOff", new Color(0.16f, 0.13f, 0.1f), 0.6f);

        // Kính đèn: trong suốt, alpha 0,2 đến 0,4, albedo gần đen (Light nằm sát trong kính); sáng thì phát sáng hổ phách nhẹ (< 1) (đọc là kính hắt ánh lửa), tắt thì trong hẳn.
        public static Material GlassWarm() => AssetFactory.Glass("LampGlassWarm", new Color(0.012f, 0.007f, 0.002f, 0.38f), new Color(0.75f, 0.4f, 0.1f));
        public static Material GlassCold() => AssetFactory.Glass("LampGlassCold", new Color(0.006f, 0.01f, 0.014f, 0.34f), new Color(0.3f, 0.42f, 0.55f));
        public static Material GlassClear() => AssetFactory.Glass("LampGlassClear", new Color(0.04f, 0.03f, 0.015f, 0.22f), Color.black);
        /// <summary>Vật liệu "ẩn" của ngọn lửa khi đèn tắt (trong suốt hoàn toàn, không phát sáng).</summary>
        public static Material Hidden() => AssetFactory.Transparent("LampHidden", new Color(0f, 0f, 0f, 0f));

        public static float Peak(Material m)
        {
            if (m == null || !m.IsKeywordEnabled("_EMISSION") || !m.HasProperty("_EmissionColor"))
                return 0f;
            var c = m.GetColor("_EmissionColor");
            return Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        }

        /// <summary>Hộp bao thế giới của riêng một vật liệu (submesh) của renderer; mesh không đọc được thì lấy cả renderer.</summary>
        public static Bounds SlotBounds(Renderer r, int slot)
        {
            var filter = r.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
                return r.bounds;
            Bounds local;
            if (mesh.subMeshCount == 1)
                local = mesh.bounds;
            else if (mesh.isReadable && slot < mesh.subMeshCount)
                local = mesh.GetSubMesh(slot).bounds;
            else
                return r.bounds;
            var t = r.transform;
            var c = local.center;
            var x = local.extents;
            var b = new Bounds(t.TransformPoint(c), Vector3.zero);
            for (int i = 0; i < 8; i++)
                b.Encapsulate(t.TransformPoint(c + new Vector3((i & 1) == 0 ? -x.x : x.x, (i & 2) == 0 ? -x.y : x.y, (i & 4) == 0 ? -x.z : x.z)));
            return b;
        }

        static bool IsFlame(Material m) => m.name.ToLowerInvariant().Contains("flame");

        static bool IsGlass(Renderer r, Material m) => m.name.ToLowerInvariant().Contains("glass") || r.name == "Glass";

        /// <summary>
        /// Biến kính/ngọn lửa của mọi renderer dưới root thành vật đèn thật. Kính (vật liệu tên có "glass", hoặc khối giữ chỗ tên
        /// Glass) thành kính trong suốt hổ phách (glow lạnh thì xanh lạnh); ngọn lửa của art thành LampFlame; nếu lamp chưa có
        /// ngọn lửa thì thêm lõi lửa (cầu nhỏ phát sáng mạnh) vào giữa phần kính. Vật liệu đã phát sáng khác giữ nguyên.
        /// realtime: thêm LampGlowSync (đèn tắt thì lửa ẩn, kính trong hẳn). Trả về số phần phát sáng.
        /// </summary>
        public static int Bind(Transform root, Light light, Material glow, bool realtime)
        {
            glow = glow != null ? glow : Glow();
            bool cold = glow.name.Contains("Cold");
            var glass = cold ? GlassCold() : GlassWarm();
            var clear = GlassClear();
            var flame = cold ? ColdGlow() : Flame();
            var hidden = Hidden();
            var parts = new List<LampGlowSync.Part>();
            var off = Off();
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            bool hasFlame = false;
            foreach (var r in renderers)
                foreach (var m in r.sharedMaterials)
                    hasFlame |= m != null && IsFlame(m);
            bool haveGlassBounds = false;
            Bounds glassBounds = default;
            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null)
                        continue;
                    Material lit, unlit;
                    if (IsFlame(mats[i]))
                    {
                        lit = flame; unlit = hidden;
                    }
                    else if (IsGlass(r, mats[i]))
                    {
                        lit = glass; unlit = clear;
                        if (!haveGlassBounds)
                        {
                            glassBounds = SlotBounds(r, i);
                            haveGlassBounds = true;
                        }
                    }
                    else if (Peak(mats[i]) >= MinGlow)
                    {
                        lit = mats[i]; unlit = off;
                    }
                    else
                        continue;
                    if (mats[i] != lit)
                    {
                        mats[i] = lit;
                        changed = true;
                    }
                    parts.Add(new LampGlowSync.Part { renderer = r, slot = i, lit = lit, unlit = unlit });
                }
                if (changed)
                    r.sharedMaterials = mats;
            }
            if (!hasFlame && haveGlassBounds)
                parts.Add(AddFlameCore(root, glassBounds, flame, hidden));
            if (realtime && parts.Count > 0)
                Sync(root.gameObject, light, parts);
            return parts.Count;
        }

        /// <summary>Lõi lửa: cầu nhỏ phát sáng mạnh, hơi thon, đặt thấp trong phần kính (chỗ bấc) để nhìn xuyên qua kính.</summary>
        static LampGlowSync.Part AddFlameCore(Transform root, Bounds glass, Material flame, Material hidden)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "FlameCore";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root, false);
            var c = glass.center;
            go.transform.position = new Vector3(c.x, c.y - 0.1f * glass.size.y, c.z);
            float d = Mathf.Clamp(0.36f * Mathf.Min(glass.size.x, glass.size.z), 0.02f, 0.055f);
            go.transform.localScale = new Vector3(d, d * 1.6f, d);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = flame;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            return new LampGlowSync.Part { renderer = r, slot = 0, lit = flame, unlit = hidden, toggleRenderer = true };
        }

        static void Sync(GameObject host, Light light, List<LampGlowSync.Part> parts)
        {
            var sync = host.AddComponent<LampGlowSync>();
            sync.lamp = light;
            sync.parts = parts.ToArray();
        }

        /// <summary>
        /// Gắn Light (đã đặt sẵn) vào các mảng phát sáng của model phông (đèn đường, đèn bến) trong bán kính 0,6 m: đèn realtime
        /// có LampGlowSync để kính tối khi đèn bị LightBudget tắt. Trả về số mảng tìm thấy.
        /// </summary>
        public static int BindNear(Transform envRoot, Light light, bool realtime, float radius = 0.6f)
        {
            var parts = new List<LampGlowSync.Part>();
            var off = Off();
            foreach (var r in envRoot.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (Peak(mats[i]) >= MinGlow && SlotBounds(r, i).SqrDistance(light.transform.position) <= radius * radius)
                        parts.Add(new LampGlowSync.Part { renderer = r, slot = i, lit = mats[i], unlit = off });
            }
            if (realtime && parts.Count > 0)
                Sync(light.gameObject, light, parts);
            return parts.Count;
        }

        /// <summary>
        /// Đèn bến/đèn đường có sẵn trong model phông (node tên bắt đầu bằng namePrefix, vật liệu có mảng phát sáng): đặt một đèn
        /// điểm không bóng dưới bóng đèn của từng cột. Đèn realtime (vào LightBudget, chọn theo khoảng cách tới camera), thuộc lớp
        /// Exterior như phông.
        /// </summary>
        public static List<Light> LightEnvLamps(Transform envRoot, string namePrefix, Color color, float intensity, float range)
        {
            var result = new List<Light>();
            foreach (var t in envRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith(namePrefix) || t.name.StartsWith("COL_"))
                    continue;
                var r = t.GetComponent<Renderer>();
                if (r == null)
                    continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (Peak(mats[i]) < MinGlow)
                        continue;
                    var bulb = SlotBounds(r, i).center;
                    var l = BuilderUtils.PointLight("LampLight", t, Vector3.zero, color, intensity, range, true);
                    l.transform.position = bulb + Vector3.down * 0.1f;
                    BuilderUtils.SetLightLayer(l, WorldRenderingLayers.Exterior);
                    BindNear(envRoot, l, true);
                    result.Add(l);
                    break;
                }
            }
            return result;
        }
    }
}
