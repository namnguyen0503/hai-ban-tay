using UnityEngine;

namespace HaiBanTay.Ship
{
    /// <summary>Đồ rời trượt theo lắc tàu: lực ngang có trần, vận tốc có trần. Secured thì đứng yên.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class LooseObjectSway : MonoBehaviour
    {
        public float maxAccel = 1.2f;
        public float maxSpeed = 0.6f;

        static PhysicsMaterial _slippery;
        Rigidbody _rb;

        public bool Secured { get; private set; }
        /// <summary>Có người nắm giữ (Jacques nắm hai quai): đứng yên tại chỗ nhưng chưa tính là đã chằng.</summary>
        public bool Held { get; private set; }

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            // Ma sát thấp để lực có trần đủ làm đồ trượt; giới hạn tốc độ giữ cho đồ không văng.
            _slippery ??= new PhysicsMaterial("Slippery")
            {
                dynamicFriction = 0.05f,
                staticFriction = 0.05f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            foreach (var c in GetComponentsInChildren<Collider>())
                c.sharedMaterial = _slippery;
        }

        public void Secure(bool secured)
        {
            Secured = secured;
            if (_rb == null)
                Awake();
            if (secured)
            {
                if (!_rb.isKinematic)
                {
                    _rb.linearVelocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                }
                _rb.isKinematic = true;
            }
            else
            {
                _rb.isKinematic = false;
            }
        }

        public void Hold(bool held)
        {
            Held = held;
            if (_rb == null)
                Awake();
            if (held && !_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
        }

        void FixedUpdate()
        {
            var ship = ShipMotion.Current;
            if (Secured || ship == null || _rb.isKinematic)
                return;
            if (Held)
            {
                _rb.linearVelocity = new Vector3(0f, _rb.linearVelocity.y, 0f);
                return;
            }
            var a = Vector3.ClampMagnitude(ship.LateralAccel, maxAccel);
            _rb.AddForce(a, ForceMode.Acceleration);
            var v = _rb.linearVelocity;
            var horizontal = new Vector3(v.x, 0f, v.z);
            if (horizontal.magnitude > maxSpeed)
            {
                horizontal = horizontal.normalized * maxSpeed;
                _rb.linearVelocity = new Vector3(horizontal.x, v.y, horizontal.z);
            }
        }
    }
}
