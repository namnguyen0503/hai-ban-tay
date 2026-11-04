using System.Collections;
using HaiBanTay.Audio;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Ship;
using UnityEngine;

namespace HaiBanTay.Narrative
{
    /// <summary>
    /// Cảnh 01 — Bến Nhà Rồng (PLAN mục 7.2): B1.1 thẻ, B1.2 hai bàn tay (bỏ khi vào từ prologue), B1.3 trên kè vác 2 bao khoai
    /// từ xe lên cầu thang tàu, qua boong, xuống hầm thực phẩm (F03), B1.4 nhổ neo, B1.5 sang cảnh 02.
    /// </summary>
    public sealed class NhaRongDirector : SceneDirector
    {
        public ShipMotion ship;
        public ShipAmbience ambience;
        public EngineHum engine;
        public BackdropMover backdrop;
        public BeatTrigger railing;
        [Tooltip("Cả con tàu (trừ cầu thang tàu): phải ở trên tàu thì mới nhổ neo.")]
        public PlayerArea aboard;
        [Tooltip("Vào khối bếp sau khi nhổ neo thì sang cảnh 02.")]
        public ShipZone galleyZone;
        /// <summary>Ba cách vùng bếp dưới chừng này (m, tới ngưỡng cửa) thì chuyển sang cảnh 02.</summary>
        const float GalleyDoorReach = 1f;
        public GameObject gangway;
        public GameObject gangwayBarrier;
        [Header("Bao khoai (B1.3)")]
        public Holdable[] sacks = new Holdable[0];
        public SackStack stack;
        [Tooltip("Số bao phải xếp xuống hầm trước khi nhổ neo.")]
        public int sackTarget = 2;
        [Tooltip("Hết giờ này (giây) kể từ khi tự do đi lại thì nhổ neo dù chưa đủ bao.")]
        public float sackSeconds = 180f;
        [Header("Nhổ neo (B1.4)")]
        [Tooltip("Dây buộc tàu: tháo khi nhổ neo.")]
        public GameObject[] ropes = new GameObject[0];
        [Tooltip("Gốc đám đông trên bến (con của phông bờ): người đi lại dừng, người đứng yên ở lại với bến lùi xa.")]
        public Transform quayCrowd;
        public NpcAgent mai;
        public NpcAgent[] jobStaff;
        public NpcAgent jobCaptain;
        public InteractTarget jobStaffTarget, jobCaptainTarget;
        public Transform[] jobMaiPath;
        public Transform jobSpawn;
        public bool MaiAtCaptain { get; private set; }
        public bool JobAccepted => Root.Events.HasFired(EventIds.S1JobAccepted);
        public NpcAgent purser;
        public NpcAgent chef;
        [Tooltip("Chỗ đưa người chơi lên boong nếu mãi không lên tàu (đầu cầu thang tàu).")]
        public Transform boardPoint;
        public Transform gangwayFootPoint;
        public Transform gangwayTopPoint;
        public float openingSeconds = 8f;
        public float notebookPromptSeconds = 8f;
        public float boardWaitSeconds = 25f;
        public float lingerMaxSeconds = 40f;
        public float lingerFlagSeconds = 6f;
        public float retractSeconds = 2.5f;

        /// <summary>Bao đã xếp xuống hầm.</summary>
        public int SacksDelivered => stack != null ? stack.Count : 0;
        /// <summary>Đã nhổ neo (cầu thang tàu đã thu).</summary>
        public bool Departed { get; private set; }

