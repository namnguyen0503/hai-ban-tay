using System;
using UnityEngine;

namespace HaiBanTay.Interaction
{
    /// <summary>Vùng thả: nhận Holdable đúng loại, đặt vào điểm neo, phát Placed.</summary>
    public sealed class DropZone : MonoBehaviour, IInteractable
    {
        public string acceptKind;
        public Transform anchor;
        public bool lockOnPlace = true;

        public Holdable Contents { get; private set; }
        public event Action<Holdable> Placed;

        public bool Accepts(Holdable h)
        {
            return h != null && Contents == null && (string.IsNullOrEmpty(acceptKind) || h.kind == acceptKind);
        }

        public string GetPromptKey(Interactor who) => who != null && Accepts(who.Holding) ? "prompt.drop" : null;
        public bool CanInteract(Interactor who) => who != null && Accepts(who.Holding);

        public void Interact(Interactor who)
        {
            if (!Accepts(who.Holding))
                return;
            Place(who.ReleaseHeld());
        }

        /// <summary>Lấy vật ra khỏi vùng (đầu bếp đã dùng bao khoai) để nhận vật tiếp theo.</summary>
        public Holdable Take()
        {
            var h = Contents;
            Contents = null;
            if (h != null)
                h.Locked = false;
            return h;
        }

        public bool Place(Holdable h)
        {
            if (!Accepts(h))
                return false;
            var t = anchor != null ? anchor : transform;
            h.Detach(t.position, t.rotation, physics: false);
            Contents = h;
            if (lockOnPlace)
                h.Locked = true;
            Placed?.Invoke(h);
            return true;
        }
    }
}
