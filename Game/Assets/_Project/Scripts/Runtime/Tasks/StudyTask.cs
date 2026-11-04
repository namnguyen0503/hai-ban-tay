using System.Collections;
using System.Collections.Generic;
using System.Text;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Notebook;
using HaiBanTay.Player;
using UnityEngine;

namespace HaiBanTay.Tasks
{
    /// <summary>
    /// Buổi học tối trong sổ (M11, F07a): ôn các từ còn nợ từ tối trước bằng ghép từ, rồi làm bài chính của ngày (ghép từ,
    /// tô chữ, điền từ hoặc viết câu) theo kế hoạch của StudyPlanner. Người dạy đọc mẫu từng mục (StudyItem.Line hoặc
    /// FrenchPair.Line). Xong buổi: từ đã học vào vốn từ, từ bị bớt (tan ca muộn) dồn sang tối sau (StudyPlanner.Finish).
    /// </summary>
    public sealed class StudyTask : TaskBase
    {
        public Transform chair;
        public AnimationClip chairClip;
        public Transform studyLookAt;
        SitController _sit;
        StudyPlanner.Plan _plan;
        bool _stageDone;
        int _lastMatched;
        int _taughtPair = -1;
        int _taughtItem = -1;
        int[] _letterItem = new int[0];

        public StudyPlanner.Plan Plan => _plan;
        /// <summary>Bước đang làm: "review", "match", "trace", "fill", "sentence"; null khi chưa bắt đầu hoặc đã xong.</summary>
        public string Stage { get; private set; }
        public MatchWordsLogic Match { get; private set; }
        public TraceLogic Trace { get; private set; }
        public FillLogic Fill { get; private set; }
        public SentenceLogic Sentence { get; private set; }

        /// <summary>Gọi trước Activate (ShiftDirector lên kế hoạch theo giờ tan ca).</summary>
        public void Begin(StudyPlanner.Plan plan)
        {
            Rearm();
            _plan = plan;
        }

        void Awake()
        {
            idleHintSeconds = 0f;
        }

        protected override void OnActivated()
        {
            if (_plan == null || _plan.Kind == EveningData.Rest)
            {
                Complete();
                return;
            }
            var rig = PlayerRig.Current;
            if (rig != null && chair != null)
            {
                _sit = rig.GetComponent<SitController>();
                _sit.SitForStudy(chair, studyLookAt != null ? studyLookAt.position : chair.position + Vector3.left, chairClip);
            }
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            var root = GameRoot.Instance;
            if (root == null)
                yield break;
            yield return new WaitForSeconds(0.6f);
            var nb = root.UI.Notebook;

            if (_plan.Kind == EveningData.Match)
            {
                // Ghép từ: từ nợ và từ của bài chung một lượt ghép.
                var pairs = new List<FrenchPair>(_plan.Review);
                pairs.AddRange(_plan.Pairs);
                yield return RunMatch(nb, pairs, _plan.Review.Count > 0 && _plan.Pairs.Count == 0 ? "study.review_title" : null, "match");
            }
            else
            {
                if (_plan.Review.Count > 0)
                    yield return RunMatch(nb, _plan.Review, "study.review_title", "review");
                switch (_plan.Kind)
                {
                    case EveningData.Trace:
                        yield return RunTrace(root, nb);
                        break;
                    case EveningData.Fill:
                        yield return RunFill(root, nb);
                        break;
                    case EveningData.Sentence:
                        yield return RunSentence(root, nb);
                        break;
                }
            }
            Stage = null;
            Complete();
        }

        IEnumerator RunMatch(NotebookView nb, List<FrenchPair> pairs, string headingKey, string stage)
        {
            if (pairs.Count == 0)
                yield break;
            Stage = stage;
            Match = new MatchWordsLogic(pairs);
            _lastMatched = 0;
            _taughtPair = -1;
            _stageDone = false;
            TeachNextPair();
            var root = GameRoot.Instance;
            nb.OpenMatch(Match, () => _stageDone = true, headingKey != null ? root.Content.GetString(headingKey) : null);
            while (!_stageDone)
            {
                if (Match.MatchedCount != _lastMatched)
                {
                    _lastMatched = Match.MatchedCount;
                    ReportProgress();
                    if (!Match.Done)
                        TeachNextPair();
                }
                yield return null;
            }
        }

