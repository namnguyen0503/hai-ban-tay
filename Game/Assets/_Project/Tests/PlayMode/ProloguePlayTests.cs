using System.Collections;
using System.Collections.Generic;
using HaiBanTay.Core;
using HaiBanTay.Narrative;
using HaiBanTay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>00_Prologue: đi bộ thật tới điểm hội thoại; chạy cả prologue bằng Sim rồi vào 01 và bỏ qua beat giơ tay B1.2.</summary>
    public class ProloguePlayTests
    {
        [TearDown]
        public void TearDown()
        {
            var root = PlayTestUtil.Root();
            root.Input.SimMove = Vector2.zero;
            root.Input.SimRaise = false;
            SaveSystem.AllowInSim = false;
            SceneFlow.FromPrologue = false;
            Time.captureDeltaTime = 0f;
            PlayTestUtil.ExitSim();
        }

        [UnityTest]
        public IEnumerator PlayerWalksFromSpawnToTalkPoint()
        {
            var root = PlayTestUtil.Root();
            root.ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.PrologueScene);
            var rig = PlayerRig.Current;
            Assert.IsNotNull(rig);
            var director = Object.FindAnyObjectByType<PrologueDirector>();
            var zone = director.talkZone;
            float startX = rig.Position.x;
            // Thẻ "Sài Gòn, 1911" khóa điều khiển vài giây; xong thì đi bộ thật dọc vỉa hè (frame batchmode quá ngắn nên cố định 60 khung/giây).
            Time.captureDeltaTime = 1f / 60f;
            yield return PlayTestUtil.WaitUntil(() => root.Input.GameplayEnabled && director.Beat == "P1" && !root.UI.Card.Visible, 60f);
            Assert.IsTrue(root.Input.GameplayEnabled, "người chơi phải điều khiển được sau thẻ mở đầu");
            root.Input.SimMove = new Vector2(0f, 1f);
            yield return PlayTestUtil.WaitUntil(() => zone.PlayerInside || director.Beat != "P1", 90f);
            root.Input.SimMove = Vector2.zero;
            Assert.IsTrue(zone.PlayerInside || director.Beat != "P1", $"không tới được điểm hội thoại, kẹt ở {rig.Position}");
            Assert.Greater(rig.Position.x - startX, 15f, "phải tự đi một đoạn phố dài");
            // Anh Lê tới điểm đầu tiên rồi đứng chờ: Ba còn xa (quá leWaitRadius) thì chưa dạo phố, Ba lại gần thì mới bắt đầu.
            yield return PlayTestUtil.WaitUntil(() => director.LeArrived, 120f);
            Assert.IsTrue(director.LeArrived, "anh Lê phải tới điểm xem đầu tiên");
            var apart = rig.Position - director.le.Position;
            apart.y = 0f;
            if (apart.magnitude > director.leWaitRadius)
            {
                yield return new WaitForSeconds(1f);
                Assert.AreEqual("P1", director.Beat, "Ba còn xa thì anh Lê đứng chờ, chưa bắt đầu dạo phố");
                var near = director.le.Position - director.le.transform.forward * 1.5f;
                rig.motor.Teleport(near, Mathf.Atan2(director.le.Position.x - near.x, director.le.Position.z - near.z) * Mathf.Rad2Deg);
            }
            yield return PlayTestUtil.WaitUntil(() => director.Beat == "P1a", 60f);
            Assert.AreEqual("P1a", director.Beat, $"phải bắt đầu dạo phố khi anh Lê tới nơi và Ba lại gần (Ba {rig.Position}, Lê {director.le.Position}, điểm xem {director.discoveryLePoints[0].position}, vùng={zone.PlayerInside})");
            Assert.IsTrue(director.LeArrived);
            Assert.Less(Vector3.Distance(director.le.Position, director.discoveryLePoints[0].position), 0.6f, "anh Lê dừng ở điểm xem đầu tiên");
        }

        [UnityTest]
        public IEnumerator SimRunsPrologueIntoNhaRongAndSkipsHandsBeat()
        {
            var root = PlayTestUtil.Root();
            root.ResetProgress();
            SaveSystem.AllowInSim = true;
            var fired = PlayTestUtil.Record(root.Events);
            var beats = new List<string>();
            PlayTestUtil.EnterSim(20f);
            SceneManager.LoadScene(SceneFlow.PrologueScene);
            string last = null;
            float end = Time.realtimeSinceStartup + 120f;
            bool inNhaRong = false;
            while (Time.realtimeSinceStartup < end)
            {
                string beat = SceneDirector.Current != null ? SceneDirector.Current.Beat : null;
                if (beat != null && beat != last)
                    beats.Add(SceneManager.GetActiveScene().name + ":" + beat);
                last = beat;
                inNhaRong |= SceneManager.GetActiveScene().name == SceneFlow.GameScenes[0] && !root.Scenes.IsLoading;
                if (inNhaRong && beats.Contains("01_NhaRong:B1.3"))
                    break;
                yield return null;
            }
            string all = string.Join(", ", beats);
            Assert.IsTrue(inNhaRong, "prologue phải kết thúc ở 01. Beat: " + all);
            foreach (var p in new[] { "P1", "P1a", "P1b", "P1c", "P2", "P3", "P4" })
                Assert.IsTrue(beats.Contains("00_Prologue:" + p), $"thiếu beat {p}: {all}");
            Assert.IsFalse(beats.Contains("01_NhaRong:B1.1"), "01 từ prologue không có thẻ mở đầu B1.1: " + all);
            Assert.IsFalse(beats.Contains("01_NhaRong:B1.2"), "01 từ prologue phải bỏ beat giơ tay B1.2: " + all);
            Assert.AreEqual(1, fired.FindAll(e => e == EventIds.S1HandsRaised).Count, "cờ giơ tay phát đúng một lần");
            Assert.IsTrue(root.Events.HasFired(EventIds.S1HandsRaised), "vẫn giữ s1.hands.raised");
            Assert.IsTrue(root.Notebook.IsUnlocked("p_two_hands"), "trang sổ về hai bàn tay mở sau P3");
            foreach (var b in new[] { "B0", "B0b", "B0c", "B0d", "B1.3" })
                CollectionAssert.Contains(beats, "01_NhaRong:" + b);
            foreach (var ev in new[] { EventIds.S0StreetDone, EventIds.S0PathChosen, EventIds.S1JobAccepted })
                Assert.IsTrue(root.Events.HasFired(ev), ev);
            foreach (var page in new[] { "p_saigon_new", "p_other_path", "p_job" })
                Assert.IsTrue(root.Notebook.IsUnlocked(page), page);
            var save = SaveSystem.Read();
            Assert.IsNotNull(save, "phải lưu sau prologue");
            Assert.AreEqual(SceneFlow.GameScenes[0], save.Scene);
            Assert.IsFalse(SceneFlow.FromPrologue, "cờ FromPrologue phải được 01 tiêu thụ");
        }

        [UnityTest]
        public IEnumerator NhaRongEnteredDirectlyKeepsHandsBeat()
        {
            var root = PlayTestUtil.Root();
            root.ResetProgress();
            SceneFlow.FromPrologue = false;
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[0]);
            var director = Object.FindAnyObjectByType<NhaRongDirector>();
            yield return PlayTestUtil.WaitUntil(() => director.Beat == "B1.2", 30f);
            Assert.AreEqual("B1.2", director.Beat, "vào 01 bằng Chọn chương/Ctrl+F2 vẫn có beat giơ tay");
            Assert.IsFalse(root.Events.HasFired(EventIds.S1HandsRaised));
        }
    }
}
