using System.Collections.Generic;
using HaiBanTay.Content;
using HaiBanTay.Core;
using UnityEngine;

namespace HaiBanTay.Tasks
{
    /// <summary>
    /// Lên bài học buổi tối (F07a: tối đọc, viết tới 11 giờ hoặc nửa đêm). Tan ca đúng giờ thì buổi tối bắt đầu 22:00 và học
    /// đủ số mục của bài; tan ca muộn thì mỗi MinutesPerItem phút muộn bớt một mục (ít nhất một). Từ còn nợ từ tối trước
    /// (GameState.PendingWords) được ôn trước bằng ghép từ; từ của các mục bị bớt dồn sang tối sau, không mất.
    /// </summary>
    public static class StudyPlanner
    {
        /// <summary>F04: làm tới 9 giờ tối.</summary>
        public const int ShiftEnd = 21 * 60;
        /// <summary>Buổi tối bắt đầu sau tan ca một tiếng.</summary>
        public const int EveningGap = 60;
        public const int MinutesPerItem = 20;
        /// <summary>Mỗi lượt phục vụ trễ làm ca kết thúc muộn thêm chừng này phút.</summary>
        public const int LatePenalty = 20;

        public sealed class Plan
        {
            public string Kind = EveningData.Rest;
            public string Heading;
            /// <summary>Từ nợ từ tối trước, ôn bằng ghép từ trước bài chính.</summary>
            public readonly List<FrenchPair> Review = new List<FrenchPair>();
            /// <summary>match: các cặp từ của bài chính.</summary>
            public readonly List<FrenchPair> Pairs = new List<FrenchPair>();
            /// <summary>trace, fill, sentence: các mục của bài chính.</summary>
            public readonly List<StudyItem> Items = new List<StudyItem>();
            /// <summary>Từ của các mục bị bớt (dồn sang tối sau).</summary>
            public readonly List<string> Deferred = new List<string>();
            public int Budget;

            public int MainCount => Kind == EveningData.Match ? Pairs.Count : Items.Count;

            /// <summary>Mọi từ học được khi xong buổi.</summary>
            public List<string> Taught()
            {
                var list = new List<string>();
                foreach (var w in Review)
                    list.Add(w.Id);
                foreach (var w in Pairs)
                    list.Add(w.Id);
                foreach (var it in Items)
                    list.AddRange(it.Words);
                return list;
            }
        }

        /// <summary>Giờ tan ca thật (phút): ít nhất 21:00, cộng phạt cho mỗi lượt trễ, không sớm hơn đồng hồ.</summary>
        public static float EndOfShift(float clockMinutes, int latePhases)
        {
            return Mathf.Max(clockMinutes, ShiftEnd + Mathf.Max(0, latePhases) * LatePenalty);
        }

        public static float EveningStart(float endOfShift) => Mathf.Max(endOfShift, ShiftEnd) + EveningGap;

        /// <summary>Số mục học được khi buổi tối bắt đầu lúc eveningMinutes.</summary>
        public static int ItemsFor(int baseItems, float eveningMinutes)
        {
            float late = Mathf.Max(0f, eveningMinutes - (ShiftEnd + EveningGap));
            return Mathf.Max(1, Mathf.Max(1, baseItems) - Mathf.FloorToInt(late / MinutesPerItem));
        }

        public static Plan Make(ContentDatabase db, GameState state, EveningData ev, float eveningMinutes)
        {
            var plan = new Plan();
            if (ev == null || !ev.HasStudy)
                return plan;
            plan.Kind = ev.Study;
            plan.Budget = ItemsFor(ev.Items, eveningMinutes);
            int budget = plan.Budget;

            // Ôn từ nợ trước, chừa ít nhất một mục cho bài mới.
            foreach (var id in state.PendingWords)
            {
                if (budget <= 1)
                    break;
                if (state.Knows(id) || !db.TryGetWord(id, out var w))
                    continue;
                plan.Review.Add(w);
                budget--;
            }

            if (ev.Study == EveningData.Match)
            {
                foreach (var w in db.WordsInSet(ev.Set))
                {
                    if (plan.Pairs.Count < budget)
                        plan.Pairs.Add(w);
                    else
                        plan.Deferred.Add(w.Id);
                }
            }
            else
            {
                foreach (var it in db.GetLesson(ev.Lesson).Items)
                {
                    if (plan.Items.Count < budget)
                        plan.Items.Add(it);
                    else
                        plan.Deferred.AddRange(it.Words);
                }
            }
            return plan;
        }

