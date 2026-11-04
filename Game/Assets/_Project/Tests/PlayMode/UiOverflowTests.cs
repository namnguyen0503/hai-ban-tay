using System.Collections;
using System.Collections.Generic;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Notebook;
using HaiBanTay.Shift;
using HaiBanTay.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Mọi màn UI ở 1920×1080: chữ nằm trong vùng an toàn 5%, không tràn khỏi khung của chính nó hay khung cha.
    /// UI được vẽ vào RenderTexture 1920×1080 (ScreenshotCapture) nên kích thước panel không phụ thuộc cửa sổ batchmode.
    /// </summary>
    public class UiOverflowTests
    {
        const float W = ScreenshotCapture.Width;
        const float H = ScreenshotCapture.Height;
        const float Tolerance = 1.5f;

        Rect _safe;

        GameRoot _root;
        readonly List<string> _problems = new List<string>();
        int _checkedLabels;

        [TearDown]
        public void TearDown()
        {
            ScreenshotCapture.RestoreUi();
            PlayTestUtil.ExitSim();
            _root?.UI.Pause.Show(false);
            GameSettings.ResetForTests();
        }

        static IEnumerator Settle(float seconds = 0.4f)
        {
            float end = Time.realtimeSinceStartup + seconds;
            yield return null;
            while (Time.realtimeSinceStartup < end)
                yield return null;
        }

        static bool Shown(VisualElement e)
        {
            for (var v = e; v != null; v = v.parent)
            {
                var rs = v.resolvedStyle;
                if (rs.display == DisplayStyle.None || rs.visibility == Visibility.Hidden || rs.opacity <= 0.01f)
                    return false;
            }
            return true;
        }

        static bool Contains(Rect outer, Rect inner) =>
            inner.xMin >= outer.xMin - Tolerance && inner.xMax <= outer.xMax + Tolerance &&
            inner.yMin >= outer.yMin - Tolerance && inner.yMax <= outer.yMax + Tolerance;

        static string Short(string s) => s.Length > 40 ? s.Substring(0, 40) + "…" : s;

        void Check(string context, Label only = null)
        {
            _root.UI.Root.Query<Label>().ForEach(l =>
            {
                if (only != null && l != only)
                    return;
                if (string.IsNullOrEmpty(l.text) || !Shown(l))
                    return;
                var r = l.worldBound;
                if (r.width < 1f || r.height < 1f)
                    return;
                _checkedLabels++;
                string id = $"[{context}] \"{Short(l.text)}\"";
                if (!Contains(_safe, r))
                    _problems.Add($"{id} ngoài vùng an toàn: {r}");

                var c = l.contentRect;
                // Nhãn co theo chữ có bề ngang đúng bằng chữ; ở panel scale lẻ (cỡ chữ 120%, tỉ lệ điện thoại) đo lại đúng bề ngang đó thì
                // sai số làm tròn bắt xuống dòng dù layout và ảnh vẫn một dòng (QA-0104, LargeTextShots). Dư 1 px cho làm tròn.
                var need = l.MeasureTextSize(l.text, c.width + 1f, VisualElement.MeasureMode.AtMost, 0f, VisualElement.MeasureMode.Undefined);
                if (need.x > c.width + Tolerance || need.y > c.height + Tolerance)
                    _problems.Add($"{id} tràn khung chữ: cần {need}, có {c.size}");

                var p = l.parent;
                if (p != null && p.worldBound.width > 1f && !Contains(p.worldBound, r))
                    _problems.Add($"{id} tràn khỏi khung cha {p.worldBound}: {r}");
            });
        }

        IEnumerator Card(IEnumerator show, string context)
        {
            var co = _root.StartCoroutine(show);
            yield return Settle(1.2f);
            Check(context);
            _root.StopCoroutine(co);
            _root.UI.Card.Hide();
        }

        // Thời gian chạy tăng theo số thoại, thẻ và trang (mỗi mục chờ thời gian thật để layout ổn định).
        [UnityTest, Timeout(600000)]
        public IEnumerator AllScreensFitSafeAreaAt1080p() => AllScreensFit(1920, 1080, 1);

        [UnityTest, Timeout(600000)]
        public IEnumerator AllScreensFitAtLargeText1080p() => AllScreensFit(1920, 1080, 2);

        [UnityTest, Timeout(600000)]
        public IEnumerator AllScreensFitAtLargeTextPhone() => AllScreensFit(2760, 1256, 2);

        IEnumerator AllScreensFit(int width, int height, int textSize)
        {
            _problems.Clear();
            _checkedLabels = 0;
            _root = PlayTestUtil.Root();
            _root.ResetProgress();
            yield return Settle();
            ScreenshotCapture.RedirectUi(width, height);
            var settings = GameSettings.Current.Clone();
            settings.UiTextSize = textSize;
            GameSettings.Apply(settings);
            yield return Settle();
            var ui = _root.UI;
            var bounds = ui.Root.worldBound;
            _safe = new Rect(bounds.width * 0.05f, bounds.height * 0.05f, bounds.width * 0.9f, bounds.height * 0.9f);
            var content = _root.Content;
            string S(string key) => content.GetString(key);
            FrenchPair Word(string id) => content.TryGetWord(id, out var w) ? w : null;
            // Chữ dài nhất có thể hiện ra: mỗi từ {w:id} lấy bản dài hơn giữa tiếng Pháp và tiếng Việt.
            string Long(string text) => Vocabulary.Longest(text, Word);

            if (width == W && height == H && textSize == 1)
                Assert.AreEqual(W, ui.Root.worldBound.width, 1f, "panel không ở 1920×1080, kiểm tra không có ý nghĩa");
            Assert.AreEqual((float)width / height, bounds.width / bounds.height, 0.001f, "panel phải giữ đúng tỷ lệ yêu cầu");

            // HUD: prompt, gợi ý, vòng tiến độ.
            foreach (var kv in content.Ui.Strings)
            {
                if (kv.Key.StartsWith("prompt."))
                {
                    ui.Prompt.SetWorldPrompt(Long(kv.Value));
                    ui.Prompt.SetProgress(0.5f);
                }
                else if (kv.Key.StartsWith("hint."))
                    ui.Prompt.ShowHint(Long(kv.Value), 30f);
                else
                    continue;
                yield return Settle(0.1f);
                Check(kv.Key);
            }
            ui.Prompt.Clear();

            // Phụ đề.
            foreach (var line in content.Lines)
            {
                content.Subtitles.Speakers.TryGetValue(line.Speaker, out var speaker);
                ui.Subtitles.Show(speaker, line.Fr, Long(line.Vi));
                yield return Settle(0.1f);
                Check("subtitle " + line.Id);
            }
            ui.Subtitles.Hide();

            // Thông báo trang mới + thẻ tư liệu góc màn hình.
            foreach (var page in content.Pages)
            {
                ui.Toast.Hide(); // Mỗi trang phải được đo riêng, không đọc lại toast đầu trong hàng đợi.
                ui.Toast.Show(S("notebook.new_page") + ": " + page.Title);
                yield return Settle(0.1f);
                Check("toast " + page.Id);
            }
            ui.Toast.Hide();
            foreach (var card in content.Cards)
            {
                ui.Cards.Clear();
                ui.Cards.Show(card.Id);
                yield return Settle(0.8f);
                Check("card " + card.Id);
            }
            ui.Cards.Clear();

            // Sổ: mọi trang (bật mọi cờ để thân trang dài nhất), ghép từ, nối trang.
            foreach (var page in content.Pages)
            {
                _root.Notebook.Unlock(page.Id);
                if (page.AppendIfFlag != null)
                    _root.Flags.Set(page.AppendIfFlag.Flag);
            }
            ui.Toast.Hide();
            foreach (var page in content.Pages)
            {
                ui.Notebook.OpenAt(page.Id);
                yield return Settle(0.2f);
                Check("notebook " + page.Id);
            }
            ui.Notebook.Close();
            var sets = new List<string>();
            foreach (var p in content.FrenchWords.Pairs)
                if (!string.IsNullOrEmpty(p.Set) && !sets.Contains(p.Set))
                    sets.Add(p.Set);
            Assert.IsNotEmpty(sets, "chưa có bài học từ nào");
            foreach (var set in sets)
            {
                ui.Notebook.OpenMatch(new MatchWordsLogic(content.WordsInSet(set)), null);
                yield return Settle(0.2f);
                Check("notebook match " + set);
                ui.Notebook.Close();
            }
            // Học buổi tối (M11): tô chữ, điền từ, viết câu của mọi bài; chú thích tô chữ dài nhất (cả từ, chữ đang tô gạch chân).
            foreach (var lesson in content.Study.Lessons)
            {
                switch (lesson.Kind)
                {
                    case EveningData.Trace:
                        string longestWord = string.Empty;
                        foreach (var it in lesson.Items)
                            if ((it.Text ?? string.Empty).Length > longestWord.Length)
                                longestWord = it.Text;
                        string caption = $"<u><color=#964A28>{longestWord[0]}</color></u>{longestWord.Substring(1)}";
                        ui.Notebook.OpenTrace(StudyPlanner.BuildTrace(content, lesson.Items), () => caption, null);
                        break;
                    case EveningData.Fill:
                        ui.Notebook.OpenFill(StudyPlanner.BuildFill(content, lesson.Items), null);
                        break;
                    case EveningData.Sentence:
                        ui.Notebook.OpenSentence(StudyPlanner.BuildSentence(lesson.Items), null);
                        break;
                    default:
                        continue;
                }
                yield return Settle(0.2f);
                Check($"notebook {lesson.Kind} {lesson.Id}");
                ui.Notebook.Close();
            }
            ui.Notebook.OpenLink(new PageLinkController(content.Notebook.LinkTarget, _root.Events), null);
            ui.Notebook.HighlightPages(content.Notebook.LinkTarget.A, content.Notebook.LinkTarget.B);
            ui.Notebook.SetFooter(S("hint.link_pages"));
            yield return Settle(0.2f);
            Check("notebook link");
            ui.Notebook.SetFooter(string.Empty);
            ui.Notebook.Close();

            // Màn chữ toàn màn hình: mở đầu, thẻ giờ, thẻ kết, tên game, credits, màn chờ.
            var e = content.Ending;
            yield return Card(ui.Card.Show(S("card.opening"), 60f), "card.opening");
            yield return Card(ui.Card.Show(S("card.time_4am"), 60f), "card.time_4am");
            yield return Card(ui.Card.Show($"{S("card.time_evening")} · 23:59", 60f), "card.time_evening");
            foreach (var day in content.Days.Days)
                yield return Card(ui.Card.ShowDated(day.Title, $"{day.Leg} · {ShiftClockLogic.Format(day.ClockStart)}", 60f), "day " + day.Id);
            foreach (var key in new[] { "card.prologue.place", "card.prologue.later", "card.prologue.days", "card.prologue.noon", "card.job.departure", "card.mrs.evening" })
                yield return Card(ui.Card.Show(S(key), 60f), key);
            for (int i = 0; i < 7; i++)
                yield return Card(ui.Card.Show(S("end.mai." + i) + "\n\n" + S("end.mai.source"), 60f), "Ông Mai " + i);
            yield return Card(ui.Card.Show(e.Narrator, 60f), "narrator");
            for (int i = 0; i < e.EndCards.Count; i++)
                yield return Card(ui.Card.ShowDated(e.EndCards[i].Date, e.EndCards[i].Text, 60f), "endcard " + i);
            yield return Card(ui.Card.ShowTitle(e.Title, 60f), "title");
            // Credits chia trang: kiểm từng trang.
            var creditLists = new[]
            {
                ("credits.sources", Ending.EndCards.SourceLines(content)),
                ("credits.assets", Ending.EndCards.AssetLines(_root.Config.credits)),
            };
            foreach (var (key, list) in creditLists)
            {
                var pages = Narrative.OpeningCard.Paginate(list);
                for (int i = 0; i < pages.Count; i++)
                    yield return Card(ui.Card.ShowLines(S(key), pages[i], 60f), $"{key} {i + 1}/{pages.Count}");
            }
            ui.Card.ShowStatic(e.Title, S("end.quit_hint"));
            yield return Settle(0.2f);
            Check("end static");
            ui.Card.Hide();
            // Disclaimer: màn "Chơi mới" (chữ + dòng gợi ý phím) và dòng credits.
            ui.Card.ShowStatic(S("disclaimer.body"), S("disclaimer.continue"));
            yield return Settle(0.2f);
            Check("disclaimer");
            ui.Card.Hide();
            yield return Card(ui.Card.Show(S("credits.disclaimer"), 60f), "credits.disclaimer");

            // Bản đồ chuyển cảnh (chụp gần cuối, khi mọi điểm dừng đã hiện).
            foreach (var t in content.MapTransitions.Transitions)
            {
                var co = _root.StartCoroutine(ui.Map.Play(t));
                yield return Settle(Mathf.Max(0.5f, t.Duration * 0.9f));
                Check("map " + t.Id);
                _root.StopCoroutine(co);
                ui.Map.Hide();
            }

            // HUD ca làm: đủ mọi loại lệnh, số lượng hai chữ số, nhãn dài nhất (từ tiếng Pháp chưa học).
            var board = new OrderBoardLogic();
            foreach (var kind in OrderKinds.All)
                board.Add(kind, 12);
            string longestTitle = string.Empty;
            foreach (var day in content.Days.Days)
                if (day.Title.Length > longestTitle.Length)
                    longestTitle = day.Title;
            ui.Shift.SetDay(longestTitle);
            ui.Shift.SetClock("23:59");
            ui.Shift.SetOrders(board.Orders, o => Long(S(OrderKinds.LabelKey(o.Kind))));
            // Mũi tên chỉ đường (M11): nhãn đích dài nhất, khoảng cách ba chữ số.
            string longestGo = string.Empty;
            foreach (var key in new[] { "hud.go_galley", "hud.go_hold", "hud.go_coal", "hud.go_basket", "hud.go_seat" })
                if (S(key).Length > longestGo.Length)
                    longestGo = S(key);
            ui.Shift.SetPointer(string.Format(S("hud.pointer"), longestGo, 123.4f), 35f);
            ui.Shift.Show(true);
            yield return Settle(0.3f);
            Check("shift hud");
            ui.Shift.Show(false);
            ui.Shift.Clear();

            // Menu chính, chọn chương (mọi ngày), cài đặt.
            ui.Menu.SetTitle(S("menu.title"), S("menu.subtitle"));
            ui.Menu.ShowButtons(new (string, System.Action)[]
            {
                (S("menu.continue"), null), (S("menu.new"), () => { }), (S("menu.chapters"), () => { }),
                (S("menu.settings"), () => { }), (S("menu.quit"), () => { }),
            });
            yield return Settle(0.3f);
            Check("menu main");
            var chapters = new List<(string, System.Action)> { (S("scene.01.name"), () => { }) };
            foreach (var day in content.Days.Days)
                chapters.Add(($"{day.Title} · {day.Leg}", null));
            chapters.Add((S("scene.03.name"), null));
            chapters.Add((S("menu.back"), () => { }));
            ui.Menu.ShowButtons(chapters);
            yield return Settle(0.3f);
            Check("menu chapters");
            ui.Menu.Hide();
            ui.Settings.Open(null);
            yield return Settle(0.3f);
            // Mỗi nhãn đều phải qua cùng phép đo; đưa nhãn trong vùng cuộn vào khung trước khi đo.
            foreach (var label in ui.Settings.Root.Query<Label>().ToList())
            {
                label.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(label);
                yield return Settle(0.05f);
                Check("settings", label);
            }
            ui.Settings.Close();

            ui.Pause.Show(true);
            yield return Settle(0.2f);
            Check("pause");
            ui.Pause.Show(false);

            Debug.Log($"UiOverflow: đã kiểm {_checkedLabels} nhãn");
            Assert.Greater(_checkedLabels, 50, "kiểm quá ít nhãn, layout có thể chưa chạy");
            Assert.IsEmpty(_problems, string.Join("\n", _problems));
        }
    }
}
