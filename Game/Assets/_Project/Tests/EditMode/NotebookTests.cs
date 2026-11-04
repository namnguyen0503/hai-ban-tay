using System.IO;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Notebook;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    public class NotebookTests
    {
        static ContentDatabase Load()
        {
            var dir = Path.Combine(Application.dataPath, "_Project/Resources/Content");
            return ContentDatabase.Load(n =>
            {
                var p = Path.Combine(dir, n + ".vi.json");
                return File.Exists(p) ? File.ReadAllText(p) : null;
            });
        }

        [Test]
        public void EventUnlocksMatchingPagesOnce()
        {
            var db = Load();
            var events = new GameEvents();
            var nb = new NotebookController(db, events, new GameFlags());
            int count = 0;
            nb.PageUnlocked += _ => count++;
            events.Raise(EventIds.S1Depart);
            Assert.IsTrue(nb.IsUnlocked("p_departure"));
            Assert.IsTrue(nb.IsUnlocked("p_ship"));
            Assert.AreEqual(2, count);
            events.Raise(EventIds.S1Depart);
            Assert.AreEqual(2, count);
        }

        [Test]
        public void AppendIfFlagAddsSentence()
        {
            var db = Load();
            var flags = new GameFlags();
            var nb = new NotebookController(db, new GameEvents(), flags);
            var page = db.GetPage("p_departure");
            Assert.AreEqual(page.Body, nb.BodyFor(page));
            flags.Set(EventIds.FlagS1Lingered);
            StringAssert.StartsWith(page.Body, nb.BodyFor(page));
            StringAssert.EndsWith(page.AppendIfFlag.Text, nb.BodyFor(page));
        }

        [Test]
        public void OnlyTargetPairRaisesLinkedEvent()
        {
            var db = Load();
            var events = new GameEvents();
            var link = new PageLinkController(db.Notebook.LinkTarget, events);
            Assert.IsFalse(link.TryLink("p_pay", "p_monsieur"));
            Assert.IsFalse(link.TryLink("p_french_sailor", "p_why_france"));
            Assert.IsFalse(link.TryLink("p_pay", "p_pay"));
            Assert.IsFalse(events.HasFired(EventIds.S3PagesLinked));
            Assert.IsTrue(link.TryLink("p_why_france", "p_pay"));
            Assert.IsTrue(events.HasFired(EventIds.S3PagesLinked));
        }

        [Test]
        public void LinkWorksInEitherOrder()
        {
            var t = new LinkTarget { A = "p_pay", B = "p_why_france", Event = EventIds.S3PagesLinked };
            Assert.IsTrue(PageLinkController.IsTargetPair(t, "p_pay", "p_why_france"));
            Assert.IsTrue(PageLinkController.IsTargetPair(t, "p_why_france", "p_pay"));
            Assert.IsFalse(PageLinkController.IsTargetPair(t, "p_pay", "p_tram"));
        }
    }
}
