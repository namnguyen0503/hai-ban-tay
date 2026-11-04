using System;
using System.Collections.Generic;
using HaiBanTay.Core;
using HaiBanTay.Player;
using UnityEngine;

namespace HaiBanTay.Interaction
{
    /// <summary>Raycast từ tâm màn hình, tầm 2 m; chọn vật có prompt gần nhất; gửi prompt lên UI;
    /// E / nhấn chuột trái thì tương tác; giữ một Holdable.</summary>
    public sealed class Interactor : MonoBehaviour
    {
        public Camera cam;
        public float range = 2f;
        public LayerMask mask = ~(1 << 2); // bỏ layer Ignore Raycast
        public FirstPersonMotor motor;
        public HandsRig hands;
        public Transform holdOneHand;
        public Transform holdTwoHands;
        public Transform holdRightShoulder;
        /// <summary>Điểm xách bên hông phải (theo hướng thân, không theo pitch camera); tạo lúc chạy, PlayerBody đặt vị trí.</summary>
        public Transform holdHang;

        readonly RaycastHit[] _hits = new RaycastHit[16];
        readonly Dictionary<Collider, IPromptProvider> _cache = new Dictionary<Collider, IPromptProvider>();
        readonly Dictionary<Collider, bool> _npc = new Dictionary<Collider, bool>();
        GameRoot _root;
        string _shownKey;

        public Holdable Holding { get; private set; }
        /// <summary>Dụng cụ đang cầm (xẻng…), lấy từ ToolHook.</summary>
        public Tool Tool { get; private set; }
        public bool WorkCarrying
        {
            get => _workCarrying;
            set { if (_workCarrying != value) { _workCarrying = value; HandsVersion++; } }
        }
        bool _workCarrying;
        /// <summary>Tăng mỗi lần đồ trên tay đổi (nhặt, buông, cầm/trả dụng cụ, bê việc): hướng dẫn làm mới ngay, không chờ nhịp 0,2 s (QA-0070).</summary>
        public int HandsVersion { get; private set; }
        /// <summary>Hai tay trống: không cầm đồ, không cầm dụng cụ.</summary>
        public bool HandsFree => Holding == null && Tool == Tool.None && !WorkCarrying;
        public IPromptProvider Current { get; private set; }
        public Collider LookCollider { get; private set; }
        public Vector3 LookPoint { get; private set; }

        public event Action<Holdable> Picked;

        void Awake()
        {
            if (holdHang == null)
            {
                holdHang = new GameObject("HoldHang").transform;
                holdHang.SetParent(transform, false);
                holdHang.localPosition = new Vector3(0.3f, 0.8f, 0f);
            }
            // Đồ ôm trước ngực đi theo khung tay (HandsRig hãm góc cúi/ngẩng), không chúi vào bụng khi nhìn xuống chân.
            if (hands != null)
            {
                if (holdOneHand != null && holdOneHand.parent != hands.transform) holdOneHand.SetParent(hands.transform, true);
                if (holdTwoHands != null && holdTwoHands.parent != hands.transform) holdTwoHands.SetParent(hands.transform, true);
            }
        }

        void Start()
        {
            _root = GameRoot.Instance;
        }

        void OnDisable()
        {
            ClearPrompt();
        }

        void Update()
        {
            if (cam == null)
                return;
            Scan(new Ray(cam.transform.position, cam.transform.forward));

            // Đang đổ (vật bận) thì E không làm gì: không hiện gợi ý.
            bool busy = Holding != null && Holding.Busy;
            string key = busy ? null : Current?.GetPromptKey(this);
            if (key == null && Holding != null && !busy)
                key = "prompt.drop";
            ShowPrompt(key);

            if (_root != null && _root.Input.ConsumeInteract())
                TryInteract();
        }

        /// <summary>Chọn vật gần nhất dọc tia. Vật đặc không có prompt chắn tầm nhìn; trigger đang không có prompt bị bỏ qua.</summary>
        public void Scan(Ray ray)
        {
            Current = null;
            LookCollider = null;
            int count = Physics.RaycastNonAlloc(ray, _hits, range, mask, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.distance >= best || hit.collider.transform.IsChildOf(transform))
                    continue;
                var provider = Resolve(hit.collider);
                // Trigger (vùng đặt đồ) chỉ chặn tia khi đang có việc cho người chơi; không thì nhìn xuyên tới vật bên trong
                // (xô nằm trong vùng đặt xô vẫn xách được).
                if (hit.collider.isTrigger && (provider == null || provider.GetPromptKey(this) == null))
                    continue;
                // Người (NPC) không có việc cho người chơi không chắn tia: đứng sát chỗ làm cũng không khóa việc.
                if (provider == null && IsNpc(hit.collider))
                    continue;
                best = hit.distance;
                Current = provider;
                LookCollider = hit.collider;
                LookPoint = hit.point;
            }
        }

