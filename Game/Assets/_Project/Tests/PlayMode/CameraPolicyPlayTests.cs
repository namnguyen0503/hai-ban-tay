using System.Collections;
using System.Linq;
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
    /// Luật camera 17/10 (QA-0062 còn trên bản người dùng chơi: "Không làm lùi camera nữa. Luôn giữ nguyên góc nhìn default"): khi làm
    /// việc, game không được tự dời hay xoay camera. Mỗi việc đo từng khung:
    /// <list type="bullet">
    /// <item>mắt ở đúng chỗ mặc định trên người (chỉ có bob; không đẩy tới, không hạ đầu);</item>
    /// <item>hướng nhìn không đổi khi người chơi không xoay (chuột đang dùng để cọ/dốc thì camera khóa);</item>
    /// <item>người không dịch chuyển tức thời (bước lại gần ở tốc độ đi bộ thì được);</item>
    /// <item>xương đầu nằm sau mắt, vòng cổ áo ngoài khung 16:9 và 2760:1256;</item>
    /// <item>tay với tới từ tư thế đứng: cổ tay xương không hở khỏi bàn tay.</item>
    /// </list>
    /// </summary>
    public class CameraPolicyPlayTests
    {
        const float WristGapLimit = 0.035f;
        // Bob ngang tối đa nửa biên độ (FirstPersonMotor.bobAmplitude × 1,6 khi mang) và dọc tối đa một biên độ.
        const float EyeSlack = 0.05f;
        // Đi bộ 1,3 m/s qua SmoothStep (đỉnh 1,5×) ở 60 Hz: dưới 4 cm mỗi khung; dịch chuyển tức thời thì lớn hơn nhiều.
        const float StepPerFrame = 0.06f;

        static GameRoot Root => GameRoot.Instance;
        static PlayerRig Rig => PlayerRig.Current;

        [SetUp] public void SetUp() => Time.captureDeltaTime = 1f / 60f;

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            if (Root != null) { Root.Input.SimUse = false; Root.Input.SimLook = Vector2.zero; }
            foreach (var probe in Object.FindObjectsByType<PlayerClipProbe>(FindObjectsSortMode.None)) Object.Destroy(probe.gameObject);
            PlayTestUtil.ExitSim();
        }

        /// <summary>Ghi các vi phạm của một lượt việc, khung nào tệ nhất.</summary>
        sealed class CameraWatch
        {
            readonly PlayerRig _rig;
            readonly PlayerViewProbe _probe;
            readonly Transform _headBone;
            readonly string _label;
            float _yaw, _pitch;
            Vector3 _lastRig, _lastEye;
            bool _started;
            int _frames, _settle;
            public float EyeForward, EyeDrop, Turn, Jump, HeadAhead, WristGap, Into, Reversed;
            public string ReversedAt = "";
            public int Neck;
            public string EyeAt = "", TurnAt = "", JumpAt = "", HeadAt = "", NeckAt = "", WristAt = "", IntoAt = "";
            /// <summary>Đồ đang làm (bao khoai, nồi) không do Interactor cầm: đo thêm độ lún vào thân/đùi/tạp dề.</summary>
            public Transform Item;
            /// <summary>Tay, thân, dụng cụ, đồ bê lún vào vật của cảnh (chậu, nồi, bàn…), mỗi khung.</summary>
            readonly PlayerClipProbe _clip;
            string _phase = "";

            readonly bool _seated;

            public CameraWatch(PlayerRig rig, string label, bool seated = false)
            {
                _rig = rig;
                _seated = seated;
                _label = label;
                _probe = new PlayerViewProbe(rig);
                _headBone = rig.body.animator.GetBoneTransform(HumanBodyBones.Head);
                _clip = PlayerClipProbe.Attach(shots: true);
                _clip.Label = () => _phase;
                Rebase();
            }

            /// <summary>Hướng nhìn hiện tại thành mốc (sau khi test tự xoay camera, ví dụ nhắm vào chậu).</summary>
            public void Rebase()
            {
                _yaw = _rig.look.Yaw;
                _pitch = _rig.look.Pitch;
                _started = false;
            }

            public void Sample(string phase, bool checkWrists = true)
            {
                _frames++;
                string at = $"{phase} (khung {_frames})";
                _phase = at;
                var eye = _rig.head.position;
                var local = _rig.transform.InverseTransformPoint(eye);
                // Ngồi ghế thật: mắt ở độ cao ghế đặt (SitController); vẫn không được dời ra trước hay hạ thêm.
                float baseHeight = _rig.motor.headHeight + (_seated ? _rig.motor.ExtraHeadHeight : Mathf.Max(0f, _rig.motor.ExtraHeadHeight));
                float forward = Mathf.Abs(local.z);
                if (forward > EyeForward) { EyeForward = forward; EyeAt = at; }
                float drop = baseHeight - local.y;
                if (drop > EyeDrop) { EyeDrop = drop; EyeAt = at; }
                if (!_seated && _rig.motor.ExtraHeadHeight < -0.001f && drop < 0.2f) { EyeDrop = Mathf.Max(EyeDrop, -_rig.motor.ExtraHeadHeight); EyeAt = at + " ExtraHeadHeight âm"; }
                float turn = Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(_yaw, _rig.look.Yaw)), Mathf.Abs(_rig.look.Pitch - _pitch));
                if (turn > Turn) { Turn = turn; TurnAt = at; }
                if (_started)
                {
                    float jump = (_rig.transform.position - _lastRig).magnitude;
                    float eyeJump = ((eye - _lastEye) - (_rig.transform.position - _lastRig)).magnitude;
                    jump = Mathf.Max(jump, eyeJump);
                    if (jump > Jump) { Jump = jump; JumpAt = at; }
                }
                _lastRig = _rig.transform.position;
                _lastEye = eye;
                _started = true;
                float ahead = Vector3.Dot(_headBone.position - eye, _rig.transform.forward);
                if (ahead > HeadAhead) { HeadAhead = ahead; HeadAt = at; }
                int neck = _probe.NeckInFrame();
                if (neck > Neck) { Neck = neck; NeckAt = at + $" cúi {_rig.look.Pitch:F0}°"; }
                // Đang bước lại gần, rồi 0,2 s tay trượt tới điểm cầm: chưa tính hở cổ tay.
                _settle = _rig.Stepping ? 12 : Mathf.Max(0, _settle - 1);
                float reversed = PlayerClipDetector.Reversal(_rig.body, out string arm);
                if (_settle == 0 && reversed > Reversed) { Reversed = reversed; ReversedAt = $"{at}: {arm}"; }
                string where = "";
                float into = Item != null ? _probe.ItemInBody(Item, out where) : _probe.ItemInBody(out where);
                if (into > Into) { Into = into; IntoAt = $"{at}: {where}"; }
                if (checkWrists && _settle == 0 && _rig.ArmsShown)
                {
                    float gap = Mathf.Max(_probe.WristGap(true), _probe.WristGap(false));
                    if (gap > WristGap) { WristGap = gap; WristAt = at + " " + Reach(); }
                }
            }

            /// <summary>Số đo tầm với lúc tệ nhất (hệ người: x phải, y từ chân, z trước): vai, điểm cầm, khoảng cách, độ dài tay.</summary>
            string Reach()
            {
                var root = _rig.transform;
                string Side(bool left)
                {
                    var grip = _rig.hands.GripAnchor(left);
                    var shoulder = root.InverseTransformPoint(_rig.body.Shoulder(left).position);
                    if (grip == null) return $"{(left ? "T" : "P")}: vai {shoulder:F2}, không có điểm cầm";
                    var g = root.InverseTransformPoint(grip.position);
                    return $"{(left ? "T" : "P")}: vai {shoulder:F2} → cầm {g:F2}, cách {Vector3.Distance(shoulder, g):F2} m / tay {_rig.body.ArmLength(left):F2} m";
                }
                return $"[{Side(true)}; {Side(false)}]";
            }

            public void AssertClean()
            {
                Debug.Log($"CAMERA_POLICY {_label}: mắt tới {EyeForward * 100f:F1} cm, hạ {EyeDrop * 100f:F1} cm ({EyeAt}); xoay {Turn:F1}° ({TurnAt}); " +
                    $"nhảy {Jump * 100f:F1} cm/khung ({JumpAt}); đầu trước mắt {HeadAhead * 100f:F1} cm ({HeadAt}); cổ áo {Neck} điểm ({NeckAt}); " +
                    $"cổ tay hở {WristGap * 100f:F1} cm ({WristAt}); lún {Into * 100f:F1} cm ({IntoAt}); thân lùi {_rig.body.BodyBack * 100f:F1} cm");
                Assert.LessOrEqual(EyeForward, EyeSlack, $"{_label}: mắt dời ra trước/sau {EyeForward * 100f:F1} cm ({EyeAt})");
                Assert.LessOrEqual(EyeDrop, EyeSlack, $"{_label}: mắt hạ {EyeDrop * 100f:F1} cm so với đứng thẳng ({EyeAt})");
                Assert.LessOrEqual(Turn, 0.5f, $"{_label}: game tự xoay camera {Turn:F1}° ({TurnAt})");
                Assert.LessOrEqual(Jump, StepPerFrame, $"{_label}: người/camera nhảy {Jump * 100f:F1} cm trong một khung ({JumpAt})");
                Assert.LessOrEqual(HeadAhead, 0f, $"{_label}: xương đầu ở trước mắt {HeadAhead * 100f:F1} cm ({HeadAt})");
                Assert.AreEqual(0, Neck, $"{_label}: vòng cổ áo lọt vào khung ({NeckAt})");
                Assert.LessOrEqual(WristGap, WristGapLimit, $"{_label}: tay không với tới từ tư thế đứng, cổ tay hở {WristGap * 100f:F1} cm ({WristAt})");
                Assert.LessOrEqual(Into, PlayerViewProbe.Limit(PlayerViewProbe.Kind.ItemInBody), $"{_label}: đồ lún vào người {Into * 100f:F1} cm ({IntoAt})");
                Assert.AreEqual(0f, Reversed, $"{_label}: cẳng tay vặn ngược quá ngưỡng {PlayerClipDetector.ReversedTwist:F0}° thêm {Reversed:F0}° ({ReversedAt})");
                string report = _clip.WriteReport("camera_policy_" + _label);
                string clips = _clip.Track.Summary();
                Debug.Log($"PLAYER_CLIP {_label}: {report}\n{clips}");
                if (_rig.ArmsShown)
                    Assert.IsTrue(_clip.Detector.ArmsMeasured, $"{_label}: không dựng được vỏ tay Ba (mesh thân không đọc được)");
                var majors = _clip.Track.Majors.ToList();
                Object.Destroy(_clip.gameObject);
                Assert.IsEmpty(majors.Select(f => f.Line), $"{_label}: tay/thân/đồ của Ba xuyên vật\n{clips}");
            }
        }

        static ShiftDirector Quiet()
        {
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            d.meals.cook.enabled = false;
            // Dụng cụ NPC đang cầm (xẻng phụ bếp) không là con của NPC: tắt NPC thì dụng cụ treo lơ lửng chỗ tay cũ, cất luôn.
            foreach (var follow in Object.FindObjectsByType<HaiBanTay.Npc.NpcToolFollow>(FindObjectsSortMode.None))
                if (follow.tool != null) follow.tool.gameObject.SetActive(false);
            foreach (var npc in Object.FindObjectsByType<HaiBanTay.Npc.NpcAgent>(FindObjectsSortMode.None)) npc.gameObject.SetActive(false);
            return d;
        }

        static IEnumerator Watch(CameraWatch watch, string phase, float seconds, System.Func<Vector2> look = null, bool use = true,
            bool checkWrists = true, System.Func<bool> until = null)
        {
            Root.Input.SimUse = use;
            for (float t = 0f; t < seconds && (until == null || !until()); t += Time.deltaTime)
            {
                // Chỉ rê chuột khi trạm đã khóa camera (chuột dùng để cọ/dốc): trước đó rê là người chơi tự xoay, không phải game.
                Root.Input.SimLook = look != null && Rig.look.Locked ? look() : Vector2.zero;
                yield return null;
                // Trạm vừa nhả khóa trong khung này (đổ xong): lệnh rê của test đã tới camera như tay người chơi, không phải game xoay.
                if (Root.Input.SimLook != Vector2.zero && !Rig.look.Locked) watch.Rebase();
                watch.Sample(phase, checkWrists);
            }
            Root.Input.SimUse = false;
            Root.Input.SimLook = Vector2.zero;
        }

        static void Hold(Holdable h)
        {
            var who = Rig.interactor;
            if (who.Holding != null) who.ReleaseHeld();
            h.gameObject.SetActive(true);
            h.Locked = false;
            who.Pick(h);
            Assert.AreEqual(h, who.Holding, h.name);
        }

        static Vector2 ScrubStroke() => new Vector2(Time.frameCount % 24 < 12 ? 15f : -15f, 8f);

        [UnityTest, Timeout(180000)]
        public IEnumerator WashingAtTheBasinKeepsTheDefaultView()
        {
            yield return ChoreTestUtil.Galley();
            var d = Quiet();
            d.meals.BeginMeal(new HaiBanTay.Content.PhaseData { Id = "kiểm_camera_rửa", Dirty = 1 });
            yield return d.meals.Serve();
            var wash = d.wash;
            wash.brushHook.Interact(Rig.interactor); // cọ bằng bàn chải lấy từ giá cạnh chậu (brush17)
            wash.OnInteract(wash.pile, Rig.interactor);
            wash.OnInteract(wash.basin, Rig.interactor); wash.OpenTap(); // nước chảy rồi mới cọ (QA-0132)
            Assert.IsTrue(wash.Logic.InBasin, "nồi nằm trong chậu");
            // Đứng lùi khỏi chỗ cọ 0,35 m: bắt đầu cọ thì Ba bước tới, không bị kéo tức thời.
            var basin = wash.basin.transform;
            ChoreTestUtil.Look(basin.TransformPoint(new Vector3(0f, 0f, WashStation.StandBack - 0.35f)), basin.position + Vector3.up * 0.68f);
            yield return new WaitForSeconds(0.4f);
            var watch = new CameraWatch(Rig, "cọ chậu");
            yield return Watch(watch, "trước khi cọ", 0.3f, use: false);
            yield return Watch(watch, "cọ", 3f, ScrubStroke);
            Assert.IsTrue(Rig.hands.Working || wash.Logic.Scrub.Done, "đang cọ");
            yield return Watch(watch, "nghỉ cọ", 1f, use: false, checkWrists: false);
            watch.AssertClean();
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator TippingThePotatoSackKeepsTheDefaultView()
        {
            yield return ChoreTestUtil.Galley();
            var d = Quiet();
            var sacks = d.sacks;
            ChoreTestUtil.Look(new Vector3(1.32f, 0f, -1.35f), sacks.mouth.transform.position);
            sacks.sacks[0].gameObject.SetActive(true);
            sacks.zone.Place(sacks.sacks[0]);
            yield return new WaitForSeconds(0.3f);
            var watch = new CameraWatch(Rig, "dốc bao khoai") { Item = sacks.sacks[0].transform };
            sacks.OnInteract(sacks.mouth, Rig.interactor);
            yield return Watch(watch, "cởi bao, bước tới", 0.6f, use: false);
            yield return Watch(watch, "dốc", 6f, () => new Vector2(0f, -12f), until: () => sacks.Pour.Empty);
            watch.AssertClean();
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator PeelingPotatoesKeepsTheDefaultView()
        {
            yield return ChoreTestUtil.Galley();
            var task = Object.FindAnyObjectByType<ShiftDirector>().storyStations.OfType<PeelStation>().First(x => !x.asparagus);
            task.OnOrder(1);
            var back = task.stand + (task.stand - task.board.transform.position).WithY(0f).normalized * 0.6f;
            ChoreTestUtil.Look(back, task.board.transform.position);
            yield return new WaitForSeconds(0.3f);
            var watch = new CameraWatch(Rig, "gọt khoai");
            task.OnInteract(task.board, Rig.interactor);
            // Ông Mai gọt mẫu: mắt Ba không bị kéo đi đâu cả.
            yield return Watch(watch, "xem gọt mẫu", 90f, use: false, checkWrists: false, until: () => task.Ready);
            Assert.IsTrue(task.Ready);
            yield return Watch(watch, "gọt", 4f, () => new Vector2(0f, Time.frameCount % 16 < 8 ? 12f : -12f));
            Assert.IsTrue(task.Working || task.Pending == 0, "đang gọt");
            watch.AssertClean();
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator PouringTheCoalBasketKeepsTheDefaultView()
        {
            yield return ChoreTestUtil.Galley();
            var d = Quiet();
            var coal = d.coal;
            var basket = coal.baskets[0];
            Hold(basket);
            coal.SetStock(0);
            ChoreTestUtil.Look(new Vector3(0.3f, 0f, 1.45f), coal.bin.transform.position + Vector3.up * 0.5f);
            yield return new WaitForSeconds(0.4f);
            var watch = new CameraWatch(Rig, "đổ sọt than");
            coal.OnInteract(coal.bin, Rig.interactor);
            bool began = false;
            yield return Watch(watch, "đổ", 8f, use: false, until: () => (began |= coal.PouringBasket) && !coal.PouringBasket);
            Assert.IsTrue(began && coal.IsEmptyBasket(basket), "đổ hết sọt");
            watch.AssertClean();
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator PouringTheAshBucketKeepsTheDefaultView()
        {
            yield return ChoreTestUtil.Galley();
            var d = Quiet();
            var ash = d.ash;
            ash.bucket.gameObject.SetActive(true);
            ash.Rake.ForceFull();
            Hold(ash.bucket);
            var chute = ash.chute.transform;
            var mouth = chute.TransformPoint(new Vector3(0.2f, 0.98f, 0f));
            var stand = mouth + new Vector3(1.0f, 0f, 0.15f);
            stand.y = chute.position.y;
            ChoreTestUtil.Look(stand, mouth);
            yield return new WaitForSeconds(0.4f);
            var watch = new CameraWatch(Rig, "đổ xô tro");
            ash.OnInteract(ash.chute, Rig.interactor);
            bool began = false;
            yield return Watch(watch, "đổ", 8f, use: false, until: () => (began |= ash.bucket.Busy) && !ash.bucket.Busy);
            Assert.IsTrue(began, "đổ xô");
            watch.AssertClean();
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator EatingSoupStandingKeepsTheDefaultView()
        {
            yield return ChoreTestUtil.Galley();
            // Giữ NPC: ông Mai là người nói trong lượt súp.
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            d.people.crewMeal.SetActive(true);
            var soup = d.storyStations.OfType<VoyageEncounter>().First(x => x.Kind == OrderKinds.Soup);
            soup.OnOrder(1);
            var back = soup.stand + (soup.stand - soup.target.transform.position).WithY(0f).normalized * 0.5f;
            ChoreTestUtil.Look(back, soup.target.transform.position);
            yield return new WaitForSeconds(0.4f);
            // Ngồi xuống ghế thật đối diện ông Mai (được đổi chiều cao mắt và quay mặt vào bàn); từ lúc ngồi xong, ăn không được dời camera.
            soup.OnInteract(soup.target, Rig.interactor);
            var sit = Rig.GetComponent<SitController>();
            yield return PlayTestUtil.WaitUntil(() => sit.StudySeated, 5f);
            Assert.IsTrue(sit.StudySeated, "ngồi ghế ăn súp");
            yield return new WaitForSeconds(0.6f);
            var watch = new CameraWatch(Rig, "ăn súp", seated: true);
            // Thoại phát theo giờ thật, test chạy 1/60 s mỗi khung: chờ đủ khung cho ba câu mở đầu và lượt nĩa.
            yield return Watch(watch, "cầm nĩa", 600f, use: false, checkWrists: false, until: () => soup.Stage >= 1 && !soup.Busy);
            Assert.AreEqual(1, soup.Stage, $"ăn xong lượt nĩa (bận {soup.Busy})");
            watch.AssertClean();
        }
    }

    static class CameraPolicyVectorExtensions
    {
        public static Vector3 WithY(this Vector3 v, float y) => new Vector3(v.x, y, v.z);
    }
}
