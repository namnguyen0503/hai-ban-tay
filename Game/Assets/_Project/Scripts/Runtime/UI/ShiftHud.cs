using System;
using System.Collections.Generic;
using HaiBanTay.Shift;
using UnityEngine;
using UnityEngine.UIElements;

namespace HaiBanTay.UI
{
    /// <summary>HUD ca làm (góc trái, trong vùng an toàn): ngày, đồng hồ, các lệnh của lượt hiện tại kèm biểu tượng
    /// và số lượng. Nhãn lệnh có thể còn chữ Pháp chưa học (Vocabulary). Thanh thăng bằng (giữa dưới) khi vác bao lên
    /// cầu thang lúc tàu lắc. Mũi tên chỉ hướng (giữa trên) tới chỗ việc đang chờ khi người chơi ở xa.</summary>
    public sealed class ShiftHud
    {
        readonly VisualElement _root;
        readonly Label _day;
        readonly Label _clock;
        readonly VisualElement _list;
        readonly VisualElement _balance;
        readonly VisualElement _balanceMark;
        readonly VisualElement _pointer;
        readonly VisualElement _pointerArrow;
        readonly Label _pointerLabel;
        string _clockText;
        string _pointerText;

        public bool Visible { get; private set; }
        public VisualElement Root => _root;

        public ShiftHud(VisualElement parent)
        {
            _root = Ui.Box("hbt-shift", parent);
            var head = Ui.Box("hbt-shift-head", _root);
            _clock = Ui.Text("hbt-shift-clock", head);
            _day = Ui.Text("hbt-shift-day", head);
            _list = Ui.Box("hbt-shift-orders", _root);
            _balance = Ui.Box("hbt-balance", parent);
            Ui.Box("hbt-balance-center", _balance);
            _balanceMark = Ui.Box("hbt-balance-mark", _balance);
            Ui.Show(_balance, false);
            _pointer = Ui.Box("hbt-pointer", parent);
            _pointerArrow = Ui.Box("hbt-pointer-arrow", _pointer);
            _pointerArrow.generateVisualContent += DrawArrow;
            _pointerLabel = Ui.Text("hbt-pointer-label", _pointer);
            Ui.Show(_pointer, false);
            Show(false);
        }

        public void Show(bool visible)
        {
            Visible = visible;
            Ui.Show(_root, visible);
        }

        public void SetDay(string text)
        {
            _day.text = text ?? string.Empty;
        }

        public void SetClock(string text)
        {
            if (text == _clockText)
                return;
            _clockText = text;
            _clock.text = text;
        }

        public bool BalanceVisible { get; private set; }

        /// <summary>Độ nghiêng của bao -1…1; null = ẩn thanh.</summary>
        public void SetBalance(float? lean)
        {
            bool show = lean.HasValue;
            if (show != BalanceVisible)
            {
                BalanceVisible = show;
                Ui.Show(_balance, show);
            }
            if (!show)
                return;
            float k = Mathf.Clamp(lean.Value, -1f, 1f);
            _balanceMark.style.left = Length.Percent(50f + 50f * k);
            _balanceMark.EnableInClassList("hbt-balance-mark--danger", Mathf.Abs(k) > 0.65f);
        }

        public bool PointerVisible { get; private set; }
        public string PointerText => _pointerText;
        /// <summary>Góc mũi tên (độ, 0 = thẳng trước mặt, dương = bên phải).</summary>
        public float PointerAngle { get; private set; }

        /// <summary>Mũi tên chỉ hướng: angle là góc lệch so với hướng nhìn; text null = ẩn.</summary>
        public void SetPointer(string text, float angle)
        {
            bool show = !string.IsNullOrEmpty(text);
            if (show != PointerVisible)
            {
                PointerVisible = show;
                Ui.Show(_pointer, show);
            }
            if (!show)
                return;
            if (text != _pointerText)
            {
                _pointerText = text;
                _pointerLabel.text = text;
            }
            if (Mathf.Abs(Mathf.DeltaAngle(PointerAngle, angle)) > 0.5f)
            {
                PointerAngle = angle;
                _pointerArrow.style.rotate = new Rotate(new Angle(angle, AngleUnit.Degree));
            }
        }

