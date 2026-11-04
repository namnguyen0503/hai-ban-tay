using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HaiBanTay.Player
{
    /// <summary>Đọc HaiBanTay.inputactions (PLAN mục 7.1). Gom các lý do chặn điều khiển (sổ, tạm dừng, phim)
    /// và khóa/mở chuột theo đó. Các trường Sim* cho test PlayMode giả lập thao tác.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameInput : MonoBehaviour
    {
        InputActionMap _map;
        InputAction _move, _look, _interact, _use, _raise, _notebook, _pause, _sit;
        readonly HashSet<string> _blocks = new HashSet<string>();
        bool _simInteract;
        // Tạm dừng, sổ quan sát, ghi chú debug chỉ phủ lên màn chơi: việc đang giữ chuột (xẻng đầy than, dao đang kéo, chảo đang
        // kéo) đứng yên chứ không coi là nhả tay (QA-0027). Phim, menu thì vẫn coi là nhả như cũ.
        static readonly string[] Overlays = { "pause", "notebook", "debug_note" };
        bool _heldUnderOverlay;

        /// <summary>Điều khiển cảm ứng (UI.TouchControls) ghi vào đây; cộng với bàn phím/chuột như Sim*.</summary>
        public readonly TouchState Touch = new TouchState();

        public Vector2 SimMove;
        public Vector2 SimLook;
        public bool SimUse;
        public bool SimRaise;

        /// <summary>false khi có sổ, menu tạm dừng hoặc đoạn phim đang chặn điều khiển.</summary>
        public bool GameplayEnabled => _blocks.Count == 0;
        public bool IsBlockedBy(string reason) => _blocks.Contains(reason);

        public void Initialize(InputActionAsset asset)
        {
            if (asset == null)
            {
                Debug.LogError("GameInput: thiếu InputActionAsset trong GameConfig.");
                return;
            }
            _map = Instantiate(asset).FindActionMap("Player", true);
            _move = _map.FindAction("Move", true);
            _look = _map.FindAction("Look", true);
            _interact = _map.FindAction("Interact", true);
            _use = _map.FindAction("Use", true);
            _raise = _map.FindAction("Raise", true);
            _notebook = _map.FindAction("Notebook", true);
            _pause = _map.FindAction("Pause", true);
            _sit = _map.FindAction("Sit", false);
            _map.Enable();
            UpdateCursor();
        }

        void OnDestroy()
        {
            _map?.Disable();
        }

        public Vector2 Move
        {
            get
            {
                if (!GameplayEnabled)
                    return Vector2.zero;
                var v = (_move != null ? _move.ReadValue<Vector2>() : Vector2.zero) + SimMove + Touch.Move;
                return Vector2.ClampMagnitude(v, 1f);
            }
        }

        /// <summary>Độ dời chuột khung này (pixel). Bằng 0 khi điều khiển bị chặn.</summary>
        public Vector2 Look => GameplayEnabled ? LookRaw : Vector2.zero;

        public Vector2 LookRaw => (_look != null ? _look.ReadValue<Vector2>() : Vector2.zero) + SimLook + Touch.Look;

        public bool UseHeld => GameplayEnabled ? LiveUse : _heldUnderOverlay && OnlyOverlays;

        bool LiveUse => (_use != null && _use.IsPressed()) || SimUse || Touch.Use;

        public bool OnlyOverlays
        {
            get
            {
                foreach (var b in _blocks)
                    if (System.Array.IndexOf(Overlays, b) < 0)
                        return false;
                return _blocks.Count > 0;
            }
        }

        /// <summary>Giơ hai bàn tay: Space hoặc chuột trái + chuột phải. Không bị chặn bởi phim (B1.2, E2 cần).</summary>
        public bool RaiseHeld => (_raise != null && _raise.IsPressed()) || SimRaise || Touch.Raise;

        public bool NotebookPressed => (_notebook != null && _notebook.WasPressedThisFrame()) || Touch.Pressed(TouchButton.Notebook);
        public bool PausePressed => (_pause != null && _pause.WasPressedThisFrame()) || Touch.Pressed(TouchButton.Pause);
        /// <summary>C: ngồi xuống / đứng dậy.</summary>
        public bool SitPressed => GameplayEnabled && ((_sit != null && _sit.WasPressedThisFrame()) || ConsumeSimSit() || Touch.Pressed(TouchButton.Sit));
        public bool SimSit;

        bool ConsumeSimSit()
        {
            bool v = SimSit;
            SimSit = false;
            return v;
        }

        /// <summary>E hoặc nhấn chuột trái. Chỉ Interactor gọi (mỗi khung một lần).</summary>
        public bool ConsumeInteract()
        {
            if (!GameplayEnabled)
            {
                _simInteract = false;
                return false;
            }
            bool sim = _simInteract;
            _simInteract = false;
            return sim || (_interact != null && _interact.WasPressedThisFrame()) || Touch.Pressed(TouchButton.Interact);
        }

        // Chạy trước mọi script gameplay: chốt thao tác cảm ứng của khung trước thành giá trị khung này.
        void Update()
        {
            Touch.Latch();
        }

        public void SimulateInteract()
        {
            _simInteract = true;
        }

        public void Block(string reason)
        {
            if (_blocks.Count == 0)
                _heldUnderOverlay = LiveUse;
            _blocks.Add(reason);
            UpdateCursor();
        }

        public void Unblock(string reason)
        {
            _blocks.Remove(reason);
            UpdateCursor();
        }

        public void ClearBlocks()
        {
            _blocks.Clear();
            SimMove = SimLook = Vector2.zero;
            SimUse = SimRaise = false;
            Touch.Clear();
            UpdateCursor();
        }

        /// <summary>Chuột hiện khi có màn cần trỏ (sổ, tạm dừng); còn lại khóa giữa màn hình.</summary>
        public bool CursorFree { get; private set; }

        public void SetCursorFree(bool free)
        {
            CursorFree = free;
            UpdateCursor();
        }

        void UpdateCursor()
        {
            Cursor.lockState = CursorFree ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = CursorFree;
        }
    }
}
