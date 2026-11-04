using System;
using System.Collections;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// BOT_VIDEO=1: ghi khung hình (camera chính + UI, 1280×720, 8 khung/giây theo giờ game: Time.unscaledTime, hoặc Time.time khi bot nhanh đặt captureDeltaTime) trong lúc bot chơi,
    /// ra Logs/video/&lt;run&gt;/f_######.jpg và frames.txt (số khung, giây, cảnh/beat). Tools/bot_video.py ghép thành MP4 có chương.
    /// Đọc GPU bất đồng bộ, nén JPG ở luồng phụ: bot không phải chờ ghi đĩa.
    /// Quay trailer: BOT_VIDEO_FPS=60 (khung/giây), BOT_VIDEO_HD=1|2 (1920×1080 | 2560×1440, JPG 90), BOT_VIDEO_UI=0 (chỉ cảnh, không HUD/phụ đề),
    /// BOT_VIDEO_BEATS=P1a,B0,… (chỉ quay các beat này). Có Time.captureDeltaTime (BOT_FAST, test Marseille) thì quay như render
    /// offline: đọc GPU đồng bộ, hàng nén đầy thì chờ chứ không bỏ khung, giờ game đứng chờ máy ghi nên video đủ khung.
    /// </summary>
    public sealed class BotVideo : MonoBehaviour
    {
        const int MaxPending = 32;
        int Width = 1280, Height = 720, _quality = 70;
        float Interval = 1f / 8f;
        bool _withUi = true;
        System.Collections.Generic.HashSet<string> _beats;

        public static BotVideo Current { get; private set; }
        public static bool Requested => Environment.GetEnvironmentVariable("BOT_VIDEO") == "1";

        string _folder;
        Func<string> _label;
        StreamWriter _index;
        RenderTexture _scene, _ui;
        float _next;
        int _frame, _pending, _written, _skipped;

        sealed class Shot
        {
            public int id, parts;
            public byte[] scene, ui;
        }

        public static void Begin(string run, Func<string> label)
        {
            if (Current != null) return;
            var go = new GameObject("BotVideo");
            DontDestroyOnLoad(go);
            var v = go.AddComponent<BotVideo>();
            v._folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Logs", "video", run));
            Directory.CreateDirectory(v._folder);
            v._index = new StreamWriter(Path.Combine(v._folder, "frames.txt"));
            v._label = label;
            if (int.TryParse(Environment.GetEnvironmentVariable("BOT_VIDEO_FPS"), out int fps) && fps > 0)
                v.Interval = 1f / Mathf.Clamp(fps, 1, 60);
            // BOT_VIDEO_HD=1: 1920×1080; =2: 2560×1440 (trailer: quay 2K rồi thu về 1080p cho cạnh mượt).
            var hd = Environment.GetEnvironmentVariable("BOT_VIDEO_HD");
            if (hd == "1") { v.Width = 1920; v.Height = 1080; v._quality = 90; }
            else if (hd == "2") { v.Width = 2560; v.Height = 1440; v._quality = 90; }
            v._withUi = Environment.GetEnvironmentVariable("BOT_VIDEO_UI") != "0";
            var beats = Environment.GetEnvironmentVariable("BOT_VIDEO_BEATS");
            if (!string.IsNullOrWhiteSpace(beats))
                v._beats = new System.Collections.Generic.HashSet<string>(System.Linq.Enumerable.Select(beats.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), x => x.Trim()));
            v._scene = new RenderTexture(v.Width, v.Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            v._ui = new RenderTexture(v.Width, v.Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Current = v;
            Debug.Log("BotVideo: ghi vào " + v._folder);
        }

        // captureDeltaTime: Time.unscaledTime vẫn chạy theo giờ thật, giờ game mới đều 1/Fps mỗi khung.
        static float Clock => Time.captureDeltaTime > 0f ? Time.time : Time.unscaledTime;

        void LateUpdate()
        {
            // Lệch 10% khoảng chụp: khung 1/60 s với khoảng 1/60 s không bị bỏ cách khung vì sai số float.
            if (Clock + Interval * 0.1f < _next) return;
            string label = "";
            try { label = _label?.Invoke() ?? ""; } catch (Exception) { }
            if (_beats != null)
            {
                int tab = label.IndexOf('\t');
                string head = tab >= 0 ? label.Substring(0, tab) : label;
                if (!_beats.Contains(head.Substring(head.LastIndexOf(' ') + 1))) { _next = Clock; return; }
            }
            _next = Mathf.Max(_next + Interval, Clock + Interval * 0.5f);
            var cam = Camera.main;
            if (cam == null || !cam.isActiveAndEnabled) return;
            bool offline = Time.captureDeltaTime > 0f;
            if (offline)
                while (Volatile.Read(ref _pending) >= MaxPending) Thread.Sleep(2);
            else if (Volatile.Read(ref _pending) >= MaxPending) { _skipped++; return; }
            if (_withUi && ScreenshotCapture.UiTexture == null) ScreenshotCapture.RedirectUi();

            var req = new RenderPipeline.StandardRequest { destination = _scene };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else
            {
                var prev = cam.targetTexture;
                cam.targetTexture = _scene;
                cam.Render();
                cam.targetTexture = prev;
            }
            if (_withUi && ScreenshotCapture.UiTexture != null) Graphics.Blit(ScreenshotCapture.UiTexture, _ui);
            else if (_withUi) { var p = RenderTexture.active; RenderTexture.active = _ui; GL.Clear(true, true, Color.clear); RenderTexture.active = p; }

            var shot = new Shot { id = _frame++ };
            _index.WriteLine(shot.id + "\t" + Clock.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + "\t" + label);
            Interlocked.Increment(ref _pending);
            if (offline && !_withUi)
            {
                var sync = AsyncGPUReadback.Request(_scene, 0, TextureFormat.RGBA32);
                sync.WaitForCompletion();
                if (!sync.hasError) shot.scene = sync.GetData<byte>().ToArray();
                shot.parts = 1;
                Part(shot);
                return;
            }
            AsyncGPUReadback.Request(_scene, 0, TextureFormat.RGBA32, r => { if (!r.hasError) shot.scene = r.GetData<byte>().ToArray(); Part(shot); });
            if (_withUi)
                AsyncGPUReadback.Request(_ui, 0, TextureFormat.RGBA32, r => { if (!r.hasError) shot.ui = r.GetData<byte>().ToArray(); Part(shot); });
            else
                shot.parts++;
        }

        void Part(Shot shot)
        {
            if (++shot.parts < 2) return;
            if (shot.scene == null) { Interlocked.Decrement(ref _pending); return; }
            string path = Path.Combine(_folder, "f_" + shot.id.ToString("D6") + ".jpg");
            ThreadPool.QueueUserWorkItem(_ => Encode(shot, path));
        }

        void Encode(Shot shot, string path)
        {
            try
            {
                var a = shot.scene; var b = shot.ui;
                var rgb = new byte[Width * Height * 3];
                for (int i = 0, o = 0; i < a.Length; i += 4, o += 3)
                {
                    // UI Toolkit vẽ vào RT nền trong suốt (màu đã nhân alpha), giống ScreenshotCapture.
                    float k = b != null ? b[i + 3] / 255f : 0f;
                    for (int c = 0; c < 3; c++)
                    {
                        float v = a[i + c] * (1f - k) + (b != null ? b[i + c] : 0);
                        rgb[o + c] = (byte)(v > 255f ? 255 : v);
                    }
                }
                var jpg = ImageConversion.EncodeArrayToJPG(rgb, GraphicsFormat.R8G8B8_SRGB, (uint)Width, (uint)Height, 0, _quality);
                File.WriteAllBytes(path, jpg);
                Interlocked.Increment(ref _written);
            }
            catch (Exception) { }
            finally { Interlocked.Decrement(ref _pending); }
        }

        /// <summary>Chờ các khung đang đọc/nén xong (tối đa 20 s), đóng frames.txt, gỡ máy ghi.</summary>
        public IEnumerator Finish()
        {
            enabled = false;
            float until = Time.realtimeSinceStartup + 20f;
            while (Volatile.Read(ref _pending) > 0 && Time.realtimeSinceStartup < until) yield return null;
            _index.Flush(); _index.Dispose();
            Debug.Log($"BotVideo: {_written}/{_frame} khung đã ghi, bỏ {_skipped} khung vì hàng chờ đầy; {_folder}");
            _scene.Release(); Destroy(_scene);
            _ui.Release(); Destroy(_ui);
            Current = null;
            Destroy(gameObject);
        }
    }
}
