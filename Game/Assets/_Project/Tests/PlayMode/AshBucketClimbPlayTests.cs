using System.Collections;
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
    /// Xô tro đặt ở chỗ đặt trước cửa tro lò thấp hơn bước chân của Ba (đáy capsule 0,05 + stepOffset 0,3 m): bot ngày 1, 3, 5 thấy Ba đứng cao
    /// 0,19–0,38 m ở lối lấy chổi (−3,3; 1,2) cạnh xô, camera tự nhô lên. Ba cách xô 0,5 m theo đường chéo thì capsule lấn góc hộp 2 cm và
    /// bị đẩy lên nằm trên mép hộp (độ nâng = cao hộp − 0,17 m, hộp cao hơn thì nâng nhiều hơn). Xô đặt rồi thì người đi sát, kể cả đứng
    /// chạm hộp, không được bị nâng lên.
    /// </summary>
    public sealed class AshBucketClimbPlayTests
    {
        [TearDown]
        public void TearDown()
        {
            var root = PlayTestUtil.Root();
            root.Input.SimMove = Vector2.zero;
            Time.captureDeltaTime = 0f;
            PlayTestUtil.ExitSim();
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator PlayerIsNotLiftedByAPlacedAshBucket()
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            AshStation ash = null;
            yield return PlayTestUtil.WaitUntil(() => (ash = Object.FindAnyObjectByType<AshStation>()) != null, 10f);
            var input = PlayTestUtil.Root().Input;
            yield return PlayTestUtil.WaitUntil(() => input.GameplayEnabled, 30f);
            Assert.IsTrue(input.GameplayEnabled, "người chơi phải điều khiển được");
            var rig = PlayerRig.Current;
            Time.captureDeltaTime = 1f / 60f;
            var box = ash.bucket.GetComponent<BoxCollider>();
            Assert.IsNotNull(box, "hộp va chạm xô tro");
            var body = ash.bucket.GetComponent<Rigidbody>();
            float height = box.size.y;
            // Hình thử: 0 = hộp như dựng; 1 = trụ đứng (capsule) cùng cao, bán kính nửa cạnh hộp.
            var table = new System.Text.StringBuilder();
            float shipped = 0f;
            string shippedWhere = "";
            for (int shape = 0; shape < 2; shape++)
            {
                CapsuleCollider cap = null;
                if (shape == 1)
                {
                    box.enabled = false;
                    cap = ash.bucket.gameObject.AddComponent<CapsuleCollider>();
                    cap.direction = 1;
                    cap.radius = box.size.x * 0.5f;
                    cap.height = height;
                    cap.center = new Vector3(0f, height * 0.5f, 0f);
                }
                float worst = 0f;
                string where = "";
                for (int cell = 0; cell < ash.coal.cells.Length; cell++)
                {
                    var spot = ash.coal.cells[cell].bucketSpot.transform.position;
                    ash.bucket.transform.position = spot;
                    body.position = spot;
                    Physics.SyncTransforms();
                    for (int angle = 0; angle < 360; angle += 45)
                    {
                        var rot = Quaternion.Euler(0f, angle, 0f);
                        // Ba tới chỗ bot đứng (cách xô 0,5 m đường chéo) từ 3 hướng: đi thẳng vào từ xa, đứng sẵn, đi lướt qua góc hộp.
                        for (int approach = 0; approach < 3; approach++)
                        {
                            var from = approach == 0 ? rot * new Vector3(0f, 0f, 1.2f) : approach == 1 ? rot * new Vector3(-0.29f, 0f, -0.41f) : rot * new Vector3(0f, 0f, -0.65f);
                            var to = approach == 2 ? rot * new Vector3(-0.29f, 0f, -0.41f) : Vector3.zero;
                            float lift = 0f;
                            for (int pass = 0; pass < 2; pass++)
                            {
                                // pass 0 có xô; pass 1 (chỉ khi bị nâng) không xô, để loại bàn, tường, bậc quanh lò.
                                if (shape == 0) box.enabled = pass == 0; else cap.enabled = pass == 0;
                                Physics.SyncTransforms();
                                var feet = spot + from;
                                feet.y = rig.Position.y;
                                var aim = (approach == 2 ? to - from : -from).WithY(0f);
                                float yaw = Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg;
                                rig.motor.Teleport(feet, yaw);
                                rig.look.SetAngles(yaw, 0f);
                                for (int f = 0; f < 6; f++) yield return null;
                                float baseY = rig.Position.y;
                                input.SimMove = approach == 1 ? Vector2.zero : new Vector2(0f, 1f);
                                float limit = approach == 0 ? 1.5f : approach == 1 ? 0.8f : 0.7f;
                                for (float t = 0f; t < limit; t += Time.deltaTime)
                                {
                                    yield return null;
                                    // Đi lướt: dừng khi tới điểm đứng của bot, như bot "arrived".
                                    if (approach == 2 && Vector3.Distance((rig.Position - spot).WithY(0f), to) < 0.05f) break;
                                }
                                input.SimMove = Vector2.zero;
                                for (int f = 0; f < 30; f++) yield return null;
                                float lifted = rig.Position.y - baseY;
                                if (pass == 0) { lift = lifted; if (lifted < 0.05f) break; }
                                else if (lifted > 0.05f) lift = 0f;
                            }
                            box.enabled = shape == 0;
                            if (cap != null) cap.enabled = true;
                            if (lift > worst) { worst = lift; where = $"ô {cell}, góc {angle}°, kiểu {approach}, người {rig.Position}"; }
                        }
                    }
                }
                table.Append($" {(shape == 0 ? "hộp" : "trụ")} cao {height:0.00} m: nâng ổn định tối đa {worst:0.00} m ({where});");
                if (shape == 0) { shipped = worst; shippedWhere = where; }
                if (cap != null) Object.Destroy(cap);
                box.enabled = true;
            }
            Debug.Log("ASH_BUCKET_CLIMB" + table);
            Assert.Less(shipped, 0.1f, $"Ba bị xô tro nâng lên {shipped:0.00} m ({shippedWhere});{table}");
        }
        /// <summary>
        /// Ba đi vào phụ bếp đứng yên từ 8 hướng, kể cả đứng sát rồi bước tiếp: capsule người không được trườn lên capsule NPC (bot ngày 1/3/5
        /// 20/10: Ba cao 0,17–0,33 m ở lối bưng nồi và lối lấy chổi, QA-0125). Pass 2 bỏ capsule NPC để loại tường, bàn quanh chỗ đứng.
        /// </summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator PlayerIsNotLiftedByAnNpcHeWalksInto()
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            var helper = Object.FindAnyObjectByType<HelperRoutine>();
            Assert.IsNotNull(helper, "phụ bếp trong cảnh bếp");
            var input = PlayTestUtil.Root().Input;
            yield return PlayTestUtil.WaitUntil(() => input.GameplayEnabled, 30f);
            var rig = PlayerRig.Current;
            Time.captureDeltaTime = 1f / 60f;
            var agent = helper.agent;
            foreach (var routine in Object.FindObjectsByType<NpcRoutine>(FindObjectsSortMode.None)) { routine.StopAllCoroutines(); routine.enabled = false; }
            agent.StopAllCoroutines();
            agent.ClearWalkState();
            agent.yieldToPlayer = false;
            var col = agent.GetComponent<CapsuleCollider>();
            Assert.IsNotNull(col, "capsule va chạm của NPC");
            col.enabled = true;
            var spot = agent.Position;
            agent.Place(spot, 0f);
            Physics.SyncTransforms();
            float worst = 0f, floorY = rig.Position.y;
            string where = "";
            var table = new System.Text.StringBuilder();
            for (int angle = 0; angle < 360; angle += 45)
            {
                var rot = Quaternion.Euler(0f, angle, 0f);
                // Đi thẳng vào từ 1,2 m; rồi đứng sát (0,45 m) bước tiếp; rồi lướt lệch 0,3 m qua mép capsule.
                for (int approach = 0; approach < 3; approach++)
                {
                    var from = rot * (approach == 0 ? new Vector3(0f, 0f, -1.2f) : approach == 1 ? new Vector3(0f, 0f, -0.45f) : new Vector3(0.3f, 0f, -1.0f));
                    float lift = 0f;
                    for (int pass = 0; pass < 2; pass++)
                    {
                        col.enabled = pass == 0;
                        Physics.SyncTransforms();
                        var feet = spot + from;
                        feet.y = floorY;
                        float yaw = (angle + (approach == 2 ? -17f : 0f)) % 360f;
                        rig.motor.Teleport(feet, yaw);
                        rig.look.SetAngles(yaw, 0f);
                        for (int f = 0; f < 6; f++) yield return null;
                        float baseY = rig.Position.y;
                        input.SimMove = new Vector2(0f, 1f);
                        for (float t = 0f; t < 2f; t += Time.deltaTime)
                            yield return null;
                        input.SimMove = Vector2.zero;
                        for (int f = 0; f < 30; f++) yield return null;
                        float lifted = rig.Position.y - baseY;
                        if (pass == 0) { lift = lifted; if (lifted < 0.05f) break; }
                        else if (lifted > 0.05f) lift = 0f;
                    }
                    col.enabled = true;
                    if (lift > 0.02f) table.Append($" [góc {angle}° kiểu {approach}: {lift:0.00} m, người {rig.Position}]");
                    if (lift > worst) { worst = lift; where = $"góc {angle}°, kiểu {approach}, người {rig.Position}"; }
                }
            }
            Debug.Log("NPC_LIFT NPC " + spot + table);
            Assert.Less(worst, 0.1f, $"Ba bị capsule NPC nâng lên {worst:0.00} m ({where});{table}");
        }

        /// <summary>
        /// Lưới an toàn (FirstPersonMotor.OnControllerColliderHit): capsule đã nằm trên đồ (xô, lớp Holdable) thì bị đẩy ra và thả xuống sàn ngay
        /// trong khung, không đứng lên được. Bot ngày 1/3/5 thấy Ba gối mép hộp xô cao 0,36–0,55 m dù hộp đã nâng; hình học gối mép không
        /// tái hiện được bằng đi bộ từ nhiều hướng nên đặt Ba thẳng lên đỉnh xô.
        /// </summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator PlayerPlacedOnTopOfTheBucketIsPushedOffToTheFloor()
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            AshStation ash = null;
            yield return PlayTestUtil.WaitUntil(() => (ash = Object.FindAnyObjectByType<AshStation>()) != null, 10f);
            var input = PlayTestUtil.Root().Input;
            yield return PlayTestUtil.WaitUntil(() => input.GameplayEnabled, 30f);
            var rig = PlayerRig.Current;
            Time.captureDeltaTime = 1f / 60f;
            var spot = ash.coal.cells[0].bucketSpot.transform.position;
            var body = ash.bucket.GetComponent<Rigidbody>();
            ash.bucket.transform.position = spot;
            body.position = spot;
            Physics.SyncTransforms();
            float floor = rig.Position.y;
            var box = ash.bucket.GetComponent<BoxCollider>();
            // Chân Ba ngay trên mặt xô, cách tâm xô 0,1 m (đứng nằm lên hộp).
            var feet = spot + new Vector3(0.1f, box.size.y + 0.03f, 0f);
            rig.motor.Teleport(feet, 90f);
            float worst = 0f;
            for (int f = 0; f < 90; f++)
            {
                yield return null;
                if (f >= 20) worst = Mathf.Max(worst, rig.Position.y - floor);
            }
            Assert.Less(worst, 0.1f, $"Ba vẫn đứng trên xô: cao {worst:0.00} m so với sàn sau 0,3 s");
            Assert.Greater(Vector3.Distance(new Vector3(rig.Position.x, 0f, rig.Position.z), new Vector3(spot.x, 0f, spot.z)), 0.4f, "Ba bị đẩy ra khỏi xô");
        }
    }
}
