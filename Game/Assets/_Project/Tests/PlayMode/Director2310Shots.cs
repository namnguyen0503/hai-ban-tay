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
    /// <summary>Ảnh các beat DIRECTOR §9 (23/10): xe cà-rem ở bến Nhà Rồng, cậu bán báo ở quán Canebière, mặt sông và cột buồm cuối phố prologue.</summary>
    [Category("Screenshots"), Explicit]
    public sealed class Director2310Shots
    {
        [TearDown]
        public void TearDown()
        {
            ScreenshotCapture.RestoreUi();
            Time.captureDeltaTime = 0f;
            GameRoot.Instance?.Quality.Apply(null);
            PlayTestUtil.ExitSim();
        }

        static void Look(Vector3 feet, Vector3 target)
        {
            var rig = PlayerRig.Current;
            var to = target - feet - Vector3.up * 1.62f;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            rig.motor.Teleport(feet, yaw);
            rig.look.SetAngles(yaw, pitch);
        }

        static IEnumerator Open(string scene)
        {
            Time.captureDeltaTime = 1f / 60f;
            var root = PlayTestUtil.Root();
            root.ResetProgress();
            root.Quality.Apply("Laptop");
            SceneManager.LoadScene(scene);
            for (int i = 0; i < 150; i++) yield return null;
            Assert.IsNotNull(PlayerRig.Current);
            root.UI.ResetTransient();
            root.Subtitles.StopAll();
            root.UI.SetFade(0f);
            PlayerRig.Current.SetControl(false);
            PlayerRig.Current.SetNoise(0f);
        }

        static IEnumerator Shot(string name, Vector3 feet, Vector3 target)
        {
            Look(feet, target);
            for (int i = 0; i < 20; i++) yield return null;
            yield return ScreenshotCapture.CaptureCamera(PlayerRig.Current.cam, "d2310_" + name);
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator IceCreamCartOnTheQuay()
        {
            yield return Open(SceneFlow.GameScenes[0]);
            yield return Shot("cart_wide", new Vector3(12f, -4.1f, -16f), new Vector3(19.4f, -3.2f, -17.3f));
            yield return Shot("cart_close", new Vector3(16.8f, -4.1f, -17.6f), new Vector3(19.4f, -3.2f, -17.3f));
            yield return Shot("cart_vendor", new Vector3(18.8f, -4.1f, -20.3f), new Vector3(19.5f, -3.3f, -18.3f));
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator NewsBoyAtTheCafe()
        {
            yield return Open(SceneFlow.GameScenes[2]);
            yield return Shot("newsboy_wide", new Vector3(-1.5f, -2.4f, -21.5f), new Vector3(3.9f, -1.6f, -22.5f));
            yield return Shot("newsboy_close", new Vector3(1.2f, -2.4f, -22.2f), new Vector3(3.9f, -1.7f, -22.5f));
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator RiverEndOfThePrologueStreet()
        {
            yield return Open(SceneFlow.PrologueScene);
            yield return Shot("river_far", new Vector3(30f, .12f, -7.5f), new Vector3(62f, 11f, 0f));
            yield return Shot("river_mid", new Vector3(44f, .12f, -7f), new Vector3(62f, 9f, 0f));
            yield return Shot("river_cafe_view", new Vector3(14f, .12f, -7.5f), new Vector3(55f, 8f, -2f));
        }
    }
}
#endif
