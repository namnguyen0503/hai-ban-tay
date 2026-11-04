using System;
using UnityEngine.UIElements;

namespace HaiBanTay.UI
{
    /// <summary>Menu tạm dừng: Tiếp tục / Cài đặt / Về menu chính / Thoát.</summary>
    public sealed class PauseMenu
    {
        readonly VisualElement _root;

        public bool Visible { get; private set; }
        public VisualElement Root => _root;

        public PauseMenu(VisualElement parent, (string label, Action action)[] items)
        {
            _root = new VisualElement();
            _root.AddToClassList("hbt-pause");
            parent.Add(_root);
            foreach (var (label, action) in items)
                Ui.Button(label, action, _root);
            Show(false);
        }

        public void Show(bool visible)
        {
            if (visible != Visible)
                Core.GameRoot.Instance?.Audio.PlaySfx(visible ? "SFX/ui_open" : "SFX/ui_close", 0.5f);
            Visible = visible;
            Ui.Show(_root, visible);
        }
    }
}
