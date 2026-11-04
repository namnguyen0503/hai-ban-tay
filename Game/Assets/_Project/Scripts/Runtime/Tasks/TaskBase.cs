using System;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using UnityEngine;

namespace HaiBanTay.Tasks
{
    public enum TaskState
    {
        Locked,
        Active,
        Done
    }

    /// <summary>Vòng đời tác vụ Locked → Active → Done; phát doneEvent khi xong; đứng yên quá 30 s thì gợi ý hint.idle.</summary>
    public abstract class TaskBase : MonoBehaviour
    {
        public string doneEvent;
        public float idleHintSeconds = 30f;

        GameEvents _events;
        float _lastProgress;
        bool _hintShown;

        public TaskState State { get; private set; } = TaskState.Locked;
        public event Action<TaskBase> Completed;

        /// <summary>Bus sự kiện (mặc định của GameRoot; test gán riêng).</summary>
        public GameEvents Events
        {
            get => _events ?? GameRoot.Instance?.Events;
            set => _events = value;
        }

        protected void Rearm()
        {
            if (State == TaskState.Active) throw new InvalidOperationException("Tác vụ đang hoạt động.");
            State = TaskState.Locked;
        }

        public void Activate()
        {
            if (State != TaskState.Locked)
                return;
            State = TaskState.Active;
            _lastProgress = Time.time;
            _hintShown = false;
            OnActivated();
        }

        /// <summary>Hoàn thành ngay (Sim / phím demo).</summary>
        public void ForceComplete()
        {
            if (State == TaskState.Locked)
                Activate();
            Complete();
        }

        protected void ReportProgress()
        {
            _lastProgress = Time.time;
            if (_hintShown)
            {
                _hintShown = false;
                GameRoot.Instance?.UI.Prompt.HideHint();
            }
        }

        protected void Complete()
        {
            if (State == TaskState.Done)
                return;
            State = TaskState.Done;
            OnCompleted();
            if (!string.IsNullOrEmpty(doneEvent))
                Events?.Raise(doneEvent);
            Completed?.Invoke(this);
        }

        protected virtual void OnActivated() { }
        protected virtual void OnCompleted() { }

        protected virtual void Update()
        {
            if (State != TaskState.Active || _hintShown || idleHintSeconds <= 0f)
                return;
            if (Time.time - _lastProgress < idleHintSeconds)
                return;
            _hintShown = true;
            var root = GameRoot.Instance;
            if (root != null)
                root.UI.Prompt.ShowHint(root.Content.GetString("hint.idle"), 8f);
        }

        // ---- Chỉ dẫn từng bước (ShiftDirector hỏi vài lần mỗi giây) ----

        /// <summary>Bước tiếp theo ở việc này cho người chơi: chỗ cần tới (target) và khóa câu chỉ dẫn (ui_strings guide.*).
        /// false = việc này không còn gì để chỉ.</summary>
        public virtual bool Guide(Interactor who, out Transform target, out string key)
        {
            target = null;
            key = null;
            return false;
        }

        /// <summary>Người chơi đang dở tay ở việc này (đang cầm đồ, dụng cụ của nó): chỉ dẫn bám việc này trước các lệnh khác.</summary>
        public virtual bool Engaged(Interactor who) => false;

        protected static bool Step(Component at, string key, out Transform target, out string outKey)
        {
            target = at != null ? at.transform : null;
            outKey = key;
            return key != null;
        }

        // ---- TaskTarget gọi vào ----
        public virtual string PromptFor(TaskTarget target, Interactor who) => null;
        public virtual bool CanInteract(TaskTarget target, Interactor who) => false;
        public virtual void OnInteract(TaskTarget target, Interactor who) { }
    }
}
