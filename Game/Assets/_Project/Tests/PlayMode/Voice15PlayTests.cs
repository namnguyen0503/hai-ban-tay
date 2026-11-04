using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HaiBanTay.Ending;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Core;
using HaiBanTay.Narrative;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;

namespace HaiBanTay.Tests
{
    public sealed class Voice15PlayTests
    {
        readonly List<string> _starts = new List<string>();
        readonly List<string> _failures = new List<string>();
        string _active, _scene;
        SceneDirector _director;
        float _startedAt, _expectedSeconds;
        GameRoot _root;

        [TearDown]
        public void CleanUp()
        {
            Application.logMessageReceived -= Observe;
            SceneFlow.FromPrologue = false;
            Time.captureDeltaTime = 0f;
            PlayTestUtil.ExitSim();
        }

        void Observe(string message, string stack, LogType type)
        {
            if (!message.StartsWith("VO ") || SceneManager.GetActiveScene().name != _scene) return;
            // Ca xong thì game tự tải ngày kế (cùng tên cảnh): câu của director khác không thuộc kịch bản đang đếm.
            if (_director != null && SceneDirector.Current != _director) { _active = null; return; }
            var parts = message.Split(' ');
            string id = parts[3];
            var source = _root.Audio.transform.Find("Audio_VO").GetComponent<AudioSource>();
            if (parts[1] == "START")
            {
                if (_active != null) _failures.Add($"Chồng giọng: {_active} → {id}");
                _active = id;
                _startedAt = Time.realtimeSinceStartup;
                _expectedSeconds = source.clip != null ? source.clip.length / source.pitch : 0f;
                _starts.Add(id);
                if (source.clip != _root.Audio.Clip(_root.Content.GetLine(id).Audio) || !source.isPlaying)
                    _failures.Add($"Sai clip hoặc AudioSource không phát: {id}");
                if (!_root.UI.Subtitles.Visible) _failures.Add($"Chưa hiện chữ: {id}");
            }
            else
            {
                if (parts[1] == "END" && Mathf.Abs(Time.realtimeSinceStartup - _startedAt - _expectedSeconds) > 0.15f + Time.unscaledDeltaTime)
                    _failures.Add($"Sai thời lượng phát thực: {id}");
                if (parts[1] == "END" && (_root.UI.Subtitles.Visible || source.isPlaying))
                    _failures.Add($"Hết câu nhưng chữ/tiếng còn: {id}");
                _active = null;
            }
        }

        void Record(GameRoot root, string scene)
        {
            _root = root;
            root.Subtitles.StopAll();
            _scene = scene;
            _active = null;
            _director = null;
            _starts.Clear(); _failures.Clear();
            Application.logMessageReceived += Observe;
        }

        void AssertRecording(string label)
        {
            File.WriteAllLines(Path.GetFullPath(Path.Combine(Application.dataPath, "../../Logs/voice15_" + label + ".log")), _starts);
            Assert.IsEmpty(_failures, string.Join("\n", _failures));
            Assert.IsNotEmpty(_starts, label);
        }

        static void Add(Dictionary<string, int> counts, string id)
        {
            if (!string.IsNullOrEmpty(id)) counts[id] = counts.TryGetValue(id, out int n) ? n + 1 : 1;
        }