        bool IsNpc(Collider c)
        {
            if (!_npc.TryGetValue(c, out bool npc))
            {
                npc = c.GetComponentInParent<HaiBanTay.Npc.NpcAgent>() != null;
                _npc[c] = npc;
            }
            return npc;
        }

        IPromptProvider Resolve(Collider c)
        {
            if (!_cache.TryGetValue(c, out var p))
            {
                p = c.GetComponentInParent<IPromptProvider>();
                _cache[c] = p;
            }
            return p;
        }

        /// <summary>Nhìn trúng component này (hoặc con của nó)?</summary>
        public bool IsLookingAt(Component target)
        {
            if (LookCollider == null || target == null)
                return false;
            var t = LookCollider.transform;
            return t == target.transform || t.IsChildOf(target.transform);
        }

        public void TryInteract()
        {
            if (Holding != null && Holding.Busy)
                return;
            if (Current is IInteractable i && i.CanInteract(this))
                i.Interact(this);
            else if (Holding != null)
                DropInFront();
        }

        public bool TakeTool(Tool tool)
        {
            if (tool == Tool.None || !HandsFree)
                return false;
            Tool = tool;
            HandsVersion++;
            if (hands != null)
            {
                hands.SetTool(tool);
                // Bàn chải cầm một tay phải (dáng nắm), dụng cụ cán dài khác cầm hai tay (dáng xẻng).
                hands.SetPose(IsBrush(tool) ? HandPose.Grip : HandPose.Shovel);
            }
            return true;
        }

        public static bool IsBrush(Tool tool) => tool == Tool.Brush || tool == Tool.LongBrush;

        public Tool ReleaseTool()
        {
            var t = Tool;
            Tool = Tool.None;
            if (t != Tool.None) HandsVersion++;
            if (hands != null && t != Tool.None)
            {
                hands.SetShovelLoaded(false);
                hands.SetTool(Tool.None);
                hands.SetPose(HandPose.Lowered);
            }
            return t;
        }

        public void Pick(Holdable h)
        {
            if (h == null || !HandsFree)
                return;
            Holding = h;
            HandsVersion++;
            bool hang = h.hang && h.hangGrip != null && holdHang != null;
            h.AttachTo(hang ? holdHang : h.ShoulderCarried && holdRightShoulder != null ? holdRightShoulder :
                h.twoHanded ? holdTwoHands : holdOneHand);
            if (motor != null)
                motor.Carrying = h.twoHanded && !hang;
            if (hands != null)
            {
                hands.SetShoulderCarry(h.ShoulderCarried, holdTwoHands);
                if (hang)
                    hands.SetHangCarry(h.hangGrip);
                else
                {
                    hands.SetPose(h.twoHanded ? HandPose.Carry : HandPose.Grip);
                    if (h.leftGrip != null || h.rightGrip != null) hands.SetHeldGrips(h.leftGrip, h.rightGrip);
                }
            }
            Picked?.Invoke(h);
        }

        /// <summary>Bỏ vật đang cầm khỏi tay (người gọi tự đặt vị trí cho nó).</summary>
        public Holdable ReleaseHeld()
        {
            var h = Holding;
            Holding = null;
            if (h != null) HandsVersion++;
            if (motor != null)
                motor.Carrying = false;
            if (hands != null && h != null)
            {
                hands.SetShoulderCarry(false, null);
                hands.SetHangCarry(null);
                hands.SetWorkGrips(null, null);
                hands.SetPose(HandPose.Lowered);
            }
            return h;
        }

        /// <summary>Thả vật (không cần camera) — cho test.</summary>
        public void DropInFrontForTests()
        {
            var h = ReleaseHeld();
            if (h != null)
                h.Detach(transform.position + transform.forward, Quaternion.identity, physics: false);
        }

        public void DropInFront()
        {
            var h = ReleaseHeld();
            if (h == null)
                return;
            var f = cam.transform.forward;
            f.y = 0f;
            var pos = cam.transform.position + f.normalized * 0.7f + Vector3.down * 0.5f;
            h.Detach(pos, Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f), physics: true);
        }

        void ShowPrompt(string key)
        {
            if (key == _shownKey || _root == null)
                return;
            _shownKey = key;
            _root.UI.Prompt.SetWorldPrompt(key != null ? _root.Content.GetString(key) : null);
        }

        public void ClearPrompt()
        {
            _shownKey = null;
            if (_root != null && _root.UI != null)
                _root.UI.Prompt.SetWorldPrompt(null);
        }
    }
}
