using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Hồi quy lỗi kẹt Ngày 1: xô nằm trong vùng đặt xô trước cửa tro; cào xong (tay còn cầm cào) phải nhìn trúng xô và
    /// xách được bằng E (cào tự treo lại về móc), đổ ở máng tro (nghiêng dần tới 150°, PourMotion.TwoHandTilt), rồi cất xô về chỗ cũ trong hầm
    /// than — cất xong mới tính xong việc.
    /// </summary>
    public class AshBucketPlayTests
    {
        [TearDown]
        public void TearDown()
        {
            PlayTestUtil.ExitSim();
        }

        [UnityTest]
        public IEnumerator BucketCanBeLiftedAfterRakingWithTheRakeStillInHand()
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            AshStation ash = null;
            yield return PlayTestUtil.WaitUntil(() => (ash = Object.FindAnyObjectByType<AshStation>()) != null, 10f);
            Assert.IsNotNull(ash);
            var rig = PlayerRig.Current;
            var who = rig.interactor;

            Assert.IsTrue(ash.BucketStored, "đầu ca xô nằm ở chỗ cất trong hầm than");
            // Giả lập đã mang xô ra đặt trước cửa tro.
            ash.bucket.Detach(ash.spot.transform.position, ash.spot.transform.rotation, physics: false);
            Assert.IsTrue(ash.BucketReady);
            ash.hook.Interact(who);
            Assert.AreNotEqual(Tool.None, who.Tool, "đã cầm cào");
            ash.Rake.ForceFull();

            // Đứng trước xô như khi vừa cào xong, nhìn vào thân xô.
            var bucket = ash.bucket.transform.position;
            var feet = bucket + new Vector3(0.9f, 0f, -0.7f);
            var to = bucket + Vector3.up * 0.2f - (feet + Vector3.up * 1.62f);
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            rig.motor.Teleport(feet, yaw);
            rig.look.SetAngles(yaw, pitch);
            yield return PlayTestUtil.WaitUntil(() => ReferenceEquals(who.Current, ash.bucket), 5f);
            Assert.IsTrue(ReferenceEquals(who.Current, ash.bucket), "tia nhắm phải trúng xô, không bị vùng đặt xô che");
            Assert.AreEqual("prompt.pick_hang_tool", ash.bucket.GetPromptKey(who));

            // Đầu ca có thẻ/thoại chặn điều khiển: chờ tới lúc người chơi điều khiển được.
            var input = PlayTestUtil.Root().Input;
            yield return PlayTestUtil.WaitUntil(() => input.GameplayEnabled, 30f);
            Assert.IsTrue(input.GameplayEnabled, "người chơi phải điều khiển được");
            input.SimulateInteract();
            yield return PlayTestUtil.WaitUntil(() => who.Holding == ash.bucket, 5f);
            Assert.AreEqual(ash.bucket, who.Holding, "xách được xô");
            Assert.AreEqual(Tool.None, who.Tool);
            Assert.IsTrue(ash.hook.Hanging, "cào về móc");

            // Mang xô tới máng tro trong hầm than, nhìn vào máng, E để đổ.
            var chute = ash.chute.transform;
            var mouth = chute.position + chute.TransformVector(new Vector3(0.2f, 0.98f, 0f));
            var stand = mouth + new Vector3(1.3f, 0f, 0.3f);
            stand.y = chute.position.y;
            var look = mouth - (stand + Vector3.up * 1.62f);
            yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            pitch = -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
            rig.motor.Teleport(stand, yaw);
            rig.look.SetAngles(yaw, pitch);
            yield return PlayTestUtil.WaitUntil(() => ReferenceEquals(who.Current, ash.chute), 5f);
            Assert.IsTrue(ReferenceEquals(who.Current, ash.chute),
                $"tia nhắm phải trúng máng tro, đang trúng {(who.LookCollider != null ? who.LookCollider.name : "không gì")}");
            Assert.AreEqual("prompt.empty_ash", ash.PromptFor(ash.chute, who));
            int before = ash.Emptied;
            input.SimulateInteract();
            yield return PlayTestUtil.WaitUntil(() => ash.bucket.Busy, 2f);
            Assert.IsTrue(ash.bucket.Busy, "đang nghiêng xô đổ tro");
            float maxTilt = 0f;
            yield return PlayTestUtil.WaitUntil(() =>
            {
                maxTilt = Mathf.Max(maxTilt, Vector3.Angle(ash.bucket.transform.up, Vector3.up));
                return ash.Emptied > before && !ash.bucket.Busy;
            }, 8f);
            Assert.AreEqual(before + 1, ash.Emptied, "đổ được tro");
            Assert.Greater(maxTilt, PourMotion.TwoHandTilt - 1f, "xô nghiêng tới góc đổ hết (tay trượt xuống đáy, không úp 180° cho cẳng tay khỏi vặn ngược)");
            Assert.AreEqual(ash.bucket, who.Holding, "đổ xong xô vẫn trên tay");
            Assert.IsTrue(ash.AwaitingStore, "chưa cất xô thì chưa xong việc");

            // Cất xô về chỗ cũ: xô nằm đúng chỗ, không lơ lửng ở vị trí tay cũ.
            var store = ash.store.transform;
            var storeStand = store.position + new Vector3(0.83f, 0f, 0.04f);
            var toStore = store.position + Vector3.up * 0.1f - (storeStand + Vector3.up * 1.62f);
            yaw = Mathf.Atan2(toStore.x, toStore.z) * Mathf.Rad2Deg;
            pitch = -Mathf.Atan2(toStore.y, new Vector2(toStore.x, toStore.z).magnitude) * Mathf.Rad2Deg;
            rig.motor.Teleport(storeStand, yaw);
            rig.look.SetAngles(yaw, pitch);
            yield return PlayTestUtil.WaitUntil(() => ReferenceEquals(who.Current, ash.store), 5f);
            Assert.IsTrue(ReferenceEquals(who.Current, ash.store),
                $"tia nhắm phải trúng chỗ cất xô, đang trúng {(who.LookCollider != null ? who.LookCollider.name : "không gì")}");
            Assert.AreEqual("prompt.store_bucket", ash.PromptFor(ash.store, who));
            input.SimulateInteract();
            yield return PlayTestUtil.WaitUntil(() => who.Holding == null, 5f);
            for (int i = 0; i < 30; i++)
                yield return new WaitForFixedUpdate();
            Assert.Less(Vector3.Distance(ash.bucket.transform.position, store.position), 0.05f, "xô nằm ở chỗ cất");
            Assert.Less(Vector3.Distance(ash.bucket.Body.position, store.position), 0.05f, "thân vật lý cũng ở chỗ cất");
            Assert.IsFalse(ash.AwaitingStore);
        }
    }
}
