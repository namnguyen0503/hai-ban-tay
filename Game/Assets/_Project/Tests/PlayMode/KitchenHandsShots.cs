using System.Collections;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Ảnh duyệt tay NPC trong bếp (W5.3, phản hồi 14/10): quản bếp nhấc nắp, khuấy muôi trong lòng nồi (chuỗi khung), đậy nắp; phụ
    /// bếp xúc than, bê xẻng, nạp than vào cửa lò, bê nồi bẩn bằng hai quai. Camera phụ đặt bên cạnh NPC, nhìn vào tay.
    /// </summary>
    [Category("Screenshots"), Explicit]
    public sealed class KitchenHandsShots
    {
        static PlayerRig Rig => PlayerRig.Current;
        [SetUp] public void SetUp() => Time.captureDeltaTime = 1f / 60f;
        [TearDown] public void TearDown()
        {
            Time.captureDeltaTime = 0f; ScreenshotCapture.RestoreUi(); PlayTestUtil.ExitSim();
        }

        static IEnumerator Side(string name, Transform npc, float side = 0.85f, float height = 1.8f, float ahead = -0.55f, float fov = 55f)
        {
            var go = new GameObject("KitchenShot");
            var camera = go.AddComponent<Camera>();
            camera.CopyFrom(Rig.cam); camera.enabled = false; camera.fieldOfView = fov; camera.nearClipPlane = 0.02f;
            var source = Rig.cam.GetUniversalAdditionalCameraData(); var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = source.renderPostProcessing; data.volumeLayerMask = source.volumeLayerMask; data.volumeTrigger = Rig.cam.transform;
            go.transform.position = Eye(npc, side, height, ahead);
            go.transform.LookAt(Target(npc));
            yield return ScreenshotCapture.CaptureCamera(camera, "kitchen14_" + name);
            Object.Destroy(go);
        }

        /// <summary>Điểm máy nhìn vào: hai tay và vật đang cầm trước ngực NPC.</summary>
        internal static Vector3 Target(Transform npc) => npc.position + Vector3.up * 0.95f + npc.forward * 0.55f;

        /// <summary>
        /// Chỗ đặt máy (ảnh duyệt và clip KitchenMotionClips): sau lưng chéo qua vai (phía trước NPC thường là lò, tường). Chỗ đặt bị
        /// nồi, nắp, tường chắn giữa máy và tay thì thử các chỗ khác quanh NPC.
        /// </summary>
        internal static Vector3 Eye(Transform npc, float side, float height, float ahead)
        {
            var target = Target(npc);
            var tries = new[]
            {
                new Vector3(side, height, ahead), new Vector3(-side, height, ahead), new Vector3(side * 1.3f, height + 0.25f, 0.1f),
                new Vector3(-side * 1.3f, height + 0.25f, 0.1f), new Vector3(side * 0.4f, height + 0.4f, -1.1f), new Vector3(-side * 0.4f, height + 0.4f, -1.1f),
                new Vector3(side * 1.4f, height - 0.2f, 0.7f), new Vector3(-side * 1.4f, height - 0.2f, 0.7f),
            };
            foreach (var t in tries)
            {
                var p = npc.position + npc.right * t.x + Vector3.up * t.y + npc.forward * t.z;
                if (Clear(p, target, npc)) return p;
            }
            return npc.position + npc.right * side + Vector3.up * height + npc.forward * ahead;
        }

        /// <summary>Không collider nào (trừ thân NPC) bao máy hay cắt đường nhìn; không hình nhỏ nào (nồi, nắp không collider) chắn trước 30 cm cuối.</summary>
        static bool Clear(Vector3 eye, Vector3 target, Transform npc)
        {
            foreach (var c in Physics.OverlapSphere(eye, 0.15f, ~0, QueryTriggerInteraction.Ignore))
                if (!c.transform.IsChildOf(npc)) return false;
            var dir = target - eye;
            float length = dir.magnitude;
            var ray = new Ray(eye, dir / length);
            foreach (var hit in Physics.RaycastAll(ray, length - 0.3f, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(npc)) return false;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r.transform.IsChildOf(npc)) continue;
                var b = r.bounds;
                if (b.size.magnitude > 2f || b.Contains(target)) continue;
                if (b.IntersectRay(ray, out float d) && d < length - 0.3f) return false;
            }
            return true;
        }

        static IEnumerator Wait(NpcHandWork hands, string activity, float seconds = 60f)
        {
            yield return PlayTestUtil.WaitUntil(() => hands.Activity == activity, seconds);
            Assert.AreEqual(activity, hands.Activity);
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator CookAndHelperHandsOnRealThings()
        {
            yield return ChoreTestUtil.Galley();
            GameRoot.Instance.UI.SetFade(0f);
            ChoreTestUtil.Look(new Vector3(2.5f, 0f, -1.5f), new Vector3(0f, 1f, 0f));
            var meals = Object.FindAnyObjectByType<MealPots>();
            var cook = Object.FindAnyObjectByType<CookRoutine>();
            var cookHands = cook.GetComponent<NpcHandWork>();
            var helper = Object.FindAnyObjectByType<HelperRoutine>();
            var helperHands = helper.GetComponent<NpcHandWork>();
            helper.enabled = false;
            meals.BeginMeal(new PhaseData { Id = "anh_khuay", Dirty = 2 });

            yield return Wait(cookHands, "lid");
            yield return new WaitForSeconds(0.9f);
            yield return Side("01_cook_lifts_lid", cook.transform);
            yield return Wait(cookHands, "stir");
            yield return new WaitForSeconds(1.2f);
            yield return Side("02_cook_stirs_side", cook.transform);
            yield return Side("03_cook_stirs_left", cook.transform, -0.85f);
            yield return Side("04_stir_close_above", cook.transform, 0.45f, 1.95f, 0.35f, 45f);
            for (int k = 0; k < 6; k++)
            {
                yield return new WaitForSeconds(0.12f);
                yield return Side("05_stir_seq_" + k, cook.transform, 0.45f, 1.95f, 0.35f, 45f);
            }
            yield return PlayTestUtil.WaitUntil(() => cookHands.Activity == "lid", 30f);
            yield return new WaitForSeconds(0.5f);
            yield return Side("06_cook_puts_lid_back", cook.transform);

            helper.enabled = true;
            yield return Wait(helperHands, "scoop");
            yield return new WaitForSeconds(1.1f);
            yield return Side("07_helper_scoops", helper.transform);
            yield return Side("07b_helper_scoops_left", helper.transform, -0.85f);
            yield return Wait(helperHands, "carry");
            yield return new WaitForSeconds(1.5f);
            yield return Side("08_helper_carries_shovel", helper.transform);
            yield return Side("08b_shovel_front_left", helper.transform, -1.0f, 1.6f, 0.9f);
            yield return Wait(helperHands, "feed");
            yield return new WaitForSeconds(1.1f);
            yield return Side("09_helper_feeds_fire_door", helper.transform);
            yield return Side("09b_helper_feeds_left", helper.transform, -0.85f);

            // Thùng khoai đầy một bao: quản bếp gác muôi, bê thùng, đổ khoai vào nồi đang nấu.
            var sacks = Object.FindAnyObjectByType<SackStation>();
            for (int i = 0; i < 4 && sacks.InTub < sacks.potatoesPerSack; i++) sacks.SimulateWork();
            yield return Wait(cookHands, "carry", 90f);
            yield return new WaitForSeconds(1.2f);
            yield return Side("11_cook_carries_tub", cook.transform);
            yield return Wait(cookHands, "pour", 60f);
            yield return new WaitForSeconds(0.9f);
            yield return Side("12_cook_pours_potatoes", cook.transform);
            yield return Side("12b_pour_left", cook.transform, -0.85f);
            yield return PlayTestUtil.WaitUntil(() => cookHands.Activity == "stir", 90f);

            meals.StartCoroutine(meals.Serve());
            yield return PlayTestUtil.WaitUntil(() => meals.helper.agent.Carrying != null, 60f);
            yield return new WaitForSeconds(0.8f);
            yield return Side("10_helper_carries_pot", meals.helper.transform);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator JacquesHandsOnRealPots()
        {
            yield return ChoreTestUtil.Galley();
            GameRoot.Instance.UI.SetFade(0f);
            ChoreTestUtil.Look(new Vector3(2.5f, 0f, 0.5f), new Vector3(0f, 1f, 0f));
            var cast = Object.FindAnyObjectByType<DayCast>();
            var hands = cast.JacquesHands;
            foreach (var p in cast.stormPots) p.Secure(false);
            cast.jacques.gameObject.SetActive(true);
            cast.jacques.Place(cast.jacquesPots.position, cast.jacquesPots.eulerAngles.y);
            cast.StartCoroutine(cast.HoldStormPots());
            yield return PlayTestUtil.WaitUntil(() => hands.Activity == "hold" && hands.arms.leftWeight > 0.99f, 30f);
            yield return new WaitForSeconds(0.5f);
            yield return Side("13_jacques_holds_storm_pot", cast.jacques.transform);
            yield return Side("13b_jacques_storm_left", cast.jacques.transform, -0.85f);
            foreach (var p in cast.stormPots) p.Secure(true);
            yield return PlayTestUtil.WaitUntil(() => cast.HeldPot == null && hands.Activity == "", 30f);

            var tall = Object.FindAnyObjectByType<TallPotStation>().potRoot;
            tall.gameObject.SetActive(true);
            cast.StartCoroutine(cast.HoldTallPot());
            yield return PlayTestUtil.WaitUntil(() => hands.Activity == "hold" && hands.arms.leftWeight > 0.99f, 60f);
            yield return new WaitForSeconds(0.5f);
            yield return Side("14_jacques_holds_tall_pot_rim", cast.jacques.transform);
            yield return Side("14b_tall_pot_left", cast.jacques.transform, -0.85f);
        }
    }
}
