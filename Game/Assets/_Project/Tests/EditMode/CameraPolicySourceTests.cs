using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Luật camera 17/10 ở mức mã nguồn: game không tự dời hay xoay camera vì tư thế làm việc. Chặn từ gốc các lối cũ đã gây QA-0062
    /// (camera lùi sau thân không đầu): đẩy mắt (EyeForward), hạ đầu để "cúi" (ExtraHeadHeight âm ngoài chỗ ngồi thật), dịch chuyển tức
    /// thời người chơi và tự xoay camera trong trạm làm việc. Bước lại gần ở tốc độ đi bộ dùng PlayerRig.StepToward.
    /// </summary>
    public class CameraPolicySourceTests
    {
        static string Runtime => Path.Combine(Application.dataPath, "_Project/Scripts/Runtime");

        static string[] Files(string folder) => Directory.GetFiles(Path.Combine(Runtime, folder), "*.cs", SearchOption.AllDirectories);

        static string Code(string path) =>
            // Bỏ chú thích để câu giải thích lịch sử không tính là lệnh.
            Regex.Replace(Regex.Replace(File.ReadAllText(path), @"/\*.*?\*/", "", RegexOptions.Singleline), @"//[^\n]*", "");

        [Test]
        public void NothingPushesTheEyeAwayFromTheHead()
        {
            var hits = Directory.GetFiles(Runtime, "*.cs", SearchOption.AllDirectories).Where(f => Code(f).Contains("EyeForward")).ToArray();
            CollectionAssert.IsEmpty(hits.Select(Path.GetFileName), "không còn đường đẩy mắt ra trước thân");
        }

        [Test]
        public void HeadHeightOnlyChangesForRealSeatsAndTheStool()
        {
            // Ngồi ghế học, ngồi ghế quán (cảnh kết), đứng lên ghế đẩu cọ nồi cao: chỗ đặt người cao/thấp thật trong thế giới.
            var allowed = new[] { "SitController.cs", "EndingDirector.cs", "ScrubPotTask.cs", "TallPotStation.cs", "FirstPersonMotor.cs" };
            var hits = Directory.GetFiles(Runtime, "*.cs", SearchOption.AllDirectories)
                .Where(f => !allowed.Contains(Path.GetFileName(f)) && Regex.IsMatch(Code(f), @"ExtraHeadHeight\s*=[^=]"))
                .Select(Path.GetFileName).ToArray();
            CollectionAssert.IsEmpty(hits, "trạm làm việc không được hạ/nâng đầu người chơi");
        }

        [Test]
        public void WorkStationsNeverTeleportOrTurnThePlayer()
        {
            // Cảnh truyện có màn đen hay sóng đánh (đổi pha tối, sóng bão, thăng bằng cầu thang lúc bão) được đặt người/góc nhìn.
            var story = new[] { "HaulStation.cs", "ShiftDirector.cs", "StairsBalance.cs" };
            var hits = Files("Shift").Concat(Files("Tasks"))
                .Where(f => !story.Contains(Path.GetFileName(f)))
                .SelectMany(f =>
                {
                    string code = Code(f);
                    return new[] { @"\.Teleport\(", @"look\.SetAngles\(", @"\bMoveTo\([^)]*,\s*\d+f\s*,\s*[\d.]+f\)" }
                        .Where(p => Regex.IsMatch(code, p)).Select(p => $"{Path.GetFileName(f)}: {p}");
                }).ToArray();
            CollectionAssert.IsEmpty(hits, "trạm làm việc dùng PlayerRig.StepToward (đi bộ, giữ hướng nhìn), không dịch chuyển tức thời hay tự xoay camera");
        }

        [Test]
        public void TheBodyDoesNotBendForwardWhileWorking()
        {
            string code = Code(Path.Combine(Runtime, "Player/PlayerBody.cs"));
            Assert.IsFalse(Regex.IsMatch(code, @"_spine\.rotation\s*="), "làm ở bàn không gập thân (gập thì cổ ra trước mắt)");
            StringAssert.Contains("KeepBodyBehindEye", code, "thân lùi dưới camera thay cho đẩy camera");
        }
    }
}
