using System;
using System.Collections.Generic;

namespace HaiBanTay.Core
{
    /// <summary>Bus sự kiện theo chuỗi ID (xem EventIds). Nhớ các sự kiện đã phát để bên đăng ký muộn tra lại.</summary>
    public sealed class GameEvents
    {
        readonly Dictionary<string, List<Action>> _handlers = new Dictionary<string, List<Action>>();
        readonly HashSet<string> _fired = new HashSet<string>();

        public event Action<string> AnyRaised;

        public bool HasFired(string id) => _fired.Contains(id);

        public void Subscribe(string id, Action handler)
        {
            if (!_handlers.TryGetValue(id, out var list))
            {
                list = new List<Action>();
                _handlers[id] = list;
            }
            list.Add(handler);
        }

        public void Unsubscribe(string id, Action handler)
        {
            if (_handlers.TryGetValue(id, out var list))
                list.Remove(handler);
        }

        public void Raise(string id)
        {
            _fired.Add(id);
            if (_handlers.TryGetValue(id, out var list))
            {
                var snapshot = list.ToArray();
                for (int i = 0; i < snapshot.Length; i++)
                    snapshot[i]();
            }
            AnyRaised?.Invoke(id);
        }

        /// <summary>Các sự kiện đã phát (SaveSystem ghi lại).</summary>
        public IReadOnlyCollection<string> Fired => _fired;

        /// <summary>Đánh dấu đã phát mà không gọi bên đăng ký (nạp bản lưu).</summary>
        public void MarkFired(string id)
        {
            _fired.Add(id);
        }

        public void Reset()
        {
            _fired.Clear();
        }
    }
}
