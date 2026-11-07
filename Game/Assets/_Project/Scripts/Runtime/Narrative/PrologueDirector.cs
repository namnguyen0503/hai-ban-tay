using System.Collections;
using HaiBanTay.Audio;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using UnityEngine;

namespace HaiBanTay.Narrative
{
    /// <summary>Cảnh 00_Prologue — Sài Gòn 1911 (Docs/SCRIPT.md, mục Prologue): P1 thẻ mở đầu và đoạn phố tự đi, P2 hỏi đáp theo
    /// S16, P3 giơ hai bàn tay, P4 anh Lê đồng ý rồi tới Bến Nhà Rồng. Điểm neo (đường đi của anh Lê, chỗ nói chuyện) do builder gán.</summary>
    public sealed class PrologueDirector : SceneDirector
    {
        public AmbienceZone ambience;
        [Tooltip("Anh Lê: đi kèm người chơi rồi dừng ở điểm hội thoại.")]
        public NpcAgent le;
        public NpcAgent iceSeller;
        [Tooltip("Đường anh Lê đi từ chỗ xuất phát tới điểm hội thoại (điểm cuối là chỗ đứng nói chuyện).")]
        public Transform[] lePath;
        [Tooltip("Chỗ anh Lê đứng nói chuyện; hướng quay (yaw) của Transform là hướng nhìn.")]
        public Transform talkLe;
        [Tooltip("Chỗ người chơi đứng khi nói chuyện (Sim dịch chuyển tới đây); hướng quay là hướng nhìn.")]
        public Transform talkPlayer;
        [Tooltip("Vùng quanh điểm hội thoại: người chơi bước vào thì anh Lê bắt chuyện.")]
        public PlayerArea talkZone;
        public InteractTarget[] discoveries;
        public Transform[] discoveryLePoints;
        public InteractTarget iceTarget;
        public Transform iceCup, leIceCup;
        /// <summary>Tổng thời gian giữ chuột để ăn hết cốc (chia đều cho các thìa).</summary>
        // 2 s mỗi thìa: 0,83 s/thìa trông như ăn vội (người dùng 06/11: "ăn chậm thôi").
        public const float IceEatSeconds = 6f;
        public float IceEaten { get; private set; }
        public int DiscoveriesSeen { get; private set; }
        public float placeCardSeconds = 4f;
        public float followHintSeconds = 20f;

        /// <summary>Anh Lê đã tới điểm hội thoại.</summary>
        public bool LeArrived { get; private set; }

