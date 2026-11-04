using HaiBanTay.Content;
using HaiBanTay.Core;

namespace HaiBanTay.Notebook
{
    /// <summary>E5: chỉ cặp đích (linkTarget, không phân biệt thứ tự) phát sự kiện s3.pages.linked.
    /// Cặp khác không phát gì và không báo lỗi.</summary>
    public sealed class PageLinkController
    {
        readonly LinkTarget _target;
        readonly GameEvents _events;

        public bool Linked { get; private set; }

        public PageLinkController(LinkTarget target, GameEvents events)
        {
            _target = target;
            _events = events;
        }

        public static bool IsTargetPair(LinkTarget t, string a, string b)
        {
            if (t == null || string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a == b)
                return false;
            return (a == t.A && b == t.B) || (a == t.B && b == t.A);
        }

        public bool TryLink(string draggedPageId, string droppedOnPageId)
        {
            if (Linked || !IsTargetPair(_target, draggedPageId, droppedOnPageId))
                return false;
            Linked = true;
            _events?.Raise(_target.Event);
            return true;
        }
    }
}
