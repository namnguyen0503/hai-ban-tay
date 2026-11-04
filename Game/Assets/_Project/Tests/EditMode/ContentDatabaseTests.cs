using System.IO;
using System.Linq;
using HaiBanTay.Content;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    public class ContentDatabaseTests
    {
        static string ContentDir => Path.Combine(Application.dataPath, "_Project/Resources/Content");

        static ContentDatabase LoadFromDisk()
        {
            return ContentDatabase.Load(name =>
            {
                var path = Path.Combine(ContentDir, name + ".vi.json");
                return File.Exists(path) ? File.ReadAllText(path) : null;
            });
        }

        [Test]
        public void LoadsAllContentFiles()
        {
            Assert.AreEqual(9, ContentDatabase.FileNames.Length);
            var db = LoadFromDisk();
            Assert.IsNotNull(db.Notebook);
            Assert.IsNotNull(db.SourceCards);
            Assert.IsNotNull(db.Subtitles);
            Assert.IsNotNull(db.Ui);
            Assert.IsNotNull(db.FrenchWords);
            Assert.IsNotNull(db.MapTransitions);
            Assert.IsNotNull(db.Ending);
            Assert.IsNotNull(db.Days);
            Assert.IsNotNull(db.Study);
        }

        [Test]
        public void IdsAreUniqueAndReferencesResolve()
        {
            var db = LoadFromDisk(); // ném ContentException nếu ID trùng hoặc tham chiếu hỏng
            Assert.AreEqual(db.Notebook.Pages.Count, db.Notebook.Pages.Select(p => p.Id).Distinct().Count());
            Assert.IsEmpty(db.FindBrokenReferences());
            foreach (var page in db.Notebook.Pages)
                foreach (var id in page.SourceCardIds)
                    Assert.IsNotNull(db.GetCard(id));
        }

        [Test]
        public void LinkTargetPointsToExistingLinkablePages()
        {
            var db = LoadFromDisk();
            var t = db.Notebook.LinkTarget;
            Assert.IsTrue(db.GetPage(t.A).Linkable);
            Assert.IsTrue(db.GetPage(t.B).Linkable);
        }

        [Test]
        public void EverySubtitleHasKnownSpeakerAndText()
        {
            var db = LoadFromDisk();
            foreach (var line in db.Subtitles.Lines)
            {
                Assert.IsFalse(string.IsNullOrEmpty(line.Vi), line.Id);
                Assert.IsTrue(db.Subtitles.Speakers.ContainsKey(line.Speaker), $"{line.Id}: speaker '{line.Speaker}' chưa khai báo");
            }
            // Mọi nhân vật được có thoại, kể cả anh Ba.
            Assert.AreEqual("Anh Ba", db.Subtitles.Speakers["ba"]);
        }

        [Test]
        public void SourceCardsCiteFactsAndHaveNoStagedLayer()
        {
            var db = LoadFromDisk();
            CollectionAssert.DoesNotContain(db.SourceCards.Layers.Keys, "dan_dung", "game không còn thẻ dàn dựng");
            foreach (var card in db.SourceCards.Cards)
            {
                Assert.AreNotEqual("dan_dung", card.Layer, card.Id);
                Assert.IsNotEmpty(card.FactIds, $"{card.Id}: thẻ nguồn phải ghi fact trong FACTS.md");
            }
        }

        [Test]
        public void DisclaimerTextExists()
        {
            var db = LoadFromDisk();
            foreach (var key in new[] { "disclaimer.body", "disclaimer.continue", "credits.disclaimer" })
                Assert.IsFalse(string.IsNullOrWhiteSpace(db.GetString(key)), key);
            StringAssert.Contains("hư cấu", db.GetString("disclaimer.body"));
            StringAssert.Contains("hư cấu", db.GetString("credits.disclaimer"));
        }

        [Test]
        public void MissingFileThrowsWithFileName()
        {
            var ex = Assert.Throws<ContentException>(() => ContentDatabase.Load(name => null));
            StringAssert.Contains(".vi.json", ex.Message);
        }

        [Test]
        public void MissingUiStringThrows()
        {
            var db = LoadFromDisk();
            Assert.Throws<ContentException>(() => db.GetString("khong.ton.tai"));
        }

        [Test]
        public void SceneNamesAndFpsFormatExist()
        {
            var db = LoadFromDisk();
            foreach (var n in new[] { "scene.01.name", "scene.02.name", "scene.03.name", "debug.fps" })
                Assert.IsFalse(string.IsNullOrEmpty(db.GetString(n)), n);
        }

        [Test]
        public void EndingDurationWithinPlanRange()
        {
            var db = LoadFromDisk();
            float total = db.Ending.EstimatedSeconds();
            Assert.That(total, Is.InRange(480f, 720f), $"tổng thời lượng ending {total} s");
        }

        [Test]
        public void EverySubtitleHasPositiveDuration()
        {
            var db = LoadFromDisk();
            foreach (var line in db.Subtitles.Lines)
                Assert.Greater(line.Duration, 0f, line.Id);
        }
    }
}
