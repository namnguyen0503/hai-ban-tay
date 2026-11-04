using System;
using System.Collections;
using System.Linq;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Narrative;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using UnityEngine;

namespace HaiBanTay.Tests
{
    // Mọi việc bếp đi qua GameInput, kể cả đặt nồi/rổ, cọ và tráng; chỉ đọc station để kiểm kết quả.
    public sealed class PlayerBot
    {
        public readonly PlaythroughLog Log;
        public bool Ok { get; private set; }
        public PlayerRig Rig => PlayerRig.Current;
        public GameInput Input => GameRoot.Instance.Input;
        public bool WorkInterrupted => SceneDirector.Current is ShiftDirector shift && !shift.InShift && !shift.Board.HasOpen(OrderKinds.Coffee) &&
                                       (shift.restSeat == null || !shift.restSeat.available);
        StairsBalance _balance;

        /// <summary>Số khung/giây bot giả lập (BOT_FPS); batchmode bỏ qua targetFrameRate nên Frame() tự giãn nhịp.</summary>
        public static int Fps = 60;
        /// <summary>
        /// BOT_FAST=1: mỗi khung vẫn đúng 1/Fps giây game (Time.captureDeltaTime) nhưng không chờ giờ thật, tối đa FastMultiplier lần.
        /// Chỉ để kiểm luồng truyện/thoại: UI, tải cảnh và bộ dò xuyên thân (giây liền) chạy theo giờ thật nên báo thiếu lỗi hình ảnh
        /// (04/11: cả game chỉ nhanh ~1,3 lần). Kiểm lỗi hình ảnh chạy x1.
        /// </summary>
        public static bool Fast;
        public const int FastMultiplier = 4;
        /// <summary>
        /// Quay trailer (BOT_VIDEO=1, BOT_VIDEO_UI=0): lúc bot đứng chờ (không tự nhắm, không đi) máy quay nhìn mặt người đang nói,
        /// Ba nói thì nhìn người vừa nói với Ba; không ai nói thì ngẩng dần lên ngang tầm mắt. Bot nhắm vào giữa thân NPC hay đồ dưới
        /// thấp rồi đứng yên nhìn bụng, chân, mặt bàn suốt câu thoại (người dùng 04/11: "mặt không ngẩng lên mà cứ nhìn xuống đất").
        /// </summary>
        public static bool Cinematic;
        const float CinematicSmooth = 0.35f, CinematicRestPitch = 4f, CinematicRange = 9f, CinematicMemory = 6f;
        readonly System.Collections.Generic.Dictionary<HaiBanTay.Npc.NpcAgent, Transform> _heads =
            new System.Collections.Generic.Dictionary<HaiBanTay.Npc.NpcAgent, Transform>();
        HaiBanTay.Npc.NpcAgent _lastTalker;
        float _lastTalkerAt = float.NegativeInfinity, _camYawVel, _camPitchVel;

        public PlayerBot(PlaythroughLog log) { Log = log; }
        public void Fail(string message) { Ok = false; Log.Note(message); }
        /// <summary>Bước phụ không bắt buộc đã xử lý xong (đã ghi lỗi nếu có): tiếp tục tuyến.</summary>
        public void Recover() { Ok = true; }

        public void BindScene()
        {
            _balance = UnityEngine.Object.FindAnyObjectByType<StairsBalance>();
            Log.BindScene();
        }

        public void ReleaseInput()
        {
            Input.SimMove = Vector2.zero;
            Input.SimLook = Vector2.zero;
            Input.SimUse = Input.SimRaise = Input.SimSit = false;
        }

        public IEnumerator Frame()
        {
            CinematicLook();
            yield return null;
            Log.ObserveFrame();
            yield return Log.FlushFindings();
            if (Fast)
                yield break;
            // Batchmode can ignore targetFrameRate. Pace input frames without changing the game clock:
            // otherwise sub-millisecond CC.Move steps are discarded by the scene's minMoveDistance.
            if (Fps >= 60)
            {
                if (Application.isBatchMode && Time.unscaledDeltaTime < 1f / 120f)
                    System.Threading.Thread.Sleep(16);
            }
            else if (Application.isBatchMode && Time.unscaledDeltaTime < 0.75f / Fps)
                System.Threading.Thread.Sleep(Mathf.RoundToInt((1f / Fps - Time.unscaledDeltaTime) * 1000f));
        }

        void CinematicLook()
        {
            var rig = Rig;
            if (!Cinematic || rig == null || rig.look == null || rig.cam == null) return;
            // Bot vừa tự nhắm (nhấn, cầm) hay đang đi, đang rê chuột: không giành máy quay.
            if (Time.frameCount - Log.BotDriveFrame < 3 || Input.SimLook != Vector2.zero || Input.SimMove != Vector2.zero) return;
            var root = GameRoot.Instance;
            string id = root != null && root.Subtitles != null ? root.Subtitles.CurrentLineId : null;
            string speaker = id != null ? root.Content.GetLine(id).Speaker : null;
            var talker = speaker != null && speaker != HaiBanTay.Npc.TalkGaze.Player ? HaiBanTay.Npc.TalkGaze.Agent(speaker) : null;
            var eye = rig.cam.transform.position;
            if (talker != null && talker.isActiveAndEnabled && (talker.transform.position - eye).sqrMagnitude < CinematicRange * CinematicRange)
            {
                _lastTalker = talker;
                _lastTalkerAt = Time.time;
            }
            else if (speaker == null || _lastTalker == null || !_lastTalker.isActiveAndEnabled || Time.time - _lastTalkerAt > CinematicMemory)
                talker = null;
            else
                talker = _lastTalker;
            float yaw = rig.look.Yaw, pitch = CinematicRestPitch;
            if (talker != null)
            {
                var d = Head(talker) - eye;
                pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                // Máy quay bị cảnh khóa (ngồi, cutscene): chỉ ngẩng/cúi, không xoay ngang.
                if (!rig.look.Locked) yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            }
            else if (rig.look.Pitch <= CinematicRestPitch + 8f)
                return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            rig.look.SetAngles(Mathf.SmoothDampAngle(rig.look.Yaw, yaw, ref _camYawVel, CinematicSmooth, Mathf.Infinity, dt),
                Mathf.SmoothDampAngle(rig.look.Pitch, pitch, ref _camPitchVel, CinematicSmooth, Mathf.Infinity, dt));
        }

