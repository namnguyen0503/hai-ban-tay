using System.Collections;
using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>Lịch làm việc lặp lại của một NPC trong ca. Lộ trình là các điểm qua lối trống do builder đặt.</summary>
    public abstract class NpcRoutine : MonoBehaviour
    {
        public NpcAgent agent;

        void OnEnable()
        {
            if (agent == null)
                agent = GetComponent<NpcAgent>();
            StartCoroutine(Run());
        }

        protected abstract IEnumerator Run();

        void OnDisable()
        {
            StopAllCoroutines();
            // Đường đi đang dở bị cắt ngang (khối finally của WalkNav không chạy): xóa cờ đi, chờ, nối đoạn.
            if (agent != null) agent.ClearWalkState();
            Stopped();
        }

        /// <summary>Routine bị dừng giữa chừng (tắt component): trả vật đang thao tác về trạng thái hợp lệ.</summary>
        protected virtual void Stopped() { }

        /// <summary>Đi theo lộ trình xuôi hoặc ngược (bỏ điểm đầu vì đang đứng ở đó).</summary>
        protected IEnumerator Path(Vector3[] points, bool reverse = false)
        {
            if (points == null || points.Length == 0)
                yield break;
            for (int i = 1; i < points.Length; i++)
                yield return agent.Go(points[reverse ? points.Length - 1 - i : i]);
        }
    }
}
