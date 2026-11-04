using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace HaiBanTay.UI
{
    /// <summary>Thông báo ngắn góc trên trái (trang mới trong sổ).</summary>
    public sealed class ToastView
    {
        const float Seconds = 4f;
        readonly Label _label;
        float _until;
        bool _suppressed;
        readonly Queue<string> _queue = new Queue<string>();
        public bool Pending => _until > 0f || _queue.Count > 0;
        public bool Visible => _until > 0f && !_suppressed;
        public void SetSuppressed(bool value)
        {
            _suppressed = value;
            Ui.Show(_label, Visible);
        }

        public ToastView(VisualElement parent)
        {
            _label = Ui.Text("hbt-toast", parent);
            Ui.Show(_label, false);
        }

        public string Text => _label.text;

        public void Show(string text)
        {
            _queue.Enqueue(text);
            if (_until == 0f) Next();
        }

        void Next()
        {
            _until = 0f;
            if (_queue.Count > 0)
            {
                _label.text = _queue.Dequeue();
                _until = Time.unscaledTime + Seconds;
            }
            Ui.Show(_label, Visible);
        }

        public void Hide()
        {
            _queue.Clear();
            Ui.Show(_label, false);
            _until = 0f;
        }

        public void Tick()
        {
            if (_suppressed && _until > 0f) _until += Time.unscaledDeltaTime;
            if (!_suppressed && _until > 0f && Time.unscaledTime > _until)
                Next();
        }
    }
}
