using System.Collections;
using System.Collections.Generic;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Ship;
using HaiBanTay.Tasks;
using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>
    /// Các nhân vật theo ngày của cảnh 02 (M11), đọc từ DayData.Npcs: hai lính Pháp giải ngũ (F05, F07a), thủy thủ Jacques
    /// (ngày bão), ông Mai (F01c, phòng ăn sĩ quan). deck: đứng trên boong giếng trước cả ca; help: vào bếp làm cùng trong
    /// lượt (lính nhặt rau giúp, Jacques giữ nồi); visit: ông Mai ghé bếp, gọi người chơi, đợi người chơi lại gần rồi nói
    /// chuyện; teach: buổi tối ngồi ở góc đèn dầu dạy chữ. Thoại phát khi người chơi tới gần (phụ đề), ai nói thì người đó
    /// làm động tác nói. NPC đi qua cửa bếp thì giữ cửa mở (HingedDoor chỉ tự mở cho người chơi).
    /// Sim: đặt thẳng vào chỗ, bỏ thoại, phát sự kiện ngay.
    /// </summary>
    public sealed class DayCast : MonoBehaviour
    {
        public const string Soldiers = "soldiers";
        public const string Jacques = "jacques";
        public const string Mai = "mai";

        public NpcAgent soldierA;
        public NpcAgent soldierB;
        public NpcAgent jacques;
        public NpcAgent mai;

        [Header("Đường đi (thế giới)")]
        [Tooltip("Từ boong giếng trước qua cửa bếp vào trong bếp (lính, Jacques).")]
        public Vector3[] deckToGalley = new Vector3[0];
        [Tooltip("Ông Mai: từ hành lang (phía phòng ăn sĩ quan) vào bếp.")]
        public Vector3[] corridorToGalley = new Vector3[0];

        [Header("Chỗ đứng, chỗ ngồi")]
        public Transform[] deckSpots = new Transform[0];
        [Tooltip("Nói chuyện xong ở boong: đường đi từ chỗ cửa bếp vòng ra lan can phía bắc (thế giới).")]
        public Vector3[] deckAwayPath = new Vector3[0];
        [Tooltip("Chỗ đứng tựa lan can sau khi nói chuyện ở boong.")]
        public Transform[] deckAwaySpots = new Transform[0];
        public Transform[] greensSpots = new Transform[0];
        public Transform jacquesPots;
        public Transform jacquesTallPot;
        public Transform maiSpot;
        public Transform[] teachSeats = new Transform[0];
        [Tooltip("Ông Mai ngồi ở bàn đánh bài buổi tối (việc 'cards': anh em nghỉ, đánh bài trong lúc Ba tự đọc, viết).")]
        public GameObject cardMai;
        /// <summary>Ghế đẩu của người dạy (chỉ hiện buổi tối có học).</summary>
        public GameObject teachStools;
        /// <summary>Bóng người ngồi đọc ở góc đèn dầu (M8): ẩn khi người dạy thật ngồi đó.</summary>
        public GameObject silhouette;

        [Header("Liên kết")]
        public HingedDoor galleyDoor;
        [Tooltip("Ba nồi rời trên lò nam lúc bão (SecurePotsTask): Jacques nắm hai quai một nồi cho khỏi trượt trong lúc Ba chằng.")]
        public LooseObjectSway[] stormPots = new LooseObjectSway[0];
        [Tooltip("Khoảng cách từ thân nồi cao tới tâm thân Jacques khi giữ nồi (m).")]
        public float rimStandOff = 0.3f;
        public GreensStation greens;
        public TallPotStation scrub;

        public float talkRadius = 3.2f;
        public float visitTalkRadius = 4.2f;
        [Tooltip("Hai lính nhặt xong một bó rau mỗi chừng này giây.")]
        public float helpInterval = 22f;
        public float doorAssistRadius = 2.2f;

        DayData _day;
        readonly Dictionary<NpcAgent, Coroutine> _moves = new Dictionary<NpcAgent, Coroutine>();
        readonly List<Coroutine> _beats = new List<Coroutine>();
        readonly HashSet<NpcBeat> _gone = new HashSet<NpcBeat>();
        float _nextDoorCheck;
        float _nextLayerCheck;
        GameObject _arm;
        NpcHandWork _jacquesHands;
        LooseObjectSway _heldPot;
        Coroutine _jacquesWork;
        readonly Dictionary<NpcAgent, Renderer[]> _renderers = new Dictionary<NpcAgent, Renderer[]>();
        readonly Dictionary<NpcAgent, bool> _indoors = new Dictionary<NpcAgent, bool>();

        /// <summary>Số câu thoại NPC đã phát (test).</summary>
        public int LinesPlayed { get; private set; }
        /// <summary>Số bó rau hai lính đã nhặt giúp.</summary>
        public int HelpedBunches { get; private set; }
        public bool Talking { get; private set; }

        public NpcAgent[] All => new[] { soldierA, soldierB, jacques, mai };
        bool _peeling;

        public void ReservePeeling(bool on)
        {
            _peeling = on;
            if (on && mai != null && _moves.TryGetValue(mai, out var moving))
            {
                if (moving != null) StopCoroutine(moving);
                _moves.Remove(mai);
                mai.ClearWalkState();
            }
        }

        public void PrepareCoffee()
        {
            HideAll();
            int i = 0;
            foreach (var a in new[] { soldierA, soldierB })
            { a.gameObject.SetActive(true); a.TeleportTo(deckSpots[i].position, deckSpots[i].eulerAngles.y); a.Play("Idle"); i++; }
        }

        void Awake()
        {
            foreach (var a in All)
                if (a != null)
                    _renderers[a] = a.GetComponentsInChildren<Renderer>(true);
            if (jacques != null)
                _jacquesHands = jacques.GetComponent<NpcHandWork>();
            foreach (var speaker in new[] { "linh_tre", "linh_mu", "jacques", "ong_mai", "mai" })
                TalkGaze.Bind(speaker, SpeakerAgent(speaker));
        }

        /// <summary>Nồi Jacques đang nắm (test).</summary>
        public LooseObjectSway HeldPot => _heldPot;
        public NpcHandWork JacquesHands => _jacquesHands;

        /// <summary>Đầu ngày: ẩn mọi người, đặt người đứng boong cả ngày.</summary>
        public void Begin(DayData day)
        {
            _day = day;
            _gone.Clear();
            HideAll();
            if (scrub != null && scrub.sailorArm != null)
                _arm = scrub.sailorArm.gameObject;
            if (teachStools != null)
                teachStools.SetActive(false);
            if (cardMai != null)
                cardMai.SetActive(false);
            if (day == null)
                return;
            foreach (var beat in day.Npcs)
            {
                if (beat.Phase != "day")
                    continue;
                var actors = Actors(beat.Who);
                for (int i = 0; i < actors.Count; i++)
                {
                    var spot = Spot(deckSpots, i);
                    if (spot == null)
                        continue;
                    Show(actors[i], spot.position, spot.eulerAngles.y);
                    actors[i].Play(i == 0 ? "Talk" : "IdleLook");
                }
                if (beat.Action == "deck")
                    _beats.Add(StartCoroutine(DeckChat(beat, actors)));
                else if (beat.Action == "help")
                    _beats.Add(StartCoroutine(TalkWhenNear(beat, actors, talkRadius, raiseEvent: true)));
            }
        }

        /// <summary>
        /// Hai lính hóng gió trên boong ngoài cửa bếp: chỉ bắt chuyện khi Ba mở cửa bước ra (không nói vọng qua vách), nói xong thì
        /// đi vòng ra lan can phía bắc đứng tựa, không đứng chắn cửa nữa.
        /// </summary>
        IEnumerator DeckChat(NpcBeat beat, List<NpcAgent> actors)
        {
            yield return TalkWhenNear(beat, actors, talkRadius, raiseEvent: true, gate: BaAtDeckDoor, backToWork: false);
            if (Sim.Enabled)
                yield break;
            for (int i = 0; i < actors.Count; i++)
            {
                var spot = Spot(deckAwaySpots, i);
                if (actors[i] == null || !actors[i].gameObject.activeSelf || spot == null)
                    continue;
                var path = new List<Vector3>(deckAwayPath) { spot.position };
                MoveTo(actors[i], path.ToArray(), spot.eulerAngles.y, "LeanRail", 0.6f + i * 0.9f);
            }
        }

        /// <summary>Ba đã ra boong, hoặc cửa bếp đang mở (Ba vừa mở cửa).</summary>
        bool BaAtDeckDoor()
        {
            var rig = PlayerRig.Current;
            return rig != null && (!Inside(rig.Position) || (galleyDoor != null && galleyDoor.IsOpen));
        }

        /// <summary>Lượt mới bắt đầu: người hết việc thì về, người có việc trong lượt thì tới.</summary>
        public void PhaseStarted(PhaseData phase)
        {
            if (_day == null || phase == null)
                return;
            foreach (var beat in _day.Npcs)
                if (!string.IsNullOrEmpty(beat.Until) && beat.Until == phase.Id)
                    Leave(beat);
            foreach (var beat in _day.Npcs)
            {
                if (beat.Phase != phase.Id)
                    continue;
                if (beat.Action == "help")
                    _beats.Add(StartCoroutine(Help(beat)));
                else if (beat.Action == "visit")
                    _beats.Add(StartCoroutine(Visit(beat)));
            }
            // Jacques đang ở bếp mà lượt cọ nồi cao có sóng: khi nồi đã kéo về góc rửa thì sang đứng giữ nồi phía bên kia (thay cánh tay
            // giữ chỗ của trạm nồi cao).
            if (jacques != null && jacques.gameObject.activeSelf && phase.TallSway && phase.Orders.Exists(o => o.Task == OrderKinds.ScrubTall)
                && jacquesTallPot != null)
            {
                if (_arm != null)
                    _arm.SetActive(false);
                _beats.Add(StartCoroutine(JacquesWhenPotHome()));
            }
        }

        /// <summary>Lượt xong: người giúp trong lượt (không có until) và người ghé thăm ra về.</summary>
        public void PhaseEnded(PhaseData phase)
        {
            if (_day == null || phase == null)
                return;
            foreach (var beat in _day.Npcs)
            {
                if (beat.Phase != phase.Id)
                    continue;
                if ((beat.Action == "help" && string.IsNullOrEmpty(beat.Until)) || beat.Action == "visit")
                    Leave(beat);
            }
        }

        /// <summary>Hết ca: mọi người rời bếp (tối có người dạy thì BeginEvening đặt lại).</summary>
        public void EndShift()
        {
            StopBeats();
            HideAll();
            if (_arm != null)
                _arm.SetActive(true);
        }

        /// <summary>Buổi tối có học: người dạy ngồi ở góc đèn dầu. Trả về beat teach (null nếu tối nghỉ).</summary>
        public NpcBeat BeginEvening()
        {
            StopBeats();
            HideAll();
            var beat = _day?.Npcs.Find(b => b.Phase == "evening" && b.Action == "teach");
            if (teachStools != null)
                teachStools.SetActive(beat != null);
            if (silhouette != null && beat != null)
                silhouette.SetActive(false);
            // S16: "trong khi chúng tôi nghỉ hoặc đánh bài, anh Ba đọc hay viết": ông Mai ngồi bàn bài.
            if (cardMai != null)
                cardMai.SetActive(_day != null && _day.Npcs.Exists(b => b.Phase == "evening" && b.Action == "cards" && b.Who == Mai));
            if (beat == null)
                return null;
            var actors = Actors(beat.Who);
            for (int i = 0; i < actors.Count; i++)
            {
                var seat = Spot(teachSeats, i);
                if (seat == null)
                    continue;
                Show(actors[i], seat.position, seat.eulerAngles.y);
                actors[i].Play(i == 0 ? "SitRead" : "SitIdle");
            }
            if (!Sim.Enabled && teachStools != null)
                _beats.Add(StartCoroutine(StoolsUnderHips(actors)));
            return beat;
        }

        /// <summary>
        /// Clip ngồi (Rocketbox) đặt hông lệch khỏi gốc NPC: vài khung sau khi ngồi, dời ghế đẩu tới ngay dưới xương hông để
        /// người dạy ngồi trên ghế chứ không ngồi trên không.
        /// </summary>
        IEnumerator StoolsUnderHips(List<NpcAgent> actors)
        {
            for (int f = 0; f < 3; f++)
                yield return null;
            var stools = teachStools.transform;
            for (int i = 0; i < actors.Count && i < stools.childCount; i++)
            {
                var animator = actors[i] != null ? actors[i].GetComponentInChildren<Animator>() : null;
                var hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
                if (hips == null)
                    continue;
                var stool = stools.GetChild(i);
                var p = hips.position;
                StoolOffset = new Vector2(p.x - actors[i].Position.x, p.z - actors[i].Position.z).magnitude;
                stool.position = new Vector3(p.x, stool.position.y, p.z);
            }
        }

        /// <summary>Khoảng lệch hông–gốc của người dạy lần ngồi gần nhất (m, test).</summary>
        public float StoolOffset { get; private set; }

        /// <summary>Phát lần lượt các câu (ai nói thì làm động tác nói); Sim bỏ qua.</summary>
        public IEnumerator Say(IList<string> lines)
        {
            var root = GameRoot.Instance;
            if (root == null || lines == null || Sim.Enabled)
                yield break;
            Talking = true;
            foreach (var id in lines)
            {
                var speaker = SpeakerAgent(root.Content.GetLine(id).Speaker);
                string before = speaker != null ? speaker.State : null;
                if (speaker != null && speaker.gameObject.activeInHierarchy && !IsSitting(before))
                    speaker.Play("Talk");
                LinesPlayed++;
                yield return root.Subtitles.PlayAndWait(id);
                if (speaker != null && !string.IsNullOrEmpty(before) && !IsSitting(before))
                    speaker.Play(before);
            }
            Talking = false;
        }

        /// <summary>Sau lượt nhặt rau, ông Mai đi vào (từ chỗ đang đứng, hoặc qua hành lang), nói chuyện, rồi đi ra.</summary>
        public IEnumerator SayAfterWork(IList<string> lines)
        {
            if (lines == null || lines.Count == 0) yield break;
            if (Sim.Enabled)
                Show(mai, maiSpot.position, maiSpot.eulerAngles.y);
            else
            {
                MoveTo(mai, PathToGalley(mai, maiSpot.position), maiSpot.eulerAngles.y, "Idle");
                yield return WaitArrived(new List<NpcAgent> { mai });
                FacePlayer(mai);
            }
            yield return Say(lines);
            WalkOut(mai);
        }

        /// <summary>
        /// Đường vào bếp của ông Mai: đang ở trong bếp thì đi thẳng từ chỗ đứng (không biến mất rồi hiện ra ở cửa); đang ở
        /// ngoài thì đi qua hành lang; chưa có mặt thì hiện ở đầu hành lang (ngoài tầm nhìn) rồi đi vào.
        /// </summary>
        Vector3[] PathToGalley(NpcAgent a, Vector3 spot)
        {
            var path = new List<Vector3>();
            if (!a.gameObject.activeSelf)
            {
                Show(a, corridorToGalley.Length > 0 ? corridorToGalley[0] : spot, maiSpot != null ? maiSpot.eulerAngles.y : 0f);
                path.AddRange(corridorToGalley);
            }
            else if (!Inside(a.Position))
                path.AddRange(corridorToGalley);
            path.Add(spot);
            return path.ToArray();
        }

        /// <summary>Ông Mai tới chỗ dạy gọt (PeelStation giữ ông trong lượt gọt nên không qua MoveTo).</summary>
        public IEnumerator BringMai(Vector3 spot)
        {
            foreach (var p in PathToGalley(mai, spot))
                yield return mai.Go(p);
        }

        /// <summary>Đi ra theo đường đã vào rồi ẩn (ngoài tầm nhìn), thay cho biến mất tại chỗ.</summary>
        void WalkOut(NpcAgent a)
        {
            if (a == null || !a.gameObject.activeSelf)
                return;
            if (Sim.Enabled)
            {
                Hide(a);
                return;
            }
            var path = new List<Vector3> { a.Position };
            var route = a == mai ? corridorToGalley : deckToGalley;
            for (int i = route.Length - 1; i >= 0; i--)
                path.Add(route[i]);
            MoveTo(a, path.ToArray(), a.transform.eulerAngles.y, null, 0f, hideAtEnd: true);
        }

        static bool IsSitting(string state) => state == "SitIdle" || state == "SitRead";

        NpcAgent SpeakerAgent(string speaker)
        {
            switch (speaker)
            {
                case "linh_tre": return soldierA;
                case "linh_mu": return soldierB;
                case "jacques": return jacques;
                case "ong_mai": return mai;
                default: return null;
            }
        }

        // ---------- Các kiểu xuất hiện ----------

        IEnumerator Help(NpcBeat beat)
        {
            var actors = Actors(beat.Who);
            bool soldiers = beat.Who == Soldiers;
            for (int i = 0; i < actors.Count; i++)
            {
                var spot = soldiers ? Spot(greensSpots, i) : jacquesPots;
                if (spot == null)
                    continue;
                if (!actors[i].gameObject.activeSelf)
                {
                    var start = deckToGalley.Length > 0 ? deckToGalley[0] + Vector3.right * (0.7f * i) : spot.position;
                    Show(actors[i], start, spot.eulerAngles.y);
                }
                var path = new List<Vector3>();
                if (!Inside(actors[i].Position))
                    path.AddRange(deckToGalley);
                path.Add(spot.position);
                MoveTo(actors[i], path.ToArray(), spot.eulerAngles.y, soldiers ? "WorkTable" : "Idle", i * 0.8f,
                    arrive: soldiers ? null : (System.Func<IEnumerator>)(() => HoldStormPots()));
            }
            if (!Sim.Enabled)
                yield return WaitArrived(actors);
            Raise(beat.Event);
            if (!Sim.Enabled)
                _beats.Add(StartCoroutine(TalkWhenNear(beat, actors, talkRadius, raiseEvent: false)));
            if (!soldiers || greens == null)
                yield break;
            // Hai lính nhặt rau giúp: định kỳ xong một bó trong sọt.
            float interval = Sim.Enabled ? 0.5f : helpInterval;
            while (!_gone.Contains(beat))
            {
                yield return new WaitForSeconds(interval);
                if (_gone.Contains(beat))
                    yield break;
                // Nhặt rau trên mặt bàn: giữ WorkTable (clip PickUp cúi xuống sàn, khuất sau bàn).
                if (greens.HelperSort())
                    HelpedBunches++;
            }
        }

        IEnumerator Visit(NpcBeat beat)
        {
            var actors = Actors(beat.Who);
            if (actors.Count == 0 || maiSpot == null)
                yield break;
            var who = actors[0];
            // Ông Mai vừa ở trong bếp (dạy gọt xong) thì đi tiếp từ chỗ đứng, không hiện lại ở cửa (QA-0012).
            bool alreadyHere = who.gameObject.activeSelf && Inside(who.Position);
            MoveTo(who, PathToGalley(who, maiSpot.position), maiSpot.eulerAngles.y, "Idle");
            if (Sim.Enabled)
            {
                Raise(beat.Event);
                yield break;
            }
            yield return WaitArrived(actors);
            var root = GameRoot.Instance;
            // Đang đứng ngay cạnh Ba thì không cần gọi.
            if (!string.IsNullOrEmpty(beat.Call) && root != null && !(alreadyHere && PlayerNear(actors, visitTalkRadius)))
            {
                who.Play("Point");
                root.Subtitles.Play(beat.Call);
            }
            yield return TalkWhenNear(beat, actors, visitTalkRadius, raiseEvent: true);
            yield return new WaitForSeconds(2f);
            Leave(beat);
        }

        /// <summary>Đợi người chơi lại gần một người trong nhóm rồi phát thoại; raiseEvent: phát beat.Event sau thoại.</summary>
        IEnumerator TalkWhenNear(NpcBeat beat, List<NpcAgent> actors, float radius, bool raiseEvent, System.Func<bool> gate = null,
            bool backToWork = true)
        {
            if (Sim.Enabled)
            {
                if (raiseEvent)
                    Raise(beat.Event);
                yield break;
            }
            var wait = new WaitForSeconds(0.2f);
            while (!PlayerNear(actors, radius) || Talking || (gate != null && !gate()))
                yield return wait;
            if (beat.Lines.Count > 0)
            {
                // Quay sang nói với Ba thì thôi động tác làm việc (tay nhặt rau giữa không khí, QA-0143); nói xong quay lại chỗ làm.
                var yaws = new float[actors.Count];
                var states = new string[actors.Count];
                for (int i = 0; i < actors.Count; i++)
                {
                    var a = actors[i];
                    if (a == null || !a.gameObject.activeInHierarchy || a.Walking || IsSitting(a.State))
                        continue;
                    yaws[i] = a.transform.eulerAngles.y;
                    states[i] = a.State;
                    FacePlayer(a);
                    if (a.State != "Idle" && a.State != "Talk" && a.State != "IdleLook")
                        a.Play("Idle");
                }
                yield return Say(beat.Lines);
                for (int i = 0; backToWork && i < actors.Count; i++)
                    if (states[i] != null && actors[i] != null && actors[i].gameObject.activeInHierarchy && !actors[i].Walking)
                        StartCoroutine(BackToWork(actors[i], yaws[i], states[i]));
            }
            if (raiseEvent)
                Raise(beat.Event);
        }

        static IEnumerator BackToWork(NpcAgent a, float yaw, string state)
        {
            yield return a.Face(yaw);
            if (!a.Walking)
                a.Play(state);
        }

        bool PlayerNear(List<NpcAgent> actors, float radius)
        {
            var rig = PlayerRig.Current;
            if (rig == null)
                return false;
            var p = rig.Position;
            foreach (var a in actors)
            {
                if (a == null || !a.gameObject.activeInHierarchy)
                    continue;
                var d = a.Position - p;
                if (Mathf.Abs(d.y) < 2f && d.x * d.x + d.z * d.z < radius * radius)
                    return true;
            }
            return false;
        }

        void FacePlayer(NpcAgent a)
        {
            var rig = PlayerRig.Current;
            if (rig == null || a == null || !a.gameObject.activeInHierarchy || a.Walking)
                return;
            var d = rig.Position - a.Position;
            if (d.x * d.x + d.z * d.z < 0.01f)
                return;
            StartCoroutine(a.Face(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg));
        }

        void Leave(NpcBeat beat)
        {
            _gone.Add(beat);
            foreach (var a in Actors(beat.Who))
            {
                if (a == null || !a.gameObject.activeSelf)
                    continue;
                if (Sim.Enabled)
                {
                    Hide(a);
                    continue;
                }
                var path = new List<Vector3> { a.Position };
                var route = a == mai ? corridorToGalley : deckToGalley;
                for (int i = route.Length - 1; i >= 0; i--)
                    path.Add(route[i]);
                MoveTo(a, path.ToArray(), a.transform.eulerAngles.y, null, 0f, hideAtEnd: true);
            }
        }

        // ---------- Di chuyển ----------

        void MoveTo(NpcAgent a, Vector3[] path, float yaw, string endState, float delay = 0f, bool hideAtEnd = false,
            System.Func<IEnumerator> arrive = null)
        {
            if (a == null || _peeling && a == mai)
                return;
            if (_moves.TryGetValue(a, out var running) && running != null)
                StopCoroutine(running);
            StopJacquesWork(a);
            if (Sim.Enabled)
            {
                _moves.Remove(a);
                if (hideAtEnd)
                    Hide(a);
                else if (path.Length > 0)
                {
                    a.Place(path[path.Length - 1], yaw);
                    UpdateLayer(a);
                    if (endState != null)
                        a.Play(endState);
                }
                return;
            }
            _moves[a] = StartCoroutine(Walk(a, path, yaw, endState, delay, hideAtEnd, arrive));
        }

        IEnumerator Walk(NpcAgent a, Vector3[] path, float yaw, string endState, float delay, bool hideAtEnd, System.Func<IEnumerator> arrive)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            // Đang nắm nồi thì buông tay, đứng thẳng rồi mới đi.
            if (a == jacques && _jacquesHands != null)
                yield return _jacquesHands.LetGo();
            foreach (var p in path)
                yield return a.Go(p);
            if (hideAtEnd)
            {
                Hide(a);
                yield break;
            }
            yield return a.Face(yaw);
            if (endState != null)
                a.Play(endState);
            _moves.Remove(a);
            if (arrive != null && a == jacques)
                _jacquesWork = StartCoroutine(arrive());
        }

        // ---------- Jacques giữ nồi ----------

        /// <summary>
        /// Bão (ngày 4): Jacques tới trước nồi rời chưa chằng gần nhất trên lò nam, cúi người nắm hai quai giữ nồi khỏi trượt; Ba
        /// chằng xong nồi đó thì sang nồi kế tiếp. Hết nồi thì đứng trông.
        /// </summary>
        public IEnumerator HoldStormPots()
        {
            if (_jacquesHands == null || jacques == null)
                yield break;
            var ahead = jacquesPots != null ? jacquesPots.position : jacques.Position;
            while (true)
            {
                LooseObjectSway pot = null;
                float best = float.MaxValue;
                foreach (var p in stormPots)
                {
                    if (p == null || p.Secured || !p.gameObject.activeInHierarchy)
                        continue;
                    var d = p.transform.position - jacques.Position;
                    float dist = d.x * d.x + d.z * d.z;
                    if (dist < best) { best = dist; pot = p; }
                }
                if (pot == null)
                    break;
                pot.Hold(true);
                _heldPot = pot;
                // Đứng ngay trước nồi, phía lối đi (cùng khoảng cách tới lò như chỗ đứng của Jacques), nhìn vào nồi.
                var at = pot.transform.position;
                var stand = new Vector3(at.x, jacques.Position.y, ahead.z);
                yield return jacques.Go(stand);
                yield return jacques.Face(Mathf.Atan2(at.x - stand.x, at.z - stand.z) * Mathf.Rad2Deg);
                jacques.Play("Idle");
                yield return _jacquesHands.Lean(20f);
                yield return _jacquesHands.HoldHandles(pot.transform);
                while (!pot.Secured && pot.gameObject.activeInHierarchy)
                    yield return null;
                pot.Hold(false);
                _heldPot = null;
                yield return _jacquesHands.LetGo();
            }
            jacques.Play("IdleLook");
            _jacquesWork = null;
        }

        IEnumerator JacquesWhenPotHome()
        {
            while (scrub != null && scrub.Logic.State != TallPotState.HomeDirty)
                yield return null;
            if (jacques == null || !jacques.gameObject.activeSelf)
                yield break;
            MoveTo(jacques, new[] { jacques.Position, jacquesTallPot.position }, jacquesTallPot.eulerAngles.y, "Idle",
                arrive: () => HoldTallPot());
        }

        /// <summary>Nồi cao chao (ngày 4): Jacques đứng phía bên kia nồi, đối diện Ba, hai lòng bàn tay áp hai bên thân nồi giữ cho Ba cọ.</summary>
        public IEnumerator HoldTallPot()
        {
            var pot = scrub != null ? scrub.potRoot : null;
            if (_jacquesHands == null || jacques == null || pot == null)
                yield break;
            var b = NpcHandWork.LocalBounds(pot);
            var center = pot.TransformPoint(new Vector3(b.center.x, b.max.y, b.center.z));
            float radius = Mathf.Min(b.extents.x * pot.lossyScale.x, b.extents.z * pot.lossyScale.z);
            var outward = jacques.Position - center;
            outward.y = 0f;
            outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.back;
            var stand = center + outward * (radius + rimStandOff);
            stand.y = jacques.Position.y;
            yield return jacques.Go(stand);
            yield return jacques.Face(Mathf.Atan2(-outward.x, -outward.z) * Mathf.Rad2Deg);
            jacques.Play("Idle");
            // Nồi cao 1 m, vai Jacques cao hơn chỗ áp tay ~0,5 m: nghiêng 28° và áp tay sát vành (drop 0,1) thì cổ tay mới với tới thân nồi.
            yield return _jacquesHands.Lean(28f);
            yield return _jacquesHands.HoldBody(pot, center, radius, drop: 0.1f);
            if (scrub != null) scrub.JacquesHolding = true;
            _jacquesWork = null;
        }

        void StopJacquesWork(NpcAgent a)
        {
            if (a != jacques)
                return;
            if (_jacquesWork != null)
                StopCoroutine(_jacquesWork);
            _jacquesWork = null;
            if (_heldPot != null)
                _heldPot.Hold(false);
            _heldPot = null;
        }

        IEnumerator WaitArrived(List<NpcAgent> actors)
        {
            float until = Time.time + 40f;
            while (Time.time < until)
            {
                bool moving = false;
                foreach (var a in actors)
                    if (a != null && _moves.ContainsKey(a))
                        moving = true;
                if (!moving)
                    yield break;
                yield return null;
            }
        }

        void Update()
        {
            // Đi giữa boong và bếp: đổi lớp ánh sáng (mặt trời chỉ rọi lớp ngoài trời, đèn bếp chỉ rọi lớp trong khoang).
            if (Time.time >= _nextLayerCheck)
            {
                _nextLayerCheck = Time.time + 0.25f;
                foreach (var a in All)
                    if (a != null && a.gameObject.activeSelf)
                        UpdateLayer(a);
            }
            // Giữ cửa bếp mở khi có người của ngày đi qua (cửa chỉ tự mở cho người chơi).
            if (galleyDoor == null || Time.time < _nextDoorCheck || _moves.Count == 0)
                return;
            _nextDoorCheck = Time.time + 0.5f;
            foreach (var a in _moves.Keys)
                if (a != null && HoldDoorNear(a.Position))
                    break;
        }

        /// <summary>Người đi gần cửa bếp (trong doorAssistRadius) thì mở cửa giữ cho họ đi qua. true nếu đã mở.</summary>
        public bool HoldDoorNear(Vector3 position)
        {
            if (galleyDoor == null)
                return false;
            var d = position - galleyDoor.transform.position;
            if (Mathf.Abs(d.y) >= 2f || d.x * d.x + d.z * d.z >= doorAssistRadius * doorAssistRadius)
                return false;
            galleyDoor.Set(true);
            return true;
        }

        // ---------- Tiện ích ----------

        /// <summary>Đang ở trong khối bếp (bếp, hành lang): x của boong giếng nhỏ hơn mặt ngoài vách tây bếp.</summary>
        static bool Inside(Vector3 p) => p.x > -5.1f && p.y > -0.5f;

        /// <summary>Trong khoang (khối bếp, hoặc dưới boong) thì dùng lớp đèn trong khoang, còn lại lớp ngoài trời.</summary>
        static bool Indoors(Vector3 p) => p.y < -0.5f || p.x > -5.1f;

        void UpdateLayer(NpcAgent a)
        {
            if (!_renderers.TryGetValue(a, out var list))
                return;
            bool inside = Indoors(a.Position);
            if (_indoors.TryGetValue(a, out bool was) && was == inside)
                return;
            _indoors[a] = inside;
            uint mask = inside ? WorldRenderingLayers.Interior : WorldRenderingLayers.Exterior;
            foreach (var r in list)
                if (r != null && !(r is ParticleSystemRenderer))
                    r.renderingLayerMask = mask;
        }

        /// <summary>Lớp ánh sáng hiện tại của một người (test).</summary>
        public bool IsLitAsInterior(NpcAgent a) => !_indoors.TryGetValue(a, out bool inside) || inside;

        List<NpcAgent> Actors(string who)
        {
            var list = new List<NpcAgent>(2);
            switch (who)
            {
                case Soldiers:
                    if (soldierA != null) list.Add(soldierA);
                    if (soldierB != null) list.Add(soldierB);
                    break;
                case Jacques:
                    if (jacques != null) list.Add(jacques);
                    break;
                case Mai:
                    if (mai != null) list.Add(mai);
                    break;
            }
            return list;
        }

        static Transform Spot(Transform[] spots, int i) => spots != null && i < spots.Length ? spots[i] : null;

        void Show(NpcAgent a, Vector3 position, float yaw)
        {
            if (a == null)
                return;
            a.Place(position, yaw);
            if (!a.gameObject.activeSelf)
                a.gameObject.SetActive(true);
            UpdateLayer(a);
        }

        void Hide(NpcAgent a)
        {
            if (a == null || _peeling && a == mai)
                return;
            if (_moves.TryGetValue(a, out var running) && running != null)
                StopCoroutine(running);
            _moves.Remove(a);
            StopJacquesWork(a);
            if (a == jacques && _jacquesHands != null)
                _jacquesHands.Abort();
            if (a.gameObject.activeSelf)
                a.gameObject.SetActive(false);
        }

        void HideAll()
        {
            foreach (var a in All)
                Hide(a);
            _moves.Clear();
        }

        void StopBeats()
        {
            foreach (var c in _beats)
                if (c != null)
                    StopCoroutine(c);
            _beats.Clear();
            Talking = false;
        }

        static void Raise(string id)
        {
            if (!string.IsNullOrEmpty(id))
                GameRoot.Instance?.Events.Raise(id);
        }
    }
}