        void TeachNextPair()
        {
            for (int i = 0; i < Match.Count; i++)
            {
                if (Match.IsMatched(i))
                    continue;
                if (i != _taughtPair && !string.IsNullOrEmpty(Match.Pair(i).Line))
                {
                    _taughtPair = i;
                    GameRoot.Instance?.Subtitles.Play(Match.Pair(i).Line);
                }
                return;
            }
        }

        IEnumerator RunTrace(GameRoot root, NotebookView nb)
        {
            Stage = EveningData.Trace;
            Trace = StudyPlanner.BuildTrace(root.Content, _plan.Items);
            // Lượt tô thứ k (một từ) thuộc mục nào (để hiện từ và đọc mẫu khi sang mục mới); mục không có nét nào bị bỏ.
            var map = new List<int>();
            for (int i = 0; i < _plan.Items.Count; i++)
                foreach (char c in _plan.Items[i].Text ?? string.Empty)
                    if (root.Content.TryGetLetter(c.ToString(), out _))
                    {
                        map.Add(i);
                        break;
                    }
            _letterItem = map.ToArray();
            _taughtItem = -1;
            _stageDone = false;
            TeachItem(ItemOfLetter(0));
            nb.OpenTrace(Trace, TraceCaption, () => _stageDone = true);
            int seen = 0;
            while (!_stageDone)
            {
                if (Trace.LettersDone != seen)
                {
                    seen = Trace.LettersDone;
                    ReportProgress();
                    TeachItem(ItemOfLetter(seen));
                }
                yield return null;
            }
        }

        int ItemOfLetter(int letter)
        {
            if (_letterItem.Length == 0)
                return -1;
            return _letterItem[Mathf.Clamp(letter, 0, _letterItem.Length - 1)];
        }

        /// <summary>Từ đang tô; dòng dưới là nghĩa.</summary>
        string TraceCaption()
        {
            if (Trace == null || Trace.Done)
                return string.Empty;
            int item = ItemOfLetter(Trace.LettersDone);
            if (item < 0)
                return Trace.Current;
            var sb = new StringBuilder(_plan.Items[item].Text ?? string.Empty);
            sb.Append("\n<size=60%>").Append(_plan.Items[item].Vi).Append("</size>");
            return sb.ToString();
        }

        void TeachItem(int item)
        {
            if (item < 0 || item == _taughtItem || item >= _plan.Items.Count)
                return;
            _taughtItem = item;
            var line = _plan.Items[item].Line;
            if (!string.IsNullOrEmpty(line))
                GameRoot.Instance?.Subtitles.Play(line);
        }

        IEnumerator RunFill(GameRoot root, NotebookView nb)
        {
            Stage = EveningData.Fill;
            Fill = StudyPlanner.BuildFill(root.Content, _plan.Items);
            _taughtItem = -1;
            _stageDone = false;
            TeachItem(0);
            nb.OpenFill(Fill, () => _stageDone = true, _plan.Heading);
            int seen = 0;
            while (!_stageDone)
            {
                if (Fill.Index != seen)
                {
                    seen = Fill.Index;
                    ReportProgress();
                    TeachItem(seen);
                }
                yield return null;
            }
        }

        IEnumerator RunSentence(GameRoot root, NotebookView nb)
        {
            Stage = EveningData.Sentence;
            Sentence = StudyPlanner.BuildSentence(_plan.Items);
            _taughtItem = -1;
            _stageDone = false;
            TeachItem(0);
            nb.OpenSentence(Sentence, () => _stageDone = true, _plan.Heading);
            int seen = 0;
            while (!_stageDone)
            {
                if (Sentence.Index != seen)
                {
                    seen = Sentence.Index;
                    ReportProgress();
                    TeachItem(seen);
                }
                yield return null;
            }
        }

        protected override void OnCompleted()
        {
            StopAllCoroutines();
            if (_sit != null) _sit.Stand();
            Stage = null;
            var root = GameRoot.Instance;
            if (_plan != null && root != null)
                StudyPlanner.Finish(_plan, root.State);
            var nb = root != null ? root.UI.Notebook : null;
            if (nb != null && nb.InStudy)
                nb.Close();
        }
    }
}