        IEnumerator Start()
        {
            TalkGaze.Bind("le", le);
            Root.ResetProgress();
            Rig.SetOutfit(TrangPhuc.Thuong);
            SceneFlow.FromPrologue = false;
            // Piano chủ đề mở đầu ở mức nhỏ; SubtitlePlayer dùng ducking sẵn có khi có thoại.
            Root.Audio.PlayMusic("Music/menu", loop: true, volume: 0.2f);
            Root.UI.Notebook.AllowToggle = true;

            // P1 Thẻ mở đầu rồi tự đi một đoạn phố, anh Lê đi kèm.
            Beat = "P1";
            BeginCutscene();
            Rig.SetControl(false);
            Root.UI.Card.Blackout();
            yield return Root.UI.Card.Show(S("card.prologue.place"), placeCardSeconds);
            Root.UI.Card.Hide();
            EndCutscene();
            Rig.SetControl(true);
            Guide("guide.pro_follow", () => le.transform);
            yield return LeWalks();
            yield return WaitPlayerNearLe();
            ClearGuide();
            // Còi tàu xa từ khoảng sông cuối phố (DIRECTOR §9), nhỏ, trước khi Ba nhìn phố.
            Root.Audio.PlaySfx("SFX/ship_horn", 0.22f);

            yield return StreetDiscoveries();
            Beat = "P1c";
            BeginCutscene();
            Rig.SetControl(false);
            yield return Root.Scenes.Fade(0f, 1f, 0.6f);
            Root.UI.Card.Blackout();
            Root.UI.SetFade(0f);
            yield return Root.UI.Card.Show(S("card.prologue.later"), 4f);
            le.TeleportTo(talkLe.position, talkLe.eulerAngles.y);
            Rig.motor.Teleport(talkPlayer.position, talkPlayer.eulerAngles.y);
            Root.UI.Card.Hide();

            // P2 Hỏi đáp theo nguyên văn S16 (F12). Hai người quay mặt vào nhau.
            Beat = "P2";
            BeginCutscene();
            Rig.SetControl(false);
            var toLe = talkLe.position - talkPlayer.position;
            float yaw = Mathf.Atan2(toLe.x, toLe.z) * Mathf.Rad2Deg;
            yield return Rig.MoveTo(talkPlayer.position, yaw, 0f, 0.8f);
            yield return le.Face(talkLe.eulerAngles.y);
            foreach (var id in new[] { "pro_ba_patriot", "pro_le_patriot", "pro_ba_secret", "pro_le_secret", "pro_ba_abroad", "pro_ba_abroad2", "pro_ba_invite", "pro_ba_invite2" })
            {
                // Anh Lê chỉ khua tay khi chính anh nói; lúc Ba nói thì đứng nghe.
                le.Play(id.StartsWith("pro_le_") ? "Talk" : "Idle");
                yield return Root.Subtitles.PlayAndWait(id);
            }

            // P3 Anh Lê hỏi tiền; người chơi giữ Space giơ hai bàn tay (khung HandsShotPose, E2 dùng lại).
            Beat = "P3";
            yield return Root.Subtitles.PlayAndWait("pro_le_money");
            le.Play("Idle");
            yield return WaitForRaise(1.5f, "prompt.raise_hands", 0f);
            var pose = Root.Config != null ? Root.Config.handsShot : null;
            if (pose != null)
            {
                // Khung riêng của P3: lùi một bước để anh Lê vào trọn khung, cúi ít để thấy mặt anh, hai tay nằm trên vùng phụ đề.
                var back = Rig.transform.position - Rig.transform.forward * pose.prologueStepBack;
                back.y = Rig.transform.position.y;
                yield return Rig.MoveTo(back, yaw, 0f, 0.5f);
                // Anh Lê cúi nhìn xuống hai bàn tay (clip Listen: gật đầu lắng nghe).
                le.Play("Listen");
                yield return Rig.EnterHandsShot(pose, true);
            }
            Root.Events.Raise(EventIds.S1HandsRaised);
            Root.UI.Cards.Show("sc_hands_story");
            // Im lặng thứ nhất (DIRECTOR §5): âm phố hạ dần, "Đây, tiền đây." nói trong im lặng.
            Root.Audio.Hush(0.12f, 1.2f);
            yield return Root.Subtitles.PlayAndWait("pro_ba_hands");
            Root.Audio.Hush(1f, 2.5f);
            yield return Root.Subtitles.PlayAndWait("pro_ba_work");
            yield return Root.Subtitles.PlayAndWait("pro_ba_work2");
            yield return new WaitForSeconds(1f);
            Rig.ExitHandsShot();
            Rig.look.SetAngles(yaw, 0f);
            Rig.SetControl(false);

            // P4 Anh Lê đồng ý hơi nhanh, rồi hẹn "thu xếp với nhà" (DIRECTOR §6: lời hứa mỏng; Lê không đến thì người chơi tự thấy
            // ở bến, không có thẻ chữ nói thay). Thẻ thời gian rồi sang Bến Nhà Rồng.
            Beat = "P4";
            le.Play("Talk");
            yield return Root.Subtitles.PlayAndWait("pro_le_agree");
            yield return Root.Subtitles.PlayAndWait("pro_le_family");
            le.Play("Idle");
            Root.UI.Cards.Clear();
            ambience?.FadeOut();
            Root.Audio.StopMusic(1.5f);
            yield return Root.Scenes.Fade(0f, 1f, 1f);
            Root.UI.Card.Blackout();
            Root.UI.SetFade(0f);
            yield return Root.UI.Card.Show(S("card.prologue.days"), 5f);
            Root.Events.Raise(EventIds.S0PathChosen);
            Root.UI.Card.Hide();
            Root.UI.Notebook.AllowToggle = true;
            Root.UI.Notebook.OpenAt("p_other_path");
            yield return WaitFor(() => !Root.UI.Notebook.IsOpen, () => Root.UI.Notebook.Close());
            SaveSystem.Write(SaveSystem.Capture(SceneFlow.GameScenes[0], Root.State, Root.Flags, Root.Notebook, Root.Events));
            SceneFlow.FromPrologue = true;
            Root.Scenes.Load(SceneFlow.GameScenes[0]);
        }