        Vector3 Head(HaiBanTay.Npc.NpcAgent agent)
        {
            if (!_heads.TryGetValue(agent, out var head))
            {
                var animator = agent.GetComponentInChildren<Animator>();
                head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
                _heads[agent] = head;
            }
            return head != null ? head.position + Vector3.up * 0.06f : agent.transform.position + Vector3.up * 1.6f;
        }

        public IEnumerator Wait(string step, Func<bool> done, float gameSeconds = 180f, float realSeconds = 240f, string severity = "BLOCKER")
        {
            float gameStart = Time.time, realStart = Time.realtimeSinceStartup;
            // BOT_FAST: lúc tải cảnh hay UI chạy theo giờ thật, khung rỗng chạy rất nhanh nên giờ game vọt (04/11: 180 s game trong 2,4 s
            // thật khi vào prologue). Hạn giờ game chỉ tính khi giờ thật cũng đã qua gameSeconds / FastMultiplier.
            float realFloor = Fast ? gameSeconds / FastMultiplier : 0f;
            while (!done() && (Time.time - gameStart < gameSeconds || Time.realtimeSinceStartup - realStart < realFloor)
                   && Time.realtimeSinceStartup - realStart < realSeconds)
                yield return Frame();
            Ok = done();
            if (!Ok)
            {
                Log.Bug("timeout_" + step, severity, "Điều kiện hoàn tất: " + step,
                    $"Timeout sau {Time.time - gameStart:F2} giây game / {Time.realtimeSinceStartup - realStart:F2} giây thật.");
                yield return Log.Checkpoint("timeout_" + step);
            }
        }

        public void AimAt(Vector3 point)
        {
            var d = point - Rig.cam.transform.position;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            Rig.look.SetAngles(yaw, pitch);
            Log.BotDriveFrame = Time.frameCount;
        }

        public IEnumerator Walk(string step, Vector3 goal, float tolerance = 0.25f, bool detour = true)
        {
            yield return Log.Checkpoint("walk_" + step);
            yield return WalkLeg(step, goal, tolerance, detour);
            if (Rig.Position.y > 0.12f)
            {
                // Ba đứng cao hơn sàn: ghi vật dưới chân (bước lên đồ thấp dưới stepOffset thì camera nhô lên).
                var under = new System.Collections.Generic.List<string>();
                foreach (var c in Physics.OverlapSphere(Rig.Position + Vector3.up * 0.1f, 0.5f, ~0, QueryTriggerInteraction.Ignore))
                    if (c.GetComponentInParent<HaiBanTay.Player.PlayerRig>() == null && c.bounds.max.y > 0.08f && c.bounds.max.y < 0.7f)
                        under.Add($"{c.name} (cha {c.transform.parent?.name}) cao tới {c.bounds.max.y:0.00}");
                // Vật thật đỡ capsule: cầu quét xuống từ ngang eo (bán kính capsule 0,28), tên và mặt trên chỗ chạm.
                string held = Physics.SphereCast(Rig.Position + Vector3.up * 0.8f, 0.27f, Vector3.down, out var down, 1.2f, ~0, QueryTriggerInteraction.Ignore)
                    ? $"{down.collider.name} (cha {down.collider.transform.parent?.name}, lớp {LayerMask.LayerToName(down.collider.gameObject.layer)}) chạm ở y {down.point.y:0.00}, người cách tâm {(new Vector3(down.point.x - Rig.Position.x, 0f, down.point.z - Rig.Position.z)).magnitude:0.00} m"
                    : "không có";
                Log.Note($"Ba đứng cao {Rig.Position.y:0.00} m tại {step}; dưới chân: {string.Join("; ", under)}; đỡ capsule: {held}.");
            }
            yield return Log.Checkpoint((Ok ? "arrived_" : "blocked_") + step);
        }

