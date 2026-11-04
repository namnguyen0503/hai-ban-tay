using UnityEngine;
using UnityEngine.UIElements;

namespace HaiBanTay.UI
{
    /// <summary>Tâm ngắm, prompt thao tác, vòng tiến độ và gợi ý. Prompt "cảnh" (do director đặt) ưu tiên hơn prompt vật thể.</summary>
    public sealed class PromptView
    {
        readonly VisualElement _crosshair;
        readonly Label _prompt;
        readonly ProgressRing _ring;
        readonly Label _hint;
        string _world;
        string _scene;
        float _hintUntil;
        bool _suppressed;
        public bool HintVisible => _hintUntil > 0f && !_suppressed;
        public void SetSuppressed(bool value)
        {
            _suppressed = value;
            Ui.Show(_hint, HintVisible);
        }

        public PromptView(VisualElement parent)
        {
            _crosshair = Ui.Box("hbt-crosshair", parent);
            _ring = new ProgressRing();
            parent.Add(_ring);
            _prompt = Ui.Text("hbt-prompt", parent);
            _hint = Ui.Text("hbt-hint", parent);
            SetProgress(null);
            Refresh();
            Ui.Show(_hint, false);
        }

        public void SetWorldPrompt(string text)
        {
            _world = text;
            Refresh();
        }

        public void SetScenePrompt(string text)
        {
            _scene = text;
            Refresh();
        }

        /// <summary>Giá trị vòng tiến độ đang hiện (null = ẩn).</summary>
        public float? Progress { get; private set; }

        /// <summary>null = ẩn vòng.</summary>
        public void SetProgress(float? value)
        {
            Progress = value;
            Ui.Show(_ring, value.HasValue);
            if (value.HasValue)
                _ring.Value = value.Value;
        }

        public void SetCrosshair(bool visible)
        {
            Ui.Show(_crosshair, visible);
        }

        public void ShowHint(string text, float seconds)
        {
            _hint.text = InputGlyphs.Apply(text);
            Ui.Show(_hint, !_suppressed && !string.IsNullOrEmpty(text));
            _hintUntil = Time.unscaledTime + seconds;
        }

        public void HideHint()
        {
            _hintUntil = 0f;
            Ui.Show(_hint, false);
        }

        public void Tick()
        {
            if (_suppressed && _hintUntil > 0f) _hintUntil += Time.unscaledDeltaTime;
            if (!_suppressed && _hintUntil > 0f && Time.unscaledTime > _hintUntil)
            {
                _hintUntil = 0f;
                Ui.Show(_hint, false);
            }
        }

        public void Clear()
        {
            _world = _scene = null;
            SetProgress(null);
            HideHint();
            Refresh();
        }

        void Refresh()
        {
            var text = !string.IsNullOrEmpty(_scene) ? _scene : _world;
            _prompt.text = InputGlyphs.Apply(text) ?? string.Empty;
            // Prompt thao tác phải đọc được ngay cả khi thẻ/toast đang chờ; chỉ xếp hàng gợi ý dài.
            Ui.Show(_prompt, !string.IsNullOrEmpty(text));
        }
    }
}
