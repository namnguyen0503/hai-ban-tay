using System;
using System.Collections.Generic;
using System.IO;
using HaiBanTay.Notebook;
using Newtonsoft.Json;
using UnityEngine;

namespace HaiBanTay.Core
{
    /// <summary>Bản lưu tiến trình: đầu mỗi ngày trên tàu (và khi tới Marseille).</summary>
    public sealed class SaveData
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        /// <summary>Cảnh để tiếp tục: 02_Galley (ngày Day) hoặc 03_Marseille.</summary>
        public string Scene { get; set; }
        public int Day { get; set; } = GameState.FirstDay;
        public int MaxDay { get; set; } = GameState.FirstDay;
        public bool ReachedMarseille { get; set; }
        public float Grime { get; set; }
        public float Fatigue { get; set; }
        public List<string> Vocab { get; set; } = new List<string>();
        /// <summary>Từ nợ sang buổi học sau (GameState.PendingWords).</summary>
        public List<string> PendingWords { get; set; } = new List<string>();
        public List<string> Pages { get; set; } = new List<string>();
        public List<string> Flags { get; set; } = new List<string>();
        public List<string> Events { get; set; } = new List<string>();
        public string SavedAt { get; set; }
    }

    /// <summary>Ghi/đọc persistentDataPath/save.json. Không ghi khi chạy test Sim (giữ bản lưu thật của người chơi).</summary>
    public static class SaveSystem
    {
        /// <summary>Thư mục lưu; test đặt thư mục tạm.</summary>
        public static string Folder { get; set; }
        /// <summary>Test bật để ghi thật trong chế độ Sim.</summary>
        public static bool AllowInSim { get; set; }

        static string SavePath => Path.Combine(Folder ?? Application.persistentDataPath, "save.json");

        public static bool HasSave => File.Exists(SavePath);

        public static SaveData Capture(string scene, GameState state, GameFlags flags, NotebookController notebook, GameEvents events)
        {
            var d = new SaveData
            {
                Scene = scene,
                Day = state.Day,
                MaxDay = state.MaxDay,
                ReachedMarseille = state.ReachedMarseille,
                Grime = state.Grime,
                Fatigue = state.Fatigue,
                Vocab = new List<string>(state.Vocab),
                PendingWords = new List<string>(state.PendingWords),
                Flags = new List<string>(flags.All),
                Events = new List<string>(events.Fired),
                SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            };
            foreach (var p in notebook.Unlocked)
                d.Pages.Add(p.Id);
            return d;
        }

        /// <summary>Khôi phục trạng thái (sau ResetProgress). Trang sổ mở lại không hiện thông báo.</summary>
        public static void Apply(SaveData d, GameState state, GameFlags flags, NotebookController notebook, GameEvents events)
        {
            state.Reset();
            state.Day = Math.Max(GameState.FirstDay, d.Day);
            state.MaxDay = Math.Max(state.Day, d.MaxDay);
            state.ReachedMarseille = d.ReachedMarseille;
            state.Grime = Mathf.Clamp01(d.Grime);
            state.Fatigue = Mathf.Clamp01(d.Fatigue);
            foreach (var w in d.Vocab)
                state.Learn(w);
            if (d.PendingWords != null)
                foreach (var w in d.PendingWords)
                    if (!state.Knows(w) && !state.PendingWords.Contains(w))
                        state.PendingWords.Add(w);
            foreach (var f in d.Flags)
                flags.Set(f);
            foreach (var e in d.Events)
                events.MarkFired(e);
            foreach (var p in d.Pages)
            {
                try
                {
                    notebook.EnsureUnlocked(p);
                }
                catch (Content.ContentException)
                {
                    Debug.LogWarning($"SaveSystem: bỏ qua trang '{p}' không còn trong nội dung");
                }
            }
        }

        public static bool Write(SaveData data)
        {
            if (Sim.Enabled && !AllowInSim)
                return false;
            try
            {
                WriteAtomic(SavePath, JsonConvert.SerializeObject(data, Formatting.Indented));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("SaveSystem: không ghi được bản lưu. " + e.Message);
                return false;
            }
        }

        public static SaveData Read()
        {
            try
            {
                if (!File.Exists(SavePath))
                    return null;
                var d = JsonConvert.DeserializeObject<SaveData>(File.ReadAllText(SavePath));
                if (d == null || d.Version > SaveData.CurrentVersion)
                    return null;
                return d;
            }
            catch (Exception e)
            {
                Debug.LogWarning("SaveSystem: bản lưu hỏng, bỏ qua. " + e.Message);
                return null;
            }
        }

        public static void Delete()
        {
            if (File.Exists(SavePath))
                File.Delete(SavePath);
        }

        /// <summary>Ghi ra file tạm rồi thay thế: mất điện giữa chừng không làm hỏng bản cũ.</summary>
        public static void WriteAtomic(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }
    }
}
