using System;
using System.Collections.Generic;
using UnityEngine;

namespace HaiBanTay.Audio
{
    /// <summary>Bảng khóa → AudioClip. ProjectSetup quét Assets/_Project/Audio và sinh lại; khóa dạng "VO/chef_depart".</summary>
    public sealed class AudioLibrary : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string key;
            public AudioClip clip;
        }

        public List<Entry> entries = new List<Entry>();

        Dictionary<string, AudioClip> _map;

        public AudioClip Get(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;
            if (_map == null)
            {
                _map = new Dictionary<string, AudioClip>();
                foreach (var e in entries)
                    if (e.clip != null && !string.IsNullOrEmpty(e.key))
                        _map[e.key] = e.clip;
            }
            return _map.TryGetValue(key, out var clip) ? clip : null;
        }
    }
}
