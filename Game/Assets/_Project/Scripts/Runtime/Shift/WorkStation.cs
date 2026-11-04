using System;
using HaiBanTay.Tasks;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Chỗ làm một loại việc lặp lại suốt ca (thùng than + lò, kho lạnh + bàn sơ chế, chậu rửa).
    /// Mở khi ca bắt đầu (Activate), đóng khi hết ca (Close). Mỗi đơn vị việc làm xong phát Worked; ShiftDirector
    /// trừ vào lệnh cùng loại trên bảng việc. Không gợi ý "đứng yên" (việc nào làm lúc nào do người chơi chọn).
    /// </summary>
    public abstract class WorkStation : TaskBase
    {
        public event Action<WorkStation, int> Worked;

        /// <summary>Loại việc (OrderKinds).</summary>
        public abstract string Kind { get; }

        protected virtual void Awake()
        {
            idleHintSeconds = 0f;
        }

        protected void Work(int units = 1)
        {
            ReportProgress();
            Worked?.Invoke(this, units);
        }

        /// <summary>Hết ca.</summary>
        public void Close()
        {
            if (State == TaskState.Active)
                Complete();
        }

        /// <summary>Lượt mới có lệnh loại này (count đơn vị): rải vệt bẩn, mang rau ra sọt, chỉ chỗ kéo chảo…</summary>
        public virtual void OnOrder(int count) { }

        /// <summary>Sim / phím demo: làm xong một đơn vị việc ngay.</summary>
        public abstract void SimulateWork();
    }
}
