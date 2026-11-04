using System.Collections;
using System.Collections.Generic;
using HaiBanTay.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HaiBanTay.Narrative
{
    /// <summary>Cảnh 00_Menu: Tiếp tục · Chơi mới · Chọn chương · Cài đặt · Thoát. Ở chế độ Sim tự chọn Chơi mới.
    /// "Chơi mới" hiện màn disclaimer rồi vào prologue (00_Prologue) và 01; "Tiếp tục" và "Chọn chương" không hiện disclaimer.</summary>
    public sealed class MenuDirector : SceneDirector
    {
        const string Block = "menu";

        [Tooltip("Màn disclaimer của \"Chơi mới\" tự qua sau chừng này giây (thời gian thật) nếu người chơi không bấm phím.")]
        public float disclaimerMaxSeconds = 8f;

        SaveData _save;
        bool _starting;

        IEnumerator Start()
        {
            Beat = "Menu";
            Root.UI.Notebook.AllowToggle = false;
            Root.UI.Prompt.SetCrosshair(false);
            Root.Input.Block(Block);
            Root.Input.SetCursorFree(true);
            Root.UI.Menu.SetTitle(S("menu.title"), S("menu.subtitle"));
            Root.Audio.PlayMusic("Music/menu", loop: true);
            _save = SaveSystem.Read();
            ShowMain();
            if (Sim.Enabled)
            {
                yield return new WaitForSeconds(Sim.StepSeconds);
                NewGame();
            }
        }

        void ShowMain()
        {
            Root.UI.Menu.ShowButtons(new (string, System.Action)[]
            {
                (S("menu.continue"), _save != null ? Continue : (System.Action)null),
                (S("menu.new"), NewGame),
                (S("menu.chapters"), ShowChapters),
                (S("menu.settings"), OpenSettings),
                (S("menu.quit"), Quit),
            });
        }

        void ShowChapters()
        {
            bool all = GameSettings.Current.UnlockAllChapters;
            int maxDay = _save != null ? _save.MaxDay : GameState.FirstDay;
            bool marseille = _save != null && _save.ReachedMarseille;
            var items = new List<(string, System.Action)> { (S("scene.00.name"), BeginPrologue), (S("scene.01.name"), BeginNewGame) };
            foreach (var day in Root.Content.Days.Days)
            {
                int n = day.Number;
                bool open = all || n <= maxDay;
                items.Add(($"{day.Title} · {day.Leg}", open ? () => StartDay(n, maxDay, marseille) : (System.Action)null));
            }
            items.Add((S("scene.03.name"), all || marseille ? () => StartMarseille(maxDay) : (System.Action)null));
            items.Add((S("menu.back"), ShowMain));
            Root.UI.Menu.ShowButtons(items);
        }

        void OpenSettings()
        {
            Root.UI.Menu.Hide();
            Root.UI.Settings.Open(ShowMain);
        }

        void Close()
        {
            Root.UI.Menu.Hide();
            Root.Input.Unblock(Block);
            Root.Input.SetCursorFree(false);
            Root.UI.Prompt.SetCrosshair(true);
            Root.UI.Notebook.AllowToggle = true;
            Root.Audio.StopMusic(2f);
        }

        /// <summary>Nút "Chơi mới": màn disclaimer rồi mới vào prologue.</summary>
        void NewGame()
        {
            if (_starting)
                return;
            _starting = true;
            StartCoroutine(NewGameWithDisclaimer());
        }

        IEnumerator NewGameWithDisclaimer()
        {
            // Menu ẩn nhưng vẫn chặn điều khiển; màn chữ nền đen sẽ được SceneFlow dọn khi chuyển cảnh.
            Root.UI.Menu.Hide();
            yield return Root.UI.Card.ShowUntil(S("disclaimer.body"), S("disclaimer.continue"), AnyKeyPressed, disclaimerMaxSeconds);
            BeginPrologue();
        }

        /// <summary>Phím, chuột trái hoặc chạm màn hình vừa bấm. Ở chế độ Sim coi như đã bấm.</summary>
        static bool AnyKeyPressed()
        {
            if (Sim.Enabled)
                return true;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            var touch = Touchscreen.current;
            return (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
                   || (mouse != null && mouse.leftButton.wasPressedThisFrame)
                   || (touch != null && touch.primaryTouch.press.wasPressedThisFrame);
        }

        /// <summary>Vào prologue Sài Gòn 1911; prologue xong tự sang 01.</summary>
        void BeginPrologue()
        {
            Close();
            Root.ResetProgress();
            SceneFlow.FromPrologue = false;
            Root.Scenes.Load(SceneFlow.PrologueScene);
        }

        /// <summary>Vào cảnh 01 ngay, không disclaimer, không prologue (Chọn chương → Bến Nhà Rồng): giữ beat giơ tay B1.2.</summary>
        void BeginNewGame()
        {
            Close();
            Root.ResetProgress();
            SceneFlow.FromPrologue = false;
            Root.Scenes.Load(SceneFlow.GameScenes[0]);
        }

        void Continue()
        {
            var data = SaveSystem.Read();
            if (data == null)
            {
                _save = null;
                ShowMain();
                return;
            }
            Close();
            Root.ResetProgress();
            SaveSystem.Apply(data, Root.State, Root.Flags, Root.Notebook, Root.Events);
            // Bản lưu sau prologue ở 01: vào 01 như từ prologue (giữ tiến trình, bỏ giơ tay).
            if (data.Scene == SceneFlow.GameScenes[0])
            {
                SceneFlow.FromPrologue = true;
                Root.Scenes.Load(SceneFlow.GameScenes[0]);
                return;
            }
            Root.Scenes.Load(data.Scene == SceneFlow.GameScenes[2] ? SceneFlow.GameScenes[2] : SceneFlow.GameScenes[1]);
        }

        void StartDay(int day, int maxDay, bool marseille)
        {
            Close();
            ChapterStart.PrepareDay(Root, day, maxDay, marseille);
            Root.Scenes.Load(SceneFlow.GameScenes[1]);
        }

        void StartMarseille(int maxDay)
        {
            Close();
            ChapterStart.PrepareMarseille(Root, maxDay);
            Root.Scenes.Load(SceneFlow.GameScenes[2]);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnDestroy()
        {
            if (Root == null)
                return;
            Root.Input.Unblock(Block);
            Root.UI.Menu.Hide();
        }
    }
}
