using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Narrative;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Ship;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace HaiBanTay.Tests
{
    // Observations never change game state. Candidates need human/code triage before becoming confirmed bugs.
    public sealed class PlaythroughLog : IDisposable
    {
        [Serializable]
        public sealed class Row
        {
            public string run, type, step, scene, beat, clock, target, collider, promptKey, visiblePromptKey, promptText, hint, screenshot, holding, tool;
            public string severity, issue, expected, actual, cause, message, stack;
            public int frame, day, lampChecks, panNext, panDelivered, panSlips;
            public float gameTime, realTime, deltaTime, unscaledDeltaTime, timeScale;
            public Vector3 position, cameraPosition, cameraAngles, heldPosition, ashBucketPosition, ashBucketBodyPosition, panPosition;
            public bool gameplayEnabled, ashBucketReady, ashBucketHeld, ashBucketKinematic, lampRotationsFinite, panGripping;
            public OrderRow[] orders;
            public string[] hud;
        }

        [Serializable]
        public sealed class OrderRow
        {
            public string kind;
            public int need, done;
            public bool extra;
        }

        sealed class Prop
        {
            public Transform root;
            public Renderer[] renderers;
            public float floatingSince = -1f;
        }

        public static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Logs", "playthrough"));
        // Mỗi lần chạy có ảnh/log riêng, không ghi đè bằng chứng ngày 1 khi kiểm ngày 4.
        public static readonly string RunFolderName = "polish7_" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        public static string ImageFolder => Path.Combine(Folder, RunFolderName);
        public readonly string Run = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        public string Step { get; private set; } = "setup";
        public bool HasBlocker { get; private set; }
        public readonly List<Row> Findings = new List<Row>();

        readonly StreamWriter _writer;
        readonly Queue<Row> _pending = new Queue<Row>();
        readonly HashSet<string> _seen = new HashSet<string>();
        readonly List<string> _history = new List<string>();
        readonly List<Prop> _props = new List<Prop>();
        readonly RaycastHit[] _supportHits = new RaycastHit[32];
        readonly PlaythroughFrameObserver _observer;
        Label _prompt, _hint;
        PlayerRig _lastRig;
        Holdable _lastHeld;
        Vector3 _lastPosition;
        float _heldSince, _groundedY;
        float _promptMismatchSince;
        string _promptMismatch;
        float _timelineAt = -30f;
        string _timelineScene, _timelineBeat;
        SwingingLamp[] _lamps = Array.Empty<SwingingLamp>();
        int _lampChecks;
        bool _lampsFinite = true;
        int _nextShot, _propIndex, _lastObservedFrame = -1;

        // W1 (PRODUCTION_PLAN mục 6, bảng "Cách đo"): camera nhảy khi đứng yên không thao tác, khóa bật/tắt liên tục, ca
        // không tiến triển. Ngưỡng WARN ghi MINOR để xem; ngưỡng ERROR ghi MAJOR (bài bot đỏ).
        /// <summary>Khung bot vừa tự đặt góc nhìn (nhắm, quay người khi đi): camera đổi vì bot, không tính là nhảy.</summary>
        public int BotDriveFrame { get; set; } = -10;
        const string CameraCause = "Ai đang điều khiển camera ở khung này? Đối chiếu LookController, khóa camera của station, ExtraHeadHeight, PlayerRig.StepToward/MoveTo.";
        const string LockCause = "Station nào đang bật/tắt khóa theo tia nhìn mỗi khung? Dùng chốt giữ (latch) như PanStation/WashStation sau W0.";
        PlayerRig _camRig, _lockRig;
        string _camScene, _progressKey;
        int _camFrame = -10;
        Vector3 _camPos, _camFeet;
        Quaternion _camRot;
        bool _camSeated, _lookLocked, _moveLocked, _useHeld;
        int _useEdgeFrame = -10;
        float _progressSince = -1f;
        readonly Queue<float> _lockFlips = new Queue<float>();
        public int CameraSamples { get; private set; }
        // NPC trong vật: thân NPC đang đi chồng collider tĩnh (bán kính trừ 2 cm), lấy mẫu 10 Hz.
        NpcAgent[] _npcs = Array.Empty<NpcAgent>();
        string _npcScene;
        float _npcSampleAt, _npcListAt;
        readonly Dictionary<NpcAgent, float> _npcInside = new Dictionary<NpcAgent, float>();
        readonly Collider[] _npcHits = new Collider[16];
        public int NpcSamples { get; private set; }
        // Xuyên thân NPC (BodyClipDetector): NPC đang hiện trong 8 m quanh camera, cùng nhịp 10 Hz.
        readonly BodyClipDetector _clip = new BodyClipDetector();
        readonly BodyClipTrack _clipTrack = new BodyClipTrack();
        readonly List<NpcAgent> _clipNpcs = new List<NpcAgent>();
        readonly List<BodyClipDetector.Contact> _clipContacts = new List<BodyClipDetector.Contact>();
        float _clipAt = -1f;
        public int BodyClipSamples => _clipTrack.Samples;
        const string ClipCause = "Đồ gắn vào điểm bê cố định theo gốc NPC (CarryPoint) không theo xương tay/vai khi clip vung? Clip dùng Avatar của Cook " +
                                 "trên thân khác cỡ? Chạy BodyClipSweep để có ảnh cận ở Logs/shots/bodyclip/.";

        // Tay, thân, dụng cụ, đồ bê của Ba lún vào mesh cảnh (PlayerClipDetector), mỗi khung, mọi cảnh bot đi qua.
        readonly PlayerClipProbe _playerClip;
        public PlayerClipTrack PlayerClips => _playerClip != null ? _playerClip.Track : null;
        const string PlayerClipCause = "Điểm cầm/chỗ đứng của trạm đặt tay hay đồ xuyên qua vật (mép chậu, vành nồi, bàn), hoặc thân lùi sau mắt " +
                                       "chạm vách. Sửa ở anchor/chỗ đứng của trạm; ảnh cận ở Logs/shots/playerclip/, bảng ở Logs/qa/player_clip_bot_*.md.";

        public PlaythroughLog()
        {
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory(ImageFolder);
            _writer = new StreamWriter(Path.Combine(Folder, "run_log_" + RunFolderName + ".jsonl"), true, new UTF8Encoding(false)) { AutoFlush = true };
            foreach (var path in Directory.GetFiles(ImageFolder, "*.jpg"))
            {
                var prefix = Path.GetFileName(path).Split('_')[0];
                if (int.TryParse(prefix, out int number)) _nextShot = Math.Max(_nextShot, number);
            }
            Application.logMessageReceived += OnUnityLog;
            _observer = new GameObject("Playthrough frame observations").AddComponent<PlaythroughFrameObserver>();
            _observer.Log = this;
            UnityEngine.Object.DontDestroyOnLoad(_observer.gameObject);
            _playerClip = PlayerClipProbe.Attach(shots: true);
            _playerClip.Label = () => Step;
            _playerClip.OnMajor = f => Bug("player_clip_" + f.kind + "_" + f.part + "_" + f.obj, "MAJOR",
                "Tay, thân, dụng cụ, đồ bê của Ba không xuyên vật quá ngưỡng liền 0,2 s (cánh tay 2 cm, bàn tay 3 cm, tay nắm 8 cm, dụng cụ 4 cm, đồ bê 3 cm, thân 5 cm).",
                f.Line + NpcNear(), PlayerClipCause);
            var start = Snapshot("run_start");
            start.message = "build_id " + BuildInfo.Id;
            Write(start);
        }

        /// <summary>
        /// Chẩn đoán lún vào đồ NPC (QA-0151: ngực Ba vào bao vác vai của phu, bot không ghi NPC nào): hai NPC gần Ba nhất trong 2,5 m với
        /// vị trí, hướng, trạng thái đi (NpcAgent.WalkDiag: chờ, xuyên vật, nhường Ba, khe đồ↔Ba).
        /// </summary>
        internal static string NpcNear()
        {
            var rig = PlayerRig.Current;
            if (rig == null) return "";
            var near = UnityEngine.Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None)
                .Where(n => n != null && n.isActiveAndEnabled && (n.Position - rig.Position).sqrMagnitude < 2.5f * 2.5f)
                .OrderBy(n => (n.Position - rig.Position).sqrMagnitude).Take(2).ToArray();
            if (near.Length == 0) return " · không có NPC trong 2,5 m";
            return string.Concat(near.Select(n =>
                $" · NPC {n.name} @{n.Position} cách {Vector3.Distance(n.Position, rig.Position):F2} m, yaw {n.transform.eulerAngles.y:F0}, Ba yaw {rig.transform.eulerAngles.y:F0}: {n.WalkDiag()}"));
        }

        public void BindScene()
        {
            var ui = GameRoot.Instance?.UI?.Root;
            _prompt = ui?.Q<Label>(className: "hbt-prompt");
            _hint = ui?.Q<Label>(className: "hbt-hint");
            _props.Clear();
            _lamps = UnityEngine.Object.FindObjectsByType<SwingingLamp>(FindObjectsSortMode.None);
            foreach (var held in UnityEngine.Object.FindObjectsByType<Holdable>(FindObjectsSortMode.None))
                if (held.kind == "sack" || held.kind == "ash_bucket" || held.name.IndexOf("bucket", StringComparison.OrdinalIgnoreCase) >= 0)
                    AddProp(held.transform);
            var shift = SceneDirector.Current as ShiftDirector;
            if (shift != null)
            {
                foreach (var delivery in shift.storyStations.OfType<VoyageDelivery>()) AddProp(delivery.load.transform);
                AddProp(shift.tall != null ? shift.tall.potRoot : null);
                AddProp(shift.pan != null && shift.pan.pan != null ? shift.pan.pan.transform : null);
                if (shift.wash != null)
                {
                    AddProp(shift.wash.basinPot != null ? shift.wash.basinPot.transform : null);
                    foreach (var pot in shift.wash.rackVisuals) AddProp(pot != null ? pot.transform : null);
                }
                if (shift.pots != null) foreach (var pot in shift.pots.pots) AddProp(pot != null ? pot.transform : null);
            }
            AddProp(PlayerRig.Current?.hands?.shovelLoad?.transform);
            _lastRig = null;
        }

        void AddProp(Transform root)
        {
            if (root == null || _props.Any(p => p.root == root)) return;
            _props.Add(new Prop { root = root, renderers = root.GetComponentsInChildren<Renderer>(true) });
        }

        public string VisiblePrompt => _prompt != null && _prompt.resolvedStyle.display != DisplayStyle.None ? _prompt.text : "";

        public Row Snapshot(string type)
        {
            var root = GameRoot.Instance;
            var rig = PlayerRig.Current;
            var who = rig != null ? rig.interactor : null;
            var shift = SceneDirector.Current as ShiftDirector;
            return new Row
            {
                run = Run, type = type, step = Step, scene = SceneManager.GetActiveScene().name,
                beat = SceneDirector.Current?.Beat ?? "", day = shift?.Day?.Number ?? 0,
                clock = shift?.Clock?.Text ?? "", frame = Time.frameCount, gameTime = Time.time,
                realTime = Time.realtimeSinceStartup, position = rig != null ? rig.Position : Vector3.zero,
                deltaTime = Time.deltaTime, unscaledDeltaTime = Time.unscaledDeltaTime,
                timeScale = Time.timeScale, lampChecks = _lampChecks, lampRotationsFinite = _lampsFinite,
                cameraPosition = rig != null ? rig.cam.transform.position : Vector3.zero,
                cameraAngles = rig != null ? rig.cam.transform.eulerAngles : Vector3.zero,
                target = (who?.Current as Component)?.name ?? "", collider = who?.LookCollider != null ? who.LookCollider.name : "",
                promptKey = who?.Current?.GetPromptKey(who) ?? "", promptText = VisiblePrompt,
                holding = who?.Holding != null ? who.Holding.name : "", tool = who?.Tool.ToString() ?? "",
                heldPosition = who?.Holding != null ? who.Holding.transform.position : Vector3.zero,
                ashBucketPosition = shift?.ash?.bucket != null ? shift.ash.bucket.transform.position : Vector3.zero,
                ashBucketBodyPosition = shift?.ash?.bucket?.Body != null ? shift.ash.bucket.Body.position : Vector3.zero,
                ashBucketReady = shift?.ash != null && shift.ash.BucketReady,
                ashBucketHeld = shift?.ash?.bucket != null && shift.ash.bucket.IsHeld,
                ashBucketKinematic = shift?.ash?.bucket?.Body != null && shift.ash.bucket.Body.isKinematic,
                panPosition = shift?.pan?.pan != null ? shift.pan.pan.position : Vector3.zero,
                panNext = shift?.pan?.Next ?? -1, panDelivered = shift?.pan?.Delivered ?? 0,
                panSlips = shift?.pan?.Tow.Slips ?? 0, panGripping = shift?.pan?.Tow.Gripping ?? false,
                visiblePromptKey = root?.Content?.Ui?.Strings.FirstOrDefault(s => s.Value == VisiblePrompt).Key ?? "",
                hint = _hint != null && _hint.resolvedStyle.display != DisplayStyle.None ? _hint.text : "",
                gameplayEnabled = root != null && root.Input.GameplayEnabled,
                orders = shift?.Board.Orders.Select(o => new OrderRow { kind = o.Kind, need = o.Need, done = o.Done, extra = o.Extra }).ToArray() ?? Array.Empty<OrderRow>(),
                hud = root?.UI?.Shift?.Root?.Query<Label>().ToList().Select(l => l.text).ToArray() ?? Array.Empty<string>()
            };
        }

        void Write(Row row) => _writer.WriteLine(JsonUtility.ToJson(row));

        public void Note(string message)
        {
            var row = Snapshot("diagnostic");
            row.message = message;
            Write(row);
        }

        public IEnumerator Checkpoint(string step)
        {
            Step = step;
            _history.Add(step);
            Write(Snapshot("step"));
            yield return FlushFindings();
        }

        /// <summary>Chụp ảnh tại mốc đáng xem (không đổi trạng thái game).</summary>
        public IEnumerator Shot(string name)
        {
            Step = name;
            var row = Snapshot("shot");
            yield return Capture(row);
        }

        IEnumerator Capture(Row row)
        {
            // Chạy -nographics (run_tests.sh): không có thiết bị đồ họa để chụp; chỉ ghi dòng log (vẽ camera lúc này làm Unity sập).
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Write(row);
                yield break;
            }
            string slug = new string((row.step ?? "observation").Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            if (slug.Length > 90) slug = slug.Substring(0, 90);
            string stem = $"{++_nextShot:D3}_{slug}";
            row.screenshot = RunFolderName + "/" + stem + ".jpg";
            // An absolute name keeps the existing capture helper's output inside this run's permitted directory.
            // Logs/shots must already exist, because its Folder getter otherwise creates an out-of-scope directory.
            string temporary = Path.Combine(ImageFolder, stem + "_capture");
            yield return ScreenshotCapture.Capture(temporary);
            // Reuse the existing camera/UI capture, then downsample with Unity's native texture pipeline.
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            source.LoadImage(File.ReadAllBytes(temporary + ".png"));
            var small = RenderTexture.GetTemporary(1280, 720, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, small);
            var previous = RenderTexture.active;
            RenderTexture.active = small;
            var result = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            result.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            result.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(Folder, row.screenshot), result.EncodeToJPG(80));
            File.Delete(temporary + ".png");
            UnityEngine.Object.Destroy(source); UnityEngine.Object.Destroy(result);
            RenderTexture.ReleaseTemporary(small);
            Write(row);
        }

        public void Bug(string id, string severity, string expected, string actual, string cause = "Cần đối chiếu code và ảnh; chưa kết luận nguyên nhân.")
        {
            string scene = SceneManager.GetActiveScene().name;
            if (!_seen.Add(scene + ":" + id)) return;
            var row = Snapshot("bug_candidate");
            row.issue = id; row.severity = severity; row.expected = expected; row.actual = actual; row.cause = cause;
            row.message = string.Join(" → ", _history.Skip(Math.Max(0, _history.Count - 10)));
            Findings.Add(row);
            _pending.Enqueue(row);
            if (severity == "BLOCKER") HasBlocker = true;
        }

        public IEnumerator FlushFindings()
        {
            while (_pending.Count > 0) yield return Capture(_pending.Dequeue());
        }

        void OnUnityLog(string message, string stack, LogType type)
        {
            if (type != LogType.Warning && type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            var row = Snapshot("unity_log");
            row.severity = type.ToString(); row.message = message; row.stack = stack;
            Write(row);
            Bug("unity_" + type + "_" + message, type == LogType.Warning ? "MINOR" : "MAJOR", "Không có cảnh báo/lỗi runtime.", message, stack);
        }

        public void ObserveFrame()
        {
            if (_lastObservedFrame == Time.frameCount) return;
            _lastObservedFrame = Time.frameCount;
            string scene = SceneManager.GetActiveScene().name, beat = SceneDirector.Current?.Beat ?? "";
            bool changed = scene != _timelineScene || beat != _timelineBeat;
            if (changed || Time.realtimeSinceStartup - _timelineAt >= 30f)
            {
                _timelineScene = scene; _timelineBeat = beat; _timelineAt = Time.realtimeSinceStartup;
                var row = Snapshot("timeline");
                row.step = changed ? "beat_" + scene + "_" + beat : "interval_" + Step;
                _pending.Enqueue(row);
            }
            foreach (var lamp in _lamps)
            {
                if (lamp == null) continue;
                _lampChecks++;
                var q = lamp.transform.localRotation;
                if (float.IsFinite(q.x) && float.IsFinite(q.y) && float.IsFinite(q.z) && float.IsFinite(q.w)) continue;
                _lampsFinite = false;
                Bug("lamp_nan_" + lamp.name, "MAJOR", "Rotation đèn hữu hạn.", lamp.name + ": " + q,
                    "Game/Assets/_Project/Scripts/Runtime/Ship/SwingingLamp.cs: Update");
            }
            var rig = PlayerRig.Current;
            if (rig == null) return;
            Vector3 p = rig.Position;
            if (!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z))
                Bug("nan_player", "BLOCKER", "Tọa độ người chơi hữu hạn.", "Tọa độ NaN/Infinity: " + p);
            bool supported = Physics.Raycast(p + Vector3.up * 0.15f, Vector3.down, 0.55f, ~(1 << 2), QueryTriggerInteraction.Ignore);
            if (_lastRig != rig || supported) _groundedY = p.y;
            if (_lastRig == rig && _groundedY - p.y > 1f && rig.ControlEnabled && !supported)
                Bug("player_falling", "BLOCKER", "Người chơi đứng trên sàn/cầu thang.", $"Y tụt {_groundedY - p.y:F3} m từ mặt đỡ gần nhất; frame trước {_lastPosition}, hiện tại {p}.");
            if (p.y < -12f) Bug("below_floor", "BLOCKER", "Không rơi xuyên sàn.", "Người chơi ở y=" + p.y.ToString("F3", CultureInfo.InvariantCulture));
            _lastRig = rig; _lastPosition = p;

            var who = rig.interactor;
            var held = who.Holding;
            // Scene instructions intentionally override world prompts outside an active shift.
            // Require a stable mismatch to ignore the single frame after an action changes its prompt key.
            if (SceneDirector.Current is ShiftDirector activeShift && activeShift.InShift && GameRoot.Instance.Input.GameplayEnabled)
            {
                var key = who.Current?.GetPromptKey(who) ?? (held != null ? "prompt.drop" : null);
                string visible = VisiblePrompt;
                string mismatch = key != null && !string.IsNullOrEmpty(visible) && visible != GameRoot.Instance.Content.GetString(key)
                    ? ((who.Current as Component)?.name ?? "held_object") + ":" + key + ":" + visible : null;
                if (mismatch != _promptMismatch) { _promptMismatch = mismatch; _promptMismatchSince = Time.time; }
                if (mismatch != null && Time.time - _promptMismatchSince > 0.5f)
                    Bug("world_prompt_mismatch_" + ((who.Current as Component)?.name ?? "held_object"), "MAJOR",
                        "Chữ hướng dẫn ứng với vật đang nhắm và prompt key " + key + ".", "Chữ hiển thị không khớp trong hơn 0,5 giây: " + visible,
                        "Game/Assets/_Project/Scripts/Runtime/UI/PromptView.cs: Refresh; đối chiếu Interactor.Update và scene override.");
            }
            else _promptMismatch = null;
            if (held != _lastHeld) { _lastHeld = held; _heldSince = Time.time; }
            if (held != null && Time.time - _heldSince > 1.5f)
            {
                // Xô, nước đá xách bên hông (Interactor.holdHang): so với điểm xách, không với điểm cầm trước ngực (bot 04/11 báo nhầm 1,2 m).
                var anchor = who.holdHang != null && held.transform.parent == who.holdHang ? who.holdHang :
                    held.ShoulderCarried && who.holdRightShoulder != null ? who.holdRightShoulder :
                    held.twoHanded ? who.holdTwoHands : who.holdOneHand;
                if (anchor != null && Vector3.Distance(held.transform.position, anchor.position) > 0.75f)
                    Bug("held_far_" + held.kind, "VISUAL-SUSPECT", "Vật đang cầm nằm gần tay.", $"{held.name}: cách điểm cầm {Vector3.Distance(held.transform.position, anchor.position):F3} m.", "Game/Assets/_Project/Scripts/Runtime/Interaction/Holdable.cs: LateUpdate");
            }
            if (GameRoot.Instance.Input.GameplayEnabled && who.Current is Tasks.TaskTarget target &&
                (target.task is CoalStation || target.task is AshStation) && VisiblePrompt.IndexOf("khoai", StringComparison.OrdinalIgnoreCase) >= 0)
                Bug("unrelated_prompt_" + target.id, "MAJOR", "Prompt mô tả đúng vật đang nhắm.", $"Nhắm {target.name}, collider {who.LookCollider?.name}; chữ hiển thị: {VisiblePrompt}", "Game/Assets/_Project/Scripts/Runtime/UI/PromptView.cs: Refresh");
            if (_props.Count > 0) CheckFloating(_props[_propIndex++ % _props.Count], rig.cam, held);
        }

        void CheckFloating(Prop prop, Camera cam, Holdable held)
        {
            // Carried objects are checked against their hand anchors. Shovel-load floor gaps remain
            // observations for triage: attached tool geometry can be intentionally above the floor.
            if (prop.root == null || !prop.root.gameObject.activeInHierarchy || cam == null ||
                prop.root.IsChildOf(PlayerRig.Current.transform) ||
                (held != null && prop.root == held.transform)) return;
            bool hasBounds = false;
            Bounds bounds = default;
            foreach (var r in prop.renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!hasBounds) { bounds = r.bounds; hasBounds = true; } else bounds.Encapsulate(r.bounds);
            }
            if (!hasBounds) return;
            var screen = cam.WorldToViewportPoint(bounds.center);
            if (screen.z <= 0f || screen.x < 0f || screen.x > 1f || screen.y < 0f || screen.y > 1f) { prop.floatingSince = -1f; return; }
            // A viewport/frustum match is insufficient: walls must also occlude the visual observation.
            var ray = bounds.center - cam.transform.position;
            if (Physics.Raycast(cam.transform.position, ray.normalized, out var visibleHit, ray.magnitude, ~(1 << 2), QueryTriggerInteraction.Ignore) &&
                !visibleHit.transform.IsChildOf(prop.root) && visibleHit.transform != prop.root) { prop.floatingSince = -1f; return; }
            float support = float.NegativeInfinity;
            string supportName = "";
            for (int sample = 0; sample < 5; sample++)
            {
                float x = sample == 1 ? bounds.extents.x * 0.7f : sample == 2 ? -bounds.extents.x * 0.7f : 0f;
                float z = sample == 3 ? bounds.extents.z * 0.7f : sample == 4 ? -bounds.extents.z * 0.7f : 0f;
                var origin = new Vector3(bounds.center.x + x, bounds.min.y + 0.025f, bounds.center.z + z);
                int n = Physics.RaycastNonAlloc(origin, Vector3.down, _supportHits, 4f, ~(1 << 2), QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var hit = _supportHits[i];
                    if (hit.transform == prop.root || hit.transform.IsChildOf(prop.root) || hit.transform.IsChildOf(PlayerRig.Current.transform)) continue;
                    if (hit.point.y > support) { support = hit.point.y; supportName = hit.collider.name; }
                }
            }
            float gap = bounds.min.y - support;
            if (!float.IsFinite(gap) || gap <= 0.05f) { prop.floatingSince = -1f; return; }
            if (prop.floatingSince < 0f) prop.floatingSince = Time.time;
            if (Time.time - prop.floatingSince > 1f)
                Bug("floating_" + prop.root.name, "VISUAL-SUSPECT", "Đáy renderer cách mặt đỡ không quá 5 cm.", $"Suspected floating: {prop.root.name}; gap={gap:F3} m, bounds.min.y={bounds.min.y:F3}, mặt đỡ={supportName} ở y={support:F3}.");
        }

        public void Finish(string coverage)
        {
            var row = Snapshot("run_end"); row.message = coverage; Write(row);
            if (_playerClip != null)
            {
                foreach (var f in _playerClip.Track.Minors)
                    Bug("player_clip_warn_" + f.kind + "_" + f.part + "_" + f.obj, "MINOR", "Tay, thân, dụng cụ, đồ bê của Ba không chạm sâu vào vật.", f.Line);
                Debug.Log("PLAYER_CLIP bot: " + _playerClip.WriteReport("bot_" + RunFolderName) + "\n" + _playerClip.Track.Summary());
            }
            var text = new StringBuilder("# Báo cáo playthrough — cần đối chiếu các ứng viên lỗi\n\n");
            text.AppendLine("Run: `" + Run + "`. " + coverage + "\n");
            text.AppendLine("Các mục tự động là quan sát/ứng viên; lỗi đường đi hoặc nhắm của bot phải bị loại sau khi đối chiếu code. Không khẳng định đã tìm mọi lỗi.\n");
            int number = 0;
            foreach (var bug in Findings.OrderBy(b => b.severity == "BLOCKER" ? 0 : b.severity == "MAJOR" ? 1 : b.severity == "MINOR" ? 2 : 3))
            {
                text.AppendLine($"## {++number}. [{bug.severity}] {bug.issue}\n");
                text.AppendLine($"- Cảnh / beat / giờ: `{bug.scene}` / `{bug.beat}` / `{bug.clock}`; ngày {bug.day}; vị trí `{bug.position}`.");
                text.AppendLine("- Tái hiện: " + bug.message + ".");
                text.AppendLine("- Mong đợi: " + bug.expected);
                text.AppendLine("- Quan sát: " + bug.actual);
                text.AppendLine($"- Prompt: `{bug.promptKey}` — {bug.promptText}.");
                text.AppendLine($"- Ảnh: [{bug.screenshot}]({bug.screenshot}).");
                text.AppendLine("- Log: xem run_log.jsonl, type=bug_candidate, issue=" + bug.issue + ".");
                text.AppendLine("- Nguyên nhân phỏng đoán: " + bug.cause + "\n");
            }
            File.WriteAllText(Path.Combine(Folder, "BUGS_auto_" + RunFolderName + ".md"), text.ToString(), new UTF8Encoding(false));
            // Artifact CI chỉ lấy Logs/*.log: in các mục BLOCKER/MAJOR vào log Unity để đọc được mà không cần vào máy chạy.
            foreach (var bug in Findings.Where(b => b.severity == "BLOCKER" || b.severity == "MAJOR"))
                Debug.Log($"PLAYTHROUGH_BUG [{bug.severity}] {bug.issue} @ {bug.scene}/{bug.beat}/{bug.clock} {bug.position}: {bug.actual}");
        }

        /// <summary>
        /// Gọi một lần cuối mỗi khung, sau Cinemachine, tay và thân (PlaythroughFrameObserver chạy muộn nhất). Đo giữa hai khung
        /// liền nhau; bỏ qua khung có input của bot, phiên chuyển (ngồi/đứng, MoveTo, đổi cảnh) và lúc tắt điều khiển.
        /// </summary>
        public void ObserveLate()
        {
            var root = GameRoot.Instance;
            var rig = PlayerRig.Current;
            if (root == null || rig == null || rig.cam == null) { _camRig = _lockRig = null; _progressKey = null; return; }
            var input = root.Input;
            string scene = SceneManager.GetActiveScene().name;
            var sit = rig.GetComponent<SitController>();
            bool seated = sit != null && sit.Seated;
            var cam = rig.cam.transform;
            Vector3 pos = cam.position, feet = rig.Position;
            Quaternion rot = cam.rotation;
            bool continuous = _camRig == rig && _camScene == scene && _camFrame == Time.frameCount - 1;
            bool quiet = input.GameplayEnabled && rig.ControlEnabled && !rig.Moving && seated == _camSeated &&
                         input.SimMove == Vector2.zero && input.SimLook == Vector2.zero && Time.frameCount - BotDriveFrame > 1 &&
                         Vector3.Distance(feet, _camFeet) < 0.01f;
            if (continuous && quiet)
            {
                CameraSamples++;
                // Ngưỡng theo khung 60 fps; khung dài hơn (tua nhanh, máy chậm) thì nới theo tỉ lệ.
                float scale = Mathf.Max(1f, Time.deltaTime * 60f);
                float moved = Vector3.Distance(pos, _camPos), turned = Quaternion.Angle(rot, _camRot);
                string detail = $"Một khung (dt {Time.deltaTime:F3} s): camera dời {moved * 100f:F1} cm, xoay {turned:F1}° khi đứng yên, không có input.";
                if (moved > 0.15f * scale || turned > 10f * scale)
                    Bug("camera_jump_" + Step, "MAJOR", "Camera không nhảy khi đứng yên không thao tác (≤ 15 cm, ≤ 10° mỗi khung).", detail, CameraCause);
                else if (moved > 0.05f * scale || turned > 3f * scale)
                    Bug("camera_jump_warn_" + Step, "MINOR", "Camera đứng yên khi không thao tác (≤ 5 cm, ≤ 3° mỗi khung).", detail, CameraCause);
            }
            _camRig = rig; _camScene = scene; _camFrame = Time.frameCount; _camPos = pos; _camRot = rot; _camFeet = feet; _camSeated = seated;

            bool lookLocked = rig.look.Locked, moveLocked = rig.motor.MovementLocked;
            // Khóa đổi ngay khi nhấn/nhả nút thao tác (giữ để gạt từng lá rồi nhả) là theo tay người chơi, không phải dao động:
            // chỉ đếm lần đổi khóa khi nút đã giữ nguyên quá 2 khung (khóa theo tia nhìn như QA-0007 vẫn bị đếm).
            bool useHeld = input.UseHeld;
            if (useHeld != _useHeld) _useEdgeFrame = Time.frameCount;
            _useHeld = useHeld;
            bool byInput = Time.frameCount - _useEdgeFrame <= 2;
            if (_lockRig == rig)
            {
                if (lookLocked != _lookLocked && !byInput) _lockFlips.Enqueue(Time.time);
                if (moveLocked != _moveLocked && !byInput) _lockFlips.Enqueue(Time.time);
                while (_lockFlips.Count > 0 && Time.time - _lockFlips.Peek() > 1f) _lockFlips.Dequeue();
                string detail = $"{_lockFlips.Count} lần đổi khóa trong 1 s (look {lookLocked}, move {moveLocked}).";
                if (_lockFlips.Count >= 5)
                    Bug("lock_flicker_" + Step, "MAJOR", "Khóa camera/di chuyển không bật tắt liên tục (< 5 lần mỗi giây).", detail, LockCause);
                else if (_lockFlips.Count >= 3)
                    Bug("lock_flicker_warn_" + Step, "MINOR", "Khóa camera/di chuyển đổi ít hơn 3 lần mỗi giây.", detail, LockCause);
            }
            else _lockFlips.Clear();
            _lockRig = rig; _lookLocked = lookLocked; _moveLocked = moveLocked;

            // Không tiến triển: trong ca còn lệnh mở, ngoài thoại và thẻ tua nhanh, mà bảng việc, tiến độ ca, đồ trên tay đứng yên.
            if (SceneDirector.Current is ShiftDirector shift && shift.InShift && shift.Board.Orders.Any(o => !o.IsDone) &&
                input.GameplayEnabled && !root.Subtitles.IsPlaying && !root.UI.Card.Visible)
            {
                var who = rig.interactor;
                string key = shift.PhaseIndex + "|" + shift.Clock.Progress.ToString("F3", CultureInfo.InvariantCulture) + "|" +
                             string.Join(",", shift.Board.Orders.Select(o => o.Done)) + "|" + (who.Holding != null ? who.Holding.name : "") + "|" + who.Tool +
                             // Vòng tiến độ thao tác trên màn (cọ nồi cao ~2,5 phút) đi lên từng 5% cũng là tiến triển người chơi thấy.
                             "|" + (root.UI.Prompt.Progress.HasValue ? Mathf.FloorToInt(root.UI.Prompt.Progress.Value * 20f).ToString() : "-");
                if (key != _progressKey) { _progressKey = key; _progressSince = Time.time; }
                float idle = Time.time - _progressSince;
                string detail = $"Bảng việc, tiến độ ca và đồ trên tay đứng yên {idle:F0} s (bước {Step}).";
                if (idle > 90f)
                    Bug("no_progress_" + shift.Beat, "MAJOR", "Đang có lệnh mở thì trạng thái bếp đổi trong 90 s.", detail,
                        "Việc đang chờ cái gì? Đối chiếu Guide của station, trạng thái bếp và lệnh trên bảng.");
                else if (idle > 30f)
                    Bug("no_progress_warn_" + shift.Beat, "MINOR", "Trạng thái bếp đổi trong 30 s khi có lệnh mở.", detail);
            }
            else _progressKey = null;
            ObserveReach(rig, seated);
            ObserveNpcs();
            ObserveGuide(root);
        }

        PlayerViewProbe _reachProbe;
        PlayerRig _reachRig;
        float _reachSince = -1f, _reversedSince = -1f;
        const string ReachCause = "Điểm cầm của trạm ngoài tầm tay đứng thẳng (vai sau mắt 11 cm, tay 0,58 m): nâng/dời chỗ làm hoặc " +
            "PlayerRig.StepToWork sát hơn; không hạ đầu, gập thân hay dời camera (luật camera 17/10).";

        /// <summary>
        /// Luật camera 17/10: mắt không rời chỗ mặc định (chỉ ghế thật, ghế đẩu được đổi độ cao) và tay phải với tới điểm cầm từ tư thế
        /// đứng thẳng. Bắt mọi trạm bot đi qua: tay bám điểm cầm mà cổ tay xương hở quá 3,5 cm suốt 0,3 s là MAJOR.
        /// </summary>
        void ObserveReach(PlayerRig rig, bool seated)
        {
            if (rig.head != null && Mathf.Abs(rig.head.localPosition.z) > 0.005f)
                Bug("eye_forward_" + Step, "MAJOR", "Mắt ở đúng chỗ mặc định trên đầu (luật camera 17/10).",
                    $"Camera dời theo phương trước/sau {rig.head.localPosition.z * 100f:F1} cm.", CameraCause);
            if (!seated && rig.motor.ExtraHeadHeight < -0.01f)
                Bug("head_dip_" + Step, "MAJOR", "Không hạ đầu khi đứng làm việc (luật camera 17/10).",
                    $"ExtraHeadHeight {rig.motor.ExtraHeadHeight:F2} m khi không ngồi.", CameraCause);
            // Cẳng tay vặn ngược (dáng/chỗ bám cho lòng bàn tay quay sai phía) liền 0,3 s. Dáng Open tay không (giơ hai bàn tay, lòng
            // ngửa lên): cẳng xoay ngửa ~110° là đúng dáng, ảnh tp17_handsshot_00_Prologue đã xem 23/10; không tính.
            bool openPalms = rig.hands != null && rig.hands.Pose == HandPose.Open && rig.interactor.Holding == null;
            if (rig.body != null && !openPalms && PlayerClipDetector.Reversal(rig.body, out string arm) > 0f && !rig.Stepping)
            {
                if (_reversedSince < 0f) _reversedSince = Time.time;
                if (Time.time - _reversedSince >= 0.3f)
                    Bug("forearm_reversed_" + Step, "MAJOR", "Cẳng tay không vặn ngược: lòng bàn tay áp vào vật đang cầm (17/10 \"ngược cẳng\").",
                        $"Cẳng tay {arm} (ngưỡng ±{PlayerClipDetector.ReversedTwist:F0}°); dáng {rig.hands.Pose}, cầm {(rig.interactor.Holding != null ? rig.interactor.Holding.name : "-")}.",
                        "Chỗ bám hay dáng tay (HandsRig.targets, grip của trạm/đồ) lật sai 180° quanh trục ngón.");
            }
            else _reversedSince = -1f;
            var hands = rig.hands;
            // Tay đang ẩn (ArmsVisibility, 17/10): cổ tay hở không ai thấy.
            if (hands == null || rig.body == null || !hands.Working || rig.Stepping || !rig.ArmsShown) { _reachSince = -1f; return; }
            if (_reachRig != rig) { _reachProbe = new PlayerViewProbe(rig); _reachRig = rig; }
            float gap = Mathf.Max(_reachProbe.WristGap(true), _reachProbe.WristGap(false));
            if (gap <= 0.035f) { _reachSince = -1f; return; }
            if (_reachSince < 0f) _reachSince = Time.time;
            if (Time.time - _reachSince < 0.3f) return;
            var root = rig.transform;
            string Side(bool left)
            {
                var grip = hands.GripAnchor(left);
                var shoulder = root.InverseTransformPoint(rig.body.Shoulder(left).position);
                if (grip == null) return $"{(left ? "T" : "P")}: không có điểm cầm";
                var g = root.InverseTransformPoint(grip.position);
                return $"{(left ? "T" : "P")}: vai {shoulder.ToString("F2")} → {grip.name} {g.ToString("F2")}, cách {Vector3.Distance(shoulder, g):F2} m / tay {rig.body.ArmLength(left):F2} m";
            }
            var anchor = hands.GripAnchor(false) != null ? hands.GripAnchor(false) : hands.GripAnchor(true);
            string where = anchor == null ? "?" : (anchor.parent != null ? anchor.parent.name + "/" : "") + anchor.name;
            Bug("reach_" + where, "MAJOR", "Tay với tới điểm cầm khi đứng thẳng (cổ tay xương cách bàn tay ≤ 3,5 cm).",
                $"{where}: hở {gap * 100f:F1} cm; {Side(true)}; {Side(false)}", ReachCause);
        }

        float _guideAt;
        Transform _guideBad;

        /// <summary>
        /// GUIDE: điểm hướng dẫn đang hiện phải là vật có trong cảnh, đang bật; vật thao tác được thì còn collider bật. Lấy mẫu
        /// mỗi 0,5 s: một mẫu sai là MINOR (có thể rơi đúng khung vừa nhặt/đặt), sai liền hai mẫu (≥ 0,5 s) là MAJOR.
        /// </summary>
        void ObserveGuide(GameRoot root)
        {
            if (Time.time < _guideAt) return;
            _guideAt = Time.time + 0.5f;
            var guide = root.UI != null ? root.UI.Guide : null;
            Transform t = guide != null && !string.IsNullOrEmpty(guide.Text) ? guide.Target : null;
            string code = null, actual = null, cause = null;
            if (t != null && !t.gameObject.activeInHierarchy)
            {
                code = "guide_target_inactive_";
                actual = $"“{guide.Text}” chỉ tới {t.name} đang tắt.";
                cause = "Station trả Guide tới vật đã ẩn hoặc đã cất?";
            }
            else if (t != null && t.GetComponentInParent<IPromptProvider>() != null && !t.GetComponentsInChildren<Collider>().Any(c => c.enabled))
            {
                code = "guide_target_unreachable_";
                actual = $"“{guide.Text}” chỉ tới {t.name} không có collider bật.";
                cause = "Collider của vật bị tắt trong lúc hướng dẫn còn chỉ tới nó.";
            }
            if (code == null) { _guideBad = null; return; }
            bool lasting = _guideBad == t;
            _guideBad = t;
            string expected = code == "guide_target_inactive_" ? "Hướng dẫn chỉ tới vật đang có trong cảnh." : "Vật hướng dẫn chỉ tới nhắm được (có collider bật).";
            if (lasting) Bug(code + t.name, "MAJOR", expected, actual + " (liền hai mẫu, ≥ 0,5 s)", cause);
            else Bug(code + "warn_" + t.name, "MINOR", expected, actual, cause);
        }

        /// <summary>NPC đang đi mà thân chồng vào vật tĩnh: WARN một mẫu, ERROR liên tục quá 0,3 s (bảng oracle W1).</summary>
        void ObserveNpcs()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (scene != _npcScene || Time.time >= _npcListAt)
            {
                _npcScene = scene;
                _npcListAt = Time.time + 5f;
                _npcs = UnityEngine.Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None);
                _npcInside.Clear();
            }
            if (Time.time < _npcSampleAt) return;
            _npcSampleAt = Time.time + 0.1f;
            foreach (var npc in _npcs)
            {
                if (npc == null) continue;
                if (!npc.isActiveAndEnabled || !npc.BodyActive || !npc.Walking) { _npcInside.Remove(npc); continue; }
                NpcSamples++;
                var p = npc.Position;
                float r = Mathf.Max(0.05f, npc.bodyRadius - 0.02f);
                int n = Physics.OverlapCapsuleNonAlloc(p + Vector3.up * (0.32f + r), p + Vector3.up * (1.38f - r), r, _npcHits, ~(1 << 2), QueryTriggerInteraction.Ignore);
                Collider inside = null;
                for (int i = 0; i < n && inside == null; i++)
                    // Vật đang bị kéo (VesselTow thêm vào NpcAgent.Obstacles) không phải vật tĩnh: Ba đẩy nó tới người đang nhường.
                    if (NpcPathing.IsObstacle(_npcHits[i], npc.transform, p.y)
                        && !(_npcHits[i].attachedRigidbody != null && NpcAgent.Obstacles.Contains(_npcHits[i].attachedRigidbody.transform)))
                        inside = _npcHits[i];
                if (inside == null) { _npcInside.Remove(npc); continue; }
                if (!_npcInside.TryGetValue(npc, out float since)) { _npcInside[npc] = since = Time.time; }
                float stuck = Time.time - since;
                string detail = $"{npc.name} đang đi, thân chồng {inside.name} ở {p} (liên tục {stuck:F1} s); tới {npc.WalkGoal}, đi xuyên vì kẹt {npc.PassingStatic}, chờ {(npc.Waiting ? npc.BlockedBy : "không")}.";
                if (stuck > 0.3f)
                    Bug("npc_inside_" + npc.name + "_" + inside.name, "MAJOR", "NPC đang đi không chồng vào vật tĩnh quá 0,3 s.", detail,
                        "NpcAgent đi xuyên khi kẹt (staticGhostUntil) hay tuyến builder cắt qua vật? W5: bỏ đi xuyên, NavMesh cho nhân vật quan trọng.");
                else
                    Bug("npc_inside_warn_" + npc.name + "_" + inside.name, "MINOR", "NPC đang đi không chạm vật tĩnh.", detail);
            }
            ObserveBodyClips(scene);
        }

        /// <summary>
        /// Đồ cầm, tay, chân NPC lún vào thân mình hay người khác (BodyClipDetector), chỉ NPC người chơi nhìn thấy trong 8 m. Lần đầu
        /// qua ngưỡng minor là MINOR; lún quá ngưỡng major liền ≥ 0,2 s là MAJOR.
        /// </summary>
        void ObserveBodyClips(string scene)
        {
            var rig = PlayerRig.Current;
            if (rig == null || rig.cam == null) return;
            float dt = _clipAt < 0f ? 0.1f : Mathf.Min(Time.time - _clipAt, 0.5f);
            _clipAt = Time.time;
            var eye = rig.cam.transform.position;
            _clipNpcs.Clear();
            foreach (var npc in _npcs)
            {
                if (npc == null || !BodyClipDetector.Measurable(npc) || (npc.Position - eye).sqrMagnitude > 8f * 8f) continue;
                var skin = npc.GetComponentInChildren<SkinnedMeshRenderer>();
                if (skin != null && skin.isVisible) _clipNpcs.Add(npc);
            }
            _clipContacts.Clear();
            _clip.Sample(_clipNpcs, _clipContacts);
            const string expected = "Đồ cầm, tay, chân NPC không lún vào thân mình hay người khác quá ngưỡng (đồ 4 cm, tay/chân 4 cm, tay nắm đồ 8 cm, tay rời đồ bê 10 cm) liền 0,2 s.";
            foreach (var f in _clipTrack.Feed(scene, _clipContacts, dt, _clip.Role))
                Bug("body_clip_" + f.npc + "_" + f.kind + "_" + f.a + "_" + f.b, "MAJOR", expected, BodyClipTrack.Line(f), ClipCause);
            foreach (var c in _clipContacts)
            {
                var f = _clipTrack.Findings[BodyClipTrack.KeyOf(c)];
                if (f.Severity == "MINOR")
                    Bug("body_clip_warn_" + f.npc + "_" + f.kind + "_" + f.a + "_" + f.b, "MINOR", "Đồ cầm, tay, chân NPC không chạm sâu vào thân.", BodyClipTrack.Line(f));
            }
        }

        public void Dispose()
        {
            Application.logMessageReceived -= OnUnityLog;
            _observer.Log = null;
            UnityEngine.Object.Destroy(_observer.gameObject);
            if (_playerClip != null) UnityEngine.Object.Destroy(_playerClip.gameObject);
            _writer.Dispose();
        }
    }

    // A separate observer also sees the frames advanced inside screenshot capture and scene loading.
    // Chạy sau mọi LateUpdate khác (Cinemachine, tay, thân) để đo camera ở tư thế cuối khung.
    [DefaultExecutionOrder(32000)]
    public sealed class PlaythroughFrameObserver : MonoBehaviour
    {
        public PlaythroughLog Log;
        void LateUpdate()
        {
            Log?.ObserveFrame();
            Log?.ObserveLate();
        }
    }
}
