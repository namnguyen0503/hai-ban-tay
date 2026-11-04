using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HaiBanTay.Audio
{
    /// <summary>Phát âm thanh theo khóa (AudioLibrary) qua bốn nhóm Music / SFX / Ambience / VO có âm lượng riêng.
    /// Bộ lọc thông thấp đặt trên AudioListener của cảnh (LowPassZone dùng).</summary>
    public sealed class AudioService : MonoBehaviour
    {
        public const float FullCutoff = 22000f;

        readonly float[] _volumes = { 0.7f, 1f, 0.8f, 1f };
        AudioLibrary _library;
        AudioSource _vo;
        AudioSource _sfx2D;
        AudioSource _music;
        AudioLowPassFilter _lowPass;
        Coroutine _musicFade;
        float _musicGain = 1f;
        float _musicLevel = 1f;
        float _duck = 1f;
        bool _speaking;

        public void Initialize(AudioLibrary library)
        {
            _library = library;
            _vo = Make2D("VO");
            _sfx2D = Make2D("SFX");
            _music = Make2D("Music");
            _music.loop = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        AudioSource Make2D(string name)
        {
            var go = new GameObject("Audio_" + name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            return s;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _lowPass = null;
            _hush = _hushTarget = 1f;
        }

        /// <summary>Âm lượng một nhóm; âm nền và nhạc nhân thêm mức "im lặng có điều khiển" (Hush).</summary>
        public float Volume(AudioChannel ch) =>
            _volumes[(int)ch] * (ch == AudioChannel.Ambience || ch == AudioChannel.Music ? _hush : 1f);

        float _hush = 1f, _hushTarget = 1f, _hushRate = 1f;

        /// <summary>
        /// Im lặng có điều khiển (DIRECTOR §5): kéo âm nền và nhạc về <paramref name="level"/> trong <paramref name="seconds"/> giây
        /// (thời gian thật), Hush(1, s) để trả lại. Chỉ dùng ở ba khoảnh khắc của cả game.
        /// </summary>
        public void Hush(float level, float seconds)
        {
            _hushTarget = Mathf.Clamp01(level);
            _hushRate = Mathf.Abs(_hushTarget - _hush) / Mathf.Max(0.01f, seconds);
            if (seconds <= 0f)
                _hush = _hushTarget;
        }

        public void SetVolume(AudioChannel ch, float v)
        {
            _volumes[(int)ch] = Mathf.Clamp01(v);
            // VO dài đang phát cũng phải nhận ngay thay đổi trong Cài đặt.
            if (ch == AudioChannel.Vo && _vo != null)
                _vo.volume = Volume(ch);
        }

        /// <summary>Âm lượng bốn nhóm = âm lượng tổng × âm lượng nhóm (GameSettings).</summary>
        public void ApplySettings(Core.GameSettings s)
        {
            SetVolume(AudioChannel.Music, s.MasterVolume * s.MusicVolume);
            SetVolume(AudioChannel.Sfx, s.MasterVolume * s.SfxVolume);
            SetVolume(AudioChannel.Ambience, s.MasterVolume * s.AmbienceVolume);
            SetVolume(AudioChannel.Vo, s.MasterVolume * s.VoVolume);
            UpdateMusicVolume();
        }

        public void SetMusicDuck(bool speaking) => _speaking = speaking;

        void Update()
        {
            _duck = Mathf.MoveTowards(_duck, _speaking ? 0.65f : 1f, Time.unscaledDeltaTime * 2f);
            _hush = Mathf.MoveTowards(_hush, _hushTarget, Time.unscaledDeltaTime * Mathf.Max(0.01f, _hushRate));
            UpdateMusicVolume();
        }

        void UpdateMusicVolume()
        {
            if (_music != null)
                _music.volume = Volume(AudioChannel.Music) * _musicLevel * _musicGain * _duck;
        }

        public AudioClip Clip(string key) => _library != null ? _library.Get(key) : null;

        /// <summary>Phát thoại; trả về độ dài clip (0 nếu chưa có file).</summary>
        public float PlayVo(string key, float playbackRate = 1f)
        {
            var clip = Clip(key);
            if (clip == null)
                return 0f;
            _vo.Stop();
            _vo.clip = clip;
            _vo.pitch = Mathf.Clamp(playbackRate, 0.01f, 3f);
            _vo.volume = Volume(AudioChannel.Vo);
            _vo.Play();
            return clip.length / _vo.pitch;
        }

        /// <summary>Clip thoại còn đang phát (kể cả khi AudioListener.pause giữ nó đứng yên).</summary>
        public bool VoPlaying => _vo != null && _vo.isPlaying;

        public void StopVo()
        {
            if (_vo == null) return;
            _vo.Stop();
            _vo.clip = null;
        }

        public void PlaySfx(string key, float volume = 1f)
        {
            var clip = Clip(key);
            if (clip != null)
                _sfx2D.PlayOneShot(clip, volume * Volume(AudioChannel.Sfx));
        }

        public void PlaySfxAt(string key, Vector3 position, float volume = 1f)
        {
            var clip = Clip(key);
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, position, volume * Volume(AudioChannel.Sfx));
        }

        /// <summary>Phát nhạc nền; bài đang phát trùng khóa thì để nguyên (không phát lại từ đầu).</summary>
        public void PlayMusic(string key, bool loop = false, float volume = 1f)
        {
            var clip = Clip(key);
            if (clip == null)
                return;
            if (_musicFade != null)
            {
                StopCoroutine(_musicFade);
                _musicFade = null;
            }
            _musicGain = 1f;
            _musicLevel = Mathf.Clamp01(volume);
            UpdateMusicVolume();
            if (_music.isPlaying && _music.clip == clip)
                return;
            _music.clip = clip;
            _music.loop = loop;
            _music.Play();
        }

        /// <summary>Nhỏ dần rồi dừng nhạc nền.</summary>
        public void StopMusic(float fadeSeconds = 1.5f)
        {
            if (_music != null && _music.isPlaying)
            {
                if (_musicFade != null)
                    StopCoroutine(_musicFade);
                _musicFade = StartCoroutine(FadeMusic(fadeSeconds));
            }
        }

        IEnumerator FadeMusic(float seconds)
        {
            float from = _musicGain;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                _musicGain = Mathf.Lerp(from, 0f, t / seconds);
                UpdateMusicVolume();
                yield return null;
            }
            _musicGain = 0f;
            UpdateMusicVolume();
            _music.Stop();
            _musicFade = null;
        }

        /// <summary>Gắn nguồn lặp vào host (3D nếu spatial). Không có clip theo khóa thì dùng fallback (có thể null).</summary>
        public AudioSource StartLoop(string key, AudioChannel ch, GameObject host, AudioClip fallback = null,
            float volume = 1f, bool spatial = false)
        {
            var clip = Clip(key) ?? fallback;
            if (clip == null || host == null)
                return null;
            var s = host.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = spatial ? 1f : 0f;
            s.minDistance = 2f;
            s.maxDistance = 25f;
            s.volume = volume * Volume(ch);
            s.Play();
            return s;
        }

        public IEnumerator FadeSource(AudioSource s, float to, float seconds)
        {
            if (s == null)
                yield break;
            float from = s.volume;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                if (s == null)
                    yield break;
                s.volume = Mathf.Lerp(from, to, t / seconds);
                yield return null;
            }
            if (s == null)
                yield break;
            s.volume = to;
            if (to <= 0f)
                s.Stop();
        }

        /// <summary>Tần số cắt của bộ lọc trên AudioListener (22 kHz = không lọc).</summary>
        public void SetLowPass(float cutoff)
        {
            if (_lowPass == null)
            {
                var listener = FindAnyObjectByType<AudioListener>();
                if (listener == null)
                    return;
                _lowPass = listener.GetComponent<AudioLowPassFilter>();
                if (_lowPass == null)
                    _lowPass = listener.gameObject.AddComponent<AudioLowPassFilter>();
            }
            _lowPass.cutoffFrequency = Mathf.Clamp(cutoff, 10f, FullCutoff);
            _lowPass.enabled = cutoff < FullCutoff - 1f;
        }
    }
}
