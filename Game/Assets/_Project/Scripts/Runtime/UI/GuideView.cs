using UnityEngine;
using UnityEngine.UIElements;

namespace HaiBanTay.UI
{
    /// <summary>
    /// Chỉ dẫn từng bước: dòng "việc tiếp theo" ở giữa trên và một mốc hình thoi đặt đúng chỗ cần tới trên màn hình (kèm
    /// khoảng cách). Đích nằm ngoài khung nhìn hoặc sau lưng thì mốc nép vào mép màn hình thành mũi tên chỉ hướng quay.
    /// Đang nhắm trúng đích thì mốc ẩn để không che prompt thao tác.
    /// </summary>
    public sealed class GuideView
    {
        static readonly Color Fill = new Color(0.96f, 0.8f, 0.42f, 0.95f);
        static readonly Color Outline = new Color(0.1f, 0.07f, 0.05f, 0.85f);
        // Mốc (rộng 120 px, chữ khoảng cách bên dưới) luôn nằm trong vùng an toàn 5% mép màn hình.
        const float EdgeX = 0.41f, EdgeY = 0.38f;

        readonly VisualElement _bar;
        readonly Label _text;
        readonly VisualElement _marker;
        readonly VisualElement _icon;
        readonly Label _distance;
        Transform _target;
        Vector3 _offset;
        bool _arrow;
        string _shownText;
        int _shownMetres = -1;
        bool _hidden;
        // Mốc tự đặt tính một lần cho mỗi đích (tìm component), không lặp lại mỗi lần chỉ dẫn đổi qua đổi lại.
        readonly System.Collections.Generic.Dictionary<Transform, Vector3> _autoOffsets =
            new System.Collections.Generic.Dictionary<Transform, Vector3>();

        public string Text => _shownText;
        public Transform Target => _target;
        public bool MarkerVisible { get; private set; }
        /// <summary>Mốc đang nép mép màn hình (đích ngoài khung nhìn).</summary>
        public bool MarkerAtEdge => MarkerVisible && _arrow;
        /// <summary>Định dạng khoảng cách ({0} = mét), gán từ ui_strings.</summary>
        public string DistanceFormat { get; set; } = "{0}";

        public GuideView(VisualElement parent)
        {
            _bar = Ui.Box("hbt-guide", parent);
            _text = Ui.Text("hbt-guide-text", _bar);
            _marker = Ui.Box("hbt-guide-marker", parent);
            _icon = Ui.Box("hbt-guide-icon", _marker);
            _icon.generateVisualContent += Draw;
            _distance = Ui.Text("hbt-guide-distance", _marker);
            Ui.Show(_bar, false);
            Ui.Show(_marker, false);
        }

        /// <summary>text null = ẩn dòng chỉ dẫn; target null = ẩn mốc. offset: dời mốc so với gốc đích (m, thế giới); bỏ trống thì
        /// tự đặt: giữa mặt trên vùng nhắm (collider) của vật, trên đầu nếu là người.</summary>
        public void Set(string text, Transform target, Vector3? offset = null)
        {
            if (text != _shownText)
            {
                _shownText = text;
                _text.text = InputGlyphs.Apply(text) ?? string.Empty;
                Ui.Show(_bar, !_hidden && !string.IsNullOrEmpty(text));
            }
            if (target != _target || offset.HasValue)
                _offset = offset ?? CachedOffset(target);
            _target = target;
            if (target == null)
                ShowMarker(false);
        }

        Vector3 CachedOffset(Transform t)
        {
            if (t == null)
                return Vector3.zero;
            if (!_autoOffsets.TryGetValue(t, out var o))
                _autoOffsets[t] = o = AutoOffset(t);
            return o;
        }

        static Vector3 AutoOffset(Transform t)
        {
            if (t == null)
                return Vector3.zero;
            if (t.GetComponentInParent<Npc.NpcAgent>() != null)
                return Vector3.up * 2f;
            var c = t.GetComponent<Collider>();
            if (c == null || !c.enabled || c.bounds.size.sqrMagnitude < 1e-6f)
                return Vector3.up * 0.15f;
            var b = c.bounds;
            return b.center + Vector3.up * Mathf.Min(b.extents.y, 0.3f) - t.position;
        }

        public void Clear()
        {
            Set(null, null);
            _autoOffsets.Clear();
        }

