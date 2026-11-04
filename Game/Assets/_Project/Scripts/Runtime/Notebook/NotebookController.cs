using System;
using System.Collections.Generic;
using HaiBanTay.Content;
using HaiBanTay.Core;

namespace HaiBanTay.Notebook
{
    /// <summary>Mở khóa trang sổ theo sự kiện (unlockEvent), giữ thứ tự mở khóa, ghép câu appendIfFlag.</summary>
    public sealed class NotebookController
    {
        readonly ContentDatabase _content;
        readonly GameFlags _flags;
        readonly List<NotebookPage> _unlocked = new List<NotebookPage>();
        readonly HashSet<string> _ids = new HashSet<string>();

        public event Action<NotebookPage> PageUnlocked;

        public NotebookController(ContentDatabase content, GameEvents events, GameFlags flags)
        {
            _content = content;
            _flags = flags;
            if (events != null)
                events.AnyRaised += OnEvent;
        }

        public IReadOnlyList<NotebookPage> Unlocked => _unlocked;
        public bool IsUnlocked(string pageId) => _ids.Contains(pageId);

        void OnEvent(string id)
        {
            foreach (var page in _content.Notebook.Pages)
                if (page.UnlockEvent == id)
                    Unlock(page.Id);
        }

        public bool Unlock(string pageId)
        {
            if (_ids.Contains(pageId))
                return false;
            var page = _content.GetPage(pageId);
            _ids.Add(pageId);
            _unlocked.Add(page);
            PageUnlocked?.Invoke(page);
            return true;
        }

        /// <summary>Mở khóa không thông báo (khi nhảy cảnh bằng phím demo cần trang của cảnh trước).</summary>
        public void EnsureUnlocked(string pageId)
        {
            if (_ids.Contains(pageId))
                return;
            _ids.Add(pageId);
            _unlocked.Add(_content.GetPage(pageId));
        }

        public string BodyFor(NotebookPage page)
        {
            var extra = page.AppendIfFlag;
            if (extra != null && _flags != null && _flags.Has(extra.Flag))
                return page.Body + " " + extra.Text;
            return page.Body;
        }

        public void Reset()
        {
            _unlocked.Clear();
            _ids.Clear();
        }
    }
}
