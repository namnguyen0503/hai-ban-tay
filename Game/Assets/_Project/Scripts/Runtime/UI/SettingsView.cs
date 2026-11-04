using System;
using HaiBanTay.Content;
using HaiBanTay.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HaiBanTay.UI
{
    /// <summary>Màn cài đặt (từ menu chính hoặc menu tạm dừng). Mỗi thay đổi áp dụng ngay (GameSettings.Apply).
    /// Nút −/+ và Bật/Tắt cỡ lớn, đọc được từ xa trên TV lớp học.</summary>
    public sealed class SettingsView
    {
        readonly VisualElement _root;
        readonly VisualElement _rows;
        readonly ContentDatabase _content;
        GameSettings _working;
        Action _onClose;

        public bool Visible { get; private set; }
        public VisualElement Root => _root;

        public SettingsView(VisualElement parent, ContentDatabase content)
        {
            _content = content;
            _root = Ui.Box("hbt-pause", parent);
            _root.pickingMode = PickingMode.Position;
            var panel = Ui.Box("hbt-settings", _root);
            panel.pickingMode = PickingMode.Position;
            Ui.Text("hbt-settings-title", panel, S("settings.title"));
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "settings-scroll" };
            scroll.AddToClassList("hbt-settings-rows");
            panel.Add(scroll);
            _rows = scroll.contentContainer;
            _rows.pickingMode = PickingMode.Position;
            var footer = Ui.Box("hbt-settings-footer", panel);
            footer.pickingMode = PickingMode.Position;
            Ui.Button(S("settings.done"), Close, footer);
            Ui.Show(_root, false);
        }

        string S(string key) => _content.GetString(key);

        public void Open(Action onClose)
        {
            if (!Visible)
                GameRoot.Instance?.Audio.PlaySfx("SFX/ui_open", 0.5f);
            _onClose = onClose;
            _working = GameSettings.Current.Clone();
            Visible = true;
            Ui.Show(_root, true);
            Render();
        }

        public void Close()
        {
            if (!Visible)
                return;
            Visible = false;
            GameRoot.Instance?.Audio.PlaySfx("SFX/ui_close", 0.5f);
            Ui.Show(_root, false);
            var cb = _onClose;
            _onClose = null;
            cb?.Invoke();
        }

        void Commit()
        {
            GameSettings.Apply(_working.Clone());
            _working = GameSettings.Current.Clone();
            Render();
        }

        void Render()
        {
            _rows.Clear();
            var x = _working;
            Cycle("settings.ui_size", S("settings.ui_size_" + x.UiTextSize), () => x.UiTextSize = (x.UiTextSize + 1) % 3);
            Toggle("settings.subtitle_background", x.SubtitleBackground, v => x.SubtitleBackground = v);
            Percent("settings.subtitle_opacity", x.SubtitleOpacity, v => x.SubtitleOpacity = v);
            Step("settings.subtitle_size", $"{Mathf.RoundToInt(x.SubtitleScale * 100f)}%", d => x.SubtitleScale = Round(x.SubtitleScale + d * 0.1f));
            Percent("settings.camera_shake", x.CameraShake, v => x.CameraShake = v);
            Percent("settings.head_bob", x.HeadBob, v => x.HeadBob = v);
            Toggle("settings.touch_toggle", x.TouchToggle, v => x.TouchToggle = v);
            Step("settings.sensitivity", $"{x.MouseSensitivity:0.0}×", d => x.MouseSensitivity = Round(x.MouseSensitivity + d * 0.1f));
            Toggle("settings.invert_y", x.InvertY, v => x.InvertY = v);
            Percent("settings.volume_master", x.MasterVolume, v => x.MasterVolume = v);
            Percent("settings.volume_music", x.MusicVolume, v => x.MusicVolume = v);
            Percent("settings.volume_sfx", x.SfxVolume, v => x.SfxVolume = v);
            Percent("settings.volume_ambience", x.AmbienceVolume, v => x.AmbienceVolume = v);
            Percent("settings.volume_vo", x.VoVolume, v => x.VoVolume = v);
            Toggle("settings.always_translate", x.AlwaysTranslate, v => x.AlwaysTranslate = v);
            Cycle("settings.tier", TierLabel(x.Tier), () => x.Tier = NextTier(x.Tier));
            Cycle("settings.fps_limit", FpsLabel(x.FpsLimit), () => x.FpsLimit = NextFps(x.FpsLimit));
            Toggle("settings.unlock_all", x.UnlockAllChapters, v => x.UnlockAllChapters = v);
        }

        static float Round(float v) => Mathf.Round(v * 10f) / 10f;

        VisualElement Row(string key)
        {
            var row = Ui.Box("hbt-settings-row", _rows);
            row.name = key;
            row.pickingMode = PickingMode.Position;
            Ui.Text("hbt-settings-label", row, S(key));
            return row;
        }

        void Step(string key, string value, Action<int> change)
        {
            var row = Row(key);
            Ui.Button(S("settings.decrease"), () => { change(-1); Commit(); }, row, "hbt-settings-step");
            Ui.Text("hbt-settings-value", row, value);
            Ui.Button(S("settings.increase"), () => { change(1); Commit(); }, row, "hbt-settings-step");
        }

        void Percent(string key, float value, Action<float> set)
        {
            Step(key, $"{Mathf.RoundToInt(value * 100f)}%", d => set(Mathf.Clamp01(Round(value + d * 0.1f))));
        }

        void Toggle(string key, bool value, Action<bool> set)
        {
            var row = Row(key);
            Ui.Button(S(value ? "settings.on" : "settings.off"), () => { set(!value); Commit(); }, row, "hbt-settings-toggle");
        }

        void Cycle(string key, string value, Action next)
        {
            var row = Row(key);
            Ui.Button(value, () => { next(); Commit(); }, row, "hbt-settings-toggle");
        }

        string TierLabel(string tier)
        {
            if (tier == "Laptop")
                return S("settings.tier_laptop");
            if (tier == "PC")
                return S("settings.tier_pc");
            return S("settings.tier_auto");
        }

        string FpsLabel(int limit)
        {
            if (limit == 0)
                return S("settings.fps_auto");
            if (limit < 0)
                return S("settings.fps_uncapped");
            return limit.ToString();
        }

        static int NextFps(int limit)
        {
            int i = Array.IndexOf(GameSettings.FpsChoices, limit);
            return GameSettings.FpsChoices[(i + 1) % GameSettings.FpsChoices.Length];
        }

        static string NextTier(string tier)
        {
            if (string.IsNullOrEmpty(tier))
                return "Laptop";
            return tier == "Laptop" ? "PC" : null;
        }
    }
}
