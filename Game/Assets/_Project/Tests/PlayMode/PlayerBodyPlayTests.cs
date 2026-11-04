using System;
using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Narrative;
using HaiBanTay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    public class PlayerBodyPlayTests
    {
        // Tay Ba ẩn khi làm bếp (ArmsVisibility, 17/10): kiểm vải cổ tay, cổ tay, bàn tay cần tay hiện như lúc giơ hai bàn tay.
        [SetUp] public void ShowArms() => PlayerRig.ArmsShownByDefault = true;

        [TearDown]
        public void TearDown()
        {
            PlayerRig.ArmsShownByDefault = false;
            PlayTestUtil.ExitSim();
        }

        static IEnumerator Prepare(string scene)
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(scene);
            var controller = PlayerRig.Current.GetComponent<CharacterController>();
            // Spawn nhấc capsule khỏi sàn: kiểm tư thế đứng phải chờ tiếp đất thật, không chờ số frame tùy FPS.
            yield return PlayTestUtil.WaitUntil(() => controller.isGrounded, 2f);
            Assert.IsTrue(controller.isGrounded, scene + ": nhân vật phải tiếp đất trước kiểm trang phục");
            SceneDirector.Current.StopAllCoroutines();
            var rig = PlayerRig.Current;
            rig.SetOutfit(TrangPhuc.PhuBep); // Những assert bộ phụ bếp gốc vẫn kiểm chính bộ đó.
            rig.SetControl(false);
            rig.SetNoise(0f);
        }

        [UnityTest]
        public IEnumerator OrdinaryOutfitChangesBodyAndEveryCuffAndRestoresKitchenOutfit()
        {
            yield return Prepare(SceneFlow.GameScenes[0]);
            var rig = PlayerRig.Current;
            var body = rig.body;
            var original = body.skin.sharedMesh;
            var materials = body.skin.sharedMaterials;
            rig.SetOutfit(TrangPhuc.Thuong);
            Assert.AreSame(body.thuongMesh, body.skin.sharedMesh);
            Assert.AreNotSame(original, body.skin.sharedMesh);
            Assert.AreEqual(0, body.skin.sharedMesh.GetTriangles(4).Length, "không dây tạp dề");
            Assert.AreEqual(0, body.skin.sharedMesh.GetTriangles(6).Length, "không mũ");
            Assert.IsFalse(body.apron.gameObject.activeSelf);
            int cuffs = 0;
            foreach (var poses in new[] { rig.hands.leftPoseMeshes, rig.hands.rightPoseMeshes })
                foreach (var poseMesh in poses)
                    foreach (var renderer in poseMesh.GetComponentsInChildren<MeshRenderer>(true))
                        if (renderer.name.StartsWith("Sleeve")) { cuffs++; Assert.AreSame(body.thuongMaterials[0], renderer.sharedMaterial); }
            Assert.Greater(cuffs, 0, "phải kiểm cổ tay FP thật");
            foreach (HandPose pose in Enum.GetValues(typeof(HandPose)))
            {
                rig.hands.SetPose(pose); rig.hands.Snap();
                yield return null; yield return null; yield return new WaitForFixedUpdate();
                AssertWrists(rig, "Áo thường/" + pose);
            }
            rig.SetOutfit(TrangPhuc.PhuBep);
            Assert.AreSame(original, body.skin.sharedMesh);
            CollectionAssert.AreEqual(materials, body.skin.sharedMaterials);
            Assert.IsTrue(body.apron.gameObject.activeSelf);
            foreach (var poses in new[] { rig.hands.leftPoseMeshes, rig.hands.rightPoseMeshes })
                foreach (var poseMesh in poses)
                    foreach (var renderer in poseMesh.GetComponentsInChildren<MeshRenderer>(true))
                        if (renderer.name.StartsWith("Sleeve")) Assert.AreSame(materials[0], renderer.sharedMaterial);
        }

        [UnityTest]
        public IEnumerator LookingDownShowsChestApronTrousersAndFeetInBothScenes()
        {
            foreach (string scene in new[] { SceneFlow.GameScenes[0], SceneFlow.GameScenes[1] })
            {
                yield return Prepare(scene);
                var rig = PlayerRig.Current;
                rig.hands.SetPose(HandPose.Lowered);
                foreach (float pitch in new[] { 70f, 75f, 80f, 85f })
                {
                    rig.look.SetAngles(rig.look.Yaw, pitch);
                    yield return null;
                    yield return null;
                    // Coroutine yield null chạy trước LateUpdate. FixedUpdate kế tiếp đọc khung camera/tay đã chốt,
                    // giữ kiểm thực tế runtime (không tự gọi IK, không ép Snap và không đổi ngưỡng).
                    yield return new WaitForFixedUpdate();
                    Assert.AreEqual(pitch, rig.look.Pitch, "Không được kiểm 85° bằng góc bị chặn ở 80°");
                    var body = rig.body;
                    Assert.IsNotNull(body);
                    Assert.AreEqual(1, body.GetComponentsInChildren<SkinnedMeshRenderer>().Length);
                    var mesh = new Mesh();
                    body.skin.BakeMesh(mesh, true);
                    try
                    {
                        var vertices = mesh.vertices;
                        foreach (int slot in new[] { 0, 1, 4, 5, 7 })
                        {
                            int visible = 0;
                            foreach (int index in mesh.GetTriangles(slot))
                            {
                                var p = rig.cam.WorldToViewportPoint(body.skin.transform.TransformPoint(vertices[index]));
                                if (p.z > rig.cam.nearClipPlane && p.x > 0f && p.x < 1f && p.y > 0f && p.y < 1f) visible++;
                            }
                            Assert.Greater(visible, 12, $"{scene}: slot {slot} (áo/quần/dây tạp dề/giày) phải thấy khi cúi {pitch}°");
                        }
                        AssertToesNotHiddenByCloth(rig, mesh, scene + "/" + pitch);
                        Assert.Less(Vector3.Distance(body.LeftWrist.position, rig.hands.left.position), 0.02f,
                            $"{scene}: xương={body.LeftWrist.position:F4}, tay={rig.hands.left.position:F4}, rig={rig.transform.position:F4}, camera={rig.cam.transform.position:F4}, dt={Time.deltaTime:F4}, stretch={body.ArmStretch:F4}");
                        Assert.Less(Vector3.Distance(body.RightWrist.position, rig.hands.right.position), 0.02f);
                        Assert.Less(rig.hands.left.position.y, rig.head.position.y - 0.6f, "tay thả ngang hông");
                    }
                    finally { UnityEngine.Object.Destroy(mesh); }
                }
            }
        }

        [UnityTest]
        public IEnumerator WristsStayWithinTwoCentimetresForEveryPoseToolShotAndShoulderLean()
        {
            yield return Prepare(SceneFlow.GameScenes[0]);
            var rig = PlayerRig.Current;
            foreach (float pitch in new[] { -75f, 0f, 80f })
                foreach (HandPose pose in Enum.GetValues(typeof(HandPose)))
                {
                    rig.look.SetAngles(rig.look.Yaw, pitch);
                    rig.hands.SetPose(pose);
                    rig.hands.Snap();
                    yield return null;
                    yield return null;
                    yield return new WaitForFixedUpdate();
                    AssertWrists(rig, $"{pose}/{pitch}°");
                }
            foreach (Tool tool in new[] { Tool.Shovel, Tool.Rake, Tool.Broom })
            {
                rig.hands.SetTool(tool);
                rig.hands.SetPose(HandPose.Shovel);
                rig.hands.Snap();
                yield return null;
                yield return new WaitForFixedUpdate();
                AssertWrists(rig, tool.ToString());
            }
            rig.hands.SetTool(Tool.None);
            rig.look.SetAngles(rig.look.Yaw, 0f);
            rig.hands.SetPose(HandPose.Carry);
            rig.hands.SetShoulderCarry(true, rig.interactor.holdTwoHands);
            foreach (float lean in new[] { -22f, 0f, 22f })
            {
                rig.interactor.holdTwoHands.localRotation = Quaternion.Euler(0f, 0f, lean);
                rig.hands.Snap();
                yield return null;
                yield return new WaitForFixedUpdate();
                AssertWrists(rig, $"Vác vai {lean}°");
            }
            rig.hands.SetShoulderCarry(false, null);
            foreach (bool prologue in new[] { false, true })
            {
                yield return rig.EnterHandsShot(GameRoot.Instance.Config.handsShot, prologue);
                rig.hands.Snap();
                yield return null;
                yield return new WaitForFixedUpdate();
                AssertWrists(rig, prologue ? "P3" : "B1.2/E2");
                HandsShotPlayTests.AssertPalms(rig.cam, rig.hands);
            }
        }

        [UnityTest]
        public IEnumerator ControllerMovementDrivesIdleWalkRunStrafeAndBackwardWithoutFootFreeze()
        {
            yield return Prepare(SceneFlow.GameScenes[0]);
            var rig = PlayerRig.Current;
            var input = GameRoot.Instance.Input;
            var foot = rig.body.animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            float oldWalk = rig.motor.walkSpeed;
            float oldCapture = Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                input.ClearBlocks();
                rig.SetControl(true);
                rig.interactor.enabled = false;
                foreach (var step in new[]
                {
                    (Vector2.up, 2f, "Walk"), (Vector2.up, 3f, "Run"),
                    (Vector2.right, 2f, "Strafe"), (Vector2.left, 2f, "Strafe"), (Vector2.down, 2f, "Backward")
                })
                {
                    rig.motor.Teleport(new Vector3(24f, -4.1f, -20f), 0f);
                    rig.motor.walkSpeed = step.Item2;
                    input.SimMove = step.Item1;
                    yield return new WaitForSeconds(0.35f);
                    var first = rig.transform.InverseTransformPoint(foot.position);
                    float excursion = 0f;
                    for (int frame = 0; frame < 45; frame++)
                    {
                        yield return null;
                        excursion = Mathf.Max(excursion, Vector3.Distance(first, rig.transform.InverseTransformPoint(foot.position)));
                    }
                    Assert.IsTrue(rig.body.animator.GetCurrentAnimatorStateInfo(0).IsName(step.Item3), step.Item3);
                    Assert.That(rig.body.ActualSpeed, Is.EqualTo(rig.motor.CurrentSpeed).Within(0.25f), "nhịp dùng dịch chuyển controller thật");
                    Assert.Greater(excursion, 0.04f, step.Item3 + ": chân phải bước");
                }
                input.SimMove = Vector2.zero;
                yield return new WaitForSeconds(0.4f);
                Assert.IsTrue(rig.body.animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"));
                Assert.Less(rig.body.ActualSpeed, 0.035f);
            }
            finally
            {
                input.SimMove = Vector2.zero;
                rig.motor.walkSpeed = oldWalk;
                Time.captureDeltaTime = oldCapture;
            }
        }

        [UnityTest]
        public IEnumerator LongApronClearsBothLegsThroughSquatWalkAndStairPoses()
        {
            yield return Prepare(SceneFlow.GameScenes[1]);
            var rig = PlayerRig.Current;
            rig.hands.SetPose(HandPose.Lowered);
            var body = rig.body;
            body.enabled = false;
            var animator = body.animator;
            var bones = new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
                HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot };
            foreach (string state in new[] { "Idle", "Walk", "Run", "Stairs", "Squat" })
                for (int frame = 0; frame <= 20; frame++)
                {
                    animator.Play(state, 0, frame / 20f);
                    animator.Update(0f);
                    body.AlignBodyAndHands();
                    var apron = body.apron;
                    var vertices = apron.Mesh.vertices;
                    var indices = apron.Mesh.triangles;
                    for (int triangle = 0; triangle < indices.Length; triangle += 3)
                    {
                        var a = vertices[indices[triangle]];
                        var b = vertices[indices[triangle + 1]];
                        var c = vertices[indices[triangle + 2]];
                        foreach (var p in new[] { a, (a + b) * 0.5f, (a + b + c) / 3f })
                            for (int leg = 0; leg < 2; leg++) for (int segment = 0; segment < 2; segment++)
                            {
                                var start = apron.transform.InverseTransformPoint(animator.GetBoneTransform(bones[leg * 3 + segment]).position);
                                var end = apron.transform.InverseTransformPoint(animator.GetBoneTransform(bones[leg * 3 + segment + 1]).position);
                                var axis = end - start;
                                float t = Mathf.Clamp01(Vector3.Dot(p - start, axis) / axis.sqrMagnitude);
                                float distance = Vector3.Distance(p, start + axis * t);
                                Assert.Greater(distance, segment == 0 ? 0.12f : 0.085f, $"{state}/{frame}: vải xuyên chân {leg}/{segment}");
                            }
                    }
                    if (state == "Idle")
                    {
                        Assert.Greater(apron.Mesh.bounds.size.y * apron.transform.lossyScale.y, 0.60f, "tạp dề phải dài tới giữa ống chân");
                        Assert.Less(apron.Mesh.bounds.min.y, 0.48f);
                    }
                }
            body.enabled = true;
        }

        [UnityTest]
        public IEnumerator ShoeSolesStayAboveGroundInBothScenes()
        {
            foreach (string scene in new[] { SceneFlow.GameScenes[0], SceneFlow.GameScenes[1] })
            {
                yield return Prepare(scene);
                var rig = PlayerRig.Current;
                rig.hands.SetPose(HandPose.Lowered);
                rig.look.SetAngles(rig.look.Yaw, 80f);
                yield return null;
                yield return null;
                yield return new WaitForFixedUpdate();
                var mesh = new Mesh();
                try
                {
                    rig.body.skin.BakeMesh(mesh, true);
                    var vertices = mesh.vertices;
                    var controller = rig.GetComponent<CharacterController>();
                    foreach (var bone in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                    {
                        var foot = rig.body.animator.GetBoneTransform(bone);
                        var ray = new Ray(foot.position + Vector3.up * 0.35f, Vector3.down);
                        Physics.Raycast(ray, out var firstHit, 0.7f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                        float ground = float.NegativeInfinity;
                        foreach (var hit in Physics.RaycastAll(ray, 0.7f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                            if (hit.collider != controller && hit.normal.y > 0.6f) ground = Mathf.Max(ground, hit.point.y);
                        Assert.IsFalse(float.IsNegativeInfinity(ground), $"{scene}/{bone}: thiếu mặt sàn");
                        float sole = float.PositiveInfinity;
                        foreach (int index in mesh.GetTriangles(5))
                        {
                            var world = rig.body.skin.transform.TransformPoint(vertices[index]);
                            if (Vector3.Dot(world - rig.transform.position, rig.transform.right) *
                                Vector3.Dot(foot.position - rig.transform.position, rig.transform.right) > 0f)
                                sole = Mathf.Min(sole, world.y);
                        }
                        var calf = foot.parent;
                        var thigh = calf.parent;
                        Debug.Log($"CostumeGround {scene}/{bone}: đế={sole:F4}, sàn={ground:F4}, chân={foot.position:F3}, gối={calf.position:F3}, hông={thigh.position:F3}, dài={Vector3.Distance(thigh.position, calf.position) + Vector3.Distance(calf.position, foot.position):F4}, grounded={controller.isGrounded}, speed={rig.body.ActualSpeed:F4}, chạm={firstHit.collider?.name}, gấu={rig.body.apron.Renderer.bounds.min:F3}, camera={rig.cam.transform.position:F3}");
                        Assert.LessOrEqual(sole, ground + 0.04f, $"{scene}/{bone}: giày lơ lửng trên sàn");
                        Assert.GreaterOrEqual(sole, ground - 0.005f,
                            $"{scene}/{bone}: đế={sole:F4}, sàn={ground:F4}, chạm đầu={firstHit.collider?.name}, grounded={controller.isGrounded}");
                    }
                }
                finally { UnityEngine.Object.Destroy(mesh); }
            }
        }

        [UnityTest]
        public IEnumerator BodyClothCoversBackOfCuffForEveryHandPose()
        {
            yield return Prepare(SceneFlow.GameScenes[0]);
            var rig = PlayerRig.Current;
            rig.look.SetAngles(rig.look.Yaw, 0f);
            var go = new GameObject("Kiểm mối nối vải", typeof(MeshCollider));
            go.layer = 2; // Không tham gia raycast đặt chân của runtime.
            go.transform.SetParent(rig.body.skin.transform, false);
            var collider = go.GetComponent<MeshCollider>();
            var mesh = new Mesh();
            try
            {
                foreach (HandPose pose in Enum.GetValues(typeof(HandPose)))
                {
                    rig.hands.SetPose(pose);
                    yield return null;
                    yield return null;
                    yield return new WaitForFixedUpdate();
                    // Bù scale trong BakeMesh vì collider còn dùng transform 0,95 của thân; không nhân scale hai lần.
                    collider.sharedMesh = null;
                    rig.body.skin.BakeMesh(mesh, true);
                    var shirt = mesh.GetTriangles(0);
                    mesh.subMeshCount = 1;
                    mesh.SetTriangles(shirt, 0);
                    collider.sharedMesh = mesh;
                    Physics.SyncTransforms();
                    foreach (var hand in new[] { rig.hands.left, rig.hands.right })
                    {
                        // Cuff thật kết thúc ở z=-18 cm. Vải thân phải phủ đủ bốn phía ngay dưới mép đó.
                        var rotation = rig.body.SleeveRotation(hand == rig.hands.left);
                        var center = hand.position + rotation * new Vector3(0f, 0f, -0.17f);
                        var right = rotation * Vector3.right; var up = rotation * Vector3.up;
                        foreach (var direction in new[] { right, -right, up, -up })
                            Assert.IsTrue(collider.Raycast(new Ray(center + direction * 0.09f, -direction), out _, 0.09f),
                                $"{pose}/{hand.name}: khe vải ở chân cuff theo {direction:F3}");
                    }
                }
            }
            finally
            {
                collider.sharedMesh = null;
                UnityEngine.Object.Destroy(go);
                UnityEngine.Object.Destroy(mesh);
            }
        }

        [UnityTest]
        public IEnumerator CostumeHasHatButtonsClosedShoesAndMatchingSleeves()
        {
            yield return Prepare(SceneFlow.GameScenes[0]);
            var body = PlayerRig.Current.body;
            foreach (int slot in new[] { 0, 1, 4, 5, 6, 7, 8, 9 })
                Assert.Greater(body.skin.sharedMesh.GetIndexCount(slot), 30, "thiếu phần trang phục: " + slot);
            var hat = body.skin.sharedMaterials[6];
            Assert.AreEqual("HaiBanTay/PlayerHeadShadow", hat.shader.name);
            Assert.GreaterOrEqual(hat.FindPass("ShadowCaster"), 0);
            Assert.GreaterOrEqual(hat.FindPass("OutsideView"), 0, "camera ngoài phải thấy mũ");
            Assert.IsNotNull(body.apron);
            var shirt = body.skin.sharedMaterials[0];
            Assert.LessOrEqual(shirt.GetColor("_BaseColor").maxColorComponent, 0.71f, "vải không cháy trắng");
            foreach (var renderer in PlayerRig.Current.hands.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.name.StartsWith("Sleeve")) Assert.AreSame(shirt, renderer.sharedMaterial);
        }

        static void AssertWrists(PlayerRig rig, string pose)
        {
            Assert.Less(Vector3.Distance(rig.body.LeftWrist.position, rig.hands.left.position), 0.02f, pose + "/trái");
            Assert.Less(Vector3.Distance(rig.body.RightWrist.position, rig.hands.right.position), 0.02f, pose + "/phải");
            Assert.LessOrEqual(rig.body.ArmStretch, 1.9f);
        }

        static void AssertToesNotHiddenByCloth(PlayerRig rig, Mesh mesh, string pose)
        {
            var bodyGo = new GameObject("Kiểm che mũi giày", typeof(MeshCollider));
            var apronGo = new GameObject("Kiểm che tạp dề", typeof(MeshCollider));
            bodyGo.layer = apronGo.layer = 2;
            bodyGo.transform.SetParent(rig.body.skin.transform, false);
            apronGo.transform.SetParent(rig.body.apron.transform, false);
            try
            {
                var bodyCollider = bodyGo.GetComponent<MeshCollider>();
                var apronCollider = apronGo.GetComponent<MeshCollider>();
                bodyCollider.sharedMesh = mesh;
                apronCollider.sharedMesh = rig.body.apron.Mesh;
                Physics.SyncTransforms();
                int visible = 0;
                var vertices = mesh.vertices;
                foreach (int index in mesh.GetTriangles(8))
                {
                    var world = rig.body.skin.transform.TransformPoint(vertices[index]);
                    var viewport = rig.cam.WorldToViewportPoint(world);
                    if (viewport.z <= rig.cam.nearClipPlane || viewport.x <= 0f || viewport.x >= 1f || viewport.y <= 0f || viewport.y >= 1f) continue;
                    var offset = world - rig.cam.transform.position;
                    var ray = new Ray(rig.cam.transform.position, offset.normalized);
                    if (!bodyCollider.Raycast(ray, out _, offset.magnitude - 0.005f)
                        && !apronCollider.Raycast(ray, out _, offset.magnitude - 0.005f)) visible++;
                }
                Assert.Greater(visible, 12, pose + ": mũi giày phải thấy thật, không bị áo/tạp dề che");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(bodyGo);
                UnityEngine.Object.DestroyImmediate(apronGo);
            }
        }

        [UnityTest]
        public IEnumerator HeadKeepsShadowAndOutsideViewWithoutPrimaryDepthPass()
        {
            yield return Prepare(SceneFlow.GameScenes[0]);
            var body = PlayerRig.Current.body;
            var material = body.skin.sharedMaterials[body.headSubmesh];
            Assert.Greater(body.skin.sharedMesh.GetTriangles(body.headSubmesh).Length, 100);
            Assert.AreEqual("HaiBanTay/PlayerHeadShadow", material.shader.name);
            Assert.GreaterOrEqual(material.FindPass("ShadowCaster"), 0, "đầu phải có pass đổ bóng");
            Assert.AreEqual(-1, material.FindPass("ForwardLit"), "đầu không có pass camera");
            Assert.AreEqual(-1, material.FindPass("DepthOnly"));
            Assert.GreaterOrEqual(material.FindPass("OutsideView"), 0);
            var properties = new MaterialPropertyBlock();
            body.skin.GetPropertyBlock(properties);
            Assert.AreEqual(1f, properties.GetFloat("_HidePlayerHead"));
            Assert.AreEqual(ShadowCastingMode.On, body.skin.shadowCastingMode);
            Assert.IsTrue(body.skin.enabled);
        }
    }
}
