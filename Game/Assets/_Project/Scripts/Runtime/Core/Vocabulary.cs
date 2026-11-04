using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HaiBanTay.Content;

namespace HaiBanTay.Core
{
    /// <summary>
    /// Chữ hiển thị nhắc tới từ tiếng Pháp bằng {w:id} (french_words.vi.json). Từ đã học hiện nghĩa tiếng Việt;
    /// từ chưa học giữ nguyên tiếng Pháp, in nghiêng. Lệnh của quản bếp dễ hiểu dần theo số từ học được buổi tối.
    /// </summary>
    public static class Vocabulary
    {
        static readonly Regex Token = new Regex(@"\{w:([a-z0-9_]+)\}", RegexOptions.Compiled);

        /// <summary>Các ID từ được nhắc trong chuỗi (theo thứ tự xuất hiện, có thể lặp).</summary>
        public static IEnumerable<string> TokensIn(string text)
        {
            if (string.IsNullOrEmpty(text))
                yield break;
            foreach (Match m in Token.Matches(text))
                yield return m.Groups[1].Value;
        }

        public static bool HasTokens(string text) => !string.IsNullOrEmpty(text) && Token.IsMatch(text);

        /// <summary>lookup trả null nếu không có từ (giữ nguyên token để dễ thấy lỗi).</summary>
        public static string Render(string text, Func<string, FrenchPair> lookup, Func<string, bool> learned)
        {
            if (!HasTokens(text))
                return text ?? string.Empty;
            return Token.Replace(text, m =>
            {
                var word = lookup(m.Groups[1].Value);
                if (word == null)
                    return m.Value;
                return learned(word.Id) ? word.Vi : "<i>" + word.Fr + "</i>";
            });
        }

        /// <summary>Chuỗi dài nhất có thể hiện ra (để kiểm tra tràn chữ): mỗi token lấy bản dài hơn giữa Pháp và Việt.</summary>
        public static string Longest(string text, Func<string, FrenchPair> lookup)
        {
            if (!HasTokens(text))
                return text ?? string.Empty;
            return Token.Replace(text, m =>
            {
                var word = lookup(m.Groups[1].Value);
                if (word == null)
                    return m.Value;
                return word.Fr.Length >= word.Vi.Length ? word.Fr : word.Vi;
            });
        }

        /// <summary>Render theo tiến trình của người chơi (GameRoot): từ đã học, hoặc cài đặt "luôn dịch đầy đủ".</summary>
        public static string Render(string text)
        {
            var root = GameRoot.Instance;
            if (root == null || root.Content == null)
                return text ?? string.Empty;
            return Render(text, id => root.Content.TryGetWord(id, out var w) ? w : null,
                id => GameSettings.Current.AlwaysTranslate || root.State.Knows(id));
        }
    }
}
