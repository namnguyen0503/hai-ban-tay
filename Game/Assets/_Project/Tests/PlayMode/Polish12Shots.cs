using System.Collections;
using System.Linq;
using HaiBanTay.Core;
using HaiBanTay.Ending;
using HaiBanTay.Npc;
using HaiBanTay.Tasks;
using UnityEngine.Rendering.Universal;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Ship;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace HaiBanTay.Tests
{
    /// <summary>Ảnh QA12 dùng ánh sáng gameplay; không có fill dành riêng cho ảnh.</summary>
    [Category("Screenshots"), Explicit]
    public sealed class Polish12Shots
    {
        static GameRoot Root => GameRoot.Instance;
        static PlayerRig Rig => PlayerRig.Current;
        static ShiftDirector Director => Object.FindAnyObjectByType<ShiftDirector>();
        [SetUp] public void SetUp() => Time.captureDeltaTime = 1f / 60f;
        [TearDown] public void TearDown()
        {
            if (Root != null) { Root.Input.SimUse = false; Root.Input.SimLook = Vector2.zero; Root.Subtitles.StopAll(); }
            Time.captureDeltaTime = 0f; ScreenshotCapture.RestoreUi(); PlayTestUtil.ExitSim();
        }

        static IEnumerator Capture(string name)
        {
            Root.UI.SetFade(0f);
            yield return ScreenshotCapture.Capture("polish12_after_" + name);
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator StormSecondWaveGameplay()
        {
            PlayTestUtil.ExitSim(); var root = PlayTestUtil.Root(); root.ResetProgress();
            ChapterStart.PrepareDay(root, 4, 4, false);
            SceneManager.LoadScene(SceneFlow.GameScenes[1]);
            yield return PlayTestUtil.WaitUntil(() => Director != null && Director.Day != null && Director.Day.Number == 4 && Director.Beat == "D4.sweep", 40f);
            var d = Director; Assert.AreEqual("D4.sweep", d.Beat);
            d.StopAllCoroutines(); d.Clock.Running = false; Root.Subtitles.StopAll();
            d.storm.StartStorm(); d.people.seasick.SetActive(true);
            var haul = d.haul; haul.waveEnabled = true; haul.OnOrder(2);
            Rig.motor.Teleport(haul.Logic.PointAt(0.6f), 90f); haul.OnInteract(haul.basketTarget, Rig.interactor);
            Root.Input.SimUse = true;
            yield return VoyagePlayTests.Follow(haul, Rig, () => haul.Pending == 1);
            Root.Input.SimUse = false;
            Assert.AreEqual(1, haul.DeliveredTrips); Assert.IsFalse(haul.WaveDone);
            Rig.motor.Teleport(haul.Logic.PointAt(0.6f), 90f); haul.OnInteract(haul.basketTarget, Rig.interactor);
            var lost = haul.basket; Root.Input.SimUse = true;
            yield return VoyagePlayTests.Follow(haul, Rig, () => haul.WaveDone);
            Root.Input.SimUse = false;
            Assert.AreEqual(1, haul.LostBaskets); Assert.AreEqual(1, haul.Pending);
            yield return PlayTestUtil.WaitUntil(() => Root.UI.Tint.SplashLevel < 0.02f && !Rig.ControlEnabled, 5f);
            Assert.Less(Root.UI.Tint.SplashLevel, 0.02f);
            Assert.IsFalse(Rig.ControlEnabled, "Đúng beat bám cột, camera game còn khóa.");
            Assert.AreEqual(ShipRegion.Exterior, d.lights.Region);
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(x => x.isActiveAndEnabled && x.bakingOutput.lightmapBakeType != LightmapBakeType.Baked).ToArray();
            Assert.LessOrEqual(lights.Length, 3); Assert.LessOrEqual(lights.Count(x => x.shadows != LightShadows.None), 1);
            Assert.IsTrue(lights.Any(x => x.transform.parent.name == "Lamp_StormMast"));
            yield return Capture("01_storm_second_wave_gameplay");
            yield return PlayTestUtil.WaitUntil(() => Rig.ControlEnabled, 10f);
            Assert.IsTrue(lost == null); Assert.AreNotSame(lost, haul.basket);
            ChoreTestUtil.Look(new Vector3(-16f, 0f, 4.6f), new Vector3(-15f, .8f, 5.8f));
            yield return new WaitForSeconds(.4f); yield return Capture("01_storm_shipmates_gameplay");
            ChoreTestUtil.Look(haul.Logic.PointAt(0.8f), haul.BasketPosition + Vector3.up * 0.2f);
            yield return new WaitForSeconds(0.4f);
            yield return Capture("21_replacement_basket");
            haul.OnInteract(haul.basketTarget, Rig.interactor); Root.Input.SimUse = true;
            yield return VoyagePlayTests.Follow(haul, Rig, () => haul.Pending == 0);
            Root.Input.SimUse = false;
            Assert.AreEqual(2, haul.DeliveredTrips); Assert.AreEqual(1, haul.LostBaskets);
        }

        static void Arms(string context)
        {
            foreach (bool left in new[] { true, false })
            {
                var shoulder = Rig.body.animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                var elbow = Rig.body.animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                var wrist = left ? Rig.body.LeftWrist : Rig.body.RightWrist;
                var hand = left ? Rig.hands.left : Rig.hands.right;
                Debug.Log($"Tầm {context} {(left ? "trái" : "phải")}: vai={Rig.transform.InverseTransformPoint(shoulder.position):F4} grip={Rig.transform.InverseTransformPoint(hand.position):F4} cổ tay={Rig.transform.InverseTransformPoint(wrist.position):F4} dài={Vector3.Distance(shoulder.position, elbow.position) + Vector3.Distance(elbow.position, wrist.position):F4}");
            }
            Assert.LessOrEqual(Rig.body.ArmStretch, 1.031f, context + ": giới hạn giãn tay");
            Assert.Less(Vector3.Distance(Rig.body.LeftWrist.position, Rig.hands.left.position), .02f, context + ": cổ tay trái");
            Assert.Less(Vector3.Distance(Rig.body.RightWrist.position, Rig.hands.right.position), .02f, context + ": cổ tay phải");
            // Dò đúng vị trí skin ở thế giới, tránh nhân scale hai lần khi BakeMesh rồi gắn collider vào renderer.
            var source = Rig.body.skin.sharedMesh;
            var vertices = source.vertices; var weights = source.boneWeights; var bind = source.bindposes;
            var matrices = Rig.body.skin.bones.Select((bone, i) => bone.localToWorldMatrix * bind[i]).ToArray();
            for (int i = 0; i < vertices.Length; i++)
            {
                var w = weights[i]; var v = vertices[i];
                vertices[i] = matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0
                    + matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1
                    + matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2
                    + matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
            }
            var mesh = new Mesh { indexFormat = source.indexFormat, vertices = vertices, triangles = source.GetTriangles(0) };
            var go = new GameObject("Polish12SleeveProbe"); go.layer = 2;
            var collider = go.AddComponent<MeshCollider>(); collider.sharedMesh = mesh; Physics.SyncTransforms();
            foreach (bool left in new[] { true, false })
            {
                var elbow = Rig.body.animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                var wrist = left ? Rig.body.LeftWrist : Rig.body.RightWrist;
                var axis = (wrist.position - elbow.position).normalized;
                var across = Vector3.Cross(axis, Rig.transform.up).normalized;
                var up = Vector3.Cross(across, axis).normalized;
                foreach (float t in new[] { .05f, .2f, .4f })
                    foreach (var direction in new[] { across, -across, up, -up })
                    {
                        var center = Vector3.Lerp(elbow.position, wrist.position, t);
                        Assert.IsTrue(collider.Raycast(new Ray(center + direction * .16f, -direction), out _, .16f),
                            context + ": vải kín quanh khuỷu " + (left ? "trái" : "phải") + " tại " + t + " hướng " + direction + " tâm " + center);
                    }
            }
            Object.Destroy(go); Object.Destroy(mesh);
        }
        static IEnumerator Detail(string name, Vector3 eye, Vector3 target)
        {
            var go = new GameObject("Polish12Detail"); var camera = go.AddComponent<Camera>();
            camera.CopyFrom(Rig.cam); camera.enabled = false; camera.fieldOfView = 50f;
            var source = Rig.cam.GetUniversalAdditionalCameraData(); var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = source.renderPostProcessing; data.volumeLayerMask = source.volumeLayerMask; data.volumeTrigger = Rig.cam.transform;
            go.transform.position = eye; go.transform.LookAt(target);
            yield return ScreenshotCapture.CaptureCamera(camera, "polish12_after_" + name);
            Object.Destroy(go);
        }
        static IEnumerator Side(string name) => Detail(name, Rig.transform.TransformPoint(new Vector3(2.3f, 1.15f, .65f)), Rig.transform.TransformPoint(new Vector3(0f, .85f, .06f)));
        static IEnumerator LeftSide(string name) => Detail(name, Rig.transform.TransformPoint(new Vector3(-1.8f, 1.2f, .3f)), Rig.transform.TransformPoint(new Vector3(0f, 1f, .15f)));
        static IEnumerator Aimed(PeelStation task, System.Func<bool> until, float seconds)
        {
            for (float t = 0f; t < seconds && !until(); t += Time.deltaTime)
            {
                if (!Rig.look.Locked) ChoreTestUtil.Look(task.stand, task.board.transform.position);
                yield return null;
            }
            Assert.IsTrue(until(), "Không hoàn thành lượt gọt qua đầu vào thật.");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator BodySleevesApronAndChores()
        {
            yield return ChoreTestUtil.Galley(); var d = Director;
            ChoreTestUtil.Look(new Vector3(2f, 0f, -1.2f), new Vector3(2f, 1.6f, 2f));
            yield return new WaitForSeconds(.5f); Arms("idle");
            yield return Detail("03_idle_front", Rig.transform.TransformPoint(new Vector3(0f, 1.15f, 2.5f)), Rig.transform.TransformPoint(Vector3.up * .95f));
            yield return Side("08_apron_idle");
            Rig.look.SetAngles(Rig.look.Yaw, 80f); yield return new WaitForSeconds(.5f);
            yield return Side("03_08_bent_sleeves_apron"); Arms("gập"); yield return Capture("08_apron_look_down");
            Rig.SetOutfit(TrangPhuc.Thuong); yield return new WaitForSeconds(.4f);
            yield return Side("03_ordinary_sleeves"); Rig.SetOutfit(TrangPhuc.PhuBep);
            Rig.look.SetAngles(Rig.look.Yaw, 0f);
            var sack = d.sacks.sacks[0]; sack.gameObject.SetActive(true); sack.Locked = false; sack.Interact(Rig.interactor);
            yield return new WaitForSeconds(.6f); Assert.AreSame(sack, Rig.interactor.Holding); Arms("vác bao");
            yield return Detail("02_03_11_sack_side", Rig.transform.TransformPoint(new Vector3(2.2f, 1.6f, .9f)), Rig.transform.TransformPoint(new Vector3(.22f, 1.4f, .45f))); yield return Capture("02_sack_gameplay");
            Rig.interactor.ReleaseHeld(); sack.gameObject.SetActive(false);
            var water = d.storyStations.OfType<VoyageDelivery>().First(x => x.Kind == OrderKinds.Water);
            water.OnOrder(1); water.load.Interact(Rig.interactor); water.OnInteract(water.destination, Rig.interactor);
            ChoreTestUtil.Look(water.deliveryStand, water.destination.transform.position); yield return new WaitForSeconds(.4f);
            Root.Input.SimUse = true;
            yield return PlayTestUtil.WaitUntil(() => water.Progress > .35f, 5f);
            Assert.Greater(water.Progress, .35f); yield return new WaitForFixedUpdate(); Arms("rót nước");
            yield return Capture("02_water_gameplay"); yield return LeftSide("02_water_side");
            yield return PlayTestUtil.WaitUntil(() => water.Pending == 0, 5f); Assert.AreEqual(0, water.Pending); Root.Input.SimUse = false;
            d.coal.hook.Interact(Rig.interactor);
            ChoreTestUtil.Look(new Vector3(.3f, 0f, 1.5f), d.coal.bin.transform.position); yield return new WaitForSeconds(.4f);
            Root.Input.SimUse = true; yield return PlayTestUtil.WaitUntil(() => d.coal.Shovel.Loaded, 5f);
            Assert.IsTrue(d.coal.Shovel.Loaded); yield return new WaitForFixedUpdate(); Arms("xúc than");
            yield return Side("02_03_coal_side"); Root.Input.SimUse = false; Rig.interactor.ReleaseTool();
            var pan = d.pan; pan.Activate(); pan.OnOrder(1);
            var start = new Vector3(-.76f, 0f, .94f); pan.pan.position = start; pan.pan.transform.position = start;
            ChoreTestUtil.Look(start + Vector3.left * .9f, pan.handle.transform.position); yield return new WaitForSeconds(.4f);
            Root.Input.SimUse = true; yield return PlayTestUtil.WaitUntil(() => pan.Tow.Gripping, 5f);
            Assert.IsTrue(pan.Tow.Gripping); yield return new WaitForFixedUpdate(); Arms("kéo chảo");
            yield return Capture("02_pan_gameplay"); Root.Input.SimUse = false;
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator SackPourAndWashAlsoKeepReach()
        {
            yield return ChoreTestUtil.Galley(); var d = Director; var sacks = d.sacks;
            d.meals.cook.enabled = false;
            ChoreTestUtil.Look(new Vector3(1.32f, 0f, -1.05f), sacks.mouth.transform.position);
            sacks.zone.Place(sacks.sacks[0]); sacks.OnInteract(sacks.mouth, Rig.interactor);
            yield return new WaitForSeconds(.8f); yield return new WaitForFixedUpdate(); yield return Side("02_sack_untie_side"); Arms("cởi bao");
            Root.Input.SimUse = true; Root.Input.SimLook = new Vector2(0f, -12f);
            yield return PlayTestUtil.WaitUntil(() => sacks.Pour.Progress > .6f, 10f);
            Assert.Greater(sacks.Pour.Progress, .6f); yield return new WaitForFixedUpdate(); yield return Side("02_sack_pour_side"); Arms("dốc bao");
            yield return Capture("02_sack_pour_gameplay"); yield return Side("02_sack_pour_side");
            yield return PlayTestUtil.WaitUntil(() => sacks.Pour.Progress > .85f, 10f);
            yield return new WaitForFixedUpdate(); Arms("dốc bao gần hết"); yield return Side("02_sack_pour_end_side");
            yield return PlayTestUtil.WaitUntil(() => sacks.Pour.Empty, 10f); Assert.IsTrue(sacks.Pour.Empty);
            Root.Input.SimUse = false; Root.Input.SimLook = Vector2.zero;
            d.meals.BeginMeal(new HaiBanTay.Content.PhaseData { Id = "kiểm_tầm_rửa", Dirty = 1 });
            yield return d.meals.Serve(); var wash = d.wash;
            wash.OnInteract(wash.pile, Rig.interactor); wash.OnInteract(wash.basin, Rig.interactor); wash.OpenTap(); // nước chảy rồi mới cọ (QA-0132)
            ChoreTestUtil.Look(new Vector3(5.85f, 0f, -1.6f), wash.basin.transform.position + Vector3.up * .68f);
            yield return new WaitForSeconds(.4f);
            Root.Input.SimUse = true; Root.Input.SimLook = new Vector2(12f, 8f);
            yield return new WaitForSeconds(.4f); Assert.IsTrue(Rig.hands.Working);
            yield return new WaitForFixedUpdate(); yield return Detail("02_wash_side", Rig.transform.TransformPoint(new Vector3(-1.5f, 1.1f, -.7f)), Rig.transform.TransformPoint(new Vector3(0f, .8f, .25f))); Arms("rửa nồi");
            yield return Capture("02_wash_gameplay"); yield return Detail("02_wash_side", Rig.transform.TransformPoint(new Vector3(-1.5f, 1.1f, -.7f)), Rig.transform.TransformPoint(new Vector3(0f, .8f, .25f)));
            // Cận tay trên vành chậu, vành nồi: bên phải, đối diện qua chậu (góc người dùng thấy tay xuyên chậu 17/10).
            yield return Detail("02_wash_right", Rig.transform.TransformPoint(new Vector3(.9f, 1.15f, .35f)), Rig.transform.TransformPoint(new Vector3(0f, .98f, .45f)));
            // Từ trên xuống (trước đây góc đối diện qua chậu rơi vào trong vách): thấy hai bàn tay trên vành nồi và vành chậu cùng lúc.
            yield return Detail("02_wash_top", Rig.transform.TransformPoint(new Vector3(.1f, 2.05f, .15f)), Rig.transform.TransformPoint(new Vector3(0f, .9f, .45f)));
            Root.Input.SimUse = false; Root.Input.SimLook = Vector2.zero;
            yield return new WaitForSeconds(.2f); Assert.AreEqual(0f, Rig.motor.ExtraHeadHeight, .01f, "Trả tư thế khi nghỉ cọ.");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator SoupAlsoKeepsReach()
        {
            yield return ChoreTestUtil.Galley(); var d = Director;
            d.people.crewMeal.SetActive(true);
            var soup = d.storyStations.OfType<VoyageEncounter>().First(x => x.Kind == OrderKinds.Soup);
            soup.OnOrder(1); ChoreTestUtil.Look(soup.stand, soup.target.transform.position);
            yield return new WaitForSeconds(.4f); soup.OnInteract(soup.target, Rig.interactor);
            int sample = 0;
            foreach (float pause in new[] { .2f, .5f, .5f })
            {
                yield return new WaitForSeconds(pause); yield return new WaitForFixedUpdate(); Arms("cầm nĩa");
                if (++sample == 1) yield return Capture("02_soup_fork_gameplay");
                if (sample == 2) yield return Detail("02_soup_fork_side", Rig.transform.TransformPoint(new Vector3(.9f, 1.5f, -.15f)), Rig.transform.TransformPoint(new Vector3(0f, 1.2f, .2f)));
            }
            yield return PlayTestUtil.WaitUntil(() => !soup.Busy, 12f); Assert.IsFalse(soup.Busy);
            soup.OnInteract(soup.target, Rig.interactor);
            sample = 0;
            foreach (float pause in new[] { .2f, .5f, .5f })
            {
                yield return new WaitForSeconds(pause); yield return new WaitForFixedUpdate(); Arms("cầm thìa");
                if (++sample == 1) yield return Capture("02_soup_spoon_gameplay");
                if (sample == 2) yield return Detail("02_soup_spoon_side", Rig.transform.TransformPoint(new Vector3(.9f, 1.5f, -.15f)), Rig.transform.TransformPoint(new Vector3(0f, 1.2f, .2f)));
            }
            yield return PlayTestUtil.WaitUntil(() => !soup.Busy, 5f); Assert.AreEqual(0, soup.Pending);
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator PeelingReservedSpaceAndMaiButtons()
        {
            yield return ChoreTestUtil.Galley(); var d = Director;
            var task = d.storyStations.OfType<PeelStation>().First(x => !x.asparagus);
            var cook = task.nearbyCrew.OfType<CookRoutine>().Single();
            var tubHome = cook.tub.position; var tubParent = cook.tub.parent;
            cook.agent.PickUp(cook.tub); d.sacks.TubAway = true;
            task.OnOrder(2);
            Assert.IsNull(cook.agent.Carrying); Assert.IsFalse(d.sacks.TubAway);
            Assert.AreSame(tubParent, cook.tub.parent); Assert.Less(Vector3.Distance(tubHome, cook.tub.position), .001f);
            ChoreTestUtil.Look(task.stand, task.board.transform.position); yield return new WaitForSeconds(.3f);
            task.OnInteract(task.board, Rig.interactor);
            yield return PlayTestUtil.WaitUntil(() => task.ShowingDemo, 40f); Assert.IsTrue(task.ShowingDemo);
            foreach (var crew in task.nearbyCrew) { Assert.IsFalse(crew.enabled); Assert.Greater(Vector3.Distance(crew.agent.Position, task.maiSpot.position), 1f); }
            ChoreTestUtil.Look(task.stand, task.maiSpot.position + Vector3.up * 1.15f);
            yield return new WaitForSeconds(.35f);
            yield return Capture("05_peeling_lesson");
            yield return PlayTestUtil.WaitUntil(() => task.Ready, 40f); Assert.IsTrue(task.Ready);
            for (int n = 0; n < 2; n++)
            {
                if (!task.Ready) task.OnInteract(task.board, Rig.interactor);
                ChoreTestUtil.Look(task.stand, task.board.transform.position); yield return new WaitForSeconds(.3f);
                Root.Input.SimUse = true; Root.Input.SimLook = new Vector2(0f, 15f);
                yield return Aimed(task, () => task.Progress > .4f, 5f); yield return new WaitForFixedUpdate();
                if (n == 0) yield return Side("02_03_peeling_side");
                Arms("gọt");
                if (n == 0) { yield return Capture("02_05_peeling_gameplay"); yield return Side("02_03_peeling_side"); }
                int pending = task.Pending; yield return Aimed(task, () => task.Pending < pending, 5f);
                Root.Input.SimUse = false; Root.Input.SimLook = Vector2.zero;
            }
            Assert.AreEqual(0, task.Pending); foreach (var crew in task.nearbyCrew) Assert.IsTrue(crew.enabled, "Khôi phục routine sau beat.");
            d.cast.mai.Play("Idle");
            yield return Detail("07_mai_buttons_turn", d.cast.mai.Position + new Vector3(.8f, 1.25f, -1.1f), d.cast.mai.Position + Vector3.up * 1.1f);
            d.people.Evening(6); yield return new WaitForSeconds(.5f);
            yield return Detail("07_mai_buttons_sit", new Vector3(-3.3f, 1.05f, -5.6f), new Vector3(-4.3f, .85f, -4.3f));
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator CargoSeasickAndGalleyLighting()
        {
            yield return ChoreTestUtil.Galley(); var d = Director;
            foreach (var kind in new[] { OrderKinds.Meat, OrderKinds.Fish, OrderKinds.Ice })
            {
                var task = d.storyStations.OfType<VoyageDelivery>().First(x => x.Kind == kind); task.OnOrder(1);
                ChoreTestUtil.Look(task.pickupStand, task.load.transform.position + Vector3.up * .25f); yield return new WaitForSeconds(.4f);
                if (kind == OrderKinds.Meat) Assert.Greater(task.load.transform.position.y - task.pickupStand.y, .5f);
                if (kind == OrderKinds.Fish)
                    foreach (var fish in task.load.GetComponentsInChildren<MeshFilter>().Where(x => x.name == "WholeFish"))
                        foreach (var v in fish.sharedMesh.vertices)
                        { var p = task.load.transform.InverseTransformPoint(fish.transform.TransformPoint(v)); Assert.Less(Mathf.Abs(p.x), .2625f); Assert.Less(Mathf.Abs(p.z), .2475f); }
                yield return Capture(kind == OrderKinds.Meat ? "19_meat_tray" : kind == OrderKinds.Fish ? "09_fish_crate" : "10_ice_source");
                task.load.Interact(Rig.interactor);
                ChoreTestUtil.Look(new Vector3(-1.5f, 0f, .8f), new Vector3(-2.8f, 1f, 2.3f)); yield return new WaitForSeconds(.5f);
                Assert.AreSame(task.load, Rig.interactor.Holding);
                if (kind == OrderKinds.Fish) yield return LeftSide("03_fish_elbow");
                Arms(kind);
                if (kind == OrderKinds.Fish) yield return Capture("09_fish_held");
                if (kind == OrderKinds.Ice)
                {
                    Assert.Less(Vector3.Distance(Rig.hands.left.position, task.load.leftGrip.position), .01f);
                    Assert.Less(Vector3.Distance(Rig.hands.right.position, task.load.rightGrip.position), .01f);
                    yield return Capture("10_ice_grip_gameplay"); yield return LeftSide("10_ice_grip_side");
                }
                task.OnInteract(task.destination, Rig.interactor); Assert.AreEqual(0, task.Pending); task.load.gameObject.SetActive(false);
            }
            d.people.seasick.SetActive(true);
            ChoreTestUtil.Look(new Vector3(-16f, 0f, 4.6f), new Vector3(-15f, .8f, 5.8f)); yield return new WaitForSeconds(.5f);
            Assert.IsTrue(d.people.seasick.GetComponentsInChildren<SeasickPose>().All(x => !x.floorSeated));
            yield return Capture("06_seasick_seat");
            yield return Detail("06_seasick_seat_side", new Vector3(-12.9f, 1.3f, 4.5f), new Vector3(-15f, .85f, 5.8f));
            ChoreTestUtil.Look(new Vector3(1.8f, 0f, -1.5f), new Vector3(-1.5f, 1.5f, 1.8f)); yield return new WaitForSeconds(.5f);
            yield return Capture("11_galley_lighting");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator CafeSeatContacts()
        {
            PlayTestUtil.ExitSim(); PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[2]);
            var d = Object.FindAnyObjectByType<EndingDirector>(); Assert.IsNotNull(d); d.StopAllCoroutines(); Root.Subtitles.StopAll(); Root.UI.Card.Hide();
            ChoreTestUtil.Look(new Vector3(4.2f, -2.4f, -21.5f), new Vector3(4.8f, -1.7f, -23.6f));
            yield return new WaitForSeconds(4f);
            var guests = Object.FindObjectsByType<CrowdRoutine>(FindObjectsSortMode.None).Where(x => x.mode == CrowdMode.Sit && x.agent.Position.y < -2f && x.agent.Position.z < -22f).ToArray();
            Assert.AreEqual(4, guests.Length);
            foreach (var guest in guests)
            {
                guest.agent.SetAnimated(true);
                var a = guest.GetComponentInChildren<Animator>(); a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                a.Play("SitIdle", 0, 0f); a.Update(.01f);
                float bottom = float.PositiveInfinity; var mesh = new Mesh();
                foreach (var renderer in guest.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(x => x.sharedMesh.vertexCount).Take(1))
                { renderer.BakeMesh(mesh); foreach (var v in mesh.vertices) bottom = Mathf.Min(bottom, renderer.transform.TransformPoint(v).y); }
                Object.Destroy(mesh);
                Assert.AreEqual(guest.agent.Position.y, bottom, .035f, "Đế giày chạm sàn café.");
                Assert.AreEqual(guest.agent.Position.y + .575f, a.GetBoneTransform(HumanBodyBones.Hips).position.y, .045f, "Hông ngồi trên mặt ghế.");
            }
            yield return Capture("04_cafe_seats");
            yield return Detail("04_cafe_seat_side", new Vector3(4.6f, -1.25f, -24.8f), new Vector3(4.1f, -1.75f, -24f));
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator UiReadingQueue()
        {
            Time.captureDeltaTime = 0f; // Thẻ dùng unscaledTime thực, không dùng nhịp mô phỏng ảnh bão.
            yield return ChoreTestUtil.Galley(); var ui = Root.UI;
            ChoreTestUtil.Look(new Vector3(-1.4f, 0f, .2f), new Vector3(0f, 1f, 0f));
            yield return new WaitForSeconds(.3f);
            ui.Cards.Clear(); ui.Toast.Hide(); ui.Prompt.Clear(); Root.Subtitles.StopAll();
            var card = Root.Content.Cards.First(); var line = Root.Content.Lines.First(x => !string.IsNullOrEmpty(x.Vi));
            string hint = Root.Content.GetString("hint.pan_slip"), toast = Root.Content.GetString("notebook.new_page");
            ui.Cards.Show(card.Id); ui.Prompt.ShowHint(hint, 6f); ui.Toast.Show(toast); Root.Content.Subtitles.Speakers.TryGetValue(line.Speaker, out var speaker);
            yield return null; // Phát thoại sau Update của UI, như coroutine thoại trong gameplay.
            ui.Subtitles.Show(speaker, line.Fr, line.Vi);
            var action = ui.Root.Q<Label>(className: "hbt-prompt");
            Assert.IsNotNull(action);
            string actionText = Root.Content.GetString("prompt.hang_shovel");
            ui.Prompt.SetWorldPrompt(actionText);
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(ui.Toast.Visible, "Không chồng toast trong khung phụ đề vừa bắt đầu.");
            Assert.IsFalse(ui.Cards.Visible); Assert.IsFalse(ui.Prompt.HintVisible);
            yield return new WaitForSeconds(.4f);
            Assert.AreEqual(actionText, action.text); Assert.AreNotEqual(DisplayStyle.None, action.resolvedStyle.display, "Phụ đề không che prompt thao tác.");
            Assert.IsTrue(ui.Subtitles.Visible); Assert.IsFalse(ui.Toast.Visible); Assert.IsFalse(ui.Cards.Visible); Assert.IsFalse(ui.Prompt.HintVisible);
            yield return Capture("24_subtitle_only"); ui.Subtitles.Hide(); yield return new WaitForSeconds(.3f);
            Assert.IsTrue(ui.Toast.Visible); Assert.IsFalse(ui.Cards.Visible); Assert.IsFalse(ui.Prompt.HintVisible);
            Assert.AreNotEqual(DisplayStyle.None, action.resolvedStyle.display, "Toast không che prompt thao tác.");
            yield return Capture("24_toast_only"); yield return new WaitForSeconds(4.1f);
            Assert.IsTrue(ui.Cards.Visible); Assert.IsFalse(ui.Toast.Visible); Assert.IsFalse(ui.Prompt.HintVisible); Assert.AreEqual(card.Id, ui.Cards.CurrentId);
            Assert.AreNotEqual(DisplayStyle.None, action.resolvedStyle.display, "Thẻ nguồn không che prompt thao tác.");
            yield return Capture("24_compact_card_only"); yield return new WaitForSeconds(9.1f);
            Assert.IsTrue(ui.Prompt.HintVisible); Assert.IsFalse(ui.Cards.Visible); Assert.AreEqual(toast, ui.Toast.Text);
            yield return Capture("24_hint_only");
        }
    }
}
