using UnityEngine;

namespace HaiBanTay.Interaction
{
    /// <summary>Vật rơi khỏi khu vực (hoặc xuống dưới killY) thì tự về chỗ cũ sau một lúc.</summary>
    public sealed class ReturnToStart : MonoBehaviour
    {
        const float CheckInterval = 0.25f;

        public bool useArea;
        public Bounds area;
        public float killY = -3f;
        public float delay = 1f;

        Vector3 _position;
        Quaternion _rotation;
        Rigidbody _rb;
        Holdable _holdable;
        float _outside;
        float _timer;

        public int Returns { get; private set; }
        public Vector3 HomePosition => _position;
        public Quaternion HomeRotation => _rotation;

        void Awake()
        {
            _position = transform.position;
            _rotation = transform.rotation;
            _rb = GetComponent<Rigidbody>();
            _holdable = GetComponent<Holdable>();
        }

        public void SetHome(Vector3 position, Quaternion rotation)
        {
            _position = position;
            _rotation = rotation;
        }

        public bool IsOutside(Vector3 p) => p.y < killY || (useArea && !area.Contains(p));

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f)
                return;
            _timer = CheckInterval;
            if (_holdable != null && _holdable.IsHeld)
            {
                _outside = 0f;
                return;
            }
            _outside = IsOutside(transform.position) ? _outside + CheckInterval : 0f;
            if (_outside >= delay)
                Restore();
        }

        public void Restore()
        {
            _outside = 0f;
            transform.SetPositionAndRotation(_position, _rotation);
            if (_rb != null && !_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            Returns++;
        }
    }
}
