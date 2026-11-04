using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Hồi quy bot playthrough Marseille E3: dốc vô hình leo qua thành mạn bị nghiêng ngược, đầu cao quay về phía người chơi
    /// thành bức tường 1,09 m, không đi xuống cầu cảng được. Đi bộ thật từ boong xuống tới cầu cảng.
    /// </summary>
    public class GangwayWalkPlayTests
    {
        [TearDown]
        public void TearDown()
        {
            var root = PlayTestUtil.Root();
            root.Input.SimMove = Vector2.zero;
            Time.captureDeltaTime = 0f;
            PlayTestUtil.ExitSim();
        }

        [UnityTest]
        public IEnumerator PlayerWalksFromDeckDownToTheQuay()
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[2]);
            var rig = PlayerRig.Current;
            Assert.IsNotNull(rig);
            var input = PlayTestUtil.Root().Input;
            yield return PlayTestUtil.WaitUntil(() => input.GameplayEnabled, 60f);
            Assert.IsTrue(input.GameplayEnabled, "người chơi phải điều khiển được");

            // Thanh chắn cầu thang mở ở beat nhận lương; ở đây mở tay để chỉ kiểm hình học lối đi.
            var barrier = GameObject.Find("GangwayBarrier");
            Assert.IsNotNull(barrier);
            barrier.SetActive(false);

            var gangway = GameObject.Find("Gangway").transform;
            var env = gangway.parent;
            var feet = env.TransformPoint(new Vector3(0f, 0f, -0.6f));
            float yaw = Quaternion.LookRotation(env.TransformDirection(Vector3.back)).eulerAngles.y;
            rig.motor.Teleport(feet, yaw);
            rig.look.SetAngles(yaw, 0f);
            yield return null;

            // Batchmode chạy frame cực ngắn: cố định 60 khung/giây game để CharacterController thật sự di chuyển.
            Time.captureDeltaTime = 1f / 60f;
            input.SimMove = new Vector2(0f, 1f);
            float Z() => env.InverseTransformPoint(rig.Position).z;
            yield return PlayTestUtil.WaitUntil(() => Z() < -9.6f, 60f);
            input.SimMove = Vector2.zero;
            var local = env.InverseTransformPoint(rig.Position);
            Assert.Less(local.z, -9.6f, $"phải xuống tới cầu cảng, đang kẹt ở {local}");
        }
    }
}
