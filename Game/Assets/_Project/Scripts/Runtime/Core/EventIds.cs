namespace HaiBanTay.Core
{
    /// <summary>ID sự kiện dạng s{cảnh}.{beat}.{trạng thái}. JSON tham chiếu đúng các chuỗi này
    /// và Tools/validate_content.py kiểm tra khớp.</summary>
    public static class EventIds
    {
        public const string S0StreetDone = "s0.street.done";
        public const string S0PathChosen = "s0.path.chosen";
        public const string S1JobAccepted = "s1.job.accepted";
        public const string S3ContextSeen = "s3.context.seen";
        public const string S3ClothesBorrowed = "s3.clothes.borrowed";
        public const string S3MaiRecalled = "s3.mai.recalled";

        // Cảnh 01
        public const string S1HandsRaised = "s1.hands.raised";
        public const string S1NotebookIntro = "s1.notebook.intro";
        public const string S1Depart = "s1.depart";

        // Cảnh 02
        public const string S2CoalDone = "s2.coal.done";
        public const string S2SackDone = "s2.sack.done";
        public const string S2StormStart = "s2.storm.start";
        public const string S2SecureDone = "s2.secure.done";
        public const string S2SailorHelp = "s2.sailor.help";
        public const string S2ScrubDone = "s2.scrub.done";
        public const string S2StormCalm = "s2.storm.calm";
        public const string S2WordsDone = "s2.words.done";
        public const string S2WashDone = "s2.wash.done";
        public const string S2ShiftEnd = "s2.shift.end";
        // M11: sáu ngày trên tàu
        public const string S2PortSingapore = "s2.port.singapore";
        public const string S2SoldiersHelp = "s2.soldiers.help";
        public const string S2MaiTalk = "s2.mai.talk";
        public const string S2HaulDone = "s2.haul.done";
        public const string S2StormWave = "s2.storm.wave";
        public const string S2PileHigh = "s2.pile.high";
        public const string S2BookLent = "s2.book.lent";

        // S16: chi tiết sinh hoạt trên tàu.
        public const string S2WaterDone = "s2.water.done";
        public const string S2ProvisionsDone = "s2.provisions.done";
        public const string S2ServeDone = "s2.serve.done";
        public const string S2PeelDone = "s2.peel.done";
        public const string S2AsparagusDone = "s2.asparagus.done";
        public const string S2CoffeeDone = "s2.coffee.done";
        public const string S2QuocNguDone = "s2.quoc_ngu.done";
        public const string S2LetterDone = "s2.letter.done";
        public const string S2SoupDone = "s2.soup.done";
        public const string S2ChieuDone = "s2.chieu.done";
        public const string S2GoodFrench = "s2.good_french";

        // Cảnh 03
        public const string S3Dawn = "s3.dawn";
        public const string S3FinalCleanDone = "s3.final_clean.done";
        public const string S3PayQueueEntered = "s3.pay.queue.entered";
        public const string S3PayCalled = "s3.pay.called";
        public const string S3CrewFarewell = "s3.crew.farewell";
        public const string S3QuayObserved = "s3.quay.observed";
        public const string S3CafeEntered = "s3.cafe.entered";
        public const string S3PayReceived = "s3.pay.received";
        public const string S3QuayStep = "s3.quay.step";
        public const string S3TramPass = "s3.tram.pass";
        public const string S3CafeServed = "s3.cafe.served";
        public const string S3PagesLinked = "s3.pages.linked";
        public const string S3End = "s3.end";

        // Cờ trạng thái (GameFlags)
        public const string FlagS1Lingered = "s1.lingered";
    }
}
