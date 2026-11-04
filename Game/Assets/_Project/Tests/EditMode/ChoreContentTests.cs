using System.IO;
using HaiBanTay.Content;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    public class ChoreContentTests
    {
        [Test]
        public void EveryMealFitsOnTheHobs()
        {
            // W3.2: hai lò, mỗi lò ba ô; nồi bữa và nồi cao nấu cùng lúc không quá số ô (MealPots đặt mỗi nồi một ô).
            var db = ContentDatabase.Load(name => File.ReadAllText(Path.Combine(Application.dataPath, "_Project/Resources/Content", name + ".vi.json")));
            foreach (var day in db.Days.Days)
                foreach (var phase in day.Phases)
                    Assert.LessOrEqual(phase.Dirty + (phase.CookTall ? 1 : 0), 6, $"Ngày {day.Number}/{phase.Id}: nhiều nồi hơn số ô bếp.");
        }

        [Test]
        public void EverySackPhaseCooksPotsForTheTub()
        {
            // Quản bếp chỉ đổ thùng khoai vào nồi đang nấu (CookRoutine.FetchTub): lượt nào có việc dốc bao thì phải có nồi bữa đang
            // nấu, không thì thùng đầy nằm đó mãi.
            var db = ContentDatabase.Load(name => File.ReadAllText(Path.Combine(Application.dataPath, "_Project/Resources/Content", name + ".vi.json")));
            foreach (var day in db.Days.Days)
                foreach (var phase in day.Phases)
                    if (phase.Orders.Exists(o => o.Task == "sack"))
                        Assert.GreaterOrEqual(phase.Dirty, 1, $"Ngày {day.Number}/{phase.Id}: dốc bao mà không có nồi nào đang nấu.");
        }

        [Test]
        public void ChalkMarksAreNamedOnlyWhereTheSceneHasThem()
        {
            // QA-0009: vòng phấn chỗ đặt xô tro đã bỏ khỏi cảnh; chữ chỉ được nhắc vòng phấn của việc kéo chảo (PanStation có dấu phấn).
            var db = ContentDatabase.Load(name => File.ReadAllText(Path.Combine(Application.dataPath, "_Project/Resources/Content", name + ".vi.json")));
            var allowed = new[] { "order.pan", "guide.pan_to_stove", "guide.pan_to_wash" };
            foreach (var kv in db.Ui.Strings)
                if (kv.Value.Contains("phấn"))
                    CollectionAssert.Contains(allowed, kv.Key, $"{kv.Key}: \"{kv.Value}\" nhắc vòng phấn ở chỗ cảnh không có");
        }

        [Test]
        public void EveryWashUsesOnlyPotsCookedByEarlierMeals()
        {
            var db = ContentDatabase.Load(name => File.ReadAllText(Path.Combine(Application.dataPath, "_Project/Resources/Content", name + ".vi.json")));
            foreach (var day in db.Days.Days)
            {
                int pending = 0;
                bool tallCooked = false;
                foreach (var phase in day.Phases)
                {
                    // Montage (bữa tua nhanh) nấu và phục vụ trước rồi mới rửa: rửa được cả nồi của chính bữa đó.
                    if (phase.Montage)
                        pending += phase.Dirty;
                    foreach (var order in phase.Orders)
                    {
                        if (order.Task == OrderKinds.Wash)
                        {
                            Assert.Greater(pending, 0, $"Ngày {day.Number}/{phase.Id}: chưa nấu thì chưa có nồi bẩn.");
                            // count 99 = rửa hết; số cụ thể thì không đòi nhiều hơn số nồi bẩn đang có (không kẹt, QA-0001).
                            Assert.IsTrue(order.Count == 99 || order.Count <= pending, $"Ngày {day.Number}/{phase.Id}: rửa đúng nồi từ các bữa trước.");
                            pending -= order.Count == 99 ? pending : order.Count;
                        }
                        if (order.Task == OrderKinds.ScrubTall)
                            Assert.IsTrue(tallCooked, "Nồi cao phải nấu trước khi cọ.");
                    }
                    if (!phase.Montage)
                        pending += phase.Dirty;
                    tallCooked |= phase.CookTall;
                }
                Assert.AreEqual(0, pending, $"Ngày {day.Number}: không bỏ sót nồi cuối ca.");
            }
            // Bếp 700–800 suất (F04) kể bằng đống nồi bày và thẻ quy mô; số nồi Ba thật sự nấu/rửa không quá số ô bếp có nồi.
            Assert.LessOrEqual(db.GetDay(5).Phases.Find(p => p.Id == "lunch").Dirty, 3);
            Assert.IsTrue(db.GetDay(5).Phases.Exists(p => p.Cards.Contains("sc_scale")));
        }
    }
}