        IEnumerator Start()
        {
            TalkGaze.Bind("mai", mai);
            TalkGaze.Bind("ong_mai", mai);
            TalkGaze.Bind("captain", jobCaptain);
            TalkGaze.Bind("quan_bep", chef);
            TalkGaze.Bind("purser", purser);
            if (jobStaff != null && jobStaff.Length > 0) TalkGaze.Bind("nhan_vien", jobStaff[0]);
            // Vào từ prologue thì giữ tiến trình (cờ s1.hands.raised, thẻ sc_hands_story) và bỏ beat giơ tay B1.2.
            bool fromPrologue = SceneFlow.FromPrologue;
            SceneFlow.FromPrologue = false;
            if (!fromPrologue)
                Root.ResetProgress();
            Rig.SetOutfit(TrangPhuc.Thuong);
            SetGrime(0f, instant: true);
            ship.Intensity = 0f;
            if (gangwayBarrier != null)
                gangwayBarrier.SetActive(false);

            if (fromPrologue)
            {
                // Giơ tay đã làm ở P3: chỉ giữ cờ và thẻ nguồn "hai bàn tay trắng".
                Beat = "B1.2p";
                if (!Root.Events.HasFired(EventIds.S1HandsRaised))
                    Root.Events.Raise(EventIds.S1HandsRaised);
                Root.UI.Cards.Show("sc_hands");
            }
            else
            {
                // B1.1 Thẻ mở đầu
                Beat = "B1.1";
                BeginCutscene();
                Rig.SetControl(false);
                Root.UI.Card.Blackout();
                yield return Root.UI.Card.Show(S("card.opening"), openingSeconds);

                // B1.2 Hai bàn tay: camera cúi nhìn đúng khung HandsShotPose; giữ Space thì tay ngửa trống nâng vào khung.
                Beat = "B1.2";
                var pose = Root.Config != null ? Root.Config.handsShot : null;
                if (pose != null)
                    Rig.look.SetAngles(Rig.look.Yaw, pose.pitch);
                Root.UI.Card.Hide();
                yield return WaitForRaise(1.5f, "prompt.raise_hands", 0f);
                if (pose != null)
                    yield return Rig.EnterHandsShot(pose);
                Root.Events.Raise(EventIds.S1HandsRaised);
                Root.UI.Cards.Show("sc_hands");
                Root.UI.Cards.Show("sc_hands_story");
                yield return new WaitForSeconds(2.5f);
                yield return Root.Scenes.Fade(0f, 1f, 1f);
                Rig.ExitHandsShot();
                Rig.SetControl(false);
                yield return Root.Scenes.Fade(1f, 0f, 1f);
            }

            if (!JobAccepted)
                yield return ApplyForJob();
            yield return Root.Scenes.Fade(0f, 1f, 0.6f);
            Root.UI.Card.Blackout();
            Root.UI.SetFade(0f);
            yield return Root.UI.Card.Show(S("card.job.departure"), 4f);
            Rig.SetOutfit(TrangPhuc.PhuBep);
            Rig.motor.Teleport(jobSpawn.position, jobSpawn.eulerAngles.y);
            Root.UI.Card.Hide();
            jobCaptain.gameObject.SetActive(false);
            foreach (var actor in jobStaff) actor.gameObject.SetActive(false);
            mai.TeleportTo(jobSpawn.position + Vector3.right * 2f, 270f);
            SaveSystem.Write(SaveSystem.Capture(SceneFlow.GameScenes[0], Root.State, Root.Flags, Root.Notebook, Root.Events));

            // B1.3 Trên kè, đầu giờ trưa: sổ quan sát, ông Mai chỉ việc, vác bao khoai.
            Beat = "B1.3";
            EndCutscene();
            Rig.SetControl(true);
            Root.Events.Raise(EventIds.S1NotebookIntro);
            StartCoroutine(MaiSpeaks());
            Root.UI.Prompt.SetScenePrompt(S("prompt.notebook"));
            Guide("guide.nr_notebook");
            var nb = Root.UI.Notebook;
            int opened = nb.OpenCount;
            float t0 = Time.time;
            yield return WaitFor(() => nb.OpenCount > opened || Time.time - t0 > notebookPromptSeconds);
            Root.UI.Prompt.SetScenePrompt(null);
            ClearGuide();
            if (nb.IsOpen)
                yield return WaitFor(() => !nb.IsOpen, null, 60f);

            yield return CarrySacks();

            // B1.4 Nhổ neo. Ba đang ở dưới kè thì chờ lên tàu: cầu thang tàu thu lên ngay khi tàu rời kè.
            Beat = "B1.4";
            if (aboard != null && !aboard.PlayerInside)
            {
                Root.UI.Prompt.SetScenePrompt(S("hint.board"));
                Guide("hint.board", () => gangwayFootPoint);
                yield return WaitFor(() => aboard.PlayerInside, SimBoard, boardWaitSeconds);
                Root.UI.Prompt.SetScenePrompt(null);
                ClearGuide();
                if (!aboard.PlayerInside)
                    yield return PutAboard();
            }
            Root.Audio.PlaySfx("SFX/ship_horn");
            if (ambience != null)
                ambience.departed = true;
            engine?.Play();
            Root.Audio.PlayMusic("Music/voyage", loop: true, volume: 0.25f);
            backdrop.Begin();
            ship.Intensity = 0.15f;
            Root.Subtitles.Play("chef_depart");
            Root.Events.Raise(EventIds.S1Depart);
            if (gangwayBarrier != null)
                gangwayBarrier.SetActive(true);
            FreezeQuay();
            Departed = true;
            StartCoroutine(Retract());
            StartCoroutine(MaiAtRail());
            float lingerStart = railing.InsideSeconds;
            float t1 = Time.time;
            Guide("guide.nr_galley", () => galleyZone != null ? galleyZone.transform : null);
            // Bếp ở cảnh 01 chỉ là vỏ (bếp lò, đồ, người ở cảnh 02): chuyển cảnh ngay ở ngưỡng cửa bếp, không để người chơi bước vào nhìn
            // căn bếp trống (người dùng báo; bot 04/11 quay thấy phòng chỉ có một cái bàn). Hết giờ đi lại trên tàu cũng chuyển.
            var galleyBounds = galleyZone != null ? galleyZone.GetComponent<Collider>() : null;
            yield return WaitFor(() => galleyZone != null && Time.time - t1 >= 4f && (galleyZone.PlayerInside
                || (galleyBounds != null && Rig != null && galleyBounds.bounds.SqrDistance(Rig.Position + Vector3.up) < GalleyDoorReach * GalleyDoorReach))
                || Time.time - t1 >= lingerMaxSeconds);
            if (railing.InsideSeconds - lingerStart >= lingerFlagSeconds)
                Root.Flags.Set(EventIds.FlagS1Lingered);

            // B1.5 Chuyển cảnh
            Beat = "B1.5";
            BeginCutscene();
            Root.Scenes.LoadWithTransition(SceneFlow.GameScenes[1], "A");
        }

