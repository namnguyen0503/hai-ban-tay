using System.Collections;
using System.IO;
using HaiBanTay.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace HaiBanTay.Tests
{
    /// <summary>Render camera chính + UI Toolkit vào ảnh 1920×1080 (dùng cho Tools/screenshots.sh và kiểm tra tràn chữ).</summary>
    public static class ScreenshotCapture
    {
        public const int Width = 1920;
        public const int Height = 1080;

        static RenderTexture _uiRt;
        static PanelSettings _original;
        static PanelSettings _clone;

        /// <summary>RT mà UI đang vẽ vào sau RedirectUi (null khi UI vẽ ra màn hình). BotVideo ghép nó lên khung camera.</summary>
        public static RenderTexture UiTexture => _clone != null ? _uiRt : null;

        public static string Folder
        {
            get
            {
                var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Logs", "shots"));
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>Cho UI vẽ vào RenderTexture 1920×1080 (panel có đúng kích thước tham chiếu).</summary>
        public static void RedirectUi(int width = Width, int height = Height)
        {
            var doc = GameRoot.Instance.UI.GetComponent<UIDocument>();
            if (_clone != null && doc.panelSettings == _clone && _uiRt.width == width && _uiRt.height == height)
                return;
            if (_clone != null)
                RestoreUi();
            _original = doc.panelSettings;
            _uiRt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _clone = Object.Instantiate(_original);
            _clone.targetTexture = _uiRt;
            _clone.clearColor = true;
            _clone.colorClearValue = new Color(0f, 0f, 0f, 0f);
            doc.panelSettings = _clone;
        }

        public static void RestoreUi()
        {
            if (_clone == null || GameRoot.Instance == null)
                return;
            var doc = GameRoot.Instance.UI.GetComponent<UIDocument>();
            doc.panelSettings = _original;
            Object.Destroy(_clone);
            _uiRt.Release();
            Object.Destroy(_uiRt);
            _clone = null;
            _uiRt = null;
        }

        public static IEnumerator Capture(string name, int width = Width, int height = Height)
        {
            RedirectUi(width, height);
            yield return null; // để panel vẽ lại vào RT
            var cam = Camera.main;
            var camRt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            if (cam != null)
            {
                var req = new RenderPipeline.StandardRequest { destination = camRt };
                if (RenderPipeline.SupportsRenderRequest(cam, req))
                    RenderPipeline.SubmitRenderRequest(cam, req);
                else
                {
                    var prev = cam.targetTexture;
                    cam.targetTexture = camRt;
                    cam.Render();
                    cam.targetTexture = prev;
                }
            }
            var scene = Read(camRt);
            var ui = Read(_uiRt);
            var a = scene.GetPixels32();
            var b = ui.GetPixels32();
            for (int i = 0; i < a.Length; i++)
            {
                float k = b[i].a / 255f;
                a[i] = new Color32(
                    (byte)Mathf.Clamp(a[i].r * (1f - k) + b[i].r, 0, 255),
                    (byte)Mathf.Clamp(a[i].g * (1f - k) + b[i].g, 0, 255),
                    (byte)Mathf.Clamp(a[i].b * (1f - k) + b[i].b, 0, 255), 255);
            }
            scene.SetPixels32(a);
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), scene.EncodeToPNG());
            Object.Destroy(scene);
            Object.Destroy(ui);
            camRt.Release();
            Object.Destroy(camRt);
            Debug.Log("Screenshot: " + name);
        }

        public static IEnumerator CaptureCamera(Camera camera, string name)
        {
            yield return null;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var request = new RenderPipeline.StandardRequest { destination = rt };
            RenderPipeline.SubmitRenderRequest(camera, request);
            var texture = Read(rt);
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), texture.EncodeToPNG());
            Object.Destroy(texture);
            rt.Release();
            Object.Destroy(rt);
            Debug.Log("Screenshot: " + name);
        }

        static Texture2D Read(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex;
        }
    }
}
