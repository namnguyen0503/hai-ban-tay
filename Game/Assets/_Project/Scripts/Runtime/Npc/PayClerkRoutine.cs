using System.Collections;
using HaiBanTay.Core;
using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>Director sở hữu lượt trả lương; hàng không tự chạy khi người chơi còn ở trong bếp.</summary>
    public sealed class PayClerkRoutine : MonoBehaviour
    {
        public NpcAgent agent;
        public CrowdQueue queue;
        public Transform[] exits = new Transform[0];
        public Transform[] envelopes = new Transform[0];
        public bool PlayerCalled { get; private set; }
        public int Handovers { get; private set; }
        bool _started, _paidPlayer;

        public IEnumerator ServeQueue()
        {
            if (_started)
                yield break;
            _started = true;
            TalkGaze.BindLine("mrs_pay_next", agent);
            while (queue.Count > 0)
            {
                CrowdRoutine front = null;
                foreach (var member in queue.members)
                    if (queue.SlotOf(member) == 0)
                        front = member;
                // Đầu hàng bị người chơi chiếm thì NpcAgent nhường; không chờ vô hạn việc đặt chân đúng ô.
                float until = Time.time + 8f;
                while (front != null && front.PlacedSlot != 0 && Time.time < until)
                    yield return null;
                agent.Play("Documents");
                yield return GameRoot.Instance.Subtitles.PlayAndWait("mrs_pay_next");
                yield return agent.Do("HandOver", 1.2f);
                int index = System.Array.IndexOf(queue.members, front);
                var envelope = index >= 0 && index < envelopes.Length ? envelopes[index] : null;
                if (envelope != null && front != null)
                {
                    envelope.gameObject.SetActive(true);
                    envelope.SetParent(front.agent.carryPoint, false);
                    envelope.localPosition = Vector3.zero;
                    front.agent.Play("Documents");
                }
                Handovers++;
                if (index >= 0 && index < exits.Length) queue.exit = exits[index];
                queue.Advance();
                yield return new WaitForSeconds(1.5f);
            }
            agent.Play("Talk");
            PlayerCalled = true;
        }

        public IEnumerator HandToPlayer()
        {
            if (!PlayerCalled || _paidPlayer)
                yield break;
            _paidPlayer = true;
            yield return agent.Do("HandOver", 1.2f);
            Handovers++;
            agent.Play("Documents");
        }
    }
}
