using HaiBanTay.Shift;
using NUnit.Framework;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Lỗi kẹt tiến trình (Docs/QA/bugs.md, PRODUCTION_PLAN W0): bảng việc phải khớp với việc thật đã làm.
    /// Rửa sớm hết nồi thì lượt rửa không được đòi một chiếc nồi không còn; việc làm trước lệnh được tính khi lệnh tới.
    /// </summary>
    public class ProgressRegressionTests
    {
        [Test]
        public void OrderForNothingIsDoneAtOnce()
        {
            var order = new Order(OrderKinds.Wash, 0, false);
            Assert.IsTrue(order.IsDone, "QA-0001: lệnh cần 0 đơn vị phải xong ngay, không bị ép thành 1");
        }

        [Test]
        public void WashPhaseAfterWashingEveryPotEarlyIsDone()
        {
            var wash = new WashLogic();
            wash.AddDirty(3);
            for (int i = 0; i < 3; i++)
                Assert.IsTrue(wash.CompleteOne());
            var board = new OrderBoardLogic();
            board.Add(OrderKinds.Wash, ShiftDirector.WashNeed(wash));
            Assert.IsTrue(board.AllDone, "QA-0001: rửa hết nồi trước lượt rửa thì lượt rửa xong ngay, không kẹt");
        }

        [Test]
        public void PotStillInTheBasinCountsForTheWashPhase()
        {
            var wash = new WashLogic();
            wash.AddDirty(2);
            Assert.IsTrue(wash.TakeToBasin());
            Assert.AreEqual(2, ShiftDirector.WashNeed(wash), "QA-0001: nồi đang ở chậu cũng phải rửa xong mới hết lượt");
        }

        [Test]
        public void WorkDoneBeforeTheOrderCountsWhenTheOrderArrives()
        {
            var board = new OrderBoardLogic();
            Assert.AreEqual(0, board.Report(OrderKinds.Sack, 1), "chưa có lệnh thì chưa trừ vào lệnh nào");
            var sacks = board.Add(OrderKinds.Sack, 2);
            Assert.AreEqual(1, sacks.Done, "QA-0002: bao đã vác trước lệnh được tính ngay khi lệnh tới");
            Assert.IsFalse(sacks.IsDone);
            board.Report(OrderKinds.Sack, 1);
            Assert.IsTrue(sacks.IsDone);
        }

        [Test]
        public void FireAndAshAreNotBankedAhead()
        {
            var board = new OrderBoardLogic();
            board.Report(OrderKinds.Fire, 3);
            board.Report(OrderKinds.Ash, 1);
            Assert.IsFalse(board.Add(OrderKinds.Fire, 2).IsDone, "lửa đo theo lò thật, xúc thừa lúc trước không thay cho lò đang yếu");
            Assert.IsFalse(board.Add(OrderKinds.Ash, 1).IsDone, "tro đo theo ghi lò thật");
        }

        [Test]
        public void PeelingSpeedDoesNotDependOnFrameRate()
        {
            const float pixelsPerSecond = 400f;
            float at60 = 0f, at30 = 0f;
            for (int i = 0; i < 60; i++)
                at60 += PeelStation.StrokeProgress(pixelsPerSecond / 60f, 1f / 60f);
            for (int i = 0; i < 30; i++)
                at30 += PeelStation.StrokeProgress(pixelsPerSecond / 30f, 1f / 30f);
            Assert.AreEqual(at60, at30, 1e-4f, "QA-0005: gọt một giây ở 30 fps và 60 fps phải được như nhau");
            Assert.Greater(at60, 0.2f, "rê đều tay một giây phải gọt được một phần củ");
        }

        [Test]
        public void ChefCallsTheChoreThatIsActuallyWaiting()
        {
            string Call(string kind, string fallback = null)
            {
                var b = new OrderBoardLogic();
                b.Add(kind, 1);
                return ShiftDirector.OutsideCall(b, fallback);
            }
            Assert.AreEqual("chef_ash", Call(OrderKinds.Ash), "QA-0006: cần cào tro thì không gọi 'dọn chảo'");
            Assert.AreEqual("chef_wash", Call(OrderKinds.Wash), "QA-0006: cần rửa nồi thì không gọi 'đem nước'");
            Assert.AreEqual("chef_call_water", Call(OrderKinds.Water));
            Assert.AreEqual("chef_call_pan", Call(OrderKinds.Pan));
            Assert.AreEqual("chef_call_coal", Call(OrderKinds.Fire));
            Assert.AreEqual("chef_story_sweep", Call(OrderKinds.Sweep));
            Assert.AreEqual("phase_line", Call(OrderKinds.Chieu, "phase_line"), "việc không có câu riêng thì nhắc câu mở lượt");
        }

        [Test]
        public void BankedWorkSurvivesANewPhaseButNotANewDay()
        {
            var board = new OrderBoardLogic();
            board.Report(OrderKinds.CoalFetch, 1);
            board.Clear();
            Assert.IsTrue(board.Add(OrderKinds.CoalFetch, 1).IsDone, "sang lượt mới vẫn giữ việc đã làm trước");
            board.Report(OrderKinds.Water, 1);
            board.ResetDay();
            Assert.IsFalse(board.Add(OrderKinds.Water, 1).IsDone, "ngày mới không mang việc của hôm trước");
        }
    }
}
