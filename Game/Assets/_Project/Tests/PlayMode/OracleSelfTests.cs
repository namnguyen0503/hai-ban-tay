using System.Collections;
using System.Linq;
using HaiBanTay.Core;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// W1: bộ quan sát của bot phải bắt được lớp lỗi nó canh (PRODUCTION_PLAN mục 6, "Lưới bắt được các lỗi đã vá ở W0 khi chạy
    /// trên fixture dựng lại trạng thái lỗi"). Mỗi test dựng lại trạng thái lỗi và kiểm có MAJOR; trạng thái lành thì không báo.
    /// </summary>
    public class OracleSelfTests
    {
        static PlayerRig Rig => PlayerRig.Current;
        PlaythroughLog _log;
        float _scale = 1f;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _scale;
            if (Rig != null) Rig.look.Locked = false;
            _log?.Dispose();
            _log = null;
            PlayTestUtil.ExitSim();
        }

        static bool Has(PlaythroughLog log, string prefix, string severity) =>
            log.Findings.Any(f => f.issue.StartsWith(prefix) && f.severity == severity);

        [UnityTest, Timeout(120000)]
        public IEnumerator CameraJumpWithoutInputIsMajorButSmoothOrBotDrivenTurnsAreNot()
        {
            yield return ChoreTestUtil.Galley();
            _log = new PlaythroughLog();
            for (int i = 0; i < 30; i++) yield return null;
            Assert.Greater(_log.CameraSamples, 10, "bộ quan sát đo camera mỗi khung khi đứng yên");
            Assert.IsFalse(_log.Findings.Any(f => f.issue.StartsWith("camera_jump")), "đứng yên thì không báo");

            // Quay mượt (1° mỗi khung, như liếc tay ông Mai) không phải nhảy.
            yield return _log.Checkpoint("smooth_turn");
            for (int i = 0; i < 20; i++) { Rig.look.SetAngles(Rig.look.Yaw + 1f, Rig.look.Pitch); yield return null; }
            Assert.IsFalse(_log.Findings.Any(f => f.issue.StartsWith("camera_jump")), "quay mượt không báo");

            // Bot tự nhắm: camera đổi vì input, không báo.
            yield return _log.Checkpoint("bot_aim");
            Rig.look.SetAngles(Rig.look.Yaw + 40f, Rig.look.Pitch);
            _log.BotDriveFrame = Time.frameCount;
            yield return null; yield return null;
            Assert.IsFalse(_log.Findings.Any(f => f.issue.StartsWith("camera_jump")), "góc nhìn đổi do bot nhắm không phải camera nhảy");

            // Game tự giật camera 25° trong một khung khi người chơi đứng yên (như khóa camera bật/tắt): MAJOR.
            yield return _log.Checkpoint("game_snap");
            Rig.look.SetAngles(Rig.look.Yaw + 25f, Rig.look.Pitch);
            yield return null; yield return null;
            Assert.IsTrue(Has(_log, "camera_jump_game_snap", "MAJOR"), "camera giật 25° không input phải là MAJOR");

            // Giật vừa (5°): chỉ WARN.
            yield return _log.Checkpoint("small_snap");
            Rig.look.SetAngles(Rig.look.Yaw + 5f, Rig.look.Pitch);
            yield return null; yield return null;
            Assert.IsTrue(Has(_log, "camera_jump_warn_small_snap", "MINOR"), "giật 5° là WARN");
            Assert.IsFalse(Has(_log, "camera_jump_small_snap", "MAJOR"));
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator LockFlickerLikeTheOldPanRinseIsMajor()
        {
            yield return ChoreTestUtil.Galley();
            _log = new PlaythroughLog();
            // Khóa giữ suốt một giây (chốt giữ sau W0): không báo.
            yield return _log.Checkpoint("latched");
            Rig.look.Locked = true;
            for (float t = 0f; t < 1.2f; t += Time.deltaTime) yield return null;
            Rig.look.Locked = false;
            yield return null;
            Assert.IsFalse(_log.Findings.Any(f => f.issue.StartsWith("lock_flicker")), "khóa rồi mở một lần không phải dao động");

            // Gạt lá nhanh: giữ nút gạt một lá rồi nhả, khóa theo đúng nút (6 lần một giây). Theo tay người chơi, không báo.
            yield return _log.Checkpoint("sort_leaves_by_hold");
            var input = GameRoot.Instance.Input;
            for (int leaf = 0; leaf < 6; leaf++)
            {
                input.SimUse = Rig.look.Locked = leaf % 2 == 0;
                for (float t = 0f; t < 1f / 6f; t += Time.deltaTime) yield return null;
            }
            input.SimUse = Rig.look.Locked = false;
            yield return null;
            Assert.IsFalse(_log.Findings.Any(f => f.issue.StartsWith("lock_flicker")), "khóa theo nhấn/nhả nút không phải dao động");

            // Bản trước W0: tia nhìn trượt khỏi chảo/nồi là mở khóa, trúng lại là khóa — đổi gần như mỗi khung.
            yield return _log.Checkpoint("pan_rinse_before_w0");
            for (float t = 0f; t < 1f; t += Time.deltaTime)
            {
                Rig.look.Locked = !Rig.look.Locked;
                yield return null;
            }
            Rig.look.Locked = false;
            Assert.IsTrue(Has(_log, "lock_flicker_pan_rinse_before_w0", "MAJOR"), "khóa bật/tắt mỗi khung phải là MAJOR");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator GuideAtAnUnreachableTargetIsMajorOnlyWhenItLasts()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            var ash = Object.FindAnyObjectByType<AshStation>();
            var guide = GameRoot.Instance.UI.Guide;
            d.Board.Clear();
            // Việc tro chỉ có khi ô lửa có tro thật (FireCellState, 15/10).
            d.coal.CellState(0).Set(0f, 0f, 0.6f);
            d.Board.Add(OrderKinds.Ash, 1);
            yield return PlayTestUtil.WaitUntil(() => guide.Target == ash.bucket.transform, 2f);
            Assert.AreEqual(ash.bucket.transform, guide.Target);
            _log = new PlaythroughLog();
            var colliders = ash.bucket.GetComponentsInChildren<Collider>().Where(c => c.enabled).ToArray();
            Assert.IsNotEmpty(colliders);
            // Thoáng qua (0,3 s, ngắn hơn khoảng lấy mẫu 0,5 s): nhiều nhất một mẫu sai, không MAJOR.
            yield return _log.Checkpoint("guide_blink");
            foreach (var c in colliders) c.enabled = false;
            for (float t = 0f; t < 0.3f; t += Time.deltaTime) yield return null;
            foreach (var c in colliders) c.enabled = true;
            for (float t = 0f; t < 1.2f; t += Time.deltaTime) yield return null;
            Assert.IsFalse(Has(_log, "guide_target_unreachable_AshBucket", "MAJOR"), "sai thoáng qua không phải MAJOR");
            // Kéo dài (như hướng dẫn kẹt chỉ vào cái xô đã cầm lên): MAJOR.
            yield return _log.Checkpoint("guide_stuck");
            foreach (var c in colliders) c.enabled = false;
            for (float t = 0f; t < 1.3f; t += Time.deltaTime) yield return null;
            foreach (var c in colliders) c.enabled = true;
            Assert.IsTrue(Has(_log, "guide_target_unreachable_AshBucket", "MAJOR"), "hướng dẫn chỉ vật không nhắm được quá 0,5 s là MAJOR");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator NoProgressCatchesTheOldWashSoftlock()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            Assert.IsTrue(d.InShift);
            // Dựng lại kẹt rửa nồi trước W0: lượt rửa đòi một nồi không còn (lệnh ghi thẳng lên bảng, station không biết).
            d.Board.Clear();
            d.Board.Add(OrderKinds.Wash, 1);
            _log = new PlaythroughLog();
            _scale = Time.timeScale;
            Time.timeScale = 8f;
            for (float t = 0f; t < 40f && !Has(_log, "no_progress_warn_", "MINOR"); t += Time.deltaTime) yield return null;
            Assert.IsTrue(Has(_log, "no_progress_warn_", "MINOR"), "đứng yên quá 30 s: WARN");
            Assert.IsFalse(Has(_log, "no_progress_", "MAJOR"), "chưa tới 90 s thì chưa MAJOR");
            for (float t = 0f; t < 80f && !Has(_log, "no_progress_", "MAJOR"); t += Time.deltaTime) yield return null;
            Time.timeScale = _scale;
            Assert.IsTrue(Has(_log, "no_progress_", "MAJOR"), "lệnh rửa không thể xong, bếp đứng yên quá 90 s: MAJOR");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator WorkingResetsTheNoProgressTimer()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            d.Board.Clear();
            d.Board.Add(OrderKinds.Sweep, 40);
            _log = new PlaythroughLog();
            _scale = Time.timeScale;
            Time.timeScale = 8f;
            // Mỗi 20 s game có một việc xong: không bao giờ đứng yên quá 30 s.
            float next = Time.time + 20f;
            for (float t = 0f; t < 150f; t += Time.deltaTime)
            {
                if (Time.time >= next) { d.Board.Report(OrderKinds.Sweep, 1); next = Time.time + 20f; }
                yield return null;
            }
            Time.timeScale = _scale;
            Assert.IsFalse(_log.Findings.Any(f => f.issue.StartsWith("no_progress")), "có tiến triển đều thì không báo");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator NpcGhostingThroughAWallIsMajor()
        {
            yield return ChoreTestUtil.Galley();
            var wall = GameObject.Find("Wall_W_South")?.GetComponent<Collider>();
            Assert.IsNotNull(wall, "tường tây của bếp");
            var source = Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None).First(a => a.isActiveAndEnabled && a.BodyActive);
            // Bản sao chỉ còn NpcAgent: không routine nào khác dắt nó đi.
            var npc = Object.Instantiate(source.gameObject).GetComponent<NpcAgent>();
            foreach (var mb in npc.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != npc) Object.DestroyImmediate(mb);
            npc.avoidStatic = false;
            var b = wall.bounds;
            bool thinX = b.size.x < b.size.z;
            // NpcAgent.GuardStep không cho NPC bước vào vật tĩnh nữa (QA-0150), nên không dắt được NPC đi xuyên tường từ ngoài vào. Đặt
            // NPC sẵn trong lòng tường rồi đi dọc tường (đang chồng vật thì GuardStep cho bước để thoát ra): bộ quan sát phải bắt được.
            var along = thinX ? Vector3.forward : Vector3.right;
            float run = Mathf.Min(1.5f, (thinX ? b.extents.z : b.extents.x) * 0.8f);
            var from = new Vector3(b.center.x, source.Position.y, b.center.z) - along * run;
            npc.TeleportTo(from, Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg);
            yield return null;
            _log = new PlaythroughLog();
            npc.StartCoroutine(npc.WalkTo(from + along * run * 2f));
            for (float t = 0f; t < 6f && !Has(_log, "npc_inside_" + npc.name, "MAJOR"); t += Time.deltaTime) yield return null;
            Assert.Greater(_log.NpcSamples, 0, "bộ quan sát lấy mẫu NPC đang đi");
            Assert.IsTrue(Has(_log, "npc_inside_" + npc.name, "MAJOR"), "NPC đi xuyên tường quá 0,3 s phải là MAJOR");
            Object.Destroy(npc.gameObject);
        }
    }
}
