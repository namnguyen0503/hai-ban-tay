using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace HaiBanTay.Shift
{
    /// <summary>Biển tên tàu bằng đồng ở hành lang (F01a, F10): chữ lấy từ ui_strings, vẽ một lần vào RenderTexture.
    /// Nhìn vào và nhấn E thì hiện thẻ tư liệu về con tàu.</summary>
    public sealed class ShipNameplate : MonoBehaviour, IInteractable
    {
        public Renderer surface;
        public string textKey = "ship.nameplate";
        public string cardId = "sc_ship";
        public string promptKey = "prompt.read_plate";
        public Color plate = new Color(0.42f, 0.3f, 0.12f, 1f);
        public Color letters = new Color(0.95f, 0.85f, 0.55f, 1f);
        public float glow = 0.35f;

        WorldPanel _panel;

        void Start()
        {
            _panel = WorldPanel.Create(transform, surface, 1024, 192, plate, glow, "Nameplate");
            if (_panel == null)
                return;
            var root = _panel.Root;
            root.style.justifyContent = Justify.Center;
            root.style.alignItems = Align.Center;
            var label = Ui.Text(null, root, GameRoot.Instance.Content.GetString(textKey));
            label.style.fontSize = 60;
            label.style.color = letters;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.letterSpacing = 2;
        }

        public string GetPromptKey(Interactor who) => promptKey;
        public bool CanInteract(Interactor who) => true;

        public void Interact(Interactor who)
        {
            GameRoot.Instance?.UI.Cards.Show(cardId);
        }

        void OnDestroy()
        {
            _panel?.Dispose();
        }
    }
}
