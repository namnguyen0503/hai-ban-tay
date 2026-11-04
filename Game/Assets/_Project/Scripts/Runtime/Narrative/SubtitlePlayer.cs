using System.Collections;
using System.Collections.Generic;
using HaiBanTay.Audio;
using HaiBanTay.Content;
using HaiBanTay.UI;
using UnityEngine;

namespace HaiBanTay.Narrative
{
    /// <summary>Phát VO + phụ đề theo ID, có hàng đợi. Phụ đề luôn hiện; chưa có file thoại thì hiện chữ theo duration.</summary>
    public sealed class SubtitlePlayer : MonoBehaviour
    {
        ContentDatabase _content;
        AudioService _audio;
        SubtitleView _view;
        sealed class Request
        {
            public string Id;
            public bool Done;
        }
        readonly Queue<Request> _queue = new Queue<Request>();
        readonly Dictionary<string, Request> _pending = new Dictionary<string, Request>();
        Coroutine _running;

        public string CurrentLineId { get; private set; }
        /// <summary>Còn câu đang phát hoặc đang chờ trong hàng.</summary>
        public bool Busy => _running != null;
        /// <summary>Câu này đang phát hoặc đã xếp hàng chờ phát.</summary>
        public bool Pending(string lineId) => lineId != null && _pending.ContainsKey(lineId);
        public bool IsPlaying => _running != null;
        public int PlayedCount { get; private set; }

        public void Initialize(ContentDatabase content, AudioService audio, SubtitleView view)
        {
            _content = content;
            _audio = audio;
            _view = view;
        }

        public void Play(string lineId)
        {
            Enqueue(lineId);
        }

        Request Enqueue(string lineId)
        {
            _content.GetLine(lineId); // báo lỗi sớm nếu ID sai
            // Hai handler cùng yêu cầu một câu đang chờ thì cùng chờ lượt đó; xong rồi vẫn được nói lại.
            if (_pending.TryGetValue(lineId, out var pending)) return pending;
            var request = new Request { Id = lineId };
            _pending.Add(lineId, request);
            _queue.Enqueue(request);
            if (_running == null) _running = StartCoroutine(Run());
            return request;
        }

        /// <summary>Phát và chờ tới khi câu này (và hàng đợi trước nó) xong.</summary>
        public IEnumerator PlayAndWait(string lineId)
        {
            var request = Enqueue(lineId);
            while (!request.Done)
                yield return null;
        }

        public void StopAll()
        {
            if (CurrentLineId != null) Debug.Log($"VO CANCEL {PlayedCount} {CurrentLineId}");
            foreach (var request in _pending.Values) request.Done = true;
            _pending.Clear();
            _queue.Clear();
            if (_running != null)
                StopCoroutine(_running);
            _running = null;
            CurrentLineId = null;
            _lastSpeaker = null;
            Npc.TalkGaze.StopAll();
            _view?.Hide();
            _audio?.StopVo();
            _audio?.SetMusicDuck(false);
        }

        void OnDisable() => StopAll();

        /// <summary>Nghỉ thêm giữa hai câu của hai người khác nhau (giây, thời gian thật): người nghe có nhịp đáp lời.</summary>
        public const float SpeakerChangeGap = 1f;
        string _lastSpeaker;
        float _lastEnd = -100f;

        IEnumerator Run()
        {
            _audio?.SetMusicDuck(true);
            while (_queue.Count > 0)
            {
                var request = _queue.Dequeue();
                var line = _content.GetLine(request.Id);
                if (!Core.Sim.Enabled && _lastSpeaker != null && line.Speaker != _lastSpeaker)
                    while (AudioListener.pause || Time.unscaledTime < _lastEnd + SpeakerChangeGap)
                        yield return null;
                if (line.FirstTime && !Core.Sim.Enabled)
                    _audio?.PlaySfx("SFX/first_time", 0.55f);
                CurrentLineId = line.Id;
                PlayedCount++;
                _content.Subtitles.Speakers.TryGetValue(line.Speaker, out var speaker);
                _view?.Show(speaker, line.Fr, Core.Vocabulary.Render(line.Vi));
                float rate = Core.Sim.Enabled ? Mathf.Clamp(Time.timeScale, 1f, 3f) : Core.DevSpeed.VoiceRate;
                float clip = _audio != null ? _audio.PlayVo(line.Audio, rate) : 0f;
                float seconds = clip > 0f ? clip : (Core.Sim.Enabled ? line.Duration : line.Duration / Core.DevSpeed.VoiceRate);
                Debug.Log($"VO START {PlayedCount} {line.Id} {line.Audio} {seconds:F3}s");
                Npc.TalkGaze.LineStarted(line.Speaker, line.Gaze, seconds);
                // Có clip thì chữ theo chính tiếng: AudioSource chạy theo thời gian thật (không theo Time.timeScale của Sim), đứng yên
                // khi tạm dừng game (AudioListener.pause) và không bị khựng khi tải cảnh. Không có clip thì đếm duration cùng nhịp đó.
                // Test đặt Time.captureDeltaTime (bot nhanh, quay clip): giờ game tách khỏi giờ thật, chữ đếm đúng độ dài clip theo giờ game.
                bool captured = Time.captureDeltaTime > 0f;
                float t = 0f;
                while (true)
                {
                    yield return null;
                    if (AudioListener.pause)
                        continue;
                    t += captured ? Time.captureDeltaTime : Time.unscaledDeltaTime;
                    bool voice = !captured && clip > 0f && _audio != null && _audio.VoPlaying;
                    if (!voice && ((clip > 0f && !captured) || t >= seconds))
                        break;
                    if (t >= seconds + 1f)
                        break;
                }
                _audio?.StopVo();
                _view?.Hide();
                Debug.Log($"VO END {PlayedCount} {line.Id}");
                _lastSpeaker = line.Speaker;
                _lastEnd = Time.unscaledTime;
                CurrentLineId = null;
                _pending.Remove(line.Id);
                request.Done = true;
                yield return new WaitForSecondsRealtime(0.15f);
            }
            CurrentLineId = null;
            _running = null;
            _audio?.SetMusicDuck(false);
        }
    }
}
