using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HaiBanTay.Core
{
    /// <summary>Nạp cảnh ở chế độ Single, có màn đen chuyển cảnh và MapTransition giữa các cảnh.</summary>
    public sealed class SceneFlow : MonoBehaviour
    {
        public const string BootScene = "00_Boot";
        public const string MenuScene = "00_Menu";
        /// <summary>Cảnh mở đầu (Sài Gòn 1911), nằm giữa disclaimer và 01. Không thuộc GameScenes để giữ chỉ số cảnh 1..3.</summary>
        public const string PrologueScene = "00_Prologue";
        public static readonly string[] GameScenes = { "01_NhaRong", "02_Galley", "03_Marseille" };
        public const float FadeSeconds = 0.4f;

        /// <summary>Prologue đặt khi vào 01 từ chính nó: 01 giữ tiến trình (cờ s1.hands.raised, thẻ) và bỏ beat giơ tay B1.2.
        /// NhaRongDirector đọc rồi xóa cờ này.</summary>
        public static bool FromPrologue;

        GameRoot _root;
        bool _loading;

        public event Action<string> SceneLoaded;
        public bool IsLoading => _loading;
        public string CurrentScene => SceneManager.GetActiveScene().name;

        public void Initialize(GameRoot root)
        {
            _root = root;
        }

        /// <summary>Nạp cảnh game theo số 1..3 (phím demo): xóa trạng thái tạm, giữ tiến trình sổ.</summary>
        public void LoadByNumber(int number)
        {
            if (number < 1 || number > GameScenes.Length)
            {
                Debug.LogError($"SceneFlow: số cảnh không hợp lệ {number}");
                return;
            }
            Load(GameScenes[number - 1]);
        }

        public void Load(string sceneName)
        {
            if (_loading)
                return;
            StartCoroutine(LoadRoutine(sceneName, null));
        }

        /// <summary>Màn đen → bản đồ hải trình (map_transitions.vi.json) → cảnh mới.</summary>
        public void LoadWithTransition(string sceneName, string transitionId)
        {
            if (_loading)
                return;
            StartCoroutine(LoadRoutine(sceneName, transitionId));
        }

        IEnumerator LoadRoutine(string sceneName, string transitionId)
        {
            _loading = true;
            yield return Fade(0f, 1f);
            _root.UI.ResetTransient();
            _root.Input.ClearBlocks();
            Time.timeScale = Sim.Enabled ? Time.timeScale : DevSpeed.Scale;

            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            op.allowSceneActivation = transitionId == null;
            if (transitionId != null)
            {
                var data = _root.Content.MapTransitions.Transitions.Find(t => t.Id == transitionId);
                if (data != null)
                {
                    _root.UI.SetFade(0f);
                    yield return _root.UI.Map.Play(data);
                    _root.UI.SetFade(1f);
                }
                op.allowSceneActivation = true;
            }
            while (!op.isDone)
                yield return null;
            yield return Fade(1f, 0f);
            _loading = false;
            SceneLoaded?.Invoke(sceneName);
        }

        public IEnumerator Fade(float from, float to)
        {
            return Fade(from, to, FadeSeconds);
        }

        public IEnumerator Fade(float from, float to, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                _root.UI.SetFade(Mathf.Lerp(from, to, t / seconds));
                yield return null;
            }
            _root.UI.SetFade(to);
        }
    }
}
