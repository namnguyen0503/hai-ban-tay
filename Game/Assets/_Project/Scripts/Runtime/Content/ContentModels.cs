using System.Collections.Generic;

namespace HaiBanTay.Content
{
    public sealed class NotebookFile
    {
        public int Version { get; set; }
        public List<NotebookPage> Pages { get; set; } = new List<NotebookPage>();
        public LinkTarget LinkTarget { get; set; }
    }

    public sealed class NotebookPage
    {
        public string Id { get; set; }
        public string Title { get; set; }

        public string Body { get; set; }
        public FlagAppend AppendIfFlag { get; set; }
        public string UnlockEvent { get; set; }
        public List<string> SourceCardIds { get; set; } = new List<string>();
        public bool Linkable { get; set; }
    }

    public sealed class FlagAppend
    {
        public string Flag { get; set; }
        public string Text { get; set; }
    }

    public sealed class LinkTarget
    {
        public string A { get; set; }
        public string B { get; set; }
        public string Event { get; set; }
    }

    public sealed class SourceCardsFile
    {
        public int Version { get; set; }
        public Dictionary<string, string> Layers { get; set; } = new Dictionary<string, string>();
        public List<SourceCard> Cards { get; set; } = new List<SourceCard>();
    }

    public sealed class SourceCard
    {
        public string Id { get; set; }
        public string Layer { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public List<string> FactIds { get; set; } = new List<string>();
        public string Citation { get; set; }
    }

    public sealed class SubtitlesFile
    {
        public int Version { get; set; }
        public Dictionary<string, string> Speakers { get; set; } = new Dictionary<string, string>();
        public List<SubtitleLine> Lines { get; set; } = new List<SubtitleLine>();
    }

    public sealed class SubtitleLine
    {
        public string Id { get; set; }
        public string Speaker { get; set; }
        public string Fr { get; set; }
        public string Vi { get; set; }
        public string Audio { get; set; }
        public float Duration { get; set; }
        /// <summary>Hướng nhìn của người nói (NpcGaze): trống là nói với Ba (nhìn theo mắt Ba); "task" là hướng dẫn làm việc, "other"
        /// là nói với người khác (đầu theo clip).</summary>
        public string Gaze { get; set; }
        /// <summary>Ba gặp một thứ lần đầu (DIRECTOR §7): nốt gảy ngắn "SFX/first_time" phát ngay trước câu, không kèm chữ.</summary>
        public bool FirstTime { get; set; }
    }

    public sealed class UiStringsFile
    {
        public int Version { get; set; }
        public Dictionary<string, string> Strings { get; set; } = new Dictionary<string, string>();
    }

    public sealed class FrenchWordsFile
    {
        public int Version { get; set; }
        public List<FrenchPair> Pairs { get; set; } = new List<FrenchPair>();
    }

    public sealed class FrenchPair
    {
        /// <summary>ID từ vựng; chuỗi hiển thị nhắc tới từ này bằng {w:id} (Core.Vocabulary).</summary>
        public string Id { get; set; }
        public string Fr { get; set; }
        public string Vi { get; set; }
        /// <summary>Bài học buổi tối dạy từ này (EveningData.Set); trống thì từ chỉ xuất hiện trong lệnh.</summary>
        public string Set { get; set; }
        /// <summary>ID câu phụ đề đọc mẫu từ này (subtitles.vi.json); trống thì không đọc.</summary>
        public string Line { get; set; }
    }

    public sealed class MapTransitionsFile
    {
        public int Version { get; set; }
        public List<MapTransition> Transitions { get; set; } = new List<MapTransition>();
    }

    public sealed class MapTransition
    {
        public string Id { get; set; }
        public float Duration { get; set; }
        public List<MapStop> Stops { get; set; } = new List<MapStop>();
    }

    public sealed class MapStop
    {
        public string Name { get; set; }
        public string Date { get; set; }
    }

