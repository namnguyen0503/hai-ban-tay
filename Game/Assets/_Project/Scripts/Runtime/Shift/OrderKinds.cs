using System.Collections.Generic;

namespace HaiBanTay.Shift
{
    /// <summary>Loại việc trong lệnh của quản bếp (days.vi.json → orders[].task): việc có trong tư liệu (F03, F04, F05,
    /// F05b kéo rổ rau bằng dây sắt ngày bão) và chằng nồi khi bão.</summary>
    public static class OrderKinds
    {
        public const string Fire = "fire";
        public const string Sack = "sack";
        public const string Wash = "wash";
        public const string Secure = "secure";
        public const string ScrubTall = "scrub_tall";
        /// <summary>Kéo lê nồi cao từ góc phòng rửa ra trước lò rồi nhấc lên ô bếp (brush17).</summary>
        public const string TallOut = "tall_out";
        public const string Ash = "ash";
        public const string Sweep = "sweep";
        public const string Greens = "greens";
        public const string Pan = "pan";
        /// <summary>F05b: biển động, buộc rổ rau vào dây sắt kéo từ hầm lên bếp.</summary>
        public const string Haul = "haul";

                public const string CoalFetch = "coal_fetch";
        public const string Water = "water";
        public const string Meat = "meat";
        public const string Fish = "fish";
        public const string Ice = "ice";
        public const string Serve = "serve";
        public const string Coffee = "coffee";
        public const string Peel = "peel";
        public const string Asparagus = "asparagus";
        public const string Soup = "soup";
        public const string Chieu = "chieu";

        static readonly HashSet<string> Known = new HashSet<string> { Fire, Sack, Wash, Secure, ScrubTall, TallOut, Ash, Sweep, Greens, Pan, Haul, CoalFetch, Water, Meat, Fish, Ice, Serve, Coffee, Peel, Asparagus, Soup, Chieu };

        public static bool IsKnown(string kind) => kind != null && Known.Contains(kind);
        public static IEnumerable<string> All => Known;

        /// <summary>Khóa ui_strings của nhãn việc (có thể chứa {w:id}).</summary>
        public static string LabelKey(string kind) => "order." + kind;
    }
}
