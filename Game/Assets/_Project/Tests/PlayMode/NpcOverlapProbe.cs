using System.Collections.Generic;
using HaiBanTay.Npc;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Đo NPC đi xuyên người nhau (clip 14/10: quản bếp đi xuyên vai phụ bếp ở lối trước lò): mỗi khung, hai NPC đang bật mà tâm cách
    /// nhau (ngang) dưới Touch thì tính là chồng thân. Ghi tổng thời gian, đoạn dài nhất, khoảng cách gần nhất và cặp NPC.
    /// </summary>
    [DefaultExecutionOrder(14200)]
    public sealed class NpcOverlapProbe : MonoBehaviour
    {
        /// <summary>Hai thân bán kính 0,25 m chạm nhau ở 0,5 m; dưới 0,4 m là thấy rõ thân chồng lên nhau.</summary>
        public const float Touch = 0.4f;

        readonly List<NpcAgent> _agents = new List<NpcAgent>();
        float _stretch;

        public float Total { get; private set; }
        public float Longest { get; private set; }
        public float Closest { get; private set; } = float.MaxValue;
        public string Pair { get; private set; } = "";

        public NpcOverlapProbe Watch(params NpcAgent[] agents)
        {
            _agents.AddRange(agents);
            return this;
        }

        public string Report => $"chồng thân tổng {Total:F2} s, dài nhất {Longest:F2} s, gần nhất {(Closest < float.MaxValue ? Closest : 0f):F2} m ({Pair})";

        void LateUpdate()
        {
            bool any = false;
            for (int i = 0; i < _agents.Count; i++)
                for (int j = i + 1; j < _agents.Count; j++)
                {
                    var a = _agents[i];
                    var b = _agents[j];
                    if (a == null || b == null || !a.isActiveAndEnabled || !b.isActiveAndEnabled) continue;
                    var d = a.Position - b.Position;
                    d.y = 0f;
                    float dist = d.magnitude;
                    if (dist < Closest)
                    {
                        Closest = dist;
                        Pair = $"{a.name}–{b.name} ở {a.Position}/{b.Position}, đang đi {a.Walking}/{b.Walking}, chờ {a.Waiting}/{b.Waiting} "
                               + $"({a.BlockedBy}/{b.BlockedBy}), hướng {a.Heading}/{b.Heading}, tốc độ {a.Speed:F2}/{b.Speed:F2}, "
                               + $"đã chờ ở đích {a.HoldSeconds:F1}/{b.HoldSeconds:F1} s, t={Time.time:F1}";
                    }
                    if (dist < Touch) any = true;
                }
            if (any)
            {
                Total += Time.deltaTime;
                _stretch += Time.deltaTime;
                Longest = Mathf.Max(Longest, _stretch);
            }
            else
                _stretch = 0f;
        }
    }
}
