using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Player;
using HaiBanTay.Ship;
using UnityEngine;

namespace HaiBanTay.Audio
{
    /// <summary>Cảnh 01–03: dùng vùng đã có, crossfade các lớp và đọc Settings mỗi khung.</summary>
    public sealed class ShipAmbience : MonoBehaviour
    {
        public ShipZone galley;
        public ShipZone hold;
        public ShipZone street;
        public DayScenery scenery;
        public bool marseille;
        public PlayerArea aboard;
        public string quayKey = "Ambience/nharong_quay";
        /// <summary>
        /// Bến xa (đèn bến Colombo tối ngày 3): đang bật thì trên boong nghe tiếng hò khuân vác vọng qua mặt nước (farShoreKey), trong khoang
        /// tắt dần. null = cảnh không có.
        /// </summary>
        public GameObject farShore;
        public string farShoreKey = "Ambience/colombo_dockers";
        /// <summary>Độ lớn lớp hò khuân vác trên boong (so với kênh Ambience).</summary>
        public float farShoreLevel = 0.32f;
        public bool departed;
        public float fadeSeconds = 1.5f;

        static readonly string[] Keys =
        {
            "Ambience/galley", "Ambience/hold_cold", "Ambience/sea_deck",
            "Ambience/harbor_water", "Ambience/marseille_street"
        };
        readonly AudioSource[] _sources = new AudioSource[Keys.Length + 1];
        readonly float[] _levels = new float[Keys.Length + 1];
        AudioService _audio;
        AudioSource _farShore;
        float _farShoreLevel;
        float _nextGull;
        int _gull;

        public string CurrentKey { get; private set; }

        void Start()
        {
            _audio = GameRoot.Instance != null ? GameRoot.Instance.Audio : null;
            if (_audio == null)
                return;
            for (int i = 0; i < _sources.Length; i++)
            {
                // Cảnh 01 chỉ cần kè, nước cảng và boong; cảnh 02/03 giữ các lớp khoang cũ.
                if (aboard != null ? i != 2 && i != 3 && i != 5 : i == 5)
                    continue;
                _sources[i] = _audio.StartLoop(i == 5 ? quayKey : Keys[i], AudioChannel.Ambience, gameObject, volume: 0f);
            }
            if (farShore != null)
                _farShore = _audio.StartLoop(farShoreKey, AudioChannel.Ambience, gameObject, volume: 0f);
            _nextGull = Time.time + 9f;
        }

        /// <summary>Độ lớn hiện tại của lớp hò khuân vác bến xa (0 = tắt).</summary>
        public float FarShoreLevel => _farShoreLevel;

        void Update()
        {
            if (_audio == null || PlayerRig.Current == null)
                return;
            int zone = aboard != null ? (departed || aboard.PlayerInside ? 2 : 5)
                : hold != null && hold.PlayerInside ? 1
                : galley != null && galley.PlayerInside ? 0
                : street != null && street.PlayerInside ? 4 : 2;
            bool port = marseille || (scenery != null && scenery.Docked);
            bool quay = aboard != null ? zone == 5 : marseille && zone == 2 && PlayerRig.Current.Position.y < -1f;
            CurrentKey = aboard != null ? (zone == 5 ? quayKey : Keys[zone]) : Keys[quay ? 3 : zone];
            for (int i = 0; i < _sources.Length; i++)
            {
                float target = i == zone ? (i == 2 && marseille ? 0.18f : 0.65f) : 0f;
                if (i == 2 && quay)
                    target = 0f;
                if (i == 3 && (aboard != null ? quay : zone == 2 && port))
                    target = 0.5f;
                _levels[i] = Mathf.MoveTowards(_levels[i], target, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
                if (_sources[i] != null)
                    _sources[i].volume = _levels[i] * _audio.Volume(AudioChannel.Ambience);
            }
            if (_farShore != null)
            {
                float want = farShore.activeInHierarchy && zone == 2 ? farShoreLevel : 0f;
                _farShoreLevel = Mathf.MoveTowards(_farShoreLevel, want, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds * 2f) * farShoreLevel);
                _farShore.volume = _farShoreLevel * _audio.Volume(AudioChannel.Ambience);
            }
            if ((aboard != null ? quay : port && zone == 2) && Time.time >= _nextGull)
            {
                _audio.PlaySfx(_gull++ % 2 == 0 ? "SFX/gull_1" : "SFX/gull_2", 0.28f);
                _nextGull = Time.time + 18f;
            }
        }
    }
}
