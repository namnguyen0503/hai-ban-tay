using System;
using System.Collections.Generic;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace HaiBanTay.UI
{
    /// <summary>
    /// Điều khiển cảm ứng cho bản điện thoại, ghi vào GameInput.Touch nên gameplay không cần biết thiết bị.
    /// Ngón chạm nửa trái (ngoài nút) = cần đi ảo; nửa phải = nhìn, chạm nhanh không rê = tương tác (như nhấn chuột trái).
    /// Nút: Dùng (giữ, = giữ chuột trái), Giơ tay (giữ, = Space), Làm (E), Ngồi (C), Sổ (Tab), tạm dừng ‖ góc trái trên (Esc),
    /// Ghi chú (/). Ngón đang giữ nút mà rê đi thì vẫn xoay nhìn như nửa phải màn hình: giữ Dùng rê luôn để cào, quét, cọ
    /// hay quay người, không cần ngón thứ hai.
    /// Đọc thẳng Touchscreen (đa điểm), chỉ dùng UI Toolkit để vẽ và để biết ngón chạm có rơi vào UI khác (sổ, menu) không.
    /// Bật trên Android/iOS; trên PC bật bằng tham số "-touch" hoặc HBT_TOUCH=1 để thử.
    /// </summary>
    [DefaultExecutionOrder(-1100)]
    public sealed class TouchControls : MonoBehaviour
    {
        const float StickRadius = 120f;     // đơn vị panel (tham chiếu 1080p)
        const float LookScale = 1.25f;      // pixel panel → "pixel chuột"
        const float TapSeconds = 0.25f;
        const float TapSlop = 18f;
        const float StickZone = 0.42f;      // phần trái màn hình dành cho cần đi

        enum Role { None, Stick, Look, Button, Ui }

        sealed class Finger
        {
            public Role Role;
            public Vector2 Start, Last;
            public float StartTime;
            public bool Moved;
            public TouchButtonView Button;
        }

        sealed class TouchButtonView
        {
            public VisualElement Element;
            public Action Press;
            public bool Hold;
            public bool Gameplay;   // ẩn khi điều khiển bị chặn (sổ, phim, tạm dừng)
            public bool InMenu;     // hiện cả ở menu chính
            public int Down;
            public TouchHold State;
        }

        public static bool Enabled { get; private set; }

        GameInput _input;
        UIRoot _ui;
        SceneFlow _scenes;
        Func<bool> _noteOpen;
        VisualElement _root, _stickBase, _stickKnob;
        readonly List<TouchButtonView> _buttons = new List<TouchButtonView>();
        readonly Dictionary<int, Finger> _fingers = new Dictionary<int, Finger>();
        readonly List<int> _gone = new List<int>();
        TouchButtonView _use, _raise;
        int _stickId = -1;
        bool _toggle;

        public static bool ShouldEnable()
        {
            if (Application.isMobilePlatform)
                return true;
            if (Environment.GetEnvironmentVariable("HBT_TOUCH") == "1")
                return true;
            return Array.IndexOf(Environment.GetCommandLineArgs(), "-touch") >= 0;
        }

        public void Initialize(GameInput input, UIRoot ui, SceneFlow scenes, ContentDatabase content, Action openNote, Func<bool> noteOpen)
        {
            _input = input;
            _ui = ui;
            _scenes = scenes;
            _noteOpen = noteOpen;
            Enabled = true;
            _toggle = GameSettings.Current.TouchToggle;
            if (Touchscreen.current == null && Application.isMobilePlatform)
                Debug.LogWarning("TouchControls: chưa thấy Touchscreen, sẽ đọc khi có.");
            if (ui.TouchLayer == null)
                return;

            _root = Ui.Box("hbt-touch", ui.TouchLayer);
            _stickBase = Ui.Box("hbt-touch-stick", _root);
            _stickKnob = Ui.Box("hbt-touch-knob", _stickBase);
            Ui.Show(_stickBase, false);

            string S(string k) => content.GetString(k);
            // Cụm phải dưới: Dùng to nhất ở góc, Làm và Giơ tay quanh nó, Ngồi phía trên.
            _use = Add(S("touch.use"), "hbt-touch-use", right: 70, bottom: 70, hold: true, gameplay: true, press: null);
            _use.State = _input.Touch.UseHold;
            Add(S("touch.interact"), "hbt-touch-act", right: 300, bottom: 60, hold: false, gameplay: true,
                press: () => _input.Touch.Press(TouchButton.Interact));
            _raise = Add(S("touch.raise"), "hbt-touch-small", right: 80, bottom: 300, hold: true, gameplay: false, press: null);
            _raise.State = _input.Touch.RaiseHold;
            Add(S("touch.sit"), "hbt-touch-small", right: 300, bottom: 250, hold: false, gameplay: true,
                press: () => _input.Touch.Press(TouchButton.Sit));
            // Hàng trên phải: sổ, tạm dừng, ghi chú debug.
            // Hàng trên phải: sổ, ghi chú debug. Tạm dừng: nút ‖ ở góc trái trên.
            // Lề phải ≥ 5% bề ngang (vùng an toàn của tai thỏ/bo góc, UiOverflowTests).
            Add(S("touch.notebook"), "hbt-touch-top", right: 240, top: 40, hold: false, gameplay: false,
                press: () => _input.Touch.Press(TouchButton.Notebook));
            Add(S("touch.note"), "hbt-touch-top", right: 100, top: 40, hold: false, gameplay: false, press: openNote).InMenu = true;
            var pause = Add(null, "hbt-touch-pause", right: -1, top: 30, left: 40, hold: false, gameplay: false,
                press: () => _input.Touch.Press(TouchButton.Pause));
            Ui.Box("hbt-touch-pause-bar", pause.Element);
            Ui.Box("hbt-touch-pause-bar", pause.Element);
        }

        TouchButtonView Add(string label, string cls, float right, float bottom = -1, float top = -1, bool hold = false,
            bool gameplay = true, Action press = null, float left = -1)
        {
            var e = Ui.Box("hbt-touch-button", _root);
            e.AddToClassList(cls);
            e.pickingMode = PickingMode.Position;
            if (right >= 0) e.style.right = right;
            if (left >= 0) e.style.left = left;
            if (bottom >= 0) e.style.bottom = bottom;
            if (top >= 0) e.style.top = top;
            if (label != null)
                Ui.Text("hbt-touch-label", e, label);
            var b = new TouchButtonView { Element = e, Press = press, Hold = hold, Gameplay = gameplay };
            _buttons.Add(b);
            return b;
        }

        void Update()
        {
            if (_input == null)
                return;
            UpdateVisibility();
            var ts = Touchscreen.current;
            if (ts == null || _root == null)
            {
                ClearTouches();
                return;
            }

            var panel = _root.panel;
            foreach (var t in ts.touches)
            {
                int id = t.touchId.ReadValue();
                if (id == 0 || !t.press.isPressed)
                    continue;
                if (!_fingers.TryGetValue(id, out var f))
                {
                    f = Begin(t, panel);
                    _fingers[id] = f;
                }
                Track(id, f, t, panel);
            }

            _gone.Clear();
            foreach (var kv in _fingers)
            {
                if (!StillDown(ts, kv.Key))
                    _gone.Add(kv.Key);
            }
            foreach (int id in _gone)
            {
                End(id, _fingers[id]);
                _fingers.Remove(id);
            }

            _input.Touch.Use = _use != null && _use.State.Held(_toggle);
            _input.Touch.Raise = _raise != null && _raise.State.Held(_toggle);
            if (_use != null) Highlight(_use, _input.Touch.Use);
            if (_raise != null) Highlight(_raise, _input.Touch.Raise);
        }

        static bool StillDown(Touchscreen ts, int id)
        {
            foreach (var t in ts.touches)
            {
                if (t.touchId.ReadValue() == id && t.press.isPressed)
                    return true;
            }
            return false;
        }

        Vector2 ToPanel(IPanel panel, Vector2 screen)
        {
            if (panel == null)
                return screen;
            return RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
        }

        Finger Begin(TouchControl t, IPanel panel)
        {
            var screen = t.position.ReadValue();
            var p = ToPanel(panel, screen);
            var f = new Finger { Start = p, Last = p, StartTime = Time.unscaledTime };
            var picked = panel?.Pick(p);
            var button = FindButton(picked);
            if (button != null && Ui.IsShown(button.Element))
            {
                f.Role = Role.Button;
                f.Button = button;
                button.Down++;
                button.State?.Press(_toggle);
                Highlight(button, button.State != null ? button.State.Held(_toggle) : true);
                if (!button.Hold)
                    button.Press?.Invoke();
                return f;
            }
            // Ngón rơi vào UI khác (sổ, menu, nút tạm dừng): để UI Toolkit tự xử lý.
            if (picked != null || !_input.GameplayEnabled)
            {
                f.Role = Role.Ui;
                return f;
            }
            float width = panel != null ? _root.panel.visualTree.layout.width : Screen.width;
            if (p.x < width * StickZone && _stickId < 0)
            {
                f.Role = Role.Stick;
                _stickId = t.touchId.ReadValue();
                _stickBase.style.left = p.x - StickRadius;
                _stickBase.style.top = p.y - StickRadius;
                _stickKnob.style.translate = new Translate(0, 0);
                Ui.Show(_stickBase, true);
            }
            else
            {
                f.Role = Role.Look;
            }
            return f;
        }

        void Track(int id, Finger f, TouchControl t, IPanel panel)
        {
            var p = ToPanel(panel, t.position.ReadValue());
            var d = p - f.Last;
            f.Last = p;
            if ((p - f.Start).sqrMagnitude > TapSlop * TapSlop)
                f.Moved = true;
            switch (f.Role)
            {
                case Role.Stick:
                    var off = Vector2.ClampMagnitude(p - f.Start, StickRadius);
                    _stickKnob.style.translate = new Translate(off.x, off.y);
                    _input.Touch.Move = new Vector2(off.x, -off.y) / StickRadius;
                    break;
                case Role.Look:
                    // Panel: y hướng xuống; chuột: y hướng lên.
                    _input.Touch.AddLook(new Vector2(d.x, -d.y) * LookScale);
                    break;
                case Role.Button:
                    // Giữ nút rồi rê: ngón đó nhìn/cào/quét luôn (sau khi rời khỏi vùng run tay của cú chạm).
                    if (f.Moved && (f.Button.Hold || f.Button.Gameplay) && _input.GameplayEnabled)
                        _input.Touch.AddLook(new Vector2(d.x, -d.y) * LookScale);
                    break;
            }
        }

        void End(int id, Finger f)
        {
            switch (f.Role)
            {
                case Role.Stick:
                    _stickId = -1;
                    _input.Touch.Move = Vector2.zero;
                    Ui.Show(_stickBase, false);
                    break;
                case Role.Look:
                    if (!f.Moved && Time.unscaledTime - f.StartTime < TapSeconds)
                        _input.Touch.Press(TouchButton.Interact);
                    break;
                case Role.Button:
                    f.Button.Down = Mathf.Max(0, f.Button.Down - 1);
                    f.Button.State?.Release();
                    Highlight(f.Button, f.Button.State != null ? f.Button.State.Held(_toggle) : f.Button.Down > 0);
                    break;
            }
        }

        TouchButtonView FindButton(VisualElement picked)
        {
            for (var e = picked; e != null; e = e.parent)
            {
                foreach (var b in _buttons)
                {
                    if (b.Element == e)
                        return b;
                }
            }
            return null;
        }

        static void Highlight(TouchButtonView b, bool on)
        {
            if (on) b.Element.AddToClassList("hbt-touch-down");
            else b.Element.RemoveFromClassList("hbt-touch-down");
        }

        void UpdateVisibility()
        {
            if (_root == null)
                return;
            if (_toggle != GameSettings.Current.TouchToggle)
            {
                ClearTouches();
                _toggle = GameSettings.Current.TouchToggle;
            }
            bool menu = _scenes != null && _scenes.CurrentScene == SceneFlow.MenuScene;
            bool note = _noteOpen != null && _noteOpen();
            bool paused = _input.IsBlockedBy("pause") || (_ui.Settings != null && _ui.Settings.Visible);
            // Màn đen chuyển cảnh và bản đồ hải trình: không có gì để bấm, ẩn cả cụm nút (QA-0141).
            bool blackout = _ui.FadeAlpha > 0.5f || (_ui.Map != null && _ui.Map.Visible);
            bool notebook = _input.IsBlockedBy("notebook");
            if (menu || (!_input.GameplayEnabled && !_input.OnlyOverlays))
                _use.State.Clear();
            if (menu || paused || note || notebook || blackout)
                _raise.State.Clear();
            Ui.Show(_root, !note && !blackout);
            foreach (var b in _buttons)
            {
                bool show;
                if (menu)
                    show = b.InMenu;
                else if (b == _raise)
                    show = !paused && !notebook;
                else
                    show = !b.Gameplay || _input.GameplayEnabled;
                if (show != Ui.IsShown(b.Element))
                    Ui.Show(b.Element, show);
                if (!show && b.Down > 0)
                {
                    b.Down = 0;
                    Highlight(b, false);
                }
            }
        }

        void ClearTouches()
        {
            _fingers.Clear();
            _stickId = -1;
            _input?.Touch.Clear();
            foreach (var b in _buttons)
            {
                b.Down = 0;
                b.State?.Clear();
                Highlight(b, false);
            }
            Ui.Show(_stickBase, false);
        }

        void OnApplicationFocus(bool focused) { if (!focused) ClearTouches(); }
        void OnApplicationPause(bool paused) { if (paused) ClearTouches(); }
        void OnDisable() => ClearTouches();

        void OnDestroy()
        {
            Enabled = false;
        }
    }
}
