namespace HaiBanTay.Interaction
{
    /// <summary>Vật có prompt khi người chơi nhìn vào. Trả về khóa ui_strings, hoặc null nếu không hiện gì.</summary>
    public interface IPromptProvider
    {
        string GetPromptKey(Interactor who);
    }

    /// <summary>Vật nhận thao tác E / nhấn chuột trái.</summary>
    public interface IInteractable : IPromptProvider
    {
        bool CanInteract(Interactor who);
        void Interact(Interactor who);
    }
}
