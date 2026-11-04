using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Shift;
using HaiBanTay.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace HaiBanTay.Ending
{
    /// <summary>Sổ lương trên bàn: chữ Việt từ JSON, cùng WorldPanel như bảng phấn của bếp.</summary>
    public sealed class MarseilleLedger : MonoBehaviour, IInteractable
    {
        public Renderer surface;
        public bool available;
        WorldPanel _panel;

        void Start()
        {
            _panel = WorldPanel.Create(transform, surface, 1024, 512, new Color(0.86f, 0.8f, 0.65f), 0.25f, "SalaryLedger");
            if (_panel == null)
                return;
            var root = _panel.Root;
            root.style.justifyContent = Justify.Center;
            root.style.paddingLeft = root.style.paddingRight = 32;
            var text = Ui.Text(null, root, GameRoot.Instance.Content.GetString("mrs.ledger"));
            text.style.fontSize = 48;
            text.style.color = new Color(0.12f, 0.1f, 0.07f);
            text.style.whiteSpace = WhiteSpace.Normal;
        }

        public string GetPromptKey(Interactor who) => available ? "mrs.prompt.ledger" : null;
        public bool CanInteract(Interactor who) => available;
        public void Interact(Interactor who)
        {
            if (available)
                GameRoot.Instance.UI.Cards.Show("sc_mrs_pay_nominal");
        }
        void OnDestroy() => _panel?.Dispose();
    }
}
