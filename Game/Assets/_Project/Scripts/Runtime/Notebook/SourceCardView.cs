using System.Collections.Generic;
using HaiBanTay.Content;
using HaiBanTay.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace HaiBanTay.Notebook
{
    /// <summary>Thẻ tư liệu hiện góc trên phải, không chặn thao tác; nhiều thẻ thì xếp hàng đợi.</summary>
    public sealed class SourceCardView
    {
        public const float Seconds = 9f;

        readonly VisualElement _parent;
        readonly ContentDatabase _content;
        readonly Queue<SourceCard> _queue = new Queue<SourceCard>();
        VisualElement _current;
        float _until;
        bool _suppressed;
        public bool Pending => _current != null || _queue.Count > 0;
        public bool Visible => _current != null && !_suppressed;
        public void SetSuppressed(bool value)
        {
            _suppressed = value;
            if (_current != null) Ui.Show(_current, !value);
        }

        public string CurrentId { get; private set; }

        public SourceCardView(VisualElement parent, ContentDatabase content)
        {
            _parent = parent;
            _content = content;
        }

        public void Show(string cardId)
        {
            _queue.Enqueue(_content.GetCard(cardId));
            if (_current == null)
                Next();
        }

        void Next()
        {
            _current?.RemoveFromHierarchy();
            _current = null;
            CurrentId = null;
            if (_queue.Count == 0)
                return;
            var card = _queue.Dequeue();
            _current = Build(card, _content, inline: false);
            _parent.Add(_current);
            Ui.Show(_current, !_suppressed);
            CurrentId = card.Id;
            _until = Time.unscaledTime + Seconds;
        }

        public void Tick()
        {
            if (_suppressed && _current != null) _until += Time.unscaledDeltaTime;
            if (!_suppressed && _current != null && Time.unscaledTime > _until)
                Next();
        }

        public void Clear()
        {
            _queue.Clear();
            _current?.RemoveFromHierarchy();
            _current = null;
            CurrentId = null;
        }

        public static VisualElement Build(SourceCard card, ContentDatabase content, bool inline)
        {
            var box = Ui.Box("hbt-card");
            if (inline)
                box.AddToClassList("hbt-card--inline");
            content.SourceCards.Layers.TryGetValue(card.Layer, out var layerName);
            Ui.Text("hbt-card-layer", box, layerName ?? card.Layer);
            Ui.Text("hbt-card-title", box, card.Title);
            Ui.Text("hbt-card-body", box, card.Body);
            Ui.Text("hbt-card-citation", box, card.Citation);
            return box;
        }
    }
}
