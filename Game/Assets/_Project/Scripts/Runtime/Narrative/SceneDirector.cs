using System;
using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Player;
using UnityEngine;

namespace HaiBanTay.Narrative
{
    /// <summary>Hàm chờ dùng chung cho director các cảnh. Ở chế độ Sim, bước chờ người chơi tự hoàn thành.</summary>
    public abstract class SceneDirector : MonoBehaviour
    {
        public const string CutsceneBlock = "cutscene";

        protected GameRoot Root { get; private set; }
        protected PlayerRig Rig => PlayerRig.Current;
        public string Beat { get; protected set; }

        /// <summary>Director của cảnh đang chạy (PerfRecorder ghi beat hiện tại vào CSV).</summary>
        public static SceneDirector Current { get; private set; }

        protected virtual void Awake()
        {
            Root = GameRoot.EnsureExists();
        }

        void OnEnable()
        {
            Current = this;
        }

        void OnDisable()
        {
            if (Current == this)
                Current = null;
        }

        protected string S(string key) => Root.Content.GetString(key);

        // ---- Chỉ dẫn từng bước cho cảnh cốt truyện (ShiftDirector tự tính theo bảng việc) ----
        string _guideText;
        Func<Transform> _guideTarget;
        Tasks.TaskBase _guideTask;
        float _nextGuide;
        int _guideHands = -1;

        /// <summary>Đặt dòng chỉ dẫn (khóa ui_strings, null = xóa) và đích của mốc (hàm, vì đích có thể đổi: người đi, vật kế tiếp).</summary>
        protected void Guide(string key, Func<Transform> target = null)
        {
            _guideText = key != null ? S(key) : null;
            _guideTarget = target;
            _guideTask = null;
            RefreshGuide();
        }

        /// <summary>Dòng chỉ dẫn là câu đã định dạng sẵn.</summary>
        protected void GuideText(string text, Func<Transform> target = null)
        {
            _guideText = text;
            _guideTarget = target;
            RefreshGuide();
        }

        /// <summary>Chỉ dẫn theo từng bước của một việc (quét, rửa…); hết bước thì về dòng chỉ dẫn chung.</summary>
        protected void GuideTask(Tasks.TaskBase task)
        {
            _guideTask = task;
            RefreshGuide();
        }

        protected void ClearGuide() => Guide(null);

        void RefreshGuide()
        {
            if (Root == null || Root.UI == null || Root.UI.Guide == null || this is Shift.ShiftDirector)
                return;
            var who = Rig != null ? Rig.interactor : null;
            if (_guideTask != null && who != null && _guideTask.State == Tasks.TaskState.Active &&
                _guideTask.Guide(who, out var target, out var key))
            {
                Root.UI.Guide.Set(S(key), target);
                return;
            }
            Root.UI.Guide.Set(_guideText, _guideText != null ? _guideTarget?.Invoke() : null);
        }

        void LateUpdate()
        {
            int hands = Rig != null && Rig.interactor != null ? Rig.interactor.HandsVersion : 0;
            // Đồ trên tay vừa đổi thì làm mới ngay (QA-0070), còn lại mỗi 0,2 s.
            if (Time.time < _nextGuide && hands == _guideHands)
                return;
            _guideHands = hands;
            _nextGuide = Time.time + 0.2f;
            if (_guideText != null || _guideTask != null)
                RefreshGuide();
        }

        protected void BeginCutscene()
        {
            Root.Input.Block(CutsceneBlock);
            Root.UI.Notebook.AllowToggle = false;
            Root.UI.Prompt.SetCrosshair(false);
        }

        protected void EndCutscene()
        {
            Root.Input.Unblock(CutsceneBlock);
            Root.UI.Notebook.AllowToggle = true;
            Root.UI.Prompt.SetCrosshair(true);
        }

        /// <summary>Chờ tới khi điều kiện đúng. Sim: sau Sim.StepSeconds gọi simulate (nếu có) rồi coi như xong.</summary>
        protected IEnumerator WaitFor(Func<bool> done, Action simulate = null, float timeout = -1f)
        {
            float start = Time.time;
            bool simulated = false;
            while (!done())
            {
                if (Sim.Enabled && !simulated && Time.time - start >= Sim.StepSeconds)
                {
                    simulated = true;
                    simulate?.Invoke();
                    if (simulate == null)
                        yield break;
                }
                if (timeout > 0f && Time.time - start >= timeout)
                    yield break;
                yield return null;
            }
        }

        /// <summary>Giữ phím giơ tay đủ lâu (B1.2, E2). Trả tiến độ ra vòng tròn ở tâm màn hình.</summary>
        protected IEnumerator WaitForRaise(float holdSeconds, string promptKey, float promptDelay)
        {
            float held = 0f, waited = 0f;
            bool prompted = false;
            // Giơ hai bàn tay là lúc duy nhất tay hiện (ArmsVisibility); khung hai bàn tay theo sau, ExitHandsShot ẩn lại.
            if (Rig != null) Rig.ArmsShown = true;
            while (held < holdSeconds)
            {
                waited += Time.deltaTime;
                if (!prompted && waited >= promptDelay && promptKey != null)
                {
                    prompted = true;
                    Root.UI.Prompt.SetScenePrompt(S(promptKey));
                    Guide("guide.raise");
                }
                bool raise = Root.Input.RaiseHeld || (Sim.Enabled && waited >= Sim.StepSeconds);
                held = raise ? held + Time.deltaTime : Mathf.Max(0f, held - Time.deltaTime * 2f);
                Root.UI.Prompt.SetProgress(held > 0f ? held / holdSeconds : (float?)null);
                if (Rig != null)
                    Rig.hands.SetPose(held > 0.1f ? HandPose.Open : HandPose.Lowered);
                yield return null;
            }
            Root.UI.Prompt.SetProgress(null);
            Root.UI.Prompt.SetScenePrompt(null);
            if (prompted)
                ClearGuide();
        }

        protected void SetGrime(float value, bool instant = false)
        {
            Root.State.Grime = value;
            Rig?.hands.SetGrime(value, instant);
        }
    }
}
