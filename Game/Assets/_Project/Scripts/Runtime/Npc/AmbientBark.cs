using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Player;
using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>
    /// Câu rao, gọi nhau ngắn của một NPC đám đông: người chơi đi ngang trong bán kính thì phát một câu (phụ đề ở
    /// subtitles.vi.json) khi không có thoại cốt truyện đang chạy. Chung một nhịp toàn cảnh (MinGap) để không dồn phụ đề.
    /// Không chạy ở chế độ Sim và khi người chơi đang bị khóa điều khiển (cutscene).
    /// </summary>
    public sealed class AmbientBark : MonoBehaviour
    {
        public string[] lineIds = new string[0];
        public float radius = 6f;
        [Tooltip("Giây giữa hai câu của cùng NPC này.")]
        public float cooldown = 40f;
        public int seed;

        /// <summary>Giây tối thiểu giữa hai câu rao bất kỳ trong cảnh.</summary>
        public const float MinGap = 6f;
        static float _nextAnyAt;

        int _next;

        IEnumerator Start()
        {
            var tick = new WaitForSeconds(0.5f);
            _next = lineIds.Length > 0 ? (int)(CrowdHash.Mix(seed, 5) % (uint)lineIds.Length) : 0;
            // Lệch pha: mỗi NPC chờ một lúc ngẫu nhiên trước khi được rao lần đầu.
            yield return new WaitForSeconds(2f + 8f * CrowdHash.Unit(seed, 6));
            while (lineIds.Length > 0)
            {
                yield return tick;
                if (Sim.Enabled || Time.time < _nextAnyAt)
                    continue;
                var root = GameRoot.Instance;
                var rig = PlayerRig.Current;
                if (root == null || rig == null || !root.Input.GameplayEnabled || root.Subtitles.IsPlaying)
                    continue;
                var d = rig.Position - transform.position;
                d.y *= 0.5f;
                if (d.sqrMagnitude > radius * radius)
                    continue;
                root.Subtitles.Play(lineIds[_next]);
                _next = (_next + 1) % lineIds.Length;
                _nextAnyAt = Time.time + MinGap;
                yield return new WaitForSeconds(cooldown * (0.8f + 0.5f * CrowdHash.Unit(seed, 7)));
            }
        }
    }
}
