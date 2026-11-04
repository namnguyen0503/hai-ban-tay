using System.Collections.Generic;

namespace HaiBanTay.Core
{
    /// <summary>Cờ trạng thái xuyên cảnh (ví dụ s1.lingered).</summary>
    public sealed class GameFlags
    {
        readonly HashSet<string> _flags = new HashSet<string>();

        public bool Has(string flag) => _flags.Contains(flag);
        public void Set(string flag) => _flags.Add(flag);
        public void Clear(string flag) => _flags.Remove(flag);
        public void Reset() => _flags.Clear();
        public IReadOnlyCollection<string> All => _flags;
    }
}