        IEnumerator StreetDiscoveries()
        {
            Beat = "P1a";
            yield return Root.Subtitles.PlayAndWait("pro_le_show");
            var lines = new[] { "pro_ba_light", "pro_ba_cinema", "pro_ba_water" };
            // Ba hỏi nước ở đâu ra thì anh Lê đáp.
            var replies = new[] { null, null, "pro_le_water" };
            for (int i = 0; i < discoveries.Length; i++)
            {
                // Anh Lê chỉ dẫn đi khi Ba đã ở cạnh, tới nơi thì đợi Ba theo kịp rồi mới bắt đầu đoạn nhìn.
                yield return WaitPlayerNearLe();
                yield return le.WalkTo(discoveryLePoints[i]);
                yield return WaitPlayerNearLe();
                var target = discoveries[i];
                var mark = target.transform;
                var box = target.GetComponent<Collider>();
                var lookPoint = box != null ? box.bounds.center : target.transform.position;
                // Anh Lê quay hẳn về vật Ba sắp nói tới (đèn, màn chiếu phim, vòi nước), ngẩng/cúi nhìn đúng vào vật suốt lúc Ba nói.
                yield return le.Face(YawToward(le.Position, lookPoint));
                le.Play("Idle");
                var gaze = NpcGaze.Of(le);
                gaze.LookAt(lookPoint);
                bool seen = false;
                target.Interacted += _ => seen = true;
                target.available = true;
                // Bóng đèn treo cao: phải ngước nhìn đúng vào bóng, tia nhắm cần dài hơn tầm với thường.
                float range = Rig.interactor.range;
                Rig.interactor.range = Mathf.Max(range, Vector3.Distance(Rig.Position, lookPoint) + 3f);
                Guide("guide.pro_look", () => mark);
                yield return WaitFor(() => seen, () => seen = true);
                Rig.interactor.range = range;
                target.available = false;
                ClearGuide();
                DiscoveriesSeen++;
                yield return Root.Subtitles.PlayAndWait(lines[i]);
                if (replies[i] != null)
                {
                    le.Play("Talk");
                    yield return Root.Subtitles.PlayAndWait(replies[i]);
                    le.Play("Idle");
                }
                gaze.LookAt(null);
            }
            Beat = "P1b";
            yield return le.WalkTo(iceTarget.transform.position + Vector3.right * 1.1f);
            var toSeller = iceSeller.Position - le.Position;
            yield return le.Face(Mathf.Atan2(toSeller.x, toSeller.z) * Mathf.Rad2Deg);
            le.Play("Talk");
            yield return Root.Subtitles.PlayAndWait("pro_le_buy_ice");
            iceSeller.Play("HandOver");
            le.Play("HandOver");
            yield return PassCupToLe();
            yield return Root.Subtitles.PlayAndWait("pro_le_ice");
            bool taken = false;
            iceTarget.Interacted += _ => taken = true;
            iceTarget.available = true;
            Guide("guide.pro_ice", () => iceTarget.transform);
            yield return WaitFor(() => taken, () => taken = true);
            iceTarget.available = false;
            // Ly anh Lê vừa đưa sang tay Ba; ly thứ hai (gọi hai ly) người bán đưa cho anh Lê sau.
            leIceCup.gameObject.SetActive(false);
            StartCoroutine(SecondIceToLe());
            Guide("guide.pro_eat");
            // Gốc khung tay là cổ tay; kiểu cầm đặt cốc áp lòng bàn tay trái, thìa kẹp giữa ngón cái và ngón trỏ phải.
            IceEatMotion.CupGrip.Attach(iceCup, Rig.hands.left);
            iceCup.gameObject.SetActive(true);
            var spoon = iceCup.Find("Spoon");
            IceEatMotion.SpoonGrip.Attach(spoon, Rig.hands.right);
            spoon.localScale = Vector3.one;
            var cream = iceCup.Find("IceCream");
            var spoonScoop = spoon.Find("SpoonScoop");
            // Hai bàn tay có xương ngón: ôm cốc và kẹp cán thìa bằng ngón thật.
            Rig.hands.SetFingers(true, true);
            Rig.hands.OverrideEmission = IceEatMotion.HandEmission;
            if (Rig.hands.LeftFingersActive) Rig.hands.leftFingers.Target = IceEatMotion.CupGrip.fingers;
            if (Rig.hands.RightFingersActive) Rig.hands.rightFingers.Target = IceEatMotion.SpoonGrip.fingers;
            // Chỉ dẫn góc trái (guide.pro_eat) đã nói giữ chuột; không lặp chữ giữa màn hình, chỉ còn vòng tiến độ ở tâm ngắm.
            const float perBite = IceEatSeconds / IceEatMotion.Bites;
            float idle = 0f;
            while (IceEaten < IceEatSeconds)
            {
                // Giữ chuột thì ăn tiếp; thả quá một chút thì thìa đang dở được tua về lúc chưa xúc (kem trả về cốc).
                if (Root.Input.UseHeld || Sim.Enabled) { IceEaten += Time.deltaTime; idle = 0f; }
                else if ((idle += Time.deltaTime) > .5f)
                    IceEaten = Mathf.Max(Mathf.Floor(IceEaten / perBite) * perBite, IceEaten - 1.4f * perBite * Time.deltaTime / .5f);
                int bite = Mathf.Min((int)(IceEaten / perBite), IceEatMotion.Bites - 1);
                var frame = IceEatMotion.Evaluate(bite, IceEaten / perBite - bite);
                Rig.hands.SetOverride(new HandPoseTarget(HandPose.Grip, IceEatMotion.LeftHandPosition, IceEatMotion.LeftHandEuler, frame.rightPosition, frame.rightEuler));
                IceEatMotion.ApplyCupScoop(cream, frame.cupScoop);
                IceEatMotion.ApplySpoonScoop(spoonScoop, frame.spoonScoop);
                Root.UI.Prompt.SetProgress(IceEaten / IceEatSeconds);
                yield return null;
            }
            // Thìa cuối đã ăn xong: tay về chỗ nghỉ, cốc rỗng.
            var done = IceEatMotion.Idle(IceEatMotion.Bites);
            Rig.hands.SetOverride(new HandPoseTarget(HandPose.Grip, IceEatMotion.LeftHandPosition, IceEatMotion.LeftHandEuler, done.rightPosition, done.rightEuler));
            IceEatMotion.ApplyCupScoop(cream, 0f);
            IceEatMotion.ApplySpoonScoop(spoonScoop, 0f);
            Root.UI.Prompt.SetProgress(null);
            Root.UI.Prompt.SetScenePrompt(null);
            ClearGuide();
            yield return Root.Subtitles.PlayAndWait("pro_ba_ice");
            spoon.gameObject.SetActive(false);
            iceCup.gameObject.SetActive(false);
            leIceCup.gameObject.SetActive(false);
            Rig.hands.SetFingers(false, false);
            Rig.hands.OverrideEmission = 1f;
            Rig.hands.SetPose(HandPose.Lowered);
            Root.Events.Raise(EventIds.S0StreetDone);
        }

