using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Player;
using UnityEngine;

namespace HaiBanTay.Ship
{
    /// <summary>
    /// Khung cảnh theo ngày của cảnh 02 (M11): phông cảng thấy từ boong (Ngày 2, cảng Singapore F02), tàu đỗ thì nước
    /// cảng êm và máy chạy nhỏ, dây chắn cầu thang tàu nhắc "không lên bờ"; hành khách, thủy thủ trên boong (tắt ngày bão);
    /// dây cứu sinh căng trên boong giếng ngày có sóng phủ boong (F05b). ShiftDirector gọi Apply trong Awake, trước khi
    /// EngineHum đọc âm lượng ở Start.
    /// </summary>
    public sealed class DayScenery : MonoBehaviour
    {
        [System.Serializable]
        public sealed class Port
        {
            public string id;
            public GameObject root;
        }

        public Port[] ports = new Port[0];
        public GameObject deckCrowd;
        /// <summary>Dây cứu sinh từ cửa bếp tới cột buồm trước (ngày bão).</summary>
        public GameObject stormRigging;
        public Renderer ocean;
        public Material harborWater;
        public EngineHum engine;
        public float dockedEngineVolume = 0.12f;
        public GameObject gangwayBarrier;
        public float barrierHintRadius = 2.6f;

        Transform _barrier;
        float _nextCheck;
        bool _hinting;

        public bool Docked { get; private set; }
        public string PortId { get; private set; }

        public void Apply(DayData day)
        {
            Docked = day != null && day.Docked;
            PortId = day != null ? day.Port : null;
            foreach (var p in ports)
                if (p != null && p.root != null)
                    p.root.SetActive(!string.IsNullOrEmpty(PortId) && p.id == PortId);
            if (deckCrowd != null)
                deckCrowd.SetActive(day == null || day.DeckCrowd);
            if (stormRigging != null)
                stormRigging.SetActive(day != null && day.Phases.Exists(p => p.DeckWave));
            if (Docked && ocean != null && harborWater != null)
            {
                ocean.sharedMaterial = harborWater;
                var clock = ocean.GetComponentInParent<TimeOfDay>();
                if (clock != null) clock.water = WaterKind.Harbor;
            }
            if (engine != null && Docked)
                engine.volume = dockedEngineVolume;
            _barrier = gangwayBarrier != null ? gangwayBarrier.transform : null;
        }

        void Update()
        {
            // Tàu đỗ: tới gần dây chắn cầu thang tàu thì nhắc phụ bếp không được lên bờ.
            if (!Docked || _barrier == null || Time.time < _nextCheck)
                return;
            _nextCheck = Time.time + 0.25f;
            var rig = PlayerRig.Current;
            var root = GameRoot.Instance;
            if (rig == null || root == null)
                return;
            var d = rig.Position - _barrier.position;
            bool near = Mathf.Abs(d.y) < 2f && d.x * d.x + d.z * d.z < barrierHintRadius * barrierHintRadius;
            if (near == _hinting)
                return;
            _hinting = near;
            root.UI.Prompt.SetScenePrompt(near ? root.Content.GetString("prompt.rope_barrier") : null);
        }

        void OnDisable()
        {
            if (_hinting && GameRoot.Instance != null)
                GameRoot.Instance.UI.Prompt.SetScenePrompt(null);
            _hinting = false;
        }
    }
}
