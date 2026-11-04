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
    /// <summary>Ảnh duyệt bàn sơ chế 14/10 (QA-0040, QA-0071): đủ đồ cùng lúc như lúc chơi các lượt trên bàn — thớt rau và sọt rau
    /// ở đầu tây, rổ lá tốt, bàn gọt (xô, chậu, nồi nước, đĩa vỏ của ông Mai), khay phục vụ có nồi canh ở góc đông bắc.</summary>
    [Category("Screenshots"), Explicit]
    public sealed class Prep14Shots
    {
        static GameRoot Root => GameRoot.Instance;
        static PlayerRig Rig => PlayerRig.Current;
        [SetUp] public void SetUp() => Time.captureDeltaTime = 1f / 60f;
        [TearDown] public void TearDown()
        {
            Time.captureDeltaTime = 0f; ScreenshotCapture.RestoreUi(); PlayTestUtil.ExitSim();
        }

        static IEnumerator Detail(string name, Vector3 eye, Vector3 target, float fov = 45f)
        {
            var go = new GameObject("Prep14Detail");
            var camera = go.AddComponent<Camera>();
            camera.CopyFrom(Rig.cam); camera.enabled = false; camera.fieldOfView = fov; camera.nearClipPlane = 0.02f;
            var source = Rig.cam.GetUniversalAdditionalCameraData(); var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = source.renderPostProcessing; data.volumeLayerMask = source.volumeLayerMask; data.volumeTrigger = Rig.cam.transform;
            go.transform.position = eye; go.transform.LookAt(target);
            yield return ScreenshotCapture.CaptureCamera(camera, "prep14_" + name);
            Object.Destroy(go);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator PrepTableWithEverythingOnIt()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            var peel = d.storyStations.OfType<PeelStation>().First(x => !x.asparagus);
            var serve = d.storyStations.OfType<VoyageDelivery>().First(x => x.Kind == "serve");
            var greens = Object.FindAnyObjectByType<GreensStation>();
            d.Board.Clear();
            peel.OnOrder(6);
            serve.OnOrder(1);
            greens.rawBasket.SetParent(greens.rawTablePoint, false);
            greens.rawBasket.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            Root.UI.SetFade(0f);
            ChoreTestUtil.Look(new Vector3(0.1f, 0f, -1.35f), new Vector3(0.1f, 0.9f, 0f));
            yield return new WaitForSeconds(0.5f);
            yield return ScreenshotCapture.Capture("prep14_01_fp_from_south");
            yield return Detail("02_overview_above", new Vector3(0.1f, 2.3f, -1.6f), new Vector3(0.1f, 0.9f, 0f), 55f);
            yield return Detail("03_east_corner_tray", new Vector3(1.45f, 1.55f, -0.35f), new Vector3(0.6f, 0.95f, 0.15f));
            yield return Detail("04_west_end_greens", new Vector3(-1.6f, 1.55f, -0.6f), new Vector3(-0.55f, 0.95f, 0f));
        }
    }
}
