using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HaiBanTay.Core;
using HaiBanTay.Ending;
using HaiBanTay.Interaction;
using HaiBanTay.Narrative;
using HaiBanTay.Notebook;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace HaiBanTay.Tests
{
    [Category("Playthrough"), Explicit]
    public sealed class PlaythroughTests
    {
        PlaythroughLog _log;
        PlayerBot _bot;
        string _oldSaveFolder, _oldSettingsFolder;
        float _oldScale;
        int _oldFrameRate, _oldVSync;
        bool _oldIgnore;
        string _coverage = "Chưa bắt đầu.";
        bool _inputSettingsSaved;
        InputSettings.EditorInputBehaviorInPlayMode _oldEditorInput;
        InputSettings.BackgroundBehavior _oldBackground;
        readonly List<string> _completed = new List<string>();

        static Vector3 V(float x, float z, float y = 0f) => new Vector3(x, y, z);
        static string Scene => SceneManager.GetActiveScene().name;
        static string Beat => SceneDirector.Current?.Beat ?? "";

        [UnityTest, Timeout(7200000)]
        public IEnumerator PlayWholeGameThroughPlayerInput()
        {
            yield return Run(Stage.Whole);
            Assert.IsTrue(_bot != null && _bot.Ok && Beat == "End", "Playthrough stopped; triage BUGS.md and run_log.jsonl before attributing bot failures to the game.");
            // Đi hết game chưa đủ: còn BLOCKER/MAJOR (kẹt, NPC không tới, vật lơ lửng đã xác nhận…) thì chưa PASS.
            Assert.IsFalse(_log.Findings.Any(f => f.severity == "BLOCKER" || f.severity == "MAJOR"),
                "Bot phát hiện BLOCKER/MAJOR; đọc evidence rồi sửa trước khi coi cả game là PASS.");
        }

        /// <summary>Đoạn gỡ lỗi: menu → disclaimer → prologue → 01 Nhà Rồng tới khi vào bếp (sang 02).</summary>
        [UnityTest, Timeout(1800000)]
        public IEnumerator PlayPrologueAndNhaRongThroughPlayerInput()
        {
            yield return Run(Stage.PrologueAndQuay);
            if (_bot != null && _bot.Ok)
                yield return _bot.Wait("galley_loaded_after_quay", () => Scene == "02_Galley" && !GameRoot.Instance.Scenes.IsLoading, 60f, 90f);
            Assert.IsTrue(_bot != null && _bot.Ok && Scene == "02_Galley", "Prologue/Nhà Rồng dừng; xem Logs/playthrough.");
        }

        /// <summary>Đoạn gỡ lỗi: một ngày trên tàu (BOT_DAY, mặc định 1) từ ca sáng tới buổi tối và nghỉ. Ngày vào bằng GameState (chỉ để gỡ lỗi).</summary>
        [UnityTest, Timeout(3600000)]
        public IEnumerator PlayGalleyDayThroughPlayerInput()
        {
            yield return Run(Stage.OneDay);
            Assert.IsTrue(_bot != null && _bot.Ok, "Ngày dừng; xem Logs/playthrough.");
            Assert.AreEqual(1, _completed.Count(c => c.StartsWith("Ngày ")), "Bot phải hoàn tất cả ca và buổi tối của ngày đã chọn.");
            Assert.IsFalse(_log.Findings.Any(f => f.severity == "BLOCKER" || f.severity == "MAJOR"),
                "Bot phát hiện BLOCKER/MAJOR; đọc evidence rồi sửa trước khi coi ngày là PASS.");
        }

        [UnityTest, Timeout(1200000)]
        public IEnumerator PlayMarseilleThroughPlayerInput()
        {
            _oldSaveFolder = SaveSystem.Folder; _oldSettingsFolder = GameSettings.Folder;
            _oldScale = Time.timeScale; _oldIgnore = LogAssert.ignoreFailingMessages;
            _oldFrameRate = Application.targetFrameRate; _oldVSync = QualitySettings.vSyncCount;
            _log = new PlaythroughLog();
            SaveSystem.Folder = GameSettings.Folder = Path.Combine(PlaythroughLog.Folder, "storage", _log.Run);
            GameRoot.EnsureExists().ResetProgress();
            if (BotVideo.Requested) BotVideo.Begin(_log.Run, VideoMeta);
            Sim.Enabled = false;
            EndingDirector.TimeScaleForTests = Time.timeScale = 1f;
            Time.captureDeltaTime = 1f / 60f;
            _bot = new PlayerBot(_log);
            SceneManager.LoadScene(SceneFlow.GameScenes[2]);
            yield return null; yield return null;
            _bot.BindScene();
            float started = Time.time;
            yield return Marseille();
            _coverage = "Cảnh 03 qua input thật; không Sim, teleport, Fire hoặc gọi station trực tiếp.";
            _log.Note($"Marseille input: {Time.time - started:0.0} giây game.");
            Assert.IsTrue(_bot.Ok && Beat == "End", "Marseille input dừng; xem Logs/playthrough.");
        }

        enum Stage { PrologueAndQuay, OneDay, Whole }

        IEnumerator Run(Stage stage)
        {
            // The existing helper creates Logs/shots from its Folder getter even with an absolute output name.
            Assert.IsTrue(Directory.Exists(Path.GetFullPath(Path.Combine(PlaythroughLog.Folder, "..", "shots"))),
                "ScreenshotCapture requires existing Logs/shots; do not create directories outside the approved scope.");
            _oldSaveFolder = SaveSystem.Folder; _oldSettingsFolder = GameSettings.Folder;
            _oldScale = Time.timeScale; _oldIgnore = LogAssert.ignoreFailingMessages;
            _oldFrameRate = Application.targetFrameRate; _oldVSync = QualitySettings.vSyncCount;
            _log = new PlaythroughLog();
            SaveSystem.Folder = GameSettings.Folder = Path.Combine(PlaythroughLog.Folder, "storage", _log.Run);
            // Errors are collected with their stacks, rather than aborting before later bugs can be observed.
            LogAssert.ignoreFailingMessages = true;
            GameRoot.EnsureExists();
            if (BotVideo.Requested) BotVideo.Begin(_log.Run, VideoMeta);
            // BOT_FPS=30: chơi như máy Android giới hạn 30 khung/giây (thử phá W1); mặc định 60.
            PlayerBot.Fps = int.TryParse(Environment.GetEnvironmentVariable("BOT_FPS"), out int fps) && fps >= 15 ? fps : 60;
            // BOT_FAST=1: giờ game cố định 1/Fps mỗi khung (vật lý, dò xuyên vật, video vẫn theo nhịp game), bỏ giới hạn khung thật.
            PlayerBot.Fast = Environment.GetEnvironmentVariable("BOT_FAST") == "1";
            PlayerBot.Cinematic = BotVideo.Requested && Environment.GetEnvironmentVariable("BOT_VIDEO_UI") == "0";
            Time.captureDeltaTime = PlayerBot.Fast ? 1f / PlayerBot.Fps : 0f;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = PlayerBot.Fast ? PlayerBot.Fps * PlayerBot.FastMultiplier : PlayerBot.Fps;
            _bot = new PlayerBot(_log);
            _log.Note("Bot chạy " + PlayerBot.Fps + " khung/giây" + (PlayerBot.Fast ? ", chế độ nhanh (captureDeltaTime)." : "."));
            float began = Time.realtimeSinceStartup;
            if (stage == Stage.OneDay)
            {
                int startDay = int.TryParse(Environment.GetEnvironmentVariable("BOT_DAY"), out int d) ? d : 1;
                GameRoot.Instance.ResetProgress();
                GameRoot.Instance.State.Day = GameRoot.Instance.State.MaxDay = startDay;
                SceneManager.LoadScene(SceneFlow.GameScenes[1]);
                yield return DaysLoop(startDay, oneOnly: true);
                yield break;
            }
            SceneManager.LoadScene(SceneFlow.MenuScene);
            yield return _bot.Wait("menu_ready", () => GameRoot.Instance.UI.Menu.Visible);
            if (!_bot.Ok) yield break;
            _bot.BindScene();
            yield return _log.Checkpoint("menu_new_game");
            // UI pointer dispatch is the approved exception: menu/notebook have no GameInput simulation bindings.
            yield return ClickButton(GameRoot.Instance.Content.GetString("menu.new"), "new_game");
            if (!_bot.Ok) yield break;
            yield return Disclaimer();
            if (!_bot.Ok) yield break;
            yield return _bot.Wait("prologue_loaded", () => Scene == "00_Prologue" && PlayerRig.Current != null && !GameRoot.Instance.Scenes.IsLoading);
            if (!_bot.Ok) yield break;
            _bot.BindScene(); Time.timeScale = 1f;
            yield return Prologue();
            if (!_bot.Ok) yield break;
            _log.Note($"Mốc thời gian: prologue xong lúc {Time.realtimeSinceStartup - began:0} giây thật từ khi bắt đầu.");
            yield return _bot.Wait("nha_rong_loaded", () => Scene == "01_NhaRong" && PlayerRig.Current != null && !GameRoot.Instance.Scenes.IsLoading);
            if (!_bot.Ok) yield break;
            _bot.BindScene(); Time.timeScale = 1f;
            yield return _log.Checkpoint("nha_rong_spawn");
            yield return NhaRong();
            if (!_bot.Ok) yield break;
            _log.Note($"Mốc thời gian: 01 xong lúc {Time.realtimeSinceStartup - began:0} giây thật từ khi bắt đầu.");
            if (stage == Stage.PrologueAndQuay) yield break;

            yield return DaysLoop(1, oneOnly: false);
            if (!_bot.Ok) yield break;
            _log.Note($"Mốc thời gian: 6 ngày xong lúc {Time.realtimeSinceStartup - began:0} giây thật từ khi bắt đầu.");
            yield return _bot.Wait("marseille_loaded", () => Scene == "03_Marseille" && PlayerRig.Current != null && !GameRoot.Instance.Scenes.IsLoading);
            if (!_bot.Ok) yield break;
            _bot.BindScene(); Time.timeScale = 1f;
            yield return _log.Checkpoint("marseille_spawn");
            yield return Marseille();
            if (_bot.Ok)
            {
                _completed.Add("Marseille tới end card");
                _coverage = string.Join("; ", _completed) + ". Không dùng nhảy chương.";
                _log.Note($"TỔNG THỜI GIAN TỚI END: {Time.realtimeSinceStartup - began:0} giây thật ({(Time.realtimeSinceStartup - began) / 60f:0.0} phút); game-time {Time.time:0}.");
            }
        }

        IEnumerator DaysLoop(int firstDay, bool oneOnly)
        {
            foreach (var day in GameRoot.Instance.Content.Days.Days.Where(x => x.Number >= firstDay))
            {
                int number = day.Number;
                yield return _bot.Wait("day_" + number + "_loaded", () => SceneDirector.Current is ShiftDirector s && s.Day != null && s.Day.Number == number && s.InShift && !GameRoot.Instance.Scenes.IsLoading);
                if (!_bot.Ok) yield break;
                _bot.BindScene(); Time.timeScale = 1f;
                float dayStart = Time.realtimeSinceStartup;
                float dayGameStart = Time.time;
                yield return _log.Checkpoint("day_" + number + "_start");
                yield return Galley((ShiftDirector)SceneDirector.Current);
                if (!_bot.Ok) yield break;
                _completed.Add("Ngày " + number + " gồm mọi lượt và buổi tối");
                _coverage = string.Join("; ", _completed);
                _log.Note($"Mốc thời gian: ngày {number} xong sau {Time.realtimeSinceStartup - dayStart:0} giây thật; {Time.time - dayGameStart:0.0} giây game, timeScale={Time.timeScale:0.00}.");
                if (oneOnly) yield break;
            }
        }

        /// <summary>Màn disclaimer sau "Chơi mới": đọc vài giây (chụp ảnh) rồi bấm một phím như người chơi.</summary>
        IEnumerator Disclaimer()
        {
            yield return _bot.Wait("disclaimer_card", () => GameRoot.Instance.UI.Card.Visible || Scene == "00_Prologue", 30f, 60f);
            if (!_bot.Ok || Scene == "00_Prologue") yield break;
            for (int i = 0; i < 90; i++) yield return _bot.Frame();
            yield return _log.Shot("disclaimer");
            yield return PressKey(Key.Space);
        }

        /// <summary>
        /// Dữ liệu gắn vào từng khung video (BOT_VIDEO), cách nhau bằng tab: cảnh+beat, ngày+giờ ca, vị trí Ba, đồ đang cầm,
        /// câu thoại đang phát (id · người nói: chữ), prompt, dòng hướng dẫn. Tools/bot_video.py biến thành phụ đề và transcript.
        /// </summary>
        string VideoMeta()
        {
            static string Clean(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
            var root = GameRoot.Instance;
            var rig = PlayerRig.Current;
            var shift = SceneDirector.Current as ShiftDirector;
            string line = "";
            var id = root != null && root.Subtitles != null ? root.Subtitles.CurrentLineId : null;
            if (!string.IsNullOrEmpty(id))
            {
                var l = root.Content.GetLine(id);
                string who = l != null && root.Content.Subtitles.Speakers.TryGetValue(l.Speaker, out var s) ? s : l?.Speaker;
                line = id + " · " + who + ": " + l?.Vi;
            }
            var ui = root?.UI?.Root;
            var guide = ui?.Q<Label>(className: "hbt-guide-text");
            string guideText = guide != null && guide.resolvedStyle.display != DisplayStyle.None ? guide.text : "";
            var who2 = rig != null ? rig.interactor : null;
            string pos = rig != null ? $"({rig.Position.x:0.0}, {rig.Position.y:0.0}, {rig.Position.z:0.0})" : "";
            return string.Join("\t", Clean(Scene + " " + Beat),
                Clean((shift?.Day != null ? "ngày " + shift.Day.Number : "") + (shift?.Clock != null ? " " + shift.Clock.Text : "")),
                pos, Clean(who2?.Holding != null ? who2.Holding.name : ""), Clean(line),
                Clean(_log != null ? _log.VisiblePrompt : ""), Clean(guideText));
        }

        IEnumerator PressKey(Key key)
        {
            // Batchmode không có cửa sổ game được focus: cho thiết bị phím nhận sự kiện như khi người chơi bấm phím thật.
            var settings = InputSystem.settings;
            if (!_inputSettingsSaved)
            {
                _inputSettingsSaved = true;
                _oldEditorInput = settings.editorInputBehaviorInPlayMode; _oldBackground = settings.backgroundBehavior;
            }
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
            if (!keyboard.enabled) InputSystem.EnableDevice(keyboard);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return _bot.Frame();
            _log.Note($"Phím {key}: bàn phím #{keyboard.deviceId} enabled={keyboard.enabled} đang giữ={keyboard[key].isPressed}; chế độ nền={InputSystem.settings.backgroundBehavior}; gameplay={_bot.Input.GameplayEnabled}.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return _bot.Frame();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_bot != null) _bot.ReleaseInput();
            if (BotVideo.Current != null) yield return BotVideo.Current.Finish();
            if (_log != null)
            {
                yield return _log.Checkpoint("run_stop");
                _log.Finish(_coverage + " Dừng tại " + Scene + "/" + Beat + ".");
                _log.Dispose(); _log = null;
            }
            ScreenshotCapture.RestoreUi();
            if (_inputSettingsSaved)
            {
                InputSystem.settings.editorInputBehaviorInPlayMode = _oldEditorInput;
                InputSystem.settings.backgroundBehavior = _oldBackground;
                _inputSettingsSaved = false;
            }
            SaveSystem.Folder = _oldSaveFolder; GameSettings.Folder = _oldSettingsFolder;
            Time.timeScale = _oldScale;
            Time.captureDeltaTime = 0f;
            QualitySettings.vSyncCount = _oldVSync; Application.targetFrameRate = _oldFrameRate;
            LogAssert.ignoreFailingMessages = _oldIgnore;
        }

        /// <summary>Prologue Sài Gòn 1911: đi bộ dọc vỉa hè tới chỗ anh Lê, nghe hỏi đáp, giữ Space giơ hai bàn tay.</summary>
        IEnumerator Prologue()
        {
            yield return _bot.Wait("prologue_control", () => Beat == "P1" && !GameRoot.Instance.UI.Card.Visible && GameRoot.Instance.Input.GameplayEnabled);
            if (!_bot.Ok) yield break;
            yield return _log.Checkpoint("prologue_street");
            // Anh Lê dừng ở chỗ cái đèn (điểm nhìn đầu tiên, c39f347) chứ không đi hết phố: tới đứng sau anh ấy 1,5 m.
            var prologue = (PrologueDirector)SceneDirector.Current;
            yield return _bot.Walk("prologue_to_le", prologue.discoveryLePoints[0].position + Vector3.left * 1.5f, 0.5f);
            if (!_bot.Ok) yield break;
            yield return _bot.Wait("le_starts_street_discoveries", () => Beat == "P1a");
            if (!_bot.Ok) yield break;
            var stands = new[] { V(-13f, -7.5f, 0.12f), V(-9f, -7.7f, 0.12f), V(-6f, -7.5f, 0.12f) };
            for (int i = 0; i < prologue.discoveries.Length; i++)
            {
                int seen = i;
                yield return _bot.Walk("saigon_discovery_" + i, stands[i]);
                if (!_bot.Ok) yield break;
                yield return _bot.Wait("saigon_discovery_ready_" + i, () => prologue.discoveries[seen].available);
                if (!_bot.Ok) yield break;
                yield return _bot.Press(prologue.discoveries[i], "saigon_look_" + i, () => prologue.DiscoveriesSeen > seen, "prompt.story.look");
                if (!_bot.Ok) yield break;
            }
            yield return _bot.Walk("saigon_ice_counter", V(6f, -7.5f, 0.12f));
            if (!_bot.Ok) yield break;
            yield return _bot.Wait("ice_ready", () => prologue.iceTarget.available);
            if (!_bot.Ok) yield break;
            yield return _bot.Press(prologue.iceTarget, "take_ice", () => prologue.iceCup.gameObject.activeInHierarchy, "prompt.story.ice");
            if (!_bot.Ok) yield break;
            yield return _log.Shot("ice_in_hand");
            _bot.Input.SimUse = true;
            yield return _bot.Wait("ice_eaten", () => prologue.IceEaten >= PrologueDirector.IceEatSeconds, 15f, 25f);
            _bot.Input.SimUse = false;
            if (!_bot.Ok) yield break;
            yield return _bot.Wait("prologue_raise_prompt", () => Beat == "P3" && _log.VisiblePrompt.Contains("Space"));
            if (!_bot.Ok) yield break;
            yield return _log.Checkpoint("raise_hands_prologue");
            _bot.Input.SimRaise = true;
            yield return _bot.Wait("hands_raised_prologue", () => Beat == "P4");
            _bot.Input.SimRaise = false;
            if (!_bot.Ok) yield break;
            yield return _bot.Wait("other_path_notebook", () => GameRoot.Instance.UI.Notebook.IsOpen);
            if (!_bot.Ok) yield break;
            yield return _log.Shot("other_path_notebook");
            yield return PressKey(Key.Tab);
        }

        /// <summary>
        /// 01 Nhà Rồng vào từ prologue: mở sổ bằng Tab theo prompt, vác 2 bao khoai từ xe kéo lên cầu thang tàu, qua boong, xuống hầm
        /// thực phẩm và xếp (E); tàu nhổ neo; đi bộ về cửa bếp để sang cảnh 02.
        /// </summary>
        IEnumerator NhaRong()
        {
            var job = (NhaRongDirector)SceneDirector.Current;
            yield return _bot.Wait("job_control", () => Beat == "B0" && _bot.Input.GameplayEnabled && !GameRoot.Instance.UI.Card.Visible);
            if (!_bot.Ok) yield break;
            foreach (var point in new[] { V(0.9f, -15.7f, -4.1f), V(-1.5f, -14.2f, -4.1f), V(-1.5f, -13f, -4.1f), V(-1.5f, -7.38f), V(-1.5f, -5.3f) })
            { yield return _bot.Walk("job_boarding", point); if (!_bot.Ok) yield break; }
            yield return _bot.Press(job.jobStaffTarget, "ask_staff_for_work", () => Beat != "B0", "prompt.job.ask", 60f);
            if (!_bot.Ok) yield break;
            yield return _bot.Wait("follow_mai", () => Beat == "B0c");
            if (!_bot.Ok) yield break;
            foreach (var point in new[] { V(-1.5f, -6.1f), V(-4.1f, -6.1f), V(-5.5f, -6.1f) })
            { yield return _bot.Walk("job_follow_mai", point); if (!_bot.Ok) yield break; }
            yield return _bot.Wait("mai_at_captain", () => job.MaiAtCaptain);
            if (!_bot.Ok) yield break;
            yield return _bot.Press(job.jobCaptainTarget, "captain_interview", () => job.JobAccepted, "prompt.job.captain", 90f);
            if (!_bot.Ok) yield break;
            Assert.IsTrue(GameRoot.Instance.Notebook.IsUnlocked("p_job"));
            yield return _bot.Wait("quay_control", () => Beat == "B1.3" && _bot.Input.GameplayEnabled, 60f, 90f);
            if (!_bot.Ok) yield break;
            var d = (NhaRongDirector)SceneDirector.Current;
            var layout = UnityEngine.Object.FindAnyObjectByType<HaiBanTay.Ship.ShipLayout>();
            var holdStair = layout.stairs.GetComponentsInChildren<HaiBanTay.Ship.ShipStair>().Single(st => st.BottomWorld.y < -2.7f && st.TopWorld.y > -0.5f);
            yield return _log.Shot("quay_free");
            // Prompt "[Tab] Sổ quan sát": bấm Tab thật, đọc một lúc, đóng lại cũng bằng Tab.
            var nb = GameRoot.Instance.UI.Notebook;
            yield return _bot.Wait("quay_notebook_prompt", () => _log.VisiblePrompt.Contains("Tab") || nb.IsOpen, 12f, 20f, "MAJOR");
            if (_bot.Ok && !nb.IsOpen)
            {
                yield return PressKey(Key.Tab);
                yield return _bot.Wait("quay_notebook_open", () => nb.IsOpen, 5f, 8f);
                if (_bot.Ok)
                {
                    for (int i = 0; i < 90; i++) yield return _bot.Frame();
                    yield return _log.Shot("quay_notebook_open");
                    yield return PressKey(Key.Tab);
                    yield return _bot.Wait("quay_notebook_closed", () => !nb.IsOpen, 5f, 8f);
                }
            }
            _bot.Recover();
            for (int i = 0; i < d.sackTarget; i++)
            {
                var sack = d.sacks.FirstOrDefault(x => x != null && x.gameObject.activeInHierarchy && !x.Locked && !x.IsHeld);
                if (sack == null) { _bot.Fail("Không còn bao khoai nào để nhấc trên kè."); yield break; }
                yield return CarrySackToHold(d, sack, holdStair, i);
                if (!_bot.Ok) yield break;
                if (i + 1 < d.sackTarget)
                {
                    // Quay lại kè lấy bao tiếp: ra khỏi hầm, lên thang, qua boong rồi xuống cầu thang tàu.
                    foreach (var p in new[]
                    {
                        holdStair.LaneBottomWorld, holdStair.LaneTopWorld + Vector3.left * 0.5f, new Vector3(-9.514f, 0f, 0f),
                        new Vector3(-9.514f, 0f, -2.2f), new Vector3(-9.4f, 0f, -2.9f), new Vector3(-1.5f, 0f, -3.0f),
                        new Vector3(-1.5f, 0f, -5f), new Vector3(-1.5f, 0f, -7.9f),
                        new Vector3(-1.5f, -4.1f, -13.0f), new Vector3(0.9f, -4.1f, -15.7f),
                    })
                    {
                        yield return _bot.Walk("quay_back_for_sack_" + i, p);
                        if (!_bot.Ok) yield break;
                    }
                }
            }
            yield return _bot.Wait("departure", () => d.Departed, 20f, 30f);
            if (!_bot.Ok) yield break;
            yield return _log.Shot("departed");
            // Về cửa bếp (cửa bản lề thật mở khi tới gần) từ buồng thang hầm: ra boong, vòng tới mặt trước thượng tầng.
            foreach (var p in new[]
            {
                holdStair.LaneBottomWorld, holdStair.LaneTopWorld + Vector3.left * 0.5f, new Vector3(-9.514f, 0f, 0f),
                new Vector3(-9.514f, 0f, -2.2f), V(-9.2f, -3.45f), V(-11.4f, -3.45f)
            })
            {
                yield return _bot.Walk("deck_to_galley", p, 0.3f);
                if (!_bot.Ok) yield break;
            }
            Time.timeScale = 1f;
            yield return _bot.Wait("galley_entered", () => Beat == "B1.5" || Scene == "02_Galley", 90f, 120f);
            if (_bot.Ok) { _completed.Add("Menu, prologue, Nhà Rồng (vác 2 bao, nhổ neo, vào bếp)"); _coverage = string.Join("; ", _completed); }
        }

        /// <summary>Nhấc một bao ở xe kéo (E), vác theo lối bắt buộc (cầu thang tàu, boong, thang hầm) và xếp xuống hầm (E).</summary>
        IEnumerator CarrySackToHold(NhaRongDirector d, Holdable sack, HaiBanTay.Ship.ShipStair holdStair, int index)
        {
            var sp = sack.transform.position;
            yield return _bot.Walk("sack_cart_" + index, new Vector3(sp.x, _bot.Rig.Position.y, sp.z + 1.3f), 0.35f);
            if (!_bot.Ok) yield break;
            // Bao trên kè có thể lăn chồng nhau; người chơi nhấc bao nào đang nhìn trúng cũng được, bot làm như vậy.
            yield return _bot.Focus(sack, "aim_quay_sack_" + index, "prompt.pick_two_hands");
            if (!_bot.Ok && _bot.Rig.interactor.Current is Holdable seen && seen != sack && d.sacks.Contains(seen) && !seen.Locked && !seen.IsHeld)
            {
                _log.Note($"Bao {sack.name} bị {seen.name} che; nhấc {seen.name}.");
                sack = seen;
            }
            yield return _bot.Press(sack, "lift_quay_sack_" + index, () => _bot.Rig.interactor.Holding == sack, "prompt.pick_two_hands");
            if (!_bot.Ok) yield break;
            yield return _log.Shot("sack_lifted_" + index);
            var route = new[]
            {
                new Vector3(0.9f, -4.1f, -15.7f), new Vector3(-1.5f, -4.1f, -14.2f), new Vector3(-1.5f, -4.1f, -13.0f),
                new Vector3(-1.5f, 0f, -7.38f), new Vector3(-1.5f, 0f, -5f), new Vector3(-1.5f, 0f, -3.0f), new Vector3(-9.4f, 0f, -2.9f),
                new Vector3(-9.514f, 0f, -2.2f), new Vector3(-9.514f, 0f, 0f),
                holdStair.LaneTopWorld + Vector3.left * 0.4f, holdStair.LaneBottomWorld, new Vector3(-2.9f, -2.8f, 1.1f),
            };
            for (int i = 0; i < route.Length; i++)
            {
                yield return _bot.Walk("carry_sack_" + index + "_" + i, route[i], 0.3f);
                if (_bot.Ok && _bot.Rig.interactor.Holding != sack) { _bot.Fail($"Bao khoai tuột khỏi tay ở chặng {i}."); yield return _log.Checkpoint("sack_slipped"); }
                if (!_bot.Ok) yield break;
            }
            int before = d.stack.Count;
            yield return _bot.Press(d.stack, "stack_sack_" + index, () => d.stack.Count > before, "prompt.drop");
            if (_bot.Ok) yield return _log.Shot("sack_stacked_" + index);
        }

        IEnumerator To(Component target, Vector3 stand, string step)
        {
            yield return _bot.Travel(step, stand);
            // Reach and a live prompt decide whether interaction is possible, rather than centimetre-perfect waypoints.
            yield return _bot.Focus(target, step);
            if (!_bot.Ok) yield return _bot.Approach(target, step, stand);
        }

        IEnumerator Galley(ShiftDirector shift)
        {
            int lastPhase = -1;
            var failed = new Dictionary<string, int>();
            float began = Time.realtimeSinceStartup;
            while (shift != null && shift.InShift && Time.realtimeSinceStartup - began < 2400f)
            {
                if (lastPhase != shift.PhaseIndex)
                {
                    if (lastPhase >= 0) _completed.Add($"D{shift.Day.Number} lượt {lastPhase + 1}");
                    lastPhase = shift.PhaseIndex; failed.Clear();
                    _coverage = string.Join("; ", _completed);
                }
                yield return _log.Checkpoint("read_order_board_D" + shift.Day.Number + "_" + shift.PhaseIndex);
                var open = shift.Board.Orders.Where(o => !o.IsDone).ToList();
                if (open.Count == 0)
                {
                    int phase = shift.PhaseIndex;
                    yield return _bot.Wait("next_phase_D" + shift.Day.Number + "_" + phase, () => !shift.InShift || shift.PhaseIndex != phase || shift.Board.Orders.Any(o => !o.IsDone));
                    if (!_bot.Ok) yield break;
                    continue;
                }
                // Clear ash first, then keep the fire supplied before food preparation and dishes.
                var priority = new[] { OrderKinds.Ash, OrderKinds.Fire, OrderKinds.Sweep, OrderKinds.CoalFetch, OrderKinds.Water, OrderKinds.Meat, OrderKinds.Fish, OrderKinds.Ice, OrderKinds.Serve, OrderKinds.Peel, OrderKinds.Asparagus, OrderKinds.Soup, OrderKinds.Chieu, OrderKinds.Sack, OrderKinds.Greens, OrderKinds.Secure, OrderKinds.TallOut, OrderKinds.ScrubTall, OrderKinds.Wash, OrderKinds.Pan, OrderKinds.Haul };
                string kind = priority.FirstOrDefault(k => open.Any(o => o.Kind == k) && (!failed.TryGetValue(k, out int count) || count < 3));
                if (kind == null)
                {
                    _log.Bug("shift_softlock_D" + shift.Day.Number + "_" + shift.PhaseIndex, "BLOCKER", "Hoàn thành mọi lệnh trên bảng để sang lượt sau.",
                        "Các lệnh còn lại đã được thử nhưng không hoàn tất: " + string.Join(", ", open.Select(o => o.Kind + " " + o.Done + "/" + o.Need)));
                    yield return _log.Checkpoint("shift_cannot_continue");
                    _bot.Fail("Unresolved shift orders; no later scene or task-state shortcut used.");
                    yield break;
                }
                switch (kind)
                {
                    case OrderKinds.CoalFetch: yield return CoalRefill(shift.coal); break;
                    case OrderKinds.Water:
                    case OrderKinds.Meat:
                    case OrderKinds.Fish:
                    case OrderKinds.Ice:
                    case OrderKinds.Serve: yield return StoryDelivery(shift.storyStations.OfType<VoyageDelivery>().First(s => s.Kind == kind)); break;
                    case OrderKinds.Peel:
                    case OrderKinds.Asparagus:
                        var peel = shift.storyStations.OfType<PeelStation>().First(s => s.Kind == kind);
                        yield return _bot.FreeHands();
                        if (_bot.Ok) yield return To(peel.board, peel.stand, "peel_board");
                        if (_bot.Ok) yield return _bot.Peel(peel);
                        break;
                    case OrderKinds.Soup:
                    case OrderKinds.Chieu: yield return StoryEncounter(shift.storyStations.OfType<VoyageEncounter>().First(s => s.Kind == kind)); break;
                    case OrderKinds.Fire: yield return Fire(shift.coal); break;
                    case OrderKinds.Ash: yield return Ash(shift.ash); break;
                    case OrderKinds.Sweep: yield return Sweep(shift.sweep); break;
                    case OrderKinds.Sack: yield return Sack(shift.sacks); break;
                    case OrderKinds.Greens: yield return Greens(shift.greens); break;
                    case OrderKinds.Wash: yield return Wash(shift.wash); break;
                    case OrderKinds.Pan: yield return Pan(shift.pan); break;
                    case OrderKinds.Secure: yield return Secure(shift.pots); break;
                    case OrderKinds.TallOut: yield return TallOut(shift.tall); break;
                    case OrderKinds.ScrubTall: yield return TallPot(shift.tall); break;
                    case OrderKinds.Haul: yield return Haul(shift.haul); break;
                }
                if (!_bot.Ok) failed[kind] = failed.TryGetValue(kind, out int count) ? count + 1 : 1;
                else failed.Remove(kind);
                if (PlayerRig.Current == null || !float.IsFinite(PlayerRig.Current.Position.y) || PlayerRig.Current.Position.y < -12f) yield break;
            }
            if (shift != null && shift.InShift)
            {
                _log.Bug("day_real_time_budget", "BLOCKER", "Hoàn tất ngày trong ngân sách 40 phút thật.", "Đã dùng hết 40 phút thật; vẫn trong ca.");
                _bot.Fail("Day reached real-time budget.");
                yield break;
            }
            yield return _bot.Wait("evening_D" + shift.Day.Number, () => Beat.EndsWith(".evening") || Beat.EndsWith(".rest"));
            if (!_bot.Ok) yield break;
            yield return _log.Checkpoint("evening_D" + shift.Day.Number);
            if (shift.Day.Evening != null && shift.Day.Evening.Coffee)
            {
                yield return _bot.Wait("coffee_order", () => shift.Board.HasOpen(OrderKinds.Coffee), 30f, 45f);
                if (!_bot.Ok) yield break;
                yield return StoryDelivery(shift.storyStations.OfType<VoyageDelivery>().First(s => s.Kind == OrderKinds.Coffee));
                if (!_bot.Ok) yield break;
            }
            if (shift.Day.Evening != null && shift.Day.Evening.HasStudy)
            {
                yield return Study(shift);
                if (!_bot.Ok) yield break;
            }
            yield return _bot.Wait("rest_prompt_D" + shift.Day.Number, () => shift.restSeat.available && _bot.Input.GameplayEnabled);
            if (!_bot.Ok) yield break;
            yield return To(shift.restSeat, V(-3.0f, 1.15f), "evening_rest");
            if (!_bot.Ok) yield break;
            int day = shift.Day.Number;
            yield return _bot.Press(shift.restSeat, "evening_rest_D" + day, () => shift.Finished || Scene != "02_Galley", "prompt.rest");
        }

        IEnumerator Tool(ToolHook hook, Vector3 stand)
        {
            if (_bot.Rig.interactor.Tool == hook.tool)
            { yield return _bot.Wait("held_tool_ready", () => _bot.Rig.interactor.Tool == hook.tool); yield break; }
            yield return _bot.FreeHands();
            if (!_bot.Ok) yield break;
            yield return To(hook, stand, "take_" + hook.name);
            if (!_bot.Ok) yield break;
            yield return _bot.Press(hook, "take_" + hook.name, () => _bot.Rig.interactor.Tool == hook.tool, hook.takeKey);
        }

        IEnumerator CoalRefill(CoalStation coal)
        {
                yield return _bot.FreeHands();
                if (!_bot.Ok) yield break;
                yield return _bot.Wait("available_coal_basket", () => coal.baskets.Any(b => b.gameObject.activeInHierarchy && !b.Locked && !b.IsHeld && !coal.IsEmptyBasket(b)), 60f, 90f);
                if (!_bot.Ok) yield break;
                Holdable basket = null;
                // The right basket has a clear approach. Try another visible basket if one is obstructed.
                foreach (var candidate in coal.baskets.Where(b => b.gameObject.activeInHierarchy && !b.Locked && !b.IsHeld && !coal.IsEmptyBasket(b)).OrderByDescending(b => b.transform.position.x))
                {
                    yield return To(candidate, V(candidate.transform.position.x > 1.5f ? 2.2f : 1.1f, -3.55f), "coal_basket_" + candidate.name);
                    if (_bot.Ok) { basket = candidate; break; }
                    _log.Bug("occluded_" + candidate.name, "MAJOR", "Có thể nhắm và nhấc sọt than nhìn thấy.", "Không thấy prompt sọt; tia trúng " + _bot.Rig.interactor.LookCollider?.name + ".");
                    yield return _log.Checkpoint("occluded_" + candidate.name);
                }
                if (basket == null) { _bot.Fail("No accessible coal basket; all available baskets were tried."); yield break; }
                yield return _bot.Press(basket, "coal_basket", () => _bot.Rig.interactor.Holding == basket);
                if (!_bot.Ok) yield break;
                yield return To(coal.bin, V(0.65f, 0.95f), "refill_coal_bin");
                if (!_bot.Ok) yield break;
                int refills = coal.Refills;
                yield return _bot.Press(coal.bin, "refill_coal_bin", () => coal.Refills > refills && !coal.PouringBasket, "prompt.refill_bin");
                if (!_bot.Ok) yield break;
                // Sọt rỗng mang về chỗ cũ trong hầm than.
                yield return To(coal.basketReturn, V(1.1f, -3.55f), "return_coal_basket");
                if (!_bot.Ok) yield break;
                yield return _bot.Press(coal.basketReturn, "return_coal_basket", () => _bot.Rig.interactor.Holding == null, "prompt.return_basket");
                if (!_bot.Ok) yield break;
        }

        IEnumerator Fire(CoalStation coal)
        {
            if (coal.Bin.Empty)
            { yield return CoalRefill(coal); if (!_bot.Ok) yield break; }
            yield return Tool(coal.hook, V(1.1f, 1.75f));
            if (!_bot.Ok) yield break;
            yield return To(coal.bin, V(0.65f, 0.95f), "shovel_bin");
            if (!_bot.Ok) yield break;
            for (int attempt = 0; ; attempt++)
            {
                // Thùng cạn giữa chừng không phải lỗi giữ chuột: phụ bếp cũng đi xúc ở thùng này và lấy nốt miếng than cuối (bot ngày 5 20/10:
                // giữ chuột 25 s trên thùng trống). Đi lấy sọt than rồi quay lại xúc.
                yield return _bot.Hold(coal.bin, "load_shovel", () => coal.Shovel.Loaded || coal.Bin.Empty, () => coal.Shovel.Fill, () => Vector2.zero, "prompt.shovel", false);
                if (!_bot.Ok) yield break;
                if (coal.Shovel.Loaded) break;
                _bot.Input.SimUse = false;
                if (attempt > 0) { _bot.Fail("Thùng than cạn hai lần liền khi xúc."); yield break; }
                yield return CoalRefill(coal);
                if (!_bot.Ok) yield break;
                yield return Tool(coal.hook, V(1.1f, 1.75f));
                if (!_bot.Ok) yield break;
                yield return To(coal.bin, V(0.65f, 0.95f), "shovel_bin_again");
                if (!_bot.Ok) yield break;
            }
            yield return To(coal.stove, V(-3.0f, 0.55f), "stove_with_loaded_shovel");
            if (!_bot.Ok) { _bot.Input.SimUse = false; yield break; }
            // Lệnh than chia cho các ô bếp: đổ vào miệng lò hướng dẫn đang chỉ, như người chơi (ô đã đủ phần báo "sang lò bên kia").
            TaskTarget Mouth() => coal.Guide(_bot.Rig.interactor, out var guided, out _) && guided != null && guided.GetComponent<TaskTarget>() is TaskTarget m
                ? m : coal.stove;
            var mouth = Mouth();
            int poured = coal.Shovel.Poured;
            yield return _bot.Focus(mouth, "pour_coal");
            string seen = _bot.Ok ? _bot.Rig.interactor.Current?.GetPromptKey(_bot.Rig.interactor) : null;
            // Lệnh than vừa xong giữa chừng (phụ bếp đổ nốt): CoalStation.OnCompleted trả nhát than về thùng, xẻng rỗng, hướng dẫn
            // chỉ "lửa đang ấm" ở lò không có prompt. Bot 01/11 (QA-0153) vẫn đi nhắm miệng lò chờ prompt.pour → BLOCKER giả.
            if (!coal.Shovel.Loaded || coal.State != TaskState.Active)
            {
                _bot.Input.SimUse = false;
                _log.Note($"Lệnh than xong trước khi đổ (xẻng {(coal.Shovel.Loaded ? "còn than" : "đã trả than")}, {coal.State}); bỏ nhát này.");
                yield break;
            }
            // Phụ bếp cũng đổ than: ô hướng dẫn chỉ lúc nãy có thể vừa đầy (prompt.stove_full), kể cả trong lúc bot đi tới ô hướng dẫn mới
            // chỉ (bot lượt 4 01/11: thử lại một lần, tới nơi ô 3 đã đầy → MAJOR giả). Hỏi lại hướng dẫn như người chơi, tối đa 3 lần; không
            // còn ô nào nhận thì hướng dẫn chỉ về thùng (trả than). Lần cuối mới so prompt (ghi lỗi nếu vẫn sai).
            string want = "prompt.pour";
            for (int retry = 0; retry < 3 && (!_bot.Ok || seen != want); retry++)
            {
                if (_bot.Ok) _log.Note($"Ô {mouth.name} vừa đổi: {seen}; hỏi lại hướng dẫn (lần {retry + 1}).");
                mouth = Mouth();
                want = mouth == coal.bin ? "prompt.return_coal" : "prompt.pour";
                yield return _bot.Approach(mouth, "pour_coal_retry");
                if (!_bot.Ok) break;
                yield return _bot.Focus(mouth, "pour_coal_retry", retry == 2 ? want : null);
                seen = _bot.Ok ? _bot.Rig.interactor.Current?.GetPromptKey(_bot.Rig.interactor) : null;
                if (!coal.Shovel.Loaded || coal.State != TaskState.Active)
                {
                    _bot.Input.SimUse = false;
                    _log.Note($"Lệnh than xong trong lúc tìm ô ({coal.State}); bỏ nhát này.");
                    yield break;
                }
            }
            if (!_bot.Ok) { _bot.Input.SimUse = false; yield break; }
            _bot.Input.SimUse = false;
            yield return _bot.Wait("coal_poured", () => coal.Shovel.Poured > poured || !coal.Shovel.Loaded, 15f, 30f);
            yield return _log.Checkpoint("coal_poured");
        }

        IEnumerator StoryDelivery(VoyageDelivery task)
        {
            yield return _bot.FreeHands(); if (!_bot.Ok) yield break;
            if (task.fromHold) yield return ToFoodHold(task.Kind, task.pickupStand);
            else yield return _bot.Travel(task.Kind + "_pickup", task.pickupStand);
            if (!_bot.Ok) yield break;
            yield return _bot.TakeStoryLoad(task); if (!_bot.Ok) yield break;
            if (task.fromHold) yield return FromFoodHold(task.Kind, task.load);
            else if (task.Kind == OrderKinds.Coffee)
            {
                yield return _bot.Travel("coffee_to_soldiers", GalleyAt(-3.3f, 1.6f)); if (!_bot.Ok) yield break;
                foreach (var point in new[] { GalleyAt(-4.3f, 2.25f), GalleyAt(-4.75f, 2.6f), GalleyAt(-5.6f, 2.6f), task.deliveryStand })
                { yield return _bot.Walk("coffee_to_soldiers", point, 0.3f); if (!_bot.Ok) yield break; }
            }
            if (!_bot.Ok) yield break;
            if (task.Kind != OrderKinds.Coffee) yield return _bot.Travel(task.Kind + "_destination", task.deliveryStand);
            if (!_bot.Ok) yield break;
            yield return _bot.PlaceStoryLoad(task);
        }

        IEnumerator StoryEncounter(VoyageEncounter task)
        {
            yield return _bot.FreeHands(); if (!_bot.Ok) yield break;
            if (task.Kind == OrderKinds.Chieu)
            {
                yield return _bot.Travel("first_class_deck", GalleyAt(-3.3f, 1.6f)); if (!_bot.Ok) yield break;
                // Lên thang dạo boong từ phía chân (x −9,21): đứng cạnh sườn thang thì tay vịn ẩn chắn lối.
                foreach (var point in new[] { GalleyAt(-4.3f, 2.25f), GalleyAt(-5.6f, 2.6f), V(-8.2f, 2.4f), V(-9.9f, 3.7f), V(-9.95f, 4.8f), V(-5.164f, 4.8f, 2.85f), task.stand })
                { yield return _bot.Walk("first_class_deck", point, 0.3f); if (!_bot.Ok) yield break; }
            }
            else
            {
                yield return _bot.Travel("crew_dining_door", V(-0.9f, -2.4f)); if (!_bot.Ok) yield break;
                yield return _bot.Walk("crew_dining_entry", V(-0.9f, -3.6f), 0.15f);
                if (_bot.Ok) yield return _bot.Walk("crew_dining_seat", task.stand, 0.15f);
            }
            if (!_bot.Ok) yield break;
            yield return _bot.Encounter(task);
        }

        IEnumerator Ash(AshStation ash)
        {
            var who = _bot.Rig.interactor;
            if (who.Holding != ash.bucket)
            { yield return _bot.FreeHands(); if (!_bot.Ok) yield break; }
            // Xô cất trong hầm than cạnh máng tro: lấy ra, đặt giữa trước cửa tro. Xô rỗng nằm trước ô gần hết tro thì dời sang ô nhiều tro.
            if (!ash.BucketReady || ash.AwaitingStore || (ash.Rake.InBucket == 0 && AshCell(ash) != ash.BucketCell))
            {
                if (who.Holding != ash.bucket)
                {
                    if (ash.BucketStored) yield return To(ash.bucket, V(0.95f, -4.1f), "ash_bucket_from_store");
                    else yield return _bot.Approach(ash.bucket, "ash_bucket_stray");
                    if (!_bot.Ok) yield break;
                    yield return _bot.Press(ash.bucket, "pick_ash_bucket", () => who.Holding == ash.bucket);
                    if (!_bot.Ok) yield break;
                }
                if (ash.AwaitingStore)
                { yield return StoreBucket(ash, "store_ash_bucket_leftover"); yield break; }
                if (ash.Rake.InBucket == 0)
                {
                    yield return PlaceBucket(ash, "put_ash_bucket", AshCell(ash));
                    if (!_bot.Ok) yield break;
                }
            }
            // Lò có nhiều ô (mỗi bếp một cửa tro): cào ô đang đặt xô tới khi xô đầy hoặc ô hết tro (xô vơi thì đem đổ, lượt sau đặt ô khác).
            int cell = Mathf.Max(0, ash.BucketCell);
            if (!ash.Rake.BucketFull && ash.coal.CellState(cell).Ash > 0.001f)
            {
                yield return Tool(ash.hook, PlayerBot.RakeHookStand);
                if (!_bot.Ok) yield break;
                var door = ash.coal.cells[cell].ashDoor;
                // Đứng đúng chỗ (sai 0,25 m của Travel là xa cửa thêm 17 cm): xa hơn thì tia tới tâm cửa tro sượt mép sau xô (collider cao 0,55 m),
                // trúng xô thay vì cửa (bot 23/10 D3, ô 4).
                var rakeStand = AshStand(ash, cell, 0.52f);
                yield return _bot.Travel("rake_ash_door", rakeStand);
                yield return _bot.Walk("rake_ash_door_stand", rakeStand, 0.08f);
                yield return To(door, rakeStand, "rake_ash_door");
                if (!_bot.Ok) yield break;
                yield return _bot.Hold(door, "rake_downward", () => ash.Rake.BucketFull || ash.coal.CellState(cell).Ash <= 0.001f,
                    () => ash.Rake.Fill, () => new Vector2(0f, -15f), "prompt.rake");
                if (!_bot.Ok) yield break;
            }
            // Keep the rake in hand: the bucket's real E path must return the tool to its hook.
            if (who.Holding != ash.bucket)
            {
                yield return To(ash.bucket, AshStand(ash, cell, 0.62f) + Vector3.Cross(Vector3.up, AshStand(ash, cell, 1f) - AshStand(ash, cell, 0f)) * -0.4f, "pick_full_ash_bucket");
                if (!_bot.Ok) yield break;
                yield return _bot.Press(ash.bucket, "pick_full_ash_bucket", () => who.Holding == ash.bucket);
                if (!_bot.Ok) yield break;
            }
            yield return To(ash.chute, V(1.4f, -3.55f), "ash_chute");
            if (!_bot.Ok) yield break;
            int emptied = ash.Emptied;
            yield return _bot.Press(ash.chute, "empty_ash_bucket", () => ash.Emptied > emptied && !ash.bucket.Busy, "prompt.empty_ash");
            if (!_bot.Ok) yield break;
            yield return StoreBucket(ash, "store_ash_bucket");
        }

        IEnumerator StoreBucket(AshStation ash, string step)
        {
            yield return To(ash.store, V(0.95f, -4.1f), step);
            if (!_bot.Ok) yield break;
            yield return _bot.Press(ash.store, step, () => ash.BucketStored && !ash.AwaitingStore, "prompt.store_bucket");
        }

        /// <summary>
        /// Ô lò để đặt xô, như hướng dẫn của trò chơi (AshStation.Guide → ô nhiều tro nhất); ô xô đang nằm chỉ giữ khi còn đủ một nhát cào.
        /// Bot 17/10 chọn ô đầu tiên còn tro &gt; 0,001: ô 0 đang cháy luôn sinh lắt nhắt vài phần nghìn, xô "đầy" sau 4 nhát gần như rỗng,
        /// lặp hơn 100 lượt trong khi các ô khác còn 6 xô tro, ngày 3 hết 40 phút thật.
        /// </summary>
        static int AshCell(AshStation ash)
        {
            int at = ash.BucketCell;
            if (at >= 0 && ash.coal.CellState(at).Ash >= ash.Rake.AshPerStroke) return at;
            int best = Mathf.Max(0, at);
            for (int i = 0; i < ash.coal.cells.Length; i++)
                if (ash.coal.CellState(i).Ash > ash.coal.CellState(best).Ash) best = i;
            return best;
        }

        /// <summary>Chỗ đứng trước chỗ đặt xô của ô <paramref name="cell"/>, lùi <paramref name="back"/> m ra ngoài theo hướng cửa tro → xô.</summary>
        static Vector3 AshStand(AshStation ash, int cell, float back)
        {
            var spot = ash.coal.cells[cell].bucketSpot.transform.position;
            var outward = spot - ash.coal.cells[cell].ashDoor.transform.position;
            outward.y = 0f;
            return spot + outward.normalized * back;
        }

        /// <summary>Chỗ đứng trước cửa tro mà thân Ba (capsule 0,3 m) không chạm lò; ô sát góc lò thì lùi thêm (bot 23/10: kẹt CastRange ở ô tây).</summary>
        static Vector3 FreeAshStand(AshStation ash, int cell, float back)
        {
            for (float b = back; b <= back + 0.4f; b += 0.05f)
            {
                var p = AshStand(ash, cell, b);
                bool blocked = false;
                foreach (var c in Physics.OverlapCapsule(p + Vector3.up * 0.4f, p + Vector3.up * 1.45f, 0.3f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                    if (c.GetComponentInParent<PlayerRig>() == null && c.GetComponentInParent<Holdable>() == null && c.GetComponentInParent<NpcAgent>() == null)
                    { blocked = true; break; }
                if (!blocked) return p;
            }
            return AshStand(ash, cell, back);
        }

        IEnumerator PlaceBucket(AshStation ash, string step, int cell)
        {
            var spot = ash.coal.cells[cell].bucketSpot;
            var stand = FreeAshStand(ash, cell, 0.62f);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0)
                {
                    _log.Note("Placement did not persist; retry by picking up the visible bucket with E.");
                    if (_bot.Rig.interactor.Holding != ash.bucket)
                    {
                        yield return _bot.Approach(ash.bucket, step + "_recover_pick");
                        if (!_bot.Ok) yield break;
                        yield return _bot.Press(ash.bucket, step + "_recover_pick", () => _bot.Rig.interactor.Holding == ash.bucket);
                        if (!_bot.Ok) yield break;
                    }
                }
                yield return To(spot, stand, step);
                if (!_bot.Ok) yield break;
                yield return _bot.Walk(step + "_stand", stand, 0.08f);
                yield return _bot.Press(spot, step, () => ash.BucketReady, "prompt.put_bucket");
                if (!_bot.Ok) continue;
                // Confirm placement across later rendered/physics frames, rather than its first transient position.
                float stableSince = Time.time;
                yield return _bot.Wait(step + "_stays_put", () =>
                {
                    if (!ash.BucketReady) stableSince = Time.time;
                    return ash.BucketReady && Time.time - stableSince > 0.5f;
                }, 3f, 10f);
                if (_bot.Ok) yield break;
            }
        }

        IEnumerator Sweep(SweepStation sweep)
        {
            yield return Tool(sweep.hook, V(2.6f, -1.3f));
            if (!_bot.Ok) yield break;
            int index = Enumerable.Range(0, sweep.spots.Length).Where(i => sweep.Logic.IsDirty(i)).DefaultIfEmpty(-1).First();
            if (index < 0)
            { _bot.Fail("Order requests sweeping but no visible dirty spot remains."); yield break; }
            var spot = sweep.spots[index];
            var from = spot.transform.position + new Vector3(0f, 0f, spot.transform.position.z > 0 ? -0.75f : 0.75f);
            if (from.z > 0.4f)
            {
                // Đích ở phía bắc bàn sơ chế: vào từ đầu đông (nút route_3), không để đồ thị chung cắt qua mặt bàn từ phía nam.
                yield return _bot.Travel("sweep_north_lane", V(2.5f, 0.95f));
                if (!_bot.Ok) yield break;
            }
            yield return To(spot, from, "sweep_spot_" + index);
            if (!_bot.Ok) yield break;
            if (index == 2)
                yield return _log.Shot("sweep_spot_2_prompt");
            yield return _bot.Hold(spot, "sweep_spot_" + index, () => !sweep.Logic.IsDirty(index), () => sweep.Logic.Progress(index),
                () => new Vector2(Time.frameCount % 24 < 12 ? 15f : -15f, 5f), "prompt.sweep");
            if (_bot.Ok && index == 2)
                yield return _log.Shot("sweep_spot_2_cleaned");
        }

        /// <summary>
        /// Vác bao khoai (M10b): bếp → cửa bếp → boong giếng trước → buồng thang có mái → 14 bậc xuống hầm thực phẩm, nhấc bao (E),
        /// vác đúng lối đó về bếp (tàu lắc thì rê chuột giữ thăng bằng, FeedBalance), đặt lên bàn sơ chế, cởi dây, dốc khoai.
        /// </summary>
        IEnumerator Sack(SackStation sacks, int attempt = 0)
        {
            yield return _bot.FreeHands();
            if (!_bot.Ok) yield break;
            yield return _bot.Wait("available_sack", () => sacks.sacks.Any(s => s.gameObject.activeInHierarchy && !s.Locked && !s.IsHeld), 90f, 120f);
            if (!_bot.Ok) yield break;
            var sack = sacks.sacks.First(s => s.gameObject.activeInHierarchy && !s.Locked && !s.IsHeld);
            yield return ToFoodHold("sack", ShipAt(12.3f, 2.8f, 1.2f));
            if (!_bot.Ok) yield break;
            yield return _bot.Approach(sack, "food_hold_sack");
            if (!_bot.Ok) yield break;
            yield return _bot.Press(sack, "lift_sack", () => _bot.Rig.interactor.Holding == sack, "prompt.pick_two_hands");
            if (!_bot.Ok) yield break;
            yield return FromFoodHold("sack", sack);
            if (_bot.Ok) yield return To(sacks.zone, V(1.32f, -1.05f), "carry_sack_to_prep");
            if (!_bot.Ok)
            {
                if (_bot.Rig.interactor.Holding != sack && attempt < 2)
                { _log.Note("Bao tuột khỏi tay: thử lại từ hầm."); yield return Sack(sacks, attempt + 1); }
                yield break;
            }
            yield return _bot.Press(sacks.zone, "place_sack", () => sacks.OnTable == sack, "prompt.drop");
            if (!_bot.Ok) yield break;
            yield return _bot.Press(sacks.mouth, "untie_sack", () => sacks.Pour.Untied, "prompt.untie");
            if (!_bot.Ok) yield break;
            yield return _bot.Wait("potato_tub_returned", () => !sacks.TubAway, 120f, 180f);
            if (!_bot.Ok) yield break;
            int delivered = sacks.Delivered;
            yield return _bot.Hold(sacks.mouth, "pour_sack_downward", () => sacks.Delivered > delivered, () => sacks.Pour.Progress,
                () => new Vector2(0f, -15f), "prompt.pour_sack");
        }

        static HaiBanTay.Ship.ShipLayout ShipLayoutNow => UnityEngine.Object.FindAnyObjectByType<HaiBanTay.Ship.ShipLayout>();
        static Vector3 GalleyAt(float x, float z) => ShipLayoutNow.galley.TransformPoint(V(x, z));
        static Vector3 ShipAt(float x, float y, float z) => ShipLayoutNow.transform.TransformPoint(new Vector3(x, y, z));

        /// <summary>Bếp → cửa bếp → boong giếng trước → buồng thang có mái → 14 bậc xuống hầm thực phẩm, tới điểm `last` trong hầm.</summary>
        IEnumerator ToFoodHold(string step, Vector3 last, bool aroundRailEnd = true)
        {
            yield return _bot.Travel(step + "_galley_door", GalleyAt(-1.6f, -1.0f));
            if (!_bot.Ok) yield break;
            var route = new List<Vector3> { GalleyAt(-3.3f, 1.6f), GalleyAt(-4.3f, 2.25f), GalleyAt(-4.75f, 2.6f), GalleyAt(-5.6f, 2.6f), ShipAt(5.45f, 5.6f, -2.2f),
                ShipAt(5.45f, 5.6f, 0f), ShipAt(6.35f, 5.6f, 0f), ShipAt(9.9f, 2.8f, 0f) };
            // Vòng qua đầu tay vịn thang hầm (chân thang x = 10,42) trước khi rẽ vào chỗ hàng ở phía z > 0.
            if (aroundRailEnd) route.Add(ShipAt(10.75f, 2.8f, 0f));
            route.Add(last);
            foreach (var point in route)
            {
                yield return _bot.Walk(step + "_to_hold", point, 0.3f);
                if (!_bot.Ok) yield break;
            }
        }

        /// <summary>
        /// Hầm → thang → boong giếng trước → cửa bếp. Đang vác `carried` mà tuột khỏi tay thì dừng (lỗi). Đi chếch thẳng tới cửa bếp thì đồ
        /// vác sượt vách tây bếp (x ≈ −5,2), thùng thịt chọc vào lỗ cửa sổ 7–8 cm (bot ngày 3 18/10): đi dọc boong cách vách ~0,9 m tới
        /// ngay trước cửa (tọa độ thế giới) rồi đi thẳng vào; rẽ chéo vào cửa thì thùng va cánh cửa 17 cm.
        /// </summary>
        IEnumerator FromFoodHold(string step, Holdable carried)
        {
            foreach (var point in new[] { ShipAt(10.75f, 2.8f, 0f), ShipAt(9.9f, 2.8f, 0f), ShipAt(6.3f, 5.6f, 0f), ShipAt(5.45f, 5.6f, 0f), new Vector3(-6.15f, 0f, 0.3f), new Vector3(-6.15f, 0f, 2.6f), GalleyAt(-5.6f, 2.6f), GalleyAt(-4.75f, 2.6f), GalleyAt(-4.3f, 2.25f), GalleyAt(-3.3f, 1.6f), GalleyAt(-1.6f, -1.0f) })
            {
                yield return _bot.Walk(step + "_from_hold", point, 0.3f);
                if (_bot.Ok && carried != null && _bot.Rig.interactor.Holding != carried)
                { _bot.Fail("Vật đang vác tuột khỏi tay trên đường từ hầm về bếp: " + carried.name); yield return _log.Checkpoint("carried_slipped"); }
                if (!_bot.Ok) yield break;
            }
        }

        IEnumerator Greens(GreensStation greens)
        {
            yield return _bot.FreeHands();
            if (!_bot.Ok) yield break;
            if (greens.ReadyToWash)
            {
                yield return To(greens.cleanTarget, V(0.05f, -0.85f), "clean_greens_basket");
                if (!_bot.Ok) yield break;
                yield return _bot.Press(greens.cleanTarget, "carry_clean_greens", () => greens.CarryingClean, "prompt.carry_greens_to_wash");
                if (!_bot.Ok) yield break;
                yield return To(greens.sink, V(5.85f, -1.6f), "greens_sink");
                if (!_bot.Ok) yield break;
                yield return _bot.Press(greens.sink, "place_greens_in_sink", () => greens.InSink, "prompt.place_greens_in_sink");
                if (!_bot.Ok) yield break;
                yield return _bot.Hold(greens.sink, "wash_greens", () => greens.Washed, () => greens.WashProgress, () => Vector2.zero, "prompt.wash_greens");
                if (!_bot.Ok) yield break;
                yield return _bot.Press(greens.sink, "lift_washed_greens", () => greens.CarryingClean, "prompt.take_clean_greens");
                if (!_bot.Ok) yield break;
                yield return To(greens.board, V(-1.8f, -0.3f), "deliver_greens_to_cook");
                if (!_bot.Ok) yield break;
                yield return _bot.Press(greens.board, "deliver_washed_greens", () => !greens.CarryingClean, "prompt.deliver_greens");
                yield break;
            }
            // Rổ A từ hầm lên bàn, nhặt từng bó; chưa rửa/đặt rổ B thì chưa tính đơn vị.
            if (!greens.RawReady)
            {
                yield return ToFoodHold("greens", ShipAt(10.2f, 2.8f, 0.9f));
                if (!_bot.Ok) yield break;
                yield return _bot.Press(greens.crate, "take_raw_basket", () => greens.CarryingRaw, "prompt.take_greens");
                if (!_bot.Ok) yield break;
                yield return FromFoodHold("greens", null);
                if (!_bot.Ok) yield break;
                yield return To(greens.board, V(-1.8f, -0.3f), "place_raw_basket");
                if (!_bot.Ok) yield break;
                yield return _bot.Press(greens.board, "place_raw_greens", () => greens.RawReady, "prompt.place_raw_greens");
                if (!_bot.Ok) yield break;
            }
            yield return To(greens.board, V(-1.8f, -0.3f), "greens_board");
            if (!_bot.Ok) yield break;
            if (!greens.Logic.HasBunch)
            {
                yield return _bot.Press(greens.board, "take_bunch_from_basket", () => greens.Logic.HasBunch, "prompt.take_bunch");
                if (!_bot.Ok) yield break;
            }
            while (greens.Logic.HasBunch)
            {
                int index = greens.Logic.Index;
                var renderer = greens.leaves[index].GetComponentInChildren<Renderer>();
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                var color = block.GetColor("_BaseColor");
                // Classify the rendered leaf's colour rather than asking the hidden leaf-answer array.
                bool wilted = color.r > color.g;
                float direction = wilted ? -15f : 15f;
                yield return _bot.Hold(greens.board, "sort_leaf_" + index + (wilted ? "_left" : "_right"),
                    () => !greens.Logic.HasBunch || greens.Logic.Index > index, () => greens.Logic.Lean,
                    () => new Vector2(direction, 0f), "prompt.sort");
                if (!_bot.Ok) yield break;
                yield return _bot.Frame();
            }
        }

        IEnumerator Wash(WashStation wash)
        {
            yield return _bot.FreeHands();
            if (!_bot.Ok) yield break;
            yield return To(wash.pile, V(5.55f, -1.4f), "dirty_pots");
            if (!_bot.Ok) yield break;
            yield return _bot.Press(wash.pile, "pot_into_basin", () => wash.Logic.InBasin, "prompt.take_pot");
            if (!_bot.Ok) yield break;
            yield return To(wash.basin, V(5.85f, -1.6f), "wash_basin");
            if (!_bot.Ok) yield break;
            yield return _bot.Press(wash.basin, "place_pot_in_basin", () => !wash.CarryingPot, "prompt.place_pot");
            if (!_bot.Ok) yield break;
            if (!wash.WaterRunning)
            {
                // Vòi trên chậu: gạt cho nước chảy rồi mới cọ (QA-0132).
                yield return _bot.Press(wash.tapTarget, "open_tap", () => wash.WaterRunning, "prompt.tap_open");
                if (!_bot.Ok) yield break;
            }
            if (_bot.Rig.interactor.Tool != HaiBanTay.Player.Tool.Brush && !wash.BrushOnRim)
            {
                // Bàn chải ngắn ở kệ cạnh chậu: lấy rồi quay lại chậu.
                yield return Tool(wash.brushHook, PlayerBot.BrushShelfStand);
                if (!_bot.Ok) yield break;
                yield return To(wash.basin, V(5.85f, -1.6f), "wash_basin_brush");
                if (!_bot.Ok) yield break;
            }
            yield return _bot.Hold(wash.basin, "wash_pot_drag", () => wash.Logic.BasinClean, () => wash.Logic.Scrub.Progress,
                ScrubCoverage.For(() => wash.Scrubbing), "prompt.wash");
            if (!_bot.Ok) yield break;
            yield return _bot.Hold(wash.basin, "rinse_pot", () => wash.Rinsed, () => wash.RinseProgress,
                () => Vector2.zero, "prompt.rinse_pot");
            if (!_bot.Ok) yield break;
            int washed = wash.Logic.Washed;
            yield return _bot.Press(wash.basin, "rack_clean_pot", () => wash.Logic.Washed > washed, "prompt.rack_pot");
        }

        IEnumerator Secure(SecurePotsTask pots)
        {
            yield return _bot.FreeHands();
            if (!_bot.Ok) yield break;
            foreach (var target in pots.targets)
            {
                var p = target.transform.position;
                yield return To(target, V(p.x, -1.6f), "secure_" + target.name);
                if (!_bot.Ok) yield break;
                int count = pots.SecuredCount;
                yield return _bot.Press(target, "secure_" + target.name, () => pots.SecuredCount > count, "prompt.secure");
                if (!_bot.Ok) yield break;
            }
            yield return _bot.Wait("all_pots_secured", () => pots.State == TaskState.Done);
        }

        /// <summary>Kéo lê nồi cao theo các điểm của người (đi lùi, nhìn vào nồi, giữ chuột); dừng khi <paramref name="arrived"/>.</summary>
        IEnumerator TowTall(TallPotStation tall, Vector3[] path, Func<bool> arrived, string step)
        {
            var target = tall.potTarget;
            Vector3 Aim() => tall.potRoot.position + Vector3.up * 0.75f;
            _bot.AimAt(Aim());
            yield return _bot.Focus(target, step + "_grip", "prompt.tow_tall");
            if (!_bot.Ok) yield break;
            _bot.Input.SimUse = true;
            yield return _bot.Wait(step + "_grip", () => tall.Tow.Gripping, 15f, 30f);
            if (!_bot.Ok) { _bot.Input.SimUse = false; yield break; }
            float start = Time.time;
            foreach (var goal in path)
            {
                _log.Note($"Tall tow waypoint: goal={goal}; pot={tall.potRoot.position}; slips={tall.Tow.Slips}.");
                int regrips = 0, releases = 0;
                float best = PlayerBot.HorizontalDistance(_bot.Rig.Position, goal), progressed = Time.time;
                while (!arrived() && PlayerBot.HorizontalDistance(_bot.Rig.Position, goal) > 0.1f && Time.time - start < 300f)
                {
                    if (!tall.Tow.Gripping)
                    {
                        _bot.Input.SimMove = Vector2.zero; _bot.Input.SimUse = false;
                        yield return _bot.Frame();
                        if (++regrips > 3) { _bot.Fail("Three tall-pot regrips failed on this waypoint."); break; }
                        var nearby = _bot.Rig.Position - tall.potRoot.position; nearby.y = 0f;
                        if (nearby.sqrMagnitude < 0.01f) nearby = Vector3.forward;
                        yield return _bot.Walk(step + "_return", tall.potRoot.position + nearby.normalized * 0.8f, 0.12f);
                        _bot.AimAt(Aim());
                        yield return _bot.Focus(target, step + "_regrip", "prompt.tow_tall");
                        if (!_bot.Ok) break;
                        _bot.Input.SimUse = true;
                        yield return _bot.Wait(step + "_regrip", () => tall.Tow.Gripping, 12f, 20f);
                        if (!_bot.Ok) break;
                        progressed = Time.time;
                    }
                    _bot.AimAt(Aim());
                    var move = goal - _bot.Rig.Position; move.y = 0f; move.Normalize();
                    // Đứng chắn nhau với NPC giữa lối (cả hai chờ nhau): lách sang bên như người chơi thật, đổi bên mỗi 1,5 s.
                    float stalled = Time.time - progressed;
                    // Phụ bếp nhường nồi đang kéo còn Ba chờ phụ bếp đi qua: hai bên đứng chờ nhau. Người chơi thật nhả chuột cho nồi hết là vật cản,
                    // phụ bếp đi tiếp, rồi nắm lại; bot làm đúng vậy (tối đa hai lần mỗi điểm).
                    if (stalled > 6f && releases < 2)
                    {
                        releases++;
                        _log.Note($"Kẹt {stalled:0}s ở điểm {goal}: nhả nồi 3 s cho NPC đi qua (lần {releases}).");
                        _bot.Input.SimMove = Vector2.zero; _bot.Input.SimUse = false;
                        yield return new WaitForSeconds(3f);
                        progressed = Time.time;
                        continue;
                    }
                    if (stalled > 4f)
                        move = Quaternion.Euler(0f, ((int)((stalled - 4f) / 1.5f) % 2 == 0) ? 70f : -70f, 0f) * move;
                    _bot.Input.SimMove = new Vector2(Vector3.Dot(move, _bot.Rig.transform.right), Vector3.Dot(move, _bot.Rig.transform.forward));
                    yield return _bot.Frame();
                    float remaining = PlayerBot.HorizontalDistance(_bot.Rig.Position, goal);
                    if (best - remaining > 0.05f) { best = remaining; progressed = Time.time; }
                    // Waypoint sát chậu khoai (mép chậu cao ngang hông): thân người dừng cách 0,1–0,3 m, coi như tới, đi tiếp đoạn sau.
                    if (Time.time - progressed > 3f && remaining < 0.3f) break;
                    // Nồi đã tới sát điểm (nồi theo sau người 0,76 m nên người không bao giờ vào được điểm nằm trong vòng nồi): coi như tới.
                    if (Time.time - progressed > 3f && PlayerBot.HorizontalDistance(tall.potRoot.position, goal) < 0.5f) break;
                    if (Time.time - progressed > 10f)
                    {
                        string obstacle = "unknown";
                        if (Physics.SphereCast(_bot.Rig.Position + Vector3.up * 0.8f, 0.25f, move, out var hit, 1f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                            obstacle = hit.collider.name;
                        // Vật chồng sẵn lên thân người (SphereCast bỏ qua): liệt kê mọi collider trong 0,45 m quanh người.
                        var near = new System.Collections.Generic.List<string>();
                        foreach (var c in Physics.OverlapCapsule(_bot.Rig.Position + Vector3.up * 0.3f, _bot.Rig.Position + Vector3.up * 1.5f, 0.45f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                            if (c.GetComponentInParent<HaiBanTay.Player.PlayerRig>() == null)
                                near.Add($"{(c.transform.parent != null ? c.transform.parent.name + "/" : "")}{c.name}@{c.bounds.center}");
                        if (near.Count > 0) obstacle += "; near=" + string.Join(", ", near);
                        // Nồi có đi theo tay không: vật chặn trên đường nồi về phía người, và chỗ đứng các NPC.
                        var potBody = tall.potRoot.GetComponent<Rigidbody>();
                        var toPlayer = _bot.Rig.Position - tall.potRoot.position; toPlayer.y = 0f;
                        if (potBody != null && toPlayer.sqrMagnitude > 1e-4f)
                            foreach (var h in potBody.SweepTestAll(toPlayer.normalized, 0.3f, QueryTriggerInteraction.Ignore))
                                obstacle += $"; potHit={(h.collider.transform.parent != null ? h.collider.transform.parent.name + "/" : "")}{h.collider.name}@{h.distance:F2}";
                        foreach (var npc in UnityEngine.Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None))
                            if (HaiBanTay.Tests.PlayerBot.HorizontalDistance(npc.transform.position, _bot.Rig.Position) < 2f)
                                obstacle += $"; npc={npc.name}@{npc.transform.position} ghost={npc.YieldingToPlayer} walk={npc.Walking}";
                        obstacle += $"; gripping={tall.Tow.Gripping} dist={toPlayer.magnitude:F2}";
                        _log.Bug(step + "_blocked", "MAJOR", "Giữ chuột và đi lùi kéo nồi cao tới chỗ cần tới.",
                            $"Người chơi không tiến về waypoint 10 s; pot={tall.potRoot.position}, player={_bot.Rig.Position}, goal={goal}, obstacle={obstacle}.");
                        break;
                    }
                }
                yield return _log.Checkpoint(step + "_waypoint");
                if (!_bot.Ok || arrived()) break;
            }
            _bot.Input.SimMove = Vector2.zero;
            yield return _bot.Wait(step + "_arrived", arrived, 20f, 30f);
            _bot.Input.SimUse = false;
            yield return _bot.Frame();
        }

        /// <summary>tall_out: kéo nồi cao từ góc phòng rửa qua cửa vách ngăn ra trước lò bắc, rồi E nhấc lên ô bếp.</summary>
        IEnumerator TallOut(TallPotStation tall)
        {
            yield return _bot.FreeHands();
            if (!_bot.Ok) yield break;
            if (tall.Logic.State == TallPotState.HomeClean)
            {
                var home = tall.potRoot.position;
                yield return To(tall.potTarget, V(home.x, home.z + 0.85f), "tall_pot_home");
                if (!_bot.Ok) yield break;
                yield return _log.Shot("tall_pot_grip_home");
                var spot = tall.stoveSpot.position;
                // Đi lùi lên bắc ra giữa cửa vách ngăn (z 0,3), sang tây qua bàn, rồi dọc trước lò bắc từ đông sang tây: nồi tới chỗ đặt sau cùng.
                var path = new[] { V(home.x, 0.3f), V(1.7f, 0.3f), V(1.7f, 1.2f), V(0.2f, 1.25f), V(spot.x + 0.9f, spot.z), V(spot.x - 0.85f, spot.z - 0.4f) };
                yield return TowTall(tall, path, () => tall.Logic.State == TallPotState.AtStoveClean, "tow_tall_out");
                if (!_bot.Ok) yield break;
                yield return _log.Shot("tall_pot_at_stove");
            }
            if (tall.Logic.State == TallPotState.AtStoveClean)
            {
                var p = tall.potRoot.position;
                yield return To(tall.potTarget, V(p.x, p.z - 0.9f), "tall_pot_lift_stand");
                if (!_bot.Ok) yield break;
                yield return _bot.Press(tall.potTarget, "lift_tall_pot", () => tall.Lifting || tall.Logic.State == TallPotState.OnHobClean, "prompt.lift_tall");
                if (!_bot.Ok) yield break;
                yield return _bot.Wait("tall_pot_on_hob", () => !tall.Lifting && tall.Logic.State == TallPotState.OnHobClean, 20f, 30f);
                if (_bot.Ok) yield return _log.Shot("tall_pot_on_hob");
            }
        }

        /// <summary>scrub_tall: E nhấc nồi bẩn xuống, kéo về góc phòng rửa, lấy bàn chải cán dài, đứng ghế cọ, tráng.</summary>
        IEnumerator TallPot(TallPotStation tall)
        {
            yield return _bot.FreeHands();
            if (!_bot.Ok) yield break;
            if (tall.Logic.State == TallPotState.OnHobDirty)
            {
                var p = tall.potRoot.position;
                yield return To(tall.potTarget, V(p.x, 1.65f), "tall_pot_hob_stand");
                if (!_bot.Ok) yield break;
                yield return _bot.Press(tall.potTarget, "lift_down_tall_pot", () => tall.Lifting || tall.Logic.State == TallPotState.AtStoveDirty, "prompt.lift_down_tall");
                if (!_bot.Ok) yield break;
                yield return _bot.Wait("tall_pot_on_floor", () => !tall.Lifting && tall.Logic.State == TallPotState.AtStoveDirty, 20f, 30f);
                if (!_bot.Ok) yield break;
            }
            if (tall.Logic.State == TallPotState.AtStoveDirty)
            {
                var p = tall.potRoot.position;
                var home = tall.home.position;
                yield return To(tall.potTarget, V(p.x + 0.85f, p.z), "tall_pot_tow_back_stand");
                if (!_bot.Ok) yield break;
                // Kéo về: dọc trước lò sang đông, qua bàn, vào cửa vách ngăn (z 0,3), rồi đi lùi xuống nam: nồi tới góc rửa sau cùng.
                var path = new[] { V(0.2f, 1.25f), V(1.7f, 1.2f), V(1.7f, 0.3f), V(home.x, 0.3f), V(home.x, home.z - TallPotStation.TowReach - 0.05f) };
                yield return TowTall(tall, path, () => tall.Logic.State == TallPotState.HomeDirty, "tow_tall_back");
                if (!_bot.Ok) yield break;
            }
            if (tall.Logic.State != TallPotState.HomeDirty) yield break;
            if (!tall.OnStool)
            {
                yield return Tool(tall.longBrushHook, PlayerBot.LongBrushStand);
                if (!_bot.Ok) yield break;
                var stool = tall.stool.transform.position;
                yield return To(tall.stool, V(stool.x + 0.45f, stool.z + 0.35f), "tall_pot_stool");
                if (_bot.Ok) yield return _log.Shot("tall_pot_stool_reached");
                if (!_bot.Ok) yield break;
                yield return _bot.Press(tall.stool, "step_up_stool", () => tall.OnStool, "prompt.stool");
                if (!_bot.Ok) yield break;
                yield return _bot.Wait("stool_staging_settled", () => PlayerBot.HorizontalDistance(_bot.Rig.Position, tall.standPoint.position) < 0.08f && _bot.Rig.motor.ExtraHeadHeight > 0.3f);
                if (!_bot.Ok) yield break;
            }
            _bot.AimAt(tall.potRoot.position + Vector3.up * 0.2f);
            // Như người chơi: cọ chỗ với tới, chững (dưới 1,5% mỗi 5 s) thì nhấn E xoay nồi một phần tư vòng rồi cọ tiếp (bot 23/10: không
            // xoay thì 143 s). Một mẫu cọ cho cả lượt: tạo lại sau mỗi lần xoay thì mẫu quét lại từ hàng đầu, tiến độ đứng ở 0,67.
            var coverage = ScrubCoverage.For(() => tall.Scrubbing);
            float scrubFrom = Time.time;
            for (int turn = 0; !tall.Logic.Scrubbed; turn++)
            {
                // Ngày sóng: tiến độ dừng ở swayAt cho tới khi Jacques giữ nồi; chờ như cũ, xoay không giúp. Quá 150 s: lượt cuối, chững là lỗi.
                bool capped = tall.Sway && !tall.SailorHelped && tall.Scrub.Progress >= tall.swayAt - 0.01f;
                bool last = Time.time - scrubFrom > 150f;
                yield return _bot.Hold(tall.potTarget, "scrub_tall_pot", () => tall.Logic.Scrubbed, () => tall.Scrub.Progress,
                    coverage, "prompt.scrub_turn_tall", true, capped || last ? 25f : 5f, !capped && !last, capped || last ? 0.005f : 0.015f);
                if (!_bot.Ok || tall.Logic.Scrubbed) break;
                var from = tall.pot.rotation;
                yield return _bot.Press(tall.potTarget, "turn_tall_pot", () => !tall.Turning && Quaternion.Angle(tall.pot.rotation, from) > 80f,
                    "prompt.scrub_turn_tall", 5f);
                if (!_bot.Ok) yield break;
                _bot.AimAt(tall.potRoot.position + Vector3.up * 0.2f);
            }
            if (!_bot.Ok) yield break;
            yield return _bot.Hold(tall.potTarget, "rinse_tall_pot", () => tall.Logic.State == TallPotState.HomeClean, () => tall.RinseProgress,
                () => Vector2.zero, "prompt.rinse_pot");
        }

        IEnumerator Pan(PanStation pan)
        {
            yield return _bot.FreeHands();
            if (!_bot.Ok) yield break;
            int delivered = pan.Delivered;
            if (pan.pan.position.x < 1f)
            {
                // Give the crew room to finish their visible walk before approaching a pan near the stove.
                yield return _bot.Travel("wait_outside_crew_lane", V(2.9f, 1.5f));
                if (!_bot.Ok) yield break;
                var crew = UnityEngine.Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None);
                yield return _bot.Wait("crew_clear_of_pan", () => crew.All(n => n == null ||
                    PlayerBot.HorizontalDistance(n.transform.position, pan.pan.position) > 0.85f), 90f, 120f, "MINOR");
                if (!_bot.Ok) yield break;
            }
            // Centre the pan in the narrow wash doorway before turning through it.
            bool inWashRoom = pan.pan.position.x > 3.4f && pan.pan.position.z < 0.9f;
            var direction = pan.Next == 0 && inWashRoom ? V(5.75f, -0.96f) - pan.pan.position : Vector3.right;
            direction.y = 0f; direction.Normalize();
            var stand = pan.pan.position + direction * 0.95f; stand.y = 0f;
            if (!inWashRoom)
            {
                // Approach the stove-side handle around the east end of the prep table.
                // The generic graph can choose a final leg through the table when the pan blocks its reachability cast.
                stand.z = Mathf.Max(1f, Mathf.Min(stand.z, 1.1f));
                yield return _bot.Travel("pan_handle_east_approach", V(2.7f, stand.z));
                if (!_bot.Ok) yield break;
                yield return _bot.Walk("pan_handle_north_approach", stand, 0.08f);
                yield return _bot.Focus(pan.handle, "pan_handle", "prompt.tow");
                if (!_bot.Ok) yield return _bot.Approach(pan.handle, "pan_handle", stand);
            }
            else yield return To(pan.handle, stand, "pan_handle");
            if (!_bot.Ok) yield break;
            _bot.Input.SimUse = true;
            yield return _bot.Wait("grip_pan", () => pan.Tow.Gripping, 15f, 30f);
            if (!_bot.Ok) { _bot.Input.SimUse = false; yield break; }
            // First lift the tow route clear of the table legs, then cross below the crew's north lane.
            // A retry starts where the pan actually lies; it must not tow an almost-delivered pan back to the wash doorway.
            // Wash-room door (17/10): partition x 3.3, opening z −0.3…0.9. The 0.8 m pan fits only near the door's centre line z 0.3:
            // tow north of the prep table (z ≤ 0.45) to x 2.3, step down to z 0.3 and pull straight east so the trailing pan lines up
            // before the door; coming back, pull it fully out west (x 1.7) before turning north past the Wall_Wash_N corner (3.3, 0.9).
            var path = pan.Next == 1
                ? new[] { V(2.3f, 1.05f), V(2.3f, 0.3f), V(6.6f, 0.3f), V(6.6f, 0.55f) }
                : inWashRoom
                    ? new[] { V(4.0f, 0.3f), V(1.7f, 0.3f), V(1.7f, 1.2f), V(0.2f, 1.2f), V(-2.35f, 1.45f) }
                    : pan.pan.position.x < 1f
                        ? new[] { V(-2.35f, 1.45f) }
                        : new[] { V(2.7f, 1.8f), V(2.7f, 1.2f), V(0.2f, 1.2f), V(-2.35f, 1.45f) };
            float start = Time.time, progressed = Time.time;
            bool detoured = false;
            int toolYields = 0;
            foreach (var goal in path)
            {
                _log.Note($"Tow waypoint: goal={goal}; pan={pan.pan.position}; slips={pan.Tow.Slips}.");
                int regrips = 0;
                float best = PlayerBot.HorizontalDistance(_bot.Rig.Position, goal);
                progressed = Time.time;
                while (pan.Delivered == delivered && PlayerBot.HorizontalDistance(_bot.Rig.Position, goal) > 0.08f && Time.time - start < 300f)
                {
                    if (!pan.Tow.Gripping)
                    {
                        _bot.Input.SimMove = Vector2.zero; _bot.Input.SimUse = false;
                        yield return _bot.Frame();
                        _log.Note($"Pan slipped; return within reach: pan={pan.pan.position}; slips={pan.Tow.Slips}.");
                        if (++regrips > 3) { _bot.Fail("Three pan regrips failed on this waypoint; inspect the route and collider evidence."); break; }
                        var nearby = _bot.Rig.Position - pan.pan.position; nearby.y = 0f;
                        if (nearby.sqrMagnitude < 0.01f) nearby = Vector3.left;
                        // Ba đã đứng trong tầm tay (≥ 0,72 m: nửa chảo + bán kính người) thì tại chỗ bám lại; đi tới điểm 0,8 m tính
                        // theo hướng cũ có thể nằm trong bàn bếp khi chảo kẹt sát bàn.
                        var pickup = pan.pan.position + nearby.normalized * Mathf.Clamp(nearby.magnitude, 0.72f, 0.9f); pickup.y = 0f;
                        yield return _bot.Walk("return_within_pan_reach", pickup, 0.12f);
                        _bot.AimAt(pan.handle.GetComponent<Collider>().bounds.center);
                        yield return _bot.Focus(pan.handle, "regrip_pan", "prompt.tow");
                        if (!_bot.Ok) break;
                        _bot.Input.SimUse = true;
                        yield return _bot.Wait("regrip_pan", () => pan.Tow.Gripping, 12f, 20f);
                        if (!_bot.Ok) break;
                        progressed = Time.time;
                    }
                    _bot.AimAt(pan.handle.GetComponent<Collider>().bounds.center);
                    var move = goal - _bot.Rig.Position; move.y = 0f; move.Normalize();
                    _bot.Input.SimMove = new Vector2(Vector3.Dot(move, _bot.Rig.transform.right), Vector3.Dot(move, _bot.Rig.transform.forward));
                    yield return _bot.Frame();
                    float remaining = PlayerBot.HorizontalDistance(_bot.Rig.Position, goal);
                    if (best - remaining > 0.05f) { best = remaining; progressed = Time.time; }
                    // Lùi vào phụ bếp đang cầm xẻng (hộp PlayerGuard chặn, phụ bếp đứng chờ Ba tránh): bước ngược ra nhường rồi chờ phụ bếp
                    // đi qua, như người chơi thật; không thì hai bên chờ nhau tới khi bot lách (pan_blocked 18/10).
                    if (Time.time - progressed >= 2f && toolYields < 3 && _bot.NpcToolNear(0.6f, out var tool))
                    {
                        toolYields++;
                        _log.Note($"Tow yields to NPC tool at {tool.bounds.center}.");
                        for (float e = 0f; e < 0.6f && pan.Tow.Gripping; e += Time.deltaTime)
                        { _bot.Input.SimMove = -_bot.Input.SimMove.normalized; yield return _bot.Frame(); }
                        _bot.Input.SimMove = Vector2.zero;
                        for (float e = 0f; e < 12f && tool.enabled && _bot.NpcToolNear(1.0f, out _); e += Time.deltaTime) yield return _bot.Frame();
                        best = PlayerBot.HorizontalDistance(_bot.Rig.Position, goal); progressed = Time.time;
                        continue;
                    }
                    if (Time.time - progressed < 8f) continue;
                    string obstacle = "unknown";
                    if (Physics.SphereCast(_bot.Rig.Position + Vector3.up * 0.8f, 0.25f, move, out var hit, 1f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                        obstacle = hit.collider.name;
                    _log.Bug("pan_blocked_" + pan.Next, "MAJOR", "Giữ chuột và đi lùi kéo chảo tới vòng phấn.", $"Người chơi không tiến về waypoint 8s; pan={pan.pan.position}, player={_bot.Rig.Position}, goal={goal}, obstacle={obstacle}. Cần loại trừ đường kéo của bot.");
                    if (detoured) break;
                    detoured = true;
                    float sideEnd = Time.time + 1.2f;
                    while (Time.time < sideEnd && pan.Tow.Gripping) { _bot.Input.SimMove = new Vector2(1f, -0.2f); yield return _bot.Frame(); }
                    progressed = Time.time;
                }
                yield return _log.Checkpoint("tow_pan_waypoint");
                if (!_bot.Ok) { _bot.Input.SimMove = Vector2.zero; _bot.Input.SimUse = false; yield break; }
                if (pan.Delivered > delivered) break;
            }
            _bot.Input.SimMove = Vector2.zero;
            // Keep gripping while the pan catches up; delivery is checked only while the grip is held.
            yield return _bot.Wait("pan_arrived", () => pan.Delivered > delivered, 30f, 45f);
            _bot.Input.SimUse = false;
            yield return _bot.Frame();
            if (pan.NeedsWash)
            {
                if (_bot.Rig.interactor.Tool != HaiBanTay.Player.Tool.LongBrush)
                {
                    yield return Tool(pan.longBrushHook, PlayerBot.LongBrushStand);
                    if (!_bot.Ok) yield break;
                }
                // Chảo giao xong nằm cách chân người kéo ≥ 0,72 m (PanStation: chỉ giao khi Ba cách tâm vòng đủ xa, thường ở phía đông chảo
                // sát tường đông): đã trong tầm thì cọ tại chỗ; chưa thì tới cạnh chảo phía tây (PanStation bước tới chỗ đứng chà).
                var gap = _bot.Rig.Position - pan.pan.position; gap.y = 0f;
                var scrubAt = gap.magnitude >= 0.72f && gap.magnitude <= 1.1f ? _bot.Rig.Position : pan.pan.position + Vector3.left * PanStation.ScrubStand;
                yield return To(pan.handle, scrubAt, "wash_copper_pan");
                if (!_bot.Ok) yield break;
                yield return _bot.Hold(pan.handle, "scrub_copper_pan", () => pan.Scrub.Done, () => pan.Scrub.Progress,
                    ScrubCoverage.For(() => pan.Scrubbing), "prompt.scrub");
                if (!_bot.Ok) yield break;
                yield return _bot.Hold(pan.handle, "rinse_copper_pan", () => !pan.NeedsWash, () => pan.RinseProgress,
                    () => Vector2.zero, "prompt.rinse_pot");
            }
            yield return _log.Checkpoint("pan_arrived");
        }

        static object StudyLogic(NotebookView nb) =>
            nb.Mode == NotebookMode.Match ? nb.Match : nb.Mode == NotebookMode.Trace ? nb.Trace
            : nb.Mode == NotebookMode.Fill ? nb.Fill : nb.Mode == NotebookMode.Sentence ? (object)nb.Sentence : null;

        /// <summary>Học buổi tối (M11): có thể ôn từ nợ (ghép từ) rồi bài chính (ghép từ, tô chữ, điền từ, viết câu).</summary>
        IEnumerator Study(ShiftDirector shift)
        {
            var nb = GameRoot.Instance.UI.Notebook;
            for (int stage = 0; stage < 4; stage++)
            {
                yield return _bot.Wait("study_open_D" + shift.Day.Number + "_" + stage, () => nb.InStudy || Beat.EndsWith(".rest") || shift.Finished, 120f, 180f);
                if (!_bot.Ok || !nb.InStudy) yield break;
                yield return _log.Checkpoint("study_" + nb.Mode);
                var mode = nb.Mode;
                object logic = StudyLogic(nb);
                switch (nb.Mode)
                {
                    case NotebookMode.Match: yield return StudyMatch(nb); break;
                    case NotebookMode.Trace: yield return StudyTrace(nb); break;
                    case NotebookMode.Fill: yield return StudyFill(nb); break;
                    case NotebookMode.Sentence: yield return StudySentence(nb); break;
                }
                if (!_bot.Ok) yield break;
                // Ôn từ nợ có thể nối thẳng sang bài chính mà sổ không đóng: hết bài khi sổ đóng, đổi chế độ hoặc mở bài mới.
                yield return _bot.Wait("study_stage_closed_" + stage, () => !nb.InStudy || nb.Mode != mode || !ReferenceEquals(StudyLogic(nb), logic), 30f, 45f);
                if (!_bot.Ok) yield break;
            }
        }

        IEnumerator StudyMatch(NotebookView nb)
        {
            var match = nb.Match;
            for (int i = 0; i < match.Count; i++)
            {
                if (match.IsMatched(i))
                    continue;
                var pair = match.Pair(i);
                int matched = match.MatchedCount;
                yield return ClickButton(pair.Fr, "study_fr_" + pair.Id, "hbt-word");
                yield return ClickButton(pair.Vi, "study_vi_" + pair.Id, "hbt-word");
                yield return _bot.Wait("word_matched_" + pair.Id, () => match.MatchedCount > matched, 15f, 30f);
                if (!_bot.Ok) yield break;
            }
        }

        /// <summary>Tô chữ bằng chuột: nhấn ở chấm đầu nét, kéo theo nét mờ, nhả; đủ nét thì sang chữ sau.</summary>
        IEnumerator StudyTrace(NotebookView nb)
        {
            var trace = nb.Trace;
            for (int guard = 0; guard < 80 && nb.InStudy && nb.Mode == NotebookMode.Trace; guard++)
            {
                int done = trace.LettersDone;
                VisualElement canvas = null;
                yield return _bot.Wait("trace_canvas", () =>
                {
                    canvas = GameRoot.Instance.UI.Root.Q(className: "hbt-trace-canvas");
                    return canvas != null && canvas.contentRect.width > 1f;
                }, 10f, 20f);
                if (!_bot.Ok) yield break;
                var rect = canvas.worldBound;
                // Nét theo đơn vị chiều cao chữ, x tới trace.Aspect (ô tô rộng theo cả từ).
                float aspect = trace.Aspect;
                Vector2 At(Vector2 q) => new Vector2(rect.x + q.x / aspect * rect.width, rect.y + q.y * rect.height);
                foreach (var stroke in trace.Guide.ToList())
                {
                    var points = TraceLogic.Sample(stroke, 0.02f);
                    if (points.Length == 0)
                        continue;
                    SendPointer(canvas, EventType.MouseDown, At(points[0]));
                    for (int i = 1; i < points.Length; i++)
                    {
                        SendPointer(canvas, EventType.MouseDrag, At(points[i]));
                        if (i % 6 == 0)
                            yield return _bot.Frame();
                    }
                    SendPointer(canvas, EventType.MouseUp, At(points[points.Length - 1]));
                    yield return _bot.Frame();
                    if (trace.Done || trace.LettersDone != done)
                        break;
                }
                yield return _bot.Wait("trace_letter_" + done, () => trace.Done || trace.LettersDone > done || !nb.InStudy, 5f, 10f);
                if (!_bot.Ok) yield break;
            }
        }

        IEnumerator StudyFill(NotebookView nb)
        {
            var fill = nb.Fill;
            for (int guard = 0; guard < 40 && nb.InStudy && nb.Mode == NotebookMode.Fill && !fill.Done; guard++)
            {
                var item = fill.Current;
                int index = fill.Index;
                yield return ClickButton(item.Options[item.Answer], "fill_" + index, "hbt-study-option");
                yield return _bot.Wait("fill_answered_" + index, () => fill.Done || fill.Index > index || !nb.InStudy, 10f, 15f);
                if (!_bot.Ok) yield break;
            }
        }

        IEnumerator StudySentence(NotebookView nb)
        {
            var sentence = nb.Sentence;
            for (int guard = 0; guard < 80 && nb.InStudy && nb.Mode == NotebookMode.Sentence && !sentence.Done; guard++)
            {
                var item = sentence.Current;
                int index = sentence.Index, placed = sentence.Placed;
                yield return ClickButton(item.Tokens[placed], $"sentence_{index}_{placed}", "hbt-study-option");
                yield return _bot.Wait($"sentence_word_{index}_{placed}", () => sentence.Done || sentence.Index > index || sentence.Placed > placed || !nb.InStudy, 10f, 15f);
                if (!_bot.Ok) yield break;
            }
        }

        /// <summary>
        /// Kéo rổ rau (F05b): xuống hầm, buộc rổ, giữ chuột và đi trước rổ dọc dây sắt về bếp (không vượt quá tầm dây).
        /// Ngày bão: sóng cuốn người chơi tới cột buồm trước; chờ lấy lại điều khiển, vòng mạn phải miệng hầm hàng số 2
        /// về lại dây rồi kéo tiếp.
        /// </summary>
        IEnumerator Haul(HaulStation haul)
        {
            yield return _bot.FreeHands();
            if (!_bot.Ok) yield break;
            int pending = haul.Pending;
            for (int attempt = 0; attempt < 3 && haul.Pending >= pending && haul.Pending > 0; attempt++)
            {
                if (_bot.Rig.Position.x < -9.5f)
                {
                    // Sóng cuốn người chơi tới cột buồm trước. Về lại boong giếng: vòng mạn phải, qua đầu tời tay trái
                    // (khe giữa miệng hầm số 2 và tời chỉ rộng 0,58 m nên phải đi ngoài tời), rồi tới cửa buồng thang.
                    foreach (var point in new[] { V(-17f, 3f), V(-14.8f, 3f), V(-13.6f, 4.5f), V(-10.6f, 4.5f), V(-9.6f, 3.2f), V(-8.2f, 2.4f), V(-6.6f, 2.4f), V(-5.65f, 1.35f) })
                    {
                        yield return _bot.Walk("haul_back_deck", point, 0.4f);
                        if (!_bot.Ok) yield break;
                    }
                }
                if (!haul.Logic.Tied)
                {
                    // Rổ nằm ở sàn hầm thực phẩm: xuống bằng đúng lối thang (đồ thị chung không biết lối này).
                    yield return ToFoodHold("haul", haul.Logic.PointAt(haul.Logic.Basket + 1.0f), aroundRailEnd: false);
                    if (!_bot.Ok) yield break;
                    yield return _bot.Press(haul.basketTarget, "tie_basket", () => haul.Logic.Tied, "prompt.tie_basket");
                    if (!_bot.Ok) yield break;
                }
                else
                {
                    // Sau sóng người chơi đang ở boong giếng (cửa bếp, ngoài buồng thang): vào buồng thang rồi tới điểm dây.
                    foreach (var point in new[] { ShipAt(5.45f, 5.6f, 0f), ShipAt(6.35f, 5.6f, 0f) })
                    {
                        yield return _bot.Walk("haul_rope_cabin", point, 0.3f);
                        if (!_bot.Ok) yield break;
                    }
                    yield return _bot.Walk("haul_rope", haul.Logic.PointAt(Mathf.Min(haul.Logic.Length, haul.Logic.Basket + 1.2f)), 0.3f);
                    if (!_bot.Ok) yield break;
                }
                bool waved = haul.WaveDone;
                _bot.Input.SimUse = true;
                yield return HaulAlong(haul, () => haul.Pending < pending || (!waved && haul.WaveDone));
                _bot.Input.SimUse = false;
                _bot.Input.SimMove = Vector2.zero;
                if (!_bot.Ok) yield break;
                if (!waved && haul.WaveDone)
                {
                    _log.Note("Sóng phủ boong (F05b): chờ lấy lại điều khiển rồi quay lại dây.");
                    yield return _bot.Wait("wave_control_back", () => !haul.Waving && _bot.Rig.ControlEnabled, 20f, 30f);
                    if (!_bot.Ok) yield break;
                }
            }
            yield return _bot.Wait("haul_delivered", () => haul.Pending < pending, 30f, 45f);
        }

        /// <summary>Đi theo các điểm dây sắt phía trước rổ; đi xa quá tầm dây thì đứng chờ rổ theo kịp.</summary>
        IEnumerator HaulAlong(HaulStation haul, Func<bool> stop)
        {
            var rig = _bot.Rig;
            float lastBasket = haul.Logic.Basket, movedAt = Time.time;
            while (!stop())
            {
                if (!_bot.Input.GameplayEnabled || rig.motor.MovementLocked || !rig.ControlEnabled)
                {
                    _bot.Input.SimMove = Vector2.zero;
                    yield return _bot.Frame();
                    continue;
                }
                float at = haul.Logic.Project(rig.Position, out _);
                int i = 1;
                while (i < haul.points.Length - 1 && haul.Logic.DistanceAt(i) <= at + 0.3f)
                    i++;
                var goal = haul.points[i];
                float ahead = at - haul.Logic.Basket;
                var delta = goal - rig.Position;
                rig.look.SetAngles(Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg, 0f);
                _log.BotDriveFrame = Time.frameCount;
                _bot.Input.SimMove = ahead > haul.Logic.Rope + 0.8f ? Vector2.zero : new Vector2(0f, 1f);
                yield return _bot.Frame();
                if (haul.Logic.Basket > lastBasket + 0.05f)
                {
                    lastBasket = haul.Logic.Basket;
                    movedAt = Time.time;
                }
                if (Time.time - movedAt > 20f)
                {
                    _log.Bug("haul_stuck", "MAJOR", "Giữ chuột, đi trước rổ dọc dây sắt về bếp.",
                        $"Rổ không nhích 20 giây game; rổ={haul.Logic.Basket:F2}/{haul.Logic.Length:F2}, người chơi={rig.Position}.");
                    _bot.Fail("Haul basket did not follow the player.");
                    break;
                }
            }
            _bot.Input.SimMove = Vector2.zero;
        }

        IEnumerator Marseille()
        {
            Time.captureDeltaTime = 1f / 60f;
            var ending = (EndingDirector)SceneDirector.Current;
            var galley = ending.sweep.transform.parent.parent;
            Vector3 G(float x, float z) => galley.TransformPoint(V(x, z));
            // Đi trong lan can boong, vòng phía trước cầu thang và tời thay vì xuyên qua chúng.
            foreach (var point in new[] { V(3f, -2.4f), V(-3.25f, -2.4f), V(-3.25f, 1f), V(-7.2f, 1f), V(-7.2f, 0.72f), G(-4.5f, 2.6f),
                G(-4.5f, 1.4f), G(-2.4f, 1.4f), G(-2.4f, -0.85f), G(2.4f, -0.85f), G(2.45f, -1.3f) })
            {
                yield return _bot.Walk("final_shift_broom", point);
                if (!_bot.Ok) yield break;
            }
            yield return _bot.Wait("final_sweep_active", () => ending.sweep.State == TaskState.Active);
            if (!_bot.Ok) yield break;
            yield return _bot.Press(ending.sweep.hook, "take_final_broom", () => _bot.Rig.interactor.Tool == HaiBanTay.Player.Tool.Broom);
            if (!_bot.Ok) yield break;
            foreach (var point in new[] { G(2.4f, -0.85f), G(-1.5f, -0.6f) })
            { yield return _bot.Walk("final_sweep_stand", point); if (!_bot.Ok) yield break; }
            yield return _bot.Hold(ending.sweep.spots[0], "final_sweep", () => ending.sweep.State == TaskState.Done,
                () => ending.sweep.Logic.Progress(0), () => new Vector2(35f, 20f), "prompt.sweep");
            if (!_bot.Ok) yield break;
            foreach (var point in new[] { G(2.6f, 0.3f), G(4.3f, 0.3f), G(5.6f, -0.05f), G(5.75f, -1.6f) })
            { yield return _bot.Walk("final_wash_stand", point); if (!_bot.Ok) yield break; }
            yield return _bot.Wait("final_wash_active", () => ending.wash.State == TaskState.Active);
            if (!_bot.Ok) yield break;
            yield return _bot.Press(ending.wash.pile, "take_final_pot", () => ending.wash.Logic.InBasin, "prompt.take_pot");
            if (!_bot.Ok) yield break;
            // Cùng luồng bồn rửa của bếp: cầm nồi tới bồn, đặt xuống, cọ, tráng rồi úp lên giá.
            yield return _bot.Press(ending.wash.basin, "place_final_pot", () => !ending.wash.CarryingPot, "prompt.place_pot");
            if (!_bot.Ok) yield break;
            if (!ending.wash.WaterRunning)
            {
                yield return _bot.Press(ending.wash.tapTarget, "open_final_tap", () => ending.wash.WaterRunning, "prompt.tap_open");
                if (!_bot.Ok) yield break;
            }
            if (_bot.Rig.interactor.Tool != HaiBanTay.Player.Tool.Brush && !ending.wash.BrushOnRim && ending.wash.brushHook != null)
            {
                // Bàn chải ngắn ở kệ cạnh chậu (như bếp): lấy rồi quay lại chậu.
                yield return _bot.Walk("final_brush_stand", G(5.95f, -1.85f));
                if (!_bot.Ok) yield break;
                yield return _bot.Press(ending.wash.brushHook, "take_final_brush", () => _bot.Rig.interactor.Tool == HaiBanTay.Player.Tool.Brush,
                    ending.wash.brushHook.takeKey);
                if (!_bot.Ok) yield break;
                yield return _bot.Walk("final_wash_stand", G(5.75f, -1.6f));
                if (!_bot.Ok) yield break;
            }
            yield return _bot.Hold(ending.wash.basin, "wash_final_pot", () => ending.wash.Logic.BasinClean,
                () => ending.wash.Logic.Scrub.Progress, () => new Vector2(Time.frameCount % 24 < 12 ? 15f : -15f, 8f), "prompt.wash");
            if (!_bot.Ok) yield break;
            yield return _bot.Hold(ending.wash.basin, "rinse_final_pot", () => ending.wash.Rinsed, () => ending.wash.RinseProgress,
                () => Vector2.zero, "prompt.rinse_pot");
            if (!_bot.Ok) yield break;
            yield return _bot.Press(ending.wash.basin, "rack_final_pot", () => ending.wash.State == TaskState.Done, "prompt.rack_pot");
            if (!_bot.Ok) yield break;
            // Cửa phòng rửa chỉ mở ở z -0,3…0,9 (tường x 3,3); bếp lò sát cửa boong nên vòng phía tây.
            foreach (var point in new[] { G(5.6f, -0.05f), G(4.3f, 0.3f), G(2.6f, 0.3f), G(2.4f, -0.85f), G(-2.4f, -0.85f),
                G(-2.4f, 1.4f), G(-4.5f, 1.4f), G(-4.5f, 2.6f), V(-7.2f, 0.72f), V(-7.2f, 1f),
                V(-3.25f, 1f), V(-3.25f, -2.4f), V(-2.5f, -1.1f) })
            { yield return _bot.Walk("final_work_to_pay", point); if (!_bot.Ok) yield break; }
            yield return _bot.Wait("ending_raise_prompt", () => Beat == "E2b" && _log.VisiblePrompt.Contains("Space"));
            if (!_bot.Ok) yield break;
            yield return _log.Checkpoint("raise_hands_pay");
            _bot.Input.SimRaise = true;
            yield return _bot.Wait("pay_received", () => Beat == "E2c");
            _bot.Input.SimRaise = false;
            if (!_bot.Ok) yield break;
            // Ông Mai (EndingDirector.MaiIndex) không chào từ biệt: chào năm người ở lại trước, rồi mới tới nói chuyện với ông.
            foreach (int i in new[] { 0, 1, 2, 4, 5, EndingDirector.MaiIndex })
            {
                // Các điểm đứng chào tránh cầu thang, cowl và nắp hầm ở giữa boong.
                var routes = new[]
                {
                    new[] { V(-3.25f, -2.4f), V(-3.25f, 1f), V(-7.2f, 1f), ending.crew[0].Position + Vector3.forward * 1.05f },
                    new[] { V(-6.7f, 1f), V(-6.7f, 2.85f), ending.crew[1].Position + Vector3.forward * 1.05f },
                    new[] { V(-6.7f, 2.85f), V(-6.7f, 1f), V(-3.25f, 1f), V(-3.25f, -2.4f), ending.crew[2].Position + Vector3.right },
                    new[] { V(2.4f, -1.4f), ending.crew[3].Position + Vector3.right },
                    new[] { V(2.4f, -1.4f), ending.crew[4].Position + Vector3.back * 1.05f },
                    new[] { ending.crew[5].Position + Vector3.back * 1.05f }
                };
                foreach (var stand in routes[i])
                {
                    yield return _bot.Walk("farewell_" + i, stand);
                    if (!_bot.Ok) yield break;
                }
                int count = ending.Farewells;
                bool mai = i == EndingDirector.MaiIndex;
                yield return _bot.Press(ending.farewellTargets[i], "farewell_" + i, () => ending.Farewells > count,
                    mai ? "mrs.prompt.mai" : "mrs.prompt.bye", mai ? 150f : 20f);
                if (!_bot.Ok) yield break;
            }
            yield return _bot.Wait("gangway_open", () => Beat == "E3" && !ending.gangwayBarrier.activeSelf);
            if (!_bot.Ok) yield break;
            float top = GameObject.Find("Landing") != null ? 1.12f : 0f;
            var points = new List<Vector3> { V(0f, -1.15f) };
            if (top > 0f) for (int i = 0; i < 5; i++) points.Add(V(0f, -1.75f - 0.3f * i, top * (i + 1) / 5f));
            points.Add(V(0f, -3.2f, top));
            for (int i = 1; i <= 10; i++) points.Add(V(0f, -3.3f - 0.6f * (i - 1), Mathf.Lerp(top, -2.5f, i / 10f)));
            points.Add(ending.quayPoint.position);
            foreach (var point in points)
            {
                yield return _bot.Walk("gangway_to_quay", point);
                if (_bot.Ok) continue;
                foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (t.name == "GangwayBarrier" || t.name == "InnerRamp" || t.name == "Landing" || t.name == "InnerStep_0")
                        _log.Note($"Gangway diag: {t.name} active={t.gameObject.activeInHierarchy} pos={t.position} euler={t.eulerAngles} scale={t.lossyScale} collider={(t.GetComponent<Collider>() != null ? t.GetComponent<Collider>().bounds.ToString() : "none")}.");
                yield return _log.Checkpoint("gangway_diag");
                yield break;
            }
            yield return _bot.Wait("quay_trigger_entered", () => ending.quayTrigger.HasFired);
            if (!_bot.Ok) yield break;
            yield return _bot.Wait("quay_observe_active", () => Beat == "E3b");
            if (!_bot.Ok) yield break;
            yield return _bot.Walk("poor_porters", V(-2.2f, -12.6f, -2.5f));
            if (!_bot.Ok) yield break;
            yield return _bot.Wait("poor_porters_heard", () => Beat == "E4");
            if (!_bot.Ok) yield break;
            yield return _bot.Walk("street_edge", V(0f, -15f, -2.5f));
            if (!_bot.Ok) yield break;
            yield return _bot.Wait("tram_passed", () => ending.tram.Done && Beat == "E4b", 180f, 240f);
            if (!_bot.Ok) yield break;
            var stands = new[] { V(0f, -14.4f, -2.5f), V(-2.1f, -20.5f, -2.5f), V(-2f, -25.5f, -2.5f), V(-1.3f, -22f, -2.5f) };
            for (int i = 0; i < ending.newDiscoveries.Length; i++)
            {
                int seen = i;
                yield return _bot.Walk("marseille_discovery_" + i, stands[i]);
                if (!_bot.Ok) yield break;
                yield return _bot.Wait("marseille_discovery_ready_" + i, () => ending.newDiscoveries[seen].available);
                if (!_bot.Ok) yield break;
                yield return _bot.Press(ending.newDiscoveries[i], "marseille_look_" + i, () => ending.NewThingsSeen > seen, "prompt.story.look");
                if (!_bot.Ok) yield break;
            }
            yield return _bot.Wait("cafe_ready", () => ending.chair.available);
            if (!_bot.Ok) yield break;
            Assert.AreEqual(TrangPhuc.Thuong, _bot.Rig.body.Outfit);
            yield return _bot.Walk("cafe_chair", V(0.5f, -22.35f, -2.5f));
            if (!_bot.Ok) yield break;
            yield return _bot.Press(ending.chair, "sit_cafe", () => !_bot.Rig.ControlEnabled);
            if (!_bot.Ok) yield break;
            yield return _bot.Wait("link_notebook_open", () => GameRoot.Instance.UI.Notebook.IsOpen && GameRoot.Instance.UI.Notebook.Mode == NotebookMode.Link);
            if (!_bot.Ok) yield break;
            var target = GameRoot.Instance.Content.Notebook.LinkTarget;
            VisualElement a = null, b = null;
            yield return _bot.Wait("link_cards_laid_out", () =>
            {
                var cards = GameRoot.Instance.UI.Root.Query<VisualElement>(className: "hbt-link-card").ToList();
                a = cards.FirstOrDefault(c => (string)c.userData == target.A);
                b = cards.FirstOrDefault(c => (string)c.userData == target.B);
                return a != null && b != null && a.panel != null && b.panel != null && a.worldBound.width > 1f && b.worldBound.width > 1f;
            }, 30f, 60f);
            if (!_bot.Ok) yield break;
            yield return _log.Checkpoint("drag_notebook_pages");
            var from = a.worldBound.center; var to = b.worldBound.center;
            var picked = a.panel.Pick(from);
            Assert.IsTrue(picked == a || (picked != null && a.Contains(picked)), "Bot error: notebook source card is covered.");
            SendPointer(a, EventType.MouseDown, from);
            yield return _bot.Frame();
            var afterPress = a.worldBound.center;
            Assert.LessOrEqual(Vector2.Distance(from, afterPress), 5f, "Trang sổ nhảy khỏi con trỏ khi bắt đầu kéo.");
            // Follow the currently visible destination after the UI has processed the press.
            to = b.worldBound.center;
            for (int i = 1; i <= 10; i++) { SendPointer(a, EventType.MouseDrag, Vector2.Lerp(from, to, i / 10f)); yield return _bot.Frame(); }
            SendPointer(a, EventType.MouseUp, to);
            yield return _bot.Wait("notebook_pages_linked", () => ending.Link.Linked, 20f, 30f);
            if (!_bot.Ok) yield break;
            yield return _log.Checkpoint("pages_linked");
            yield return _bot.Wait("end_card", () => Beat == "End", 300f, 360f);
            yield return _log.Checkpoint("end_card");
        }

        IEnumerator ClickButton(string text, string step, string className = null)
        {
            Button button = null;
            yield return _bot.Wait("ui_button_" + step, () =>
            {
                button = GameRoot.Instance.UI.Root.Query<Button>(className: className).ToList().FirstOrDefault(b => b.text == text && b.enabledInHierarchy && b.resolvedStyle.display != DisplayStyle.None);
                return button != null && button.worldBound.width > 1f;
            }, 30f, 60f);
            if (!_bot.Ok) yield break;
            yield return _log.Checkpoint("ui_click_" + step);
            var picked = button.panel.Pick(button.worldBound.center);
            Assert.IsTrue(picked == button || (picked != null && button.Contains(picked)), "Bot error: UI button is covered or pointer coordinates are wrong: " + text);
            SendPointer(button, EventType.MouseDown, button.worldBound.center);
            yield return _bot.Frame();
            SendPointer(button, EventType.MouseUp, button.worldBound.center);
            yield return _bot.Frame();
        }

        static void SendPointer(VisualElement target, EventType type, Vector2 position)
        {
            var source = new Event { type = type, button = 0, mousePosition = position };
            if (type == EventType.MouseDown)
            { using (var e = PointerDownEvent.GetPooled(source)) { e.target = target; target.SendEvent(e); } }
            else if (type == EventType.MouseUp)
            { using (var e = PointerUpEvent.GetPooled(source)) { e.target = target; target.SendEvent(e); } }
            else
            { using (var e = PointerMoveEvent.GetPooled(source)) { e.target = target; target.SendEvent(e); } }
        }
    }
}
