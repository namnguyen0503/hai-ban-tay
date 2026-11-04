using System.Collections.Generic;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Narrative;
using HaiBanTay.Notebook;
using UnityEngine;
using UnityEngine.UIElements;

namespace HaiBanTay.UI
{
    /// <summary>Gốc UI Toolkit xuyên cảnh. Thứ tự lớp (dưới → trên): viền cơ thể, letterbox, HUD (ca làm, prompt, thẻ),
    /// sổ, phụ đề, màn chữ, nút cảm ứng, menu chính, màn đen, tạm dừng, cài đặt, debug, ghi chú debug. HUD, sổ và thẻ nằm trong vùng an toàn 5%;
    /// letterbox và màn đen phủ tràn viền.</summary>
    [DefaultExecutionOrder(20000)]
    public sealed class UIRoot : MonoBehaviour
    {
        public const float SafeAreaPercent = 5f;

        VisualElement _fade;
        VisualElement _safeFrame;
        Label _fps;
        UIDocument _document;
        PanelSettings _panel;
        readonly List<VisualElement> _safeLayers = new List<VisualElement>();
        Rect _lastSafeArea;
        Vector2Int _lastScreen;

        public VisualElement Root { get; private set; }
        public PromptView Prompt { get; private set; }
        /// <summary>Chỉ dẫn từng bước: dòng việc tiếp theo và mốc chỉ chỗ cần tới.</summary>
        public GuideView Guide { get; private set; }
        public ShiftHud Shift { get; private set; }
        public MainMenuView Menu { get; private set; }
        public SettingsView Settings { get; private set; }
        public HelpView Help { get; private set; }
        public SubtitleView Subtitles { get; private set; }
        public ToastView Toast { get; private set; }
        public SourceCardView Cards { get; private set; }
        public NotebookView Notebook { get; private set; }
        public OpeningCard Card { get; private set; }
        public MapTransitionView Map { get; private set; }
        public LetterboxView Letterbox { get; private set; }
        public ScreenTint Tint { get; private set; }
        public PauseMenu Pause { get; private set; }
        public VisualElement PauseLayer { get; private set; }
        /// <summary>Nút cảm ứng (điện thoại): trên HUD/sổ, dưới menu và tạm dừng.</summary>
        public VisualElement TouchLayer { get; private set; }
        /// <summary>Màn ghi chú debug (phím /): trên cùng.</summary>
        public VisualElement NoteLayer { get; private set; }
        public float FadeAlpha { get; private set; }

        public void Initialize(GameConfig config, ContentDatabase content)
        {
            var doc = gameObject.AddComponent<UIDocument>();
            _document = doc;
            _panel = config != null && config.panelSettings != null ? Instantiate(config.panelSettings) : null;
            doc.panelSettings = _panel;
            Root = doc.rootVisualElement;
            if (Root == null)
            {
                Debug.LogError("UIRoot: UIDocument không tạo được rootVisualElement (thiếu PanelSettings?)");
                return;
            }
            Root.pickingMode = PickingMode.Ignore;
            Root.style.flexGrow = 1;

            var tintLayer = Layer(false);
            Tint = new ScreenTint(tintLayer);
            Letterbox = new LetterboxView(Layer(false));

            var hud = Layer();
            Shift = new ShiftHud(hud);
            Guide = new GuideView(hud) { DistanceFormat = content != null ? content.GetString("hud.guide_distance") : "{0}" };
            Prompt = new PromptView(hud);
            Toast = new ToastView(hud);
            Cards = new SourceCardView(hud, content);

            Notebook = new NotebookView(Layer(), config != null ? config.serifFont : null);
            // Phụ đề nằm trên sổ: B2.6 Jacques dạy từ khi sổ ghép từ đang mở.
            Subtitles = new SubtitleView(Layer());

            var cards = Layer(false);
            Map = new MapTransitionView(cards);
            Card = new OpeningCard(cards);

            TouchLayer = Layer();

            Menu = new MainMenuView(Layer(), content);

            _fade = Layer(false);
            _fade.style.backgroundColor = Color.black;
            _fade.style.opacity = 0f;

            PauseLayer = Layer();
            Settings = new SettingsView(Layer(), content);
            Help = new HelpView(Layer(), content);

            var debug = Layer();
            _fps = Ui.Text(null, debug);
            _fps.style.position = Position.Absolute;
            _fps.style.left = new Length(SafeAreaPercent, LengthUnit.Percent);
            _fps.style.top = new Length(SafeAreaPercent, LengthUnit.Percent);
            _fps.style.fontSize = 30;
            _fps.style.color = Color.white;
            _fps.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);
            Ui.Show(_fps, false);

