using System.Collections;
using System.IO;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Clip chuyển động của NPC bếp (phản hồi 14/10: "sao không tăng frame lên"): mỗi động tác quay liên tục 15 khung/giây (đoạn dài
    /// 10 khung/giây) vào Logs/shots/clips/&lt;tên&gt;/f_###.jpg, giờ game cố định 1/60 s mỗi khung (captureDeltaTime) nên nhịp đúng
    /// như chơi. Tools/clips.py ghép thành MP4 và tấm lưới khung để duyệt chuyển động (giật, tay bay, nhịp khuấy).
    /// </summary>
    [Category("Screenshots"), Explicit]
    public sealed class KitchenMotionClips
    {
        const int Width = 1280, Height = 720;
        static PlayerRig Rig => PlayerRig.Current;

        [SetUp] public void SetUp() => Time.captureDeltaTime = 1f / 60f;
        [TearDown] public void TearDown()
        {
            Time.captureDeltaTime = 0f; ScreenshotCapture.RestoreUi(); PlayTestUtil.ExitSim();
        }

        /// <summary>
        /// Quay npc tới khi done() đúng hoặc hết maxSeconds. Máy đặt như ảnh duyệt (KitchenHandsShots.Eye: sau vai, tránh vật che);
        /// follow: máy dời theo NPC (giữ khoảng lệch lúc đầu, không xoay theo), không thì đứng yên và lia theo ngực NPC.
        /// </summary>
        static IEnumerator Clip(string name, Transform npc, System.Func<bool> done, float maxSeconds, bool follow = false, int every = 4,
            float side = 0.85f, float height = 1.8f, float ahead = -0.55f, float fov = 55f)
        {
            var folder = Path.Combine(ScreenshotCapture.Folder, "clips", name);
            if (Directory.Exists(folder))
                foreach (var old in Directory.GetFiles(folder)) File.Delete(old);
            Directory.CreateDirectory(folder);
            var go = new GameObject("KitchenClip");
            var camera = go.AddComponent<Camera>();
            camera.CopyFrom(Rig.cam); camera.enabled = false; camera.fieldOfView = fov;
            // Đèn treo, mép khung cửa sát máy (trong 0,6 m) không vẽ: clip 22:1x bị chụp đèn che nửa khung.
            camera.nearClipPlane = follow ? 0.6f : 0.02f;
            var source = Rig.cam.GetUniversalAdditionalCameraData(); var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = source.renderPostProcessing; data.volumeLayerMask = source.volumeLayerMask; data.volumeTrigger = Rig.cam.transform;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            var eye = follow ? FollowEye(npc) : KitchenHandsShots.Eye(npc, side, height, ahead);
            var look = KitchenHandsShots.Target(npc);
            int frame = 0, shot = 0;
            for (float t = 0f; t < maxSeconds && !done(); t += Time.deltaTime, frame++)
            {
                if (follow)
                {
                    // Máy trượt theo, không giật theo từng bước của NPC.
                    eye = Vector3.Lerp(eye, FollowEye(npc), 1f - Mathf.Exp(-3f * Time.deltaTime));
                    look = Vector3.Lerp(look, KitchenHandsShots.Target(npc), 1f - Mathf.Exp(-6f * Time.deltaTime));
                }
                else look = KitchenHandsShots.Target(npc);
                if (frame % every == 0)
                {
                    go.transform.position = follow ? Unblocked(npc, look, eye) : eye;
                    go.transform.LookAt(look);
                    RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = rt });
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    texture.Apply();
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(folder, $"f_{shot++:000}.jpg"), texture.EncodeToJPG(85));
                }
                yield return null;
            }
            File.WriteAllText(Path.Combine(folder, "fps.txt"), (60 / every).ToString());
            Debug.Log($"Clip: {name} {shot} khung");
            Object.Destroy(texture);
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(go);
        }

        /// <summary>
        /// Máy theo NPC: vách, cửa chắn giữa NPC và máy thì kéo máy vào trước vách (clip 22:1x: cả khung là mặt vách). Chỉ tính vật tĩnh:
        /// thân NPC, đồ đang bê, NPC khác (thân động học) không kéo máy vào (máy chui vào trong người).
        /// </summary>
        static Vector3 Unblocked(Transform npc, Vector3 look, Vector3 eye)
        {
            var dir = eye - look;
            float dist = dir.magnitude;
            float near = dist;
            foreach (var hit in Physics.SphereCastAll(look, 0.12f, dir / dist, dist, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.attachedRigidbody == null && !hit.transform.IsChildOf(npc) && hit.distance > 0f)
                    near = Mathf.Min(near, hit.distance);
            return near < dist ? look + dir / dist * Mathf.Max(1.2f, near - 0.1f) : eye;
        }

        /// <summary>
        /// Máy theo NPC đang đi: trên cao 2,3 m, lùi 1,6 m về phía giữa lối bếp (z = 0) và chếch về phía đông, nhìn xuống người: hướng
        /// cố định trong thế giới nên không xoay vòng theo NPC, không chui vào sau lưng sát vai.
        /// </summary>
        static Vector3 FollowEye(Transform npc)
        {
            var p = npc.position;
            var toAisle = new Vector3(0.6f, 0f, Mathf.Abs(p.z) > 0.4f ? -Mathf.Sign(p.z) : 1f).normalized;
            // Hướng đầu bị vách che (NPC đứng sát lò, sát vách) thì xoay quanh NPC tìm hướng thoáng gần nhất: clip 22:3x đứng ở lò thì
            // máy kéo sát đỉnh đầu, chỉ thấy tạp dề.
            var look = KitchenHandsShots.Target(npc);
            Vector3 best = p + Vector3.up * 2.3f + toAisle * 1.6f;
            float bestFree = -1f;
            foreach (float angle in new[] { 0f, 40f, -40f, 80f, -80f, 120f, -120f, 160f, -160f })
            {
                var eye = p + Vector3.up * 2.3f + Quaternion.Euler(0f, angle, 0f) * toAisle * 1.6f;
                float full = Vector3.Distance(look, eye);
                float free = Vector3.Distance(look, Unblocked(npc, look, eye));
                if (free >= full - 0.01f) return eye;
                if (free > bestFree)
                {
                    bestFree = free;
                    best = eye;
                }
            }
            return best;
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator CookAndHelperClips()
        {
            yield return ChoreTestUtil.Galley();
            GameRoot.Instance.UI.SetFade(0f);
            ChoreTestUtil.Look(new Vector3(2.5f, 0f, -1.5f), new Vector3(0f, 1f, 0f));
            var meals = Object.FindAnyObjectByType<MealPots>();
            var cook = Object.FindAnyObjectByType<CookRoutine>();
            var cookHands = cook.GetComponent<NpcHandWork>();
            var helper = Object.FindAnyObjectByType<HelperRoutine>();
            var helperHands = helper.GetComponent<NpcHandWork>();
            // Hai người bếp gần nhau tới đâu suốt các clip (đối chiếu với khung hình khi duyệt).
            var overlap = meals.gameObject.AddComponent<NpcOverlapProbe>().Watch(cook.agent, helper.agent);
            helper.enabled = false;
            meals.BeginMeal(new PhaseData { Id = "clip", Dirty = 2 });

            // Nhấc nắp → khuấy → gõ miệng nồi → đậy nắp.
            yield return PlayTestUtil.WaitUntil(() => cookHands.Activity == "lid", 60f);
            bool stirred = false;
            yield return Clip("cook_lid_stir_lid", cook.transform, () => (stirred |= cookHands.Activity == "stir") && cookHands.Activity == "", 16f);
            // Khuấy nhìn gần từ trên.
            yield return PlayTestUtil.WaitUntil(() => cookHands.Activity == "stir" && cookHands.Step == "circle", 60f);
            yield return Clip("cook_stir_close", cook.transform, () => cookHands.Step != "circle", 4f, every: 2, side: 0.45f, height: 1.95f, ahead: 0.35f, fov: 45f);

            // Phụ bếp: xúc than → bê → nạp qua cửa lò.
            helper.enabled = true;
            yield return PlayTestUtil.WaitUntil(() => helperHands.Activity == "scoop", 60f);
            int feeds = helper.Feeds;
            yield return Clip("helper_scoop_feed", helper.transform, () => helper.Feeds > feeds, 25f, follow: true);

            // Thùng khoai đầy: gác muôi → bê thùng → đổ vào nồi → trả thùng → cầm muôi lại.
            var sacks = Object.FindAnyObjectByType<SackStation>();
            for (int i = 0; i < 4 && sacks.InTub < sacks.potatoesPerSack; i++) sacks.SimulateWork();
            yield return PlayTestUtil.WaitUntil(() => sacks.TubAway, 90f);
            int tubs = cook.TubsCarried;
            Debug.Log($"KITCHEN_CLIPS cook_tub_pour bắt đầu t={Time.time:F1}");
            meals.StartCoroutine(LogGap(cook.agent, helper.agent, 8f));
            yield return Clip("cook_tub_pour", cook.transform, () => cook.TubsCarried > tubs && cookHands.InHand(cook.ladle, false), 45f, follow: true, every: 6);

            // Dọn bữa: phụ bếp bê nồi bằng hai quai về chồng nồi bẩn.
            bool served = false;
            meals.StartCoroutine(Track(meals.Serve(), () => served = true));
            yield return PlayTestUtil.WaitUntil(() => meals.helper.agent.Carrying != null || served, 60f);
            yield return Clip("helper_carries_pot", meals.helper.transform, () => served, 40f, follow: true, every: 6);
            Debug.Log("KITCHEN_CLIPS overlap: " + overlap.Report);
        }

        /// <summary>Khoảng cách tâm hai NPC mỗi 0,2 s trong chừng này giây (đối chiếu khung clip).</summary>
        static IEnumerator LogGap(NpcAgent a, NpcAgent b, float seconds)
        {
            var sb = new System.Text.StringBuilder("KITCHEN_CLIPS gap:");
            for (float t = 0f; t < seconds; t += 0.2f)
            {
                var d = a.Position - b.Position;
                d.y = 0f;
                sb.Append($" {t:F1}s={d.magnitude:F2}{(b.Waiting ? "w" : "")}");
                yield return new WaitForSeconds(0.2f);
            }
            Debug.Log(sb.ToString());
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator JacquesClips()
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
            // Vươn tới quai nồi, giữ; Ba chằng xong thì buông, sang nồi kế tiếp.
            float start = Time.time;
            yield return Clip("jacques_storm_pots", cast.jacques.transform, () =>
            {
                if (Time.time - start > 4f && cast.HeldPot != null && !cast.HeldPot.Secured) cast.HeldPot.Secure(true);
                return Time.time - start > 12f;
            }, 14f, follow: true);
            foreach (var p in cast.stormPots) p.Secure(true);
            yield return PlayTestUtil.WaitUntil(() => cast.HeldPot == null && hands.Activity == "", 30f);

            var tall = Object.FindAnyObjectByType<TallPotStation>().potRoot;
            tall.gameObject.SetActive(true);
            cast.StartCoroutine(cast.HoldTallPot());
            yield return Clip("jacques_tall_pot", cast.jacques.transform, () => false, 8f, follow: true);
        }

        static IEnumerator Track(IEnumerator inner, System.Action finished)
        {
            yield return inner;
            finished();
        }
    }
}
