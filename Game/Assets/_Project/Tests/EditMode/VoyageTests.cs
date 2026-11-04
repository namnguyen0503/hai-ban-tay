using System.Collections.Generic;
using System.IO;
using System.Linq;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Shift;
using HaiBanTay.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>M11: sáu ngày trên tàu (days.vi.json), kéo rổ rau bằng dây sắt ngày bão (HaulLogic), nhặt rau có người giúp.</summary>
    public class VoyageTests
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

        // ---------- Sáu ngày ----------

        [Test]
        public void SixDaysFromTheSouthChinaSeaToNearMarseille()
        {
            var db = LoadContent();
            Assert.IsEmpty(db.FindBrokenReferences(), "nội dung hợp lệ");
            var days = db.Days.Days;
            Assert.AreEqual(6, days.Count);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, days.Select(d => d.Number));
            var transitions = new HashSet<string>(db.MapTransitions.Transitions.Select(t => t.Id));
            foreach (var day in days.Skip(1))
                Assert.IsTrue(transitions.Contains(day.Transition), $"{day.Id}: mỗi ngày một chặng bản đồ");
            Assert.IsTrue(transitions.Contains("B"), "ngày cuối sang 03 qua bản đồ B");
            foreach (var day in days)
            {
                Assert.AreEqual(240, day.ClockStart, day.Id);
                Assert.IsNotNull(day.Evening, day.Id);
                // S16: "trong khi chúng tôi nghỉ hoặc đánh bài, anh Ba đọc hay viết đến mười một giờ": tối nào cũng học, kể cả
                // tối đầu tiên (tự chép chữ, chưa có người dạy).
                Assert.IsTrue(day.Evening.HasStudy, $"{day.Id}: tối nào cũng học");
            }
            var kinds = days.Where(d => d.Evening.HasStudy).Select(d => d.Evening.Study).Distinct().ToList();
            CollectionAssert.IsSubsetOf(new[] { EveningData.Trace, EveningData.Match, EveningData.Fill, EveningData.Sentence }, kinds,
                "đủ bốn kiểu học");
        }

        [Test]
        public void EachDayHasItsOwnCharacter()
        {
            var db = LoadContent();
            var d2 = db.GetDay(2);
            Assert.IsTrue(d2.Docked, "Ngày 2 tàu đỗ ở Singapore (F02)");
            Assert.AreEqual("singapore", d2.Port);
            Assert.AreEqual(0f, d2.Ship, 1e-4f, "tàu đỗ không lắc");
            Assert.Contains(EventIds.S2PortSingapore, d2.StartEvents);
            Assert.IsTrue(d2.Npcs.Exists(b => b.Who == "soldiers" && b.Action == "help" && b.Event == EventIds.S2SoldiersHelp),
                "hai lính giúp nhặt rau (F05)");
            Assert.AreEqual(EveningData.Trace, d2.Evening.Study);

            var d3 = db.GetDay(3);
            Assert.Greater(d3.Ship, 0.5f, "Ấn Độ Dương lắc mạnh");
            Assert.Greater(d3.AshRate, 1f, "than nhiều tro");
            Assert.IsTrue(d3.Phases.Exists(p => p.Orders.Exists(o => o.Task == OrderKinds.Pan)), "chảo đồng (F04)");

            var d4 = db.GetDay(4);
            Assert.IsTrue(d4.Phases.Exists(p => p.Storm));
            var haul = d4.Phases.Find(p => p.Orders.Exists(o => o.Task == OrderKinds.Haul));
            Assert.IsNotNull(haul, "Ngày 4 kéo rổ rau bằng dây sắt (F05b)");
            Assert.IsTrue(haul.DeckWave, "sóng phủ boong khi kéo rổ");
            Assert.IsFalse(d4.DeckCrowd, "ngày bão không ai đứng boong");
            Assert.IsTrue(d4.Npcs.Exists(b => b.Who == "jacques" && b.Action == "help" && b.Until == "calm"));
            Assert.IsTrue(d4.Phases.Exists(p => p.Orders.Exists(o => o.Task == OrderKinds.ScrubTall)), "Jacques giữ nồi cao");

            var d5 = db.GetDay(5);
            Assert.AreEqual(3, d5.Phases.Count(p => p.ServiceAt > 0), "Ngày 5 ba bữa");
            // F04 (700–800 người): kể bằng montage ba bữa, đống nồi bày và thẻ quy mô, không bắt rửa 22 nồi.
            Assert.IsTrue(d5.Phases.Count(p => p.Montage) >= 2, "Ngày 5 tua nhanh các bữa");
            StringAssert.Contains("bảy, tám trăm", db.GetString("montage.lunch"));
            Assert.IsTrue(d5.Phases.Exists(p => p.Events.Contains(EventIds.S2PileHigh)));

            var d6 = db.GetDay(6);
            Assert.Less(d6.Ship, 0.1f, "Địa Trung Hải êm");
            Assert.IsTrue(d6.Evening.Book, "lính cho mượn sách (F07a)");
            Assert.AreEqual(EveningData.Sentence, d6.Evening.Study);
        }

        [Test]
        public void SoldiersJacquesAndMaiAllSpeak()
        {
            var db = LoadContent();
            var speakers = new HashSet<string>();
            foreach (var day in db.Days.Days)
                foreach (var beat in day.Npcs)
                    foreach (var line in beat.Lines)
                        speakers.Add(db.GetLine(line).Speaker);
            foreach (var who in new[] { "linh_tre", "linh_mu", "jacques", "ong_mai", "ba" })
                Assert.IsTrue(speakers.Contains(who), "có thoại: " + who);
            Assert.AreEqual("Tôi có thể làm bất cứ việc gì!", db.GetLine("ba_d1_2").Vi, "câu S16 lấy nguyên văn");
        }

        [Test]
        public void EveryWordTaughtInTheEveningsHelpsReadOrders()
        {
            var db = LoadContent();
            var used = new HashSet<string>();
            foreach (var l in db.Subtitles.Lines)
                foreach (var t in Vocabulary.TokensIn(l.Vi))
                    used.Add(t);
            foreach (var kv in db.Ui.Strings)
                foreach (var t in Vocabulary.TokensIn(kv.Value))
                    used.Add(t);
            var taught = new HashSet<string>();
            foreach (var day in db.Days.Days)
                foreach (var w in ChapterStart.WordsOf(db, day))
                    taught.Add(w);
            Assert.GreaterOrEqual(taught.Count, 20, "sáu buổi tối dạy đủ từ cho bếp");
            foreach (var w in taught)
                Assert.IsTrue(used.Contains(w), $"từ '{w}' học xong phải giúp đọc lệnh hoặc thoại");
        }

        [Test]
        public void ChapterSelectReplaysEarlierDaysEventsAndWords()
        {
            var db = LoadContent();
            var d4 = ChapterStart.EventsOf(db.GetDay(4));
            Assert.Contains(EventIds.S2StormWave, d4);
            Assert.Contains(EventIds.S2HaulDone, d4);
            Assert.Contains(EventIds.S2SailorHelp, d4);
            CollectionAssert.Contains(ChapterStart.EventsOf(db.GetDay(6)), EventIds.S2BookLent);
            CollectionAssert.Contains(ChapterStart.EventsOf(db.GetDay(1)), EventIds.S2MaiTalk);
            CollectionAssert.IsSupersetOf(ChapterStart.WordsOf(db, db.GetDay(1)), new[] { "pain", "eau" });
            CollectionAssert.IsSupersetOf(ChapterStart.WordsOf(db, db.GetDay(2)), new[] { "feu", "mer", "sel" });
        }

        [Test]
        public void S16DailySequenceAndNewScenesHaveEvidence()
        {
            var db = LoadContent();
            // Ngày 1 làm thật cả chuỗi việc mỗi ngày theo S16; các ngày sau giữ chuỗi đó bằng montage + một hai việc chính.
            var d1 = db.GetDay(1);
            CollectionAssert.AreEqual(new[] { "sweep", "dawn", "coal", "breakfast", "serve" }, d1.Phases.Take(5).Select(p => p.Id));
            Assert.AreEqual(OrderKinds.Sweep, d1.Phases[0].Orders.Single().Task);
            Assert.IsTrue(d1.Phases[1].Orders.Any(o => o.Task == OrderKinds.Fire));
            Assert.AreEqual(OrderKinds.CoalFetch, d1.Phases[2].Orders.Single().Task);
            Assert.AreEqual(OrderKinds.Serve, d1.Phases[4].Orders.Single().Task);
            Assert.IsTrue(d1.Phases.All(p => !p.Montage || p.ServiceAt > 0), "ngày 1 chỉ tua nhanh các bữa trưa, tối");
            foreach (var day in db.Days.Days)
            {
                Assert.IsTrue(day.Phases.Any(p => p.Id == "breakfast"), day.Id + ": bữa sáng");
                Assert.IsTrue(day.Phases.Any(p => p.Orders.Any(o => o.Task == OrderKinds.Serve)) || day.Number == 3 || day.Number == 2,
                    day.Id + ": dọn bữa cho các chủ bếp");
                Assert.IsTrue(day.Phases.Any(p => !p.Montage), day.Id + ": ngày nào cũng có việc làm thật");
            }
            Assert.AreEqual(2, db.GetDay(4).Phases.Single(p => p.DeckWave).Orders.Single(o => o.Task == OrderKinds.Haul).Count);
            Assert.GreaterOrEqual(db.GetDay(1).Phases.Single(p => p.Id == "peel").Orders.Single().Count, 6, "gọt hết xô khoai, không phải một củ");
            Assert.IsTrue(db.GetDay(5).Phases.Any(p => p.Orders.Any(o => o.Task == OrderKinds.Asparagus)));
            Assert.IsTrue(db.GetDay(3).Evening.Coffee);
            Assert.AreEqual("l_quoc_ngu", db.GetDay(5).Evening.StoryLesson);
            Assert.AreEqual("vi", db.GetLesson("l_quoc_ngu").Language);
            Assert.AreEqual("l_letter", db.GetDay(6).Evening.StoryLesson);
            Assert.AreEqual("vi", db.GetLesson("l_letter").Language);
            Assert.AreEqual("Anh Mai, cũng có những người Pháp tốt, anh ạ.", db.GetLine("ba_good_french").Vi);
            Assert.AreEqual("Tại sao con lại làm cái nghề khó nhọc này? Bỏ nghề này đi. Con nên chọn một nghề khác, danh giá hơn...",
                string.Join(" ", new[] { "chieu_1", "chieu_2", "chieu_3" }.Select(id => db.GetLine(id).Vi)));
            foreach (var id in new[] { "sc_water", "sc_provisions", "sc_serve", "sc_peel", "sc_asparagus", "sc_good_french", "sc_quoc_ngu", "sc_coffee", "sc_letter", "sc_soup", "sc_chieu", "sc_night", "sc_mai_help" })
            {
                var card = db.SourceCards.Cards.Single(c => c.Id == id);
                Assert.IsTrue(card.Citation.Contains("S16")); Assert.IsNotEmpty(card.FactIds);
                Assert.IsTrue(db.Notebook.Pages.Any(p => p.SourceCardIds.Contains(id)), "chi tiết phải có trang sổ: " + id);
            }
        }

        // ---------- Kéo rổ rau ----------

        /// <summary>Sàn hầm 2 m → thang (cao 2,8 m trên 3,5 m) → boong 3 m.</summary>
        static HaulLogic HoldToDeck() => new HaulLogic(new[]
        {
            new Vector3(0f, -2.8f, 0f), new Vector3(2f, -2.8f, 0f), new Vector3(5.5f, 0f, 0f), new Vector3(8.5f, 0f, 0f),
        });

        /// <summary>Người chơi giữ dây, đi dọc đường từ ngay trước rổ tới quãng s rồi đứng đó kéo thêm 3 s.</summary>
        static void WalkTo(HaulLogic h, float s)
        {
            for (float p = h.Basket + 0.5f; p <= s; p += 0.03f)
                h.Tick(h.PointAt(p), true, 0.05f);
            Pull(h, h.PointAt(s), 3f);
        }

        static void Pull(HaulLogic h, Vector3 player, float seconds, bool pull = true)
        {
            for (float t = 0f; t < seconds; t += 0.05f)
                h.Tick(player, pull, 0.05f);
        }

        [Test]
        public void BasketMustBeTiedBeforeItMoves()
        {
            var h = HoldToDeck();
            Assert.AreEqual(HaulEvent.None, h.Tick(new Vector3(1.8f, -2.8f, 0f), true, 0.1f));
            Assert.AreEqual(0f, h.Basket);
            Assert.IsTrue(h.Tie());
            Assert.IsFalse(h.Tie(), "buộc rồi");
            Assert.IsTrue(h.OnStairs(3f));
            Assert.IsFalse(h.OnStairs(1f));
        }

        [Test]
        public void PullingKeepsTheBasketOneRopeBehind()
        {
            var h = HoldToDeck();
            h.Tie();
            Pull(h, new Vector3(1.8f, -2.8f, 0f), 2f);
            Assert.IsTrue(h.Holding);
            Assert.AreEqual(1.8f - h.Rope, h.Basket, 0.01f, "rổ theo sau một đoạn dây");
            Pull(h, new Vector3(1.8f, -2.8f, 3f), 1f);
            Assert.IsFalse(h.Holding, "đứng xa dây thì không kéo được");
            Pull(h, new Vector3(1.8f, -2.8f, 0f), 0.2f, pull: false);
            Assert.IsFalse(h.Holding);
        }

        [Test]
        public void ReleasingOnTheStairsLetsTheBasketSlideBackToTheFoot()
        {
            var h = HoldToDeck();
            h.Tie();
            // Người chơi lên gần đầu thang, rổ đã lên giữa thang.
            var top = h.PointAt(h.DistanceAt(2) - 0.1f);
            WalkTo(h, h.DistanceAt(2) - 0.1f);
            Assert.AreEqual(h.DistanceAt(2) - 0.1f - h.Rope, h.Basket, 0.02f);
            Assert.IsTrue(h.OnStairs(h.Basket), "rổ đang trên thang");
            float onStairs = h.Basket;
            Pull(h, top, 3f, pull: false);
            Assert.Less(h.Basket, onStairs, "buông dây thì rổ tụt");
            Pull(h, top, 30f, pull: false);
            Assert.AreEqual(h.DistanceAt(1), h.Basket, 0.01f, "tụt tới chân thang thì dừng");
        }

        [Test]
        public void BasketArrivesAndWaveShovesItBack()
        {
            var h = HoldToDeck();
            h.Tie();
            var e = HaulEvent.None;
            // Đi dọc đường, luôn đi trước rổ; tới cuối đường thì đứng thu dây.
            for (float s = 0.5f; s <= h.Length && e != HaulEvent.Arrived; s += 0.03f)
                e = h.Tick(h.PointAt(s), true, 0.05f);
            for (int i = 0; i < 100 && e != HaulEvent.Arrived; i++)
                e = h.Tick(h.PointAt(h.Length), true, 0.05f);
            Assert.AreEqual(HaulEvent.Arrived, e);
            Assert.IsTrue(h.Arrived);

            var g = HoldToDeck();
            g.Tie();
            WalkTo(g, g.Length - 0.5f);
            float before = g.Basket;
            Assert.Greater(before, 2f);
            g.Release();
            g.Shove(2f);
            Assert.AreEqual(before - 2f, g.Basket, 0.01f);
            g.Reset();
            Assert.IsFalse(g.Tied);
            Assert.AreEqual(0f, g.Basket);
        }

        // ---------- Nhặt rau có người giúp ----------

        [Test]
        public void SoldiersSortWholeBunchesFromTheCrate()
        {
            var s = new SortLogic();
            Assert.IsFalse(s.HelperBunch());
            s.AddBunches(2);
            Assert.IsTrue(s.TakeBunch());
            Assert.IsTrue(s.HelperBunch(), "người giúp nhặt bó còn trong sọt");
            Assert.AreEqual(0, s.Pending);
            Assert.IsTrue(s.HasBunch, "bó của người chơi trên thớt không bị lấy");
            Assert.IsFalse(s.HelperBunch());
            Assert.AreEqual(1, s.Bunches);
        }
    }
}
