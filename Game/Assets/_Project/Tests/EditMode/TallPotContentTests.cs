using System.IO;
using System.Linq;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Lịch nồi cao trong days.vi.json (brush17) chạy được với TallPotLogic: mọi bữa có nồi cao đều có nồi trên bếp (Ba kéo ra, hay lượt
    /// tua nhanh), bữa xong thì luôn có lượt cọ sau đó (lượt scrub_tall, hoặc tua nhanh kéo về cọ luôn), lệnh không bao giờ đòi bước sai.
    /// </summary>
    public class TallPotContentTests
    {
        static ContentDatabase Db() =>
            ContentDatabase.Load(name => File.ReadAllText(Path.Combine(Application.dataPath, "_Project/Resources/Content", name + ".vi.json")));

        static bool ScrubAhead(DayData day, int after)
        {
            for (int k = after + 1; k < day.Phases.Count; k++)
            {
                if (day.Phases[k].Orders.Exists(o => o.Task == OrderKinds.ScrubTall)) return true;
                if (day.Phases[k].CookTall) return false;
            }
            return false;
        }

        [Test]
        public void EveryTallPotOrderIsAValidStepOfTheCycle()
        {
            foreach (var day in Db().Days.Days)
            {
                var pot = new TallPotLogic();
                for (int i = 0; i < day.Phases.Count; i++)
                {
                    var phase = day.Phases[i];
                    string at = $"Ngày {day.Number}/{phase.Id}";
                    // Lượt chơi được: lệnh kéo ra làm trước khi bữa của lượt nấu (MealPots chờ nồi nằm trên ô bếp).
                    if (!phase.Montage)
                        foreach (var o in phase.Orders.Where(o => o.Task == OrderKinds.TallOut))
                        {
                            Assert.AreEqual(TallPotState.HomeClean, pot.State, $"{at}: kéo nồi cao ra khi nồi sạch ở góc rửa");
                            pot.Apply(TallPotStep.TowOut);
                            pot.Apply(TallPotStep.LiftUp);
                        }
                    if (phase.CookTall)
                    {
                        if (phase.Montage) pot.FastForwardTo(TallPotState.OnHobClean);
                        Assert.AreEqual(TallPotState.OnHobClean, pot.State, $"{at}: bữa có nồi cao mà nồi chưa nằm trên bếp (thiếu lệnh tall_out)");
                        pot.Apply(TallPotStep.Serve);
                        if (phase.Montage && !ScrubAhead(day, i)) pot.FastForwardTo(TallPotState.HomeClean);
                    }
                    foreach (var o in phase.Orders.Where(o => o.Task == OrderKinds.ScrubTall))
                    {
                        Assert.AreEqual(TallPotState.OnHobDirty, pot.State, $"{at}: cọ nồi cao khi nồi bẩn còn trên bếp");
                        pot.FastForwardTo(TallPotState.HomeClean);
                    }
                }
                Assert.AreEqual(TallPotState.HomeClean, pot.State, $"Ngày {day.Number}: hết ca nồi cao sạch ở góc rửa");
            }
        }

        [Test]
        public void EveryLunchAndDinnerCooksTheTallPotAndThreePlayedRounds()
        {
            var db = Db();
            foreach (var day in db.Days.Days)
                foreach (var phase in day.Phases.Where(p => p.Id.StartsWith("lunch") || p.Id == "dinner"))
                    Assert.IsTrue(phase.CookTall, $"Ngày {day.Number}/{phase.Id}: bữa trưa/tối nấu nồi cao");
            foreach (int n in new[] { 1, 4, 5 })
            {
                var day = db.GetDay(n);
                Assert.IsTrue(day.Phases.Exists(p => !p.Montage && p.Orders.Exists(o => o.Task == OrderKinds.TallOut)), $"Ngày {n}: Ba tự kéo nồi ra");
                Assert.IsTrue(day.Phases.Exists(p => !p.Montage && p.Orders.Exists(o => o.Task == OrderKinds.ScrubTall)), $"Ngày {n}: Ba tự cọ nồi cao");
            }
            foreach (int n in new[] { 2, 3, 4, 6 })
                Assert.IsTrue(db.GetDay(n).Phases.Exists(p => p.Id == "dinner" && p.Montage && p.StartAt == 960 && p.ServiceAt == 1050), $"Ngày {n}: montage bữa tối 16:00–17:30");
            Assert.IsFalse(db.GetDay(4).Phases.Find(p => p.Id == "breakfast").CookTall, "ngày 4 bỏ nồi cao ở bữa sáng");
        }

        [Test]
        public void PotsNeverOutnumberTheHobs()
        {
            // Sáu ô (hai lò × ba ô); lò bắc ba ô: nồi cao và chảo đồng cùng nấu (ngày 3) mỗi thứ một ô, còn chỗ cho nồi bữa.
            foreach (var day in Db().Days.Days)
                foreach (var phase in day.Phases)
                {
                    int big = (phase.CookTall ? 1 : 0) + (phase.CookPan ? 1 : 0);
                    Assert.LessOrEqual(phase.Dirty + big, 6, $"Ngày {day.Number}/{phase.Id}: nhiều nồi hơn số ô bếp");
                    Assert.LessOrEqual(big, 2, $"Ngày {day.Number}/{phase.Id}: lò bắc không đủ ô cho nồi cao và chảo");
                }
        }

        [Test]
        public void SailorHelpsOnlyOnTheSwayDayAndTheNotebookPageOpensOnDayOne()
        {
            var db = Db();
            foreach (var day in db.Days.Days)
            {
                var events = ChapterStart.EventsOf(day);
                Assert.AreEqual(day.Number == 4, events.Contains(EventIds.S2SailorHelp), $"Ngày {day.Number}: Jacques giữ nồi chỉ ngày 4");
            }
            Assert.Contains(EventIds.S2ScrubDone, ChapterStart.EventsOf(db.GetDay(1)), "trang sổ chảo đồng, nồi cao mở ở lần cọ đầu (ngày 1)");
            Assert.IsTrue(db.GetDay(4).Phases.Exists(p => p.TallSway && p.Orders.Exists(o => o.Task == OrderKinds.ScrubTall)));
        }

        [Test]
        public void TallPotStringsAndLinesExist()
        {
            var db = Db();
            foreach (var key in new[] { "order.tall_out", "prompt.tow_tall", "prompt.lift_tall", "prompt.lift_down_tall", "prompt.take_brush",
                         "prompt.take_long_brush", "prompt.need_brush", "prompt.need_long_brush", "guide.tall_tow_out", "guide.tall_lift_up",
                         "guide.tall_lift_down", "guide.tall_tow_back", "guide.tall_brush", "guide.tall_rinse", "guide.wash_brush", "guide.pan_brush" })
                Assert.IsTrue(db.Ui.Strings.ContainsKey(key), key);
            foreach (var id in new[] { "chef_tall_out", "chef_tall_back", "chef_tallpot" })
                Assert.IsTrue(db.Subtitles.Lines.Exists(l => l.Id == id), id);
        }
    }
}
