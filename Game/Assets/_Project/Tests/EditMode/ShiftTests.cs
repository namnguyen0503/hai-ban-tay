using System.Collections.Generic;
using System.IO;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Notebook;
using HaiBanTay.Player;
using HaiBanTay.UI;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>Logic ca làm (M8): lửa lò, bảng việc, đồng hồ, rửa nồi, thể lực, từ vựng, bản lưu.</summary>
    public class ShiftTests
    {
        static string ContentDir => Path.Combine(Application.dataPath, "_Project/Resources/Content");

        static ContentDatabase LoadContent()
        {
            return ContentDatabase.Load(name =>
            {
                var path = Path.Combine(ContentDir, name + ".vi.json");
                return File.Exists(path) ? File.ReadAllText(path) : null;
            });
        }

        // ---------- Lửa lò ----------

        [Test]
        public void ColdStoveLightsUpWithCoalAndDiesDownWithoutIt()
        {
            var f = new FireLogic();
            f.Tick(1f);
            Assert.AreEqual(0f, f.Level, 1e-4f, "lò nguội lúc 4 giờ sáng");
            for (int i = 0; i < 3; i++)
                f.AddCoal();
            for (int i = 0; i < 60; i++)
                f.Tick(0.1f);
            Assert.Greater(f.Level, 0.9f, "đủ ba xẻng thì lửa lên gần hết mức");
            float lit = f.Level;
            for (int i = 0; i < 3000; i++)
                f.Tick(0.1f);
            Assert.Less(f.Level, lit * 0.5f, "không thêm than thì lửa hạ dần");
            Assert.AreEqual(0f, f.Fuel, 1e-4f);
        }

        [Test]
        public void FuelIsCappedAndAshLowersTheFlame()
        {
            var f = new FireLogic();
            for (int i = 0; i < 20; i++)
                f.AddCoal();
            Assert.AreEqual(FireLogic.MaxFuel, f.Fuel, 1e-4f);
            float clean = f.Target;
            f.Set(f.Fuel, f.Level, ash: 0.5f);
            Assert.Less(f.Target, clean);
            f.Rake(1f);
            Assert.AreEqual(clean, f.Target, 1e-4f);
        }

        // ---------- Bảng việc ----------

        [Test]
        public void HudShowsOpenOrdersFirstAndFoldsTheRestIntoOneRow()
        {
            var b = new OrderBoardLogic();
            foreach (var kind in OrderKinds.All)
                b.Add(kind, 2);
            b.Report(OrderKinds.Sweep, 2);
            var few = ShiftHud.SelectVisible(b.Orders, ShiftHud.MaxRows, out int hiddenFew);
            Assert.AreEqual(ShiftHud.MaxRows - 1, few.Count, "chừa một dòng cho '+K việc khác'");
            Assert.AreEqual(b.Orders.Count - few.Count, hiddenFew);
            foreach (var o in few)
                Assert.IsFalse(o.IsDone, "việc chưa xong được ưu tiên");

            var small = new OrderBoardLogic();
            small.Add(OrderKinds.Fire, 1); small.Add(OrderKinds.Sack, 1);
            small.Report(OrderKinds.Fire, 1);
            var all = ShiftHud.SelectVisible(small.Orders, ShiftHud.MaxRows, out int none);
            Assert.AreEqual(0, none);
            Assert.AreEqual(2, all.Count);
            Assert.AreEqual(OrderKinds.Sack, all[0].Kind, "việc đã xong xếp sau");
        }

        [Test]
        public void BoardFillsOrdersOfTheSameKindInOrder()
        {
            var b = new OrderBoardLogic();
            int changes = 0;
            b.Changed += () => changes++;
            var sacks = b.Add(OrderKinds.Sack, 2);
            var fire = b.Add(OrderKinds.Fire, 3);
            Assert.IsFalse(b.AllDone);
            Assert.AreEqual(0, b.Report(OrderKinds.Wash, 1), "việc không có lệnh thì không tính");
            Assert.AreEqual(2, b.Report(OrderKinds.Fire, 2));
            Assert.AreEqual(1, fire.Remaining);
            Assert.AreEqual(1, b.Report(OrderKinds.Fire, 5), "chỉ dùng phần còn thiếu");
            Assert.IsTrue(fire.IsDone);
            Assert.IsTrue(b.HasOpen(OrderKinds.Sack));
            b.Report(OrderKinds.Sack, 2);
            Assert.IsTrue(sacks.IsDone);
            Assert.IsTrue(b.AllDone);
            Assert.Greater(changes, 3);
            b.Clear();
            Assert.AreEqual(0, b.Orders.Count);
            Assert.IsTrue(b.AllDone, "bảng trống coi như xong");
        }

        // ---------- Đồng hồ ----------

        [Test]
        public void ClockFollowsWorkNotRealTime()
        {
            var c = new ShiftClockLogic(240f);
            c.SetWindow(240f, 300f);
            c.Tick(100f);
            Assert.AreEqual("4:00", c.Text, "đứng không thì giờ không trôi");
            c.SetProgress(0.5f);
            c.Tick(100f);
            Assert.AreEqual("4:30", c.Text, "làm được nửa việc thì kim tới nửa khung");
            c.SetProgress(0.2f);
            c.Tick(100f);
            Assert.AreEqual("4:30", c.Text, "việc phát sinh không làm kim quay lui");
            c.Running = false;
            c.SetProgress(1f);
            c.Tick(100f);
            Assert.AreEqual("4:30", c.Text, "đồng hồ dừng thì kim đứng");
            c.Running = true;
            c.Tick(0.1f);
            Assert.Less(c.Minutes, 300f, "kim đuổi tới giờ đích mượt, không nhảy");
            c.Tick(100f);
            Assert.AreEqual("5:00", c.Text);
            c.SetWindow(200f, 260f);
            Assert.AreEqual(300f, c.From, "khung mới không lùi giờ");
            Assert.IsTrue(c.IsPast(300));
            Assert.IsFalse(c.IsPast(301));
            Assert.IsFalse(c.IsPast(0), "0 = không có giờ phục vụ");
            Assert.AreEqual("19:05", ShiftClockLogic.Format(19 * 60 + 5));
            Assert.AreEqual("0:30", ShiftClockLogic.Format(24 * 60 + 30));
        }

        [Test]
        public void BoardProgressCountsUnitsOfWork()
        {
            var b = new OrderBoardLogic();
            Assert.AreEqual(1f, ShiftClockLogic.BoardProgress(b), "bảng trống coi như xong");
            b.Add(OrderKinds.Sweep, 1);
            b.Add(OrderKinds.Fire, 3);
            Assert.AreEqual(0f, ShiftClockLogic.BoardProgress(b));
            b.Report(OrderKinds.Fire, 2);
            Assert.AreEqual(0.5f, ShiftClockLogic.BoardProgress(b), 1e-4f);
            b.Report(OrderKinds.Sweep, 1);
            b.Report(OrderKinds.Fire, 1);
            Assert.AreEqual(1f, ShiftClockLogic.BoardProgress(b));
        }

        [Test]
        public void EveryShiftEndsByNineAtNightAndMealsFitTheHobs()
        {
            var db = LoadContent();
            foreach (var day in db.Days.Days)
            {
                float clock = day.ClockStart;
                foreach (var phase in day.Phases)
                {
                    float from = Mathf.Max(clock, phase.StartAt);
                    float to = ShiftDirector.PhaseEnd(phase, from);
                    Assert.Greater(to, from - 0.01f, $"{day.Id}/{phase.Id}: khung giờ ngược");
                    Assert.LessOrEqual(to, 21 * 60, $"{day.Id}/{phase.Id}: ca phải xong trước 9 giờ tối (S16)");
                    Assert.LessOrEqual(phase.Dirty, 3, $"{day.Id}/{phase.Id}: số nồi nấu không quá số nồi đặt trên ô bếp");
                    if (phase.Montage)
                        Assert.IsFalse(string.IsNullOrEmpty(db.GetString(phase.Caption)), $"{day.Id}/{phase.Id}: montage thiếu caption");
                    clock = to;
                }
            }
        }

        // ---------- Rửa nồi ----------

        [Test]
        public void WashNeedsPileThenScrubThenRack()
        {
            var w = new WashLogic();
            Assert.IsFalse(w.TakeToBasin(), "chưa có nồi bẩn");
            w.AddDirty(2);
            Assert.IsTrue(w.TakeToBasin());
            Assert.IsFalse(w.TakeToBasin(), "chậu chỉ một chiếc");
            Assert.IsFalse(w.Rack(), "chưa sạch thì chưa úp");
            for (int i = 0; i < 2000 && !w.BasinClean; i++)
                w.Tick(true, true, new Vector2(300f, 200f), 0.1f);
            Assert.IsTrue(w.BasinClean);
            Assert.IsTrue(w.Rack());
            Assert.AreEqual(1, w.Washed);
            Assert.AreEqual(1, w.Pending);
            Assert.IsTrue(w.TakeToBasin());
            Assert.AreEqual(0f, w.Scrub.Progress, "nồi mới bẩn lại từ đầu");
            Assert.IsTrue(w.CompleteOne());
            Assert.AreEqual(2, w.Washed);
        }

        // ---------- Thể lực ----------

        [Test]
        public void FatigueRisesWithWorkAndRecoversOnlyToTheFloor()
        {
            var b = new BodyLogic();
            b.Begin(0.2f, 0.2f);
            for (int i = 0; i < 40; i++)
                b.Exert(0.02f);
            Assert.AreEqual(1f, b.Fatigue, 1e-4f, "có trần");
            Assert.Less(b.SpeedMultiplier, 0.75f);
            Assert.GreaterOrEqual(b.SpeedMultiplier, 0.7f - 1e-4f, "không bao giờ chặn hẳn");
            for (int i = 0; i < 20000; i++)
                b.Tick(0.1f, 0f, busy: false, exertPerSecond: 0f);
            Assert.AreEqual(0.2f, b.Fatigue, 1e-3f, "nghỉ không hồi xuống dưới mức nền của ngày");
            Assert.AreEqual(1f, b.SpeedMultiplier, 1e-3f);
        }

        [Test]
        public void SittingRecoversAtOnceAndFasterThanStanding()
        {
            var standing = new BodyLogic();
            var seated = new BodyLogic { Seated = true };
            standing.Begin(0f, 0.8f);
            seated.Begin(0f, 0.8f);
            for (int i = 0; i < 20; i++)
            {
                standing.Tick(0.1f, 0f, busy: false, exertPerSecond: 0f);
                seated.Tick(0.1f, 0f, busy: false, exertPerSecond: 0f);
            }
            Assert.AreEqual(0.8f, standing.Fatigue, 1e-4f, "đứng nghỉ phải chờ RestDelay");
            Assert.Less(seated.Fatigue, 0.8f, "ngồi thì hồi ngay");
            for (int i = 0; i < 100; i++)
            {
                standing.Tick(0.1f, 0f, busy: false, exertPerSecond: 0f);
                seated.Tick(0.1f, 0f, busy: false, exertPerSecond: 0f);
            }
            Assert.Less(0.8f - standing.Fatigue, (0.8f - seated.Fatigue) / 2f);
        }

        [Test]
        public void BusyBodyDoesNotRecover()
        {
            var b = new BodyLogic();
            b.Begin(0f, 0.6f);
            for (int i = 0; i < 1000; i++)
                b.Tick(0.1f, 0f, busy: true, exertPerSecond: 0f);
            Assert.AreEqual(0.6f, b.Fatigue, 1e-4f);
        }

        [Test]
        public void HeatFollowsSurroundingsAndExtremesTire()
        {
            var hot = new BodyLogic();
            hot.Begin(0f, 0f);
            var mild = new BodyLogic();
            mild.Begin(0f, 0f);
            for (int i = 0; i < 600; i++)
            {
                hot.Tick(0.1f, 1f, busy: true, exertPerSecond: 0f);
                mild.Tick(0.1f, 0.2f, busy: true, exertPerSecond: 0f);
            }
            Assert.Greater(hot.Heat, 0.95f);
            Assert.Greater(hot.Warmth, 0.9f);
            Assert.Greater(hot.Sweat, 0.9f);
            Assert.Greater(hot.Fatigue, mild.Fatigue, "sát lò lâu thì mệt hơn");
            var cold = new BodyLogic();
            cold.Begin(0f, 0f);
            for (int i = 0; i < 600; i++)
                cold.Tick(0.1f, -1f, busy: true, exertPerSecond: 0f);
            Assert.Greater(cold.Chill, 0.9f);
            Assert.Less(cold.WorkSpeed, 1f, "lạnh cóng thì tay chậm");
        }

        // ---------- Từ vựng ----------

        [Test]
        public void VocabularyShowsFrenchUntilLearned()
        {
            var words = new Dictionary<string, FrenchPair>
            {
                ["feu"] = new FrenchPair { Id = "feu", Fr = "le feu", Vi = "lửa" },
                ["charbon"] = new FrenchPair { Id = "charbon", Fr = "le charbon", Vi = "than" },
            };
            FrenchPair Lookup(string id) => words.TryGetValue(id, out var w) ? w : null;
            var known = new HashSet<string>();
            const string line = "Thêm {w:charbon} đi, {w:feu} yếu rồi!";
            Assert.AreEqual("Thêm <i>le charbon</i> đi, <i>le feu</i> yếu rồi!", Vocabulary.Render(line, Lookup, known.Contains));
            known.Add("charbon");
            Assert.AreEqual("Thêm than đi, <i>le feu</i> yếu rồi!", Vocabulary.Render(line, Lookup, known.Contains));
            Assert.AreEqual("Thêm le charbon đi, le feu yếu rồi!", Vocabulary.Longest(line, Lookup));
            CollectionAssert.AreEqual(new[] { "charbon", "feu" }, new List<string>(Vocabulary.TokensIn(line)));
            Assert.AreEqual("{w:khong_co}", Vocabulary.Render("{w:khong_co}", Lookup, _ => true), "từ lạ giữ nguyên để dễ thấy lỗi");
            Assert.AreEqual("Không có token", Vocabulary.Render("Không có token", Lookup, _ => false));
        }

        // ---------- Nội dung ngày ----------

        [Test]
        public void DaysStartAtFourSweepThenLightAColdStove()
        {
            var db = LoadContent();
            Assert.GreaterOrEqual(db.Days.Days.Count, 1);
            foreach (var day in db.Days.Days)
            {
                Assert.AreEqual(240, day.ClockStart, $"{day.Id}: ca bắt đầu 4 giờ sáng (F04)");
                var first = day.Phases[0];
                if (day.Number == 1)
                {
                    // Ngày đầu làm thật từng việc 4 giờ sáng theo S16: quét bếp rồi mới nhóm lò.
                    Assert.IsTrue(first.Orders.Exists(o => o.Task == OrderKinds.Sweep), $"{day.Id}: quét sạch bếp trước nhóm lò theo S16");
                    Assert.IsTrue(day.Phases[1].Orders.Exists(o => o.Task == OrderKinds.Fire), $"{day.Id}: lượt thứ hai nhóm lò");
                }
                else
                {
                    // Các ngày sau tua nhanh việc 4 giờ sáng (quét, cào tro, nhóm lửa, khuân than) bằng một montage.
                    Assert.IsTrue(first.Montage && first.Orders.Exists(o => o.Task == OrderKinds.Fire), $"{day.Id}: montage 4 giờ sáng nhóm lò");
                    StringAssert.Contains("quét", db.GetString(first.Caption), $"{day.Id}: montage kể việc quét bếp");
                }
            }
            Assert.IsNotNull(db.GetDay(GameState.FirstDay));
        }

        [Test]
        public void EveryWordTaughtInTheEveningAppearsInSomeOrderOrLine()
        {
            var db = LoadContent();
            var used = new HashSet<string>();
            foreach (var l in db.Subtitles.Lines)
                foreach (var t in Vocabulary.TokensIn(l.Vi))
                    used.Add(t);
            foreach (var kv in db.Ui.Strings)
                foreach (var t in Vocabulary.TokensIn(kv.Value))
                    used.Add(t);
            foreach (var w in db.WordsInSet("w1"))
                if (w.Id != "mer")
                    Assert.IsTrue(used.Contains(w.Id), $"từ '{w.Id}' học xong phải giúp đọc lệnh");
        }

        // ---------- Bản lưu ----------

        [Test]
        public void SaveRoundTripRestoresProgress()
        {
            var dir = Path.Combine(Path.GetTempPath(), "hbt_save_" + System.Guid.NewGuid().ToString("N"));
            var oldFolder = SaveSystem.Folder;
            try
            {
                SaveSystem.Folder = dir;
                var db = LoadContent();
                var state = new GameState { Day = 2, MaxDay = 2, Grime = 0.45f, Fatigue = 0.3f };
                state.Learn("feu");
                state.PendingWords.Add("glace");
                var flags = new GameFlags();
                flags.Set(EventIds.FlagS1Lingered);
                var events = new GameEvents();
                var notebook = new NotebookController(db, events, flags);
                events.Raise(EventIds.S1Depart);
                Assert.IsTrue(notebook.IsUnlocked("p_departure"));
                Assert.IsTrue(SaveSystem.Write(SaveSystem.Capture(SceneFlow.GameScenes[1], state, flags, notebook, events)));
                Assert.IsTrue(SaveSystem.HasSave);

                var data = SaveSystem.Read();
                Assert.IsNotNull(data);
                var state2 = new GameState();
                var flags2 = new GameFlags();
                var events2 = new GameEvents();
                var notebook2 = new NotebookController(db, events2, flags2);
                int toasts = 0;
                notebook2.PageUnlocked += _ => toasts++;
                SaveSystem.Apply(data, state2, flags2, notebook2, events2);
                Assert.AreEqual(2, state2.Day);
                Assert.AreEqual(0.45f, state2.Grime, 1e-4f);
                Assert.IsTrue(state2.Knows("feu"));
                CollectionAssert.AreEqual(new[] { "glace" }, state2.PendingWords, "từ còn nợ học tối sau");
                Assert.IsTrue(flags2.Has(EventIds.FlagS1Lingered));
                Assert.IsTrue(events2.HasFired(EventIds.S1Depart));
                Assert.IsTrue(notebook2.IsUnlocked("p_departure"));
                Assert.AreEqual(0, toasts, "nạp bản lưu không hiện thông báo trang mới");
                Assert.AreEqual(SceneFlow.GameScenes[1], data.Scene);
                SaveSystem.Delete();
                Assert.IsFalse(SaveSystem.HasSave);
            }
            finally
            {
                SaveSystem.Folder = oldFolder;
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }

        [Test]
        public void CorruptSaveIsIgnored()
        {
            var dir = Path.Combine(Path.GetTempPath(), "hbt_save_" + System.Guid.NewGuid().ToString("N"));
            var oldFolder = SaveSystem.Folder;
            try
            {
                SaveSystem.Folder = dir;
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "save.json"), "{ không phải json");
                Assert.IsNull(SaveSystem.Read());
            }
            finally
            {
                SaveSystem.Folder = oldFolder;
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }
    }
}
