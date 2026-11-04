using System;
using System.Collections;
using UnityEngine;

namespace HaiBanTay.Ending
{
    /// <summary>E4: tàu điện chạy ngang theo đường ray (4 m/s), chuông leng keng.</summary>
    public sealed class TramPass : MonoBehaviour
    {
        public Transform tram;
        public Transform from;
        public Transform to;
        public float speed = 4f;
        public string bellKey = "SFX/tram_bell";

        public bool Running { get; private set; }
        public bool Done { get; private set; }
        public event Action PassedWatcher;

        void Awake()
        {
            if (tram != null && from != null)
                tram.position = from.position;
        }

        public IEnumerator Run(Transform watcher)
        {
            Running = true;
            Core.GameRoot.Instance?.Audio.PlaySfxAt(bellKey, tram.position);
            var a = from.position;
            var b = to.position;
            float length = Vector3.Distance(a, b);
            var dir = (b - a).normalized;
            bool passed = false;
            for (float d = 0f; d < length; d += speed * Time.deltaTime)
            {
                tram.position = a + dir * d;
                if (!passed && watcher != null && Vector3.Dot(watcher.position - tram.position, dir) < 0f)
                {
                    passed = true;
                    PassedWatcher?.Invoke();
                }
                yield return null;
            }
            tram.position = b;
            if (!passed)
                PassedWatcher?.Invoke();
            Running = false;
            Done = true;
        }
    }
}
