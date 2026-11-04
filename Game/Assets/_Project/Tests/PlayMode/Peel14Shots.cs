using System.Collections;
using System.Linq;
using HaiBanTay.Core;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>Ảnh duyệt gọt khoai 14/10 (ánh sáng gameplay): ông Mai gọt mẫu, góc nhìn thứ nhất giữa nhát dao, cận cảnh
    /// củ đang gọt, xô/chậu/nồi giữa chừng và lúc hết xô.</summary>
    [Category("Screenshots"), Explicit]
    public sealed class Peel14Shots
    {
        static GameRoot Root => GameRoot.Instance;
        static PlayerRig Rig => PlayerRig.Current;
        [SetUp] public void SetUp() => Time.captureDeltaTime = 1f / 60f;
        [TearDown] public void TearDown()
        {
            if (Root != null) { Root.Input.SimUse = false; Root.Input.SimLook = Vector2.zero; Root.Subtitles.StopAll(); }
            Time.captureDeltaTime = 0f; ScreenshotCapture.RestoreUi(); PlayTestUtil.ExitSim();
        }

        static IEnumerator Capture(string name)
        {
            Root.UI.SetFade(0f);
            yield return ScreenshotCapture.Capture("peel14_" + name);
        }

        static IEnumerator Detail(string name, Vector3 eye, Vector3 target, float fov = 40f)
        {
            var go = new GameObject("Peel14Detail");
            var camera = go.AddComponent<Camera>();
            camera.CopyFrom(Rig.cam); camera.enabled = false; camera.fieldOfView = fov; camera.nearClipPlane = 0.02f;
            var source = Rig.cam.GetUniversalAdditionalCameraData(); var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = source.renderPostProcessing; data.volumeLayerMask = source.volumeLayerMask; data.volumeTrigger = Rig.cam.transform;
            go.transform.position = eye; go.transform.LookAt(target);
            yield return ScreenshotCapture.CaptureCamera(camera, "peel14_" + name);
            Object.Destroy(go);
        }

        static IEnumerator Drag(PeelStation task, System.Func<bool> until, float seconds)
        {
            Root.Input.SimUse = true;
            for (float t = 0f; t < seconds && !until(); t += Time.deltaTime)
            {
                Root.Input.SimLook = new Vector2(0f, Time.frameCount % 16 < 8 ? 9f : -9f);
                if (!task.Working) ChoreTestUtil.Look(task.stand, task.board.transform.position);
                yield return null;
            }
            Root.Input.SimLook = Vector2.zero;
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator PotatoPeelingLikeTheRealThing()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            var task = d.storyStations.OfType<PeelStation>().First(x => !x.asparagus);
            var p = task.potatoes;
            // Bảng việc như lúc chơi lượt gọt (Galley() dừng ở lượt quét): HUD và chữ hướng dẫn chỉ việc gọt.
            d.Board.Clear();
            d.Board.Add(OrderKinds.Peel, 6);
            task.OnOrder(6);
            ChoreTestUtil.Look(task.stand, task.board.transform.position);
            yield return new WaitForSeconds(0.4f);
            yield return Capture("01_bucket_full_before");
            yield return Detail("02_table_overview", task.board.transform.TransformPoint(new Vector3(0.1f, 0.6f, 0.75f)), task.board.transform.TransformPoint(new Vector3(0f, 0.05f, 0f)), 45f);
            task.OnInteract(task.board, Rig.interactor);
            yield return PlayTestUtil.WaitUntil(() => task.ShowingDemo, 40f);
            // Góc nhìn thứ nhất đúng như lúc chơi: mắt Ba tự liếc tới tay ông khi ông bắt đầu gọt, không đặt lại camera.
            yield return new WaitForSeconds(2.2f);
            // Batchmode không tự vẽ camera: lần vẽ đầu sau khi ông Mai vào khung (Animator CullUpdateTransforms) còn tư thế trước IK;
            // camera cùng góc vẽ khung sau thấy tay cầm củ và dao đúng (14/10). Vẽ một lần cho Animator thấy, rồi mới chụp.
            yield return Capture("03_mai_demonstrates_fp");
            yield return Capture("03_mai_demonstrates_fp");
            var mh = p.demoHold;
            var toBa = Vector3.ProjectOnPlane(Rig.cam.transform.position - mh.position, Vector3.up).normalized;
            yield return Detail("04_mai_hands_close", mh.position + toBa * 0.5f + Vector3.up * 0.2f + Vector3.Cross(Vector3.up, toBa) * 0.12f, mh.position, 35f);
            yield return Detail("04b_mai_side", mh.position + mh.right * 0.75f + Vector3.up * 0.15f + toBa * 0.2f, mh.position + Vector3.up * 0.1f, 45f);
            yield return PlayTestUtil.WaitUntil(() => task.Ready, 40f);
            yield return Detail("04c_mai_plate", p.demoBowl.position + toBa * 0.35f + Vector3.up * 0.3f, p.demoBowl.position, 35f);
            // Góc nhìn thứ nhất: giữa nhát dao thứ ba của củ đầu.
            yield return Drag(task, () => p.Logic.Band == 2 && p.Logic.Stroke > 0.55f && !p.Busy, 40f);
            Root.Input.SimLook = Vector2.zero;
            yield return null;
            yield return Capture("05_fp_mid_stroke");
            var hold = p.hold;
            yield return Detail("06_hands_right", hold.position + hold.right * 0.45f + Vector3.up * 0.15f + hold.forward * 0.05f, hold.position, 38f);
            yield return Detail("07_potato_close_front", hold.position + Vector3.up * 0.14f + hold.forward * 0.24f + hold.right * 0.06f, hold.position, 32f);
            yield return Detail("08_hands_left", hold.position - hold.right * 0.42f + Vector3.up * 0.12f + hold.forward * 0.05f, hold.position, 38f);
            // Củ thứ hai của Ba là củ cải (S16: "đống củ cải và khoai tây"): vỏ trắng ngả tím, ruột trắng.
            yield return Drag(task, () => p.Current != null && p.Current.name == "Turnip" && p.Held && p.Logic.Band == 2 && p.Logic.Stroke > 0.4f && !p.Busy, 60f);
            Root.Input.SimLook = Vector2.zero;
            yield return null;
            yield return Capture("08b_fp_turnip");
            yield return Detail("08c_turnip_close", hold.position + Vector3.up * 0.14f + hold.forward * 0.24f + hold.right * 0.06f, hold.position, 32f);
            // Hết củ đầu, sang củ thứ ba: nồi có củ trắng, chậu có vỏ.
            yield return Drag(task, () => p.InPot >= 3 && p.Held && p.Logic.Band == 1 && p.Logic.Stroke > 0.3f, 90f);
            yield return Capture("09_fp_third_potato");
            yield return Detail("10_bowl_and_pot", task.board.transform.TransformPoint(new Vector3(0.15f, 0.55f, 0.6f)), task.board.transform.TransformPoint(new Vector3(0.1f, 0.04f, 0.03f)), 42f);
            yield return Drag(task, () => task.Pending == 0, 150f);
            Root.Input.SimUse = false;
            yield return PlayTestUtil.WaitUntil(() => !task.Working, 6f);
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(0, p.InBucket);
            Assert.AreEqual(7, p.InPot);
            ChoreTestUtil.Look(task.stand, task.board.transform.position);
            yield return new WaitForSeconds(0.3f);
            yield return Capture("11_bucket_empty_after");
            yield return Detail("12_pot_full_close", task.board.transform.TransformPoint(new Vector3(0.43f, 0.42f, 0.35f)), task.board.transform.TransformPoint(new Vector3(0.43f, 0.08f, 0.06f)), 35f);
            yield return Detail("13_peel_bowl_close", task.board.transform.TransformPoint(new Vector3(-0.04f, 0.4f, 0.35f)), task.board.transform.TransformPoint(new Vector3(-0.04f, 0.03f, 0.03f)), 35f);
        }
    }
}
