using HaiBanTay.Shift;
using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>Lá thật theo bàn tay trong clip WorkTable của hai lính giúp nhặt rau.</summary>
    public sealed class GreensHands : MonoBehaviour
    {
        public NpcAgent agent;
        public GreensStation greens;
        public GameObject leaf;
        void LateUpdate()
        {
            bool working = agent.State == "WorkTable" && greens.RawReady &&
                (greens.Logic.Pending > 0 || greens.Logic.HasBunch);
            if (leaf.activeSelf != working) leaf.SetActive(working);
        }
    }
}
