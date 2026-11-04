using System.Collections.Generic;
using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>
    /// Nối câu thoại với NPC đang nói: các cảnh gán speaker (subtitles.vi.json) cho NpcAgent của cảnh (Bind), SubtitlePlayer báo mỗi
    /// câu bắt đầu (LineStarted). Câu của NPC thì NPC đó nhìn theo người chơi (NpcGaze) suốt câu và thêm Linger giây; câu của Ba thì
    /// người vừa nói với Ba tiếp tục nhìn Ba. Câu đánh dấu "gaze": "task" (hướng dẫn làm việc) hay "other" (nói với người khác) thì
    /// đầu theo clip.
    /// </summary>
    public static class TalkGaze
    {
        /// <summary>Speaker của người chơi (anh Ba).</summary>
        public const string Player = "ba";
        /// <summary>Hết câu vẫn nhìn thêm chừng này giây (chờ câu kế, không quay đi giữa hai câu).</summary>
        public const float Linger = 2.5f;

        static readonly Dictionary<string, NpcAgent> Speakers = new Dictionary<string, NpcAgent>();
        static readonly List<NpcGaze> Engaged = new List<NpcGaze>();

        /// <summary>Câu của speaker này do agent nói (gán lại khi đổi cảnh, đổi người nói).</summary>
        public static void Bind(string speaker, NpcAgent agent, bool overwrite = true)
        {
            if (string.IsNullOrEmpty(speaker) || speaker == Player || agent == null)
                return;
            if (overwrite || Agent(speaker) == null)
                Speakers[speaker] = agent;
        }

        /// <summary>Gán speaker của câu lineId cho agent (cảnh có người nói thay đổi theo lượt: chào từ biệt, gặp trên boong).</summary>
        public static void BindLine(string lineId, NpcAgent agent)
        {
            var root = Core.GameRoot.Instance;
            if (root != null && root.Content != null && !string.IsNullOrEmpty(lineId))
                Bind(root.Content.GetLine(lineId).Speaker, agent);
        }

        public static NpcAgent Agent(string speaker) =>
            speaker != null && Speakers.TryGetValue(speaker, out var agent) && agent != null ? agent : null;

        public static NpcGaze.Mode ModeOf(string gaze) =>
            gaze == "task" ? NpcGaze.Mode.Task : gaze == "other" ? NpcGaze.Mode.Other : NpcGaze.Mode.Talk;

        /// <summary>Một câu bắt đầu phát, dài seconds giây (SubtitlePlayer gọi).</summary>
        public static void LineStarted(string speaker, string gaze, float seconds)
        {
            Engaged.RemoveAll(g => g == null || !g.Talking);
            if (Core.Sim.Enabled)
                return;
            if (speaker == Player)
            {
                foreach (var g in Engaged)
                    g.Listen(seconds + Linger);
                return;
            }
            var agent = Agent(speaker);
            if (agent == null || !agent.isActiveAndEnabled)
                return;
            var gazer = NpcGaze.Of(agent);
            gazer.Engage(seconds + Linger, ModeOf(gaze));
            if (gazer.Talking && !Engaged.Contains(gazer))
                Engaged.Add(gazer);
        }

        /// <summary>Dừng mọi thoại (SubtitlePlayer.StopAll, đổi cảnh): thôi nhìn.</summary>
        public static void StopAll()
        {
            foreach (var g in Engaged)
                if (g != null)
                    g.Release();
            Engaged.Clear();
        }
    }
}
