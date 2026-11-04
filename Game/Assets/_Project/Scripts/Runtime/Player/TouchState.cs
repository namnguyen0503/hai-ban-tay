using UnityEngine;

namespace HaiBanTay.Player
{
    /// <summary>Nút giữ cảm ứng: nhiều ngón không đảo hai lần; mất focus/chuyển cảnh xóa cả chốt.</summary>
    public sealed class TouchHold
    {
        int _down;
        bool _latched;
        public void Press(bool toggle)
        {
            if (_down++ == 0 && toggle)
                _latched = !_latched;
        }
        public void Release() => _down = Mathf.Max(0, _down - 1);
        public bool Held(bool toggle) => toggle ? _latched : _down > 0;
        public void Clear() { _down = 0; _latched = false; }
    }

    public enum TouchButton
    {
        Interact,
        Notebook,
        Pause,
        Sit,
        Count
    }

    /// <summary>Trạng thái điều khiển cảm ứng. UI.TouchControls ghi vào phần "đang chờ" bất cứ lúc nào trong khung;
    /// GameInput.Latch (đầu khung, trước gameplay) chốt thành giá trị đọc được suốt khung đó.
    /// Nút bấm nhả = đúng một khung true, như WasPressedThisFrame.</summary>
    public sealed class TouchState
    {
        public Vector2 Move;
        public bool Use;
        public bool Raise;
        public readonly TouchHold UseHold = new TouchHold();
        public readonly TouchHold RaiseHold = new TouchHold();

        /// <summary>Độ dời nhìn khung này, cùng đơn vị với chuột (pixel ở 1080p).</summary>
        public Vector2 Look { get; private set; }

        Vector2 _pendingLook;
        readonly bool[] _pending = new bool[(int)TouchButton.Count];
        readonly int[] _frame = new int[(int)TouchButton.Count];

        public TouchState()
        {
            for (int i = 0; i < _frame.Length; i++)
                _frame[i] = -1;
        }

        public void AddLook(Vector2 delta) => _pendingLook += delta;

        public void Press(TouchButton button) => _pending[(int)button] = true;

        public bool Pressed(TouchButton button) => _frame[(int)button] == Time.frameCount;

        public void Latch()
        {
            Look = _pendingLook;
            _pendingLook = Vector2.zero;
            for (int i = 0; i < _pending.Length; i++)
            {
                if (!_pending[i])
                    continue;
                _pending[i] = false;
                _frame[i] = Time.frameCount;
            }
        }

        public void Clear()
        {
            Move = Vector2.zero;
            Use = Raise = false;
            UseHold.Clear();
            RaiseHold.Clear();
            _pendingLook = Vector2.zero;
            Look = Vector2.zero;
            for (int i = 0; i < _pending.Length; i++)
                _pending[i] = false;
        }
    }
}