    public sealed class EndingData
    {
        public PhaseData FinalMeal { get; set; }
        public int Version { get; set; }
        public Dictionary<string, float> Timings { get; set; } = new Dictionary<string, float>();
        public Dictionary<string, int> Coins { get; set; } = new Dictionary<string, int>();
        public string Narrator { get; set; }
        public List<EndCard> EndCards { get; set; } = new List<EndCard>();
        public string Title { get; set; }

        public List<string> Lines { get; set; } = new List<string>();
        public List<string> FarewellLines { get; set; } = new List<string>();
        public List<string> FarewellReplies { get; set; } = new List<string>();

        /// <summary>Thời lượng mốc (giây); thiếu khóa thì dùng giá trị mặc định.</summary>
        public float Timing(string key, float fallback)
        {
            return Timings != null && Timings.TryGetValue(key, out var v) ? v : fallback;
        }

        /// <summary>Ngân sách chơi E1a–E5c và phim E6: 480–720 s; không phải thời gian khóa điều khiển.</summary>
        public float EstimatedSeconds()
        {
            float player = Timing("dawn", 40f) + Timing("finalClean", 55f) + Timing("payQueue", 65f)
                         + Timing("pay", 35f) + Timing("farewell", 55f) + Timing("quay", 35f)
                         + Timing("quayObserve", 70f) + Timing("tram", 40f) + Timing("cafe", 45f)
                         + Timing("cafeServe", 25f) + Timing("pagesLink", 70f);
            float film = 7 * Timing("maiRecallCard", 5f) + Timing("fadeToBlack", 2f) + Timing("narrator", 5f) + EndCards.Count * Timing("endCard", 5f)
                         + Timing("title", 4f) + Timing("credits", 10f) + Timing("disclaimer", 5f);
            return player + film;
        }
    }

    public sealed class EndCard
    {
        public string Date { get; set; }
        public string Text { get; set; }
        public string SourceCardId { get; set; }
    }

    // ---------- Ca làm theo ngày (days.vi.json) ----------

    public sealed class DaysFile
    {
        public int Version { get; set; }
        public List<DayData> Days { get; set; } = new List<DayData>();
    }

    /// <summary>Một ngày trên tàu: chặng, thời tiết, các lượt việc, buổi tối.</summary>
    public sealed class DayData
    {
        public string Id { get; set; }
        public int Number { get; set; }
        public string Title { get; set; }
        public string Leg { get; set; }
        /// <summary>Bản đồ chặng (map_transitions) chạy trước khi vào ngày này; ngày đầu dùng bản đồ của cảnh 01.</summary>
        public string Transition { get; set; }
        /// <summary>Khung cảnh ô cửa: sea | port.</summary>
        public string Porthole { get; set; } = "sea";
        public float Ship { get; set; } = 0.15f;
        /// <summary>Hệ số nóng của bếp (lò, phòng).</summary>
        public float Heat { get; set; } = 1f;
        /// <summary>Giờ bắt đầu ca, tính bằng phút từ nửa đêm (240 = 4:00). Đồng hồ chạy theo việc, không theo thời gian thật.</summary>
        public int ClockStart { get; set; } = 240;
        public float FatigueStart { get; set; }
        public float GrimeStart { get; set; }
        /// <summary>Tro còn dưới ghi lò từ hôm trước (0–1): lửa không lên hết mức cho tới khi cào lò.</summary>
        public float AshStart { get; set; }
        /// <summary>Số xẻng than còn trong thùng đầu ca; -1 = đầy.</summary>
        public int CoalStock { get; set; } = -1;
        /// <summary>Hệ số tro sinh ra khi than cháy (1 = bình thường; Ấn Độ Dương tro nhiều).</summary>
        public float AshRate { get; set; } = 1f;
        /// <summary>Tàu đỗ ở cảng: không lắc, máy êm, dây chắn cầu thang tàu, không lên bờ.</summary>
        public bool Docked { get; set; }
        /// <summary>Phông cảng thấy từ boong (DayScenery): "singapore"; trống = biển khơi.</summary>
        public string Port { get; set; }
        /// <summary>Hành khách, thủy thủ đứng trên boong (tắt trong lượt bão).</summary>
        public bool DeckCrowd { get; set; } = true;
        /// <summary>Sự kiện phát ngay đầu ngày (tới cảng).</summary>
        public List<string> StartEvents { get; set; } = new List<string>();
        /// <summary>NPC trong ngày: ai, lúc nào, làm gì, nói gì (Npc.DayCast).</summary>
        public List<NpcBeat> Npcs { get; set; } = new List<NpcBeat>();
        public List<PhaseData> Phases { get; set; } = new List<PhaseData>();
        public EveningData Evening { get; set; }
    }