        IEnumerator SecondIceToLe()
        {
            yield return new WaitForSeconds(0.8f);
            // Anh Lê đang quay về Ba: quay sang người bán nhận ly (tay người bán không với tới sau lưng anh Lê), xong quay lại.
            float back = le.transform.eulerAngles.y;
            var toSeller = iceSeller.Position - le.Position;
            yield return le.Face(Mathf.Atan2(toSeller.x, toSeller.z) * Mathf.Rad2Deg);
            iceSeller.Play("HandOver");
            le.Play("HandOver");
            yield return PassCupToLe();
            le.Play("Idle");
            yield return le.Face(back);
        }

        /// <summary>
        /// Người bán trao cốc cho anh Lê (người dùng 07/11: cốc không được bay từ không khí): tay phải người bán xuống sau quầy lấy
        /// cốc (quầy cao 1,22 m che tay), nâng lên lòng bàn tay ngửa đưa ra góc quầy; anh Lê với tay nắm cốc (dáng Blender,
        /// LeCupGrip) rồi người bán buông, anh Lê kéo cốc về trước bụng.
        /// </summary>
        IEnumerator PassCupToLe()
        {
            if (_leGrip == null && !le.TryGetComponent(out _leGrip)) _leGrip = le.gameObject.AddComponent<LeCupGrip>();
            var seller = iceSeller.GetComponent<HaiBanTay.Npc.NpcArmIK>();
            if (seller == null) seller = iceSeller.gameObject.AddComponent<HaiBanTay.Npc.NpcArmIK>();
            if (_sellerHand == null) _sellerHand = new GameObject("IceSellerHand").transform;
            var toLe = le.Position - iceSeller.Position; toLe.y = 0f; toLe.Normalize();
            var palmUp = Quaternion.LookRotation(toLe, Vector3.down);
            // Tay thấp sau quầy, trước bụng người bán.
            var low = iceSeller.Position + Vector3.up * .85f + iceSeller.transform.forward * .2f;
            // Điểm trao: dáng cầm của anh Lê dời 10 cm về phía người bán, nâng lên trên mặt quầy cho Ba thấy cốc qua tay.
            var give = le.transform.TransformPoint(new Vector3(.177f, .98f, .40f)) - toLe * .1f;
            give.y = Mathf.Max(give.y, IceCounterTop + .08f);
            Vector3 Wrist(Vector3 cupBase) => cupBase - palmUp * LeCupGrip.PalmCup;
            // Anh Lê nhìn chỗ trao cốc: NpcGaze nhìn điểm thì không quay thân theo Ba giữa lúc nhận.
            var gaze = NpcGaze.Of(le);
            gaze.LookAt(give);
            seller.rightTarget = _sellerHand;
            seller.rightCurl = new Vector3(12f, 10f, 6f);
            _sellerHand.SetPositionAndRotation(low, palmUp);
            for (float t = 0f; t < 1f; t += Time.deltaTime / .35f) { seller.rightWeight = Mathf.SmoothStep(0f, 1f, t); yield return null; }
            seller.rightWeight = 1f;
            leIceCup.SetParent(le.transform, true);
            leIceCup.gameObject.SetActive(true);
            _leGrip.Offer(leIceCup, seller);
            yield return new WaitForSeconds(.15f);
            // Nâng thẳng lên quá mặt quầy rồi mới đưa ngang (đường chéo xuyên góc quầy).
            var lowWrist = _sellerHand.position;
            var over = new Vector3(lowWrist.x, Wrist(give).y + .02f, lowWrist.z);
            for (float t = 0f; t < 1f; t += Time.deltaTime / .45f) { _sellerHand.position = Vector3.Lerp(lowWrist, over, Mathf.SmoothStep(0f, 1f, t)); yield return null; }
            for (float t = 0f; t < 1f; t += Time.deltaTime / .5f) { _sellerHand.position = Vector3.Lerp(over, Wrist(give), Mathf.SmoothStep(0f, 1f, t)); yield return null; }
            _sellerHand.position = Wrist(give);
            _leGrip.handoff = give;
            _leGrip.reachAmount = 1f;
            _leGrip.Reach();
            float until = Time.time + 2f;
            while (!_leGrip.CupInHand && Time.time < until) yield return null;
            yield return new WaitForSeconds(.1f);
            for (float t = 0f; t < 1f; t += Time.deltaTime / .6f)
            {
                float k = Mathf.SmoothStep(0f, 1f, t);
                seller.rightWeight = Mathf.Clamp01(1f - k * 1.6f);
                _leGrip.reachAmount = 1f - k;
                yield return null;
            }
            seller.rightWeight = 0f;
            _leGrip.reachAmount = 0f;
            gaze.LookAt(null);
        }

