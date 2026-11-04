using System;
using UnityEngine;

namespace HaiBanTay.Interaction
{
    /// <summary>Đích tương tác chung (cửa, ghế, ghế đẩu…): hiện prompt, phát sự kiện C# khi nhấn E.</summary>
    public sealed class InteractTarget : MonoBehaviour, IInteractable
    {
        public string promptKey;
        public bool available;

        public event Action<Interactor> Interacted;

        public string GetPromptKey(Interactor who) => available ? promptKey : null;
        public bool CanInteract(Interactor who) => available;

        public void Interact(Interactor who)
        {
            Interacted?.Invoke(who);
        }
    }
}