        IEnumerator ApplyForJob()
        {
            Beat = "B0";
            BeginCutscene();
            Rig.SetControl(false);
            Root.UI.Card.Blackout();
            yield return Root.UI.Card.Show(S("card.prologue.noon"), 4f);
            Root.UI.Card.Hide();
            EndCutscene();
            Rig.SetControl(true);
            Guide("hint.job.staff", () => jobStaffTarget.available ? jobStaffTarget.transform : null);
            bool asked = false;
            jobStaffTarget.Interacted += _ => asked = true;
            jobStaffTarget.available = true;
            yield return WaitFor(() => asked, () => asked = true);
            jobStaffTarget.available = false;
            ClearGuide();
            yield return new WaitForSeconds(1f); // Một phút ngập ngừng được dàn dựng thành nhịp ngắn.
            yield return Root.Subtitles.PlayAndWait("job_ba_ask");
            foreach (var actor in jobStaff) actor.Play("Talk");
            yield return Root.Subtitles.PlayAndWait("job_staff_no");
            Beat = "B0b";
            yield return jobStaff[0].Face(270f);
            yield return jobStaff[1].Face(90f);
            foreach (var actor in jobStaff) { actor.Play("Talk2"); actor.GetComponent<StaffAside>().Laughing = true; }
            yield return Root.Subtitles.PlayAndWait("job_staff_whisper");
            foreach (var actor in jobStaff) { actor.GetComponent<StaffAside>().Laughing = false; actor.Play("Idle"); }
            mai.Play("Talk");
            yield return Root.Subtitles.PlayAndWait("job_mai_lead");
            yield return Root.Subtitles.PlayAndWait("job_mai_lead2");
            Beat = "B0c";
            Guide("hint.job.mai", () => MaiAtCaptain ? jobCaptainTarget.transform : mai.transform);
            StartCoroutine(LeadToCaptain());
            bool met = false;
            jobCaptainTarget.Interacted += _ => met = true;
            yield return WaitFor(() => MaiAtCaptain && met, () => { mai.TeleportTo(jobMaiPath[jobMaiPath.Length - 1].position, 270f); MaiAtCaptain = met = true; });
            jobCaptainTarget.available = false;
            ClearGuide();
            Beat = "B0d";
            BeginCutscene();
            Rig.SetControl(false);
            var face = jobCaptain.Position - Rig.Position;
            Rig.look.SetAngles(Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg, 0f);
            jobCaptain.Play("Talk");
            yield return Root.Subtitles.PlayAndWait("job_captain_work");
            yield return Root.Subtitles.PlayAndWait("job_ba_any");
            yield return Root.Subtitles.PlayAndWait("job_captain_accept");
            yield return Root.Subtitles.PlayAndWait("job_captain_name");
            yield return Root.Subtitles.PlayAndWait("job_ba_name");
            Root.Events.Raise(EventIds.S1JobAccepted);
            Root.UI.Cards.Show("sc_job");
            SaveSystem.Write(SaveSystem.Capture(SceneFlow.GameScenes[0], Root.State, Root.Flags, Root.Notebook, Root.Events));
        }

