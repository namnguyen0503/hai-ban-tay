using System.Collections;
using System.IO;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Ghi clip cho Brush17PlayTests: mỗi lượt việc quay hai luồng 15 khung/giây (giờ game 1/60 s mỗi khung), mắt người chơi và máy đứng
    /// chếch bên phải sau vai (thấy bàn chải, nồi, ghế, thân Ba), vào Logs/shots/clips/brush17_&lt;tên&gt;_{fp,side}/. Tools/clips.py ghép MP4.
    /// Tắt (mặc định) thì không làm gì: bộ test thường chạy -nographics.
    /// </summary>
    public static class Brush17Recorder
    {
        const int Width = 1280, Height = 720, Every = 4;
        public static bool Enabled;
        static string _fp, _side;
        static Camera _sideCam;
        static RenderTexture _rt;
        static Texture2D _tex;
        static Vector3 _offset, _lookOffset;
        static int _frame, _shot;

        static PlayerRig Rig => PlayerRig.Current;

        static string Folder(string name)
        {
            var folder = Path.Combine(ScreenshotCapture.Folder, "clips", name);
            if (Directory.Exists(folder))
                foreach (var old in Directory.GetFiles(folder)) File.Delete(old);
            Directory.CreateDirectory(folder);
            return folder;
        }

        public static void Begin(string label)
        {
            if (!Enabled || Rig == null) return;
            End();
            string slug = "brush17_" + label.Replace(' ', '_');
            _fp = Folder(slug + "_fp");
            _side = Folder(slug + "_side");
            var go = new GameObject("Brush17SideCam");
            _sideCam = go.AddComponent<Camera>();
            _sideCam.CopyFrom(Rig.cam);
            _sideCam.enabled = false;
            _sideCam.fieldOfView = 60f;
            _sideCam.nearClipPlane = 0.05f;
            var source = Rig.cam.GetUniversalAdditionalCameraData();
            var data = _sideCam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = source.renderPostProcessing;
            data.volumeLayerMask = source.volumeLayerMask;
            data.volumeTrigger = Rig.cam.transform;
            var fwd = Vector3.ProjectOnPlane(Rig.cam.transform.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Rig.transform.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            // Chếch phải, lùi, cao hơn đầu: nhìn xuống chỗ tay làm (trước mặt 0,7 m, ngang hông).
            _offset = right * 1.3f - fwd * 0.9f + Vector3.up * 0.55f;
            _lookOffset = fwd * 0.7f - Vector3.up * 0.55f;
            _rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            _frame = _shot = 0;
        }

        public static void Frame()
        {
            if (_sideCam == null) return;
            if (_frame++ % Every != 0) return;
            var head = Rig.head.position;
            _sideCam.transform.position = head + _offset;
            _sideCam.transform.LookAt(head + _lookOffset);
            Save(Rig.cam, _fp);
            Save(_sideCam, _side);
            _shot++;
        }

        static void Save(Camera cam, string folder)
        {
            RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = _rt });
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            _tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(folder, $"f_{_shot:000}.jpg"), _tex.EncodeToJPG(85));
        }

        public static void End()
        {
            if (_sideCam == null) return;
            File.WriteAllText(Path.Combine(_fp, "fps.txt"), (60 / Every).ToString());
            File.WriteAllText(Path.Combine(_side, "fps.txt"), (60 / Every).ToString());
            Debug.Log($"Clip: {_fp} {_shot} khung");
            Object.Destroy(_sideCam.gameObject);
            _rt.Release();
            Object.Destroy(_rt);
            Object.Destroy(_tex);
            _sideCam = null;
        }
    }

    /// <summary>Ảnh + clip 60 Hz của brush17 (Explicit, GPU): chạy lại đúng các lượt việc của Brush17PlayTests với máy ghi bật.</summary>
    [Category("Screenshots"), Explicit]
    public sealed class Brush17Shots
    {
        readonly Brush17PlayTests _tests = new Brush17PlayTests();

        [SetUp] public void SetUp() { Brush17Recorder.Enabled = true; _tests.SetUp(); }
        [TearDown] public void TearDown() { Brush17Recorder.End(); Brush17Recorder.Enabled = false; _tests.TearDown(); }

        [UnityTest, Timeout(300000)] public IEnumerator Brush17ScrubBasin() => _tests.ScrubbingTheBasinPotWithTheShortBrush();
        [UnityTest, Timeout(300000)] public IEnumerator Brush17ScrubPan() => _tests.ScrubbingTheCopperPanWithTheLongBrush();
        [UnityTest, Timeout(300000)] public IEnumerator Brush17TowTallPot() => _tests.TowingTheTallPotOutOfTheWashCorner();
        [UnityTest, Timeout(300000)] public IEnumerator Brush17LiftOntoHob() => _tests.LiftingTheTallPotOntoTheHob();
        [UnityTest, Timeout(300000)] public IEnumerator Brush17StoolScrub() => _tests.ScrubbingTheTallPotFromTheStool();

        /// <summary>Toàn cảnh phòng rửa: máy cao ở góc đông bắc nhìn về chậu, kệ bàn chải, nồi cao, ghế đẩu, móc bàn chải dài.</summary>
        [UnityTest, Timeout(180000)]
        public IEnumerator Brush17WashRoomOverview()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            var basin = d.wash.basin.transform.position;
            var pot = d.tall != null ? d.tall.potRoot.position : basin;
            var mid = (basin + pot) * 0.5f;
            var go = new GameObject("Brush17Overview");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(PlayerRig.Current.cam);
            cam.enabled = false;
            cam.fieldOfView = 75f;
            cam.nearClipPlane = 0.05f;
            foreach (var (name, from) in new[] { ("brush17_washroom_a", mid + new Vector3(-1.6f, 2.1f, 1.2f)), ("brush17_washroom_b", mid + new Vector3(1.4f, 2.1f, 1.2f)) })
            {
                go.transform.position = from;
                go.transform.LookAt(mid + Vector3.up * 0.4f);
                yield return ScreenshotCapture.CaptureCamera(cam, name);
            }
            Object.Destroy(go);
        }
    }
}
