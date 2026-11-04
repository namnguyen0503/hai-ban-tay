using HaiBanTay.Audio;
using HaiBanTay.Content;
using HaiBanTay.DevTools;
using HaiBanTay.Narrative;
using HaiBanTay.Notebook;
using HaiBanTay.Performance;
using HaiBanTay.Player;
using HaiBanTay.UI;
using UnityEngine;

namespace HaiBanTay.Core
{
    /// <summary>Tạo các service theo thứ tự và giữ tham chiếu. Không chứa logic gameplay.
    /// Luôn tồn tại: tự tạo trước khi nạp cảnh đầu tiên (mở thẳng cảnh nào trong Editor cũng chạy được).</summary>
    public sealed class GameRoot : MonoBehaviour
    {
        public static GameRoot Instance { get; private set; }

        public GameConfig Config { get; private set; }
        public ContentDatabase Content { get; private set; }
        public GameEvents Events { get; private set; }
        public GameFlags Flags { get; private set; }
        public GameState State { get; private set; }
        public GameInput Input { get; private set; }
        public AudioService Audio { get; private set; }
        public QualityTierManager Quality { get; private set; }
        public DisplaySetup Display { get; private set; }
        public UIRoot UI { get; private set; }
        public NotebookController Notebook { get; private set; }
        public SubtitlePlayer Subtitles { get; private set; }
        public SceneFlow Scenes { get; private set; }
        public bool Ready { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            EnsureExists();
        }

        public static GameRoot EnsureExists()
        {
            if (Instance != null)
                return Instance;
            var go = new GameObject("GameRoot");
            return go.AddComponent<GameRoot>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Initialize();
        }

        void Initialize()
        {
            Config = Resources.Load<GameConfig>(GameConfig.ResourcePath);
            if (Config == null)
                Debug.LogError("GameRoot: thiếu Resources/GameConfig.asset. Chạy menu HaiBanTay/Setup/Run All.");

            try
            {
                Content = ContentDatabase.LoadFromResources();
            }
            catch (ContentException e)
            {
                Debug.LogError("GameRoot: không nạp được nội dung. " + e.Message);
                enabled = false;
                return;
            }

            GameSettings.Load();
            Events = new GameEvents();
            Flags = new GameFlags();
            State = new GameState();

            Input = gameObject.AddComponent<GameInput>();
            Input.Initialize(Config != null ? Config.input : null);

            Audio = gameObject.AddComponent<AudioService>();
            Audio.Initialize(Config != null ? Config.audio : null);

            Quality = gameObject.AddComponent<QualityTierManager>();
            Display = gameObject.AddComponent<DisplaySetup>();
            Quality.TierChanged += Display.Apply;
            Quality.Initialize();

            UI = gameObject.AddComponent<UIRoot>();
            UI.Initialize(Config, Content);

            Notebook = new NotebookController(Content, Events, Flags);
            UI.Notebook.Bind(Notebook, Content, Input);
            Notebook.PageUnlocked += page => UI.Toast.Show(Content.GetString("notebook.new_page") + ": " + page.Title);

            Subtitles = gameObject.AddComponent<SubtitlePlayer>();
            Subtitles.Initialize(Content, Audio, UI.Subtitles);

            Scenes = gameObject.AddComponent<SceneFlow>();
            Scenes.Initialize(this);

            var pause = gameObject.AddComponent<PauseController>();
            pause.Initialize(Input, UI, Scenes);

            var fps = gameObject.AddComponent<FpsOverlay>();
            fps.Initialize(UI, Quality, Content);
            var perf = gameObject.AddComponent<PerfRecorder>();
            perf.Initialize(Quality);
            var keys = gameObject.AddComponent<DebugHotkeys>();
            keys.Initialize(UI, Scenes, Quality, fps, perf);
            // Ghi chú debug (phím /) có ở mọi bản; nút cảm ứng chỉ bật trên điện thoại.
            var notes = gameObject.AddComponent<DebugNotes>();
            notes.Initialize(Input, UI, Content);
            if (TouchControls.ShouldEnable())
            {
                var touch = gameObject.AddComponent<TouchControls>();
                touch.Initialize(Input, UI, Scenes, Content, notes.Open, () => DebugNotes.AnyOpen);
            }
            GameSettings.Changed += ApplySettings;
            ApplySettings();
            Ready = true;
        }

        void ApplySettings()
        {
            var s = GameSettings.Current;
            Audio?.ApplySettings(s);
            UI?.ApplySettings(s);
            Quality?.Apply(s.Tier);
            if (Quality != null)
                FrameCap.Apply(Quality.Current);
        }

        /// <summary>Xóa tiến trình chơi (dùng khi nhảy cảnh bằng phím demo hoặc giữa các test).</summary>
        public void ResetProgress()
        {
            Events.Reset();
            Flags.Reset();
            State.Reset();
            Notebook.Reset();
        }

        void OnDestroy()
        {
            GameSettings.Changed -= ApplySettings;
            if (Instance == this)
                Instance = null;
        }
    }
}
