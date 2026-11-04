using System.Collections;
using HaiBanTay.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// QA-0104: UiOverflowTests báo phụ đề cần hai dòng ở cỡ chữ UI lớn (panel scale 1,2) dù bề ngang đủ. Chụp phụ đề ở ba cỡ chữ (16:9 và
    /// tỉ lệ điện thoại) và ghi số đo layout/MeasureTextSize để phân định chữ bị cắt thật hay phép đo lệch.
    /// </summary>
    [Category("Screenshots"), Explicit]
    public sealed class LargeTextShots
    {
        [TearDown]
        public void TearDown()
        {
            ScreenshotCapture.RestoreUi();
            GameSettings.ResetForTests();
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator SubtitlesAtEveryTextSize()
        {
            var root = PlayTestUtil.Root();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.MenuScene);
            root.UI.Menu.Hide();
            foreach (var (w, h) in new[] { (1920, 1080), (2760, 1256) })
                foreach (int size in new[] { 1, 2 })
                {
                    ScreenshotCapture.RedirectUi(w, h);
                    var settings = GameSettings.Current.Clone();
                    settings.UiTextSize = size;
                    GameSettings.Apply(settings);
                    var line = root.Content.GetLine("chef_fire");
                    root.UI.Subtitles.Show("Quản bếp", line.Fr, Vocabulary.Render(line.Vi));
                    for (int i = 0; i < 4; i++) yield return null;
                    foreach (var l in root.UI.Subtitles.Root.Query<Label>().ToList())
                    {
                        var c = l.contentRect;
                        var need = l.MeasureTextSize(l.text, c.width, VisualElement.MeasureMode.AtMost, 0f, VisualElement.MeasureMode.Undefined);
                        Debug.Log($"LARGE_TEXT {w}x{h} cỡ {size} \"{l.text}\": layout {c.size} cần {need} font {l.resolvedStyle.fontSize}");
                    }
                    yield return ScreenshotCapture.Capture($"largetext_{w}x{h}_{size}", w, h);
                    root.UI.Subtitles.Hide();
                }
        }

        /// <summary>QA-0106: tên game ở menu chọn chương và bảng nối trang (năm thẻ một hàng) ở hai cỡ chữ, 16:9 và tỉ lệ điện thoại.</summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator MenuAndLinkBoardAtEveryTextSize()
        {
            var root = PlayTestUtil.Root();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.MenuScene);
            var content = root.Content;
            foreach (var (w, h) in new[] { (1920, 1080), (2760, 1256) })
                foreach (int size in new[] { 1, 2 })
                {
                    ScreenshotCapture.RedirectUi(w, h);
                    var settings = GameSettings.Current.Clone();
                    settings.UiTextSize = size;
                    GameSettings.Apply(settings);
                    root.UI.Menu.SetTitle(content.GetString("menu.title"), content.GetString("menu.subtitle"));
                    var chapters = new System.Collections.Generic.List<(string, System.Action)> { (content.GetString("scene.01.name"), () => { }) };
                    foreach (var day in content.Days.Days)
                        chapters.Add(($"{day.Title} · {day.Leg}", null));
                    chapters.Add((content.GetString("scene.03.name"), null));
                    chapters.Add((content.GetString("menu.back"), () => { }));
                    root.UI.Menu.ShowButtons(chapters);
                    for (int i = 0; i < 6; i++) yield return null;
                    yield return ScreenshotCapture.Capture($"menu_{w}x{h}_{size}", w, h);
                    root.UI.Menu.Hide();

                    foreach (var page in content.Notebook.Pages)
                        if (page.Linkable)
                            root.Notebook.EnsureUnlocked(page.Id);
                    root.UI.Notebook.OpenLink(new Notebook.PageLinkController(content.Notebook.LinkTarget, root.Events), null);
                    root.UI.Notebook.SetFooter(content.GetString("hint.link_pages"));
                    for (int i = 0; i < 6; i++) yield return null;
                    yield return ScreenshotCapture.Capture($"link_{w}x{h}_{size}", w, h);
                    root.UI.Notebook.SetFooter(string.Empty);
                    root.UI.Notebook.Close();
                }
        }
    }
}