        IEnumerator WalkLeg(string step, Vector3 goal, float tolerance, bool detour)
        {
            float start = Time.time, realStart = Time.realtimeSinceStartup;
            float best = HorizontalDistance(Rig.Position, goal), progressAt = Time.time;
            Vector3 progressPosition = Rig.Position;
            bool detoured = false;
            int yieldsToGuard = 0, yieldsToNpc = 0;
            while (HorizontalDistance(Rig.Position, goal) > tolerance || Mathf.Abs(Rig.Position.y - goal.y) > 0.65f)
            {
                if (WorkInterrupted)
                {
                    ReleaseInput(); Ok = false;
                    Log.Note("Shift ended during walking; return to the evening UI flow.");
                    yield break;
                }
                if (Time.time - start > 180f || Time.realtimeSinceStartup - realStart > 240f) break;
                if (!Input.GameplayEnabled || Rig.motor.MovementLocked)
                {
                    Input.SimMove = Vector2.zero;
                    yield return Frame();
                    progressAt = Time.time;
                    continue;
                }
                var delta = goal - Rig.Position;
                Rig.look.SetAngles(Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg, 0f);
                Log.BotDriveFrame = Time.frameCount;
                Input.SimMove = new Vector2(0f, 1f);
                FeedBalance();
                yield return Frame();
                float distance = HorizontalDistance(Rig.Position, goal);
                if (best - distance > 0.08f || HorizontalDistance(Rig.Position, progressPosition) > 0.3f)
                {
                    best = Mathf.Min(best, distance); progressAt = Time.time; progressPosition = Rig.Position;
                }
                if (Time.time - progressAt < 5f) continue;
                Input.SimMove = Input.SimLook = Vector2.zero;
                // Xẻng phụ bếp đang cầm chắn (PlayerGuard) và phụ bếp đứng chờ Ba tránh: lùi ra nhường như người chơi thật, chờ phụ bếp đi qua.
                if (yieldsToGuard < 3 && GuardAhead(out var guard))
                {
                    yieldsToGuard++;
                    Log.Note($"Yield to NPC tool {step}: {guard.transform.parent?.name} at {guard.bounds.center}.");
                    // Lùi về hướng trống (sau, chéo sau, ngang): lùi thẳng có lúc vào bàn sơ chế (bot 23/10: yield_tool kẹt ở Top).
                    var away = Rig.Position - Rig.transform.forward * 0.7f;
                    foreach (float turn in new[] { 180f, 145f, -145f, 110f, -110f, 90f, -90f })
                    {
                        var dir = Quaternion.Euler(0f, turn, 0f) * Rig.transform.forward;
                        dir.y = 0f; dir.Normalize();
                        var feet = Rig.Position;
                        if (Physics.CapsuleCast(feet + Vector3.up * 0.4f, feet + Vector3.up * 1.45f, 0.3f, dir, 0.75f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                            continue;
                        away = feet + dir * 0.7f;
                        break;
                    }
                    yield return WalkLeg(step + "_yield_tool", away, 0.25f, false);
                    for (float w = 0f; w < 12f && guard.enabled && GuardNear(guard, 1.2f); w += Time.deltaTime) yield return Frame();
                    best = HorizontalDistance(Rig.Position, goal); progressAt = Time.time; progressPosition = Rig.Position;
                    continue;
                }
                // NPC đang đi hoặc đang bê đồ ngay trước mặt (phu bến vác bao trên cầu thang tàu; đồ cầm cắt bước Ba mà không có
                // collider): lùi nhường rồi chờ NPC đi qua như người chơi thật, tối đa 3 lần; lượt lùi không tự nhường tiếp.
                if (yieldsToNpc < 3 && !step.EndsWith("_yield_npc", StringComparison.Ordinal) && NpcMovingNear(1.6f, out var mover))
                {
                    yieldsToNpc++;
                    Log.Note($"Yield to NPC {step}: {mover.name} at {mover.Position}, bê {(mover.Carrying != null ? mover.Carrying.name : "-")}.");
                    var away = Rig.Position - Rig.transform.forward * 0.7f;
                    foreach (float turn in new[] { 180f, 145f, -145f, 110f, -110f, 90f, -90f })
                    {
                        var dir = Quaternion.Euler(0f, turn, 0f) * Rig.transform.forward;
                        dir.y = 0f; dir.Normalize();
                        var feet = Rig.Position;
                        if (Physics.CapsuleCast(feet + Vector3.up * 0.4f, feet + Vector3.up * 1.45f, 0.3f, dir, 0.75f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                            continue;
                        away = feet + dir * 0.7f;
                        break;
                    }
                    yield return WalkLeg(step + "_yield_npc", away, 0.25f, false);
                    for (float w = 0f; w < 10f && NpcMovingNear(1.4f, out _); w += Time.deltaTime) yield return Frame();
                    best = HorizontalDistance(Rig.Position, goal); progressAt = Time.time; progressPosition = Rig.Position;
                    continue;
                }
                string obstacle = "không rõ";
                if (Physics.SphereCast(Rig.Position + Vector3.up * 0.8f, 0.25f, Rig.transform.forward, out var hit, 1f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                    obstacle = hit.collider.name;
                {
                    // Evidence for triage: every collider (any layer) the capsule touches ahead, with pose.
                    var feet = Rig.Position;
                    foreach (var h in Physics.CapsuleCastAll(feet + Vector3.up * 0.4f, feet + Vector3.up * 1.45f, 0.3f, Rig.transform.forward, 0.6f, ~0, QueryTriggerInteraction.Ignore))
                        if (h.collider.GetComponentInParent<PlayerRig>() == null)
                            Log.Note($"Blocked-evidence {step}: {h.collider.name} layer={LayerMask.LayerToName(h.collider.gameObject.layer)} dist={h.distance:F2} bounds={h.collider.bounds} euler={h.collider.transform.eulerAngles} pos={h.collider.transform.position}.");
                }
                Log.Bug("blocked_" + step, "MAJOR", "Đi bộ tới " + goal + ".",
                    $"Không tiến thêm 5 giây game; vị trí {Rig.Position}, mục tiêu {goal}, vật chắn {obstacle}. Đây là ứng viên lỗi, cần loại trừ waypoint sai của bot."
                    // Đồ NPC cầm chặn bước (LimitPlayerStep) không có collider nên SphereCast không thấy: ghi NPC gần và số lần cắt bước.
                    + $" Đồ NPC cắt bước Ba {HaiBanTay.Npc.NpcAgent.PlayerStepLimits} lần (cộng dồn)." + PlaythroughLog.NpcNear());
                yield return Log.Checkpoint("no_progress_" + step);
                if (!detour || detoured) break;
                detoured = true;
                var from = Rig.Position;
                var side = Rig.transform.right;
                yield return WalkLeg(step + "_sidestep_right", from + side * 0.9f, 0.2f, false);
                if (!Ok) yield return WalkLeg(step + "_sidestep_left", from - side * 0.9f, 0.2f, false);
                best = HorizontalDistance(Rig.Position, goal); progressAt = Time.time; progressPosition = Rig.Position;
            }
            Input.SimMove = Input.SimLook = Vector2.zero;
            Ok = HorizontalDistance(Rig.Position, goal) <= tolerance && Mathf.Abs(Rig.Position.y - goal.y) <= 0.65f;
            if (!Ok) Log.Note($"Walk failed: {step}; position={Rig.Position}; goal={goal}; game={Time.time - start:F2}; real={Time.realtimeSinceStartup - realStart:F2}; " +
                $"locked={Rig.motor.MovementLocked} stance={Rig.InWorkStance} stepping={Rig.Stepping} cc={Rig.motor.GetComponent<CharacterController>().enabled} " +
                $"cap={Rig.motor.SpeedCap} control={Rig.ControlEnabled} input={GameRoot.Instance.Input.GameplayEnabled}.");
        }

        void FeedBalance()
        {
            // The same lean value is displayed by the HUD; compensate opposite to its displacement.
            Input.SimLook = _balance != null && _balance.Active ? new Vector2(-_balance.Logic.Lean * Mathf.Max(12f, Time.deltaTime * 750f), 0f) : Vector2.zero;
        }

        /// <summary>Hộp xẻng NPC đang cầm (NpcAgent.playerGuard, tên PlayerGuard) chắn ngay trước Ba.</summary>
        bool GuardAhead(out Collider guard)
        {
            guard = null;
            var feet = Rig.Position;
            foreach (var h in Physics.CapsuleCastAll(feet + Vector3.up * 0.4f, feet + Vector3.up * 1.45f, 0.3f, Rig.transform.forward, 0.6f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider.name == "PlayerGuard" && h.collider.enabled) { guard = h.collider; return true; }
            return false;
        }

        /// <summary>Đồ dài NPC đang cầm (NpcAgent.playerGuard) cách trục thân Ba dưới distance m.</summary>
        public bool NpcToolNear(float distance, out Collider guard)
        {
            guard = null;
            if (Time.time >= _agentsAt)
            {
                _agents = UnityEngine.Object.FindObjectsByType<HaiBanTay.Npc.NpcAgent>(FindObjectsSortMode.None);
                _agentsAt = Time.time + 2f;
            }
            foreach (var a in _agents)
                if (a != null && a.playerGuard != null && a.playerGuard.enabled && GuardNear(a.playerGuard, distance))
                { guard = a.playerGuard; return true; }
            return false;
        }

        HaiBanTay.Npc.NpcAgent[] _agents = new HaiBanTay.Npc.NpcAgent[0];
        float _agentsAt;

        bool GuardNear(Collider guard, float distance)
        {
            var q = Rig.Position + Vector3.up * 0.9f;
            return (guard.ClosestPoint(q) - q).sqrMagnitude < distance * distance;
        }

        /// <summary>NPC đang đi hoặc đang bê đồ trong bán kính (ngang, cùng tầng ±1 m) quanh Ba. Chỉ gọi khi bot đã đứng yên 5 s.</summary>
        bool NpcMovingNear(float radius, out HaiBanTay.Npc.NpcAgent npc)
        {
            npc = null;
            float bestSq = radius * radius;
            foreach (var n in UnityEngine.Object.FindObjectsByType<HaiBanTay.Npc.NpcAgent>(FindObjectsSortMode.None))
            {
                if (n == null || !n.isActiveAndEnabled || (!n.Walking && n.Carrying == null)) continue;
                var d = n.Position - Rig.Position;
                if (Mathf.Abs(d.y) > 1f) continue;
                float sq = d.x * d.x + d.z * d.z;
                if (sq < bestSq) { bestSq = sq; npc = n; }
            }
            return npc != null;
        }

        public static float HorizontalDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        static Vector3 V(float x, float z, float y = 0f) => new Vector3(x, y, z);

        /// <summary>Trong phòng rửa mới (x 3,35…7, z −3…0,9, trên sàn bếp, không phải hầm lạnh).</summary>
        static bool InWash(Vector3 v) => v.x > 3.4f && v.z > -3f && v.z < 0.9f && v.y >= -0.45f;

        /// <summary>Chỗ đứng lấy/treo cào: cách chỗ đặt xô tro (−3; 1,62) ≥ 0,5 m. Chỗ cũ (−3,2; 1,5) chỉ cách 0,24 m nên capsule
        /// (bậc 0,3 m) trèo lên xô cao 0,32 m, đứng trên xô mà cào thì tia nhìn vướng móc (bot ngày 1 dừng ở D1.dawn, 17/10).</summary>
        public static readonly Vector3 RakeHookStand = new Vector3(-3.45f, 0f, 1.35f);
        /// <summary>Chỗ đứng lấy bàn chải ngắn ở kệ cạnh chậu (kệ ở (6,35; 0,87; −2,26)).</summary>
        public static readonly Vector3 BrushShelfStand = new Vector3(5.95f, 0f, -1.85f);
        /// <summary>Chỗ đứng lấy bàn chải cán dài ở móc vách ngăn (3,43; −0,62).</summary>
        public static readonly Vector3 LongBrushStand = new Vector3(4.0f, 0f, -0.62f);

        public IEnumerator Travel(string step, Vector3 goal)
        {
            // Fixed waypoints describe the shipped layout, including its real doorways and stairs.
            // ponytail: these waypoints fit the shipped map; update them when scene geometry changes.
            var p = Rig.Position;
            // Phòng ăn nhân viên (cửa x −1,35…−0,45) và boong tây ngoài cửa bếp (z 2,15…3,05) chỉ có ở 02.
            bool galley = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "02_Galley";
            bool InDining(Vector3 v) => galley && v.z < -3f && v.x < 0.3f;
            bool OnWestDeck(Vector3 v) => galley && (v.x < -5.05f || v.y > 1f);
            if ((InDining(p) && InDining(goal)) || (OnWestDeck(p) && OnWestDeck(goal)))
            { yield return Walk(step + "_same_room", goal); yield break; }
            if (InDining(p))
            {
                foreach (var point in new[] { V(-0.9f, -3.6f), V(-0.9f, -2.4f) })
                { yield return Walk(step + "_dining_exit", point); if (!Ok) yield break; }
                p = Rig.Position;
            }
            else if (OnWestDeck(p))
            {
                var exit = p.y > 1f
                    ? new[] { V(-5.164f, 4.8f, 2.85f), V(-9.95f, 4.8f), V(-9.9f, 3.7f), V(-8.2f, 2.4f), V(-5.6f, 2.6f), V(-4.75f, 2.6f), V(-4.3f, 2.25f), V(-3.3f, 1.6f) }
                    : new[] { V(-5.6f, 2.6f), V(-4.75f, 2.6f), V(-4.3f, 2.25f), V(-3.3f, 1.6f) };
                foreach (var point in exit)
                { yield return Walk(step + "_deck_exit", point, 0.3f); if (!Ok) yield break; }
                p = Rig.Position;
            }
            if (InDining(goal))
            {
                yield return MainRoomPath(step, V(-0.9f, -2.4f)); if (!Ok) yield break;
                yield return Walk(step + "_dining_entry", V(-0.9f, -3.6f), 0.15f); if (!Ok) yield break;
                yield return Walk(step + "_stand", goal); yield break;
            }
            if ((p.y < -0.45f && goal.y < -0.45f) || (p.z < -3f && goal.z < -3f) ||
                (InWash(p) && InWash(goal)))
            { yield return Walk(step + "_same_room", goal); yield break; }
            if (p.y < -0.45f)
            {
                foreach (var point in new[] { V(8.1f, 1.5f, -1.2f), V(7.35f, 1.5f, -1.2f), V(6.5f, 1.5f, -0.65f), V(5.7f, 1.5f, -0.2f), V(4.75f, 1.5f) })
                { yield return Walk(step + "_cold_exit", point); if (!Ok) yield break; }
            }
            else if (p.z < -3.0f)
            {
                foreach (var point in new[] { V(1.45f, -3.35f), V(1.45f, -2.65f) })
                { yield return Walk(step + "_coal_exit", point); if (!Ok) yield break; }
            }
            else if (InWash(p))
            {
                // Cửa vách ngăn rộng 1,2 m, z −0,3…0,9: đi qua giữa cửa rồi ra trước vách.
                foreach (var point in new[] { V(4.0f, 0.3f), V(2.7f, 0.3f) })
                { yield return Walk(step + "_wash_exit", point); if (!Ok) yield break; }
            }
            bool cold = goal.y < -0.45f, coal = goal.z < -3f, wash = !cold && !coal && InWash(goal);
            Vector3 entrance = cold ? V(4.75f, 1.5f) : coal ? V(1.45f, -2.65f) : wash ? V(2.7f, 0.3f) : goal;
            yield return MainRoomPath(step, entrance);
            if (!Ok) yield break;
            if (cold)
            {
                foreach (var point in new[] { V(5.25f, 1.5f), V(5.7f, 1.5f, -0.2f), V(6.5f, 1.5f, -0.65f), V(7.35f, 1.5f, -1.2f), V(8.1f, 1.5f, -1.2f) })
                { yield return Walk(step + "_cold_entry", point); if (!Ok) yield break; }
            }
            if (coal)
                foreach (var point in new[] { V(1.45f, -3.45f) })
                { yield return Walk(step + "_coal_entry", point); if (!Ok) yield break; }
            if (wash)
            { yield return Walk(step + "_wash_entry", V(4.0f, 0.3f)); if (!Ok) yield break; }
            if (cold || coal || wash) yield return Walk(step + "_stand", goal);
        }

        IEnumerator MainRoomPath(string step, Vector3 goal)
        {
            bool crossesCrewLane = Rig.Position.x * goal.x < 0f && Mathf.Min(Rig.Position.z, goal.z) > 0.65f;
            if (!crossesCrewLane && ClearWalk(Rig.Position, goal))
            { yield return Walk(step + "_direct", goal); if (Ok) yield break; }
            // Cross the room south of the prep table. The north crossing is the cooks' route.
            // Điểm thoát góc (-3,05; 1,15) tránh xô tro, ghế nghỉ và mép bàn buổi tối.
            var nodes = new[] { V(-3.3f, -1.05f), V(-3.05f, 1.15f), V(2.6f, -0.85f), V(2.7f, 1f), V(-3.7f, 0.1f), V(2.7f, 0.3f), V(4.45f, 1.5f), V(0f, -1.15f), V(1.45f, -2.65f) };
            int[,] edges = { {0,1}, {0,7}, {7,2}, {2,3}, {0,4}, {1,4}, {2,5}, {3,5}, {3,6}, {2,8}, {7,8} };
            var fromNodes = Enumerable.Range(0, nodes.Length).Where(i => ClearWalk(Rig.Position, nodes[i])).ToArray();
            var toNodes = Enumerable.Range(0, nodes.Length).Where(i => ClearWalk(nodes[i], goal)).ToArray();
            int start = (fromNodes.Length > 0 ? fromNodes : Enumerable.Range(0, nodes.Length)).OrderBy(i => HorizontalDistance(Rig.Position, nodes[i])).First();
            int end = (toNodes.Length > 0 ? toNodes : Enumerable.Range(0, nodes.Length)).OrderBy(i => HorizontalDistance(goal, nodes[i])).First();
            var distance = Enumerable.Repeat(float.PositiveInfinity, nodes.Length).ToArray();
            var previous = Enumerable.Repeat(-1, nodes.Length).ToArray();
            var visited = new bool[nodes.Length];
            distance[start] = 0f;
            for (int pass = 0; pass < nodes.Length; pass++)
            {
                int at = -1;
                for (int i = 0; i < nodes.Length; i++)
                    if (!visited[i] && (at < 0 || distance[i] < distance[at])) at = i;
                if (at < 0 || float.IsInfinity(distance[at])) break;
                visited[at] = true;
                for (int edge = 0; edge < edges.GetLength(0); edge++)
                {
                    int next = edges[edge, 0] == at ? edges[edge, 1] : edges[edge, 1] == at ? edges[edge, 0] : -1;
                    if (next < 0) continue;
                    float candidate = distance[at] + HorizontalDistance(nodes[at], nodes[next]);
                    if (candidate < distance[next]) { distance[next] = candidate; previous[next] = at; }
                }
            }
            var route = new System.Collections.Generic.List<int>();
            for (int at = end; at >= 0; at = previous[at]) route.Add(at);
            route.Reverse();
            foreach (int at in route) { yield return Walk(step + "_route_" + at, nodes[at]); if (!Ok) yield break; }
            yield return Walk(step + "_main_destination", goal);
        }

        bool ClearWalk(Vector3 from, Vector3 to)
        {
            var delta = to - from; delta.y = 0f;
            if (delta.magnitude < 0.05f) return true;
            return !Physics.CapsuleCastAll(from + Vector3.up * 0.4f, from + Vector3.up * 1.45f,
                0.3f, delta.normalized, delta.magnitude, ~(1 << 2), QueryTriggerInteraction.Ignore)
                .Any(hit => hit.collider.GetComponentInParent<PlayerRig>() == null);
        }

        public IEnumerator Focus(Component target, string step, string expectedKey = null)
        {
            Ok = false;
            if (WorkInterrupted) { ReleaseInput(); Log.Note("Shift ended before aiming: " + step); yield break; }
            if (target == null) { Log.Note("Missing target: " + step); yield break; }
            var provider = target as IPromptProvider;
            var colliders = target.GetComponentsInChildren<Collider>(true)
                .Where(c => c.enabled && c.gameObject.activeInHierarchy && ReferenceEquals(c.GetComponentInParent<IPromptProvider>(), provider))
                .OrderBy(c => Vector3.Distance(Rig.cam.transform.position, c.bounds.center)).ToArray();
            if (colliders.Length == 0) { Log.Note("No active collider for " + target.name); yield break; }
            float started = Time.realtimeSinceStartup;
            foreach (var c in colliders)
            {
                float end = Time.realtimeSinceStartup + 3f, gameEnd = Time.time + 8f;
                while (Time.realtimeSinceStartup < end && Time.time < gameEnd)
                {
                    if (WorkInterrupted) { ReleaseInput(); Log.Note("Shift ended while aiming: " + step); yield break; }
                    AimAt(c.bounds.center);
                    yield return Frame();
                    var who = Rig.interactor;
                    string key = who.Current?.GetPromptKey(who);
                    if (!ReferenceEquals(who.Current, provider) || string.IsNullOrEmpty(key)) continue;
                    yield return Frame();
                    if (!ReferenceEquals(who.Current, provider)) continue;
                    if (string.IsNullOrEmpty(Log.VisiblePrompt)) continue;
                    if (expectedKey != null && key != expectedKey)
                        Log.Bug("wrong_prompt_key_" + target.name + "_" + expectedKey, "MAJOR", "Prompt " + expectedKey + ".", "Prompt thực tế " + key + ".");
                    string expectedText = GameRoot.Instance.Content.GetString(key);
                    if (Log.VisiblePrompt != expectedText)
                        Log.Bug("prompt_override_" + target.name, "MAJOR", "Chữ hướng dẫn ứng với " + key + ": " + expectedText, "Chữ đang hiển thị: " + Log.VisiblePrompt);
                    yield return Log.Checkpoint("aim_" + step);
                    // Capturing advances a frame. Re-aim and verify the live scan before sending input.
                    AimAt(c.bounds.center);
                    yield return Frame();
                    if (!ReferenceEquals(who.Current, provider) || who.Current.GetPromptKey(who) != key || string.IsNullOrEmpty(Log.VisiblePrompt)) continue;
                    Ok = true;
                    yield break;
                }
            }
            Log.Note($"Focus unsuccessful: target={target.name}; current={(Rig.interactor.Current as Component)?.name}; hit={Rig.interactor.LookCollider?.name}; position={Rig.Position}; waited={Time.realtimeSinceStartup - started:F2}s.");
        }

        public IEnumerator Press(Component target, string step, Func<bool> effect, string expectedKey = null, float effectSeconds = 20f)
        {
            if (effect()) { Ok = true; Log.Note("Already complete before E: " + step); yield break; }
            yield return Focus(target, step, expectedKey);
            if (!Ok) { yield return MissingPrompt(target, step); yield break; }
            Log.Note("Input E: " + step);
            Input.SimulateInteract();
            yield return Frame();
            Log.Note("After E: " + step + "; effect=" + effect());
            yield return Wait("effect_" + step, effect, effectSeconds, effectSeconds + 10f);
            yield return Log.Checkpoint("pressed_" + step);
        }

        public IEnumerator MissingPrompt(Component target, string step)
        {
            if (WorkInterrupted) { ReleaseInput(); Ok = false; Log.Note("Station closed at shift end: " + step); yield break; }
            Log.Bug("missing_prompt_" + step, "BLOCKER", "Nhìn collider của " + target?.name + " trong tầm 2 m thấy hướng dẫn.",
                $"Không có prompt đúng mục tiêu. Tia hiện trúng {Rig.interactor.LookCollider?.name}; vị trí {Rig.Position}. Cần xác minh target có bị che hoặc bot đứng sai phía.");
            Ok = false;
            yield return Log.Checkpoint("missing_prompt_" + step);
        }

        /// <param name="stallSeconds">Tiến độ đứng yên chừng này giây thì thôi giữ.</param>
        /// <param name="partial">Thôi giữ khi chững mà chưa xong không phải lỗi (người gọi đổi cách làm, vd xoay nồi rồi cọ tiếp).</param>
        public IEnumerator Hold(Component target, string step, Func<bool> done, Func<float> progress, Func<Vector2> delta, string expectedKey, bool release = true,
            float stallSeconds = 25f, bool partial = false, float minGain = 0.005f)
        {
            if (done()) { Ok = true; Log.Note("Already complete before hold: " + step); yield break; }
            yield return Focus(target, step, expectedKey);
            if (!Ok) { yield return MissingPrompt(target, step); yield break; }
            float gameStart = Time.time, realStart = Time.realtimeSinceStartup, progressedAt = Time.time;
            float before = progress();
            Input.SimUse = true;
            Input.SimLook = Vector2.zero;
            yield return Frame();
            while (!done() && Time.time - gameStart < 180f && Time.realtimeSinceStartup - realStart < 240f)
            {
                if (WorkInterrupted) break;
                Input.SimLook = delta();
                yield return Frame();
                float now = progress();
                if (Mathf.Abs(now - before) > minGain) { before = now; progressedAt = Time.time; }
                if (Time.time - progressedAt > stallSeconds) break;
            }
            Ok = done();
            if (partial && !Ok && !WorkInterrupted)
            {
                Input.SimUse = false;
                Input.SimLook = Vector2.zero;
                Ok = true;
                Log.Note($"Hold stalled, partial: {step}; progress={progress():F3}");
                yield return Log.Checkpoint("stalled_" + step);
                yield break;
            }
            if (release || !Ok) Input.SimUse = false;
            Input.SimLook = Vector2.zero;
            if (!Ok && WorkInterrupted) { Log.Note("Shift ended during holding: " + step); yield break; }
            if (!Ok)
                Log.Bug("hold_no_effect_" + step, "BLOCKER", "Làm đúng hướng dẫn đang hiển thị thì hoàn tất thao tác.",
                    $"Giữ chuột, delta={delta()}; progress={progress():F3}; {Time.time - gameStart:F2}s game/{Time.realtimeSinceStartup - realStart:F2}s thật, thao tác chưa xong.");
            yield return Log.Checkpoint("hold_" + step);
        }

        public IEnumerator FreeHands()
        {
            ReleaseInput();
            if (WorkInterrupted) { Ok = false; Log.Note("Shift ended before changing held objects."); yield break; }
            Ok = true;
            if (Rig.interactor.Holding != null)
            {
                // Dropping is a real E action. Never release a held object by calling the interactor directly.
                Rig.look.SetAngles(Rig.look.Yaw, -60f);
                Log.BotDriveFrame = Time.frameCount;
                yield return Frame();
                var held = Rig.interactor.Holding;
                Input.SimulateInteract();
                yield return Wait("drop_" + held.name, () => Rig.interactor.Holding != held, 12f, 20f);
                yield return Log.Checkpoint("drop_" + held.name);
                if (!Ok) yield break;
            }
            if (Rig.interactor.Tool != Tool.None)
            {
                var hook = ToolHook.For(Rig.interactor.Tool);
                if (hook != null)
                {
                    var stand = hook.tool == Tool.Rake ? RakeHookStand : hook.tool == Tool.Shovel ? V(1.1f, 1.75f)
                        : hook.tool == Tool.Brush ? BrushShelfStand : hook.tool == Tool.LongBrush ? LongBrushStand : V(2.6f, -1.3f);
                    yield return Travel("hang_" + hook.name, stand);
                    if (!Ok) yield break;
                    yield return Press(hook, "hang_" + hook.name, () => Rig.interactor.Tool == Tool.None);
                }
                else Fail("No hook available to return the held tool.");
            }
        }

        // Việc mới vẫn dùng E/chuột của GameInput. Không gọi OnInteract hoặc SimulateWork.
        public IEnumerator TakeStoryLoad(VoyageDelivery task)
        {
            yield return Focus(task.load, task.Kind + "_pick");
            if (!Ok) yield return Approach(task.load, task.Kind + "_pick", task.pickupStand);
            if (!Ok) yield break;
            yield return Press(task.load, task.Kind + "_pick", () => Rig.interactor.Holding == task.load);
        }

        public IEnumerator PlaceStoryLoad(VoyageDelivery task)
        {
            int delivered = task.Delivered;
            yield return Press(task.destination, task.Kind + "_place", () => task.Placed || task.Delivered > delivered, "prompt.story_place");
            if (!Ok) yield break;
            if (task.pour)
                yield return Hold(task.destination, task.Kind + "_pour", () => task.Delivered > delivered, () => task.Progress,
                    () => Vector2.zero, "prompt.story_pour");
            else yield return Wait(task.Kind + "_delivered", () => task.Delivered > delivered, 5f, 10f);
        }

        public IEnumerator Peel(PeelStation task)
        {
            int pending = task.Pending;
            for (int attempt = 0; attempt < 2 && task.Pending == pending; attempt++)
            {
                if (!task.Ready)
                {
                    yield return Press(task.board, "peel_take", () => task.Ready || task.Demonstrating, "prompt.peel_take");
                    if (!Ok) yield break;
                }
                yield return Wait("mai_demonstration", () => task.Ready && !task.Demonstrating, 40f, 60f);
                if (!Ok) yield break;
                yield return Focus(task.board, "peel_drag", "prompt.peel_drag");
                if (!Ok) yield break;
                yield return Hold(task.board, "peel_along_vegetable", () => task.Pending < pending || task.Demonstrating,
                    () => task.Progress, () => new Vector2(0f, Time.frameCount % 20 < 10 ? 15f : -15f), "prompt.peel_drag");
                if (!Ok) yield break;
                if (task.Demonstrating) yield return Wait("asparagus_retry", () => task.Ready && !task.Demonstrating, 40f, 60f);
            }
            if (Ok) yield return Wait("vegetable_peeled", () => task.Pending < pending, 5f, 10f);
        }

        public IEnumerator Encounter(VoyageEncounter task)
        {
            int pending = task.Pending;
            yield return Press(task.target, task.Kind + "_begin", () => task.Busy, task.Kind != OrderKinds.Soup ? "prompt.chieu" : task.seat != null ? "prompt.soup_sit" : "prompt.soup_fork");
            if (!Ok) yield break;
            yield return Wait(task.Kind + "_spoken", () => !task.Busy, 90f, 120f);
            if (!Ok) yield break;
            if (task.Kind == OrderKinds.Soup)
            {
                yield return Press(task.target, "soup_spoon", () => task.Busy, "prompt.soup_spoon");
                if (!Ok) yield break;
            }
            yield return Wait(task.Kind + "_completed", () => task.Pending < pending, 30f, 45f);
        }

        public IEnumerator Approach(Component target, string step, Vector3? stand = null)
        {
            if (WorkInterrupted) { ReleaseInput(); Ok = false; Log.Note("Shift ended before approaching: " + step); yield break; }
            if (target == null) { Ok = false; Log.Note("Missing approach target " + step); yield break; }
            var collider = target.GetComponentsInChildren<Collider>().FirstOrDefault(c => c.enabled && c.gameObject.activeInHierarchy);
            var center = collider != null ? collider.bounds.center : target.transform.position;
            var towardsPlayer = Rig.Position - center;
            towardsPlayer.y = 0f;
            if (towardsPlayer.sqrMagnitude < 0.01f) towardsPlayer = -Rig.transform.forward;
            var goal = stand ?? center + towardsPlayer.normalized * 1.05f;
            if (!stand.HasValue) goal.y = Rig.Position.y;
            yield return Walk(step, goal, 0.2f);
            // A blocked preferred standing point can still leave the object in reach.
            yield return Focus(target, step);
            if (Ok) yield break;
            // Check adjacent reachable viewing positions before diagnosing a missing prompt.
            for (int side = -1; side <= 1; side += 2)
            {
                var offset = Vector3.Cross(Vector3.up, towardsPlayer.normalized) * (0.45f * side);
                yield return Walk(step + "_view_side_" + side, goal + offset, 0.2f);
                if (!Ok) continue;
                yield return Focus(target, step);
                if (Ok) yield break;
            }
            yield return MissingPrompt(target, step);
        }
    }
}
