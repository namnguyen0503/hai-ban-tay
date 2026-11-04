using System.Collections;
using System.Collections.Generic;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Npc;
using HaiBanTay.Tasks;
using HaiBanTay.Ship;
using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>Nồi giữ nguyên danh tính: nấu → phụ bếp mang về → cọ/tráng → giá sạch. Lịch lấy từ JSON.</summary>
    public sealed class MealPots : MonoBehaviour
    {
        public GameObject[] pots = new GameObject[0];
        public WashStation wash;
        public HelperRoutine helper;
        public CookRoutine cook;
        [Tooltip("Ngày 5 (DIRECTOR §6): mẩu bánh mì quản bếp đặt lên kệ cạnh chậu sau chồng nồi tám chiếc; ẩn tới lúc đó.")]
        public GameObject chefBread;
        [Tooltip("Chỗ quản bếp đứng đặt bánh (hệ bếp): phía bắc chậu rửa, ngoài chồng nồi bẩn, kệ và giá úp.")]
        public Vector3 breadStand = new Vector3(6.2f, 0f, -0.7f);
        public TallPotStation tall;
        public PanStation pan;
        public NpcAgent porter;
        [Tooltip("Ô bếp (mặt nắp gang từng ô, builder đặt): nồi nấu và nồi cao nằm đúng ô, mỗi ô một nồi (W3.2).")]
        public Transform[] hobs = new Transform[0];
        public CoalStation coal;
        public const float CookingSeconds = 2f;
        readonly Dictionary<GameObject, float> _heated = new Dictionary<GameObject, float>();
        readonly Dictionary<GameObject, ParticleSystem[]> _steam = new Dictionary<GameObject, ParticleSystem[]>();
        readonly HashSet<GameObject> _steaming = new HashSet<GameObject>();
        float _tallHeated;
        readonly List<GameObject> _cooking = new List<GameObject>();
        readonly Dictionary<GameObject, int> _hobOf = new Dictionary<GameObject, int>();
        int _tallHob = -1, _panHob = -1;
        readonly Queue<GameObject> _dirty = new Queue<GameObject>();
        readonly HashSet<GameObject> _clean = new HashSet<GameObject>();
        Vector3[] _homes;
        Quaternion[] _rotations;
        bool _cookingTall;
        Coroutine _cookAside;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int MaskTex = Shader.PropertyToID("_MaskTex");
        MaterialPropertyBlock _block;

        public int Cooking => _cooking.Count;
        /// <summary>Nồi đang nấu (test, oracle POT_HOB đọc).</summary>
        public IReadOnlyList<GameObject> CookingPots => _cooking;
        /// <summary>Ô bếp của nồi cao đang nấu, −1 nếu không nấu.</summary>
        public int TallHob => _tallHob;
        public int HobOf(GameObject pot) => _hobOf.TryGetValue(pot, out int h) ? h : -1;
        public bool UsesHob(Transform hob)
        {
            for (int i = 0; i < hobs.Length; i++)
                if (hobs[i] == hob && (i == _tallHob || i == _panHob || _hobOf.ContainsValue(i))) return true;
            return false;
        }
        public bool ReadyToServe
        {
            get
            {
                foreach (var pot in _cooking)
                    if (!_heated.TryGetValue(pot, out float seconds) || seconds < CookingSeconds) return false;
                return !_cookingTall || _tallHeated >= CookingSeconds;
            }
        }
        public float HotSeconds(GameObject pot) => _heated.TryGetValue(pot, out float seconds) ? seconds : 0f;
        bool HobHot(int hob, Vector3 pot)
        {
            if (coal == null || hob < 0 || hob >= hobs.Length || hobs[hob] == null ||
                (pot - hobs[hob].position).sqrMagnitude > 0.0036f) return false;
            return coal.CellState(coal.CellForHob(hobs[hob]))?.Hot ?? false;
        }
        void Update() => TickCooking(Time.deltaTime);
        public void TickCooking(float dt)
        {
            foreach (var pot in _cooking)
            {
                bool hot = HobHot(HobOf(pot), pot.transform.position);
                if (hot) _heated[pot] = Mathf.Min(CookingSeconds, HotSeconds(pot) + Mathf.Max(0f, dt));
                Steam(pot, hot);
            }
            if (_cookingTall)
            {
                // Nồi cao chỉ chín khi nằm (sạch) trên đúng ô bếp nóng của nó: Ba phải kéo ra và nhấc lên trước (tall_out).
                bool hot = tall.CookReady && HobHot(_tallHob, tall.potRoot.position);
                if (hot) _tallHeated = Mathf.Min(CookingSeconds, _tallHeated + Mathf.Max(0f, dt));
                Steam(tall.potRoot.gameObject, hot);
            }
        }
        public int Pending => _dirty.Count;
        public int CookedTotal { get; private set; }
        public bool TallCooked { get; private set; }
        /// <summary>Số lần NPC không tới được chỗ trong hạn và được đặt thẳng vào chỗ (QA-0004, bot ghi MAJOR).</summary>
        public int TimedOutWalks { get; private set; }
        [Tooltip("Hạn mỗi đoạn đi của NPC = gấp đôi thời gian đi bình thường (1 m/s, bê nồi 0,8 m/s) + chừng này giây; quá hạn thì đặt thẳng vào chỗ.")]
        public float walkSlackSeconds = 5f;
        [Tooltip("NPC đứng gần như yên (dưới 0,25 m) chừng này giây khi đang phải đi thì coi như kẹt.")]
        public float stallSeconds = 6f;
        /// <summary>Phụ bếp chờ quản bếp rời chỗ khuấy tối đa chừng này (giây): ông đang giữa vòng khuấy thì có khi mất hơn 8 s mới bước đi.</summary>
        const float CookClearWaitSeconds = 20f;
        /// <summary>Quản bếp đang đi trong bán kính này quanh điểm đầu tuyến bưng nồi của phụ bếp thì phụ bếp chưa xuất phát (m).</summary>
        const float CookLaneRadius = 4f;
        [Tooltip("Đợi đầu bếp đặt thùng khoai xuống tối đa chừng này giây.")]
        public float cookTimeout = 10f;

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            _homes = new Vector3[pots.Length];
            _rotations = new Quaternion[pots.Length];
            for (int i = 0; i < pots.Length; i++)
            {
                _homes[i] = pots[i].transform.position;
                _rotations[i] = pots[i].transform.rotation;
                _clean.Add(pots[i]);
                _steam[pots[i]] = pots[i].GetComponentsInChildren<ParticleSystem>(true);
                Steam(pots[i], false);
                foreach (var reset in pots[i].GetComponents<ReturnToStart>()) reset.enabled = false;
                foreach (var sway in pots[i].GetComponents<LooseObjectSway>()) sway.enabled = false;
                var rb = pots[i].GetComponent<Rigidbody>();
                if (rb != null) { rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None; }
                if (i >= 3) pots[i].SetActive(false);
            }
            if (tall != null)
            {
                _steam[tall.potRoot.gameObject] = tall.potRoot.GetComponentsInChildren<ParticleSystem>(true);
                Steam(tall.potRoot.gameObject, false);
            }
            wash.meals = this;
        }

        /// <summary>Chảo đồng đang nằm trên bếp (sau bữa còn chờ kéo về chỗ rửa) chiếm ô dưới nó, kể cả khi lượt không nấu chảo (QA-0148).</summary>
        bool UnderPan(int h)
        {
            if (pan == null || pan.pan == null || !pan.OnRange) return false;
            var d = hobs[h].position - pan.pan.position;
            d.y = 0f;
            return d.magnitude < 0.45f;
        }

        /// <summary>Ô trống gần điểm cho trước nhất; −1 nếu hết ô (days.vi.json không để quá số ô, ChoreContentTests kiểm).</summary>
        int FreeHob(Vector3 near)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int h = 0; h < hobs.Length; h++)
            {
                if (hobs[h] == null || h == _tallHob || h == _panHob || _hobOf.ContainsValue(h) || UnderPan(h)) continue;
                float d = (hobs[h].position - near).sqrMagnitude;
                if (d < bestD) { bestD = d; best = h; }
            }
            return best;
        }

        /// <param name="instant">Lượt tua nhanh (màn đen): nồi cao đi tức thời qua các bước tới trên ô bếp.</param>
        public void BeginMeal(PhaseData phase, bool instant = false)
        {
            _cooking.Clear();
            _hobOf.Clear();
            _heated.Clear();
            _tallHeated = 0f;
            _tallHob = -1;
            _panHob = -1;
            _cookingTall = phase.CookTall && tall != null;
            // Nồi cao giữ ô của nó (ô giữa lò bắc); chảo đồng nấu cùng bữa giữ ô cạnh đó (ngày 3); nồi bữa lấy các ô còn lại.
            if (_cookingTall)
            {
                _tallHob = tall.hob != null ? System.Array.IndexOf(hobs, tall.hob) : FreeHob(Galley(-2.2f, 0.9f, 2.45f));
                if (instant || Sim.Enabled) tall.FastForwardTo(TallPotState.OnHobClean);
            }
            if (phase.CookPan && pan != null && !pan.OnRange && pan.stops.Length > 0 && pan.stops[0] != null)
                _panHob = FreeHob(new Vector3(pan.stops[0].position.x, 0.9f, Galley(0f, 0f, 2.45f).z));
            for (int i = 0; i < pots.Length && _cooking.Count < phase.Dirty; i++)
                if (_clean.Remove(pots[i]))
                    PutOnStove(i);
            // Chưa đủ nồi sạch (Ba chưa rửa kịp): phụ bếp khác cọ vội mấy chiếc ở chồng nồi bẩn. Không ném lỗi giữa coroutine.
            while (_cooking.Count < phase.Dirty && _dirty.Count > 0)
            {
                var pot = TakeDirty();
                wash.RemoveDirty(1);
                PutOnStove(System.Array.IndexOf(pots, pot));
            }
            if (_cooking.Count != phase.Dirty)
                Debug.LogError($"MealPots: {phase.Id} cần {phase.Dirty} nồi, chỉ có {_cooking.Count} (kiểm days.vi.json).");
            if (porter != null) porter.Play("IdleLook");
            if (phase.CookPan && pan != null) pan.BeginCooking();
            if (_cookingTall)
                Steam(tall.potRoot.gameObject, false);
        }

        void PutOnStove(int i)
        {
            var pot = pots[i];
            _rackSlotOf.Remove(pot);
            pot.transform.SetParent(transform, true);
            int hob = FreeHob(_homes[i]);
            if (hob >= 0) _hobOf[pot] = hob;
            else Debug.LogError($"MealPots: hết ô bếp cho {pot.name} (kiểm days.vi.json: số nồi nấu cùng lúc ≤ {hobs.Length}).");
            pot.transform.SetPositionAndRotation(hob >= 0 ? hobs[hob].position : _homes[i], _rotations[i]);
            pot.SetActive(true);
            foreach (var c in pot.GetComponentsInChildren<Collider>()) c.enabled = false;
            Mark(pot, false);
            Lid(pot, true);
            Steam(pot, false);
            Broth(pot, true);
            _cooking.Add(pot);
            if (cook != null && cook.sacks.InTub > 0) AddPotatoes();
        }

        public void AddPotatoes()
        {
            foreach (var pot in _cooking)
            {
                var food = pot.transform.Find("CookingPotatoes");
                if (food != null) food.gameObject.SetActive(true);
            }
        }

        /// <summary>Nồi vừa nấu xong về chồng nồi bẩn. instant (lượt montage, màn đã đen): đặt thẳng, không NPC đi bộ.</summary>
        public IEnumerator Serve(bool instant = false)
        {
            // Lượt tua nhanh không có thao tác: ô bếp nguội hay nồi cao lệch chỗ thì ca treo mãi dưới màn đen (bot 23/10, D5 bữa tối).
            if (instant && !ReadyToServe)
            {
                Debug.LogWarning($"MealPots: lượt tua nhanh, nồi chưa chín (nồi cao {(tall != null ? tall.Logic.State.ToString() : "-")}, ô {_tallHob}); coi như đã nấu xong.");
                foreach (var pot in _cooking) _heated[pot] = CookingSeconds;
                if (_cookingTall) _tallHeated = CookingSeconds;
            }
            while (!ReadyToServe) yield return null;
            // Vừa nấu xong mới bám cặn; NPC mang chính các object đó tới chồng nồi.
            foreach (var pot in _cooking) { Steam(pot, false); Mark(pot, true); Lid(pot, false); Broth(pot, false); }
            if (pan != null) pan.Served();
            bool helperRunning = helper != null && helper.enabled;
            bool cookRunning = cook != null && cook.enabled;
            bool cookAvoided = cook != null && cook.agent.avoidOthers;
            if (helper != null) helper.enabled = false;
            if (cook != null)
            {
                cook.agent.avoidOthers = true;
                float until = Time.time + cookTimeout;
                // Chờ cả lúc thùng đang hạ xuống / nhấc lên (chưa tính là bê): tắt giữa chừng thì thùng phải nhảy về chỗ (QA-0154).
                while (cookRunning && (cook.agent.Carrying != null || cook.TubOut) && Time.time < until) yield return null;
                if (cook.agent.Carrying != null || cook.TubOut)
                {
                    Debug.LogWarning($"MealPots: đầu bếp vẫn cầm {(cook.agent.Carrying != null ? cook.agent.Carrying.name : "thùng (đang nhấc/hạ)")} sau {cookTimeout:0}s, trả thùng về chỗ.");
                    cook.ReturnTubBeforePause();
                }
                cook.enabled = false;
                // Quản bếp đứng khuấy ngay trước nồi, đúng chỗ phụ bếp phải đứng bưng nồi đó: lùi về chỗ trông bếp nhường chỗ (bot
                // 14/10: phụ bếp chờ quá hạn rồi bị đặt thẳng vào chỗ). Lượt montage, Sim thì không đi bộ.
                // Quản bếp đi thẳng, không nhường (phụ bếp nhường ông); cả hai cùng nhường thì đứng chờ nhau giữa lối.
                if (cookRunning && !Sim.Enabled && !instant)
                {
                    cook.agent.avoidOthers = false;
                    _cookAside = StartCoroutine(cook.agent.Go(cook.watchSpot));
                }
            }
            // Đường Sim sẵn có bỏ thời gian đi bộ; bot/người chơi vẫn dùng NPC thật.
            var agent = Sim.Enabled || instant ? null : helper != null ? helper.agent : porter;
            bool avoided = agent != null && agent.avoidOthers;
            if (agent != null) agent.avoidOthers = true;
            foreach (var pot in _cooking)
            {
                if (agent != null)
                {
                    var local = transform.InverseTransformPoint(pot.transform.position);
                    var toPot = new[] { Galley(-2.2f, 0f, 0.8f), Galley(local.x, 0f, local.z > 0f ? 1.5f : -1.85f) };
                    // Quản bếp còn đứng (hay đang lùi ra) sát chỗ bưng nồi này: chờ ông đi khỏi rồi mới vào.
                    // Quản bếp đi thẳng về chỗ trông bếp qua đúng lối chéo giữa hai lò, phụ bếp đi ngược chiều trên lối đó thì hai người gặp nhau
                    // giữa lối hẹp (chồng thân, đứng đối mặt 4 s ở (−2,0; 0,5)): chờ ông đi hết lối, hoặc ông không còn đứng sát điểm đầu.
                    // Phụ bếp đang đứng đúng chỗ trông bếp quản bếp sắp tới (cách dưới 0,9 m): lùi sang chỗ đứng quầy bên cạnh trước, không để ông
                    // bước vào người mình.
                    if (cook != null && agent != cook.agent && Flat(agent.Position - cook.watchSpot) < 0.9f && Flat(cook.agent.Position - cook.watchSpot) > 0.9f)
                        yield return Within(agent, new[] { Galley(0f, 0f, 1.63f) }, 1f);
                    for (float wait = 0f; wait < CookClearWaitSeconds && cook != null && agent != cook.agent
                                          && (Flat(cook.agent.Position - toPot[1]) < 1.2f || Flat(cook.agent.Position - toPot[0]) < 0.9f
                                              || (cook.agent.Walking && Flat(cook.agent.Position - toPot[0]) < CookLaneRadius)); wait += Time.deltaTime)
                        yield return null;
                    yield return Within(agent, toPot, 1f);
                    // Hai tay bám hai quai nồi rồi nhấc nồi lên trước bụng (NpcHandWork); NPC không có tay IK thì như cũ.
                    var hands = agent.GetComponent<NpcHandWork>();
                    if (hands != null)
                    {
                        var d = pot.transform.position - agent.Position;
                        yield return agent.Face(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
                        pot.transform.rotation = Quaternion.Euler(0f, agent.transform.eulerAngles.y, 0f);
                        yield return hands.HoldSides(pot.transform);
                        var carry = agent.carryPoint != null ? agent.carryPoint : agent.transform;
                        yield return NpcHandWork.Move(pot.transform, carry.position, carry.rotation, 0.4f);
                    }
                    else yield return agent.Do("Idle", 0.8f);
                    agent.PickUp(pot.transform);
                    // Phòng rửa mở rộng: vào qua cửa vách ngăn x 3,3 (z −0,3…0,9) rồi ra chồng nồi.
                    yield return Within(agent, new[] { Galley(-1.5f, 0f, 0.9f), Galley(2.8f, 0f, 0.4f), Galley(4.3f, 0f, 0.3f), Galley(5.5f, 0f, -0.4f), Galley(5.6f, 0f, -1.4f) }, 0.8f);
                }
                var position = PilePosition(_dirty.Count);
                if (agent != null)
                {
                    var hands = agent.GetComponent<NpcHandWork>();
                    if (hands != null) yield return NpcHandWork.Move(pot.transform, position, Quaternion.identity, 0.4f);
                    agent.PutDown(position, Quaternion.identity, transform);
                    if (hands != null) yield return hands.ReleaseBoth(0.3f);
                }
                else pot.transform.SetPositionAndRotation(position, Quaternion.identity);
                _dirty.Enqueue(pot);
                CookedTotal++;
                wash.AddDirty(1);
            }
            _cooking.Clear();
            _hobOf.Clear();
            _panHob = -1;
            if (_cookingTall)
            {
                // Nồi cao ở lại trên bếp, bám cặn: lượt scrub_tall (hoặc lượt tua nhanh) nhấc xuống, kéo về cọ.
                _tallHob = -1;
                tall.Served();
                Steam(tall.potRoot.gameObject, false);
                TallCooked = true;
            }
            if (cook != null)
            {
                if (_cookAside != null)
                {
                    StopCoroutine(_cookAside);
                    cook.agent.ClearWalkState();
                    _cookAside = null;
                }
                cook.agent.avoidOthers = cookAvoided;
                cook.enabled = cookRunning;
            }
            // Phụ bếp đi về chỗ làm trong lúc lượt sau đã bắt đầu: lượt không chờ đoạn đi về.
            if (agent != null) StartCoroutine(WalkBack(agent, avoided, helperRunning));
            else if (helper != null) helper.enabled = helperRunning;
        }

        /// <summary>
        /// Ngày 5, xong chồng nồi tám chiếc: quản bếp ghé phòng rửa, đặt mẩu bánh mì lên kệ cạnh chậu rồi về lò, không nói gì
        /// (DIRECTOR §6, hư cấu; cử chỉ tử tế duy nhất của ông).
        /// </summary>
        public IEnumerator ChefBread()
        {
            if (cook == null || chefBread == null || chefBread.activeSelf)
                yield break;
            bool running = cook.enabled;
            cook.enabled = false;
            var a = cook.agent;
            a.ClearWalkState();
            yield return Within(a, new[] { Galley(breadStand.x, 0f, breadStand.z) }, 1f);
            var d = chefBread.transform.position - a.Position;
            yield return a.Face(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
            a.Play("WorkTable");
            yield return new WaitForSeconds(0.7f);
            chefBread.SetActive(true);
            yield return new WaitForSeconds(0.6f);
            a.Play("Idle");
            yield return Within(a, new[] { cook.watchSpot }, 1f);
            cook.enabled = running;
        }

        public void HideBread()
        {
            if (chefBread != null) chefBread.SetActive(false);
        }

        IEnumerator WalkBack(NpcAgent agent, bool avoided, bool helperRunning)
        {
            yield return Within(agent, new[] { Galley(5.5f, 0f, -0.4f), Galley(4.3f, 0f, 0.3f), Galley(2.8f, 0f, 0.4f), Galley(-2.2f, 0f, 0.8f), Galley(-3.4f, 0f, -1.85f) }, 1f);
            agent.avoidOthers = avoided;
            if (helper != null) helper.enabled = helperRunning;
        }

        /// <summary>
        /// Một đoạn đi của NPC có hạn (QA-0004): quá walkTimeout thì dừng đoạn đi, đặt NPC vào điểm cuối và ghi cảnh báo, để
        /// lượt phục vụ không chờ mãi một NPC bị chắn.
        /// </summary>
        IEnumerator Within(NpcAgent agent, Vector3[] path, float nominalSpeed)
        {
            float length = 0f;
            var from = agent.Position;
            foreach (var p in path) { length += Vector3.Distance(new Vector3(from.x, 0f, from.z), new Vector3(p.x, 0f, p.z)); from = p; }
            float limit = 2f * length / nominalSpeed + walkSlackSeconds;
            bool finished = false;
            var routine = StartCoroutine(Track(agent.WalkPath(path), () => finished = true));
            float until = Time.time + limit, movedAt = Time.time;
            var last = agent.Position;
            while (!finished && Time.time < until && Time.time - movedAt < stallSeconds)
            {
                if ((agent.Position - last).sqrMagnitude > 0.25f * 0.25f)
                {
                    last = agent.Position;
                    movedAt = Time.time;
                }
                yield return null;
            }
            if (finished)
                yield break;
            var stuckAt = agent.Position;
            bool waiting = agent.Waiting;
            string diag = agent.WalkDiag();
            StopCoroutine(routine);
            agent.ClearWalkState();
            var end = path[path.Length - 1];
            var d = end - agent.Position;
            agent.Place(end, d.sqrMagnitude > 1e-4f ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg : agent.transform.eulerAngles.y);
            TimedOutWalks++;
            Debug.LogWarning($"MealPots: {agent.name} không tới {end} (hạn {limit:0}s, đứng yên quá {stallSeconds:0}s), đặt thẳng vào chỗ. " +
                             $"Đứng ở {stuckAt}, chờ: {(waiting ? agent.BlockedBy : "không")}. {diag}");
        }

        static IEnumerator Track(IEnumerator inner, System.Action done)
        {
            yield return inner;
            done();
        }

        static float Flat(Vector3 d) => new Vector2(d.x, d.z).magnitude;

        /// <summary>
        /// Điểm viết theo hệ khối bếp. MealPots nằm ngay dưới layout.galley ở gốc của nó (cảnh bếp và cảnh cuối Marseille); khối bếp
        /// chỉ trùng gốc thế giới ở cảnh bếp, ở Marseille tàu cập kè chỗ khác và quay ngược (bot 15/10: phụ bếp đi ra ngoài tàu).
        /// </summary>
        Vector3 Galley(float x, float y, float z) => transform.TransformPoint(new Vector3(x, y, z));

        Vector3 PilePosition(int i) => wash.pile.transform.position + new Vector3((i % 2 - 0.5f) * 0.36f, (i / 2) * 0.25f, 0f);

        public GameObject TakeDirty()
        {
            if (_dirty.Count == 0) return null;
            var pot = _dirty.Dequeue();
            int i = 0;
            foreach (var other in _dirty) other.transform.position = PilePosition(i++);
            return pot;
        }

        readonly Dictionary<GameObject, int> _rackSlotOf = new Dictionary<GameObject, int>();

        /// <summary>Chỗ trên giá đang có nồi nào (−1 nếu nồi không ở giá).</summary>
        public int RackSlotOf(GameObject pot) => _rackSlotOf.TryGetValue(pot, out int s) ? s : -1;

        /// <summary>
        /// Úp nồi sạch lên giá (QA-0133): chỗ trống đầu tiên của wash.rackSlots, vành nồi úp nằm đúng mặt kệ (gốc nồi ở đáy, nồi úp thì đáy
        /// lên trên: gốc cao hơn chỗ đặt đúng chiều cao nồi). Giá không có chỗ (cảnh cũ) thì theo hàng cũ.
        /// </summary>
        public void Rack(GameObject pot, int index)
        {
            pot.transform.SetParent(transform, true);
            _rackSlotOf.Remove(pot);
            var slots = wash.rackSlots;
            if (slots != null && slots.Length > 0)
            {
                int slot = FreeRackSlot(slots);
                var (_, height) = WashStation.PotSize(pot);
                var at = slots[slot];
                pot.transform.SetPositionAndRotation(at.position + at.up * height, at.rotation * Quaternion.Euler(180f, 0f, 0f));
                _rackSlotOf[pot] = slot;
            }
            else
            {
                index %= pots.Length;
                pot.transform.SetPositionAndRotation(wash.rack.transform.TransformPoint(new Vector3(-0.42f + index % 3 * 0.35f, 0.29f + index / 3 * 0.24f, 0f)), Quaternion.Euler(180f, 0f, 0f));
            }
            Mark(pot, false);
            _clean.Add(pot);
        }

        int FreeRackSlot(Transform[] slots)
        {
            for (int s = 0; s < slots.Length; s++)
                if (!_rackSlotOf.ContainsValue(s)) return s;
            // Giá đầy (không xảy ra với 8 nồi, 8 chỗ): chồng lên chỗ đầu.
            return 0;
        }

        public void Mark(GameObject pot, bool dirty)
        {
            foreach (var renderer in pot.GetComponentsInChildren<MeshRenderer>())
            {
                var material = renderer.sharedMaterial;
                // Lớp phủ bẩn của bàn chải (DirtOverlay, mặt nạ riêng) không nhuộm theo nồi; vật liệu không có màu nền thì bỏ qua
                // (material.color đọc _Color: shader DirtOverlay không có, Unity báo lỗi mỗi lần đánh dấu nồi — bot ngày 1 brush17b).
                if (material == null || !material.HasProperty(BaseColor) || material.HasProperty(MaskTex)) continue;
                renderer.GetPropertyBlock(_block);
                _block.SetColor(BaseColor, dirty ? new Color(0.12f, 0.085f, 0.045f) : material.GetColor(BaseColor));
                renderer.SetPropertyBlock(_block);
            }
            var food = pot.transform.Find("CookingPotatoes");
            if (food != null) food.gameObject.SetActive(false);
        }

        static void Lid(GameObject pot, bool on)
        {
            // Tìm cả dưới LidPivot: quản bếp có thể đang cầm nắp nhấc ra cạnh nồi (NpcHandWork.Stir).
            foreach (var t in pot.GetComponentsInChildren<Transform>(true))
                if (t.name == "MealLid" || t.name == "LidKnob") t.gameObject.SetActive(on);
        }

        static void Broth(GameObject pot, bool on)
        {
            var broth = pot.transform.Find("Broth");
            if (broth != null) broth.gameObject.SetActive(on);
        }

        void Steam(GameObject pot, bool on)
        {
            if (!_steam.TryGetValue(pot, out var systems)) return;
            if (on == _steaming.Contains(pot) && (on || !System.Array.Exists(systems, ps => ps.isPlaying))) return;
            if (on) _steaming.Add(pot); else _steaming.Remove(pot);
            foreach (var steam in systems)
                if (on) steam.Play(); else steam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
