using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>
    /// Hàng chờ dùng chung của các CrowdRoutine chế độ Queue (hàng lĩnh lương ở Marseille, hàng lên tàu). Ô là các Transform
    /// do builder đặt, slots[0] là đầu hàng. Advance() cho director gọi: người đầu hàng đi khỏi hàng qua exit (rồi, nếu
    /// recycle, vòng về đuôi hàng), mọi người sau tiến lên một ô.
    /// </summary>
    public sealed class CrowdQueue : MonoBehaviour
    {
        /// <summary>Các ô đứng, slots[0] là đầu hàng.</summary>
        public Transform[] slots = new Transform[0];
        /// <summary>Điểm người đầu hàng đi tới sau khi xong việc.</summary>
        public Transform exit;
        /// <summary>Hàng lên tàu: người đầu hàng trình vé rồi đi theo các điểm này (cầu thang tàu, đi dốc), khuất vào khoang,
        /// vài giây sau hiện lại ở respawn (xa đuôi hàng) và đi về đuôi hàng. Trống thì dùng exit như cũ.</summary>
        public Transform[] boardPath = new Transform[0];
        public Transform respawn;
        public float boardedSeconds = 6f;
        /// <summary>Đường quay về đuôi hàng sau exit (có thể trống).</summary>
        public Transform[] returnPath = new Transform[0];
        /// <summary>Người rời hàng đi vòng lại vào đuôi (đám đông sandbox). Tắt thì họ đứng lại ở exit.</summary>
        public bool recycle = true;
        /// <summary>Lớn hơn 0: tự gọi Advance khi người đầu hàng đã đứng đủ giây. 0: chỉ director gọi.</summary>
        public float autoAdvanceSeconds;
        /// <summary>Thành viên theo thứ tự ô lúc bắt đầu (builder gán): phần tử đầu đứng đầu hàng.</summary>
        public CrowdRoutine[] members = new CrowdRoutine[0];

        readonly CrowdQueueLogic _logic = new CrowdQueueLogic();
        readonly List<CrowdRoutine> _members = new List<CrowdRoutine>();
        float _frontSeconds;

        public int Count => _logic.Count;
        public int Served { get; private set; }
        public event Action<CrowdRoutine> Advanced;

        void Awake()
        {
            foreach (var m in members)
                if (m != null)
                    Join(m);
        }

        /// <summary>Đăng ký thành viên vào đuôi hàng (người đăng ký đầu tiên đứng đầu hàng).</summary>
        public int Join(CrowdRoutine member)
        {
            int id = _members.IndexOf(member);
            if (id < 0)
            {
                id = _members.Count;
                _members.Add(member);
            }
            _logic.Enqueue(id);
            return id;
        }

        /// <summary>Ô đang đứng của thành viên (0 = đầu hàng), -1 nếu đang đi vòng.</summary>
        public int SlotOf(CrowdRoutine member)
        {
            int id = _members.IndexOf(member);
            return id < 0 ? -1 : _logic.SlotOf(id);
        }

        /// <summary>Ô của đuôi hàng (ô trống đầu tiên), giới hạn trong số ô có.</summary>
        public int TailSlot => Mathf.Min(_logic.Count, slots.Length - 1);

        /// <summary>Vào đuôi hàng nếu ô đuôi vẫn là ô expected (người khác chưa chen vào). Trả về true khi vào được.</summary>
        public bool TryJoinTail(CrowdRoutine member, int expectedSlot)
        {
            if (_logic.Count != expectedSlot || expectedSlot >= slots.Length)
                return false;
            Join(member);
            return true;
        }

        /// <summary>Người đầu hàng rời đi, mọi người sau tiến lên. Trả về false khi hàng rỗng.</summary>
        public bool Advance()
        {
            int id = _logic.Dequeue();
            if (id < 0)
                return false;
            var leaver = _members[id];
            leaver.BeginLeaving();
            Served++;
            _frontSeconds = 0f;
            Advanced?.Invoke(leaver);
            return true;
        }

        IEnumerator Start()
        {
            var tick = new WaitForSeconds(0.25f);
            while (true)
            {
                yield return tick;
                if (autoAdvanceSeconds <= 0f || _logic.Count == 0)
                {
                    _frontSeconds = 0f;
                    continue;
                }
                var front = _members[_logic.Front];
                if (front.PlacedSlot != 0)
                {
                    _frontSeconds = 0f;
                    continue;
                }
                _frontSeconds += 0.25f;
                if (_frontSeconds >= autoAdvanceSeconds)
                    Advance();
            }
        }
    }
}
