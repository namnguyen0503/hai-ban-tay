using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HaiBanTay.DevTools
{
    /// <summary>Giữ ~400 dòng log gần nhất (kèm stack của cảnh báo/lỗi) để đính vào ghi chú debug.</summary>
    public static class LogBuffer
    {
        const int Capacity = 400;

        struct Entry
        {
            public DateTime Time;
            public float Realtime;
            public LogType Type;
            public string Message;
            public string Stack;
        }

        static readonly Entry[] _ring = new Entry[Capacity];
        static readonly object _lock = new object();
        static int _next, _count, _errors, _warnings;
        static bool _hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Hook()
        {
            lock (_lock)
            {
                _next = _count = _errors = _warnings = 0;
            }
            if (_hooked)
                return;
            _hooked = true;
            Application.logMessageReceivedThreaded += OnLog;
        }

        static void OnLog(string message, string stack, LogType type)
        {
            float rt = 0f;
            try { rt = Time.realtimeSinceStartup; } catch (Exception) { /* luồng phụ */ }
            lock (_lock)
            {
                _ring[_next] = new Entry
                {
                    Time = DateTime.Now,
                    Realtime = rt,
                    Type = type,
                    Message = message,
                    Stack = type == LogType.Log ? null : stack
                };
                _next = (_next + 1) % Capacity;
                if (_count < Capacity)
                    _count++;
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    _errors++;
                else if (type == LogType.Warning)
                    _warnings++;
            }
        }

        public static int ErrorCount { get { lock (_lock) return _errors; } }
        public static int WarningCount { get { lock (_lock) return _warnings; } }

        public static string Dump()
        {
            var sb = new StringBuilder();
            lock (_lock)
            {
                int start = (_next - _count + Capacity) % Capacity;
                for (int i = 0; i < _count; i++)
                {
                    var e = _ring[(start + i) % Capacity];
                    sb.Append(e.Time.ToString("HH:mm:ss.fff")).Append(" [").Append(e.Realtime.ToString("0.00")).Append("s] ")
                      .Append(e.Type).Append(": ").AppendLine(e.Message);
                    if (!string.IsNullOrEmpty(e.Stack))
                        sb.AppendLine(e.Stack.TrimEnd());
                }
            }
            return sb.ToString();
        }
    }
}
