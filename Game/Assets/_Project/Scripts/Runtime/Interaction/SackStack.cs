using System;
using UnityEngine;

namespace HaiBanTay.Interaction
{
    /// <summary>
    /// Chỗ xếp bao khoai ở hầm thực phẩm (cảnh 01, F03): nhận lần lượt từng bao vào các điểm xếp (anchors), một vùng nhìn
    /// chung cho cả đống nên người chơi không phải ngắm đúng từng ô như DropZone. Đủ bao thì phát Completed.
    /// </summary>
    public sealed class SackStack : MonoBehaviour, IInteractable
    {
        public string acceptKind = "sack";
        public Transform[] anchors = new Transform[0];

        public int Count { get; private set; }
        public int Capacity => anchors.Length;
        public bool Full => Count >= anchors.Length;
        public event Action<Holdable> Placed;

        public bool Accepts(Holdable h) => h != null && !Full && (string.IsNullOrEmpty(acceptKind) || h.kind == acceptKind);

        public string GetPromptKey(Interactor who) => who != null && Accepts(who.Holding) ? "prompt.drop" : null;
        public bool CanInteract(Interactor who) => who != null && Accepts(who.Holding);

        public void Interact(Interactor who)
        {
            if (Accepts(who.Holding))
                Place(who.ReleaseHeld());
        }

        public bool Place(Holdable h)
        {
            if (!Accepts(h))
                return false;
            var t = anchors[Count++];
            h.Detach(t.position, t.rotation, physics: false);
            h.Locked = true;
            Placed?.Invoke(h);
            return true;
        }
    }
}