        /// <summary>Xong buổi học: vào vốn từ, xóa khỏi danh sách nợ; từ bị bớt thêm vào nợ (nếu chưa biết).</summary>
        public static void Finish(Plan plan, GameState state)
        {
            foreach (var id in plan.Taught())
            {
                state.Learn(id);
                state.PendingWords.Remove(id);
            }
            foreach (var id in plan.Deferred)
                if (!state.Knows(id) && !state.PendingWords.Contains(id))
                    state.PendingWords.Add(id);
        }

        // ---------- Dựng logic cho từng kiểu ----------

        /// <summary>Lề hai bên và khoảng cách giữa hai chữ trong một từ (đơn vị chiều cao chữ).</summary>
        const float TraceMargin = 0.18f, TraceGap = 0.2f;

        /// <summary>Mỗi mục là một lượt tô cả từ (QA-0140): nét các chữ xếp liền nhau theo bề rộng thật của từng chữ, nên
        /// dấu "'" hẹp không chiếm cả ô. Chữ không có nét thì bỏ qua.</summary>
        public static TraceLogic BuildTrace(ContentDatabase db, IList<StudyItem> items)
        {
            var words = new List<string>();
            var glyphs = new List<List<Vector2[]>>();
            var aspects = new List<float>();
            foreach (var it in items)
            {
                var strokes = new List<Vector2[]>();
                float cursor = TraceMargin;
                foreach (char c in it.Text ?? string.Empty)
                {
                    if (!db.TryGetLetter(c.ToString(), out var g))
                        continue;
                    var letter = Strokes(g);
                    float min = float.MaxValue, max = float.MinValue;
                    foreach (var stroke in letter)
                        foreach (var q in stroke)
                        {
                            min = Mathf.Min(min, q.x);
                            max = Mathf.Max(max, q.x);
                        }
                    if (min > max)
                        continue;
                    foreach (var stroke in letter)
                    {
                        for (int i = 0; i < stroke.Length; i++)
                            stroke[i].x += cursor - min;
                        strokes.Add(stroke);
                    }
                    cursor += max - min + TraceGap;
                }
                if (strokes.Count == 0)
                    continue;
                words.Add(it.Text);
                glyphs.Add(strokes);
                aspects.Add(cursor - TraceGap + TraceMargin);
            }
            return new TraceLogic(words, glyphs, aspects);
        }

        public static List<Vector2[]> Strokes(LetterGlyph g)
        {
            var list = new List<Vector2[]>();
            foreach (var stroke in g.Strokes)
            {
                var pts = new Vector2[stroke.Count];
                for (int i = 0; i < pts.Length; i++)
                    pts[i] = new Vector2(stroke[i][0], stroke[i][1]);
                list.Add(pts);
            }
            return list;
        }

        public static FillLogic BuildFill(ContentDatabase db, IList<StudyItem> items)
        {
            var list = new List<FillLogic.Item>();
            foreach (var it in items)
            {
                var options = new string[it.Options.Count];
                int answer = 0;
                for (int i = 0; i < options.Length; i++)
                {
                    options[i] = db.TryGetWord(it.Options[i], out var w) ? w.Fr : it.Options[i];
                    if (it.Options[i] == it.Answer)
                        answer = i;
                }
                list.Add(new FillLogic.Item { Prompt = it.Fr, Translation = it.Vi, Options = options, Answer = answer });
            }
            return new FillLogic(list);
        }

        public static SentenceLogic BuildSentence(IList<StudyItem> items)
        {
            var list = new List<SentenceLogic.Item>();
            foreach (var it in items)
                list.Add(new SentenceLogic.Item { Tokens = it.Tokens.ToArray(), Translation = it.Vi });
            return new SentenceLogic(list);
        }
    }
}
