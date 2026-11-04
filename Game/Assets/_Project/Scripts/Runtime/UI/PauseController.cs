using HaiBanTay.Core;
using HaiBanTay.Player;
using UnityEngine;

namespace HaiBanTay.UI
{
    /// <summary>Esc: tạm dừng / tiếp tục (hoặc đóng sổ, đóng cài đặt). Tab: mở/đóng sổ quan sát.
    /// Màn chờ cuối đặt QuitOnEscape = true để Esc thoát game. Ở menu chính không tạm dừng.</summary>
    public sealed class PauseController : MonoBehaviour
    {
        const string BlockReason = "pause";

        GameInput _input;
        UIRoot _ui;
        SceneFlow _scenes;
        float _savedScale = 1f;

        public static bool QuitOnEscape;
        bool _paused;
        /// <summary>Đang tạm dừng (menu tạm dừng, hoặc màn Cách chơi/Cài đặt mở từ đó).</summary>
        public bool Paused => _paused;

        public void Initialize(GameInput input, UIRoot ui, SceneFlow scenes)
        {
            _input = input;
            _ui = ui;
            _scenes = scenes;
            if (ui.Root == null)
                return;
            var content = GameRoot.Instance.Content;
            var menu = new PauseMenu(ui.PauseLayer, new (string, System.Action)[]
            {
                (content.GetString("pause.resume"), Resume),
                (content.GetString("pause.help"), () => OpenHelp(false)),
                (content.GetString("pause.settings"), OpenSettings),
                (content.GetString("pause.menu"), ToMenu),
                (content.GetString("pause.quit"), Quit),
            });
            ui.SetPauseMenu(menu);
        }

        void Update()
        {
            if (_input == null || _ui == null || _ui.Root == null || DevTools.DebugNotes.AnyOpen)
                return;
            if (AutoHelpDue())
            {
                DoPause();
                OpenHelp(true);
                return;
            }
            if (_input.PausePressed)
            {
                if (_ui.Help != null && _ui.Help.Visible)
                    _ui.Help.Close();
                else if (_ui.Settings.Visible)
                    _ui.Settings.Close();
                else if (QuitOnEscape)
                    Quit();
                else if (_scenes != null && _scenes.CurrentScene == SceneFlow.MenuScene)
                    return;
                else if (Paused)
                    Resume();
                else if (_ui.Notebook.IsOpen && _ui.Notebook.Mode == Notebook.NotebookMode.Browse)
                    _ui.Notebook.Close();
                else
                    DoPause();
            }
            else if (_input.NotebookPressed && !Paused)
            {
                if (_ui.Notebook.IsOpen || _input.GameplayEnabled)
                    _ui.Notebook.Toggle();
            }
        }

        /// <summary>Tạm dừng như bấm Esc (test thử phá: tạm dừng giữa thao tác).</summary>
        public void Pause()
        {
            if (!Paused)
                DoPause();
        }

        void DoPause()
        {
            _paused = true;
            _savedScale = Time.timeScale;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            _ui.Pause.Show(true);
            _input.Block(BlockReason);
            _input.SetCursorFree(true);
        }

        public void Resume()
        {
            if (!Paused)
                return;
            _paused = false;
            Time.timeScale = _savedScale;
            AudioListener.pause = false;
            _ui.Pause.Show(false);
            _input.Unblock(BlockReason);
            _input.SetCursorFree(_ui.Notebook.IsOpen);
        }

        /// <summary>Lần đầu điều khiển được nhân vật trong cảnh chơi: hiện Cách chơi một lần (không trong test/batchmode).</summary>
        bool AutoHelpDue()
        {
            if (GameSettings.Current.HelpSeen || Application.isBatchMode || Sim.Enabled || _ui.Help == null || _ui.Help.Visible || Paused)
                return false;
            if (_scenes == null || _scenes.CurrentScene == SceneFlow.MenuScene || !_input.GameplayEnabled)
                return false;
            return Player.PlayerRig.Current != null;
        }

        void OpenHelp(bool first)
        {
            _ui.Pause.Show(false);
            _ui.Help.Open(() =>
            {
                if (!GameSettings.Current.HelpSeen)
                {
                    var settings = GameSettings.Current.Clone();
                    settings.HelpSeen = true;
                    GameSettings.Apply(settings);
                }
                if (first)
                    Resume();
                else if (Time.timeScale == 0f)
                    _ui.Pause.Show(true);
            });
        }

        void OpenSettings()
        {
            _ui.Pause.Show(false);
            _ui.Settings.Open(() =>
            {
                if (Time.timeScale == 0f)
                    _ui.Pause.Show(true);
            });
        }

        void ToMenu()
        {
            Resume();
            _scenes?.Load(SceneFlow.MenuScene);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
