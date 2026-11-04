using HaiBanTay.Editor.Setup;
using HaiBanTay.Interaction;
using HaiBanTay.Ship;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>Vật thể dùng lại ở nhiều cảnh. Mỗi vật là một ArtSlot (prefab art thay được khối xám).</summary>
    public static class Props
    {
        public static Holdable PotatoSack(Transform parent, Vector3 position, Bounds area)
        {
            var burlap = AssetFactory.Lit("Burlap", new Color(0.55f, 0.45f, 0.3f), 0.05f);
            var go = BuilderUtils.ArtSlot("PotatoSack", parent, position + Vector3.up * 0.3f, Vector3.zero, t =>
            {
                BuilderUtils.Prim(PrimitiveType.Capsule, "Body", t, Vector3.zero, new Vector3(0.45f, 0.3f, 0.35f), burlap, false, false);
                return t.gameObject;
            });
            go.layer = BuilderUtils.Layer("Holdable");
            go.tag = "Holdable";
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(0.45f, 0.6f, 0.35f);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 25f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var h = go.AddComponent<Holdable>();
            h.twoHanded = true;
            h.kind = "sack";
            var back = go.AddComponent<ReturnToStart>();
            back.useArea = true;
            back.area = area;
            return h;
        }

        public static Holdable SmallCrate(Transform parent, Vector3 position)
        {
            var wood = AssetFactory.Lit("Wood", new Color(0.4f, 0.27f, 0.16f), 0.15f);
            var go = BuilderUtils.ArtSlot("SmallCrate", parent, position + Vector3.up * 0.15f, Vector3.zero, t =>
            {
                BuilderUtils.Block("Body", t, Vector3.zero, new Vector3(0.3f, 0.3f, 0.3f), wood, false, false);
                return t.gameObject;
            });
            go.layer = BuilderUtils.Layer("Holdable");
            go.tag = "Holdable";
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(0.3f, 0.3f, 0.3f);
            go.AddComponent<Rigidbody>().mass = 3f;
            var h = go.AddComponent<Holdable>();
            h.kind = "crate";
            go.AddComponent<ReturnToStart>();
            return h;
        }

        /// <summary>
        /// Đèn bão treo trần: khung con lắc + kính phát sáng + đèn điểm không bóng đặt đúng chỗ kính. Đèn tĩnh dùng bake
        /// (realtime: false, không tốn suất realtime); đèn realtime có LampGlowSync nên LightBudget tắt đèn thì kính tối theo.
        /// glow: vật liệu kính (mặc định ấm); lightColor: màu ánh sáng.
        /// </summary>
        public static SwingingLamp HangingLamp(Transform parent, Vector3 ceiling, bool realtime, float intensity = 3f,
            float range = 7f, Material glow = null, Color? lightColor = null)
        {
            glow = glow != null ? glow : Lamps.Glow();
            var iron = AssetFactory.Lit("Iron", new Color(0.22f, 0.22f, 0.23f), 0.4f, 0.6f);
            var pivot = new GameObject("SwingingLamp");
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = ceiling;
            var slot = BuilderUtils.ArtSlot("StormLantern", pivot.transform, Vector3.zero, Vector3.zero, t =>
            {
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Rope", t, new Vector3(0f, -0.25f, 0f), new Vector3(0.015f, 0.25f, 0.015f), iron, false, false);
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Frame", t, new Vector3(0f, -0.62f, 0f), new Vector3(0.16f, 0.13f, 0.16f), iron, false, false);
                BuilderUtils.Prim(PrimitiveType.Sphere, "Glass", t, new Vector3(0f, -0.62f, 0f), new Vector3(0.12f, 0.15f, 0.12f), glow, false, false);
                return t.gameObject;
            });
            var light = BuilderUtils.PointLight("LampLight", pivot.transform, new Vector3(0f, -0.65f, 0f),
                lightColor ?? new Color(1f, 0.78f, 0.5f), intensity, range, realtime);
            Lamps.Bind(slot.transform, light, glow, realtime);
            return pivot.AddComponent<SwingingLamp>();
        }

        /// <summary>
        /// Đèn gas gắn tường: ArtSlot GasWallLantern (art là khối xám một vật liệu nên builder thêm lõi kính phát sáng bao
        /// phần thân đèn) + đèn điểm không bóng ở giữa thân. position là điểm áp tường (mặt trong vách), +Z cục bộ đi vào phòng.
        /// Trả về nhóm bọc (tên name) chứa ArtSlot và đèn.
        /// </summary>
        public static GameObject WallLantern(Transform parent, string name, Vector3 position, float yaw, bool realtime,
            float intensity = 2f, float range = 6f, Material glow = null)
        {
            glow = glow != null ? glow : Lamps.Glow();
            var iron = AssetFactory.Lit("Iron", new Color(0.22f, 0.22f, 0.23f), 0.4f, 0.6f);
            var wrap = BuilderUtils.Group(name, parent);
            wrap.transform.localPosition = position;
            wrap.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var slot = BuilderUtils.ArtSlot("GasWallLantern", wrap.transform, Vector3.zero, Vector3.zero, t =>
            {
                BuilderUtils.Block("Bracket", t, new Vector3(0f, 0f, 0.08f), new Vector3(0.04f, 0.04f, 0.16f), iron, false, false);
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Glass", t, new Vector3(0f, -0.08f, 0.16f), new Vector3(0.1f, 0.1f, 0.1f), glow, false, false);
                return t.gameObject;
            });
            var center = new Vector3(0f, -0.08f, 0.16f);
            var art = slot.transform.Find("Art");
            if (art != null && BuilderUtils.LocalBounds(slot.transform, art.GetComponentsInChildren<MeshRenderer>(true), out var b))
            {
                // Art gaslamp là một khối xám: tấm áp tường ở z nhỏ, cánh tay, rồi thân đèn (đế, hộp kính, mái) treo ở đầu cánh tay.
                // Lõi kính bao hộp thân đèn, hơi lớn hơn một chút để thấy cả khi art che.
                center = new Vector3(b.center.x, b.min.y + 0.30f * b.size.y, b.min.z + 0.725f * b.size.z);
                var core = BuilderUtils.Block("Glass", slot.transform, center,
                    new Vector3(0.80f * b.size.x, 0.38f * b.size.y, 0.40f * b.size.z), glow, false, false);
                core.transform.localPosition = center;
            }
            var light = BuilderUtils.PointLight("LanternLight", wrap.transform, center, new Color(1f, 0.78f, 0.5f), intensity, range, realtime);
            Lamps.Bind(slot.transform, light, glow, realtime);
            return wrap;
        }

        /// <summary>
        /// Đèn dầu để bàn (ArtSlot OilLamp): kính chụp và ngọn lửa phát sáng, đèn điểm ở ngọn lửa. Mặc định realtime
        /// (đèn buổi tối vào LightBudget).
        /// </summary>
        public static GameObject OilLamp(Transform parent, Vector3 position, bool realtime, float intensity, float range, out Light light)
        {
            var glow = Lamps.Glow();
            var brass = AssetFactory.Lit("Brass", new Color(0.7f, 0.58f, 0.3f), 0.55f, 0.8f);
            Light made = null;
            var lamp = BuilderUtils.ArtSlot("OilLamp", parent, position, Vector3.zero, t =>
            {
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Base", t, new Vector3(0f, 0.05f, 0f), new Vector3(0.1f, 0.05f, 0.1f), brass, false, false);
                BuilderUtils.Prim(PrimitiveType.Sphere, "Glass", t, new Vector3(0f, 0.17f, 0f), new Vector3(0.09f, 0.12f, 0.09f), glow, false, false);
                made = BuilderUtils.PointLight("OilLampLight", t, new Vector3(0f, 0.2f, 0f), new Color(1f, 0.7f, 0.4f), intensity, range, realtime);
                return t.gameObject;
            });
            Lamps.Bind(lamp.transform, made, glow, realtime);
            light = made;
            return lamp;
        }

        /// <summary>Đồ bày trí (thùng, kiện, ghế băng, mỏ neo): khối xám đúng kích thước thật, có prefab art thì thay.
        /// position là chân vật.</summary>
        public static GameObject Dress(string slot, Transform parent, Vector3 position, float yaw, Vector3 size, Material mat,
            bool cylinder = false)
        {
            return BuilderUtils.ArtSlot(slot, parent, position, new Vector3(0f, yaw, 0f), t =>
            {
                if (cylinder)
                    BuilderUtils.Prim(PrimitiveType.Cylinder, "Body", t, new Vector3(0f, size.y * 0.5f, 0f),
                        new Vector3(size.x, size.y * 0.5f, size.z), mat);
                else
                    BuilderUtils.Block("Body", t, new Vector3(0f, size.y * 0.5f, 0f), size, mat);
                return t.gameObject;
            });
        }
    }
}
