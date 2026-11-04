using System;
using HaiBanTay.Content;
using UnityEngine.UIElements;

namespace HaiBanTay.UI
{
    /// <summary>Màn "Cách chơi": điều khiển theo thiết bị (bàn phím/chuột hoặc cảm ứng) và cách đọc chỉ dẫn trên màn hình.
    /// Tự hiện lần đầu vào cảnh chơi, mở lại từ menu tạm dừng.</summary>
    public sealed class HelpView
    {
        readonly VisualElement _root;
        readonly Label _body;
        readonly ContentDatabase _content;
        Action _onClose;

        public bool Visible { get; private set; }
        public VisualElement Root => _root;

        public HelpView(VisualElement parent, ContentDatabase content)
        {
            _content = content;
            _root = Ui.Box("hbt-pause", parent);
            _root.pickingMode = PickingMode.Position;
            var panel = Ui.Box("hbt-settings", _root);
            Ui.Text("hbt-settings-title", panel, content.GetString("help.title"));
            _body = Ui.Text("hbt-help-body", panel);
            var footer = Ui.Box("hbt-settings-footer", panel);
            Ui.Button(content.GetString("help.close"), Close, footer);
            Ui.Show(_root, false);
        }

        public void Open(Action onClose)
        {
            _onClose = onClose;
            _body.text = _content.GetString(TouchControls.Enabled ? "help.touch" : "help.pc");
            Visible = true;
            Ui.Show(_root, true);
        }

        public void Close()
        {
            if (!Visible)
                return;
            Visible = false;
            Ui.Show(_root, false);
            var done = _onClose;
            _onClose = null;
            done?.Invoke();
        }
    }
}
