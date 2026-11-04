using System;
using System.Collections;
using System.Collections.Generic;
using HaiBanTay.Core;
using HaiBanTay.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace HaiBanTay.Narrative
{
    /// <summary>Màn chữ trên nền đen: thẻ mở đầu, thẻ thời gian, câu người kể, thẻ mốc, tên game, credits.</summary>
    public sealed class OpeningCard
    {
        const float TextFade = 0.6f;

        readonly VisualElement _root;
        readonly VisualElement _content;
        readonly Label _date;
        readonly Label _text;
        readonly Label _title;
        readonly VisualElement _lines;
        readonly Label _footer;

        public bool Visible { get; private set; }
        public string CurrentText => _text.text;
        public string CurrentDate => _date.text;
        public string CurrentTitle => _title.text;

        public OpeningCard(VisualElement parent)
        {
            _root = Ui.Box("hbt-fullcard", parent);
            _content = Ui.Box(null, _root);
            _content.style.alignItems = Align.Center;
            _content.style.width = new Length(100f, LengthUnit.Percent);
            _date = Ui.Text("hbt-fullcard-date", _content);
            _text = Ui.Text("hbt-fullcard-text", _content);
            _title = Ui.Text("hbt-fullcard-title", _content);
            _lines = Ui.Box(null, _content);
            _lines.style.alignItems = Align.Center;
            _lines.style.width = new Length(100f, LengthUnit.Percent);
            _footer = Ui.Text("hbt-fullcard-small", _root);
            _footer.style.position = Position.Absolute;
            _footer.style.bottom = new Length(6f, LengthUnit.Percent);
            Hide();
        }

        void Prepare(string date, string text, string title, IEnumerable<string> lines)
        {
            Ui.Show(_root, true);
            Visible = true;
            _date.text = date ?? string.Empty;
            Ui.Show(_date, !string.IsNullOrEmpty(date));
            _text.text = text ?? string.Empty;
            Ui.Show(_text, !string.IsNullOrEmpty(text));
            _title.text = title ?? string.Empty;
            Ui.Show(_title, !string.IsNullOrEmpty(title));
            _lines.Clear();
            if (lines != null)
                foreach (var l in lines)
                    Ui.Text("hbt-fullcard-small", _lines, l);
            Ui.Show(_footer, false);
        }

        public IEnumerator Show(string text, float seconds)
        {
            Prepare(null, text, null, null);
            yield return Hold(seconds);
        }

        public IEnumerator ShowDated(string date, string text, float seconds)
        {
            Prepare(date, text, null, null);
            yield return Hold(seconds);
        }

        public IEnumerator ShowTitle(string title, float seconds)
        {
            Prepare(null, null, title, null);
            yield return Hold(seconds);
        }

        /// <summary>Danh sách dài (credits) chia thành nhiều trang vừa vùng an toàn; mỗi trang giữ ít nhất MinPageSeconds.</summary>
        public IEnumerator ShowLines(string heading, IEnumerable<string> lines, float seconds)
        {
            var pages = Paginate(lines);
            float each = Mathf.Max(seconds / pages.Count, pages.Count > 1 ? MinPageSeconds : 0f);
            foreach (var page in pages)
            {
                Prepare(null, heading, null, page);
                yield return Hold(each);
            }
        }

        const float MinPageSeconds = 5f;
        /// <summary>Sức chứa một trang tính theo dòng hiển thị ở cỡ chữ 30px, chừa chỗ cho tiêu đề (cỡ chữ 100%).</summary>
        const int LinesPerPage = 15;
        /// <summary>Số ký tự vừa một dòng ở bề rộng 80% màn 1080p (cỡ chữ 100%).</summary>
        const int CharsPerLine = 90;

        public static List<List<string>> Paginate(IEnumerable<string> lines)
        {
            var pages = new List<List<string>> { new List<string>() };
            // Cỡ chữ lớn thu panel lại: ít dòng và ít ký tự mỗi dòng hơn, nếu không tiêu đề và dòng cuối ra khỏi vùng an toàn.
            float scale = GameSettings.Current != null ? GameSettings.Current.UiScale : 1f;
            int perPage = Mathf.FloorToInt(LinesPerPage / scale);
            int perLine = Mathf.FloorToInt(CharsPerLine / scale);
            int used = 0;
            if (lines == null)
                return pages;
            foreach (var l in lines)
            {
                int rows = 1 + (l.Length - 1) / perLine;
                if (used + rows > perPage && pages[pages.Count - 1].Count > 0)
                {
                    pages.Add(new List<string>());
                    used = 0;
                }
                pages[pages.Count - 1].Add(l);
                used += rows;
            }
            return pages;
        }

        /// <summary>Giữ màn chữ không giới hạn (màn chờ cuối).</summary>
        public void ShowStatic(string text, string footer)
        {
            Prepare(null, text, null, null);
            _content.style.opacity = 1f;
            _footer.text = footer ?? string.Empty;
            Ui.Show(_footer, !string.IsNullOrEmpty(footer));
        }

        /// <summary>
        /// Màn chữ chờ người chơi (disclaimer): chữ hiện dần, rồi dòng gợi ý; tắt khi dismissed() trả true
        /// hoặc sau maxSeconds. Dùng thời gian thật nên không phụ thuộc timeScale. Lúc chữ đang hiện dần
        /// (TextFade) chưa nhận phím, để cú bấm nút "Chơi mới" không bỏ qua màn ngay.
        /// </summary>
        public IEnumerator ShowUntil(string text, string footer, Func<bool> dismissed, float maxSeconds)
        {
            Prepare(null, text, null, null);
            _content.style.opacity = 0f;
            for (float t = 0f; t < TextFade; t += Time.unscaledDeltaTime)
            {
                _content.style.opacity = t / TextFade;
                yield return null;
            }
            _content.style.opacity = 1f;
            _footer.text = footer ?? string.Empty;
            Ui.Show(_footer, !string.IsNullOrEmpty(footer));
            for (float shown = 0f; shown < maxSeconds && !dismissed(); shown += Time.unscaledDeltaTime)
                yield return null;
            Ui.Show(_footer, false);
            for (float t = 0f; t < TextFade; t += Time.unscaledDeltaTime)
            {
                _content.style.opacity = 1f - t / TextFade;
                yield return null;
            }
            _content.style.opacity = 0f;
        }

        /// <summary>Hiện nền đen ngay (chưa có chữ), dùng trước thẻ đầu tiên để không lộ cảnh.</summary>
        public void Blackout()
        {
            Prepare(null, null, null, null);
        }

        IEnumerator Hold(float seconds)
        {
            float fade = Mathf.Min(TextFade, seconds * 0.25f);
            for (float t = 0f; t < fade; t += Time.deltaTime)
            {
                _content.style.opacity = t / fade;
                yield return null;
            }
            _content.style.opacity = 1f;
            float hold = seconds - 2f * fade;
            if (hold > 0f)
                yield return new WaitForSeconds(hold);
            for (float t = 0f; t < fade; t += Time.deltaTime)
            {
                _content.style.opacity = 1f - t / fade;
                yield return null;
            }
            _content.style.opacity = 0f;
        }

        public void Hide()
        {
            Ui.Show(_root, false);
            Visible = false;
        }
    }
}
