using HaiBanTay.Interaction;
using UnityEngine;

namespace HaiBanTay.Tasks
{
    /// <summary>Vật thuộc một tác vụ (thùng than, lò, nồi…): prompt và thao tác E do tác vụ quyết định.</summary>
    public sealed class TaskTarget : MonoBehaviour, IInteractable
    {
        public TaskBase task;
        public string id;

        public string GetPromptKey(Interactor who) =>
            task != null && task.State == TaskState.Active ? task.PromptFor(this, who) : null;

        public bool CanInteract(Interactor who) =>
            task != null && task.State == TaskState.Active && task.CanInteract(this, who);

        public void Interact(Interactor who)
        {
            task.OnInteract(this, who);
        }
    }
}