        static void DrawArrow(MeshGenerationContext ctx)
        {
            var r = ctx.visualElement.contentRect;
            float w = r.width, h = r.height;
            if (w <= 0f || h <= 0f)
                return;
            var p = ctx.painter2D;
            p.fillColor = new Color(0.96f, 0.91f, 0.8f, 0.95f);
            p.strokeColor = new Color(0.1f, 0.07f, 0.05f, 0.8f);
            p.lineWidth = 2f;
            Vector2 P(float x, float y) => new Vector2(r.x + x * w, r.y + y * h);
            p.BeginPath();
            p.MoveTo(P(0.5f, 0.04f));
            p.LineTo(P(0.9f, 0.6f));
            p.LineTo(P(0.62f, 0.6f));
            p.LineTo(P(0.62f, 0.96f));
            p.LineTo(P(0.38f, 0.96f));
            p.LineTo(P(0.38f, 0.6f));
            p.LineTo(P(0.1f, 0.6f));
            p.ClosePath();
            p.Fill();
            p.Stroke();
        }

        public string ClockText => _clockText;
        public int RowCount => _list.childCount;

        /// <summary>Số dòng lệnh tối đa hiện cùng lúc (kể cả dòng "+K việc khác"): nhãn dài có thể xuống hai dòng, sáu dòng
        /// vẫn nằm trong vùng an toàn 1080p.</summary>
        public const int MaxRows = 6;

        /// <summary>Định dạng dòng thu gọn ({0} = số việc ẩn); ShiftDirector gán từ ui_strings (hud.more_orders).</summary>
        public string MoreFormat { get; set; } = "+{0}";

        /// <summary>Chọn lệnh hiện ra: ưu tiên việc chưa xong (giữ thứ tự lịch), việc đã xong xếp sau. Nếu quá maxRows thì
        /// chừa một dòng cho "+K việc khác". hidden = số lệnh bị ẩn.</summary>
        public static List<Order> SelectVisible(IReadOnlyList<Order> orders, int maxRows, out int hidden)
        {
            var sorted = new List<Order>(orders.Count);
            foreach (var o in orders)
                if (!o.IsDone)
                    sorted.Add(o);
            foreach (var o in orders)
                if (o.IsDone)
                    sorted.Add(o);
            hidden = 0;
            if (sorted.Count <= maxRows)
                return sorted;
            int keep = Mathf.Max(0, maxRows - 1);
            hidden = sorted.Count - keep;
            return sorted.GetRange(0, keep);
        }

        /// <summary>Dựng lại danh sách lệnh (gọi khi bảng việc đổi, không gọi mỗi khung).</summary>
        public void SetOrders(IReadOnlyList<Order> orders, Func<Order, string> label)
        {
            _list.Clear();
            var visible = SelectVisible(orders, MaxRows, out int hidden);
            foreach (var o in visible)
            {
                var row = Ui.Box("hbt-order", _list);
                if (o.IsDone)
                    row.AddToClassList("hbt-order--done");
                row.Add(new OrderIcon(o.Kind));
                var text = Ui.Text("hbt-order-label", row, label(o));
                text.enableRichText = true;
                Ui.Text("hbt-order-count", row, $"{o.Done}/{o.Need}");
            }
            if (hidden > 0)
            {
                var more = Ui.Box("hbt-order", _list);
                Ui.Text("hbt-order-label", more, string.Format(MoreFormat, hidden));
            }
        }

        public void Clear()
        {
            _list.Clear();
            _clockText = null;
            SetBalance(null);
            SetPointer(null, 0f);
            Show(false);
        }
    }
}
