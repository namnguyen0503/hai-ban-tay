using System.Collections.Generic;
using HaiBanTay.Player;
using UnityEngine;

namespace HaiBanTay.Interaction
{
    /// <summary>Móc treo dụng cụ (xẻng than cạnh thùng than): E để lấy, E để treo lại. Cầm dụng cụ thì hai tay bận,
    /// phải treo lại mới vác được bao khoai hay rửa nồi.</summary>
    public sealed class ToolHook : MonoBehaviour, IInteractable
    {
        public Tool tool = Tool.Shovel;
        public GameObject visual;
        public string takeKey = "prompt.take_shovel";
        public string hangKey = "prompt.hang_shovel";

        bool _hanging = true;
        static readonly List<ToolHook> Hooks = new List<ToolHook>();

        void OnEnable() => Hooks.Add(this);
        void OnDisable() => Hooks.Remove(this);

        /// <summary>Móc của dụng cụ đang cầm (để treo lại tự động khi người chơi nhấc đồ).</summary>
        public static ToolHook For(Tool tool)
        {
            if (tool == Tool.None)
                return null;
            foreach (var h in Hooks)
                if (h.tool == tool && !h._hanging)
                    return h;
            return null;
        }

        public bool Hanging => _hanging;

        public string GetPromptKey(Interactor who)
        {
            if (who == null)
                return null;
            if (_hanging)
                return who.HandsFree || CanSwap(who) ? takeKey : null;
            return who.Tool == tool ? hangKey : null;
        }

        public bool CanInteract(Interactor who) => GetPromptKey(who) != null;

        public void Interact(Interactor who)
        {
            if (_hanging)
            {
                // Đang cầm dụng cụ khác (cào, chổi): treo nó về móc của nó rồi cầm dụng cụ này.
                if (CanSwap(who))
                    For(who.Tool)?.ReturnFrom(who);
                if (who.TakeTool(tool))
                    SetHanging(false);
            }
            else if (who.Tool == tool)
            {
                who.ReleaseTool();
                SetHanging(true);
            }
        }

        static bool CanSwap(Interactor who) => who.Holding == null && !who.WorkCarrying && who.Tool != Tool.None && For(who.Tool) != null;

        /// <summary>Hết ca / chuyển cảnh: dụng cụ về móc.</summary>
        public void ReturnFrom(Interactor who)
        {
            if (who != null && who.Tool == tool)
                who.ReleaseTool();
            SetHanging(true);
        }

        void SetHanging(bool hanging)
        {
            _hanging = hanging;
            if (visual != null)
                visual.SetActive(hanging);
        }
    }
}
