using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Chạy BodyClipDetector mỗi khung sau IK và NpcToolFollow, cho mọi NPC đo được trong cảnh (hay chỉ các NPC truyền vào Watch).
    /// shots: chụp chỗ lún (ba khung: cận bên hông phía chỗ chạm, cận phía trước, toàn thân) khi một cặp lún sâu hơn lần chụp trước ≥ 1 cm, tối
    /// đa 3 lần mỗi cặp, ghi đè ảnh cũ, vào Logs/shots/bodyclip/&lt;cảnh&gt;/. Report ghi Logs/qa/body_clip_&lt;nhãn&gt;.md.
    /// </summary>
    [DefaultExecutionOrder(14300)]
    public sealed class BodyClipProbe : MonoBehaviour
    {
        public readonly BodyClipDetector Detector = new BodyClipDetector();
        public readonly BodyClipTrack Track = new BodyClipTrack();
        public bool shots;
        /// <summary>Bắt mọi NPC chạy Animator, không đóng băng khi khuất (đo cả đám đông ở xa người chơi).</summary>
        public bool animateAll;

        readonly List<NpcAgent> _watch = new List<NpcAgent>();
        readonly List<NpcAgent> _npcs = new List<NpcAgent>();
        readonly List<BodyClipDetector.Contact> _contacts = new List<BodyClipDetector.Contact>();
        readonly Dictionary<string, int> _shotCount = new Dictionary<string, int>();
        float _listAt = -1f, _last = -1f;
        Camera _camera;
        RenderTexture _rt;
        Texture2D _tex;
        const int ShotW = 640, ShotH = 540;

        public BodyClipProbe Watch(params NpcAgent[] npcs)
        {
            _watch.AddRange(npcs);
            return this;
        }

        public static string ShotFolder(string scene) => Path.Combine(ScreenshotCapture.Folder, "bodyclip", scene);

        void Refresh()
        {
            _npcs.Clear();
            if (_watch.Count > 0) _npcs.AddRange(_watch.Where(n => n != null));
            else _npcs.AddRange(FindObjectsByType<NpcAgent>(FindObjectsSortMode.None));
            if (!animateAll) return;
            foreach (var npc in _npcs)
            {
                npc.keepAnimated = true;
                if (npc.isActiveAndEnabled) npc.SetAnimated(true);
                var animator = npc.GetComponentInChildren<Animator>();
                if (animator != null) animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }

        void LateUpdate()
        {
            if (Time.time >= _listAt)
            {
                _listAt = Time.time + 1f;
                Refresh();
            }
            float dt = _last < 0f ? Time.deltaTime : Time.time - _last;
            _last = Time.time;
            _contacts.Clear();
            Detector.Sample(_npcs, _contacts);
            Track.Feed(SceneManager.GetActiveScene().name, _contacts, dt, Detector.Role);
            if (!shots || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            // Chụp chỗ lún sâu nhất mỗi cặp ở khung này nếu đã qua ngưỡng minor và sâu hơn ảnh trước ≥ 1 cm.
            foreach (var c in _contacts.OrderByDescending(x => x.depth))
            {
                string key = BodyClipTrack.KeyOf(c);
                var f = Track.Findings[key];
                if (c.depth < f.maxDepth - 1e-4f || c.depth < f.shotDepth + 0.01f) continue;
                _shotCount.TryGetValue(key, out int n);
                if (n >= 3) continue;
                _shotCount[key] = n + 1;
                f.shotDepth = c.depth;
                f.shot = Shoot(c, f);
            }
        }

        string Shoot(BodyClipDetector.Contact c, BodyClipTrack.Finding f)
        {
            var rig = PlayerRig.Current;
            if (_camera == null)
            {
                var go = new GameObject("BodyClipCamera");
                _camera = go.AddComponent<Camera>();
                if (rig != null && rig.cam != null) _camera.CopyFrom(rig.cam);
                _camera.enabled = false;
                _camera.fieldOfView = 50f;
                _camera.nearClipPlane = 0.03f;
                if (rig != null && rig.cam != null)
                {
                    var source = rig.cam.GetUniversalAdditionalCameraData();
                    var data = _camera.GetUniversalAdditionalCameraData();
                    data.renderPostProcessing = source.renderPostProcessing;
                    data.volumeLayerMask = source.volumeLayerMask;
                }
                _rt = new RenderTexture(ShotW, ShotH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                _tex = new Texture2D(ShotW * 3, ShotH, TextureFormat.RGB24, false);
            }
            var npc = c.npc.transform;
            float side = c.local.x >= 0f ? 1f : -1f;
            // Cận bên hông phía chỗ chạm, cận phía trước, toàn thân chếch trước (thấy đồ đang ở đâu so với người).
            var body = npc.position + Vector3.up * 1.0f;
            var views = new[]
            {
                (c.point, c.point + npc.right * (side * 0.95f) + npc.forward * 0.3f + Vector3.up * 0.15f),
                (c.point, c.point + npc.forward * 1.0f + npc.right * (side * 0.25f) + Vector3.up * 0.2f),
                (body, body + npc.forward * 2.0f + npc.right * (side * 1.3f) + Vector3.up * 0.5f),
            };
            for (int v = 0; v < views.Length; v++)
            {
                var (look, at) = views[v];
                var eye = Clear(look, at, c.npc);
                _camera.transform.position = eye;
                _camera.transform.LookAt(look);
                RenderPipeline.SubmitRenderRequest(_camera, new RenderPipeline.StandardRequest { destination = _rt });
                var prev = RenderTexture.active;
                RenderTexture.active = _rt;
                _tex.ReadPixels(new Rect(0, 0, ShotW, ShotH), v * ShotW, 0);
                RenderTexture.active = prev;
            }
            _tex.Apply();
            var folder = ShotFolder(f.scene);
            Directory.CreateDirectory(folder);
            string name = Slug($"{f.role}_{f.kind}_{f.a}_{f.b}_{f.npc}") + ".jpg";
            File.WriteAllBytes(Path.Combine(folder, name), _tex.EncodeToJPG(85));
            return "bodyclip/" + f.scene + "/" + name;
        }

        /// <summary>Kéo máy vào trước vách / vật tĩnh chắn giữa chỗ chạm và máy (bỏ qua thân NPC, đồ đang bê).</summary>
        static Vector3 Clear(Vector3 look, Vector3 eye, NpcAgent npc)
        {
            var dir = eye - look;
            float dist = dir.magnitude, near = dist;
            foreach (var hit in Physics.RaycastAll(look, dir / dist, dist, ~(1 << 2), QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(npc.transform) && hit.collider.attachedRigidbody == null)
                    near = Mathf.Min(near, hit.distance);
            return near < dist ? look + dir / dist * Mathf.Max(0.3f, near - 0.05f) : eye;
        }

        static string Slug(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s.Normalize(NormalizationForm.FormD))
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                char k = ch == 'đ' ? 'd' : ch == 'Đ' ? 'D' : ch;
                sb.Append(char.IsLetterOrDigit(k) && k < 128 ? k : '_');
            }
            var text = Regex(sb.ToString());
            return text.Length > 110 ? text.Substring(0, 110) : text;
        }

        static string Regex(string s) => System.Text.RegularExpressions.Regex.Replace(s, "_+", "_").Trim('_');

        void OnDestroy()
        {
            if (_camera != null) Destroy(_camera.gameObject);
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            if (_tex != null) Destroy(_tex);
        }

        /// <summary>Báo cáo Markdown: MAJOR rồi MINOR, gom theo vai + loại + khúc + đồ chung tên; kèm từng NPC.</summary>
        public static string Report(IEnumerable<BodyClipTrack.Finding> findings, string title, string notes)
        {
            var list = findings.Where(f => f.Severity != "INFO").ToList();
            var sb = new StringBuilder($"# {title}\n\n{notes}\n\n");
            sb.AppendLine($"MAJOR {list.Count(f => f.Severity == "MAJOR")}, MINOR {list.Count(f => f.Severity == "MINOR")} (theo từng NPC).\n");
            sb.AppendLine("Ngưỡng (lún sâu nhất, mức lỗi kéo dài ≥ 0,2 s): " + string.Join("; ",
                System.Enum.GetValues(typeof(BodyClipDetector.Kind)).Cast<BodyClipDetector.Kind>().Select(k =>
                    $"{BodyClipDetector.KindText(k)} {BodyClipDetector.Limits(k).minor * 100f:F1}/{BodyClipDetector.Limits(k).major * 100f:F1} cm")) + ".\n");
            sb.AppendLine("## Gom theo vai\n");
            sb.AppendLine("| Mức | Cảnh | Vai | Loại | Khúc ↔ đồ | Số NPC | Lún sâu nhất | Liền dài nhất | Dáng | Ảnh |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var g in list.GroupBy(f => (f.scene, f.role, f.kind, a: BodyClipDetector.Generic(f.a), b: BodyClipDetector.Generic(Strip(f.b))))
                         .Select(g => (g, worst: g.OrderByDescending(f => f.Severity == "MAJOR").ThenByDescending(f => f.maxDepth).First()))
                         .OrderByDescending(x => x.worst.Severity == "MAJOR").ThenByDescending(x => x.worst.maxDepth))
            {
                var w = g.worst;
                var states = g.g.SelectMany(f => f.states).GroupBy(s => s.Key).Select(s => (s.Key, s.Max(x => x.Value)))
                    .OrderByDescending(s => s.Item2).Select(s => $"{(s.Key == "" ? "-" : s.Key)} {s.Item2 * 100f:F0}");
                string shot = g.g.Where(f => f.shot != null).OrderByDescending(f => f.shotDepth).Select(f => f.shot).FirstOrDefault();
                sb.AppendLine($"| {w.Severity} | {w.scene} | {w.role} | {BodyClipDetector.KindText(w.kind)} | {g.g.Key.a} ↔ {g.g.Key.b} | {g.g.Count()} | " +
                              $"{w.maxDepth * 100f:F1} cm | {g.g.Max(f => f.longest):F1} s | {string.Join(", ", states)} | {(shot != null ? $"[ảnh](../shots/{shot})" : "")} |");
            }
            sb.AppendLine("\n## Từng NPC\n");
            foreach (var f in list.OrderByDescending(f => f.Severity == "MAJOR").ThenByDescending(f => f.maxDepth))
                sb.AppendLine("- " + BodyClipTrack.Line(f) + (f.shot != null ? $" — ảnh `Logs/shots/{f.shot}`" : ""));
            return sb.ToString();
        }

        /// <summary>Bỏ tên NPC khác khỏi "Npc_xx khúc" để gom (đồ xuyên người khác).</summary>
        static string Strip(string b)
        {
            foreach (var part in BodyClipDetector.PartNames)
                if (b.EndsWith(" " + part)) return "người khác " + part;
            return b;
        }

        public static string QaFolder
        {
            get
            {
                var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Logs", "qa"));
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
    }
}