        Transform _sellerHand;
        /// <summary>Mặt quầy kem (PrologueSceneBuilder: IceCounter tâm y .67, cao 1,1 m).</summary>
        const float IceCounterTop = 1.22f;
        LeCupGrip _leGrip;

        [Tooltip("Ba cách anh Lê trong tầm này (m) thì anh Lê mới đi tiếp / mới bắt đầu đoạn nhìn.")]
        public float leWaitRadius = 3f;

        /// <summary>Người chơi còn xa thì anh Lê đứng quay về phía Ba và chờ (Guide chỉ vào anh Lê).</summary>
        IEnumerator WaitPlayerNearLe()
        {
            if (Rig == null || Near()) yield break;
            if (Sim.Enabled)
            {
                var back = le.Position - le.transform.forward * 1.5f;
                Rig.motor.Teleport(back, YawToward(back, le.Position));
                yield break;
            }
            le.Play("Idle");
            Guide("guide.pro_follow", () => le.transform);
            float nextFace = 0f, t0 = Time.time;
            bool hinted = false;
            Coroutine facing = null;
            while (!Near())
            {
                if (!hinted && Time.time - t0 >= followHintSeconds)
                {
                    hinted = true;
                    Root.UI.Prompt.SetScenePrompt(S("hint.prologue_follow"));
                }
                if (Time.time >= nextFace)
                {
                    nextFace = Time.time + 1f;
                    if (facing != null) StopCoroutine(facing);
                    facing = StartCoroutine(le.Face(YawToward(le.Position, Rig.Position)));
                }
                yield return null;
            }
            if (facing != null) StopCoroutine(facing);
            if (hinted) Root.UI.Prompt.SetScenePrompt(null);
            ClearGuide();

            bool Near()
            {
                var d = Rig.Position - le.Position;
                d.y = 0f;
                return d.sqrMagnitude <= leWaitRadius * leWaitRadius;
            }
        }

        static float YawToward(Vector3 from, Vector3 to)
        {
            var d = to - from;
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        /// <summary>Anh Lê đi dọc đường phố tới chỗ cái đèn (điểm nhìn đầu tiên), không đi quá rồi quay lại.</summary>
        IEnumerator LeWalks()
        {
            var stop = discoveryLePoints[0].position;
            var start = le.Position;
            var dir = stop - start;
            dir.y = 0f;
            foreach (var p in lePath)
                if (Vector3.Dot(p.position - start, dir) < dir.sqrMagnitude - 1f)
                    yield return le.WalkTo(p);
            yield return le.WalkTo(discoveryLePoints[0]);
            LeArrived = true;
        }
    }
}
