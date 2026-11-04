#if UNITY_EDITOR
using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>Ảnh đối chứng ánh sáng phố Sài Gòn (00_Prologue, hoàng hôn): cùng camera, tầng Laptop, không đụng giờ trong cảnh.</summary>
    [Category("Screenshots"), Explicit]
    public class PrologueLightShots
    {
        [TearDown]
        public void TearDown()
        {
            ScreenshotCapture.RestoreUi();
            Time.captureDeltaTime = 0f;
            GameRoot.Instance?.Quality.Apply(null);
            PlayTestUtil.ExitSim();
        }

        void Look(Vector3 feet, Vector3 target)
        {
            var rig = PlayerRig.Current;
            var to = target - feet - Vector3.up * 1.62f;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            rig.motor.Teleport(feet, yaw);
            rig.look.SetAngles(yaw, pitch);
        }

        IEnumerator Shot(string name, Vector3 feet, Vector3 target)
        {
            Look(feet, target);
            for (int i = 0; i < 15; i++) yield return null;
            yield return ScreenshotCapture.CaptureCamera(PlayerRig.Current.cam, "pl_" + name);
        }

        [UnityTest]
        public IEnumerator DuskStreetViews()
        {
            Time.captureDeltaTime = 1f / 60f;
            var root = PlayTestUtil.Root();
            root.ResetProgress();
            root.Quality.Apply("Laptop");
            SceneManager.LoadScene(SceneFlow.PrologueScene);
            for (int i = 0; i < 120; i++) yield return null;
            Assert.IsNotNull(PlayerRig.Current);
            root.UI.ResetTransient();
            root.Subtitles.StopAll();
            root.UI.SetFade(0f);
            PlayerRig.Current.SetControl(false);
            PlayerRig.Current.SetNoise(0f);
            yield return Shot("a_spawn_east", new Vector3(-27f, .12f, -7.5f), new Vector3(20f, 2f, -6f));
            yield return Shot("b_lamp_east", new Vector3(-18f, .12f, -7.8f), new Vector3(10f, 2.5f, -7f));
            yield return Shot("c_street_length", new Vector3(-26f, .12f, -1f), new Vector3(0f, 2.5f, -9f));
            yield return Shot("d_cafe_west", new Vector3(14f, .12f, -7.5f), new Vector3(-20f, 2.5f, -6f));
            yield return Shot("e_sky_up", new Vector3(-10f, .12f, 0f), new Vector3(15f, 12f, -9f));
            yield return Shot("f_lamp_close", new Vector3(-16f, .12f, -4.5f), new Vector3(-13f, 3.6f, -6.2f));
        }
    }
}
#endif
