using UnityEngine;

namespace HaiBanTay.Ship
{
    /// <summary>Giữ tàu đứng yên, xoay thế giới ngược roll/pitch quanh pivot, nhấp nhô nhẹ; collider tàu và người chơi giữ yên.</summary>
    public sealed class WorldMotion : MonoBehaviour
    {
        public Transform pivot;
        public ShipMotion ship;
        public float gain = 1f;
        public float bobAmplitude = 0.08f;

        Vector3 _restPosition;
        Quaternion _restRotation;

        void Awake()
        {
            _restPosition = transform.localPosition;
            _restRotation = transform.localRotation;
        }

        void LateUpdate()
        {
            var motion = ship != null ? ship : ShipMotion.Current;
            var parent = transform.parent;
            var restPosition = parent != null ? parent.TransformPoint(_restPosition) : _restPosition;
            var restRotation = parent != null ? parent.rotation * _restRotation : _restRotation;
            var center = pivot != null ? pivot.position : restPosition;
            var axis = pivot != null ? pivot.forward : (parent != null ? parent.forward : Vector3.forward);
            float roll = motion != null ? motion.Roll : 0f;
            var pitchAxis = pivot != null ? pivot.right : (parent != null ? parent.right : Vector3.right);
            float pitch = motion != null ? motion.Pitch : 0f;
            var tilt = Quaternion.AngleAxis(-roll * gain, axis) * Quaternion.AngleAxis(-pitch * gain, pitchAxis);
            float bob = motion != null ? -motion.HeaveFraction * bobAmplitude : 0f;
            transform.SetPositionAndRotation(center + tilt * (restPosition - center) + Vector3.up * bob,
                tilt * restRotation);
        }
    }
}
