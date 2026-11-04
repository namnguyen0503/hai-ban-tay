using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Ending;
using HaiBanTay.Interaction;
using HaiBanTay.Narrative;
using HaiBanTay.Notebook;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Ship;
using HaiBanTay.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>Chụp các beat chính để soát bằng mắt. Chỉ chạy qua Tools/screenshots.sh (cần GPU).</summary>
    [Category("Screenshots"), Explicit]
    public class ScreenshotTests
    {
        [TearDown]
        public void TearDown()
        {
            ScreenshotCapture.RestoreUi();
            Time.captureDeltaTime = 0f;
            PlayTestUtil.ExitSim();
        }

        static GameRoot R => GameRoot.Instance;
        static PlayerRig Rig => PlayerRig.Current;

        static IEnumerator Until(System.Func<bool> cond, float seconds = 60f)
        {
            yield return PlayTestUtil.WaitUntil(cond, seconds);
        }

        static IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
                yield return null;
        }

        static void LookFrom(Vector3 feet, Vector3 target)
        {
            Rig.motor.Teleport(feet, 0f);
            var head = feet + Vector3.up * 1.62f;
            var to = target - head;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            Rig.motor.Teleport(feet, yaw);
            Rig.look.SetAngles(yaw, pitch);
        }

        static IEnumerator Shot(string name)
        {
            yield return ScreenshotCapture.Capture(name);
            if (name == "01c_hands_shot" || name == "00h_hands_before_le" || name == "03c_pay_coins")
                HandsScreenshotCheck.Verify(name, Rig.cam, Rig.hands);
        }

        /// <summary>Điểm theo hệ cục bộ Unity của gốc tàu (Blender x, z, y) ra thế giới.</summary>
        static Vector3 Ship(Vector3 local) => Object.FindAnyObjectByType<ShipLayout>().transform.TransformPoint(local);

        [UnityTest]
        public IEnumerator ShotsCostumeReference()
        {
            foreach (string scene in new[] { SceneFlow.GameScenes[0], SceneFlow.GameScenes[1] })
            {
                PlayTestUtil.Root().ResetProgress();
                yield return PlayTestUtil.LoadAndSettle(scene);
                SceneDirector.Current.StopAllCoroutines();
                Rig.SetOutfit(TrangPhuc.PhuBep);
                Rig.SetControl(false);
                Rig.SetNoise(0f);
                Rig.hands.SetPose(HandPose.Lowered);
                foreach (float pitch in new[] { 70f, 80f, 85f })
                {
                    Rig.look.SetAngles(Rig.look.Yaw, pitch);
                    yield return Wait(0.3f);
                    Assert.AreEqual(pitch, Rig.look.Pitch, "Góc ảnh phải đúng góc cúi runtime");
                    string prefix = scene == SceneFlow.GameScenes[0] ? "costume_01_look_down" : "costume_02_look_down";
                    // Chụp camera thật trực tiếp: card mở cảnh đang dừng không được che bằng chứng trang phục.
                    yield return ScreenshotCapture.CaptureCamera(Rig.cam, prefix + "_" + pitch);
                    if (pitch == 80f) yield return ScreenshotCapture.CaptureCamera(Rig.cam, prefix);
                }
            }
            // Soát bo cổ tay và mối nối với ống tay thân ở từng tư thế thực tế.
            Rig.look.SetAngles(Rig.look.Yaw, 0f);
            foreach (HandPose pose in System.Enum.GetValues(typeof(HandPose)))
            {
                Rig.hands.SetPose(pose);
                yield return Wait(0.3f);
                yield return ScreenshotCapture.CaptureCamera(Rig.cam, "q_pose_" + pose);
            }
            Rig.hands.SetPose(HandPose.Lowered);
            // Camera độc lập nhìn đúng thân đang chơi; không tạo phiên bản trang phục khác để chụp.
            var body = Rig.body;
            Rig.look.SetAngles(Rig.look.Yaw, 0f);
            yield return Wait(0.2f);
            var go = new GameObject("CostumeOutsideCamera", typeof(Camera));
            var cam = go.GetComponent<Camera>();
            cam.fieldOfView = 36f;
            cam.nearClipPlane = 0.03f;
            var center = Rig.transform.position + Vector3.up * 0.94f;
            go.transform.position = center + Rig.transform.forward * 3.2f + Rig.transform.right * 0.5f;
            go.transform.LookAt(center);
            // Camera kiểm vật liệu dùng đèn trắng riêng; ảnh FP ở trên giữ ánh sáng đèn dầu thật.
            const int layer = 30;
            var renderers = new System.Collections.Generic.List<Renderer>(body.GetComponentsInChildren<Renderer>());
            renderers.AddRange(Rig.hands.GetComponentsInChildren<Renderer>(true));
            var oldLayers = new int[renderers.Count];
            var oldProbes = new UnityEngine.Rendering.LightProbeUsage[renderers.Count];
            for (int i = 0; i < renderers.Count; i++)
            {
                oldLayers[i] = renderers[i].gameObject.layer;
                oldProbes[i] = renderers[i].lightProbeUsage;
                renderers[i].gameObject.layer = layer;
                renderers[i].lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            }
            cam.cullingMask = 1 << layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.13f, 0.14f, 0.15f);
            var sceneLights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            var oldEnabled = new bool[sceneLights.Length];
            for (int i = 0; i < sceneLights.Length; i++)
            {
                oldEnabled[i] = sceneLights[i].enabled;
                sceneLights[i].enabled = false;
            }
            var key = new GameObject("CostumeInspectionLight", typeof(Light));
            key.transform.rotation = Quaternion.LookRotation(-Rig.transform.forward - Rig.transform.right * 0.5f - Vector3.up * 0.6f);
            var light = key.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2f;
            light.color = new Color(1f, 0.96f, 0.90f);
            light.cullingMask = 1 << layer;
            light.GetUniversalAdditionalLightData().renderingLayers = body.skin.renderingLayerMask;
            var oldAmbient = RenderSettings.ambientProbe;
            var ambient = new UnityEngine.Rendering.SphericalHarmonicsL2();
            ambient.AddAmbientLight(new Color(0.18f, 0.18f, 0.18f));
            RenderSettings.ambientProbe = ambient;
            var oldSun = RenderSettings.sun;
            RenderSettings.sun = light; // URP chọn đèn kiểm màu làm main light thay mặt trời của cảnh.
            try
            {
                foreach (float angle in new[] { 0f, 45f, 90f })
                {
                    var direction = Quaternion.AngleAxis(angle, Rig.transform.up) * Rig.transform.forward;
                    go.transform.position = center + direction * 3.2f;
                    go.transform.LookAt(center);
                    string name = angle == 0f ? "costume_full_body_front" : angle == 45f ? "costume_full_body_three_quarter" : "costume_full_body_side";
                    yield return ScreenshotCapture.CaptureCamera(cam, name);
                    if (angle == 0f) yield return ScreenshotCapture.CaptureCamera(cam, "costume_full_body");
                }
            }
            finally
            {
                for (int i = 0; i < sceneLights.Length; i++) sceneLights[i].enabled = oldEnabled[i];
                RenderSettings.sun = oldSun;
                RenderSettings.ambientProbe = oldAmbient;
                for (int i = 0; i < renderers.Count; i++)
                {
                    renderers[i].gameObject.layer = oldLayers[i];
                    renderers[i].lightProbeUsage = oldProbes[i];
                }
                Object.Destroy(go);
                Object.Destroy(key);
            }
        }

        /// <summary>
        /// M10b: con tàu thật trong 02 (giữa trưa để thấy rõ): ô cửa sổ từ boong, cửa bếp từ boong, thang và lòng hầm thực
        /// phẩm, khói ống bếp khi lò cháy, toàn tàu từ boong xuồng.
        /// </summary>
        [UnityTest]
        public IEnumerator Shots02Ship()
        {
            PlayTestUtil.Root().ResetProgress();
            ScreenshotCapture.RedirectUi();
            // Cảnh cũ (có thể cũng là 02 từ bài trước) còn sống tới khung sau: chờ director của cảnh mới.
            var before = SceneManager.GetActiveScene();
            SceneManager.LoadScene(SceneFlow.GameScenes[1]);
            var d = (ShiftDirector)null;
            yield return Until(() => (d = Object.FindAnyObjectByType<ShiftDirector>()) != null && d.gameObject.scene != before);
            yield return Until(() => d.Beat == "D1.sweep" && !R.UI.Card.Visible);
            d.coal.Fire.Set(FireLogic.MaxFuel, 1f);
            d.Clock.Minutes = 12f * 60f;
            yield return Wait(4f);

            LookFrom(new Vector3(-3.7f, 0f, 0.6f), new Vector3(-5f, 1.55f, 0.6f));
            yield return Wait(0.3f);
            yield return Shot("02s1_porthole_inside");
            LookFrom(new Vector3(-6.9f, 0f, 0.3f), new Vector3(-5.1f, 1.55f, 0.6f));
            yield return Wait(0.3f);
            yield return Shot("02s2_porthole_from_deck");
            LookFrom(new Vector3(-8.2f, 0f, 1.6f), new Vector3(-5.1f, 1.0f, 2.6f));
            yield return Wait(0.3f);
            yield return Shot("02s3_galley_door_from_deck");
            LookFrom(Ship(new Vector3(5.5f, 5.6f, 0f)), Ship(new Vector3(9.6f, 3.0f, 0f)));
            yield return Wait(0.3f);
            yield return Shot("02s4_hold_stairs_top");
            LookFrom(Ship(new Vector3(14.6f, 2.8f, -1.5f)), Ship(new Vector3(8.5f, 3.6f, 0.5f)));
            yield return Wait(0.3f);
            yield return Shot("02s5_hold_inside");
            // Từ boong giếng trước nhìn lên buồng lái: ống bếp nối xuyên mái, khói theo lửa lò.
            LookFrom(Ship(new Vector3(15f, 5.6f, -5f)), Ship(new Vector3(2.4f, 14.6f, -3.6f)));
            yield return Wait(1f);
            yield return Shot("02s6_chimney_smoke");
            // Đuôi boong xuồng, lệch mạn phải khỏi ống khói chính và giếng trời, nhìn về mũi.
            LookFrom(Ship(new Vector3(-36.6f, 11.35f, -2f)), Ship(new Vector3(25f, 9f, -2.5f)));
            yield return Wait(0.5f);
            yield return Shot("02s7_ship_from_boat_deck");
        }

        /// <summary>Prologue: thẻ mở đầu, phố có đèn điện, quán cà phê, hỏi đáp và khoảnh khắc giơ hai bàn tay trước anh Lê.</summary>
        [UnityTest]
        public IEnumerator Shots00Prologue()
        {
            PlayTestUtil.Root().ResetProgress();
            ScreenshotCapture.RedirectUi();
            var before = SceneManager.GetActiveScene();
            SceneManager.LoadScene(SceneFlow.PrologueScene);
            var d = (PrologueDirector)null;
            yield return Until(() => (d = Object.FindAnyObjectByType<PrologueDirector>()) != null && d.gameObject.scene != before);
            yield return Wait(2f);
            yield return Shot("00a_place_card");
            yield return Until(() => d.Beat == "P1" && !R.UI.Card.Visible);
            yield return Wait(0.5f);
            yield return Shot("00b_street_from_spawn");
            // Đèn điện trước quán cà phê (anchor electric_light_bulb) và quán cà phê.
            LookFrom(new Vector3(2f, 0.12f, -7.2f), new Vector3(8f, 4.6f, -6.2f));
            yield return Wait(0.5f);
            yield return Shot("00c_electric_light");
            LookFrom(new Vector3(0f, 0.12f, -3f), new Vector3(10f, 1.6f, -9f));
            yield return Wait(0.5f);
            yield return Shot("00d_cafe");
            LookFrom(new Vector3(-12f, 0.12f, 3f), new Vector3(-12f, 3f, -8f));
            yield return Wait(0.3f);
            yield return Shot("00e_street_lamp_facade");
            // Mặt tiền quán cà phê: cửa kính có khung, nội thất mờ phía sau, đèn treo, khách ngồi bàn.
            LookFrom(new Vector3(11f, 0.12f, -3.5f), new Vector3(11.5f, 1.5f, -9.8f));
            yield return Wait(0.6f);
            yield return Shot("00j_cafe_storefront");
            LookFrom(new Vector3(8.5f, 0.12f, -6.6f), new Vector3(12.5f, 1.35f, -9.8f));
            yield return Wait(0.6f);
            yield return Shot("00k_cafe_door_close");
            LookFrom(new Vector3(-26f, 0.12f, -1f), new Vector3(0f, 2.5f, -9f));
            yield return Wait(0.6f);
            yield return Shot("00l_street_length");
            // Vào hội thoại: đứng ở chỗ nói chuyện, chờ anh Lê tới.
            Rig.motor.Teleport(d.talkPlayer.position, d.talkPlayer.eulerAngles.y);
            Sim.Enabled = true;
            yield return Until(() => d.Beat == "P2", 120f);
            Sim.Enabled = false;
            yield return Wait(1.5f);
            yield return Shot("00f_conversation");
            yield return Until(() => d.Beat == "P3" && !R.Subtitles.IsPlaying, 90f);
            yield return Wait(0.5f);
            yield return Shot("00g_raise_prompt");
            R.Input.SimRaise = true;
            yield return Until(() => R.Events.HasFired(EventIds.S1HandsRaised), 20f);
            R.Input.SimRaise = false;
            yield return Wait(1f);
            yield return Shot("00h_hands_before_le");
            yield return Until(() => d.Beat == "P4", 30f);
            yield return Wait(4f);
            yield return Shot("00i_days_later_card");
        }

        [UnityTest]
        public IEnumerator Shots01NhaRong()
        {
            PlayTestUtil.Root().ResetProgress();
            ScreenshotCapture.RedirectUi();
            SceneManager.LoadScene(SceneFlow.GameScenes[0]);
            yield return Wait(3f);
            yield return Shot("01a_opening_card");
            var d = Object.FindAnyObjectByType<NhaRongDirector>();
            yield return Until(() => d.Beat == "B1.2");
            yield return Wait(0.5f);
            yield return Shot("01b_raise_prompt");
            R.Input.SimRaise = true;
            yield return Until(() => R.Events.HasFired(EventIds.S1HandsRaised));
            R.Input.SimRaise = false;
            yield return Wait(0.6f);
            yield return Shot("01c_hands_shot");
            Sim.Enabled = true;
            yield return Until(() => d.Beat == "B1.3", 120f);
            Sim.Enabled = false;
            yield return Wait(2.5f);
            yield return Shot("01d_quay_spawn");
            Rig.look.SetAngles(Rig.look.Yaw, 80f);
            yield return Wait(0.4f);
            yield return Shot("01d2_body_look_down");
            Rig.look.SetAngles(Rig.look.Yaw, 0f);
            R.UI.Notebook.OpenAt("p_why_france");
            yield return Wait(0.3f);
            yield return Shot("01e_notebook");
            R.UI.Notebook.Close();
            // Toàn cảnh kè có đám đông, từ phía đông nhìn về tây (tàu ở bên phải ảnh).
            LookFrom(new Vector3(32f, -4.1f, -17f), new Vector3(-12f, -1.5f, -20f));
            yield return Wait(1.5f);
            yield return Shot("01f_quay_crowd_overview");
            // Vác bao trên kè: cùng luồng nhặt của người chơi, thấy rõ đường lên tàu.
            Rig.interactor.Pick(d.sacks[0]);
            LookFrom(new Vector3(2.5f, -4.1f, -22f), new Vector3(-2.5f, -2.5f, -13f));
            yield return Wait(0.4f);
            ShipWalkPlayTests.AssertShoulderView(Rig.cam, d.sacks[0]);
            // Soát tư thế vác sau khi đóng thẻ tư liệu tạm ở góc phải; HUD và đường đi vẫn giữ nguyên.
            R.UI.Cards.Clear();
            yield return Shot("01f2_carry_quay");
            Rig.interactor.DropInFrontForTests();
            d.sacks[0].GetComponent<ReturnToStart>().Restore();
            yield return Wait(0.3f);
            // Mặt tiền Nhà Rồng từ giữa kè.
            LookFrom(new Vector3(6f, -4.1f, -30f), new Vector3(6f, 3.5f, -55f));
            yield return Wait(0.5f);
            yield return Shot("01g_nharong_facade");
            // Chân cầu thang tàu: hàng khách, người soát vé, phu vác bao lên.
            LookFrom(new Vector3(2.5f, -4.1f, -22f), new Vector3(-2.5f, -2.5f, -13f));
            yield return Wait(1f);
            yield return Shot("01h_gangway_queue");
            // Ông Mai và hai phu cạnh xe khoai, nhìn từ chỗ xuất hiện.
            LookFrom(new Vector3(1.5f, -4.1f, -17f), new Vector3(-5.5f, -3.2f, -18.3f));
            yield return Wait(0.5f);
            yield return Shot("01i_mai_porters");
            // Tàu nhìn từ kè.
            LookFrom(new Vector3(4f, -4.1f, -16.5f), new Vector3(-10f, 2f, -4f));
            yield return Wait(0.5f);
            yield return Shot("01j_ship_from_quay");
            // Trên boong: quản bếp ở cửa bếp, thủy thủ tựa lan can.
            LookFrom(new Vector3(-1.5f, 0f, -4.5f), new Vector3(-9f, 1.3f, -2.5f));
            yield return Wait(0.5f);
            yield return Shot("01k_deck_chef_door");
            LookFrom(new Vector3(1f, 0f, -3.5f), new Vector3(6.5f, 1.2f, -6.5f));
            yield return Wait(0.5f);
            yield return Shot("01l_deck_sailors_rail");
            // Vác bao thật cho tới hầm: Sim xếp bao rồi nhổ neo.
            LookFrom(new Vector3(1.8f, 0f, -2.6f), new Vector3(1.8f, 1f, -20f));
            yield return Wait(0.3f);
            Sim.Enabled = true;
            yield return Until(() => d.Beat == "B1.4");
            Sim.Enabled = false;
            LookFrom(new Vector3(-2.2f, -2.8f, -0.5f), new Vector3(-1.2f, -2.2f, 2.1f));
            yield return Wait(0.5f);
            yield return Shot("01m_hold_sacks");
            LookFrom(new Vector3(2f, 0f, -4.5f), new Vector3(-1.5f, -2f, -12f));
            yield return Wait(1.2f);
            yield return Shot("01n_gangway_retract");
            Rig.look.SetAngles(200f, -4f);
            yield return Wait(5f);
            yield return Shot("01o_depart");
            LookFrom(new Vector3(4f, 0f, -6f), new Vector3(10f, 0f, -40f));
            yield return Wait(8f);
            yield return Shot("01p_quay_receding");
            Sim.Enabled = true;
            yield return Until(() => R.UI.Map.Visible);
            yield return Wait(8f);
            yield return Shot("01q_map_A");
        }

        /// <summary>Hoàn tất lệnh của lượt hiện tại bằng đường Sim của các trạm (sự kiện giữ nguyên như khi chơi).</summary>
        static IEnumerator FinishPhase(ShiftDirector d)
        {
            string beat = d.Beat;
            Sim.Enabled = true;
            yield return Until(() => d.Beat != beat, 30f);
            Sim.Enabled = false;
        }

        [UnityTest]
        public IEnumerator Shots00Menu()
        {
            PlayTestUtil.Root();
            ScreenshotCapture.RedirectUi();
            SceneManager.LoadScene(SceneFlow.MenuScene);
            yield return Wait(2.5f);
            yield return Shot("00a_menu");
            R.UI.Menu.Click(2);
            yield return Wait(0.3f);
            yield return Shot("00b_chapters");
            R.UI.Menu.Click(R.UI.Menu.ButtonCount - 1);
            R.UI.Menu.Click(3);
            yield return Wait(0.3f);
            yield return Shot("00c_settings");
            R.UI.Settings.Close();
            yield return Wait(0.2f);
        }

        [UnityTest]
        public IEnumerator Shots02CarryInterpolationRegression()
        {
            PlayTestUtil.Root().ResetProgress();
            ScreenshotCapture.RedirectUi();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            yield return Until(() => d.Beat == "D1.sweep" && !R.UI.Card.Visible);
            yield return SimTo(d, "D1.breakfast");
            Assert.AreEqual("D1.breakfast", d.Beat);
            var sack = d.sacks.sacks[0];
            var start = sack.transform.position;
            var rotation = sack.transform.rotation;
            var legacy = sack.gameObject.AddComponent<LegacyCarryLateUpdateProbe>();
            legacy.enabled = false;
            legacy.FollowSpeed = sack.followSpeed;
            // Đối chứng code cũ: cùng bao, camera và một giây chờ; chỉ đổi nội suy Rigidbody.
            foreach (var interpolation in new[] { RigidbodyInterpolation.Interpolate, RigidbodyInterpolation.None })
            {
                LookFrom(Ship(new Vector3(11.6f, 2.8f, -0.4f)), start);
                yield return Wait(0.3f);
                Rig.interactor.Pick(sack);
                sack.enabled = false;
                sack.Body.interpolation = interpolation;
                sack.transform.SetParent(Rig.interactor.holdTwoHands, true);
                legacy.enabled = true;
                LookFrom(Ship(new Vector3(10.4f, 2.8f, 0f)), Ship(new Vector3(6.1f, 6.2f, 0f)));
                yield return Wait(1f);
                var renderer = sack.GetComponentInChildren<Renderer>();
                Debug.Log($"Carry đối chứng {interpolation}: local={sack.transform.localPosition}, viewport={Rig.cam.WorldToViewportPoint(renderer.bounds.center)}");
                yield return Shot("integrate_carry_legacy_" + interpolation);
                legacy.enabled = false;
                Rig.interactor.ReleaseHeld();
                sack.Detach(start, rotation, physics: false);
                sack.enabled = true;
                yield return Wait(0.3f);
            }
            LookFrom(Ship(new Vector3(11.6f, 2.8f, -0.4f)), start);
            yield return Wait(0.3f);
            Rig.interactor.Pick(sack);
            LookFrom(Ship(new Vector3(10.4f, 2.8f, 0f)), Ship(new Vector3(6.1f, 6.2f, 0f)));
            yield return Wait(1f);
            Assert.AreEqual(RigidbodyInterpolation.None, sack.Body.interpolation);
            ShipWalkPlayTests.AssertShoulderView(Rig.cam, sack);
            R.UI.Cards.Clear();
            yield return Shot("integrate_carry_current");
            Object.Destroy(legacy);
        }

        [UnityTest]
        public IEnumerator Shots02Day1()
        {
            PlayTestUtil.Root().ResetProgress();
            ScreenshotCapture.RedirectUi();
            SceneManager.LoadScene(SceneFlow.GameScenes[1]);
            yield return Wait(2f);
            yield return Shot("02a_card_day1");
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            yield return Until(() => d.Beat == "D1.sweep" && !R.UI.Card.Visible);
            yield return Wait(1f);
            LookFrom(new Vector3(1.6f, 0f, 0.4f), d.board.transform.position);
            yield return Wait(0.5f);
            yield return Shot("02a2_chalkboard_hud");
            Rig.look.SetAngles(Rig.look.Yaw, 80f);
            yield return Wait(0.4f);
            yield return Shot("02a4_body_look_down");
            Rig.look.SetAngles(Rig.look.Yaw, 0f);
            LookFrom(new Vector3(1.1f, 0f, 1.5f), d.coal.hook.transform.position + Vector3.up * 0.9f);
            yield return Wait(0.3f);
            yield return Shot("02a3_shovel_hook");
            d.coal.hook.Interact(Rig.interactor);
            LookFrom(new Vector3(0.3f, 0f, 1.2f), d.coal.bin.transform.position + Vector3.up * 0.5f);
            yield return Wait(0.5f);
            yield return Shot("02b_coal_bin");
            R.Input.SimUse = true;
            yield return Wait(0.9f);
            LookFrom(new Vector3(-3f, 0f, 0.9f), d.coal.stove.transform.position);
            yield return Wait(0.4f);
            yield return Shot("02c_stove_pour");
            R.Input.SimUse = false;
            d.coal.Fire.Set(FireLogic.MaxFuel, 1f);
            yield return Wait(0.5f);
            LookFrom(new Vector3(-2.6f, 0f, 1.4f), d.coal.stove.transform.position);
            yield return Wait(0.3f);
            yield return Shot("02c2_fire_lit");
            LookFrom(new Vector3(2.9f, 0f, -2.2f), new Vector3(-2f, 1f, 1.5f));
            yield return Wait(0.3f);
            yield return Shot("02d_galley_overview");
            // Bếp lò phụ ba nồi: cận mặt trước, và chếch từ phía đông dọc mặt bếp.
            LookFrom(new Vector3(-2.2f, 0f, -1.25f), new Vector3(-3.0f, 0.7f, -2.65f));
            yield return Wait(0.5f);
            yield return Shot("02d4_range_close");
            LookFrom(new Vector3(-0.9f, 0f, -1.7f), new Vector3(-3.3f, 0.85f, -2.65f));
            yield return Wait(0.3f);
            yield return Shot("02d5_range_oblique");
            // Bốn góc nghiệm thu bếp: ngang tầm mắt, nồi nhìn xéo, toàn khoang và từ cửa bếp.
            d.coal.hook.ReturnFrom(Rig.interactor);
            LookFrom(new Vector3(-3.25f, 0f, 0.5f), new Vector3(-3f, 1.25f, 2.05f));
            yield return Wait(0.4f);
            yield return Shot("02s1_stove_eye_level");
            LookFrom(new Vector3(-0.9f, 0f, -1.7f), new Vector3(-3.3f, 0.85f, -2.65f));
            yield return Wait(0.4f);
            yield return Shot("02s2_pots_oblique");
            LookFrom(new Vector3(2.3f, 0f, 1.0f), new Vector3(-3.1f, 1.1f, -0.15f));
            yield return Wait(0.4f);
            yield return Shot("02s3_galley_ranges");
            LookFrom(new Vector3(-4.8f, 0f, 2.58f), new Vector3(-2.6f, 0.9f, 2.4f));
            yield return Wait(0.4f);
            yield return Shot("02s4_stove_from_door");
            LookFrom(new Vector3(-3.7f, 0f, 0.6f), new Vector3(-5f, 1.55f, 0.6f));
            yield return Wait(0.5f);
            yield return Shot("02d2_porthole");
            R.UI.Pause.Show(true);
            yield return Wait(0.2f);
            yield return Shot("02d3_pause");
            R.UI.Pause.Show(false);
            d.coal.hook.ReturnFrom(Rig.interactor);
            yield return SimTo(d, "D1.breakfast");

            Assert.AreEqual("D1.breakfast", d.Beat);
            // M10b: kho lạnh cũ thay bằng hầm thực phẩm dưới boong giếng trước, xuống 14 bậc.
            LookFrom(Ship(new Vector3(11.6f, 2.8f, -0.4f)), d.sacks.sacks[0].transform.position);
            yield return Wait(1.5f);
            yield return Shot("02e_food_hold");
            Rig.interactor.Pick(d.sacks.sacks[0]);
            LookFrom(Ship(new Vector3(10.4f, 2.8f, 0f)), Ship(new Vector3(6.1f, 6.2f, 0f)));
            yield return Wait(1f);
            ShipWalkPlayTests.AssertShoulderView(Rig.cam, d.sacks.sacks[0]);
            // Thẻ tư liệu tạm che đúng phần vai phải: đóng thẻ trong ảnh soát tư thế, không đổi UI runtime.
            R.UI.Cards.Clear();
            yield return Shot("02f_carry_stairs");
            // Đặt bao lên bàn, cởi dây, dốc khoai sang thùng.
            var carried = d.sacks.sacks[0];
            yield return Until(() => carried.transform.localPosition.sqrMagnitude < 0.0025f, 10f);
            Rig.interactor.ReleaseHeld();
            d.sacks.zone.Place(carried);
            LookFrom(new Vector3(0.3f, 0f, -1.1f), d.sacks.mouth.transform.position);
            yield return Wait(0.3f);
            yield return Shot("02f2_sack_on_table");
            d.sacks.OnInteract(d.sacks.mouth, Rig.interactor);
            yield return Until(() => Rig.interactor.IsLookingAt(d.sacks.mouth), 5f);
            R.Input.SimUse = true;
            yield return Wait(0.15f);
            R.Input.SimLook = new Vector2(0f, -30f);
            yield return Until(() => d.sacks.InTub >= 4, 20f);
            yield return Shot("02f3_pouring");
            yield return Until(() => d.sacks.OnTable == null, 20f);
            R.Input.SimUse = false;
            R.Input.SimLook = Vector2.zero;
            LookFrom(new Vector3(0.9f, 0f, -1.2f), new Vector3(1.3f, 0.3f, 0f));
            yield return Until(() => d.sacks.Rolling == 0, 20f);
            yield return Wait(0.2f);
            yield return Shot("02f4_tub");
            // NPC (M10): quản bếp và phụ bếp đang làm.
            var cook = GameObject.Find("Npc_Cook").transform;
            // Đứng phía giữa bếp (bàn sơ chế) nhìn về quản bếp: quầy và lò đều sát tường.
            var toCenter = d.sacks.mouth.transform.position - cook.position;
            toCenter.y = 0f;
            LookFrom(cook.position + toCenter.normalized * 1.8f, cook.position + Vector3.up * 1.1f);
            yield return Wait(0.3f);
            yield return Shot("02f5_cook");
            var helper = GameObject.Find("Npc_Helper").transform;
            // Hướng NPC đổi theo việc: tại quầy nam, forward đẩy camera ra ngoài Wall_S (z < -3).
            // Lối giữa quầy và lò luôn ở trong phòng, không phụ thuộc pha animation/routine.
            // Dàn ảnh tĩnh: rung vị trí có thể đưa camera ra ngoài capsule rồi tia nhìn trúng chính Player.
            float helperShake = Rig.ShakeScale;
            Rig.ShakeScale = 0f;
            LookFrom(new Vector3(-2.2f, 0f, 0.8f), helper.position + Vector3.up * 1.1f);
            yield return Wait(0.3f);
            var helperTarget = helper.position + Vector3.up * 1.1f;
            if (Physics.Linecast(Rig.cam.transform.position, helperTarget, out var helperHit,
                    ~0, QueryTriggerInteraction.Ignore))
                Assert.IsTrue(helperHit.transform.IsChildOf(helper),
                    "Camera phụ bếp bị vật khác chắn: " + helperHit.collider.name);
            yield return Shot("02f6_helper");
            Rig.ShakeScale = helperShake;
            yield return SimTo(d, "D1.dishes");

            Assert.AreEqual("D1.dishes", d.Beat);
            LookFrom(new Vector3(5.7f, 0f, -1.0f), d.wash.pile.transform.position + Vector3.up * 0.3f);
            yield return Wait(0.5f);
            yield return Shot("02g_wash_pile");
            d.wash.OnInteract(d.wash.pile, Rig.interactor);
            LookFrom(new Vector3(5.75f, 0f, -1.6f), d.wash.basin.transform.position + Vector3.up * 0.1f);
            yield return Wait(0.3f);
            d.wash.OnInteract(d.wash.basin, Rig.interactor); d.wash.OpenTap(); // nước chảy rồi mới cọ (QA-0132)
            Assert.IsFalse(d.wash.CarryingPot, "Đặt nồi thật vào bể trước khi chà");
            R.Input.SimUse = true;
            R.Input.SimLook = new Vector2(30f, 18f);
            yield return Until(() => d.wash.Logic.Scrub.Progress >= 0.45f, 20f);
            yield return Shot("02g2_wash_scrub");
            R.Input.SimUse = false;
            R.Input.SimLook = Vector2.zero;
            yield return Wait(0.2f);

            Sim.Enabled = true;
            yield return Until(() => d.Beat == "D1.end", 120f);
            Sim.Enabled = false;
            yield return Until(() => d.Beat == "D1.rest", 30f);
            yield return Wait(0.5f);
            LookFrom(new Vector3(-3.2f, 0f, 0.2f), d.restSeat.transform.position + Vector3.up * 0.4f);
            yield return Wait(0.3f);
            yield return Shot("02k_evening_rest");
            LookFrom(new Vector3(-2.7f, 0f, 0.9f), new Vector3(-3.85f, 0.7f, 2f));
            yield return Wait(0.3f);
            yield return Shot("02k2_evening_jacques");
            d.restSeat.Interact(Rig.interactor);
            yield return Until(() => R.UI.Map.Visible, 30f);
            yield return Wait(8f);
            yield return Shot("02m_map_d2");
        }

        /// <summary>Các khu và việc thêm ở M9: cào lò, hầm than + máng tro, phòng rửa, chảo đồng, ô chuyển món, biển tên tàu,
        /// quét bếp, nhặt rau, giữ thăng bằng trên cầu thang.</summary>
        [UnityTest]
        public IEnumerator Shots02Rooms()
        {
            PlayTestUtil.Root().ResetProgress();
            ScreenshotCapture.RedirectUi();
            // Cảnh cũ (có thể cũng là 02 từ bài trước) còn sống tới khung sau: chờ director của cảnh mới.
            var before = SceneManager.GetActiveScene();
            SceneManager.LoadScene(SceneFlow.GameScenes[1]);
            var d = (ShiftDirector)null;
            yield return Until(() => (d = Object.FindAnyObjectByType<ShiftDirector>()) != null && d.gameObject.scene != before);
            yield return Until(() => d.Beat == "D1.sweep" && !R.UI.Card.Visible);
            yield return Wait(1f);

            LookFrom(new Vector3(-3.3f, 0f, 0.5f), new Vector3(-3.5f, 0.45f, 2.1f));
            yield return Wait(0.3f);
            yield return Shot("02r01_stove_ash");
            d.ash.hook.Interact(Rig.interactor);
            LookFrom(new Vector3(-3f, 0f, 0.75f), d.ash.door.transform.position);
            yield return Wait(0.3f);
            // Giữ chuột trước rồi mới kéo (như người chơi): camera khóa ngay khi bắt đầu cào.
            R.Input.SimUse = true;
            yield return Wait(0.15f);
            R.Input.SimLook = new Vector2(0f, -40f);
            yield return Until(() => d.ash.Rake.InBucket >= 2, 20f);
            yield return Shot("02r02_raking");
            R.Input.SimUse = false;
            R.Input.SimLook = Vector2.zero;
            d.ash.hook.ReturnFrom(Rig.interactor);
            // Tro nằm trong xô (không lơ lửng trên vành).
            var bucketPos = d.ash.bucket.transform.position;
            LookFrom(bucketPos + new Vector3(0.9f, 0f, -0.9f), bucketPos + Vector3.up * 0.2f);
            yield return Wait(0.3f);
            yield return Shot("02r02b_ash_bucket");
            // Nồi cao đứng thẳng, lớp bẩn trong miệng nồi; nhìn như người chơi đứng cạnh.
            var tallPot = GameObject.Find("PotBody").transform.position;
            LookFrom(tallPot + new Vector3(0.9f, 0f, 1.4f), tallPot + Vector3.up * 0.8f);
            yield return Wait(0.3f);
            yield return Shot("02r02c_tall_pot");
            // Xẻng xúc đầy than: than nằm trên lưỡi.
            d.coal.hook.Interact(Rig.interactor);
            Rig.hands.SetShovelLoaded(true);
            LookFrom(new Vector3(-2.6f, 0f, 0.9f), new Vector3(-2.6f, 0.2f, 2.5f));
            yield return Wait(0.5f);
            yield return Shot("02r02d_shovel_loaded");
            Rig.hands.SetShovelLoaded(false);
            d.coal.hook.ReturnFrom(Rig.interactor);
            // Ngồi nghỉ (C).
            LookFrom(new Vector3(-0.5f, 0f, -1.6f), new Vector3(-0.5f, 0.9f, 1.5f));
            R.Input.SimSit = true;
            yield return Until(() => Rig.motor.head.localPosition.y < 0.9f, 20f);
            Assert.IsTrue(Rig.GetComponent<SitController>().Seated, "C phải cho ngồi");
            yield return Shot("02r02e_sitting");
            R.Input.SimSit = true;
            yield return Until(() => Rig.motor.head.localPosition.y > 1.5f, 20f);
            Assert.IsFalse(Rig.GetComponent<SitController>().Seated);

            LookFrom(new Vector3(1.45f, 0f, -2.2f), new Vector3(1.5f, 0.7f, -5.2f));
            yield return Wait(0.3f);
            yield return Shot("02r03_coal_hold");
            LookFrom(new Vector3(1.7f, 0f, -3.7f), new Vector3(-0.1f, 0.9f, -4.6f));
            yield return Wait(0.3f);
            yield return Shot("02r04_ash_chute");
            LookFrom(new Vector3(5.3f, 0f, 0.3f), new Vector3(6.6f, 0.7f, -1.9f));
            yield return Wait(0.3f);
            yield return Shot("02r05_wash_room");
            LookFrom(new Vector3(3.6f, 0f, -0.2f), d.pan.pan.position + Vector3.up * 0.2f);
            yield return Wait(0.3f);
            yield return Shot("02r06_pan");
            LookFrom(new Vector3(3.7f, 0f, 1.3f), new Vector3(4.2f, 1.25f, 3.0f));
            yield return Wait(0.3f);
            yield return Shot("02r07_hatch");
            LookFrom(new Vector3(5.75f, 0f, 2.9f), new Vector3(6.49f, 1.45f, 2.9f));
            yield return Wait(0.3f);
            yield return Shot("02r08_nameplate");

            d.sweep.hook.Interact(Rig.interactor);
            var spot = d.sweep.spots[0].transform.position;
            LookFrom(spot + new Vector3(0f, 0f, 1.0f), spot);
            yield return Wait(0.3f);
            yield return Shot("02r09_sweep_spot");
            R.Input.SimUse = true;
            R.Input.SimLook = new Vector2(35f, 20f);
            yield return Until(() => d.sweep.Logic.Progress(0) >= 0.5f, 20f);
            yield return Shot("02r10_sweeping");
            R.Input.SimUse = false;
            R.Input.SimLook = Vector2.zero;
            d.sweep.hook.ReturnFrom(Rig.interactor);

            d.greens.OnOrder(2);
            d.greens.OnInteract(d.greens.crate, Rig.interactor);
            LookFrom(new Vector3(-0.5f, 0f, -0.95f), d.greens.board.transform.position);
            yield return Wait(0.4f);
            yield return Shot("02r11_greens");

            var sack = d.sacks.sacks[0];
            Rig.interactor.Pick(sack);
            // Giữa thang hầm, nhìn lên buồng thang.
            LookFrom(Ship(new Vector3(8.2f, 4.05f, 0f)), Ship(new Vector3(6.1f, 6.4f, 0f)));
            // Bao bay từ kho vào tay theo thời gian game: chờ tới nơi khi tàu còn êm, rồi mới cho tàu lắc.
            yield return Until(() => sack.transform.localPosition.sqrMagnitude < 0.0025f, 10f);
            d.ship.Intensity = 0.6f;
            yield return Until(() => Object.FindAnyObjectByType<StairsBalance>().Active, 5f);
            yield return Wait(0.3f);
            yield return Shot("02r12_stairs_balance");
            Rig.interactor.DropInFrontForTests();
            sack.GetComponent<ReturnToStart>()?.Restore();
        }

        // ---------- M11: sáu ngày trên tàu ----------

        static IEnumerator LoadDay(int day, System.Action<ShiftDirector> found)
        {
            var root = PlayTestUtil.Root();
            root.ResetProgress();
            ChapterStart.PrepareDay(root, day, day, false);
            ScreenshotCapture.RedirectUi();
            // Cảnh cũ (có thể cũng là 02 từ bài trước) còn sống tới khung sau: chờ director của cảnh mới.
            var before = SceneManager.GetActiveScene();
            SceneManager.LoadScene(SceneFlow.GameScenes[1]);
            ShiftDirector d = null;
            yield return Until(() => (d = Object.FindAnyObjectByType<ShiftDirector>()) != null && d.gameObject.scene != before);
            found(d);
        }

        /// <summary>Chạy Sim tới khi director tới beat rồi tắt Sim.</summary>
        static IEnumerator SimTo(ShiftDirector d, string beat, float seconds = 150f)
        {
            Sim.Enabled = true;
            yield return Until(() => d.Beat == beat, seconds);
            Sim.Enabled = false;
            Assert.AreEqual(beat, d.Beat);
        }

        /// <summary>Hết ca bằng Sim, rồi chờ sổ mở bài học của buổi tối (Sim tắt để thấy thoại và bài học như người chơi).</summary>
        static IEnumerator ToEveningStudy(ShiftDirector d, NotebookMode mode)
        {
            yield return SimTo(d, $"D{d.Day.Number}.end");
            if (d.Day.Evening.Coffee)
            {
                Sim.Enabled = true;
                yield return Until(() => R.Events.HasFired(EventIds.S2CoffeeDone), 30f);
                Sim.Enabled = false;
                Assert.IsTrue(R.Events.HasFired(EventIds.S2CoffeeDone), "Mang cà phê trước buổi học");
            }
            yield return Until(() => R.UI.Notebook.InStudy && R.UI.Notebook.Mode == mode, 90f);
            Assert.AreEqual(mode, R.UI.Notebook.Mode);
            yield return Wait(0.4f);
        }

        static IEnumerator FinishStudy()
        {
            var nb = R.UI.Notebook;
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            for (int guard = 0; guard < 600 && d.Beat != $"D{d.Day.Number}.rest"; guard++)
            {
                if (!nb.InStudy) { yield return Wait(0.1f); continue; }
                switch (nb.Mode)
                {
                    case NotebookMode.Trace: nb.SimulateTraceLetter(); break;
                    case NotebookMode.Fill: nb.SimulateFill(); break;
                    case NotebookMode.Sentence: nb.SimulateSentence(); break;
                    case NotebookMode.Match:
                        for (int i = 0; i < nb.Match.Count; i++)
                            nb.Match.TryMatch(i, i);
                        nb.FinishMatch();
                        break;
                }
                yield return Wait(0.1f);
            }
            Assert.AreEqual($"D{d.Day.Number}.rest", d.Beat, "Hoàn tất cả bài quốc ngữ và tiếng Pháp trước khi nghỉ");
        }

        /// <summary>Ngày 2 (8/6/1911, cảng Singapore, F02, F05): tàu đỗ, phông cảng từ boong, dây chắn cầu thang tàu;
        /// hai lính nhặt rau giúp; buổi tối tô chữ; bản đồ chặng sang Ngày 3.</summary>
        [UnityTest]
        public IEnumerator Shots02Day2()
        {
            ShiftDirector d = null;
            yield return LoadDay(2, x => d = x);
            yield return Wait(2f);
            yield return Shot("02n_card_day2");
            yield return Until(() => d.Beat == "D2.sweep" && !R.UI.Card.Visible);
            d.Clock.Minutes = 8f * 60f;
            yield return Wait(4f);
            var barrier = GameObject.Find("GangwayBarrier").transform.position;
            LookFrom(new Vector3(barrier.x + 2.4f, 0f, barrier.z - 1.4f), new Vector3(-10f, 4f, 58f));
            yield return Wait(0.5f);
            yield return Shot("02o1_singapore_from_deck");
            LookFrom(new Vector3(barrier.x - 0.6f, 0f, barrier.z - 1.6f), barrier + Vector3.up * 0.6f);
            yield return Wait(0.6f);
            yield return Shot("02o2_rope_barrier");

            yield return SimTo(d, "D2.greens");
            var cast = d.cast;
            var board = d.greens.board.transform.position;
            LookFrom(new Vector3(board.x + 0.3f, 0f, board.z + 1.9f), new Vector3(0.6f, 1.0f, -0.85f));
            yield return Wait(1.2f);
            yield return Shot("02o3_greens_soldiers");

            yield return ToEveningStudy(d, NotebookMode.Trace);
            yield return Shot("02p1_trace");
            R.UI.Notebook.SimulateTraceLetter();
            R.UI.Notebook.SimulateTraceLetter();
            yield return Wait(0.3f);
            yield return Shot("02p2_trace_progress");
            yield return FinishStudy();
            yield return Until(() => d.Beat == "D2.rest", 60f);
            LookFrom(new Vector3(-3.2f, 0f, 0.2f), cast.teachSeats[0].position + Vector3.up * 0.9f);
            yield return Wait(0.5f);
            yield return Shot("02p3_evening_soldiers");
            d.restSeat.Interact(Rig.interactor);
            yield return Until(() => R.UI.Map.Visible, 30f);
            yield return Wait(8f);
            yield return Shot("02m_map_d3");
        }

        /// <summary>Ngày 3: chảo đồng khi tàu lắc mạnh; buổi tối ghép từ (hai lính dạy).</summary>
        [UnityTest]
        public IEnumerator Shots02Day3()
        {
            ShiftDirector d = null;
            yield return LoadDay(3, x => d = x);
            yield return Until(() => d.Beat == "D3.sweep" && !R.UI.Card.Visible);
            yield return SimTo(d, "D3.pan");
            LookFrom(new Vector3(3.6f, 0f, -0.2f), d.pan.pan.position + Vector3.up * 0.2f);
            yield return Wait(0.5f);
            yield return Shot("02q0_pan_roll");
            yield return ToEveningStudy(d, NotebookMode.Match);
            yield return Shot("02q1_match_d3");
            yield return FinishStudy();
        }

        /// <summary>Ngày 4 (F05b): bão, Jacques giữ nồi; kéo rổ rau từ hầm lên boong giếng; sóng phủ boong cuốn Ba tới cột buồm;
        /// boong ngày bão với dây cứu sinh; buổi tối Jacques dạy ghép từ.</summary>
        [UnityTest]
        public IEnumerator Shots02Day4()
        {
            ShiftDirector d = null;
            yield return LoadDay(4, x => d = x);
            yield return Until(() => d.Beat == "D4.sweep" && !R.UI.Card.Visible);
            yield return SimTo(d, "D4.storm");
            yield return Until(() => d.pots.State == TaskState.Active);
            LookFrom(new Vector3(-0.6f, 0f, -0.6f), new Vector3(-2.2f, 1.0f, -2.4f));
            yield return Wait(3f);
            yield return Shot("02u1_storm_pots_jacques");
            d.pots.ForceComplete();

            yield return Until(() => d.Beat == "D4.haul", 30f);
            var haul = d.haul;
            yield return Until(() => haul.Pending > 0, 5f);
            LookFrom(new Vector3(-6.0f, 0f, 2.3f), new Vector3(-22.5f, 1.6f, -0.85f));
            yield return Wait(1f);
            yield return Shot("02u2_storm_deck_lifeline");
            var start = haul.BasketPosition;
            LookFrom(haul.Logic.PointAt(haul.Logic.Basket + 2.2f), start + Vector3.up * 0.3f);
            yield return Wait(0.5f);
            yield return Shot("02u3_haul_basket_hold");
            haul.OnInteract(haul.basketTarget, Rig.interactor);
            R.Input.SimUse = true;
            float stairs = haul.Logic.DistanceAt(haul.deckFrom) - 1.6f;
            yield return VoyagePlayTests.Follow(haul, Rig, () => haul.Logic.Basket >= stairs);
            var ahead = haul.Logic.PointAt(haul.Logic.Basket + 1.6f);
            LookFrom(ahead, haul.BasketPosition + Vector3.up * 0.2f);
            yield return Wait(0.2f);
            yield return Shot("02u4_haul_stairs");
            yield return VoyagePlayTests.Follow(haul, Rig, () => haul.Pending == 1);
            Assert.IsFalse(haul.WaveDone, "chuyến thứ nhất không có sóng");
            R.Input.SimUse = false;
            Rig.motor.Teleport(haul.Logic.PointAt(0.6f), 90f);
            haul.OnInteract(haul.basketTarget, Rig.interactor);
            R.Input.SimUse = true;
            yield return VoyagePlayTests.Follow(haul, Rig, () => haul.WaveDone);
            yield return Wait(0.12f);
            yield return Shot("02u5_wave_splash");
            yield return Wait(1.6f);
            yield return Shot("02u6_wave_mast");
            R.Input.SimUse = false;
            yield return Until(() => Rig.ControlEnabled, 10f);
            yield return FinishPhase(d);

            yield return Until(() => d.Beat == "D4.tallpot", 30f);
            var tall = d.cast.jacquesTallPot.position;
            LookFrom(new Vector3(tall.x + 1.3f, 0f, tall.z + 1.5f), tall + Vector3.up * 1.0f);
            yield return Wait(2f);
            yield return Shot("02u7_jacques_tallpot");
            yield return ToEveningStudy(d, NotebookMode.Match);
            yield return Shot("02u8_match_jacques");
            yield return FinishStudy();
        }

        /// <summary>Ngày 5: ngày dài nhất, đống nồi bẩn sau bữa trưa; buổi tối điền từ.</summary>
        [UnityTest]
        public IEnumerator Shots02Day5()
        {
            ShiftDirector d = null;
            yield return LoadDay(5, x => d = x);
            yield return Until(() => d.Beat == "D5.sweep" && !R.UI.Card.Visible);
            yield return SimTo(d, "D5.dishes_noon");
            yield return Wait(0.5f);
            LookFrom(new Vector3(5.7f, 0f, -0.6f), d.wash.pile.transform.position + Vector3.up * 0.4f);
            yield return Wait(0.5f);
            yield return Shot("02w1_pot_pile");
            yield return ToEveningStudy(d, NotebookMode.Fill);
            yield return Shot("02w2_fill");
            R.UI.Notebook.SimulateFill();
            yield return Wait(0.3f);
            yield return Shot("02w3_fill_next");
            yield return FinishStudy();
        }

        /// <summary>Ngày 6: biển êm; buổi tối viết câu, hai lính cho mượn sách (F07a); bản đồ chặng tới Marseille.</summary>
        [UnityTest]
        public IEnumerator Shots02Day6()
        {
            ShiftDirector d = null;
            yield return LoadDay(6, x => d = x);
            yield return Until(() => d.Beat == "D6.sweep" && !R.UI.Card.Visible);
            yield return ToEveningStudy(d, NotebookMode.Sentence);
            yield return Shot("02x1_sentence");
            R.UI.Notebook.SimulateSentence();
            yield return Wait(0.3f);
            yield return Shot("02x2_sentence_next");
            yield return FinishStudy();
            yield return Until(() => R.Events.HasFired(EventIds.S2BookLent), 60f);
            yield return Wait(1f);
            yield return Shot("02x3_book_lent");
            yield return Until(() => d.Beat == "D6.rest", 60f);
            d.restSeat.Interact(Rig.interactor);
            yield return Until(() => R.UI.Map.Visible, 30f);
            yield return Wait(10f);
            yield return Shot("02m_map_B");
        }

        [UnityTest]
        public IEnumerator Shots03Marseille()
        {
            PlayTestUtil.Root().ResetProgress();
            ScreenshotCapture.RedirectUi();
            Time.captureDeltaTime = 1f / 60f;
            SceneManager.LoadScene(SceneFlow.GameScenes[2]);
            yield return Wait(2f);
            yield return Shot("03a_dawn_fog");
            var d = Object.FindAnyObjectByType<EndingDirector>();
            LookFrom(new Vector3(3f, 0f, -2f), new Vector3(-2f, 43f, -150f));
            yield return Wait(20f);
            yield return Shot("03b_basilica_port");
            Sim.Enabled = true;
            yield return Until(() => d.Beat == "E1b", 60f);
            Sim.Enabled = false;
            var spot = d.sweep.spots[0].transform.position;
            LookFrom(spot + Vector3.forward, spot);
            yield return Wait(0.3f);
            yield return Shot("03b2_final_sweep");
            d.sweep.SimulateWork();
            yield return Until(() => d.wash.State == TaskState.Active, 30f);
            d.wash.OnInteract(d.wash.pile, Rig.interactor);
            var basin = d.wash.basin.transform;
            LookFrom(basin.position - basin.forward * 1.1f, basin.position + Vector3.up * 0.85f);
            yield return Wait(0.3f);
            yield return Shot("03b3_final_wash");
            d.wash.SimulateWork();
            yield return Until(() => d.Beat == "E2a", 30f);
            LookFrom(new Vector3(-3.3f, 0f, -2.7f), new Vector3(-3.5f, 0.8f, -0.8f));
            yield return Wait(0.6f);
            yield return Shot("03c1_pay_queue");
            LookFrom(new Vector3(-4.1f, 0f, -2.3f), new Vector3(-4.1f, 0.8f, -1.1f));
            yield return Wait(0.4f);
            yield return Shot("03c2_salary_ledger");
            LookFrom(d.payPoint.position, d.purser.agent.Position + Vector3.up);
            yield return Until(() => d.Beat == "E2b", 60f);
            R.Input.SimRaise = true;
            yield return Until(() => R.Events.HasFired(EventIds.S3PayReceived), 40f);
            Assert.AreEqual(10, d.coins.Dropped);
            yield return Shot("03c_pay_coins");
            R.Input.SimRaise = false;
            yield return Until(() => d.Beat == "E2c", 30f);
            yield return Until(() => !R.Subtitles.IsPlaying, 30f);
            // Chào năm người ở lại trước; ông Mai không chào từ biệt (đi cùng Ba lên phố), nói chuyện với ông sau cùng.
            foreach (int i in new[] { 0, 1, 2, 4, 5, EndingDirector.MaiIndex })
            {
                bool mai = i == EndingDirector.MaiIndex;
                var side = i < 2 ? Vector3.forward : i < 4 ? Vector3.right : Vector3.back;
                LookFrom(d.crew[i].Position + side * 1.7f, d.crew[i].Position + Vector3.up * 1.1f);
                int done = d.Farewells + 1;
                d.farewellTargets[i].Interact(Rig.interactor);
                if (!mai)
                {
                    yield return Until(() => d.crew[i].State == "Wave" && R.Subtitles.IsPlaying, 15f);
                    Assert.AreEqual("Wave", d.crew[i].State);
                    yield return Wait(0.3f);
                    yield return Shot("03d_farewell_" + i);
                }
                yield return Until(() => d.Farewells >= done, mai ? 150f : 30f);
            }
            yield return Until(() => d.Beat == "E3" && !d.gangwayBarrier.activeSelf, 30f);
            LookFrom(new Vector3(0f, 0f, -2.6f), new Vector3(0f, -2.5f, -12f));
            yield return Wait(0.5f);
            yield return Shot("03d_gangway");
            LookFrom(d.quayPoint.position + Vector3.back * 0.3f, new Vector3(-6f, -1f, -13f));
            yield return Until(() => d.Beat == "E3b", 30f);
            yield return Wait(0.5f);
            yield return Shot("03e_quay_crowd");
            LookFrom(new Vector3(-2.2f, -2.5f, -12.6f), new Vector3(-8f, -1.1f, -13.4f));
            yield return Until(() => R.Events.HasFired(EventIds.S3QuayObserved), 30f);
            LookFrom(new Vector3(-5.5f, -2.5f, -12.1f), new Vector3(-8f, -1.1f, -13.4f));
            yield return Wait(0.2f);
            yield return Shot("03e2_poor_workers");
            LookFrom(new Vector3(6f, -2.5f, -14f), new Vector3(-12f, 4f, 2f));
            yield return Wait(0.5f);
            yield return Shot("03o_ship_from_quay");
            LookFrom(new Vector3(0f, -2.5f, -15.2f), new Vector3(-6f, -1.2f, -18.5f));
            yield return Until(() => d.tram.Running && d.tram.tram.position.x > -10f, 30f);
            yield return Shot("03f_tram");
            Sim.Enabled = true; // Nhịp quan sát mới có ảnh riêng trong StoryShots; test này tiếp tục kiểm quán cũ.
            yield return Until(() => d.Beat == "E5a", 60f);
            Sim.Enabled = false;
            LookFrom(new Vector3(0.5f, -2.4f, -22.25f), d.chair.transform.position + Vector3.up * 0.5f);
            yield return Wait(0.3f);
            yield return Shot("03g_cafe_chair");
            d.chair.Interact(Rig.interactor);
            yield return Until(() => d.waiter.agent.Walking, 30f);
            yield return Wait(0.8f);
            Assert.IsFalse(d.waiter.CupPlaced);
            yield return Shot("03g1_waiter_carry");
            yield return Until(() => R.Events.HasFired(EventIds.S3CafeServed), 30f);
            Assert.Less(Vector3.Distance(d.cup.position, d.waiterTablePoint.position), 0.002f);
            yield return Shot("03g2_waiter_cup");
            yield return Until(() => R.UI.Notebook.IsOpen, 30f);
            yield return Wait(0.3f);
            yield return Shot("03h_link_pages");
            var t = R.Content.Notebook.LinkTarget;
            R.UI.Notebook.SimulateLink(t.A, t.B);
            yield return Wait(0.5f);
            yield return Shot("03i_linked");
            yield return Until(() => d.Beat == "E6", 20f);
            yield return Until(() => R.UI.Card.CurrentText == R.Content.Ending.Narrator, 60f);
            yield return Wait(1.5f);
            yield return Shot("03j_narrator");
            yield return Until(() => R.UI.Card.CurrentDate == R.Content.Ending.EndCards[0].Date, 20f);
            yield return Wait(1.5f);
            yield return Shot("03k_endcard");
            yield return Until(() => !string.IsNullOrEmpty(R.UI.Card.CurrentTitle), 30f);
            yield return Wait(1.5f);
            yield return Shot("03l_title");
            yield return Until(() => R.UI.Card.CurrentText == R.Content.GetString("credits.sources"), 20f);
            yield return Wait(1.5f);
            yield return Shot("03m_credits");
            yield return Until(() => R.Events.HasFired(EventIds.S3End), 30f);
            yield return Wait(0.5f);
            yield return Shot("03n_end");
        }
    }

    /// <summary>Chỉ đối chứng thời điểm LateUpdate của code trước khi gộp; không chạy trong game.</summary>
    public sealed class LegacyCarryLateUpdateProbe : MonoBehaviour
    {
        public float FollowSpeed;

        void LateUpdate()
        {
            float k = 1f - Mathf.Exp(-FollowSpeed * Time.deltaTime);
            transform.localPosition = Vector3.Lerp(transform.localPosition, Vector3.zero, k);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, Quaternion.identity, k);
        }
    }
}
