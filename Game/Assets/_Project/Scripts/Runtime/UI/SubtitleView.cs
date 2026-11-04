using HaiBanTay.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HaiBanTay.UI
{
    /// <summary>Phụ đề: tên người nói, câu tiếng Pháp (nghiêng, nhỏ) và câu tiếng Việt (>= 40 px). Câu tiếng Việt có thể
    /// còn từ Pháp chưa học (in nghiêng, Core.Vocabulary). Cỡ chữ nhân theo cài đặt.</summary>
    public sealed class SubtitleView
    {
        readonly VisualElement _box;
        readonly Label _speaker;
        readonly Label _fr;
        readonly Label _vi;

        public SubtitleView(VisualElement parent)
        {
            _box = Ui.Box("hbt-subtitles", parent);
            _speaker = Ui.Text("hbt-subtitle-speaker", _box);
            _fr = Ui.Text("hbt-subtitle-fr", _box);
            _vi = Ui.Text("hbt-subtitle-text", _box);
            _vi.enableRichText = true;
            Hide();
        }

        public string Text => _vi.text;
        public VisualElement Root => _box;

        public void ApplySettings(GameSettings settings)
        {
            SetScale(settings.SubtitleScale);
            _box.style.backgroundColor = new Color(0f, 0f, 0f, settings.SubtitleBackground ? settings.SubtitleOpacity : 0f);
        }

        /// <summary>Cỡ chữ phụ đề theo cài đặt (1 = cỡ theme: 44 px tiếng Việt).</summary>
        public void SetScale(float scale)
        {
            _speaker.style.fontSize = 30f * scale;
            _fr.style.fontSize = 30f * scale;
            _vi.style.fontSize = 44f * scale;
        }

        public bool Visible { get; private set; }

        public void Show(string speaker, string fr, string vi)
        {
            _speaker.text = speaker ?? string.Empty;
            _fr.text = fr ?? string.Empty;
            _vi.text = vi ?? string.Empty;
            Ui.Show(_fr, !string.IsNullOrEmpty(fr));
            Ui.Show(_box, true);
            Visible = true;
        }

        public void Hide()
        {
            Ui.Show(_box, false);
            Visible = false;
        }
    }
}
