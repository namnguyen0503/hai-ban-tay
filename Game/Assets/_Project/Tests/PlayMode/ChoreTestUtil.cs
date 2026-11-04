using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HaiBanTay.Tests
{
    public static class ChoreTestUtil
    {
        public static IEnumerator Galley()
        {
            PlayTestUtil.ExitSim();
            PlayTestUtil.Root().ResetProgress();
            var before = SceneManager.GetActiveScene();
            SceneManager.LoadScene(SceneFlow.GameScenes[1]);
            ShiftDirector d = null;
            yield return PlayTestUtil.WaitUntil(() => (d = Object.FindAnyObjectByType<ShiftDirector>()) != null && d.gameObject.scene != before, 30f);
            Assert.IsNotNull(d);
            yield return PlayTestUtil.WaitUntil(() => d.Beat == "D1.sweep" && !GameRoot.Instance.UI.Card.Visible, 40f);
            Assert.AreEqual("D1.sweep", d.Beat);
            d.StopAllCoroutines();
            d.Clock.Running = false;
            d.Clock.Minutes = 6f * 60f;
            d.coal.Fire.Set(FireLogic.MaxFuel, 1f);
            GameRoot.Instance.Subtitles.StopAll();
            yield return new WaitForSeconds(1f);
        }

        public static void Look(Vector3 feet, Vector3 point)
        {
            var rig = PlayerRig.Current;
            var direction = point - feet - Vector3.up * 1.62f;
            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
            rig.motor.Teleport(feet, yaw);
            rig.look.SetAngles(yaw, pitch);
        }

        /// <summary>Cọ bằng bàn chải (brush17): chuột phủ kín mặt cọ theo con trỏ thật (ScrubCoverage), không rê mù.</summary>
        public static IEnumerator Scrub(HaiBanTay.Tasks.ScrubLogic logic, System.Func<HaiBanTay.Tasks.BrushScrub> brush, float seconds = 60f)
        {
            var input = GameRoot.Instance.Input;
            var mouse = ScrubCoverage.For(brush);
            input.SimUse = true;
            float end = Time.realtimeSinceStartup + seconds;
            while (!logic.Done && Time.realtimeSinceStartup < end)
            {
                input.SimLook = mouse();
                yield return null;
            }
            input.SimUse = false; input.SimLook = Vector2.zero;
            Assert.IsTrue(logic.Done);
            yield return null;
        }

        public static IEnumerator Scrub(HaiBanTay.Tasks.ScrubLogic logic, float seconds = 40f)
        {
            var input = GameRoot.Instance.Input;
            input.SimUse = true;
            float end = Time.realtimeSinceStartup + seconds;
            while (!logic.Done && Time.realtimeSinceStartup < end)
            {
                input.SimLook = new Vector2(Time.frameCount % 24 < 12 ? 15f : -15f, 8f);
                yield return null;
            }
            input.SimUse = false; input.SimLook = Vector2.zero;
            Assert.IsTrue(logic.Done);
            yield return null;
        }
    }
}
