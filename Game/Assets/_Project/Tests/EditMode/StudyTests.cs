using System.Collections.Generic;
using System.IO;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>Học buổi tối (M11): tô chữ, điền từ, viết câu, kế hoạch học theo giờ tan ca và từ còn nợ.</summary>
    public class StudyTests
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

        // ---------- Tô chữ ----------

        static TraceLogic LetterL()
        {
            var glyph = new List<Vector2[]> { new[] { new Vector2(0.3f, 0.1f), new Vector2(0.3f, 0.9f), new Vector2(0.75f, 0.9f) } };
            return new TraceLogic(new[] { "L", "L" }, new[] { glyph, glyph });
        }

        [Test]
        public void TracingAlongTheGuideCompletesLetterByLetter()
        {
            var t = LetterL();
            Assert.AreEqual(2, t.Count);
            Assert.AreEqual("L", t.Current);
            t.BeginStroke(new Vector2(0.31f, 0.1f));
            Assert.AreEqual(TraceEvent.None, t.AddPoint(new Vector2(0.31f, 0.5f)));
            Assert.Less(t.Progress, 1f, "mới tô nửa nét đứng");
            var e = TraceEvent.None;
            for (float y = 0.5f; y <= 0.9f && e == TraceEvent.None; y += 0.02f)
                e = t.AddPoint(new Vector2(0.3f, y));
            for (float x = 0.3f; x <= 0.76f && e == TraceEvent.None; x += 0.02f)
                e = t.AddPoint(new Vector2(x, 0.9f));
            t.EndStroke();
            Assert.AreEqual(TraceEvent.LetterDone, e);
            Assert.AreEqual(1, t.LettersDone);
            Assert.IsEmpty(t.Ink, "sang chữ mới thì xóa mực");
            Assert.AreEqual(TraceEvent.AllDone, t.SimulateLetter());
            Assert.IsTrue(t.Done);
        }

        [Test]
        public void ScribblingAwayFromTheGuideDoesNotCount()
        {
            var t = LetterL();
            t.BeginStroke(new Vector2(0.8f, 0.1f));
            for (float y = 0.1f; y <= 0.5f; y += 0.02f)
                Assert.AreEqual(TraceEvent.None, t.AddPoint(new Vector2(0.8f, y)));
            t.EndStroke();
            Assert.AreEqual(0, t.LettersDone);
            Assert.Greater(t.StrayPoints, 0);
            Assert.AreEqual(0f, t.Progress, 1e-4f);
            t.ClearInk();
            Assert.IsEmpty(t.Ink);
        }

        [Test]
        public void EveryTraceLessonLetterHasStrokes()
        {
            var db = LoadContent();
            foreach (var lesson in db.Study.Lessons)
            {
                if (lesson.Kind != EveningData.Trace)
                    continue;
                var trace = StudyPlanner.BuildTrace(db, lesson.Items);
                foreach (var it in lesson.Items)
                    foreach (char c in it.Text)
                        Assert.IsTrue(db.TryGetLetter(c.ToString(), out _), $"{lesson.Id}/{it.Id}: chữ '{c}' chưa có nét để tô");
                // Mỗi mục là một lượt tô cả từ (QA-0140), ô rộng theo từ.
                Assert.AreEqual(lesson.Items.Count, trace.Count, lesson.Id + ": mỗi từ một lượt tô");
                for (int i = 0; !trace.Done; i++)
                {
                    Assert.Greater(trace.Aspect, lesson.Items[i].Text.Length * 0.3f, lesson.Items[i].Id + ": ô tô đủ rộng cho cả từ");
                    foreach (var stroke in trace.Guide)
                        foreach (var q in stroke)
                            Assert.IsTrue(q.x >= 0f && q.x <= trace.Aspect && q.y >= 0f && q.y <= 1f, lesson.Items[i].Id + ": nét nằm trong ô");
                    Assert.AreNotEqual(TraceEvent.None, trace.SimulateLetter());
                }
            }
        }

        // ---------- Điền từ ----------

        [Test]
        public void FillNeedsTheRightWordAndShowsTheFilledSentence()
        {
            var items = new[]
            {
                new FillLogic.Item { Prompt = "Lave ___ du capitaine.", Translation = "Rửa đĩa.", Options = new[] { "l'assiette", "le livre", "la corde" }, Answer = 0 },
                new FillLogic.Item { Prompt = "Il fait ___ ici.", Translation = "Nóng.", Options = new[] { "vite", "chaud" }, Answer = 1 },
            };
            var f = new FillLogic(items);
            Assert.AreNotEqual(0, f.Order[0], "đáp án không luôn đứng đầu");
            Assert.IsFalse(f.TryAnswer(2));
            Assert.IsTrue(f.WasWrong(2));
            Assert.IsFalse(f.TryAnswer(2), "chọn lại lựa chọn sai vẫn sai");
            Assert.AreEqual(1, f.WrongAttempts, "lựa chọn sai chỉ tính một lần");
            Assert.AreEqual("Lave l'assiette du capitaine.", FillLogic.Filled(f.Current));
            Assert.IsTrue(f.TryAnswer(0));
            Assert.AreEqual(1, f.Index);
            f.SimulateAnswer();
            Assert.IsTrue(f.Done);
        }

        [Test]
        public void FillLessonOptionsShowFrenchWords()
        {
            var db = LoadContent();
            var lesson = db.GetLesson("l_fill");
            var fill = StudyPlanner.BuildFill(db, lesson.Items);
            Assert.AreEqual(lesson.Items.Count, fill.Count);
            Assert.AreEqual("Va à la chambre froide chercher la viande.", FillLogic.Filled(fill.Current));
            while (!fill.Done)
                fill.SimulateAnswer();
        }

        // ---------- Viết câu ----------

        [Test]
        public void SentenceIsWrittenWordByWordInOrder()
        {
            var s = new SentenceLogic(new[]
            {
                new SentenceLogic.Item { Tokens = new[] { "Je", "lis", "un", "livre." }, Translation = "Tôi đọc sách." },
                new SentenceLogic.Item { Tokens = new[] { "la", "la", "mer" }, Translation = "thử từ trùng" },
            });
            var shuffled = s.Shuffled;
            Assert.AreEqual(4, shuffled.Length);
            CollectionAssert.AreNotEqual(new[] { 0, 1, 2, 3 }, shuffled, "các từ bị xáo");
            Assert.IsFalse(s.TryPlace(3), "chưa tới chữ cuối");
            Assert.AreEqual(1, s.WrongAttempts);
            Assert.IsTrue(s.TryPlace(0));
            Assert.IsFalse(s.TryPlace(0), "một từ chỉ dùng một lần");
            Assert.IsTrue(s.TryPlace(1));
            Assert.AreEqual("Je lis", s.Written);
            Assert.IsTrue(s.TryPlace(2));
            Assert.IsTrue(s.TryPlace(3));
            Assert.AreEqual(1, s.Index, "xong câu đầu");
            // Hai từ giống nhau: bấm từ nào cũng được.
            Assert.IsTrue(s.TryPlace(1));
            Assert.IsTrue(s.TryPlace(0));
            Assert.IsTrue(s.TryPlace(2));
            Assert.IsTrue(s.Done);
        }

        // ---------- Kế hoạch học ----------

        [Test]
        public void LateShiftMeansFewerItemsButNeverNone()
        {
            Assert.AreEqual(21 * 60, StudyPlanner.EndOfShift(14 * 60, 0), "xong sớm vẫn làm tới 9 giờ tối (F04)");
            Assert.AreEqual(21 * 60 + 40, StudyPlanner.EndOfShift(14 * 60, 2));
            Assert.AreEqual(21 * 60 + 30, StudyPlanner.EndOfShift(21 * 60 + 30, 0), "đồng hồ đã quá 9 giờ thì lấy giờ đồng hồ");
            Assert.AreEqual(22 * 60, StudyPlanner.EveningStart(21 * 60));
            Assert.AreEqual(5, StudyPlanner.ItemsFor(5, 22 * 60));
            Assert.AreEqual(3, StudyPlanner.ItemsFor(5, 22 * 60 + 40));
            Assert.AreEqual(1, StudyPlanner.ItemsFor(5, 24 * 60 + 30));
        }

        [Test]
        public void ItemsCutByALateShiftCarryOverToTheNextEvening()
        {
            var db = LoadContent();
            var state = new GameState();
            var fillEvening = new EveningData { Study = EveningData.Fill, Lesson = "l_fill", Items = 5 };
            // Hai lượt phục vụ trễ: tối bắt đầu 22:40, học 3/5 câu.
            var plan = StudyPlanner.Make(db, state, fillEvening, StudyPlanner.EveningStart(StudyPlanner.EndOfShift(14 * 60, 2)));
            Assert.AreEqual(3, plan.Items.Count);
            CollectionAssert.AreEqual(new[] { "poisson", "glace" }, plan.Deferred);
            StudyPlanner.Finish(plan, state);
            Assert.IsTrue(state.Knows("froide"));
            Assert.IsFalse(state.Knows("poisson"));
            CollectionAssert.AreEqual(new[] { "poisson", "glace" }, state.PendingWords);

            // Tối sau (đúng giờ): ôn hai từ nợ trước, rồi bài mới.
            var sentenceEvening = new EveningData { Study = EveningData.Sentence, Lesson = "l_sentence", Items = 3 };
            var next = StudyPlanner.Make(db, state, sentenceEvening, StudyPlanner.EveningStart(21 * 60));
            Assert.AreEqual(2, next.Review.Count);
            Assert.AreEqual(1, next.Items.Count, "ôn từ nợ chiếm chỗ, vẫn còn ít nhất một câu mới");
            StudyPlanner.Finish(next, state);
            Assert.IsTrue(state.Knows("poisson") && state.Knows("glace"));
            Assert.IsEmpty(state.PendingWords.FindAll(w => w == "poisson" || w == "glace"));
            Assert.Greater(state.PendingWords.Count, 0, "câu bị bớt lại dồn sang tối sau");
        }

        [Test]
        public void MatchEveningTeachesTheWholeSetWhenOnTime()
        {
            var db = LoadContent();
            var state = new GameState();
            var ev = new EveningData { Study = EveningData.Match, Set = "w1", Items = 6 };
            var plan = StudyPlanner.Make(db, state, ev, StudyPlanner.EveningStart(21 * 60));
            Assert.AreEqual(db.WordsInSet("w1").Count, plan.Pairs.Count);
            Assert.IsEmpty(plan.Deferred);
            StudyPlanner.Finish(plan, state);
            foreach (var w in db.WordsInSet("w1"))
                Assert.IsTrue(state.Knows(w.Id));
        }

        [Test]
        public void LearnedWordsMakeTheChefReadable()
        {
            var db = LoadContent();
            var state = new GameState();
            FrenchPair Lookup(string id) => db.TryGetWord(id, out var w) ? w : null;
            string line = db.GetLine("chef_d5_lunch").Vi;
            Assert.IsTrue(Vocabulary.Render(line, Lookup, state.Knows).Contains("<i>"), "chưa học thì còn chữ Pháp");
            // Học qua các buổi tối Ngày 2 (tô chữ: feu) và Ngày 5 (điền từ: poisson, glace).
            var plan = StudyPlanner.Make(db, state, new EveningData { Study = EveningData.Fill, Lesson = "l_fill", Items = 5 },
                StudyPlanner.EveningStart(21 * 60));
            StudyPlanner.Finish(plan, state);
            state.Learn("feu");
            Assert.AreEqual("Bữa trưa! cá ướp nước đá rồi, giữ lửa cho đều!", Vocabulary.Render(line, Lookup, state.Knows));
        }
    }
}