        IEnumerator LeadToCaptain()
        {
            foreach (var point in jobMaiPath) yield return mai.WalkTo(point);
            mai.Play("Idle");
            MaiAtCaptain = true;
            jobCaptainTarget.available = true;
        }

        /// <summary>Ông Mai gọi anh Ba lại lấy khoai, chỉ đường lên tàu; nhắc lại lời xin việc (F01c) khi anh nhấc bao đầu tiên.</summary>
        IEnumerator MaiSpeaks()
        {
            yield return new WaitForSeconds(1.5f);
            if (Sim.Enabled)
                yield break;
            Root.Subtitles.Play("mai_hail");
            Root.Subtitles.Play("mai_route");
            while (Rig == null || Rig.interactor.Holding == null)
            {
                if (Departed)
                    yield break;
                yield return null;
            }
            yield return new WaitForSeconds(2f);
            if (!Departed)
                Root.Subtitles.Play("mai_recall");
        }

        bool _sackCarrying, _sackGuideUnset = true;

        Transform NearestSack()
        {
            Transform best = null;
            float bestDistance = float.MaxValue;
            if (Rig == null)
                return null;
            foreach (var s in sacks)
            {
                if (s == null || !s.gameObject.activeInHierarchy || s.Locked || s.IsHeld)
                    continue;
                float d = (s.transform.position - Rig.Position).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = s.transform; }
            }
            return best;
        }

        /// <summary>Vác bao khoai: nhắc đúng việc đang cần, quản bếp gọi khi anh lên tàu hoặc chậm; hết hạn thì thôi.</summary>
        IEnumerator CarrySacks()
        {
            float start = Time.time;
            string shown = null;
            bool chefCalled = false, lateCalled = false;
            while (stack != null && stack.Count < sackTarget && Time.time - start < sackSeconds)
            {
                if (galleyZone != null && galleyZone.PlayerInside)
                    break; // đã vào bếp trước: không bắt buộc bao nữa
                if (Sim.Enabled && Time.time - start >= Sim.StepSeconds)
                {
                    // Sim: xếp thẳng bao xuống hầm như người chơi đã vác xong.
                    foreach (var s in sacks)
                        if (stack.Count < sackTarget)
                            stack.Place(s);
                    break;
                }
                var held = Rig != null ? Rig.interactor.Holding : null;
                bool carrying = held != null && held.kind == "sack";
                // Đang nhìn một vật bấm được (bao khoai, chỗ xếp bao...) thì để prompt vật thể ([E] Nhấc lên / [E] Đặt xuống)
                // thay vì câu gợi ý đường đi dài đè lên.
                var look = Rig != null ? Rig.interactor : null;
                bool aimingAtTarget = look != null && look.Current != null && !string.IsNullOrEmpty(look.Current.GetPromptKey(look));
                string key = aimingAtTarget ? "" : carrying ? "hint.sack_route" : stack.Count == 0 ? "hint.sack_start" : "hint.sack_more";
                if (key != shown)
                {
                    shown = key;
                    Root.UI.Prompt.SetScenePrompt(key.Length > 0 ? S(key) : null);
                }
                // Dòng chỉ dẫn + mốc: bao gần nhất trên kè, hoặc đường mang bao (chân cầu thang tàu → chỗ xếp bao ở hầm).
                if (carrying != _sackCarrying || _sackGuideUnset)
                {
                    _sackGuideUnset = false;
                    _sackCarrying = carrying;
                    if (carrying)
                        Guide("guide.nr_sack_route", () => aboard != null && !aboard.PlayerInside && gangwayFootPoint != null ? gangwayFootPoint : stack.transform);
                    else
                        Guide("guide.nr_sack_take", NearestSack);
                }
                if (!chefCalled && carrying && aboard != null && aboard.PlayerInside)
                {
                    chefCalled = true;
                    Root.Subtitles.Play("chef_potatoes");
                }
                if (!lateCalled && !carrying && stack.Count == 0 && Time.time - start > 75f)
                {
                    lateCalled = true;
                    Root.Subtitles.Play("chef_late_quay");
                }
                yield return null;
            }
            Root.UI.Prompt.SetScenePrompt(null);
            ClearGuide();
            // Bao còn trên kè không đi theo bến; bao đang cầm mà hết hạn thì cất đi.
            var holding = Rig != null ? Rig.interactor.Holding : null;
            if (holding != null && holding.kind == "sack" && !holding.Locked)
                Rig.interactor.ReleaseHeld();
            foreach (var s in sacks)
                if (s != null && !s.Locked)
                {
                    var back = s.GetComponent<ReturnToStart>();
                    if (back != null)
                        back.enabled = false;
                    s.gameObject.SetActive(false);
                }
        }

