using System.Collections.Generic;
using HaiBanTay.Content;
using HaiBanTay.Shift;

namespace HaiBanTay.Core
{
    /// <summary>
    /// Chọn chương: dựng lại tiến trình như thể người chơi đã đi qua mọi phần trước đó (sự kiện, trang sổ, vốn từ,
    /// bụi than), để trang sổ và lệnh tiếng Pháp khớp với ngày được chọn.
    /// </summary>
    public static class ChapterStart
    {
        static readonly string[] Scene01Events = { EventIds.S1HandsRaised, EventIds.S1NotebookIntro, EventIds.S1Depart };

        /// <summary>Sự kiện một ngày trên tàu phát ra khi chơi hết (theo days.vi.json).</summary>
        public static List<string> EventsOf(DayData day)
        {
            var list = new List<string>(day.StartEvents);
            foreach (var beat in day.Npcs)
                if (!string.IsNullOrEmpty(beat.Event))
                    list.Add(beat.Event);
            foreach (var phase in day.Phases)
            {
                if (phase.Storm)
                    list.Add(EventIds.S2StormStart);
                if (phase.DeckWave)
                    list.Add(EventIds.S2StormWave);
                foreach (var o in phase.Orders)
                {
                    if (o.Task == OrderKinds.Secure)
                        list.Add(EventIds.S2SecureDone);
                    else if (o.Task == OrderKinds.ScrubTall)
                    {
                        // Jacques giữ nồi chỉ ở lượt có sóng (ngày 4); trang sổ "Chảo đồng, nồi cao" mở ở lần cọ đầu tiên (ngày 1).
                        if (phase.TallSway)
                            list.Add(EventIds.S2SailorHelp);
                        if (!list.Contains(EventIds.S2ScrubDone))
                            list.Add(EventIds.S2ScrubDone);
                    }
                }
                list.AddRange(phase.Events);
                if (phase.Calm)
                    list.Add(EventIds.S2StormCalm);
            }
            list.Add(EventIds.S2ShiftEnd);
            if (day.Evening != null && day.Evening.HasStudy)
                list.Add(EventIds.S2WordsDone);
            if (day.Evening != null && day.Evening.Book)
                list.Add(EventIds.S2BookLent);
            if (day.Evening != null)
            {
                list.AddRange(day.Evening.Events);
                if (day.Evening.Coffee) list.Add(EventIds.S2CoffeeDone);
            }
            return list;
        }

        /// <summary>Các từ buổi tối của ngày dạy (đủ bài, như tan ca đúng giờ).</summary>
        public static List<string> WordsOf(ContentDatabase content, DayData day)
        {
            var list = new List<string>();
            var ev = day.Evening;
            if (ev == null || !ev.HasStudy)
                return list;
            if (ev.Study == EveningData.Match)
            {
                foreach (var w in content.WordsInSet(ev.Set))
                    list.Add(w.Id);
                return list;
            }
            foreach (var it in content.GetLesson(ev.Lesson).Items)
                list.AddRange(it.Words);
            return list;
        }

        /// <summary>Bắt đầu ngày dayNumber như đã chơi các ngày trước. keepMaxDay: ngày xa nhất đã mở khóa.</summary>
        public static void PrepareDay(GameRoot root, int dayNumber, int keepMaxDay, bool reachedMarseille)
        {
            root.ResetProgress();
            var content = root.Content;
            var state = root.State;
            Mark(root, Scene01Events);
            foreach (var day in content.Days.Days)
            {
                if (day.Number >= dayNumber)
                    continue;
                Mark(root, EventsOf(day));
                foreach (var w in WordsOf(content, day))
                    state.Learn(w);
            }
            var target = content.GetDay(dayNumber);
            state.Day = dayNumber;
            state.MaxDay = System.Math.Max(dayNumber, keepMaxDay);
            state.ReachedMarseille = reachedMarseille;
            state.Grime = target != null ? target.GrimeStart : 0f;
            state.Fatigue = target != null ? target.FatigueStart : 0f;
        }

        /// <summary>Tới thẳng Marseille như đã chơi hết các ngày trên tàu.</summary>
        public static void PrepareMarseille(GameRoot root, int keepMaxDay)
        {
            int last = GameState.FirstDay;
            foreach (var d in root.Content.Days.Days)
                last = System.Math.Max(last, d.Number);
            PrepareDay(root, last + 1, keepMaxDay, true);
            root.State.Day = last;
            root.State.MaxDay = System.Math.Max(last, keepMaxDay);
        }

        static void Mark(GameRoot root, IEnumerable<string> events)
        {
            foreach (var e in events)
            {
                root.Events.MarkFired(e);
                foreach (var page in root.Content.Notebook.Pages)
                    if (page.UnlockEvent == e)
                        root.Notebook.EnsureUnlocked(page.Id);
            }
        }
    }
}