        static readonly int[] Scenarios = { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
        static readonly int[] Days = { 1, 2, 3, 4, 5, 6 };

        [UnityTest, Timeout(300000)]
        public IEnumerator NpcSequencesPlayEveryLineThroughProductionSay([ValueSource(nameof(Days))] int day)
        {
            var root = PlayTestUtil.Root();
            yield return PlayTestUtil.WaitUntil(() => !root.Scenes.IsLoading, 30f);
            PlayTestUtil.EnterSim(20f);
            ChapterStart.PrepareDay(root, day, day, false);
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            Assert.AreEqual(day, d.Day.Number);
            d.StopAllCoroutines(); d.enabled = false;
            d.cast.StopAllCoroutines();
            root.Subtitles.StopAll();
            // DayCast.Say bỏ thoại khi bật Sim; kiểm đường thật, giữ nguyên nguyên chuỗi JSON.
            Sim.Enabled = false;
            var sequences = d.Day.Npcs.Select(beat => beat.Lines).ToList();
            sequences.AddRange(d.Day.Phases.Select(phase => phase.AfterLines));
            if (d.Day.Evening != null) sequences.Add(d.Day.Evening.After);
            var expected = sequences.SelectMany(lines => lines).ToArray();
            Assert.IsNotEmpty(expected);
            Record(root, SceneFlow.GameScenes[1]);
            foreach (var lines in sequences)
                yield return d.cast.Say(lines);
            AssertRecording("npc_day_" + day);
            CollectionAssert.AreEqual(expected, _starts);
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator SceneScriptsHaveOneStartPerScheduledLine([ValueSource(nameof(Scenarios))] int scenario)
        {
            var root = PlayTestUtil.Root();
            yield return PlayTestUtil.WaitUntil(() => !root.Scenes.IsLoading, 30f);
            Assert.IsFalse(root.Scenes.IsLoading);
            root.ResetProgress();
            PlayTestUtil.EnterSim(20f);
            string scene = scenario == 0 ? SceneFlow.PrologueScene : scenario == 1 ? SceneFlow.GameScenes[0]
                : scenario == 8 ? SceneFlow.GameScenes[2] : SceneFlow.GameScenes[1];
            int day = scenario - 1;
            if (scenario >= 2 && scenario <= 7) ChapterStart.PrepareDay(root, day, day, false);
            SceneFlow.FromPrologue = false;
            Record(root, scene);
            yield return PlayTestUtil.LoadAndSettle(scene);
            var director = SceneDirector.Current;
            _director = director;
            var shift = director as ShiftDirector;
            if (scenario >= 2 && scenario <= 7)
            {
                Assert.IsNotNull(shift);
                Assert.AreEqual(day, shift.Day.Number, "Danh tính ngày phải đúng fixture.");
            }
            var ending = director as EndingDirector;
            int payCalls = ending != null ? ending.purser.queue.Count : 0;
            bool done = false;
            float until = Time.realtimeSinceStartup + 480f;
            while (Time.realtimeSinceStartup < until)
            {
                done = scenario == 8 ? root.Events.HasFired(EventIds.S3End)
                    : shift != null ? shift.Finished
                    : SceneManager.GetActiveScene().name != scene;
                if (done) break;
                yield return null;
            }
            Assert.IsTrue(done, $"Scenario {scenario} kẹt ở {SceneDirector.Current?.Beat}");
            // Ca xong thì game tải ngay ngày kế (cùng tên cảnh): thôi ghi ở đây. Sim xong ca nhanh hơn tiếng, câu cuối có thể còn
            // trong hàng chờ: tính là đã lên lịch đúng một lần.
            Application.logMessageReceived -= Observe;
            var queued = root.Content.Lines.Select(l => l.Id).Where(id => root.Subtitles.Pending(id) && !_starts.Contains(id)).ToList();
            _starts.AddRange(queued);
            AssertRecording("scene_" + scenario);
            var allowed = new Dictionary<string, int>();
            if (shift != null)
            {
                foreach (var phase in shift.Day.Phases)
                {
                    Add(allowed, phase.Line);
                    foreach (var id in phase.AfterLines) Add(allowed, id);
                }
                foreach (var beat in shift.Day.Npcs)
                {
                    Add(allowed, beat.Call);
                    foreach (var id in beat.Lines) Add(allowed, id);
                }
                Add(allowed, shift.Day.Evening?.Line);
                if (shift.Day.Evening != null)
                    foreach (var id in shift.Day.Evening.After) Add(allowed, id);
            }
            if (scenario == 8)
            {
                foreach (var id in root.Content.Ending.FarewellReplies) Add(allowed, id);
                allowed["mrs_pay_next"] = payCalls;
                Assert.AreEqual(payCalls, _starts.Count(id => id == "mrs_pay_next"), "Mỗi người trong hàng được gọi đúng một lần.");
            }
            foreach (var group in _starts.GroupBy(id => id))
                Assert.LessOrEqual(group.Count(), allowed.TryGetValue(group.Key, out int n) ? n : 1,
                    $"Scenario {scenario}: {group.Key} bị gọi lại ngoài số lượt kịch bản");
            if (scenario == 0)
            {
                var expected = new[] { "pro_le_show", "pro_ba_light", "pro_ba_cinema", "pro_ba_water", "pro_le_water", "pro_le_buy_ice", "pro_le_ice", "pro_ba_ice",
                    "pro_ba_patriot", "pro_le_patriot", "pro_ba_secret", "pro_le_secret", "pro_ba_abroad", "pro_ba_abroad2", "pro_ba_invite", "pro_ba_invite2",
                    "pro_le_money", "pro_ba_hands", "pro_ba_work", "pro_ba_work2", "pro_le_agree", "pro_le_family" };
                CollectionAssert.AreEqual(expected, _starts);
            }
            if (scenario == 1)
                foreach (var id in new[] { "job_ba_ask", "job_staff_no", "job_staff_whisper", "job_mai_lead", "job_mai_lead2",
                    "job_captain_work", "job_ba_any", "job_captain_accept", "job_captain_name", "job_ba_name", "chef_depart" })
                    Assert.AreEqual(1, _starts.Count(x => x == id), id);
            if (shift != null)
                foreach (var phase in shift.Day.Phases)
                    if (!string.IsNullOrEmpty(phase.Line)) CollectionAssert.Contains(_starts, phase.Line);
            if (scenario == 8)
                foreach (var id in new[] { "mrs_arrival", "mrs_pay_here", "mrs_pay_ba", "mrs_waiter_voila", "mrs_ba_thanks", "mrs_mai_monsieur", "mrs_mai_monsieur2" })
                    Assert.AreEqual(1, _starts.Count(x => x == id), id);
            yield return PlayTestUtil.WaitUntil(() => !root.Scenes.IsLoading, 30f);
            Assert.IsFalse(root.Scenes.IsLoading, "Chuyển cảnh phải xong trước fixture kế tiếp.");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator MealAndChieuInteractionsCannotStartTwice()
        {
            yield return ChoreTestUtil.Galley();
            var root = GameRoot.Instance;
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            d.enabled = false;
            d.cast.StopAllCoroutines();
            PlayTestUtil.EnterSim(20f);
            d.people.crewMeal.SetActive(true);
            Record(root, SceneFlow.GameScenes[1]);
            foreach (var kind in new[] { OrderKinds.Soup, OrderKinds.Chieu })
            {
                var encounter = d.storyStations.OfType<VoyageEncounter>().First(x => x.Kind == kind);
                encounter.OnOrder(1);
                ChoreTestUtil.Look(encounter.stand, encounter.target.transform.position);
                encounter.OnInteract(encounter.target, PlayerRig.Current.interactor);
                encounter.OnInteract(encounter.target, PlayerRig.Current.interactor);
                yield return PlayTestUtil.WaitUntil(() => !encounter.Busy, 90f);
                Assert.IsFalse(encounter.Busy, kind);
                if (kind == OrderKinds.Soup)
                {
                    Assert.AreEqual(1, encounter.Stage);
                    encounter.OnInteract(encounter.target, PlayerRig.Current.interactor);
                    encounter.OnInteract(encounter.target, PlayerRig.Current.interactor);
                    yield return PlayTestUtil.WaitUntil(() => !encounter.Busy, 90f);
                }
                Assert.AreEqual(0, encounter.Pending, kind);
            }
            AssertRecording("encounters");
            CollectionAssert.AreEqual(new[] { "mai_meal_1", "ba_meal_1", "mai_meal_2", "crew_spoon", "ba_meal_2", "mai_meal_3", "mai_meal_4", "ba_meal_3", "mai_meal_5",
                "chieu_1", "chieu_2", "chieu_3", "ba_chieu" }, _starts);
        }

        [UnityTest]
        public IEnumerator DuplicatePendingRequestsShareOnePlaybackAndCanRepeatLater()
        {
            var root = PlayTestUtil.Root();
            yield return PlayTestUtil.WaitUntil(() => !root.Scenes.IsLoading, 30f);
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.MenuScene);
            root.Subtitles.StopAll();
            int before = root.Subtitles.PlayedCount;
            root.Subtitles.Play("pro_le_agree");
            root.Subtitles.Play("pro_le_agree");
            yield return root.Subtitles.PlayAndWait("pro_le_agree");
            Assert.AreEqual(before + 1, root.Subtitles.PlayedCount, "Yêu cầu trùng lúc đang chờ chỉ phát một lần.");
            yield return root.Subtitles.PlayAndWait("pro_le_agree");
            Assert.AreEqual(before + 2, root.Subtitles.PlayedCount, "Lượt mới vẫn được đọc lại câu đã xong.");
        }

        [UnityTest]
        public IEnumerator WaiterFinishesItsOwnLineAndStopReleasesWaiters()
        {
            var root = PlayTestUtil.Root();
            yield return PlayTestUtil.WaitUntil(() => !root.Scenes.IsLoading, 30f);
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.MenuScene);
            root.Subtitles.StopAll();
            var first = root.Subtitles.PlayAndWait("pro_le_agree");
            Assert.IsTrue(first.MoveNext());
            root.Subtitles.Play("pro_ba_work2");
            yield return PlayTestUtil.WaitUntil(() => !first.MoveNext(), 10f);
            Assert.IsFalse(first.MoveNext(), "Không chờ thêm các yêu cầu đến sau câu của mình.");
            Assert.IsTrue(root.Subtitles.IsPlaying);
            var second = root.Subtitles.PlayAndWait("pro_ba_work2");
            Assert.IsTrue(second.MoveNext());
            root.Subtitles.StopAll();
            Assert.IsFalse(second.MoveNext(), "Hủy hàng đợi phải giải phóng waiter.");
            Assert.IsNull(root.Subtitles.CurrentLineId);
            Assert.IsFalse(root.UI.Subtitles.Visible);
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator EveryPublishedLineRoutesToItsOwnAudioSourceClip()
        {
            var root = PlayTestUtil.Root();
            yield return PlayTestUtil.WaitUntil(() => !root.Scenes.IsLoading, 30f);
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.MenuScene);
            PlayTestUtil.EnterSim(20f);
            Record(root, SceneFlow.MenuScene);
            foreach (var line in root.Content.Subtitles.Lines)
                yield return root.Subtitles.PlayAndWait(line.Id);
            AssertRecording("all_lines");
            CollectionAssert.AreEqual(root.Content.Subtitles.Lines.Select(line => line.Id).ToArray(), _starts);
        }

        [UnityTest]
        public IEnumerator SubtitleEndsWithClipEvenWhenGameRunsFast()
        {
            var root = PlayTestUtil.Root();
            yield return PlayTestUtil.WaitUntil(() => !root.Scenes.IsLoading, 30f);
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.MenuScene);
            root.Subtitles.StopAll();
            Time.timeScale = 20f;
            root.Subtitles.Play("pro_le_agree");
            var clip = root.Audio.Clip("VO/pro_le_agree");
            var vo = root.Audio.transform.Find("Audio_VO").GetComponent<AudioSource>();
            Assert.AreSame(clip, vo.clip);
            float start = Time.realtimeSinceStartup;
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.IsTrue(root.UI.Subtitles.Visible, "TimeScale không được làm chữ hết trước tiếng.");
            yield return PlayTestUtil.WaitUntil(() => !root.UI.Subtitles.Visible, clip.length + 1f);
            Assert.AreEqual(clip.length, Time.realtimeSinceStartup - start, 0.15f, "Không cộng duration hoặc đệm 0,4s vào VO.");
            Assert.IsFalse(vo.isPlaying, "Hết phụ đề thì nguồn thoại cũng phải hết.");
        }
    }

    [Category("Screenshots"), Explicit]
    public sealed class Voice15Shots
    {
        [TearDown]
        public void Done()
        {
            ScreenshotCapture.RestoreUi();
            PlayTestUtil.ExitSim();
        }
        [UnityTest, Category("Screenshots"), Explicit]
        public IEnumerator SubtitleShot()
        {
            var root = PlayTestUtil.Root();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.PrologueScene);
            var director = Object.FindAnyObjectByType<PrologueDirector>();
            director.StopAllCoroutines();
            root.Subtitles.StopAll();
            root.UI.Card.Hide(); root.UI.SetFade(0f);
            director.le.TeleportTo(director.talkLe.position, director.talkLe.eulerAngles.y);
            var rig = PlayerRig.Current;
            rig.motor.Teleport(director.talkPlayer.position, director.talkPlayer.eulerAngles.y);
            rig.look.AssistTarget = null;
            rig.SetControl(false);
            ScreenshotCapture.RedirectUi();
            root.Subtitles.Play("pro_ba_abroad");
            yield return ScreenshotCapture.Capture("voice15_subtitle");
            ScreenshotCapture.RestoreUi();
        }

    }

}