        void SimBoard()
        {
            if (boardPoint != null)
                Rig.motor.Teleport(boardPoint.position, 0f);
        }

        /// <summary>Mãi không lên tàu: đưa tới đầu cầu thang tàu (fade) để tàu nhổ neo, không có màn chờ chết.</summary>
        IEnumerator PutAboard()
        {
            yield return Root.Scenes.Fade(0f, 1f, 0.5f);
            SimBoard();
            yield return null;
            yield return Root.Scenes.Fade(1f, 0f, 0.5f);
        }

        /// <summary>Phông bờ lùi dần: người đi lại dừng, ai đang trên cầu thang tàu được đặt xuống kè, người đứng yên vẫy tay tiễn tiếp.</summary>
        void FreezeQuay()
        {
            if (quayCrowd == null)
                return;
            foreach (var r in quayCrowd.GetComponentsInChildren<CrowdRoutine>(true))
            {
                var a = r.agent;
                if (a != null && a.BodyActive && a.Position.y > -3.95f && gangwayFootPoint != null)
                {
                    // Đang đứng trên cầu thang tàu sắp thu lên: đặt xuống chân cầu thang.
                    var p = gangwayFootPoint.position;
                    a.TeleportTo(new Vector3(p.x + 1.5f, p.y, p.z - 1.5f), 180f);
                }
                r.HaltIfWalking();
            }
        }

        /// <summary>
        /// Ba đứng lan can nhìn xuống bến lúc tàu rời (DIRECTOR §6): ông Mai từ phía trong boong (ngoài khung nhìn) bước tới đứng
        /// cạnh, hỏi một câu; hai câu cho người chơi biết anh Lê không tới. Ba không ra lan can thì thôi, không ép.
        /// </summary>
        IEnumerator MaiAtRail()
        {
            if (Sim.Enabled || mai == null || railing == null)
                yield break;
            float t0 = Time.time;
            while (!railing.PlayerInside || Root.Subtitles.IsPlaying)
            {
                if (Time.time - t0 > lingerMaxSeconds || (galleyZone != null && galleyZone.PlayerInside))
                    yield break;
                yield return null;
            }
            yield return new WaitForSeconds(1.5f);
            if (!railing.PlayerInside || Rig == null)
                yield break;
            var cam = Rig.cam;
            var feet = Rig.Position;
            // Lan can chạy dọc x, phía trong boong là +z: đặt ông ở sau lưng chếch bên, ngoài khung nhìn, rồi bước tới cạnh Ba.
            foreach (float side in new[] { 1f, -1f })
            {
                var from = new Vector3(feet.x + side * 3.2f, feet.y, feet.z + 1.6f);
                if (cam != null)
                {
                    var v = cam.WorldToViewportPoint(from + Vector3.up * 1f);
                    if (v.z > 0f && v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f)
                        continue;
                }
                var to = new Vector3(feet.x + side * 0.9f, feet.y, feet.z + 0.25f);
                mai.TeleportTo(from, side > 0f ? 250f : 110f);
                mai.Play("Idle");
                yield return mai.WalkTo(to);
                yield return mai.Face(180f);
                if (!railing.PlayerInside)
                    yield break;
                yield return Root.Subtitles.PlayAndWait("nr_mai_wait");
                yield return Root.Subtitles.PlayAndWait("nr_ba_friend");
                yield break;
            }
        }

        /// <summary>Tháo dây buộc tàu rồi thu cầu thang tàu lên (xoay quanh đầu cầu thang trên boong), ẩn khi xong.</summary>
        IEnumerator Retract()
        {
            yield return new WaitForSeconds(0.6f);
            foreach (var rope in ropes)
                if (rope != null)
                    rope.SetActive(false);
            if (gangway == null)
                yield break;
            // Xoay quanh đầu cầu thang tàu trên boong (mép mở mạn phải): chân cầu thang nhấc lên khỏi kè.
            var top = gangwayTopPoint != null ? gangwayTopPoint.position : new Vector3(-1.5f, 0f, -7.38f);
            float angle = 0f;
            const float total = 55f;
            while (angle < total)
            {
                float step = Mathf.Min(total - angle, total / retractSeconds * Time.deltaTime);
                gangway.transform.RotateAround(top, Vector3.right, step);
                angle += step;
                yield return null;
            }
            gangway.SetActive(false);
        }
    }
}
