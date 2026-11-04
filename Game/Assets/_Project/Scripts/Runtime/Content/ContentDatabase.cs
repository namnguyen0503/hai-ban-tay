using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace HaiBanTay.Content
{
    public sealed class ContentException : Exception
    {
        public ContentException(string message) : base(message) { }
        public ContentException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Đọc Resources/Content/*.vi.json, tra cứu theo ID, báo lỗi kèm tên file.</summary>
    public sealed class ContentDatabase
    {
        public const string ResourceFolder = "Content/";
        public static readonly string[] FileNames =
        {
            "notebook_pages", "source_cards", "subtitles", "ui_strings",
            "french_words", "map_transitions", "ending", "days", "study"
        };

        /// <summary>NPC có thể xuất hiện trong ngày (days.vi.json → npcs[].who) và việc họ làm.</summary>
        public static readonly string[] NpcIds = { "soldiers", "jacques", "mai" };
        public static readonly string[] NpcActions = { "help", "visit", "deck", "teach", "cards" };
        public static readonly string[] StudyKinds =
        {
            EveningData.Rest, EveningData.Match, EveningData.Trace, EveningData.Fill, EveningData.Sentence
        };

        readonly Dictionary<string, NotebookPage> _pages = new Dictionary<string, NotebookPage>();
        readonly Dictionary<string, SourceCard> _cards = new Dictionary<string, SourceCard>();
        readonly Dictionary<string, SubtitleLine> _lines = new Dictionary<string, SubtitleLine>();
        readonly Dictionary<string, FrenchPair> _words = new Dictionary<string, FrenchPair>();
        readonly Dictionary<string, StudyLesson> _lessons = new Dictionary<string, StudyLesson>();
        readonly Dictionary<string, LetterGlyph> _letters = new Dictionary<string, LetterGlyph>();

        public NotebookFile Notebook { get; private set; }
        public SourceCardsFile SourceCards { get; private set; }
        public SubtitlesFile Subtitles { get; private set; }
        public UiStringsFile Ui { get; private set; }
        public FrenchWordsFile FrenchWords { get; private set; }
        public MapTransitionsFile MapTransitions { get; private set; }
        public EndingData Ending { get; private set; }
        public DaysFile Days { get; private set; }
        public StudyFile Study { get; private set; }

        public static ContentDatabase LoadFromResources()
        {
            return Load(name =>
            {
                var asset = Resources.Load<TextAsset>(ResourceFolder + name + ".vi");
                return asset != null ? asset.text : null;
            });
        }

        /// <summary>reader(tênFileKhôngĐuôi) trả về nội dung JSON, hoặc null nếu thiếu file.</summary>
        public static ContentDatabase Load(Func<string, string> reader)
        {
            var db = new ContentDatabase();
            db.Notebook = Parse<NotebookFile>(reader, "notebook_pages");
            db.SourceCards = Parse<SourceCardsFile>(reader, "source_cards");
            db.Subtitles = Parse<SubtitlesFile>(reader, "subtitles");
            db.Ui = Parse<UiStringsFile>(reader, "ui_strings");
            db.FrenchWords = Parse<FrenchWordsFile>(reader, "french_words");
            db.MapTransitions = Parse<MapTransitionsFile>(reader, "map_transitions");
            db.Ending = Parse<EndingData>(reader, "ending");
            db.Days = Parse<DaysFile>(reader, "days");
            db.Study = Parse<StudyFile>(reader, "study");

            Index(db._pages, db.Notebook.Pages, p => p.Id, "notebook_pages");
            Index(db._cards, db.SourceCards.Cards, c => c.Id, "source_cards");
            Index(db._lines, db.Subtitles.Lines, l => l.Id, "subtitles");
            Index(db._words, db.FrenchWords.Pairs, w => w.Id, "french_words");
            Index(db._lessons, db.Study.Lessons, l => l.Id, "study");
            Index(db._letters, db.Study.Letters, l => l.Id, "study");

            var problems = db.FindBrokenReferences();
            if (problems.Count > 0)
                throw new ContentException("Tham chiếu hỏng trong nội dung:\n- " + string.Join("\n- ", problems));
            return db;
        }

        static T Parse<T>(Func<string, string> reader, string name)
        {
            var text = reader(name);
            if (string.IsNullOrEmpty(text))
                throw new ContentException($"Thiếu file nội dung {name}.vi.json");
            try
            {
                var value = JsonConvert.DeserializeObject<T>(text);
                if (value == null)
                    throw new ContentException($"{name}.vi.json rỗng");
                return value;
            }
            catch (JsonException e)
            {
                throw new ContentException($"{name}.vi.json: JSON lỗi. {e.Message}", e);
            }
        }

        static void Index<T>(Dictionary<string, T> map, List<T> items, Func<T, string> id, string file)
        {
            foreach (var item in items)
            {
                var key = id(item);
                if (string.IsNullOrEmpty(key))
                    throw new ContentException($"{file}.vi.json: có phần tử thiếu id");
                if (map.ContainsKey(key))
                    throw new ContentException($"{file}.vi.json: ID trùng '{key}'");
                map[key] = item;
            }
        }

        /// <summary>Danh sách tham chiếu không trỏ tới đâu (rỗng nếu ổn).</summary>
        public List<string> FindBrokenReferences()
        {
            var problems = new List<string>();
            foreach (var page in Notebook.Pages)
                foreach (var cardId in page.SourceCardIds)
                    if (!_cards.ContainsKey(cardId))
                        problems.Add($"notebook_pages: trang '{page.Id}' trỏ tới thẻ '{cardId}' không tồn tại");
            foreach (var card in Ending.EndCards)
                if (!_cards.ContainsKey(card.SourceCardId))
                    problems.Add($"ending: thẻ mốc '{card.Date}' trỏ tới thẻ '{card.SourceCardId}' không tồn tại");
            foreach (var line in Subtitles.Lines)
                if (!Subtitles.Speakers.ContainsKey(line.Speaker))
                    problems.Add($"subtitles: dòng '{line.Id}' có speaker '{line.Speaker}' không khai báo");
            foreach (var card in SourceCards.Cards)
                if (!SourceCards.Layers.ContainsKey(card.Layer))
                    problems.Add($"source_cards: thẻ '{card.Id}' có layer '{card.Layer}' không khai báo");
            foreach (var word in FrenchWords.Pairs)
                if (!string.IsNullOrEmpty(word.Line) && !_lines.ContainsKey(word.Line))
                    problems.Add($"french_words: từ '{word.Id}' trỏ tới câu '{word.Line}' không tồn tại");
            foreach (var line in Subtitles.Lines)
                CheckTokens(problems, $"subtitles: dòng '{line.Id}'", line.Vi);
            foreach (var kv in Ui.Strings)
                CheckTokens(problems, $"ui_strings: khóa '{kv.Key}'", kv.Value);
            CheckDays(problems);
            foreach (var line in Ending.Lines)
                CheckLine(problems, "ending", line);
            foreach (var line in Ending.FarewellLines)
                CheckLine(problems, "ending/farewell", line);
            foreach (var line in Ending.FarewellReplies)
                CheckLine(problems, "ending/reply", line);
            CheckStudy(problems);
            return problems;
        }

        void CheckTokens(List<string> problems, string where, string text)
        {
            foreach (var id in Core.Vocabulary.TokensIn(text))
                if (!_words.ContainsKey(id))
                    problems.Add($"{where} nhắc từ '{id}' không có trong french_words");
        }

        void CheckDays(List<string> problems)
        {
            var transitions = new HashSet<string>();
            foreach (var t in MapTransitions.Transitions)
                transitions.Add(t.Id);
            var numbers = new HashSet<int>();
            foreach (var day in Days.Days)
            {
                string d = $"days: ngày '{day.Id}'";
                if (!numbers.Add(day.Number))
                    problems.Add($"{d} trùng số ngày {day.Number}");
                if (!string.IsNullOrEmpty(day.Transition) && !transitions.Contains(day.Transition))
                    problems.Add($"{d} trỏ tới bản đồ '{day.Transition}' không tồn tại");
                if (day.Phases.Count == 0)
                    problems.Add($"{d} không có lượt việc nào");
                var phaseIds = new HashSet<string> { "day", "evening" };
                foreach (var phase in day.Phases)
                    phaseIds.Add(phase.Id);
                foreach (var beat in day.Npcs)
                {
                    string b = $"{d}, NPC '{beat.Who}'";
                    if (System.Array.IndexOf(NpcIds, beat.Who) < 0)
                        problems.Add($"{b} không phải NPC đã biết");
                    if (System.Array.IndexOf(NpcActions, beat.Action) < 0)
                        problems.Add($"{b} có việc '{beat.Action}' không hợp lệ");
                    if (string.IsNullOrEmpty(beat.Phase) || !phaseIds.Contains(beat.Phase))
                        problems.Add($"{b} trỏ tới lượt '{beat.Phase}' không có trong ngày");
                    if (!string.IsNullOrEmpty(beat.Until) && !phaseIds.Contains(beat.Until))
                        problems.Add($"{b} rời đi ở lượt '{beat.Until}' không có trong ngày");
                    foreach (var l in beat.Lines)
                        CheckLine(problems, b, l);
                    CheckLine(problems, b, beat.Call);
                }
                foreach (var phase in day.Phases)
                {
                    string p = $"{d}, lượt '{phase.Id}'";
                    foreach (var line in phase.AfterLines) CheckLine(problems, p, line);
                    CheckLine(problems, p, phase.Line);
                    foreach (var c in phase.Cards)
                        CheckCard(problems, p, c);
                    foreach (var c in phase.StartCards)
                        CheckCard(problems, p, c);
                    foreach (var o in phase.Orders)
                    {
                        if (!Shift.OrderKinds.IsKnown(o.Task))
                            problems.Add($"{p} có việc '{o.Task}' không hợp lệ");
                        if (o.Count < 1)
                            problems.Add($"{p}: việc '{o.Task}' cần số lượng >= 1");
                    }
                }
                var ev = day.Evening;
                if (ev == null)
                    continue;
                if (!string.IsNullOrEmpty(ev.StoryLesson) && !_lessons.ContainsKey(ev.StoryLesson))
                    problems.Add($"{d}: bài quốc ngữ/viết thư không tồn tại");
                CheckLine(problems, d + ", buổi tối", ev.Line);
                foreach (var l in ev.After)
                    CheckLine(problems, d + ", buổi tối", l);
                foreach (var c in ev.Cards)
                    CheckCard(problems, d + ", buổi tối", c);
                if (System.Array.IndexOf(StudyKinds, ev.Study) < 0)
                    problems.Add($"{d}: kiểu học '{ev.Study}' không hợp lệ");
                if (ev.Study == EveningData.Match && WordsInSet(ev.Set).Count < 2)
                    problems.Add($"{d}: bộ từ '{ev.Set}' của buổi tối cần ít nhất 2 từ");
                if (ev.HasStudy && ev.Study != EveningData.Match)
                {
                    if (!_lessons.TryGetValue(ev.Lesson ?? string.Empty, out var lesson))
                        problems.Add($"{d}: bài học '{ev.Lesson}' không có trong study");
                    else if (lesson.Kind != ev.Study)
                        problems.Add($"{d}: bài '{ev.Lesson}' là kiểu '{lesson.Kind}', buổi tối cần '{ev.Study}'");
                }
                if (ev.HasStudy && ev.Items < 1)
                    problems.Add($"{d}: buổi học cần ít nhất 1 mục");
            }
        }

        void CheckStudy(List<string> problems)
        {
            foreach (var g in Study.Letters)
                if (g.Strokes.Count == 0 || g.Strokes.Exists(st => st.Count < 2 || st.Exists(pt => pt == null || pt.Length != 2)))
                    problems.Add($"study: chữ '{g.Id}' cần các nét có ít nhất 2 điểm [x, y]");
            foreach (var lesson in Study.Lessons)
            {
                string l = $"study: bài '{lesson.Id}'";
                if (lesson.Kind != EveningData.Trace && lesson.Kind != EveningData.Fill && lesson.Kind != EveningData.Sentence)
                    problems.Add($"{l} có kiểu '{lesson.Kind}' không hợp lệ");
                if (lesson.Items.Count == 0)
                    problems.Add($"{l} không có mục nào");
                foreach (var item in lesson.Items)
                {
                    string it = $"{l}, mục '{item.Id}'";
                    foreach (var w in item.Words)
                        if (!_words.ContainsKey(w))
                            problems.Add($"{it} dạy từ '{w}' không có trong french_words");
                    CheckLine(problems, it, item.Line);
                    switch (lesson.Kind)
                    {
                        case EveningData.Trace:
                            if (string.IsNullOrEmpty(item.Text))
                                problems.Add($"{it} thiếu chữ để tô");
                            else
                                foreach (char c in item.Text)
                                    if (!_letters.ContainsKey(c.ToString()))
                                        problems.Add($"{it}: chữ '{c}' chưa có nét trong letters");
                            break;
                        case EveningData.Fill:
                            if (string.IsNullOrEmpty(item.Fr) || !item.Fr.Contains(Tasks.FillLogic.Blank))
                                problems.Add($"{it}: câu cần chỗ trống '{Tasks.FillLogic.Blank}'");
                            if (!_words.ContainsKey(item.Answer ?? string.Empty))
                                problems.Add($"{it}: đáp án '{item.Answer}' không có trong french_words");
                            if (!item.Options.Contains(item.Answer ?? string.Empty))
                                problems.Add($"{it}: các lựa chọn phải có đáp án");
                            foreach (var o in item.Options)
                                if (!_words.ContainsKey(o))
                                    problems.Add($"{it}: lựa chọn '{o}' không có trong french_words");
                            break;
                        case EveningData.Sentence:
                            if (item.Tokens.Count < 2)
                                problems.Add($"{it}: câu cần ít nhất 2 từ");
                            break;
                    }
                }
            }
        }

        public StudyLesson GetLesson(string id) => Get(_lessons, id, "study");
        public bool TryGetLetter(string id, out LetterGlyph glyph) => _letters.TryGetValue(id, out glyph);

        void CheckLine(List<string> problems, string where, string id)
        {
            if (!string.IsNullOrEmpty(id) && !_lines.ContainsKey(id))
                problems.Add($"{where} trỏ tới câu '{id}' không tồn tại");
        }

        void CheckCard(List<string> problems, string where, string id)
        {
            if (!_cards.ContainsKey(id))
                problems.Add($"{where} trỏ tới thẻ '{id}' không tồn tại");
        }

        /// <summary>Các từ thuộc một bài học buổi tối, theo thứ tự trong file.</summary>
        public List<FrenchPair> WordsInSet(string set)
        {
            var list = new List<FrenchPair>();
            foreach (var w in FrenchWords.Pairs)
                if (!string.IsNullOrEmpty(set) && w.Set == set)
                    list.Add(w);
            return list;
        }

        /// <summary>Ngày theo số thứ tự (1..); null nếu không có.</summary>
        public DayData GetDay(int number)
        {
            foreach (var d in Days.Days)
                if (d.Number == number)
                    return d;
            return null;
        }

        public bool TryGetWord(string id, out FrenchPair word) => _words.TryGetValue(id, out word);

        public IReadOnlyCollection<NotebookPage> Pages => _pages.Values;
        public IReadOnlyCollection<SourceCard> Cards => _cards.Values;
        public IReadOnlyCollection<SubtitleLine> Lines => _lines.Values;

        public NotebookPage GetPage(string id) => Get(_pages, id, "notebook_pages");
        public SourceCard GetCard(string id) => Get(_cards, id, "source_cards");
        public SubtitleLine GetLine(string id) => Get(_lines, id, "subtitles");

        public string GetString(string key)
        {
            if (!Ui.Strings.TryGetValue(key, out var value))
                throw new ContentException($"ui_strings.vi.json: thiếu khóa '{key}'");
            return value;
        }

        static T Get<T>(Dictionary<string, T> map, string id, string file)
        {
            if (!map.TryGetValue(id, out var value))
                throw new ContentException($"{file}.vi.json: không có ID '{id}'");
            return value;
        }
    }
}
