using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HaiBanTay.Ending
{
    /// <summary>E2: đồng xu rơi lần lượt, rải riêng để đếm được trên lòng bàn tay. Chuyển động bằng code (không vật lý) để
    /// khung hình lặp lại đúng mỗi lần chơi.</summary>
    public sealed class CoinDrop : MonoBehaviour
    {
        public GameObject coinTemplate;
        public float interval = 0.14f;
        public float fallSeconds = 0.3f;
        public float fallHeight = 0.25f;

        readonly List<GameObject> _coins = new List<GameObject>();

        public int Dropped { get; private set; }

        /// <summary>Thả count đồng, chia đều, trải thành hai hàng trên mỗi lòng bàn tay. localUp: hướng "lên" của lòng tay trong không gian cục bộ.</summary>
        public IEnumerator Drop(Transform[] palms, Vector3 localUp, int count)
        {
            var stacks = new int[palms.Length];
            var running = new List<Coroutine>();
            for (int i = 0; i < count; i++)
            {
                int p = i % palms.Length;
                running.Add(StartCoroutine(Fall(palms[p], localUp, stacks[p]++, i)));
                yield return new WaitForSeconds(interval);
            }
            foreach (var c in running)
                yield return c;
        }

        IEnumerator Fall(Transform palm, Vector3 localUp, int stack, int index)
        {
            var coin = Instantiate(coinTemplate, palm);
            coin.SetActive(true);
            coin.name = "Coin_" + index;
            _coins.Add(coin);
            var up = localUp.normalized;
            // Năm đồng tách nhau: hai hàng 3 + 2, nằm trên lòng tay từ gốc cổ tay.
            float x = stack < 3 ? (stack - 1) * 0.029f : (stack - 3.5f) * 0.030f;
            float z = stack < 3 ? 0.045f : 0.075f;
            // Gốc tay ở khớp cổ tay; mặt lòng tay FP (Rocketbox) nằm ~3,2 cm (hàng gần) / ~2,7 cm (hàng xa) phía lòng tay.
            var rest = up * (stack < 3 ? 0.036f : 0.031f) + new Vector3(x, 0f, z);
            var start = rest + up * fallHeight;
            var rot = Quaternion.FromToRotation(Vector3.up, up);
            coin.transform.localRotation = rot;
            foreach (var renderer in coin.GetComponentsInChildren<Renderer>())
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var block = new MaterialPropertyBlock();
                block.SetColor("_EmissionColor", new Color(0.15f, 0.13f, 0.09f));
                renderer.SetPropertyBlock(block);
            }
            for (float t = 0f; t < fallSeconds; t += Time.deltaTime)
            {
                float k = t / fallSeconds;
                coin.transform.localPosition = Vector3.Lerp(start, rest, k * k);
                yield return null;
            }
            coin.transform.localPosition = rest;
            Dropped++;
            Core.GameRoot.Instance?.Audio.PlaySfx("SFX/coin", 0.6f);
        }

        public void Clear()
        {
            foreach (var c in _coins)
                if (c != null)
                    Destroy(c);
            _coins.Clear();
            Dropped = 0;
        }
    }
}
