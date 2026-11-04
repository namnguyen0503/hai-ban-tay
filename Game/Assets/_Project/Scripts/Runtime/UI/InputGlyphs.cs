using System.Collections.Generic;

namespace HaiBanTay.UI
{
    /// <summary>
    /// Đổi tên phím trong câu hiện lên màn hình theo thiết bị: trên điện thoại "[E]" thành "[Làm]", "chuột trái" thành nút
    /// "Dùng"… Bảng đổi nằm ở ui_strings (touch.glyph_map, mỗi dòng "chữ PC|chữ cảm ứng"), xét theo thứ tự dòng.
    /// </summary>
    public static class InputGlyphs
    {
        static List<(string from, string to)> _map;
        static List<(string from, string to)> _toggleMap;

        /// <summary>Bật thay chữ (mặc định theo TouchControls.Enabled; test gán thẳng).</summary>
        public static bool? Override { get; set; }

        static bool Touch => Override ?? TouchControls.Enabled;

        public static string Apply(string text)
        {
            if (string.IsNullOrEmpty(text) || !Touch)
                return text;
            var map = Map();
            for (int i = 0; i < map.Count; i++)
                if (text.IndexOf(map[i].from, System.StringComparison.Ordinal) >= 0)
                    text = text.Replace(map[i].from, map[i].to);
            if (Core.GameSettings.Current.TouchToggle)
            {
                _toggleMap ??= ReadMap("touch.toggle_glyph_map");
                foreach (var pair in _toggleMap)
                    text = text.Replace(pair.from, pair.to);
            }
            return text;
        }

        static List<(string from, string to)> Map()
        {
            if (_map != null)
                return _map;
            return _map = ReadMap("touch.glyph_map");
        }

        static List<(string from, string to)> ReadMap(string key)
        {
            var map = new List<(string from, string to)>();
            var content = Core.GameRoot.Instance != null ? Core.GameRoot.Instance.Content : null;
            string raw = content != null ? content.GetString(key) : null;
            if (string.IsNullOrEmpty(raw) || raw == key)
                return map;
            foreach (var line in raw.Split('\n'))
            {
                int bar = line.IndexOf('|');
                if (bar > 0)
                    map.Add((line.Substring(0, bar), line.Substring(bar + 1)));
            }
            return map;
        }
    }
}
