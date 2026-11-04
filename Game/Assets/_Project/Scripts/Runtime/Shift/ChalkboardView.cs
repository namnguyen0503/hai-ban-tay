using System;
using System.Collections.Generic;
using HaiBanTay.Core;
using HaiBanTay.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Bảng phấn trên tường bếp: lệnh của lượt hiện tại viết bằng phấn (tiếng Pháp; từ đã học hiện nghĩa tiếng Việt).
    /// Vẽ bằng UI Toolkit vào RenderTexture riêng (WorldPanel).
    /// </summary>
    public sealed class ChalkboardView : MonoBehaviour
    {
        public Renderer surface;
        public int width = 1024;
        public int height = 640;
        public Color board = new Color(0.11f, 0.15f, 0.13f, 1f);
        public Color chalk = new Color(0.93f, 0.92f, 0.86f, 1f);
        [Tooltip("Độ tự sáng của chữ phấn (bếp tối vẫn đọc được).")]
        public float glow = 0.8f;

        WorldPanel _panel;
        Label _title;
        VisualElement _list;
        bool _ready;

        public bool Ready => _ready;
        public int LineCount => _list != null ? _list.childCount : 0;

        void Start()
        {
            _panel = WorldPanel.Create(transform, surface, width, height, board, glow, "Chalkboard");
            if (_panel == null)
                return;
            var rootElement = _panel.Root;
            rootElement.style.paddingLeft = rootElement.style.paddingRight = 40;
            rootElement.style.paddingTop = 30;
            _title = Ui.Text(null, rootElement);
            _title.style.fontSize = 72;
            _title.style.color = chalk;
            _title.style.marginBottom = 16;
            _list = Ui.Box(null, rootElement);
            _ready = true;
        }

        public void SetContent(string title, IReadOnlyList<Order> orders, Func<Order, string> label)
        {
            if (!_ready)
                return;
            _title.text = title ?? string.Empty;
            _list.Clear();
            foreach (var o in orders)
            {
                var row = Ui.Box(null, _list);
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 10;
                row.style.opacity = o.IsDone ? 0.35f : 1f;
                var icon = new OrderIcon(o.Kind, 68f) { Color = chalk };
                icon.style.marginRight = 20;
                icon.style.flexShrink = 0;
                row.Add(icon);
                var text = Ui.Text(null, row, $"{label(o)}  ×{o.Need}");
                text.enableRichText = true;
                text.style.fontSize = 60;
                text.style.color = chalk;
                text.style.whiteSpace = WhiteSpace.Normal;
                text.style.flexShrink = 1;
            }
            _panel.Redraw();
        }

        void OnDestroy()
        {
            _panel?.Dispose();
        }
    }
}
