using System;
using System.Collections.Generic;
using HaiBanTay.Content;
using HaiBanTay.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HaiBanTay.UI
{
    /// <summary>Menu chính và danh sách chọn chương (cảnh 00_Menu). Chỉ dựng giao diện; hành động do MenuDirector truyền vào.</summary>
    public sealed class MainMenuView
    {
        readonly VisualElement _root;
        readonly VisualElement _buttons;
        readonly Label _title;
        readonly Label _subtitle;
        readonly Label _version;
        readonly List<Action> _actions = new List<Action>();

        public bool Visible { get; private set; }
        public VisualElement Root => _root;
        public int ButtonCount => _buttons.childCount;
        public string VersionText => _version.text;

        public MainMenuView(VisualElement parent, ContentDatabase content)
        {
            _root = Ui.Box("hbt-menu", parent);
            _root.pickingMode = PickingMode.Position;
            var column = Ui.Box("hbt-menu-column", _root);
            column.pickingMode = PickingMode.Position;
            _title = Ui.Text("hbt-menu-title", column);
            _subtitle = Ui.Text("hbt-menu-subtitle", column);
            _buttons = Ui.Box("hbt-menu-buttons", column);
            _buttons.pickingMode = PickingMode.Position;
            _version = Ui.Text("hbt-menu-version", column,
                string.Format(content.GetString("menu.version"), Application.version, BuildInfo.Id));
            Hide();
        }

        public void SetTitle(string title, string subtitle)
        {
            _title.text = title ?? string.Empty;
            _subtitle.text = subtitle ?? string.Empty;
            Ui.Show(_subtitle, !string.IsNullOrEmpty(subtitle));
        }

        /// <summary>Hiện danh sách nút (label, hành động). Nút có hành động null bị làm mờ.</summary>
        public void ShowButtons(IEnumerable<(string label, Action action)> items)
        {
            _buttons.Clear();
            _actions.Clear();
            foreach (var (label, action) in items)
            {
                var b = Ui.Button(label, () => action?.Invoke(), _buttons, "hbt-menu-button");
                b.SetEnabled(action != null);
                _actions.Add(action);
            }
            // Danh sách dài (chọn chương: sáu ngày trên tàu): chữ nhỏ hơn để cả cột nằm trong vùng an toàn.
            _buttons.EnableInClassList("hbt-menu-buttons--compact", _actions.Count > 6);
            _root.EnableInClassList("hbt-menu--compact", _actions.Count > 6);
            if (!Visible)
                Core.GameRoot.Instance?.Audio.PlaySfx("SFX/ui_open", 0.5f);
            Visible = true;
            Ui.Show(_root, true);
        }

        /// <summary>Bấm nút thứ i (test, Sim).</summary>
        public void Click(int index)
        {
            if (index >= 0 && index < _actions.Count)
                _actions[index]?.Invoke();
        }

        public void Hide()
        {
            if (Visible)
                Core.GameRoot.Instance?.Audio.PlaySfx("SFX/ui_close", 0.5f);
            Visible = false;
            Ui.Show(_root, false);
        }
    }
}
