namespace HaiBanTay.Shift
{
    /// <summary>Đếm chính lần khuân sọt than của CoalStation, sau quét và đốt lò, trước khi xuống hầm thực phẩm. Khuân
    /// trước lệnh cũng được tính (bảng việc giữ lại tới khi lệnh tới).</summary>
    public sealed class CoalFetchStation : WorkStation
    {
        public CoalStation coal;
        int _remaining, _before;
        public override string Kind => OrderKinds.CoalFetch;
        protected override void OnActivated() { _before = coal.Refills; }
        public override void OnOrder(int count) { _remaining += count; }
        protected override void Update()
        {
            base.Update();
            if (State == HaiBanTay.Tasks.TaskState.Active && coal.Refills > _before) { _before = coal.Refills; _remaining = System.Math.Max(0, _remaining - 1); Work(); }
        }
        public override bool Engaged(HaiBanTay.Interaction.Interactor who) => coal.Engaged(who);
        public override bool Guide(HaiBanTay.Interaction.Interactor who, out UnityEngine.Transform target, out string key) =>
            coal.GuideFetch(who, out target, out key);
        public override void SimulateWork() { if (_remaining > 0) { coal.SetStock(-1); _remaining--; Work(); } }
    }
}
