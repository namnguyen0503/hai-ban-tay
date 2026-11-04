using System;
using System.Collections;
using System.IO;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Player;
using HaiBanTay.UI;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace HaiBanTay.DevTools
{
    /// <summary>
    /// Ghi chú debug: phím / (hoặc nút "Ghi chú" trên điện thoại) chụp màn hình, dừng game, mở ô viết.
    /// Gửi thì lưu một thư mục gồm note.txt, state.json (StateDump), screenshot.png, log.txt.
    /// Nơi lưu: Editor/bản build chạy trong repo → &lt;repo&gt;/DebugNotes; Android → Documents/HaiBanTay/DebugNotes
    /// (lỗi thì thư mục riêng của app); còn lại persistentDataPath/DebugNotes. Biến HBT_NOTES_DIR ghi đè.
    /// Tools/pull_debug_notes.sh gom tất cả về &lt;repo&gt;/DebugNotes.
    /// </summary>
    public sealed class DebugNotes : MonoBehaviour
    {
        const string BlockReason = "debug_note";

        GameInput _input;
        UIRoot _ui;
        ContentDatabase _content;
        VisualElement _root;
        TextField _field;
        Label _info;
        Texture2D _shot;
        bool _opening, _wasCursorFree;
        float _savedScale = 1f;
        bool _savedAudioPause;
        float _fps = 60f;

        public static DebugNotes Instance { get; private set; }
        public bool IsOpen { get; private set; }
        public static bool AnyOpen => Instance != null && (Instance.IsOpen || Instance._opening);

        public void Initialize(GameInput input, UIRoot ui, ContentDatabase content)
        {
            Instance = this;
            _input = input;
            _ui = ui;
            _content = content;
            if (ui.NoteLayer == null)
                return;
            _root = Ui.Box("hbt-pause", ui.NoteLayer);
            _root.pickingMode = PickingMode.Position;
            var panel = Ui.Box("hbt-note", _root);
            panel.pickingMode = PickingMode.Position;
            Ui.Text("hbt-note-title", panel, S("note.title"));
            _info = Ui.Text("hbt-note-info", panel, "");
            _field = new TextField { multiline = true, maxLength = 4000 };
            _field.AddToClassList("hbt-note-field");
            panel.Add(_field);
            var footer = Ui.Box("hbt-note-footer", panel);
            footer.pickingMode = PickingMode.Position;
            Ui.Button(S("note.cancel"), Cancel, footer);
            Ui.Button(S("note.submit"), Submit, footer);
            Ui.Show(_root, false);
        }

        string S(string key) => _content.GetString(key);

        void Update()
        {
            // FPS mượt (unscaled) để ghi vào state; rẻ, không cấp phát.
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f)
                _fps = Mathf.Lerp(_fps, 1f / dt, 0.05f);

            var kb = Keyboard.current;
            if (kb == null)
                return;
            if (IsOpen)
            {
                if (kb.escapeKey.wasPressedThisFrame)
                    Cancel();
                else if ((kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed) && kb.enterKey.wasPressedThisFrame)
                    Submit();
                return;
            }
            if (kb.slashKey.wasPressedThisFrame || kb.numpadDivideKey.wasPressedThisFrame)
                Open();
        }

        public void Open()
        {
            if (IsOpen || _opening || _root == null)
                return;
            StartCoroutine(OpenRoutine());
        }

        IEnumerator OpenRoutine()
        {
            _opening = true;
            // Chụp trước khi hiện ô viết: ảnh là đúng khung người chơi đang thấy (kể cả HUD).
            // Batchmode (test) không chạy end-of-frame: bỏ ảnh, vẫn ghi state.
            if (_shot != null)
                Destroy(_shot);
            _shot = null;
            if (!Application.isBatchMode)
            {
                yield return new WaitForEndOfFrame();
                try { _shot = ScreenCapture.CaptureScreenshotAsTexture(); }
                catch (Exception e) { Debug.LogWarning("DebugNotes: không chụp được màn hình. " + e.Message); }
            }
            _opening = false;

            IsOpen = true;
            _savedScale = Time.timeScale;
            _savedAudioPause = AudioListener.pause;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            _wasCursorFree = _input != null && _input.CursorFree;
            _input?.Block(BlockReason);
            _input?.SetCursorFree(true);
            _info.text = string.Format(S("note.info"), Folder());
            _field.value = "";
            Ui.Show(_root, true);
            // Ô viết nhận phím ngay (và bật bàn phím ảo trên điện thoại).
            _field.schedule.Execute(() => _field.Focus());
        }

        void Close()
        {
            IsOpen = false;
            Ui.Show(_root, false);
            _field.Blur();
            Time.timeScale = _savedScale;
            AudioListener.pause = _savedAudioPause;
            _input?.Unblock(BlockReason);
            _input?.SetCursorFree(_wasCursorFree);
        }

        /// <summary>Nội dung đang viết (test đặt trực tiếp).</summary>
        public string Draft
        {
            get => _field != null ? _field.value : null;
            set { if (_field != null) _field.value = value; }
        }

        /// <summary>Thư mục của ghi chú vừa lưu.</summary>
        public string LastSaved { get; private set; }

        public void Cancel()
        {
            if (!IsOpen)
                return;
            Close();
            if (_shot != null)
                Destroy(_shot);
            _shot = null;
        }

        public void Submit()
        {
            if (!IsOpen)
                return;
            string note = _field.value ?? "";
            // Chụp state khi game còn dừng đúng khung đã chụp ảnh.
            string dir = null;
            try
            {
                var state = StateDump.Capture(note, _fps);
                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                dir = Path.Combine(Folder(), DateTime.Now.ToString("yyyyMMdd-HHmmss") + "_" + scene);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "note.txt"), note);
                File.WriteAllText(Path.Combine(dir, "state.json"), JsonConvert.SerializeObject(state, Formatting.Indented,
                    new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore }));
                File.WriteAllText(Path.Combine(dir, "log.txt"), LogBuffer.Dump());
                if (_shot != null)
                    File.WriteAllBytes(Path.Combine(dir, "screenshot.png"), _shot.EncodeToPNG());
                Debug.Log("DebugNotes: đã lưu " + dir);
                LastSaved = dir;
            }
            catch (Exception e)
            {
                Debug.LogError("DebugNotes: không lưu được. " + e);
                Close();
                _ui?.Toast.Show(string.Format(S("note.failed"), e.Message));
                return;
            }
            Close();
            if (_shot != null)
                Destroy(_shot);
            _shot = null;
            _ui?.Toast.Show(string.Format(S("note.saved"), dir));
        }

        static string _folder;

        /// <summary>Thư mục gốc chứa các ghi chú (tạo nếu chưa có).</summary>
        public static string Folder()
        {
            string env = Environment.GetEnvironmentVariable("HBT_NOTES_DIR");
            if (!string.IsNullOrEmpty(env) && TryDir(env))
                return env;
            if (_folder != null)
                return _folder;
            if (Application.platform == RuntimePlatform.Android)
            {
                // Documents chung: mở được bằng trình quản lý tệp của máy, không cần root/adb.
                string docs = "/storage/emulated/0/Documents/HaiBanTay/DebugNotes";
                if (TryDir(docs))
                    return _folder = docs;
            }
            else
            {
                string repo = FindRepo(Application.dataPath);
                if (repo != null && TryDir(Path.Combine(repo, "DebugNotes")))
                    return _folder = Path.Combine(repo, "DebugNotes");
            }
            string fallback = Path.Combine(Application.persistentDataPath, "DebugNotes");
            TryDir(fallback);
            return _folder = fallback;
        }

        static string FindRepo(string start)
        {
            try
            {
                var d = new DirectoryInfo(start);
                for (int i = 0; i < 5 && d != null; i++, d = d.Parent)
                {
                    if (File.Exists(Path.Combine(d.FullName, "PLAN.md")) && Directory.Exists(Path.Combine(d.FullName, "Game")))
                        return d.FullName;
                }
            }
            catch (Exception) { /* không có quyền đọc thư mục cha */ }
            return null;
        }

        static bool TryDir(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                string probe = Path.Combine(path, ".probe");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            if (_shot != null)
                Destroy(_shot);
        }
    }
}
