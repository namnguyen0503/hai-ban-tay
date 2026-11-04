using System.Collections;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>Ảnh duyệt NpcGaze: ông Mai trước/khi nói chuyện với Ba đứng lệch bên, và sau khi Ba đi vòng sang ngang.</summary>
    [Category("Screenshots"), Explicit]
    public sealed class Gaze15Shots
    {
        [TearDown] public void Done() => PlayTestUtil.ExitSim();

        static void PlaceBa(NpcAgent npc, float yaw, float angle, float distance)
        {
            var dir = Quaternion.Euler(0f, yaw + angle, 0f) * Vector3.forward;
            ChoreTestUtil.Look(npc.Position + dir * distance, npc.Position + Vector3.up * 1.55f);
        }

        static IEnumerator Shots(Camera side, NpcAgent mai, string name)
        {
            yield return ScreenshotCapture.CaptureCamera(PlayerRig.Current.cam, "gaze15_" + name + "_ba");
            yield return ScreenshotCapture.CaptureCamera(side, "gaze15_" + name + "_side");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator MaiTalkingToBa()
        {
            yield return ChoreTestUtil.Galley();
            var cast = Object.FindAnyObjectByType<DayCast>();
            var mai = cast.mai;
            mai.gameObject.SetActive(true);
            float yaw = cast.maiSpot.eulerAngles.y;
            mai.Place(cast.maiSpot.position, yaw);
            mai.Play("Talk");
            var gaze = NpcGaze.Of(mai);
            var side = new GameObject("Gaze15Side").AddComponent<Camera>();
            side.enabled = false; side.nearClipPlane = .03f; side.fieldOfView = 40f;
            var head = mai.Position + Vector3.up * 1.55f;
            side.transform.position = head + Quaternion.Euler(0f, yaw - 35f, 0f) * Vector3.forward * 1.7f - Vector3.up * .15f;
            side.transform.LookAt(head + Quaternion.Euler(0f, yaw + 40f, 0f) * Vector3.forward * .35f);

            PlaceBa(mai, yaw, 35f, 1.5f);
            yield return new WaitForSeconds(1f);
            yield return Shots(side, mai, "a_clip");

            TalkGaze.Bind("ong_mai", mai);
            TalkGaze.LineStarted("ong_mai", null, 60f);
            yield return new WaitForSeconds(1.2f);
            Debug.Log($"GAZE15 a weight {gaze.Weight:F2} lookErr {gaze.LookError:F1}");
            yield return Shots(side, mai, "b_talk");

            // Ba đi vòng sang ngang 100°: thân quay theo cho thẳng mặt.
            PlaceBa(mai, yaw, 100f, 1.5f);
            yield return new WaitForSeconds(2.5f);
            Debug.Log($"GAZE15 c body {gaze.BodyError:F1} lookErr {gaze.LookError:F1}");
            yield return ScreenshotCapture.CaptureCamera(PlayerRig.Current.cam, "gaze15_c_around_ba");
            Object.Destroy(side.gameObject);
        }
    }
}
