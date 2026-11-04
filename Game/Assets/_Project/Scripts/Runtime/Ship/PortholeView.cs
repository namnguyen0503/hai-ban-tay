using UnityEngine;

namespace HaiBanTay.Ship
{
    /// <summary>Khung cảnh biển sau ô cửa sổ tròn: đường chân trời nghiêng ngược góc lắc của tàu và dập dềnh lên xuống,
    /// sóng trôi chậm. Quay quanh trục pháp tuyến (+Z cục bộ) nên mặt nạ tròn của texture không đổi.</summary>
    public sealed class PortholeView : MonoBehaviour
    {
        static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");

        public float rollGain = 1.4f;
        public float bobAmplitude = 0.04f;
        public float bobPeriod = 5.3f;

        Renderer _renderer;
        MaterialPropertyBlock _mpb;
        Quaternion _rest;

        void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _mpb = new MaterialPropertyBlock();
            _rest = transform.localRotation;
        }

        void Update()
        {
            var ship = ShipMotion.Current;
            float roll = ship != null ? ship.Roll : 0f;
            float intensity = ship != null ? ship.Intensity : 0f;
            transform.localRotation = _rest * Quaternion.Euler(0f, 0f, -roll * rollGain);
            if (_renderer == null)
                return;
            float bob = Mathf.Sin(Time.time * 2f * Mathf.PI / bobPeriod) * bobAmplitude * (0.3f + intensity);
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetVector(BaseMapSt, new Vector4(1f, 1f, 0f, bob));
            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