    /// <summary>
    /// Một lần NPC xuất hiện trong ngày. who: soldiers | jacques | mai. phase: id lượt, "day" (cả ca) hoặc "evening".
    /// action: help (tới làm cùng trong lượt rồi về), visit (ghé bếp, đứng chờ nói chuyện rồi đi), deck (đứng trên boong),
    /// teach (buổi tối, ngồi ở góc đèn dầu), cards (buổi tối, ngồi đánh bài với anh em trong lúc Ba tự học). lines: câu thoại
    /// phát khi người chơi tới gần (teach: phát đầu buổi tối).
    /// </summary>
    public sealed class NpcBeat
    {
        public string Who { get; set; }
        public string Phase { get; set; }
        public string Action { get; set; }
        /// <summary>help: lượt NPC rời đi (về khi lượt này bắt đầu); trống = về khi lượt Phase xong.</summary>
        public string Until { get; set; }
        public List<string> Lines { get; set; } = new List<string>();
        /// <summary>Câu gọi phát ngay khi NPC tới (không cần ở gần), mời người chơi lại nói chuyện.</summary>
        public string Call { get; set; }
        /// <summary>Sự kiện phát khi thoại xong (help: khi NPC bắt đầu giúp).</summary>
        public string Event { get; set; }
    }

    /// <summary>Một lượt trong ca: lệnh của quản bếp, các việc phải xong, giờ phục vụ.</summary>
    public sealed class PhaseData
    {
        public string Id { get; set; }
        /// <summary>Câu quản bếp khi bắt đầu lượt.</summary>
        public string Line { get; set; }
        /// <summary>Giờ bắt đầu lượt (phút từ nửa đêm); lượt trước xong sớm hơn thì đồng hồ tua nhanh tới giờ này.</summary>
        public int StartAt { get; set; }
        /// <summary>Giờ phục vụ (phút từ nửa đêm), cũng là giờ lượt kết thúc; 0 = không có (dùng Minutes).</summary>
        public int ServiceAt { get; set; }
        /// <summary>Độ dài lượt trên đồng hồ (phút) khi không có ServiceAt. Đồng hồ đi từ đầu tới cuối lượt theo tiến độ việc:
        /// đứng không thì giờ không trôi.</summary>
        public int Minutes { get; set; }
        /// <summary>Lượt tua nhanh (không thao tác): màn đen ngắn, giờ chạy, việc được làm xong theo đúng thứ tự (bữa nấu,
        /// nồi về chồng rửa…). Caption: khóa ui_strings hiện trên thẻ.</summary>
        public bool Montage { get; set; }
        public string Caption { get; set; }
        /// <summary>Lượt chỉ xong khi lò còn đủ lửa.</summary>
        public bool NeedsFire { get; set; }
        public bool Storm { get; set; }
        public bool Calm { get; set; }
        /// <summary>F05b: sóng lớn phủ boong giếng trước khi người chơi kéo rổ rau qua boong (DeckWave).</summary>
        public bool DeckWave { get; set; }
        /// <summary>Số nồi bẩn dồn về khu rửa khi lượt xong.</summary>
        public int Dirty { get; set; }
        /// <summary>Bữa dùng nồi cao; bữa xong mới cho cọ nồi này trên ghế.</summary>
        public bool CookTall { get; set; }
        public bool CookPan { get; set; }
        /// <summary>Lượt cọ nồi cao có sóng: cọ tới 40% thì nồi chao, chờ Jacques giữ nồi (ngày 4, sự kiện s2.sailor.help).</summary>
        public bool TallSway { get; set; }
        public List<string> AfterLines { get; set; } = new List<string>();
        public List<OrderData> Orders { get; set; } = new List<OrderData>();
        /// <summary>Sự kiện (EventIds) phát khi lượt xong.</summary>
        public List<string> Events { get; set; } = new List<string>();
        /// <summary>Thẻ tư liệu hiện khi lượt bắt đầu / khi xong.</summary>
        public List<string> StartCards { get; set; } = new List<string>();
        public List<string> Cards { get; set; } = new List<string>();
    }