            _safeFrame = Ui.Box(null, debug);
            _safeFrame.style.position = Position.Absolute;
            var p = new Length(SafeAreaPercent, LengthUnit.Percent);
            _safeFrame.style.left = _safeFrame.style.right = _safeFrame.style.top = _safeFrame.style.bottom = p;
            _safeFrame.style.borderLeftWidth = _safeFrame.style.borderRightWidth =
                _safeFrame.style.borderTopWidth = _safeFrame.style.borderBottomWidth = 3f;
            var red = new Color(1f, 0.2f, 0.2f, 0.9f);
            _safeFrame.style.borderLeftColor = _safeFrame.style.borderRightColor =
                _safeFrame.style.borderTopColor = _safeFrame.style.borderBottomColor = red;
            Ui.Show(_safeFrame, false);

            NoteLayer = Layer();
            ApplySafeArea(Screen.safeArea, Screen.width, Screen.height);
        }

        VisualElement Layer(bool safe = true)
        {
            var e = Ui.Box("hbt-layer", Root);
            if (safe)
                _safeLayers.Add(e);
            return e;
        }

        /// <summary>Screen.safeArea có gốc trái dưới; UI Toolkit có gốc trái trên. Dùng phần trăm để giữ đúng khi đổi cỡ UI.</summary>
        public void ApplySafeArea(Rect safe, int width, int height)
        {
            if (width <= 0 || height <= 0)
                return;
            _lastSafeArea = safe;
            _lastScreen = new Vector2Int(width, height);
            foreach (var layer in _safeLayers)
            {
                layer.style.left = new Length(safe.xMin / width * 100f, LengthUnit.Percent);
                layer.style.right = new Length((width - safe.xMax) / width * 100f, LengthUnit.Percent);
                layer.style.top = new Length((height - safe.yMax) / height * 100f, LengthUnit.Percent);
                layer.style.bottom = new Length(safe.yMin / height * 100f, LengthUnit.Percent);
            }
        }

        void LateUpdate()
        {
            if (Root == null)
                return;
            // Panel đích của test có kích thước riêng; safe area mô phỏng do test truyền vào.
            if (_document.panelSettings.targetTexture == null &&
                (_lastSafeArea != Screen.safeArea || _lastScreen != new Vector2Int(Screen.width, Screen.height)))
                ApplySafeArea(Screen.safeArea, Screen.width, Screen.height);
            // Xếp hàng sau Update/coroutine phát thoại để không chồng UI trong khung thoại vừa bắt đầu.
            Toast.SetSuppressed(Subtitles.Visible);
            Cards.SetSuppressed(Subtitles.Visible || Toast.Pending);
            Prompt.SetSuppressed(Subtitles.Visible || Toast.Pending || Cards.Pending);
            Prompt.Tick();
            TickGuide();
            Toast.Tick();
            Cards.Tick();
            Tint.Tick();
        }

        void TickGuide()
        {
            var root = GameRoot.Instance;
            var rig = Player.PlayerRig.Current;
            // Phim, sổ, tạm dừng: ẩn chỉ dẫn; mở lại khi người chơi điều khiển được.
            bool playing = root != null && root.Input.GameplayEnabled && rig != null && rig.cam != null && FadeAlpha < 0.5f;
            Guide.SetHidden(!playing);
            // Mũi tên đường đi (sang phòng khác) đang hiện thì mốc nhường chỗ: mốc xuyên vách dễ làm đi lạc.
            if (playing)
                Guide.Tick(rig.cam, Shift.PointerVisible ||
                    (Guide.Target != null && rig.interactor != null && rig.interactor.IsLookingAt(Guide.Target)));
        }

        /// <summary>Dọn mọi thứ gắn với cảnh cũ khi chuyển cảnh.</summary>
        public void ResetTransient()
        {
            if (Root == null)
                return;
            Prompt.Clear();
            Guide.Clear();
            Prompt.SetCrosshair(true);
            Shift.Clear();
            Toast.Hide();
            Menu.Hide();
            Prompt.SetScenePrompt(null);
            Subtitles.Hide();
            Cards.Clear();
            Notebook.Close();
            Notebook.AllowToggle = true;
            Card.Hide();
            Map.Hide();
            Letterbox.Set(0f);
            Tint.Clear();
        }

        /// <summary>Áp cài đặt hiển thị; phóng panel không thay đổi độ phân giải camera.</summary>
        public void ApplySettings(GameSettings settings)
        {
            if (Root == null)
                return;
            _document.panelSettings.scale = settings.UiScale;
            Subtitles.ApplySettings(settings);
        }

        void OnDestroy()
        {
            if (_panel != null)
                Destroy(_panel);
        }

        public void SetPauseMenu(PauseMenu menu)
        {
            Pause = menu;
        }

        public void SetFade(float alpha)
        {
            FadeAlpha = alpha;
            if (_fade != null)
                _fade.style.opacity = alpha;
        }

        public void SetFpsVisible(bool visible) => Ui.Show(_fps, visible);

        public void SetFpsText(string text)
        {
            if (_fps != null)
                _fps.text = text;
        }

        public bool SafeFrameVisible => _safeFrame != null && _safeFrame.style.display != DisplayStyle.None;

        public void ToggleSafeFrame()
        {
            Ui.Show(_safeFrame, !SafeFrameVisible);
        }
    }
}
