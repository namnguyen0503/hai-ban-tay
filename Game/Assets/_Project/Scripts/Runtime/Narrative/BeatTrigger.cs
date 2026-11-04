using System;
using HaiBanTay.Core;
using HaiBanTay.Player;
using UnityEngine;

namespace HaiBanTay.Narrative
{
    /// <summary>Vùng trigger: người chơi bước vào thì phát sự kiện (một lần) và báo C#. Theo dõi người chơi có đang ở trong.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class BeatTrigger : MonoBehaviour
    {
        public string eventId;
        public bool once = true;

        bool _fired;

        public bool PlayerInside { get; private set; }
        public float InsideSeconds { get; private set; }
        public event Action Entered;
        public event Action Exited;

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerRig>() == null)
                return;
            PlayerInside = true;
            if (once && _fired)
                return;
            _fired = true;
            if (!string.IsNullOrEmpty(eventId))
                GameRoot.Instance?.Events.Raise(eventId);
            Entered?.Invoke();
        }

        void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<PlayerRig>() == null)
                return;
            PlayerInside = false;
            Exited?.Invoke();
        }

        void Update()
        {
            if (PlayerInside)
                InsideSeconds += Time.deltaTime;
        }

        /// <summary>Kích hoạt như khi người chơi bước vào (Sim).</summary>
        public void Fire()
        {
            if (once && _fired)
                return;
            _fired = true;
            if (!string.IsNullOrEmpty(eventId))
                GameRoot.Instance?.Events.Raise(eventId);
            Entered?.Invoke();
        }

        public bool HasFired => _fired;
    }
}
