using HaiBanTay.Core;

namespace HaiBanTay.Tasks
{
    /// <summary>Buổi tối: ghép các cặp từ Pháp–Việt của một bài (french_words.vi.json, trường set) trong sổ.
    /// Câu đọc mẫu từ đầu do ShiftDirector phát trước khi mở sổ; mỗi lần ghép đúng, từ đó vào vốn từ (GameState)
    /// và câu mẫu của từ chưa ghép kế tiếp được đọc.</summary>
    public sealed class MatchWordsTask : TaskBase
    {
        /// <summary>Bài học (FrenchPair.Set). Trống thì dùng mọi từ có bài.</summary>
        public string set;

        int _lastMatched;
        int _taught = -1;

        public MatchWordsLogic Logic { get; private set; }

        protected override void OnActivated()
        {
            var root = GameRoot.Instance;
            var pairs = root.Content.WordsInSet(set);
            if (pairs.Count == 0)
                pairs = root.Content.FrenchWords.Pairs.FindAll(p => !string.IsNullOrEmpty(p.Set));
            Logic = new MatchWordsLogic(pairs);
            _lastMatched = 0;
            _taught = 0; // câu từ đầu đã phát trước khi mở sổ
            root.UI.Notebook.OpenMatch(Logic, Complete);
        }

        void TeachNext()
        {
            for (int i = 0; i < Logic.Count; i++)
            {
                if (Logic.IsMatched(i))
                    continue;
                if (i != _taught && !string.IsNullOrEmpty(Logic.Pair(i).Line))
                {
                    _taught = i;
                    GameRoot.Instance?.Subtitles.Play(Logic.Pair(i).Line);
                }
                return;
            }
        }

        protected override void Update()
        {
            base.Update();
            if (State != TaskState.Active || Logic == null)
                return;
            if (Logic.MatchedCount != _lastMatched)
            {
                _lastMatched = Logic.MatchedCount;
                LearnMatched();
                ReportProgress();
                if (!Logic.Done)
                    TeachNext();
            }
        }

        void LearnMatched()
        {
            var state = GameRoot.Instance?.State;
            if (state == null)
                return;
            for (int i = 0; i < Logic.Count; i++)
                if (Logic.IsMatched(i))
                    state.Learn(Logic.Pair(i).Id);
        }

        protected override void OnCompleted()
        {
            // Sim / phím demo: coi như đã học hết bài.
            if (Logic != null)
            {
                var state = GameRoot.Instance?.State;
                for (int i = 0; i < Logic.Count; i++)
                    state?.Learn(Logic.Pair(i).Id);
            }
            var nb = GameRoot.Instance?.UI.Notebook;
            if (nb != null && nb.IsOpen && nb.Mode == Notebook.NotebookMode.Match)
                nb.Close();
        }
    }
}
