using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HaiBanTay.Core;
using HaiBanTay.Narrative;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HaiBanTay.Performance
{
    /// <summary>Một lượt beat liên tục. 1% low = nghịch đảo thời gian trung bình của 1% khung chậm nhất (làm tròn lên).</summary>
    public sealed class PerfBeatSamples
    {
        readonly List<float> _frames = new List<float>();
        double _seconds, _draws, _setPass;
        int _drawCount, _setPassCount;
        long _maxDraws = -1, _maxSetPass = -1;
        float _maxDt;
        public int Count => _frames.Count;
        public double Seconds => _seconds;
        public double AverageFps => Count == 0 ? 0 : Count / _seconds;
        public double MinFps => Count == 0 ? 0 : 1.0 / _maxDt;
        public double DrawCallsAverage => _drawCount == 0 ? -1 : _draws / _drawCount;
        public double SetPassAverage => _setPassCount == 0 ? -1 : _setPass / _setPassCount;
        public long DrawCallsMax => _maxDraws;
        public long SetPassMax => _maxSetPass;

        public void Add(float dt, long draws, long setPass)
        {
            if (dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt))
                return;
            _frames.Add(dt);
            _seconds += dt;
            _maxDt = Mathf.Max(_maxDt, dt);
            if (draws >= 0) { _draws += draws; _drawCount++; _maxDraws = Math.Max(_maxDraws, draws); }
            if (setPass >= 0) { _setPass += setPass; _setPassCount++; _maxSetPass = Math.Max(_maxSetPass, setPass); }
        }

        public double OnePercentLow()
        {
            if (Count == 0)
                return 0;
            _frames.Sort();
            int n = Math.Max(1, (int)Math.Ceiling(Count * 0.01));
            double seconds = 0;
            for (int i = Count - n; i < Count; i++)
                seconds += _frames[i];
            return n / seconds;
        }
    }

    /// <summary>F8: một dòng CSV cho mỗi lượt beat/tầng/cảnh; chốt cả lượt cuối khi ngừng. Bộ đếm thiếu ghi -1, không giả làm 0.</summary>
    public sealed class PerfRecorder : MonoBehaviour
    {
        public const string CsvHeader = "time_s,scene,beat,fps_avg,fps_min,ms_avg,tier,duration_s,frames,fps_1pct_low,draw_calls_avg,setpass_avg,draw_calls_max,setpass_max,build_id";
        StreamWriter _writer;
        QualityTierManager _quality;
        ProfilerRecorder _draws, _setPass;
        PerfBeatSamples _samples = new PerfBeatSamples();
        string _scene, _beat;
        QualityTier _tier;
        double _elapsed;
        string _pendingScene, _pendingBeat;
        QualityTier _pendingTier;
        bool _pending;
        int _lastFrame;

        public bool Recording => _writer != null;
        public string LastPath { get; private set; }

        public void Initialize(QualityTierManager quality) => _quality = quality;
        public void Toggle() { if (Recording) Stop(); else Begin(); }

        public void Begin(string path = null)
        {
            if (Recording)
                Stop();
            LastPath = path ?? Path.Combine(Application.persistentDataPath,
                "perf_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + ".csv");
            _writer = new StreamWriter(LastPath);
            _writer.WriteLine(CsvHeader);
            _writer.Flush();
            _elapsed = 0;
            _samples = new PerfBeatSamples();
            _pending = false;
            _draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            Debug.Log("PerfRecorder: bắt đầu ghi " + LastPath);
        }

        public void Stop()
        {
            if (!Recording)
                return;
            if (_pending && _lastFrame != Time.frameCount)
                RecordPending();
            FlushBeat();
            _writer.Dispose();
            _writer = null;
            _draws.Dispose();
            _setPass.Dispose();
            _pending = false;
            Debug.Log("PerfRecorder: đã lưu " + LastPath);
        }

        static long Counter(ProfilerRecorder recorder) => recorder.Valid && recorder.Count > 0 ? recorder.LastValue : -1;

        void RecordPending() => RecordFrame(_pendingScene, _pendingBeat, _pendingTier, Time.unscaledDeltaTime,
            Counter(_draws), Counter(_setPass));

        void LateUpdate()
        {
            if (!Recording)
                return;
            // deltaTime và LastValue thuộc khung vừa vẽ, dùng danh tính đã chốt ở cuối khung đó.
            if (_pending)
                RecordPending();
            _pendingScene = SceneManager.GetActiveScene().name;
            _pendingBeat = SceneDirector.Current != null ? SceneDirector.Current.Beat : "";
            _pendingTier = _quality != null ? _quality.Current : QualityTier.Laptop;
            _pending = true;
            _lastFrame = Time.frameCount;
        }

        public void RecordFrame(string scene, string beat, QualityTier tier, float dt, long draws, long setPass)
        {
            if (!Recording)
                return;
            if (_samples.Count > 0 && (scene != _scene || beat != _beat || tier != _tier))
                FlushBeat();
            _scene = scene;
            _beat = beat;
            _tier = tier;
            _samples.Add(dt, draws, setPass);
        }

        public static string CsvField(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

        void FlushBeat()
        {
            if (_samples.Count == 0)
                return;
            _elapsed += _samples.Seconds;
            _writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0:0.000},{1},{2},{3:0.000},{4:0.000},{5:0.000},{6},{7:0.000},{8},{9:0.000},{10:0.000},{11:0.000},{12},{13},{14}",
                _elapsed, CsvField(_scene), CsvField(_beat), _samples.AverageFps, _samples.MinFps,
                _samples.Seconds / _samples.Count * 1000, _tier, _samples.Seconds, _samples.Count, _samples.OnePercentLow(),
                _samples.DrawCallsAverage, _samples.SetPassAverage, _samples.DrawCallsMax, _samples.SetPassMax, CsvField(BuildInfo.Id)));
            _writer.Flush();
            _samples = new PerfBeatSamples();
        }

        void OnDestroy() => Stop();
    }
}