        /// <summary>Tạm ẩn cả dòng và mốc (phim, sổ, tạm dừng) mà không quên bước hiện tại.</summary>
        public void SetHidden(bool hidden)
        {
            if (hidden == _hidden)
                return;
            _hidden = hidden;
            Ui.Show(_bar, !hidden && !string.IsNullOrEmpty(_shownText));
            if (hidden)
                ShowMarker(false);
        }

        /// <summary>Gọi mỗi khung sau khi camera đã đặt. aimed: đang nhắm trúng đích (ẩn mốc).</summary>
        public void Tick(Camera cam, bool aimed)
        {
            if (_hidden || _target == null || cam == null || aimed || !_target.gameObject.activeInHierarchy || _marker.panel == null)
            {
                ShowMarker(false);
                return;
            }
            var world = _target.position + _offset;
            var vp = cam.WorldToViewportPoint(world);
            bool behind = vp.z < 0f;
            var d = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
            if (behind)
                d = -d;
            bool onScreen = !behind && Mathf.Abs(d.x) <= EdgeX && Mathf.Abs(d.y) <= EdgeY;
            if (!onScreen)
            {
                if (d.sqrMagnitude < 0.0001f)
                    d = Vector2.down;
                float k = 1f / Mathf.Max(Mathf.Abs(d.x) / EdgeX, Mathf.Abs(d.y) / EdgeY);
                d *= k;
            }
            var size = _marker.panel.visualTree.layout.size;
            if (size.x <= 0f || size.y <= 0f)
                return;
            ShowMarker(true);
            SetArrow(!onScreen);
            float x = (0.5f + d.x) * size.x, y = (0.5f - d.y) * size.y;
            // Lớp HUD thụt vào theo safe area (tai thỏ 140 px trên Android): đổi từ tọa độ panel sang tọa độ của lớp cha,
            // không thì mốc lệch phải đúng bằng phần thụt (QA-0130).
            var origin = _marker.parent != null ? _marker.parent.worldBound.position : Vector2.zero;
            if (float.IsNaN(origin.x) || float.IsNaN(origin.y))
                origin = Vector2.zero;
            _marker.style.left = x - origin.x;
            _marker.style.top = y - origin.y;
            // Mũi tên trỏ ra mép theo hướng cần quay (panel: y hướng xuống).
            _icon.style.rotate = new Rotate(new Angle(onScreen ? 0f : Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg, AngleUnit.Degree));
            int metres = Mathf.RoundToInt(Vector3.Distance(cam.transform.position, world));
            if (metres != _shownMetres)
            {
                _shownMetres = metres;
                _distance.text = string.Format(DistanceFormat, metres);
            }
        }

        void ShowMarker(bool on)
        {
            if (on == MarkerVisible)
                return;
            MarkerVisible = on;
            Ui.Show(_marker, on);
        }

        void SetArrow(bool arrow)
        {
            if (arrow == _arrow)
                return;
            _arrow = arrow;
            _marker.EnableInClassList("hbt-guide-marker--edge", arrow);
            _icon.MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = ctx.visualElement.contentRect;
            float w = r.width, h = r.height;
            if (w <= 0f || h <= 0f)
                return;
            var p = ctx.painter2D;
            p.fillColor = Fill;
            p.strokeColor = Outline;
            p.lineWidth = 3f;
            Vector2 P(float x, float y) => new Vector2(r.x + x * w, r.y + y * h);
            p.BeginPath();
            if (_arrow)
            {
                // Mũi tên đặc trỏ lên (xoay theo hướng bằng style.rotate).
                p.MoveTo(P(0.5f, 0.02f));
                p.LineTo(P(0.95f, 0.62f));
                p.LineTo(P(0.64f, 0.62f));
                p.LineTo(P(0.64f, 0.98f));
                p.LineTo(P(0.36f, 0.98f));
                p.LineTo(P(0.36f, 0.62f));
                p.LineTo(P(0.05f, 0.62f));
            }
            else
            {
                // Hình thoi rỗng ruột: thấy được vật phía sau.
                p.MoveTo(P(0.5f, 0.02f));
                p.LineTo(P(0.98f, 0.5f));
                p.LineTo(P(0.5f, 0.98f));
                p.LineTo(P(0.02f, 0.5f));
                p.ClosePath();
                p.MoveTo(P(0.5f, 0.3f));
                p.LineTo(P(0.3f, 0.5f));
                p.LineTo(P(0.5f, 0.7f));
                p.LineTo(P(0.7f, 0.5f));
            }
            p.ClosePath();
            p.Fill(FillRule.OddEven);
            p.Stroke();
        }
    }
}