    public sealed class OrderData
    {
        /// <summary>fire | sack | wash | secure | scrub_tall | ash | sweep | greens | pan | haul (Shift.OrderKinds).</summary>
        public string Task { get; set; }
        public int Count { get; set; } = 1;
    }

    public sealed class EveningData
    {
        public const string Rest = "rest";
        public const string Match = "match";
        public const string Trace = "trace";
        public const string Fill = "fill";
        public const string Sentence = "sentence";

        /// <summary>rest (chỉ ngồi nghỉ) | match (ghép từ) | trace (tô chữ) | fill (điền từ) | sentence (xếp câu).</summary>
        public string Study { get; set; } = Rest;
        /// <summary>match: bộ từ (FrenchPair.Set) cho buổi học.</summary>
        public string Set { get; set; }
        /// <summary>trace, fill, sentence: bài trong study.vi.json.</summary>
        public string Lesson { get; set; }
        /// <summary>Số mục học khi tan ca đúng giờ; tan ca muộn thì bớt (Tasks.StudyPlanner).</summary>
        public int Items { get; set; } = 4;
        /// <summary>Câu mở đầu buổi tối.</summary>
        public string Line { get; set; }
        /// <summary>Hai lính cho mượn sách sau buổi học (F07a): phát sự kiện s2.book.lent.</summary>
        public bool Book { get; set; }
        /// <summary>Câu thoại sau buổi học (cho mượn sách, chúc ngủ ngon…).</summary>
        public List<string> After { get; set; } = new List<string>();
        public List<string> Cards { get; set; } = new List<string>();

        public string StoryLesson { get; set; }
        public bool Coffee { get; set; }
        public List<string> Events { get; set; } = new List<string>();
        public bool HasStudy => !string.IsNullOrEmpty(Study) && Study != Rest;
    }

    // ---------- Học buổi tối (study.vi.json) ----------

    public sealed class StudyFile
    {
        public int Version { get; set; }
        /// <summary>Chữ cái để tô: mỗi chữ là các nét, mỗi nét là dãy điểm [x, y] trong ô 0–1 (y hướng xuống).</summary>
        public List<LetterGlyph> Letters { get; set; } = new List<LetterGlyph>();
        public List<StudyLesson> Lessons { get; set; } = new List<StudyLesson>();
    }

    public sealed class LetterGlyph
    {
        public string Id { get; set; }
        public List<List<float[]>> Strokes { get; set; } = new List<List<float[]>>();
    }

    /// <summary>Một bài học buổi tối: trace | fill | sentence.</summary>
    public sealed class StudyLesson
    {
        public string Id { get; set; }
        public string Kind { get; set; }
        public string Language { get; set; }
        public string Heading { get; set; }
        public List<StudyItem> Items { get; set; } = new List<StudyItem>();
    }

    /// <summary>
    /// Một mục học. trace: text là chữ cần tô (chữ hoa trong letters). fill: fr có chỗ trống "___", answer và options là ID
    /// từ (french_words). sentence: tokens là các từ của câu theo đúng thứ tự. vi: nghĩa tiếng Việt. words: các từ học được
    /// khi xong mục (vào vốn từ). line: câu người dạy đọc mẫu khi mục bắt đầu.
    /// </summary>
    public sealed class StudyItem
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public string Fr { get; set; }
        public string Vi { get; set; }
        public string Answer { get; set; }
        public List<string> Options { get; set; } = new List<string>();
        public List<string> Tokens { get; set; } = new List<string>();
        public List<string> Words { get; set; } = new List<string>();
        public string Line { get; set; }
    }
}
