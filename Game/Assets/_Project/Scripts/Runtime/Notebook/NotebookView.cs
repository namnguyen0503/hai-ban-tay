using System;
using System.Collections.Generic;
using HaiBanTay.Content;
using HaiBanTay.Player;
using HaiBanTay.Tasks;
using HaiBanTay.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace HaiBanTay.Notebook
{
    public enum NotebookMode
    {
        Browse,
        Match,
        Link,
        /// <summary>Học buổi tối: tô chữ theo nét mờ.</summary>
        Trace,
        /// <summary>Học buổi tối: chọn từ điền chỗ trống trong câu.</summary>
        Fill,
        /// <summary>Học buổi tối: bấm các từ theo thứ tự để viết câu.</summary>
        Sentence
    }

    /// <summary>Sổ quan sát: xem trang (Browse), ghép từ Pháp–Việt (Match, B2.6), kéo trang nối trang (Link, E5), và ba
    /// kiểu học buổi tối (M11, F07a): tô chữ (Trace), điền từ (Fill), viết câu (Sentence). Mở sổ thì chặn điều khiển và
    /// hiện chuột. Logic học nằm ở Tasks (TraceLogic, FillLogic, SentenceLogic); sổ chỉ vẽ và chuyển thao tác chuột.</summary>
    public sealed class NotebookView
    {
        const string BlockReason = "notebook";

        readonly VisualElement _root;
        readonly VisualElement _list;
        readonly VisualElement _page;
        readonly Label _footer;
        NotebookController _controller;
        ContentDatabase _content;
        GameInput _input;
        string _selectedId;
        bool _showSources;
        float _nextPencil;

        void PageSound() => Core.GameRoot.Instance?.Audio.PlaySfx("SFX/page_turn", 0.5f);

        void PencilSound()
        {
            if (Time.unscaledTime < _nextPencil)
                return;
            _nextPencil = Time.unscaledTime + 0.35f;
            Core.GameRoot.Instance?.Audio.PlaySfx("SFX/pencil_write", 0.4f);
        }

        // Match
        MatchWordsLogic _match;
        int _selectedFr = -1;
        Action _onMatchDone;
        string _matchHeading;
        string _studyHeading;

        // Học buổi tối
        TraceLogic _trace;
        FillLogic _fill;
        SentenceLogic _sentence;
        Func<string> _studyCaption;
        Action _onStudyDone;
        VisualElement _canvas;
        Label _traceWord;
        Label _traceCount;
        string _lastDone;
        int _wrongToken = -1;

        // Link
        PageLinkController _link;
        Action _onLinked;
        readonly List<VisualElement> _linkCards = new List<VisualElement>();
        VisualElement _dragging;
        Vector2 _dragStart;

        public bool IsOpen { get; private set; }
        public NotebookMode Mode { get; private set; }
        /// <summary>Tab mở/đóng được không (tắt trong đoạn phim và khi đang làm bài trong sổ).</summary>
        public bool AllowToggle { get; set; } = true;
        public int OpenCount { get; private set; }
        public MatchWordsLogic Match => _match;
        public PageLinkController Link => _link;
        public TraceLogic Trace => _trace;
        public FillLogic Fill => _fill;
        public SentenceLogic Sentence => _sentence;
        /// <summary>Đang mở một bài học buổi tối (Match, Trace, Fill, Sentence).</summary>
        public bool InStudy => IsOpen && (Mode == NotebookMode.Match || Mode == NotebookMode.Trace || Mode == NotebookMode.Fill
            || Mode == NotebookMode.Sentence);

        public event Action Opened;
        public event Action Closed;

        public NotebookView(VisualElement parent, UnityEngine.TextCore.Text.FontAsset serif)
        {
            _root = new VisualElement();
            _root.AddToClassList("hbt-notebook");
            if (serif != null)
                _root.style.unityFontDefinition = FontDefinition.FromSDFFont(serif);
            parent.Add(_root);
            _list = Ui.Box("hbt-notebook-list", _root);
            _list.pickingMode = PickingMode.Position;
            _page = Ui.Box("hbt-notebook-page", _root);
            _page.pickingMode = PickingMode.Position;
            var footer = Ui.Box("hbt-notebook-footer", _root);
            _footer = Ui.Text("hbt-hint-small", footer);
            Ui.Show(_root, false);
        }

        public void Bind(NotebookController controller, ContentDatabase content, GameInput input)
        {
            _controller = controller;
            _content = content;
            _input = input;
        }

        string S(string key) => _content.GetString(key);

        // ---------- Mở / đóng ----------

        public void Toggle()
        {
            if (!AllowToggle || Mode != NotebookMode.Browse)
                return;
            if (IsOpen)
                Close();
            else
                Open(NotebookMode.Browse);
        }

        void Open(NotebookMode mode)
        {
            PageSound();
            Mode = mode;
            IsOpen = true;
            OpenCount++;
            Ui.Show(_root, true);
            _input?.Block(BlockReason);
            _input?.SetCursorFree(true);
            Render();
            Opened?.Invoke();
        }

        public void Close()
        {
            if (!IsOpen)
                return;
            IsOpen = false;
            Mode = NotebookMode.Browse;
            _dragging = null;
            Ui.Show(_root, false);
            _input?.Unblock(BlockReason);
            _input?.SetCursorFree(false);
            Closed?.Invoke();
        }

        public void OpenAt(string pageId)
        {
            _selectedId = pageId;
            _showSources = false;
            Open(NotebookMode.Browse);
        }

        public void OpenMatch(MatchWordsLogic logic, Action onDone, string heading = null)
        {
            _match = logic;
            _selectedFr = -1;
            _onMatchDone = onDone;
            _matchHeading = heading;
            Open(NotebookMode.Match);
        }

        /// <summary>Tô chữ: caption trả về dòng chữ của mục đang tô (từ và nghĩa).</summary>
        public void OpenTrace(TraceLogic logic, Func<string> caption, Action onDone)
        {
            _trace = logic;
            _studyCaption = caption;
            _onStudyDone = onDone;
            Open(NotebookMode.Trace);
        }

        public void OpenFill(FillLogic logic, Action onDone, string heading = null)
        {
            _fill = logic;
            _studyHeading = heading;
            _lastDone = null;
            _onStudyDone = onDone;
            Open(NotebookMode.Fill);
        }

        public void OpenSentence(SentenceLogic logic, Action onDone, string heading = null)
        {
            _sentence = logic;
            _studyHeading = heading;
            _lastDone = null;
            _wrongToken = -1;
            _onStudyDone = onDone;
            Open(NotebookMode.Sentence);
        }

        public void OpenLink(PageLinkController link, Action onLinked)
        {
            _link = link;
            _onLinked = onLinked;
            Open(NotebookMode.Link);
        }

        void Render()
        {
            _list.Clear();
            _page.Clear();
            Ui.Show(_list, Mode == NotebookMode.Browse);
            switch (Mode)
            {
                case NotebookMode.Browse:
                    RenderBrowse();
                    break;
                case NotebookMode.Match:
                    RenderMatch();
                    break;
                case NotebookMode.Link:
                    RenderLink();
                    break;
                case NotebookMode.Trace:
                    RenderTrace();
                    break;
                case NotebookMode.Fill:
                    RenderFill();
                    break;
                case NotebookMode.Sentence:
                    RenderSentence();
                    break;
            }
        }

        // ---------- Browse ----------

        void RenderBrowse()
        {
            _footer.text = InputGlyphs.Apply(S("notebook.close"));
            Ui.Text("hbt-notebook-heading", _list, S("notebook.title"));
            var pages = _controller.Unlocked;
            if (pages.Count == 0)
            {
                Ui.Text("hbt-page-body", _page, S("notebook.empty"));
                return;
            }
            if (_selectedId == null || !_controller.IsUnlocked(_selectedId))
                _selectedId = pages[pages.Count - 1].Id;

            foreach (var p in pages)
            {
                var id = p.Id;
                var b = Ui.Button(p.Title, () => Select(id), _list, "hbt-notebook-item");
                if (id == _selectedId)
                    b.AddToClassList("hbt-notebook-item--selected");
            }

            var page = _content.GetPage(_selectedId);
            if (_showSources)
            {
                Ui.Text("hbt-page-title", _page, S("notebook.sources"));
                var scroll = new ScrollView();
                scroll.style.flexGrow = 1;
                _page.Add(scroll);
                foreach (var cardId in page.SourceCardIds)
                    scroll.Add(SourceCardView.Build(_content.GetCard(cardId), _content, inline: true));
                Ui.Button(S("notebook.back"), () => { PageSound(); _showSources = false; Render(); }, _page);
            }
            else
            {
                Ui.Text("hbt-page-title", _page, page.Title);
                Ui.Text("hbt-page-body", _page, _controller.BodyFor(page));
                if (page.SourceCardIds.Count > 0)
                {
                    var b = Ui.Button(S("notebook.sources"), () => { PageSound(); _showSources = true; Render(); }, _page);
                    b.style.alignSelf = Align.FlexStart;
                    b.style.marginTop = 24;
                    b.style.marginLeft = 0;
                }
            }
        }

        void Select(string id)
        {
            if (_selectedId != id || _showSources)
                PageSound();
            _selectedId = id;
            _showSources = false;
            Render();
        }

        // ---------- Match ----------

        void RenderMatch()
        {
            _footer.text = string.Empty;
            Ui.Text("hbt-notebook-heading", _page, string.IsNullOrEmpty(_matchHeading) ? S("notebook.match_title") : _matchHeading);
            var row = Ui.Box("hbt-match", _page);
            row.pickingMode = PickingMode.Position;
            var frCol = Ui.Box("hbt-match-column", row);
            var viCol = Ui.Box("hbt-match-column", row);
            frCol.pickingMode = viCol.pickingMode = PickingMode.Position;

            for (int i = 0; i < _match.Count; i++)
            {
                int idx = i;
                var b = Ui.Button(_match.Pair(i).Fr, () => SelectFr(idx), frCol, "hbt-word");
                if (_match.IsMatched(i))
                    b.AddToClassList("hbt-word--done");
                if (i == _selectedFr)
                    b.AddToClassList("hbt-word--selected");
            }
            foreach (int vi in _match.ViOrder)
            {
                int idx = vi;
                var b = Ui.Button(_match.Pair(vi).Vi, () => SelectVi(idx), viCol, "hbt-word");
                if (_match.IsMatched(vi))
                    b.AddToClassList("hbt-word--done");
            }
        }

        void SelectFr(int index)
        {
            if (_match.IsMatched(index))
                return;
            _selectedFr = index;
            Render();
        }

        void SelectVi(int index)
        {
            if (_selectedFr < 0)
                return;
            _match.TryMatch(_selectedFr, index);
            _selectedFr = -1;
            Render();
            if (_match.Done)
                FinishMatch();
        }

        /// <summary>Gọi sau mỗi lần ghép (kể cả khi director giả lập).</summary>
        public void FinishMatch()
        {
            if (Mode != NotebookMode.Match || !_match.Done)
                return;
            var done = _onMatchDone;
            _onMatchDone = null;
            Mode = NotebookMode.Browse;
            Close();
            done?.Invoke();
        }

        // ---------- Tô chữ ----------

        void RenderTrace()
        {
            _footer.text = string.Empty;
            Ui.Text("hbt-notebook-heading", _page, S("study.trace_title"));
            // Ô tô rộng theo cả từ (QA-0140): từ ở trên, ô tô bên dưới.
            var row = Ui.Box("hbt-trace-column", _page);
            row.pickingMode = PickingMode.Position;
            var side = Ui.Box("hbt-trace-side", row);
            side.pickingMode = PickingMode.Position;
            _canvas = Ui.Box("hbt-trace-canvas", row);
            _canvas.pickingMode = PickingMode.Position;
            _canvas.generateVisualContent += DrawTrace;
            _canvas.RegisterCallback<PointerDownEvent>(OnTraceDown);
            _canvas.RegisterCallback<PointerMoveEvent>(OnTraceMove);
            _canvas.RegisterCallback<PointerUpEvent>(OnTraceUp);
            _traceWord = Ui.Text("hbt-trace-word", side);
            _traceWord.enableRichText = true;
            _traceCount = Ui.Text("hbt-study-sub", side);
            var retry = Ui.Button(S("study.trace_retry"), () =>
            {
                _trace?.ClearInk();
                _canvas?.MarkDirtyRepaint();
            }, side);
            retry.style.alignSelf = Align.FlexStart;
            retry.style.marginLeft = 0;
            RefreshTraceText();
        }

        /// <summary>Cạnh ô tô theo chiều cao: từ dài thì ô thấp lại để vừa bề ngang sổ.</summary>
        const float TraceCellMax = 340f, TraceWidthMax = 1180f;

        void SizeTraceCanvas()
        {
            if (_canvas == null || _trace == null)
                return;
            float cell = Mathf.Min(TraceCellMax, TraceWidthMax / Mathf.Max(0.1f, _trace.Aspect));
            _canvas.style.width = cell * _trace.Aspect;
            _canvas.style.height = cell;
        }

        void RefreshTraceText()
        {
            if (_trace == null || _traceWord == null)
                return;
            SizeTraceCanvas();
            _traceWord.text = _studyCaption != null ? _studyCaption() : _trace.Current;
            _traceCount.text = string.Format(S("study.trace_progress"), Mathf.Min(_trace.LettersDone + 1, _trace.Count), _trace.Count);
        }

        Vector2 TracePoint(Vector3 local)
        {
            var r = _canvas.contentRect;
            float w = Mathf.Max(1f, r.width), h = Mathf.Max(1f, r.height);
            float aspect = _trace != null ? _trace.Aspect : 1f;
            return new Vector2(Mathf.Clamp01(local.x / w) * aspect, Mathf.Clamp01(local.y / h));
        }

        void OnTraceDown(PointerDownEvent e)
        {
            if (_trace == null || _trace.Done || e.button != 0)
                return;
            _canvas.CapturePointer(e.pointerId);
            _trace.BeginStroke(TracePoint(e.localPosition));
            PencilSound();
            _canvas.MarkDirtyRepaint();
        }

        void OnTraceMove(PointerMoveEvent e)
        {
            if (_trace == null || !_canvas.HasPointerCapture(e.pointerId))
                return;
            if (e.deltaPosition.sqrMagnitude > 0f)
                PencilSound();
            HandleTrace(_trace.AddPoint(TracePoint(e.localPosition)));
        }

        void OnTraceUp(PointerUpEvent e)
        {
            if (_trace == null || !_canvas.HasPointerCapture(e.pointerId))
                return;
            _canvas.ReleasePointer(e.pointerId);
            _trace.EndStroke();
            _canvas.MarkDirtyRepaint();
        }

        void HandleTrace(TraceEvent ev)
        {
            _canvas?.MarkDirtyRepaint();
            if (ev == TraceEvent.None)
                return;
            if (ev == TraceEvent.AllDone)
            {
                FinishStudy();
                return;
            }
            RefreshTraceText();
        }

        /// <summary>Giả lập tô xong chữ đang tô (test, Sim).</summary>
        public void SimulateTraceLetter()
        {
            if (Mode == NotebookMode.Trace && _trace != null)
                HandleTrace(_trace.SimulateLetter());
        }

        void DrawTrace(MeshGenerationContext ctx)
        {
            if (_trace == null)
                return;
            var r = ctx.visualElement.contentRect;
            float w = r.width, h = r.height;
            if (w <= 0f || h <= 0f)
                return;
            var p = ctx.painter2D;
            // Nét tính theo đơn vị chiều cao chữ (x tới Aspect); cỡ nét theo chiều cao ô.
            float aspect = _trace.Aspect;
            float wide = w;
            Vector2 P(Vector2 q) => new Vector2(r.x + q.x / aspect * wide, r.y + q.y * h);
            w = h;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            // Nét mờ để tô theo, chấm đỏ ở đầu mỗi nét (thứ tự nét).
            p.strokeColor = new Color(0.84f, 0.78f, 0.64f);
            p.lineWidth = w * 0.11f;
            foreach (var stroke in _trace.Guide)
            {
                if (stroke.Length < 2)
                    continue;
                p.BeginPath();
                p.MoveTo(P(stroke[0]));
                for (int i = 1; i < stroke.Length; i++)
                    p.LineTo(P(stroke[i]));
                p.Stroke();
            }
            p.fillColor = new Color(0.59f, 0.29f, 0.16f);
            foreach (var stroke in _trace.Guide)
            {
                if (stroke.Length == 0)
                    continue;
                p.BeginPath();
                p.Arc(P(stroke[0]), w * 0.025f, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Fill();
            }
            // Mực của người chơi.
            p.strokeColor = new Color(0.23f, 0.16f, 0.11f);
            p.lineWidth = w * 0.035f;
            foreach (var stroke in _trace.Ink)
            {
                if (stroke.Count < 2)
                    continue;
                p.BeginPath();
                p.MoveTo(P(stroke[0]));
                for (int i = 1; i < stroke.Count; i++)
                    p.LineTo(P(stroke[i]));
                p.Stroke();
            }
        }

        // ---------- Điền từ ----------

        void RenderFill()
        {
            _footer.text = string.Empty;
            Ui.Text("hbt-notebook-heading", _page, _studyHeading ?? S("study.fill_title"));
            if (_fill == null || _fill.Done)
                return;
            var item = _fill.Current;
            Ui.Text("hbt-study-sub", _page, string.Format(S("study.item_progress"), _fill.Index + 1, _fill.Count));
            Ui.Text("hbt-study-sentence", _page, item.Prompt.Replace(FillLogic.Blank, "______"));
            Ui.Text("hbt-study-sub", _page, item.Translation);
            var options = Ui.Box("hbt-study-options", _page);
            options.pickingMode = PickingMode.Position;
            foreach (int idx in _fill.Order)
            {
                int option = idx;
                var b = Ui.Button(item.Options[idx], () => ChooseFill(option), options, "hbt-study-option");
                if (_fill.WasWrong(idx))
                    b.AddToClassList("hbt-study-option--wrong");
            }
            if (!string.IsNullOrEmpty(_lastDone))
                Ui.Text("hbt-study-sub", _page, _lastDone).style.marginTop = 24;
        }

        void ChooseFill(int option)
        {
            if (_fill == null || _fill.Done)
                return;
            var item = _fill.Current;
            if (_fill.TryAnswer(option))
            {
                PencilSound();
                _lastDone = FillLogic.Filled(item);
            }
            if (_fill.Done)
                FinishStudy();
            else
                Render();
        }

        /// <summary>Giả lập chọn đúng (test, Sim).</summary>
        public void SimulateFill()
        {
            if (Mode != NotebookMode.Fill || _fill == null || _fill.Done)
                return;
            ChooseFill(_fill.Current.Answer);
        }

        // ---------- Viết câu ----------

        void RenderSentence()
        {
            _footer.text = string.Empty;
            Ui.Text("hbt-notebook-heading", _page, _studyHeading ?? S("study.sentence_title"));
            if (_sentence == null || _sentence.Done)
                return;
            var item = _sentence.Current;
            Ui.Text("hbt-study-sub", _page, string.Format(S("study.item_progress"), _sentence.Index + 1, _sentence.Count));
            Ui.Text("hbt-study-sub", _page, item.Translation);
            Ui.Text("hbt-study-sentence", _page, _sentence.Placed > 0 ? _sentence.Written + " …" : "…");
            var tokens = Ui.Box("hbt-study-options", _page);
            tokens.pickingMode = PickingMode.Position;
            foreach (int idx in _sentence.Shuffled)
            {
                int token = idx;
                var b = Ui.Button(item.Tokens[idx], () => ChooseToken(token), tokens, "hbt-study-option");
                if (_sentence.IsUsed(idx))
                {
                    b.AddToClassList("hbt-study-option--used");
                    b.SetEnabled(false);
                }
                else if (idx == _wrongToken)
                    b.AddToClassList("hbt-study-option--wrong");
            }
            if (!string.IsNullOrEmpty(_lastDone))
                Ui.Text("hbt-study-sub", _page, _lastDone).style.marginTop = 24;
        }

        void ChooseToken(int token)
        {
            if (_sentence == null || _sentence.Done)
                return;
            var item = _sentence.Current;
            int index = _sentence.Index;
            bool ok = _sentence.TryPlace(token);
            if (ok)
                PencilSound();
            _wrongToken = ok ? -1 : token;
            if (ok && _sentence.Index != index)
                _lastDone = string.Join(" ", item.Tokens);
            if (_sentence.Done)
                FinishStudy();
            else
                Render();
        }

        /// <summary>Giả lập viết đúng cả câu đang làm (test, Sim).</summary>
        public void SimulateSentence()
        {
            if (Mode != NotebookMode.Sentence || _sentence == null || _sentence.Done)
                return;
            var item = _sentence.Current;
            _sentence.SimulateSentence();
            _lastDone = string.Join(" ", item.Tokens);
            if (_sentence.Done)
                FinishStudy();
            else
                Render();
        }

        /// <summary>Bài học xong (hoặc director kết thúc sớm): đóng sổ rồi báo cho tác vụ học.</summary>
        public void FinishStudy()
        {
            if (Mode != NotebookMode.Trace && Mode != NotebookMode.Fill && Mode != NotebookMode.Sentence)
                return;
            var done = _onStudyDone;
            _onStudyDone = null;
            _canvas = null;
            Mode = NotebookMode.Browse;
            Close();
            done?.Invoke();
        }

        // ---------- Link ----------

        void RenderLink()
        {
            _footer.text = string.Empty;
            _linkCards.Clear();
            var board = Ui.Box("hbt-link-board", _page);
            board.pickingMode = PickingMode.Position;
            foreach (var p in _controller.Unlocked)
            {
                if (!p.Linkable)
                    continue;
                var card = Ui.Box("hbt-link-card", board);
                card.pickingMode = PickingMode.Position;
                card.userData = p.Id;
                Ui.Text("hbt-link-card-title", card, p.Title);
                Ui.Text("hbt-link-card-body", card, _controller.BodyFor(p));
                card.RegisterCallback<PointerDownEvent>(OnCardDown);
                card.RegisterCallback<PointerMoveEvent>(OnCardMove);
                card.RegisterCallback<PointerUpEvent>(OnCardUp);
                _linkCards.Add(card);
            }
        }

        /// <summary>Dòng gợi ý góc dưới sổ (sổ phủ lên HUD nên gợi ý phải nằm trong sổ).</summary>
        public void SetFooter(string text)
        {
            _footer.text = text ?? string.Empty;
        }

        /// <summary>Hai trang đích sáng nhẹ (gợi ý sau 60 s).</summary>
        public void HighlightPages(string a, string b)
        {
            foreach (var c in _linkCards)
            {
                var id = (string)c.userData;
                c.EnableInClassList("hbt-link-card--glow", id == a || id == b);
            }
        }

        void OnCardDown(PointerDownEvent e)
        {
            if (_link == null || _link.Linked)
                return;
            var card = (VisualElement)e.currentTarget;
            _dragging = card;
            _dragStart = e.position;
            card.CapturePointer(e.pointerId);
            card.AddToClassList("hbt-link-card--dragging");
            // Thẻ nằm trong flex: đổi thứ tự sibling sẽ làm trang nhảy khỏi con trỏ.
        }

        void OnCardMove(PointerMoveEvent e)
        {
            var card = (VisualElement)e.currentTarget;
            if (card != _dragging || !card.HasPointerCapture(e.pointerId))
                return;
            var d = (Vector2)e.position - _dragStart;
            card.style.translate = new Translate(d.x, d.y);
        }

        void OnCardUp(PointerUpEvent e)
        {
            var card = (VisualElement)e.currentTarget;
            if (card != _dragging)
                return;
            card.ReleasePointer(e.pointerId);
            card.RemoveFromClassList("hbt-link-card--dragging");
            _dragging = null;

            VisualElement target = null;
            foreach (var other in _linkCards)
                if (other != card && other.worldBound.Contains(e.position))
                    target = other;

            if (target != null && _link.TryLink((string)card.userData, (string)target.userData))
            {
                PageSound();
                // Đặt hai trang cạnh nhau.
                card.style.translate = new Translate(0, 0);
                var board = target.parent;
                card.RemoveFromHierarchy();
                board.Insert(board.IndexOf(target) + 1, card);
                HighlightPages((string)card.userData, (string)target.userData);
                var cb = _onLinked;
                _onLinked = null;
                cb?.Invoke();
            }
            else
            {
                // Sai cặp: trang trượt về chỗ (transition trong theme.uss), không báo lỗi.
                card.style.translate = new Translate(0, 0);
            }
        }

        /// <summary>Giả lập kéo trang (test / Sim).</summary>
        public bool SimulateLink(string a, string b)
        {
            if (_link == null || !_link.TryLink(a, b))
                return false;
            PageSound();
            HighlightPages(a, b);
            var cb = _onLinked;
            _onLinked = null;
            cb?.Invoke();
            return true;
        }
    }
}
