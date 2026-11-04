using System.Collections;
using System.Collections.Generic;
using HaiBanTay.Content;
using HaiBanTay.Core;
using UnityEngine;

namespace HaiBanTay.Ending
{
    /// <summary>E6: câu người kể, các thẻ mốc, tên game, nguồn tư liệu + credits + dòng disclaimer, màn chờ cuối.</summary>
    public static class EndCards
    {
        /// <summary>Trích dẫn duy nhất từ source_cards (giữ thứ tự) — danh sách nguồn ở credits.</summary>
        public static List<string> SourceLines(ContentDatabase content)
        {
            var seen = new HashSet<string>();
            var lines = new List<string>();
            foreach (var card in content.SourceCards.Cards)
            {
                if (string.IsNullOrEmpty(card.Citation) || !seen.Add(card.Citation))
                    continue;
                lines.Add(card.Citation);
            }
            return lines;
        }

        public static List<string> AssetLines(TextAsset credits)
        {
            var lines = new List<string>();
            if (credits == null)
                return lines;
            foreach (var l in credits.text.Split('\n'))
                if (!string.IsNullOrWhiteSpace(l))
                    lines.Add(l.Trim());
            return lines;
        }

        public static IEnumerator Play(GameRoot root)
        {
            var e = root.Content.Ending;
            var card = root.UI.Card;
            card.Blackout();
            root.Events.Raise(EventIds.S3MaiRecalled);
            for (int i = 0; i < 7; i++)
                yield return card.Show(root.Content.GetString("end.mai." + i) + "\n\n" + root.Content.GetString("end.mai.source"), e.Timing("maiRecallCard", 5f));
            yield return card.Show(e.Narrator, e.Timing("narrator", 5f));
            foreach (var c in e.EndCards)
                yield return card.ShowDated(c.Date, c.Text, e.Timing("endCard", 5f));
            yield return card.ShowTitle(e.Title, e.Timing("title", 4f));
            float credits = e.Timing("credits", 10f);
            yield return card.ShowLines(root.Content.GetString("credits.sources"), SourceLines(root.Content), credits * 0.6f);
            yield return card.ShowLines(root.Content.GetString("credits.assets"),
                AssetLines(root.Config != null ? root.Config.credits : null), credits * 0.4f);
            yield return card.Show(root.Content.GetString("credits.disclaimer"), e.Timing("disclaimer", 5f));
            card.ShowStatic(e.Title, UI.InputGlyphs.Apply(root.Content.GetString("end.quit_hint")));
        }
    }
}
