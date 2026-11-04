using System;
using System.Collections.Generic;

namespace HaiBanTay.Shift
{
    /// <summary>Một lệnh trên bảng việc: loại việc, số lượng cần, đã làm.</summary>
    public sealed class Order
    {
        public readonly string Kind;
        public readonly int Need;
        /// <summary>Lệnh phát sinh giữa lượt (lò sắp tắt), không có trong days.vi.json.</summary>
        public readonly bool Extra;

        public Order(string kind, int need, bool extra)
        {
            Kind = kind;
            Need = Math.Max(0, need);
            Extra = extra;
        }

        public int Done { get; internal set; }
        public bool IsDone => Done >= Need;
        public int Remaining => Math.Max(0, Need - Done);
    }

    /// <summary>Bảng việc của một lượt. Người chơi làm theo thứ tự tùy ý; việc làm được (xúc than, vác bao, rửa nồi)
    /// trừ dần vào lệnh cùng loại còn mở. Lượt xong khi mọi lệnh xong. Việc làm trước lệnh được giữ lại trong ngày và
    /// trừ ngay khi lệnh cùng loại tới (trừ lửa, tro, rửa nồi: ba loại này đo theo lò và chồng nồi thật).</summary>
    public sealed class OrderBoardLogic
    {
        readonly List<Order> _orders = new List<Order>();
        readonly Dictionary<string, int> _banked = new Dictionary<string, int>();

        /// <summary>Loại việc được giữ lại khi làm trước lệnh.</summary>
        public static bool Banks(string kind) => kind != OrderKinds.Fire && kind != OrderKinds.Ash && kind != OrderKinds.Wash;

        /// <summary>Số đơn vị việc đã làm trước, chờ lệnh cùng loại.</summary>
        public int Banked(string kind) => _banked.TryGetValue(kind, out int n) ? n : 0;

        public IReadOnlyList<Order> Orders => _orders;
        public event Action Changed;

        public bool AllDone
        {
            get
            {
                foreach (var o in _orders)
                    if (!o.IsDone)
                        return false;
                return true;
            }
        }

        public Order Add(string kind, int need, bool extra = false)
        {
            var o = new Order(kind, need, extra);
            _orders.Add(o);
            int ahead = Banked(kind);
            if (ahead > 0 && o.Remaining > 0)
            {
                int take = Math.Min(ahead, o.Remaining);
                o.Done += take;
                _banked[kind] = ahead - take;
            }
            Changed?.Invoke();
            return o;
        }

        public bool HasOpen(string kind)
        {
            foreach (var o in _orders)
                if (o.Kind == kind && !o.IsDone)
                    return true;
            return false;
        }

        public int RemainingOf(string kind)
        {
            int n = 0;
            foreach (var o in _orders)
                if (o.Kind == kind)
                    n += o.Remaining;
            return n;
        }

        /// <summary>Trừ units vào các lệnh cùng loại còn mở (theo thứ tự thêm). Trả về số đơn vị đã dùng; phần làm thêm
        /// khi chưa có lệnh được giữ lại cho lệnh sau trong ngày (Banks), riêng lửa, tro, rửa nồi thì không.</summary>
        public int Report(string kind, int units)
        {
            int used = 0;
            foreach (var o in _orders)
            {
                if (units <= 0)
                    break;
                if (o.Kind != kind || o.IsDone)
                    continue;
                int take = Math.Min(units, o.Remaining);
                o.Done += take;
                units -= take;
                used += take;
            }
            if (units > 0 && Banks(kind))
                _banked[kind] = Banked(kind) + units;
            if (used > 0)
                Changed?.Invoke();
            return used;
        }

        public void ForceCompleteAll()
        {
            foreach (var o in _orders)
                o.Done = o.Need;
            Changed?.Invoke();
        }

        /// <summary>Lửa/tro là điều kiện trên vật thật; bảng chỉ chiếu số còn thiếu, kể cả việc làm trước lệnh.</summary>
        public void SetStateRemaining(string kind, int remaining)
        {
            int need = 0;
            foreach (var o in _orders) if (o.Kind == kind) need += o.Need;
            int done = Math.Max(0, need - Math.Max(0, remaining));
            bool changed = false;
            foreach (var o in _orders)
            {
                if (o.Kind != kind) continue;
                int next = Math.Min(o.Need, done);
                done -= next;
                changed |= o.Done != next;
                o.Done = next;
            }
            if (changed) Changed?.Invoke();
        }

        public void Clear()
        {
            _orders.Clear();
            Changed?.Invoke();
        }

        /// <summary>Đầu ngày: không còn lệnh, không còn việc làm trước.</summary>
        public void ResetDay()
        {
            _banked.Clear();
            Clear();
        }
    }
}
