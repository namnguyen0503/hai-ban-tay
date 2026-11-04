using System.Collections.Generic;
using System.Collections;
using HaiBanTay.Player;
using UnityEngine;
using UnityEngine.AI;

namespace HaiBanTay.Npc
{
    /// <summary>
    /// Thân NPC: đi theo lộ trình thẳng (NpcStepLogic), quay mặt, phát trạng thái Animator theo tên (Idle, Walk, CarryWalk,
    /// Stir, WorkTable, WorkMid, PickUp, HoldCarry, Point, Talk, SitIdle, SitRead), bê vật trước bụng. Chưa có nhân vật
    /// thật (khối xám) thì chỉ di chuyển, không có animation. Người chơi chắn đường thì đứng chờ.
    /// Đám đông (CrowdBuilder bật avoidOthers, yieldToPlayer): thêm nhường nhau giữa các NPC (CrowdSpace), chờ quá lâu thì
    /// vòng qua, chờ NPC quá nữa thì đi xuyên; người chơi đẩy vào NPC quá lâu thì NPC mở lối. CrowdLod bật/tắt Animator
    /// (SetAnimated) cho NPC ở xa.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class NpcAgent : MonoBehaviour
    {
        public float walkSpeed = 1.1f;
        public float carrySpeed = 0.8f;
        public float turnSpeed = 300f;
        /// <summary>Điểm bê vật (trước bụng, hai tay).</summary>
        public Transform carryPoint;
        /// <summary>Luôn chạy Animator dù ngoài khung hình hay xa (CrowdLod không bake tư thế tĩnh): đang làm mẫu bằng tay thật
        /// (IK vào vật cầm), không được hiện bằng mesh tĩnh.</summary>
        [System.NonSerialized] public bool keepAnimated;
        /// <summary>Nhường và vòng qua các NPC khác khi đi (đám đông). Mặc định tắt để NPC trong bếp giữ nguyên hành vi cũ.</summary>
        public bool avoidOthers;
        /// <summary>Người chơi đẩy vào NPC lâu quá thì NPC thôi chặn (tắt collider tới khi người chơi đi xa).</summary>
        public bool yieldToPlayer;
        /// <summary>Né vật cản cố định (tường, bàn, tủ) trên đường đi bằng cách rẽ qua góc vật cản (NpcPathing). Kẹt quá lâu thì đi xuyên như cũ.</summary>
        public bool avoidStatic = true;
        /// <summary>
        /// Nhân vật quan trọng (W5.2): mỗi đoạn đi tính đường trên NavMesh của cảnh (NavMeshLoader) rồi đi qua các góc của đường,
        /// không cắt qua bàn, lò, tường. Cảnh không có NavMesh hay đích ngoài NavMesh thì đi như cũ.
        /// </summary>
        public bool useNavMesh;
        /// <summary>Bán kính thân khi né vật cản (m); bê vật to thì nở thêm.</summary>
        public float bodyRadius = 0.25f;
        /// <summary>Đang cầm đồ dài chìa ra trước người (xẻng của phụ bếp): người chơi đứng phía trước trong khoảng thường + chừng này thì chờ.</summary>
        [System.NonSerialized] public float extraPlayerClearance;
        /// <summary>
        /// Hộp bao đồ dài đang cầm (xẻng của phụ bếp): người chơi sát hộp thì đứng chờ, cả lúc xoay người (Face). Xét người chơi trước
        /// mặt thôi thì lưỡi xẻng chìa chéo bên trái vẫn quét vào hông Ba đứng cạnh (bot ngày 3 18/10: lún 10–14 cm).
        /// </summary>
        [System.NonSerialized] public Collider playerGuard;
        /// <summary>Lệch pha animation (giây) cho clip lặp, để đám đông không cử động cùng nhịp.</summary>
        public float phaseSeconds;
        /// <summary>Đi dốc (cầu thang tàu): độ cao theo tỉ lệ quãng đường tới đích thay vì giữ nguyên. Mặc định tắt (sàn phẳng).</summary>
        public bool followSlope;
        /// <summary>Trạng thái Animator khi đi và khi đứng lúc đang bê vật (phu vác vai: ShoulderCarry/ShoulderIdle, khách xách va li: BagWalk/BagIdle, bồi bàn bưng khay: TrayCarry).</summary>
        public string carryWalkState = "CarryWalk";
        public string carryIdleState = "HoldCarry";
        /// <summary>Đang bê vật thì Play("Idle") của routine đổi thành carryIdleState (vác vai, xách va li đứng đúng dáng, không buông tay).</summary>
        public bool carryRemapIdle;

        /// <summary>Chờ liên tục quá mức này thì vòng qua người chơi chắn đường (giây).</summary>
        public const float SidestepAfter = 1.2f;
        /// <summary>NPC đi NavMesh bị người chơi chắn quá chừng này giây thì vòng qua bên cạnh người chơi.</summary>
        public const float PlayerDetourAfter = 0.8f;
        /// <summary>NPC khác chắn đường thì chỉ chờ chút rồi vòng qua (người đi ngược chiều né sớm để không xuyên nhau).</summary>
        public const float NpcSidestepAfter = 0.25f;
        /// <summary>Bị NPC khác chắn quá mức này thì đi xuyên qua (giây).</summary>
        public const float GhostAfter = 4f;
        /// <summary>Đích cách NPC không quá mức này mà vẫn bị chắn thì coi như đích đã bị chiếm (m).</summary>
        const float OccupiedRadius = 1.4f;
        const float SidestepSeconds = 0.7f;
        const float GhostSeconds = 2.5f;
        const float CrowdCheckInterval = 0.15f;
        const float ContactRadius = 0.75f;
        const float ContactClearRadius = 1.05f;
        const float PlayerYieldAfter = 0.8f;
        /// <summary>
        /// Ba ép vào ĐỒ NPC cầm/vác (LimitPlayerStep vừa cắt bước) cộng dồn chừng này giây mới nhường (tắt va chạm, Ba đi xuyên cả người
        /// lẫn đồ). Lướt qua hay chạm nhẹ thì Ba trượt dọc đồ, không xuyên (bot 01/11: hông Ba vào muôi, ngực vào bao — QA-0151/0152).
        /// Không dài hơn: đồ của người đứng ở chỗ làm (muôi trên nồi trước cửa tro, xẻng trực lò) chặn Ba tới chỗ làm, bot lượt 2 kẹt
        /// 90 s ở cửa tro ngày 3 với 2,5 s. Cộng dồn qua các lần ép ngắt quãng (ContactMemory), không thì ép chập chờn không bao giờ đủ.
        /// </summary>
        public const float ItemYieldAfter = 1.2f;
        /// <summary>NPC chờ đồ chạm Ba quá ReachYieldAfter thì được bước dù sát, nhưng đồ không lún vào vỏ thân Ba quá mức này (m).</summary>
        public const float ReachSinkMax = 0.03f;
        /// <summary>Đồ chạm Ba chặn NPC liền quá chừng này giây thì đường lui cuối: đi tiếp dù lún (game không được kẹt).</summary>
        public const float ReachLastResort = 2.5f;
        /// <summary>Không chạm/ép liền chừng này giây thì quên thời gian ép đã cộng.</summary>
        const float ContactMemory = 2f;
        /// <summary>
        /// NPC đang nhường (tắt va chạm thân) mà Ba ép vào đồ đang cầm/vác: lùi xa Ba với tốc độ này (m/s), dưới ngưỡng tính là "đi" của
        /// MotionProbe (0,3 m/s) nên không thành đi giật lùi. Đồ vẫn chặn Ba (LimitPlayerStep): Ba trượt dọc đồ rồi qua, không xuyên đồ.
        /// </summary>
        public const float GiveWaySpeed = 0.25f;
        /// <summary>Lùi không được (vật tĩnh sau lưng) cộng dồn chừng này giây thì đường lui cuối: Ba đi xuyên cả đồ như cũ, không kẹt.</summary>
        public const float GiveWayCorneredAfter = 1.5f;
        const float ReplanInterval = 0.25f;
        // Quá số lần rẽ này vẫn bị chắn (không có đường vòng) thì đi thẳng như cũ một lúc, không để NPC kẹt vĩnh viễn.
        const int MaxDetours = 4;
        const float StaticGhostSeconds = 2.5f;

        static readonly WaitForSeconds ContactTick = new WaitForSeconds(0.2f);

        Transform _tf;
        Animator _animator;
        Collider _collider;
        string _state;
        float _yaw, _yawVelocity;
        /// <summary>Gia tốc quay người (độ/s²): quay 90° mất ~0,6 s, tốc độ quay tăng dần (đồ đang cầm không vụt đi ở khung đầu).</summary>
        public const float TurnAccel = 900f;
        float _lastTurnAt = float.NegativeInfinity;
        bool _animated = true;
        bool _ghosted;
        float _ghostUntil;
        float _nextCrowdCheck;
        NpcAgent _crowdBlocker;

        public Transform Carrying { get; private set; }
        public string State => _state;
        public bool Waiting { get; private set; }
        /// <summary>Đang trong một lần WalkTo (kể cả lúc đứng chờ): người khác coi là "đang đi", không chờ lẫn nhau.</summary>
        public bool Walking { get; private set; }
        /// <summary>Lần chờ gần nhất bị ai/cái gì chắn (chẩn đoán: cảnh báo quá hạn của MealPots, bot).</summary>
        public string BlockedBy { get; private set; } = "";
        /// <summary>Điểm đang đi tới (góc đường / điểm vòng) và có đang đi xuyên vật tĩnh vì kẹt không: cho oracle bot ghi nguyên nhân.</summary>
        public Vector3 WalkGoal { get; private set; }
        public bool PassingStatic { get; private set; }
        /// <summary>Số hiệu trong CrowdSpace: nhỏ hơn thì được đi trước khi hai người cùng đi.</summary>
        public int CrowdId { get; private set; }
        /// <summary>Tốc độ đi hiện tại (m/s).</summary>
        public float Speed => _speedNow;
        /// <summary>Đã đứng chờ người ở đích / người đi trước bao lâu trong quãng đi này (test đọc).</summary>
        public float HoldSeconds => _holdSeconds;
        /// <summary>Hướng đi gần nhất (mặt phẳng XZ, chuẩn hóa); không đi thì bằng không.</summary>
        public Vector3 Heading { get; private set; }

        public Vector3 Position
        {
            get
            {
                EnsureInit();
                return _tf.position;
            }
        }
        /// <summary>Animator đang chạy (CrowdLod tắt khi NPC ở xa).</summary>
        public bool Animated => _animated;
        /// <summary>Đã chờ liên tục bao lâu (giây). Về 0 khi đi tiếp được.</summary>
        public float WaitSeconds { get; private set; }
        /// <summary>Lần chờ liên tục dài nhất từ lúc ResetStats (kiểm tra kẹt).</summary>
        public float LongestWait { get; private set; }

        void Awake()
        {
            EnsureInit();
        }

        /// <summary>Cache tham chiếu một lần. Routine cùng đối tượng có thể chạy trước Awake của NpcAgent nên các hàm công khai cũng gọi.</summary>
        void EnsureInit()
        {
            if (_tf != null)
                return;
            _tf = transform;
            _animator = GetComponentInChildren<Animator>();
            _collider = GetComponent<Collider>();
            if (carryPoint != null)
            {
                _carryHomePos = carryPoint.localPosition;
                _carryHomeRot = carryPoint.localRotation;
            }
            _yaw = _tf.eulerAngles.y;
            if (GetComponent<NpcUpright>() == null)
                gameObject.AddComponent<NpcUpright>();
            // Va li builder cho cầm sẵn (CrowdBuilder.GiveBag): PickUp chạy lúc dựng cảnh, Carrying không lưu vào cảnh. Lấy lại từ con
            // của điểm bê, không thì khách đi bằng clip Walk thường, tay vung, va li lơ lửng bên hông (đo xuyên thân 15/10). Đồ khác
            // treo sẵn ở điểm bê (khay bồi bàn, cốc đá của Lê) do kịch bản riêng cầm lên, không đụng.
            if (Carrying == null && carryPoint != null && carryWalkState == "BagWalk")
                for (int i = 0; i < carryPoint.childCount; i++)
                    if (carryPoint.GetChild(i).gameObject.activeSelf)
                    {
                        Carrying = carryPoint.GetChild(i);
                        break;
                    }
        }

        void OnEnable()
        {
            EnsureInit();
            CrowdId = CrowdSpace.Register(this);
            if (!All.Contains(this)) All.Add(this);
            Walking = false;
            Waiting = false;
            if (yieldToPlayer)
                StartCoroutine(WatchPlayerContact());
        }

        void OnDisable()
        {
            CrowdSpace.Unregister(this);
            All.Remove(this);
            Walking = false;
            Waiting = false;
            Heading = Vector3.zero;
            if (_ghosted)
                SetSolid(true);
        }

        public void ResetStats()
        {
            WaitSeconds = 0f;
            LongestWait = 0f;
        }

        /// <summary>Phát trạng thái Animator nếu có (khối xám thì bỏ qua). fade âm: tự chọn (vào/ra dáng đi: WalkFade, còn lại 0,25 s).</summary>
        public void Play(string state, float fade = -1f)
        {
            EnsureInit();
            if (carryRemapIdle && Carrying != null && state == "Idle")
                state = carryIdleState;
            if (_state == state)
                return;
            if (fade < 0f)
                fade = IsWalk(_state) && (state == "Idle" || state == carryIdleState) ? WalkStopFade
                    : IsWalk(state) || IsWalk(_state) ? WalkFade : 0.25f;
            _state = state;
            if (_animated)
                CrossFade(state, fade);
        }

        void CrossFade(string state, float fade)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null)
                return;
            int hash = Animator.StringToHash(state);
            if (!_animator.HasState(0, hash))
                return;
            if (phaseSeconds > 0f && IsLooping(state))
                _animator.CrossFadeInFixedTime(hash, fade, 0, phaseSeconds);
            else
                _animator.CrossFadeInFixedTime(hash, fade, 0);
        }

        /// <summary>
        /// Chuyển giữa đi và đứng: 0,25 s thì cổ tay đang vung trước người 30 cm vụt về bên hông (3,7 cm/khung 60 fps, đo giật 14/10).
        /// </summary>
        public const float WalkFade = 0.4f;

        /// <summary>
        /// Đang đi thì đứng lại (Idle/CarryIdle: tới nơi, bị Ba chắn lối; động tác khác vẫn WalkFade): tay đang vung trước người 35 cm về hông. 0,4 s thì cổ tay về 2 cm/nhịp 60 fps, đúng
        /// ngưỡng giật, đỏ hay xanh tùy pha vung lúc bị chặn (CI 01/11, quản bếp dừng vì Ba đứng lối bàn rau). Dài hơn thì về êm.
        /// </summary>
        public const float WalkStopFade = 0.6f;

        /// <summary>
        /// Tăng tốc khi bắt đầu đi, hãm khi tới (m/s²): đạt tốc độ đi ngay trong một khung thì thân, nồi, muôi đang bê giật từ đứng
        /// yên sang 1 m/s (đo giật 14/10).
        /// </summary>
        public const float Accel = 3f;
        float _speedNow;
        float _blockedTotal;
        bool _inNav;

        bool IsWalk(string state) => state == "Walk" || state == carryWalkState;

        static bool IsLooping(string state) => state != "PickUp" && state != "Point" && state != "SitDown" && state != "StandUp"
                                               && state != "Wave" && state != "HandOver";

        /// <summary>
        /// Bật/tắt Animator (CrowdLod). Tắt thì Play chỉ ghi nhớ trạng thái; bật lại thì phát lại trạng thái hiện tại.
        /// </summary>
        public void SetAnimated(bool on)
        {
            EnsureInit();
            if (_animated == on)
                return;
            _animated = on;
            if (_animator == null)
                return;
            _animator.enabled = on;
            if (on && !string.IsNullOrEmpty(_state))
                CrossFade(_state, 0.15f);
        }

        /// <summary>Routine bị dừng giữa chừng một lần WalkTo (StopAllCoroutines): xóa cờ đang đi, đang chờ.</summary>
        public void ClearWalkState()
        {
            Walking = false;
            Waiting = false;
            WaitSeconds = 0f;
            Heading = Vector3.zero;
            _speedNow = 0f;
            // StopAllCoroutines không chạy khối finally của WalkNav: xóa cờ ở đây.
            _inNav = _chained = _legCut = false;
            _blockedTotal = _holdSeconds = 0f;
        }

        /// <summary>Dịch tức thời tới vị trí và hướng (hành khách lên tàu rồi quay lại cuối hàng).</summary>
        public void TeleportTo(Vector3 position, float yaw)
        {
            EnsureInit();
            _turnFrame = Time.frameCount;
            _yaw = yaw;
            _yawVelocity = 0f;
            _speedNow = 0f;
            _tf.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        }

        /// <summary>Thân NPC đang hiện (false: đang khuất trong khoang tàu, xem SetBodyActive).</summary>
        public bool BodyActive
        {
            get
            {
                EnsureInit();
                var body = _tf.Find("Body");
                return body == null || body.gameObject.activeSelf;
            }
        }

        /// <summary>Ẩn/hiện thân NPC (con "Body") và collider: người đã khuất sau cửa khoang. Routine vẫn chạy.</summary>
        public void SetBodyActive(bool on)
        {
            EnsureInit();
            var body = _tf.Find("Body");
            if (body != null)
                body.gameObject.SetActive(on);
            if (_collider != null)
                _collider.enabled = on && !_ghosted;
        }

        public IEnumerator WalkPath(params Vector3[] points)
        {
            foreach (var p in points)
                yield return Go(p);
        }

        /// <summary>Một đoạn đi: theo NavMesh nếu useNavMesh, không thì đi thẳng (né vật cản như cũ).</summary>
        public IEnumerator Go(Vector3 target) => useNavMesh ? WalkNav(target) : WalkTo(target);

        /// <summary>Hai thân (bán kính 0,24 m) đứng cách nhau dưới chừng này là chen vào cùng một chỗ.</summary>
        public const float SpotRadius = 0.7f;

        /// <summary>Routine đang đứng chờ việc khác (người chơi đứng trong đường lưỡi xẻng): bảng QA đọc Waiting như lúc chờ trên đường.</summary>
        public void SetWaiting(bool waiting) => Waiting = waiting;

        /// <summary>
        /// Chỗ đứng spot đang có NPC khác (quản bếp đứng khuấy đúng trên cửa lò phụ bếp cần nạp) thì đứng chờ ở chỗ mình tới khi trống
        /// (tối đa maxWait giây) rồi mới đi tới: không chen vào rồi xuyên qua người nhau (clip 14/10 23:4x).
        /// </summary>
        public IEnumerator GoWhenFree(Vector3 spot, float maxWait = 10f)
        {
            for (float t = 0f; t < maxWait && CrowdSpace.Occupant(spot, SpotRadius, this) != null; t += Time.deltaTime)
            {
                Waiting = true;
                Play(Carrying != null ? carryIdleState : "Idle");
                TurnToward(spot - _tf.position);
                _tf.rotation = Quaternion.Euler(0f, _yaw, 0f);
                yield return null;
            }
            Waiting = false;
            yield return Go(spot);
        }

        NavMeshPath _navPath;
        readonly Vector3[] _corners = new Vector3[32];

        /// <summary>Đường trên NavMesh từ chỗ đứng tới đích (các góc, kể cả điểm đầu); false nếu không có đường trọn vẹn.</summary>
        public bool NavCorners(Vector3 target, out int count)
        {
            EnsureInit();
            count = 0;
            _navPath ??= new NavMeshPath();
            if (!NavMesh.SamplePosition(_tf.position, out var from, 0.6f, NavMesh.AllAreas)
                || !NavMesh.SamplePosition(target, out var to, 0.6f, NavMesh.AllAreas)
                || !NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _navPath)
                || _navPath.status != NavMeshPathStatus.PathComplete)
                return false;
            count = _navPath.GetCornersNonAlloc(_corners);
            return count > 0;
        }

        public Vector3 Corner(int i) => _corners[i];

        /// <summary>
        /// NavMesh bake theo bán kính thân; đang bê đồ thì thân cộng đồ cần rộng hơn. Góc đường nào còn sát vật tĩnh (NpcPathing.FreeAt
        /// với radius) thì đẩy ra phía ngoài khúc quanh, từng 2 cm, tối đa 25 cm, tới khi đủ chỗ, miễn là đoạn từ góc trước và tới góc
        /// sau vẫn nằm trọn trên NavMesh. Điểm đầu, điểm cuối giữ nguyên. Oracle NPC_ROUTE kiểm đúng đường này.
        /// </summary>
        public static void Widen(Vector3[] corners, int count, float radius, Transform self)
        {
            for (int i = 1; i < count - 1; i++)
            {
                var c = corners[i];
                if (NpcPathing.FreeAt(c, radius, self)) continue;
                var a = corners[i - 1] - c; a.y = 0f;
                var b = corners[i + 1] - c; b.y = 0f;
                if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) continue;
                // Vật bị đường vòng qua nằm phía trong khúc quanh: đẩy góc ra phía ngược lại.
                var outward = -(a.normalized + b.normalized);
                if (outward.sqrMagnitude < 1e-4f) continue;
                outward.Normalize();
                for (float d = 0.02f; d <= 0.25f; d += 0.02f)
                {
                    if (!NavMesh.SamplePosition(c + outward * d, out var hit, 0.05f, NavMesh.AllAreas)) break;
                    var p = hit.position;
                    if (NavMesh.Raycast(corners[i - 1], p, out _, NavMesh.AllAreas) || NavMesh.Raycast(p, corners[i + 1], out _, NavMesh.AllAreas)) break;
                    if (!NpcPathing.FreeAt(p, radius, self)) continue;
                    corners[i] = p;
                    break;
                }
            }
        }

        // Đoạn đi đang chạy bị cắt ngang để tính lại đường (vòng qua người chơi, lách người); đang nối nhiều đoạn (không về Idle giữa
        // hai góc đường, kẻo clip đi bị phát lại từ đầu mỗi góc).
        bool _legCut, _chained;
        const int MaxNavLegs = 40;

        /// <summary>
        /// Đi tới đích theo NavMesh, mỗi lần một góc đường rồi tính lại đường từ chỗ đang đứng: vòng qua người chơi, lách người giữa
        /// chừng thì đi tiếp từ chỗ mới, không quay lại góc cũ (test lối bàn sơ chế 14/10: phụ bếp đi tới đi lui quanh Ba 20 s). Đoạn
        /// cuối là đúng đích (chỗ đứng sát bàn/lò có thể ngoài mép NavMesh). Không có đường trên NavMesh thì đi thẳng như cũ.
        /// </summary>
        public IEnumerator WalkNav(Vector3 target)
        {
            _blockedTotal = 0f;
            _holdSeconds = 0f;
            _inNav = true;
            try
            {
                for (int leg = 0; leg < MaxNavLegs; leg++)
                {
                    var goal = target;
                    bool last = true;
                    if (NavCorners(target, out int n) && n > 2)
                    {
                        var corners = new Vector3[n];
                        System.Array.Copy(_corners, corners, n);
                        if (Carrying != null) Widen(corners, n, bodyRadius + 0.08f, _tf);
                        int next = 1;
                        var here = _tf.position;
                        while (next < n - 1 && new Vector2(corners[next].x - here.x, corners[next].z - here.z).sqrMagnitude < 0.15f * 0.15f) next++;
                        if (next < n - 1)
                        {
                            goal = corners[next];
                            goal.y = here.y + (followSlope ? goal.y - here.y : 0f);
                            last = false;
                        }
                    }
                    _legCut = false;
                    _chained = !last;
                    yield return WalkTo(goal);
                    if (last && !_legCut) yield break;
                }
                _chained = false;
                yield return WalkTo(target);
            }
            finally
            {
                _chained = false;
                _inNav = false;
            }
        }

        public IEnumerator WalkTo(Vector3 target)
        {
            return WalkCore(target, null);
        }

        /// <summary>Đi tới một Transform, đọc lại vị trí mỗi khung (điểm đích nằm trong vật cha có thể dời, ví dụ bến lùi xa).</summary>
        public IEnumerator WalkTo(Transform target)
        {
            return WalkCore(target.position, target);
        }

        IEnumerator WalkCore(Vector3 fixedTarget, Transform follow)
        {
            EnsureInit();
            var pos = _tf.position;
            Walking = true;
            _legCut = false;
            _nextCrowdCheck = 0f;
            _crowdBlocker = null;
            // Đi NavMesh: thời gian bị chắn cộng dồn qua các lần tính lại đường (lách hỏng → tính lại đường → chờ → lách hỏng…), nếu
            // không thì không bao giờ tới ngưỡng đi xuyên GhostAfter và đứng kẹt mãi (verify 14/10: phụ bếp đứng 6 s sau quản bếp).
            if (!_inNav)
            {
                _blockedTotal = 0f;
                _holdSeconds = 0f;
            }
            float clearFor = 0f;
            // Né vật cản cố định: điểm trung gian (góc vật cản) khi đường thẳng tới đích bị chắn.
            bool hasVia = false, aroundPlayer = false;
            Vector3 via = default;
            float nextReplan = 0f, staticGhostUntil = 0f;
            int detours = 0;
            while (true)
            {
                // GiveWay (Update) có thể vừa lùi thân khỏi Ba: bước tiếp từ chỗ đang đứng thật.
                pos = _tf.position;
                var finalTarget = follow != null ? follow.position : fixedTarget;
                var target = hasVia ? via : finalTarget;
                if (avoidStatic && !followSlope && !hasVia && Time.time >= nextReplan && Time.time >= staticGhostUntil)
                {
                    nextReplan = Time.time + ReplanInterval;
                    var toFinal = finalTarget - pos;
                    toFinal.y = 0f;
                    if (toFinal.sqrMagnitude > 0.6f * 0.6f
                        && NpcPathing.Plan(pos, finalTarget, Carrying != null ? bodyRadius + 0.08f : bodyRadius, _tf, out var detour))
                    {
                        if (detours < MaxDetours)
                        {
                            via = detour;
                            hasVia = true;
                            target = via;
                            detours++;
                        }
                        else
                            staticGhostUntil = Time.time + StaticGhostSeconds;
                    }
                }
                var heading = target - pos;
                heading.y = 0f;
                WalkGoal = target;
                PassingStatic = Time.time < staticGhostUntil;
                var player = PlayerRig.Current;
                // Đang vòng qua người chơi theo NavMesh: điểm vòng đã cách người chơi đủ thân, chỉ chặn khi sắp chạm hẳn.
                bool ahead = player != null && NpcStepLogic.Blocked(pos, heading, player.Position,
                    (aroundPlayer ? 0.55f : 0.8f) + extraPlayerClearance, aroundPlayer ? 35f : 50f);
                // Đang vác đồ trên vai (bao khoai): đồ nhô ngoài thân, đi ngang sát người chơi thì bao quệt ngực (bot 23/10: lún 14,5 cm). Người
                // chơi ngang người dưới 0,75 m cũng là chắn.
                if (!ahead && player != null && Carrying != null && !aroundPlayer)
                    ahead = NpcStepLogic.Blocked(pos, heading, player.Position, 0.75f, 95f);
                // Chỉ vì đồ dài đang cầm sát người chơi: đứng chờ, không vòng hay lách (vòng thì lưỡi xẻng quét theo, bot 18/10 vòng vào hộp).
                bool guardOnly = !ahead && GuardTouches(player);
                // Bước trước đó bị chặn vì đồ cầm/vác sắp lún vào thân Ba (ReachAllows): chờ như người chơi đứng chắn (chờ, lách, vòng).
                bool reach = !ahead && !guardOnly && Time.time < _reachHoldUntil;
                bool byPlayer = ahead || guardOnly || reach;
                bool byObstacle = BlockedByObstacle(pos, heading);
                bool byNpc = avoidOthers && CheckCrowd(pos, heading);
                Waiting = byPlayer || byObstacle || byNpc;
                if (Waiting)
                {
                    _speedNow = 0f;
                    // Đứng chờ vẫn xoay nốt về hướng định đi (đang quay dở thì hãm dần, không đứng khựng); xoay mà đồ quét vào Ba thì thôi.
                    TurnGuarded(heading);
                    BlockedBy = byPlayer ? "người chơi" : byObstacle ? "vật cản" : "NPC " + (_crowdBlocker != null ? _crowdBlocker.name : "?");
                    WaitSeconds += Time.deltaTime;
                    _blockedTotal += Time.deltaTime;
                    clearFor = 0f;
                    if (WaitSeconds > LongestWait)
                        LongestWait = WaitSeconds;
                    // NPC đi NavMesh (bếp, người theo ngày): người chơi đứng chắn lối (bàn rau, lối giữa bếp) thì vòng qua bên cạnh
                    // trên NavMesh, không đứng chờ tới khi người chơi đi chỗ khác.
                    // NPC khác đứng yên chắn lối (quản bếp đứng trông bếp giữa lối) cũng vòng qua như vậy; NPC đang đi thì chờ nó đi qua.
                    if (useNavMesh && !byObstacle && !aroundPlayer && !guardOnly && WaitSeconds >= PlayerDetourAfter)
                    {
                        bool standing = byNpc && !byPlayer && _crowdBlocker != null && (!_crowdBlocker.Walking || _crowdBlocker.Waiting);
                        if ((byPlayer && !byNpc) || standing)
                        {
                            var blocker = byPlayer ? player.Position : _crowdBlocker.Position;
                            if (PlayerDetour(pos, finalTarget, blocker, out var around))
                            {
                                via = around;
                                hasVia = aroundPlayer = true;
                                WaitSeconds = 0f;
                                if (standing) _ghostUntil = Time.time + 1.5f;
                                continue;
                            }
                        }
                    }
                    // NPC chắn là người đang đi cùng hướng phía trước: chờ người đó đi tiếp, không lách ngang ra trước mặt người ta hay
                    // đi xuyên (verify 15/10: phụ bếp lách theo sát lưng quản bếp lúc dọn bữa, tâm cách 0,42 m).
                    bool npcAhead = byNpc && !byPlayer && _crowdBlocker != null && _crowdBlocker.Walking && !_crowdBlocker.Waiting
                                    && Vector3.Dot(_crowdBlocker.Heading, heading.normalized) > 0.5f;
                    if (avoidOthers && !byObstacle && !npcAhead && !guardOnly)
                    {
                        // Điểm đích đang có người đứng (người chơi đứng ngay trên điểm): coi như đã tới, không chầu chực mãi.
                        if (WaitSeconds >= SidestepAfter && heading.sqrMagnitude <= OccupiedRadius * OccupiedRadius)
                            break;
                        // Chờ lâu thì lách qua; bị NPC chắn lâu hơn nữa thì đi xuyên để không kẹt vĩnh viễn.
                        if (_blockedTotal >= GhostAfter && byNpc && !byPlayer)
                            _ghostUntil = Time.time + GhostSeconds;
                        else if (WaitSeconds >= (byPlayer ? SidestepAfter : NpcSidestepAfter))
                        {
                            var toBlocker = byPlayer ? player.Position - pos : _crowdBlocker.Position - pos;
                            yield return Sidestep(heading, toBlocker);
                            pos = _tf.position;
                            WaitSeconds = 0f;
                            _nextCrowdCheck = 0f;
                            // Đi NavMesh: lách xong thì tính lại đường từ chỗ mới.
                            if (useNavMesh)
                            {
                                _legCut = true;
                                break;
                            }
                            continue;
                        }
                    }
                    Play(Carrying != null ? carryIdleState : "Idle");
                    yield return null;
                    pos = _tf.position;
                    continue;
                }
                WaitSeconds = 0f;
                clearFor += Time.deltaTime;
                if (clearFor > 0.6f)
                    _blockedTotal = 0f;
                bool steered = false;
                NpcAgent holdFor = null;
                float behind = float.MaxValue;
                // Lách bên cạnh người khác cả khi không nhường (quản bếp lùi ra chỗ trông bếp lúc dọn bữa đi thẳng, verify 15/10: lướt
                // sát phụ bếp đang đứng 0,42 m); chờ người ở đích / đi theo sau người phía trước chỉ khi có nhường (avoidOthers).
                var steer = useNavMesh ? SteerAround(pos, heading, finalTarget, avoidOthers, out steered, out holdFor, out behind) : heading;
                if (holdFor != null)
                {
                    // Đích còn người đứng, hay người đi trước đã sát lưng: chờ ngoài mép thân người đó (người khác thấy mình đứng chờ
                    // thì vòng qua).
                    _holdSeconds += Time.deltaTime;
                    _speedNow = 0f;
                    Waiting = true;
                    BlockedBy = "NPC " + holdFor.name;
                    TurnToward(heading);
                    _tf.rotation = Quaternion.Euler(0f, _yaw, 0f);
                    Play(Carrying != null ? carryIdleState : "Idle");
                    yield return null;
                    pos = _tf.position;
                    continue;
                }
                Play(Carrying != null ? carryWalkState : "Walk");
                float speed = Carrying != null ? carrySpeed : walkSpeed;
                // Quay người rồi mới bước: đích lệch hướng mặt nhiều thì đi chậm lại, quá ~75° thì đứng xoay tại chỗ (clip 14/10: phụ
                // bếp vừa bước vừa xoay, hướng đi lệch hướng mặt tới 133°, trông như đi giật lùi).
                speed *= NpcStepLogic.AlignedSpeed(_yaw, steered ? steer : heading);
                // Đích cuối của cả quãng đi: hãm dần cho dừng đúng chỗ (góc đường giữa chừng thì đi luôn, không hãm).
                if (!_chained && !hasVia)
                    speed = Mathf.Min(speed, NpcStepLogic.BrakeSpeed(heading.magnitude, Accel));
                speed = Mathf.Min(speed, behind);
                // Tăng tốc dần; giảm thì ngay (đích lệch mặt phải đứng xoay, không trượt tiếp theo đà mà đi giật lùi).
                _speedNow = speed < _speedNow ? speed : NpcStepLogic.Ramp(_speedNow, speed, Accel, Time.deltaTime);
                bool arrived;
                var stepFrom = pos;
                if (steered)
                {
                    // Đang lách qua bên cạnh người khác: bước theo hướng lách, chưa tính là tới góc đường.
                    pos += steer * (_speedNow * Time.deltaTime);
                    arrived = false;
                }
                else
                    arrived = NpcStepLogic.Step(ref pos, target, _speedNow, Time.deltaTime, hasVia ? 0.12f : 0.05f, followSlope);
                // Bước nào cũng không đưa thân vào vật tĩnh: lách người hay đi tắt góc lệch khỏi tuyến thì trượt dọc mép vật; kẹt quá
                // StaticSlideMax mới đi xuyên như cũ (bot 23/10: phu lách nhau chồng ghế đá, cây me, thành cầu tàu; đầu bếp chồng Wall_S).
                // Cả trên dốc (cầu tàu): mặt dốc thấp hơn đáy capsule FreeAt, chỉ thành chặn hai bên tính là vật.
                // Luật cứng cho mọi NPC, kể cả khi tắt avoidStatic (avoidStatic chỉ là tự tìm góc vòng): đầu bếp ăn trưa tắt né vật trong phòng
                // ăn nên lách nhau/lách Ba là đi thẳng vào vách (QA-0150).
                if (Time.time >= staticGhostUntil && !GuardStep(stepFrom, ref pos))
                {
                    StaticRefusals++;
                    arrived = false;
                    _speedNow = 0f;
                    _staticStuck = NpcStepLogic.StuckClock(ref _stuckAnchor, ref _stuckSince, ref _stuckLast, stepFrom, Time.time);
                    nextReplan = 0f;
                    if (hasVia && !aroundPlayer)
                        hasVia = false;
                    if (_staticStuck > StaticSlideMax)
                    {
                        // Kẹt thật: người chơi không thấy thì đặt thẳng tới điểm đang đi (nếu trống), không đi xuyên vật. Đang trong tầm nhìn
                        // thì đứng thử trượt tiếp, quá StaticSeenMax mới đi xuyên như cũ (không kẹt vĩnh viễn).
                        bool seen = SeenByPlayer(stepFrom) || SeenByPlayer(target);
                        var jump = new Vector3(target.x, followSlope ? target.y : stepFrom.y, target.z);
                        if (!seen && NpcPathing.FreeAt(jump, (bodyRadius + 0.02f) / 0.9f, _tf))
                        {
                            pos = jump;
                            // Điểm vừa nhảy tới là góc vòng đã bỏ (hasVia tắt ở trên) thì đi tiếp tới đích, không dừng ở góc.
                            arrived = hasVia || (target - finalTarget).sqrMagnitude < 1e-4f;
                            _stuckSince = -1f;
                        }
                        else if (!seen || _staticStuck > StaticSeenMax)
                        {
                            staticGhostUntil = Time.time + StaticGhostSeconds;
                            _stuckSince = -1f;
                        }
                    }
                }
                float yawBefore = _yaw;
                TurnToward(steered ? steer : heading);
                // Đồ đang cầm/vác (bao trên vai, xẻng) không được lún vào thân Ba sau bước này (QA-0150): đứng lại, lượt sau tính như bị
                // người chơi chắn.
                if (!ReachAllows(pos, _yaw))
                {
                    pos = stepFrom;
                    _yaw = yawBefore;
                    _yawVelocity = 0f;
                    _speedNow = 0f;
                    arrived = false;
                }
                _tf.SetPositionAndRotation(pos, Quaternion.Euler(0f, _yaw, 0f));
                Heading = steered ? steer : heading.sqrMagnitude > 1e-6f ? heading.normalized : Vector3.zero;
                if (arrived)
                {
                    if (!hasVia)
                        break;
                    // Đi NavMesh: tới điểm vòng qua người chơi thì để WalkNav tính lại đường từ đây (góc cũ có thể đã ở sau lưng).
                    if (useNavMesh && aroundPlayer)
                    {
                        _legCut = true;
                        break;
                    }
                    hasVia = aroundPlayer = false;
                    nextReplan = 0f;
                }
                yield return null;
            }
            Waiting = false;
            WaitSeconds = 0f;
            Walking = false;
            Heading = Vector3.zero;
            if (!_chained && !_legCut)
            {
                _speedNow = 0f;
                Play(Carrying != null ? carryIdleState : "Idle");
            }
        }

        /// <summary>
        /// Điểm vòng qua người chơi đang đứng chắn: ngang người chơi một thân rưỡi về bên trái hoặc phải, nằm trên NavMesh, đi thẳng
        /// từ chỗ đứng tới đó và từ đó tới đích đều không vướng mép NavMesh (bàn, lò). Chọn bên ngắn hơn.
        /// </summary>
        static bool PlayerDetour(Vector3 pos, Vector3 target, Vector3 player, out Vector3 via)
        {
            via = default;
            var ahead = player - pos;
            ahead.y = 0f;
            if (ahead.sqrMagnitude < 1e-4f
                || !NavMesh.SamplePosition(pos, out var from, 0.6f, NavMesh.AllAreas)
                || !NavMesh.SamplePosition(target, out var to, 0.6f, NavMesh.AllAreas))
                return false;
            ahead.Normalize();
            var side = Vector3.Cross(Vector3.up, ahead);
            float best = float.MaxValue;
            foreach (float off in new[] { 0.85f, -0.85f, 1.1f, -1.1f })
            {
                var p = player + side * off + ahead * 0.2f;
                p.y = from.position.y;
                if (!NavMesh.SamplePosition(p, out var hit, 0.15f, NavMesh.AllAreas)
                    || NavMesh.Raycast(from.position, hit.position, out _, NavMesh.AllAreas)
                    || NavMesh.Raycast(hit.position, to.position, out _, NavMesh.AllAreas))
                    continue;
                float cost = Vector3.Distance(pos, hit.position) + Vector3.Distance(hit.position, target);
                if (cost >= best)
                    continue;
                best = cost;
                via = new Vector3(hit.position.x, pos.y, hit.position.z);
            }
            return best < float.MaxValue;
        }

        /// <summary>Tâm hai người đi ngang qua nhau cách nhau ít nhất chừng này (hai thân 0,24 m chạm vai ở 0,48 m; nhắm 0,5 m thì quay người trễ còn 0,47 m).</summary>
        public const float BodyGap = 0.55f;

        /// <summary>Bắt đầu lái tránh khi thân người kia trong vòng BodyGap + chừng này.</summary>
        const float SteerLook = 0.45f;
        /// <summary>Chờ người đang đứng ở đích rời đi tối đa chừng này giây mỗi quãng đi (cộng dồn qua các lần tính lại đường).</summary>
        const float HoldMax = 4f;
        float _holdSeconds;

        /// <summary>
        /// Lách qua bên cạnh người khác thay vì đi xuyên: người ở phía trước (trong vòng BodyGap + SteerLook) mà đi thẳng thì hai tâm
        /// gần hơn BodyGap → nhắm điểm cách tâm người đó BodyGap về phía bên mình đang lệch (lệch đúng giữa thì sang trái), bước theo
        /// hướng đó, mặt quay theo. Có hiệu lực cả lúc đang được bỏ qua nhường nhau để thoát kẹt (verify 15/10: quản bếp đi xuyên phụ
        /// bếp đứng chờ 0,58 s, tâm cách 0,18 m). Hướng lách vướng vật tĩnh / mép NavMesh, hay đích nằm ngay chỗ người kia đứng thì
        /// đi như cũ.
        /// </summary>
        Vector3 SteerAround(Vector3 pos, Vector3 heading, Vector3 goal, bool yields, out bool steered, out NpcAgent hold, out float follow)
        {
            steered = false;
            hold = null;
            follow = float.MaxValue;
            var other = CrowdSpace.Nearest(this, pos, BodyGap + SteerLook);
            if (other == null)
                return heading;
            var o = other.Position;
            var toGoal = goal - o;
            toGoal.y = 0f;
            var h = heading;
            h.y = 0f;
            float reach = h.magnitude;
            if (toGoal.sqrMagnitude < BodyGap * BodyGap)
            {
                // Đích nằm ngay chỗ người đó đứng (phụ bếp chưa rời cửa lò dưới nồi quản bếp tới khuấy; verify 15/10: đi vào nhau
                // 0,77 s): tới mép thân người đó thì đứng chờ; quá HoldMax giây (hai người đổi chỗ cho nhau) thì đi vào như cũ.
                var me = pos - o;
                me.y = 0f;
                if (yields && me.sqrMagnitude < (BodyGap + 0.05f) * (BodyGap + 0.05f) && _holdSeconds < HoldMax)
                    hold = other;
                return heading;
            }
            if (reach < 1e-4f)
                return heading;
            h /= reach;
            var rel = o - pos;
            rel.y = 0f;
            float along = Vector3.Dot(rel, h);
            var perp = rel - h * along;
            // Người ở sau lưng, đi thẳng vẫn cách đủ, hay góc đường tới trước khi gặp người đó: không lách.
            if (along <= 0f || perp.magnitude >= BodyGap || along - BodyGap > reach)
                return heading;
            // Người phía trước đi cùng hướng: đi theo sau bằng tốc độ người đó, sát quá thì đứng chờ; không vượt sát vai trong lối
            // hẹp (verify 15/10: phụ bếp bám lưng quản bếp lúc dọn bữa, tâm cách 0,42 m).
            if (yields && other.Walking && !other.Waiting && Vector3.Dot(other.Heading, h) > 0.5f)
            {
                if (rel.sqrMagnitude < (BodyGap + 0.05f) * (BodyGap + 0.05f) && _holdSeconds < HoldMax)
                    hold = other;
                else
                    follow = other.Speed;
                return heading;
            }
            var side = perp.sqrMagnitude > 1e-6f ? -perp.normalized : Vector3.Cross(h, Vector3.up);
            float lateral = perp.magnitude;
            // Bên đang lệch sẵn (dạt ít nhất); bên đó vướng (người kia đứng sát lò, bàn) thì vòng sang bên kia (clip 15/10: quản
            // bếp đi dọc mặt lò, phụ bếp đứng chờ hơi lệch ra lối; lách phía lò bị chặn nên đi xuyên 0,97 s, tâm cách 0,14 m).
            for (int k = 0; k < 2; k++)
            {
                var dir = k == 0 ? Aim(h, along, side, BodyGap - lateral) : Aim(h, along, -side, BodyGap + lateral);
                var probe = pos + dir * 0.3f;
                if (avoidStatic && !NpcPathing.FreeAt(probe, Carrying != null ? bodyRadius + 0.08f : bodyRadius, _tf))
                    continue;
                if (NavMesh.Raycast(pos, probe, out _, NavMesh.AllAreas))
                    continue;
                steered = true;
                return dir;
            }
            return heading;
        }

        /// <summary>Hướng nhắm chỗ bên cạnh người phía trước; tới sát ngang người thì vẫn còn thành phần tiến (lệch tối đa ~63°).</summary>
        static Vector3 Aim(Vector3 h, float along, Vector3 side, float shift) => (h * Mathf.Max(along, 0.25f) + side * Mathf.Min(shift, 0.5f)).normalized;

        /// <summary>Có NPC khác chắn đường không. Hỏi CrowdSpace vài lần mỗi giây, giữa hai lần dùng kết quả cũ.</summary>
        bool CheckCrowd(Vector3 pos, Vector3 heading)
        {
            if (Time.time < _ghostUntil)
                return false;
            if (Time.time >= _nextCrowdCheck)
            {
                _nextCrowdCheck = Time.time + CrowdCheckInterval;
                _crowdBlocker = CrowdSpace.FindBlocker(this, heading);
            }
            return _crowdBlocker != null;
        }

        /// <summary>Bước lệch khỏi vật chắn một đoạn ngắn (không đẩy vào người chơi).</summary>
        IEnumerator Sidestep(Vector3 heading, Vector3 toBlocker)
        {
            var dir = CrowdAvoidLogic.SidestepHeading(heading, toBlocker).normalized;
            var pos = _tf.position;
            Waiting = false;
            Play(Carrying != null ? carryWalkState : "Walk");
            float speed = Carrying != null ? carrySpeed : walkSpeed;
            for (float t = 0f; t < SidestepSeconds; t += Time.deltaTime)
            {
                var player = PlayerRig.Current;
                if (player != null && NpcStepLogic.Blocked(pos, dir, player.Position))
                    break;
                // Xoay người về hướng lách rồi mới bước, không trượt ngang (đo hướng đi 14/10: phụ bếp lách ngang 1,1 m/s, lệch mặt 136°).
                float want = speed * NpcStepLogic.AlignedSpeed(_yaw, dir);
                _speedNow = want < _speedNow ? want : NpcStepLogic.Ramp(_speedNow, want, Accel, Time.deltaTime);
                var next = pos + dir * (_speedNow * Time.deltaTime);
                // Lách không được đưa thân vào vật tĩnh (bàn, rổ, lò) hay ra ngoài mép NavMesh (bot 14/10: phụ bếp lách người rồi đi
                // xuyên bàn sơ chế).
                // Cùng biên với GuardStep (thân + 2 cm): FreeAt co 0,9 lần nên bán kính trần lách sát thành cầu tàu 2 cm (bot 23/10).
                if (!NpcPathing.FreeAt(next, ((Carrying != null ? bodyRadius + 0.08f : bodyRadius) + 0.02f) / 0.9f, _tf))
                    break;
                if (useNavMesh && NavMesh.Raycast(pos, next, out _, NavMesh.AllAreas))
                    break;
                float yawBefore = _yaw;
                TurnToward(dir);
                if (!ReachAllows(next, _yaw))
                {
                    _yaw = yawBefore;
                    _yawVelocity = 0f;
                    break;
                }
                pos = next;
                _tf.SetPositionAndRotation(pos, Quaternion.Euler(0f, _yaw, 0f));
                Heading = dir;
                yield return null;
            }
        }

        /// <summary>
        /// Người chơi đứng sát NPC liên tục quá PlayerYieldAfter giây thì NPC thôi chặn (tắt collider) tới khi người chơi
        /// đi ra xa. Lối hẹp nhờ vậy không bao giờ bị NPC đứng im hay đi ngược chiều chặn kín.
        /// </summary>
        IEnumerator WatchPlayerContact()
        {
            float contact = 0f, touchedAt = float.NegativeInfinity;
            while (true)
            {
                yield return ContactTick;
                var player = PlayerRig.Current;
                if (player == null || _collider == null)
                    continue;
                var d = player.Position - _tf.position;
                float dy = Mathf.Abs(d.y);
                d.y = 0f;
                float r2 = d.sqrMagnitude;
                if (_ghosted)
                {
                    float clear = Mathf.Max(ContactClearRadius, _guardClear);
                    if (r2 > clear * clear || dy > 2f)
                        SetSolid(true);
                    contact = 0f;
                }
                // Ba đi vào đồ đang cầm/vác bị chặn (LimitPlayerStep) cũng là ép vào NPC (đồ nhô xa hơn ContactRadius), nhưng nhường
                // chậm hơn (ItemYieldAfter): nhường là Ba đi xuyên cả đồ.
                else if (Time.time - _pressedAt < 0.3f)
                {
                    contact += 0.2f;
                    touchedAt = Time.time;
                    if (contact >= ItemYieldAfter)
                        SetSolid(false);
                }
                // Hộp chặn đồ dài (xẻng) chặn Ba ngoài ContactRadius: không tính là ép thì NPC đứng chờ Ba tránh mà Ba không qua được
                // (bot 04/11 D1 4:20: Ba cách phụ bếp 0,85 m, kẹt 90 s trước cửa hầm than).
                else if (GuardTouches(player))
                {
                    contact += 0.2f;
                    touchedAt = Time.time;
                    if (contact >= ItemYieldAfter)
                    {
                        // Hộp xẻng với xa hơn ContactClearRadius: nhường tới khi Ba ra khỏi tầm hộp, không bật lại hộp khi còn chồng Ba.
                        var b = playerGuard.bounds;
                        var c = b.center - _tf.position;
                        c.y = 0f;
                        _guardClear = c.magnitude + new Vector2(b.extents.x, b.extents.z).magnitude + GuardMargin;
                        SetSolid(false);
                    }
                }
                else if (r2 < ContactRadius * ContactRadius && dy < 1.6f)
                {
                    contact += 0.2f;
                    touchedAt = Time.time;
                    if (contact >= PlayerYieldAfter && Time.time - _pressedAt > 1f)
                        SetSolid(false);
                }
                else if (Time.time - touchedAt > ContactMemory)
                    contact = 0f;
            }
        }

        float _guardClear;

        void SetSolid(bool solid)
        {
            _ghosted = !solid;
            if (solid) _guardClear = 0f;
            _giveWayStuck = 0f;
            if (_collider != null)
                _collider.enabled = solid;
            // Hộp chặn đồ dài (xẻng phụ bếp, layer Ignore Raycast) cũng tắt khi nhường: đồ vẫn chặn Ba qua LimitPlayerStep (đo mesh đồ với
            // vỏ thân), NPC lùi nhường (GiveWay). Để hộp bật thì Ba kẹt cạnh phụ bếp đang kẹt tĩnh (bot 01/11 lượt 8: D3 xô tro, 90 s).
            if (playerGuard != null)
                playerGuard.enabled = solid;
        }

        /// <summary>Collider thân (capsule); tắt khi nhường người chơi.</summary>
        public Collider BodyCollider => _collider;

        /// <summary>Collider đang tắt vì nhường đường cho người chơi.</summary>
        public bool YieldingToPlayer => _ghosted;

        float _giveWayStuck;
        /// <summary>Hướng bước Ba đang ép vào đồ (ngang, đơn vị), LimitPlayerStep ghi.</summary>
        Vector3 _pressDir;
        static readonly RaycastHit[] GroundHits = new RaycastHit[8];

        /// <summary>Đang nhường mà lùi không được quá GiveWayCorneredAfter: đồ thôi chặn Ba (đường lui cuối).</summary>
        public bool GiveWayCornered => _ghosted && _giveWayStuck >= GiveWayCorneredAfter;
        /// <summary>Tổng quãng đã lùi nhường Ba (m, test).</summary>
        public float GaveWayDistance { get; private set; }

        void Update()
        {
            GiveWay();
        }

        /// <summary>
        /// Nhường Ba mà vẫn cầm/vác đồ: tắt va chạm thân chỉ cho Ba qua người, đồ vẫn chặn (LimitPlayerStep). Ba đang ép vào đồ thì lùi
        /// xa Ba từng chút (trượt dọc vật tĩnh như GuardStep) cho khe mở ra. Bot 01/11 lượt 3: nhường bằng tắt va chạm là Ba đi xuyên
        /// bao vác vai (12 cm), nồi phụ bếp bê (11 cm), lưỡi xẻng (13 cm); còn chặn mà không lùi thì phụ bếp chờ Ba, Ba chờ xẻng (kẹt 90 s).
        /// </summary>
        void GiveWay()
        {
            if (!_ghosted || (Carrying == null && _heldTool == null) || Time.time - _pressedAt > 0.25f || _tf == null)
                return;
            var player = PlayerRig.Current;
            if (player == null)
                return;
            var away = _tf.position - player.Position;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f)
                away = -_tf.forward;
            away.y = 0f;
            // Bước ra khỏi làn Ba đang đi (ngang là chính, nhích ra trước một chút), không lùi thẳng trước mặt Ba: lùi thẳng thì Ba đi tới
            // đâu đẩy NPC tới đó như xe ủi, chỉ qua được khi NPC bị dồn góc (bot lượt 4: kẹt 5 s ở lối xô tro, lối bàn).
            var lane = _pressDir;
            if (lane.sqrMagnitude > 0.5f)
            {
                var side = away - lane * Vector3.Dot(away, lane);
                if (side.sqrMagnitude < 0.05f * 0.05f)
                    side = Vector3.Cross(Vector3.up, lane);
                away = side.normalized * 0.85f + lane * 0.5f;
            }
            var from = _tf.position;
            var step = away.normalized * (GiveWaySpeed * Time.deltaTime);
            // Chỗ lùi tới phải trống thật (GuardStep cho bước tự do khi chỗ đang đứng đã chạm vật: quản bếp đứng sát lò thì bị đẩy vào lò).
            float r = (bodyRadius + 0.02f) / 0.9f;
            foreach (float a in RetreatAngles)
            {
                var next = from + Quaternion.Euler(0f, a, 0f) * step;
                if (followSlope)
                    next.y = GroundY(next, from.y);
                if (!NpcPathing.FreeAt(next, r, _tf))
                    continue;
                GaveWayDistance += (next - from).magnitude;
                _tf.position = next;
                return;
            }
            _giveWayStuck += Time.deltaTime;
        }

        static readonly float[] RetreatAngles = { 0f, 30f, -30f, 60f, -60f, 85f, -85f };

        /// <summary>Cao độ mặt đỡ dưới p (dốc cầu tàu); không thấy hay chênh quá một bậc thì giữ y cũ.</summary>
        float GroundY(Vector3 p, float y)
        {
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, y + 0.5f, p.z), Vector3.down, GroundHits, 1f, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var c = GroundHits[i].collider;
                if (c.transform.IsChildOf(_tf) || (PlayerRig.Current != null && c.transform.IsChildOf(PlayerRig.Current.transform))) continue;
                best = Mathf.Max(best, GroundHits[i].point.y);
            }
            return best > float.NegativeInfinity && Mathf.Abs(best - y) < 0.25f ? best : y;
        }

        /// <summary>Vật đang di chuyển trên sàn (chảo đang kéo) mà NPC phải đứng chờ như chờ người chơi.</summary>
        public static readonly List<Transform> Obstacles = new List<Transform>();

        /// <summary>Đứng kẹt sát vật tĩnh (mọi hướng trượt đều chồng vật) quá chừng này giây thì đi xuyên StaticGhostSeconds.</summary>
        const float StaticSlideMax = 1.5f;
        static readonly float[] SlideAngles = { 30f, -30f, 60f, -60f, 85f, -85f };
        float _staticStuck, _stuckSince = -1f, _stuckLast = float.NegativeInfinity;
        Vector3 _stuckAnchor;

        /// <summary>
        /// Bước from→next có làm thân (bán kính bodyRadius + 2 cm, rộng hơn ngưỡng bot đo) chồng vật tĩnh không; chồng thì thử lệch
        /// hướng bước ±30°/±60°/±85° (trượt dọc mép vật), không được thì đứng lại. Đang chồng sẵn thì cho bước để thoát ra. Bước nhỏ cũng
        /// kiểm: trước đây bỏ qua bước dưới 0,1 mm, lúc tăng tốc lại sau một lần bị chặn (khung hình nhanh) vài bước như vậy lọt qua mép,
        /// rồi luật "đang chồng thì cho đi" để cả quãng sau đi xuyên vách (QA-0150, test bục trống).
        /// </summary>
        /// <summary>Trạng thái đi (chẩn đoán cảnh báo quá hạn, oracle): đích, tốc độ, số lần bị từ chối, đồng hồ kẹt, vật chắn chỗ đứng/đích.</summary>
        public string WalkDiag()
        {
            EnsureInit();
            float r = (bodyRadius + 0.02f) / 0.9f;
            string Name(Collider c) => c == null ? "trống" : c.name;
            return $"đi {Walking}, chờ {Waiting} ({BlockedBy}), đích đoạn {WalkGoal}, tốc {_speedNow:F2}, yaw {_yaw:F0}, đang xuyên vật {PassingStatic}, " +
                   $"từ chối tĩnh {StaticRefusals} (kẹt {_staticStuck:F1} s), reach {ReachRefusals}/{ReachYields}, bê {(Carrying != null ? Carrying.name : "-")}, " +
                   $"chỗ đứng {Name(NpcPathing.BlockerAt(_tf.position, r, _tf))}, đích {Name(NpcPathing.BlockerAt(WalkGoal, r, _tf))}, " +
                   $"nhường Ba (tắt va chạm) {_ghosted}, khe đồ↔Ba {ReachGapNow() * 100f:F1} cm, Ba ép {Time.time - _pressedAt:F1} s trước";
        }

        bool GuardStep(Vector3 from, ref Vector3 next)
        {
            var step = next - from;
            step.y = 0f;
            if (step.sqrMagnitude < 1e-12f)
                return true;
            // FreeAt co bán kính 0,9 lần.
            float r = (bodyRadius + 0.02f) / 0.9f;
            if (NpcPathing.FreeAt(next, r, _tf) || !NpcPathing.FreeAt(from, r, _tf))
                return true;
            float dy = next.y - from.y;
            foreach (float a in SlideAngles)
            {
                var c = from + Quaternion.Euler(0f, a, 0f) * step + Vector3.up * dy;
                if (NpcPathing.FreeAt(c, r, _tf) && !(useNavMesh && NavMesh.Raycast(from, c, out _, NavMesh.AllAreas)))
                {
                    next = c;
                    return true;
                }
            }
            next = from;
            return false;
        }

        static bool BlockedByObstacle(Vector3 pos, Vector3 heading)
        {
            foreach (var o in Obstacles)
                if (o != null && NpcStepLogic.Blocked(pos, heading, o.position, 1.1f))
                    return true;
            return false;
        }

        /// <summary>Đặt ngay tới chỗ mới, quay mặt yaw (DayCast đưa nhân vật vào cảnh, Sim).</summary>
        public void Place(Vector3 position, float yaw)
        {
            EnsureInit();
            _turnFrame = Time.frameCount;
            _yaw = yaw;
            _yawVelocity = 0f;
            _speedNow = 0f;
            _tf.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            Heading = Vector3.zero;
        }

        // ---------- Đồ cầm/vác so với thân thật của Ba (QA-0150) ----------

        /// <summary>Mọi NPC đang bật (người chơi tránh đồ của họ: LimitPlayerStep).</summary>
        static readonly List<NpcAgent> All = new List<NpcAgent>();
        /// <summary>Chỉ đo đồ khi người chơi trong chừng này (m, mặt sàn): đồ dài nhất (xẻng) không quá 1 m khỏi trục thân.</summary>
        const float ReachNear = 2.2f;
        /// <summary>Đứng kẹt vật tĩnh ngay trong tầm nhìn người chơi: thử trượt chừng này giây rồi mới đi xuyên. Dưới hạn đứng yên của các
        /// lượt có hạn (MealPots 6 s) để NPC tự thoát trước khi bị đặt thẳng vào chỗ.</summary>
        const float StaticSeenMax = 4f;

        Transform _heldTool, _reachFor, _toolFor;
        readonly List<MeshFilter> _reachMeshes = new List<MeshFilter>();
        readonly List<NpcStepLogic.Box> _reachBoxes = new List<NpcStepLogic.Box>();
        static readonly List<MeshFilter> MeshScratch = new List<MeshFilter>();
        static readonly List<Vector3> CapsuleShell = new List<Vector3>();
        float _reachHoldUntil = float.NegativeInfinity;
        /// <summary>Lúc bắt đầu đợt bị chặn vì đồ sát Ba (âm = không bị chặn).</summary>
        float _reachHeldSince = -1f;
        /// <summary>Lần cuối bị chặn vì đồ sát Ba (NpcStepLogic.ReachYield).</summary>
        float _reachLastRefused = float.NegativeInfinity;
        /// <summary>Lần cuối Ba bị đồ của NPC này chặn bước (LimitPlayerStep): tính như Ba ép vào NPC (WatchPlayerContact).</summary>
        float _pressedAt = float.NegativeInfinity;
        /// <summary>
        /// Bị chặn vì đồ sát Ba liền chừng này giây (Ba đứng giữa lối hẹp) thì đi tiếp như trước: luật "người chơi không bao giờ chặn ai
        /// quá lâu" (CrowdPlayTests, phụ bếp bê nồi qua Ba ở lối bàn sơ chế) đứng trên luật đồ không chạm thân. Cùng nhịp với chờ
        /// người chơi khác (vòng 0,8 s, lách 1,2 s): 3 s thì phụ bếp bê nồi qua Ba bị chặn vài lần mỗi chuyến, mỗi lần ~4 s, hết hạn
        /// đoạn đi của MealPots (CI 01/11, KITCHEN_HANDS stall).
        /// </summary>
        public const float ReachYieldAfter = 1.5f;
        /// <summary>
        /// Lề phía người chơi (m): lúc dừng bước thân còn đung đưa về trước vài cm (test bục trống: lún 1,4 cm với lề 4 cm); đứng ép vẫn giữ
        /// phím đi thì chân còn vung theo clip đi, đùi ở độ cao 0,6 m vung tới ±12 cm (CI 01/11: lún 1,1 cm vào muôi quản bếp với lề 8 cm).
        /// </summary>
        public const float PlayerReachMargin = 0.13f;

        /// <summary>Chẩn đoán (test, oracle): số bước/xoay đã bị chặn vì đồ sát thân Ba, vì vật tĩnh; số bước người chơi bị cắt.</summary>
        public int ReachRefusals { get; private set; }
        /// <summary>Số bước cho qua vì đã chờ quá ReachYieldAfter (Ba đứng chắn lối).</summary>
        public int ReachYields { get; private set; }
        public int StaticRefusals { get; private set; }
        public static int PlayerStepLimits { get; private set; }
        /// <summary>Hộp đồ đang đo (sau ReachGapNow/ReachAllows gần nhất).</summary>
        public IReadOnlyList<NpcStepLogic.Box> ReachBoxes => _reachBoxes;

        /// <summary>
        /// Dụng cụ đang cầm trên tay (xẻng của phụ bếp) tính như đồ đang bê: NPC không bước, không xoay làm lưỡi/cán lún vào thân Ba,
        /// Ba cũng không đi ngực vào đó. null = bỏ.
        /// </summary>
        public void SetHeldTool(Transform tool) => _heldTool = tool;

        /// <summary>Dựng hộp bao mesh của đồ đang bê (Carrying) và dụng cụ đang cầm ở tư thế hiện tại. false: không cầm gì có mesh.</summary>
        bool CollectReach()
        {
            if (Carrying != _reachFor || _heldTool != _toolFor)
            {
                _reachFor = Carrying;
                _toolFor = _heldTool;
                _reachMeshes.Clear();
                foreach (var item in new[] { Carrying, _heldTool })
                {
                    if (item == null) continue;
                    item.GetComponentsInChildren(true, MeshScratch);
                    _reachMeshes.AddRange(MeshScratch);
                }
            }
            _reachBoxes.Clear();
            for (int i = 0; i < _reachMeshes.Count; i++)
            {
                var m = _reachMeshes[i];
                if (m == null || m.sharedMesh == null || !m.gameObject.activeInHierarchy) continue;
                _reachBoxes.Add(NpcStepLogic.Box.Of(m.transform, m.sharedMesh.bounds));
            }
            return _reachBoxes.Count > 0;
        }

        /// <summary>Vỏ thân người chơi (PlayerBody.Shell); không đọc được mesh thân thì vòng điểm quanh capsule, nhô trước 10 cm.</summary>
        static IReadOnlyList<Vector3> PlayerShell(PlayerRig player, out Vector3 drift)
        {
            var body = player.body;
            if (body != null && body.ShellPoints > 0)
            {
                drift = player.Position - body.ShellOrigin;
                return TorsoShell(body);
            }
            drift = Vector3.zero;
            CapsuleShell.Clear();
            var t = player.transform;
            foreach (float h in new[] { 0.3f, 0.9f, 1.3f })
                for (int a = 0; a < 8; a++)
                {
                    var dir = Quaternion.Euler(0f, a * 45f, 0f) * t.forward;
                    float r = 0.28f + Mathf.Max(0f, Vector3.Dot(dir, t.forward)) * 0.1f;
                    CapsuleShell.Add(t.position + Vector3.up * h + dir * r);
                }
            return CapsuleShell;
        }

        static readonly List<Vector3> Torso = new List<Vector3>();
        static int _torsoFrame = -1;
        static PlayerBody _torsoBody;
        /// <summary>Điểm vỏ cách trục thân quá chừng này (m, mặt sàn) là cánh tay: tay Ba ẩn, không đo.</summary>
        const float TorsoRadius = 0.38f;

        /// <summary>
        /// Vỏ thân bỏ phần cánh tay. PlayerBody gom đỉnh cánh tay về xương ngực theo tư thế bind (dang chữ T), nên vỏ có hai "cánh tay"
        /// chĩa ngang ±0,7 m (test bục trống 31/10): đồ NPC chạm vào đó thì bị chặn oan. Tính một lần mỗi khung.
        /// </summary>
        static IReadOnlyList<Vector3> TorsoShell(PlayerBody body)
        {
            if (_torsoFrame == Time.frameCount && _torsoBody == body)
                return Torso;
            _torsoFrame = Time.frameCount;
            _torsoBody = body;
            var shell = body.Shell;
            float cx = 0f, cz = 0f;
            for (int i = 0; i < shell.Count; i++) { cx += shell[i].x; cz += shell[i].z; }
            cx /= shell.Count; cz /= shell.Count;
            Torso.Clear();
            for (int i = 0; i < shell.Count; i++)
            {
                float dx = shell[i].x - cx, dz = shell[i].z - cz;
                if (dx * dx + dz * dz <= TorsoRadius * TorsoRadius)
                    Torso.Add(shell[i]);
            }
            return Torso;
        }

        /// <summary>
        /// NPC đứng ở (nextPos, nextYaw) thì đồ đang cầm/vác còn cách vỏ thân Ba ít nhất ReachMargin không (hoặc không sát thêm). Chặn thì
        /// ghi _reachHoldUntil: WalkCore tính như người chơi đứng chắn. Bot 23/10: bao vác vai quệt ngực Ba 14 cm, lưỡi xẻng lún 7–9 cm
        /// dù đã có capsule phình và hộp PlayerGuard — vì ngực nhô trước capsule va chạm, các phép thử cũ đo tâm người chơi.
        /// </summary>
        bool ReachAllows(Vector3 nextPos, float nextYaw)
        {
            var player = PlayerRig.Current;
            if (player == null || (Carrying == null && _heldTool == null))
                return true;
            var d = player.Position - _tf.position;
            d.y = 0f;
            if (d.sqrMagnitude > ReachNear * ReachNear || !CollectReach())
                return true;
            var shell = PlayerShell(player, out var drift);
            var shift = NpcStepLogic.PoseShift(_tf.position, _tf.eulerAngles.y, nextPos, nextYaw);
            float after = NpcStepLogic.ReachGap(shell, drift, shift, _reachBoxes, out _);
            if (after >= NpcStepLogic.ReachMargin)
                return true;
            float before = NpcStepLogic.ReachGap(shell, drift, Matrix4x4.identity, _reachBoxes, out _);
            if (NpcStepLogic.ReachAllows(before, after))
                return true;
            // Đợt chặn chỉ hết khi không bị chặn liền ReachForgetAfter (lách ra một khung rồi quay lại vẫn cùng đợt). Chờ lâu thì cho đi
            // sát hơn, nhưng đồ không lún vào thân Ba quá ReachSinkMax: thân NPC đang nhường (tắt va chạm) thì không còn gì đẩy Ba ra, phu
            // bước tới là bao vai đè ngực Ba (bot 01/11 lượt 5: 12 lần cho đi, lún 12,7 cm).
            // Bị chặn liền quá ReachLastResort (Ba đứng yên chắn lối, NPC không lách được): đi như cũ, không kẹt vĩnh viễn (CI 01/11: phụ
            // bếp bê nồi đứng sau lưng Ba sát mép bàn, quá hạn MealPots).
            if (NpcStepLogic.ReachYield(ref _reachHeldSince, ref _reachLastRefused, Time.time, ReachYieldAfter)
                && (after >= -ReachSinkMax || Time.time - _reachHeldSince > ReachLastResort))
            {
                ReachYields++;
                return true;
            }
            _reachHoldUntil = Time.time + 0.3f;
            ReachRefusals++;
            return false;
        }

        /// <summary>Khoảng hở hiện tại (m) giữa đồ NPC đang cầm/vác và vỏ thân Ba; không cầm gì hay ở xa thì vô cực.</summary>
        public float ReachGapNow()
        {
            EnsureInit();
            var player = PlayerRig.Current;
            if (player == null || (Carrying == null && _heldTool == null) || !CollectReach())
                return float.PositiveInfinity;
            var shell = PlayerShell(player, out var drift);
            return NpcStepLogic.ReachGap(shell, drift, Matrix4x4.identity, _reachBoxes, out _);
        }

        /// <summary>
        /// Bước người chơi (m, ngang) sau khi tránh đồ NPC đang cầm/vác: bước làm vỏ thân sát đồ dưới ReachMargin thì bỏ phần đi về phía
        /// đồ (trượt dọc), vẫn sát thì đứng lại. Capsule va chạm không chặn được vì ngực nhô trước capsule (QA-0150).
        /// </summary>
        public static Vector3 LimitPlayerStep(PlayerRig player, Vector3 step)
        {
            if (player == null || step.sqrMagnitude < 1e-10f)
                return step;
            IReadOnlyList<Vector3> shell = null;
            var drift = Vector3.zero;
            for (int i = 0; i < All.Count; i++)
            {
                var a = All[i];
                // NPC đang nhường (WatchPlayerContact tắt collider) cho Ba qua người, đồ vẫn chặn: NPC lùi (GiveWay) cho khe mở ra. Lùi
                // không được quá GiveWayCorneredAfter thì Ba đi xuyên cả đồ (đường lui cuối, không kẹt người chơi).
                if (a == null || !a.isActiveAndEnabled || a.GiveWayCornered || (a.Carrying == null && a._heldTool == null))
                    continue;
                var d = a.transform.position - player.Position;
                d.y = 0f;
                if (d.sqrMagnitude > ReachNear * ReachNear || !a.CollectReach())
                    continue;
                shell ??= PlayerShell(player, out drift);
                float after = NpcStepLogic.ReachGap(shell, drift + step, Matrix4x4.identity, a._reachBoxes, out _);
                if (after >= PlayerReachMargin)
                    continue;
                float before = NpcStepLogic.ReachGap(shell, drift, Matrix4x4.identity, a._reachBoxes, out var away);
                if (after >= before - 1e-5f)
                    continue;
                PlayerStepLimits++;
                a._pressedAt = Time.time;
                a._pressDir = new Vector3(step.x, 0f, step.z).normalized;
                float toward = Vector3.Dot(step, away);
                if (toward < 0f)
                    step -= away * toward;
                after = NpcStepLogic.ReachGap(shell, drift + step, Matrix4x4.identity, a._reachBoxes, out _);
                if (after < PlayerReachMargin && after < before - 1e-5f)
                    return Vector3.zero;
            }
            return step;
        }

        /// <summary>Xoay một bước về dir; xoay làm đồ đang cầm quét vào thân Ba thì đứng yên khung này.</summary>
        void TurnGuarded(Vector3 dir)
        {
            float before = _yaw;
            TurnToward(dir);
            if (!ReachAllows(_tf.position, _yaw))
            {
                _yaw = before;
                _yawVelocity = 0f;
            }
            _tf.rotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        /// <summary>Bước xoay kế tiếp về dir có giữ đồ đang cầm ngoài thân Ba không (không xoay thật).</summary>
        bool TurnWouldClear(Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f || (Carrying == null && _heldTool == null))
                return true;
            float velocity = Time.time - _lastTurnAt > 0.1f ? 0f : _yawVelocity;
            float next = NpcStepLogic.TurnStep(_yaw, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, ref velocity, turnSpeed, TurnAccel, Time.deltaTime);
            return ReachAllows(_tf.position, next);
        }

        /// <summary>Điểm p (chân NPC) có nằm trong khung nhìn người chơi (trong 25 m) không.</summary>
        static bool SeenByPlayer(Vector3 p)
        {
            var rig = PlayerRig.Current;
            if (rig == null || rig.cam == null)
                return false;
            var vp = rig.cam.WorldToViewportPoint(p + Vector3.up * 1.1f);
            return vp.z > 0f && vp.z < 25f && vp.x > -0.15f && vp.x < 1.15f && vp.y > -0.3f && vp.y < 1.3f;
        }

        /// <summary>Người chơi cách hộp đồ dài đang cầm (playerGuard) dưới chừng này (m, tính từ trục thân).</summary>
        const float GuardMargin = 0.35f;
        /// <summary>Xoay người với đồ dài: chờ người chơi tránh khỏi đồ tối đa chừng này rồi mới xoay tiếp.</summary>
        const float GuardWaitMax = 20f;

        /// <summary>Trục thân người chơi (gót, hông, ngực) cách hộp playerGuard dưới GuardMargin.</summary>
        bool GuardTouches(PlayerRig player)
        {
            if (playerGuard == null || !playerGuard.enabled || player == null)
                return false;
            foreach (float h in GuardHeights)
            {
                var q = player.Position + Vector3.up * h;
                if ((playerGuard.ClosestPoint(q) - q).sqrMagnitude < GuardMargin * GuardMargin)
                    return true;
            }
            return false;
        }

        static readonly float[] GuardHeights = { 0.3f, 0.9f, 1.3f };

        /// <summary>Quay mặt về hướng (độ, quanh trục Y).</summary>
        public IEnumerator Face(float yaw)
        {
            var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            for (float t = 0f, held = 0f; t < 3f && (Mathf.Abs(Mathf.DeltaAngle(_yaw, yaw)) > 0.3f || Mathf.Abs(_yawVelocity) > 20f); t += Time.deltaTime)
            {
                if (held < GuardWaitMax && (GuardTouches(PlayerRig.Current) || !TurnWouldClear(dir)))
                {
                    held += Time.deltaTime;
                    t -= Time.deltaTime;
                    _yawVelocity = 0f;
                    Waiting = true;
                    yield return null;
                    continue;
                }
                if (held > 0f) Waiting = false;
                TurnToward(dir);
                transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
                yield return null;
            }
            _yaw = yaw;
            _yawVelocity = 0f;
            _speedNow = 0f;
            _turnFrame = Time.frameCount;
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        /// <summary>
        /// Xoay dần về hướng dir: tốc độ quay tăng, giảm theo gia tốc có trần (nhanh nhất turnSpeed), không bắt đầu quay ngay ở tốc độ
        /// tối đa (đo giật 14/10: đồ đang cầm cách trục người 0,3–0,5 m nhảy 2–6 cm ở khung đầu khi quay ngoắt).
        /// </summary>
        void TurnToward(Vector3 dir, float maxSpeed = -1f)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f)
                return;
            _turnFrame = Time.frameCount;
            // Lâu không quay (đứng chờ, làm việc): tốc độ quay cũ không còn. Giữ lại thì lúc đi tiếp thân quay ngay 170°/s, xẻng đang
            // bê vụt 2,6 cm/khung (đo giật 14/10).
            if (Time.time - _lastTurnAt > 0.1f)
                _yawVelocity = 0f;
            _lastTurnAt = Time.time;
            float want = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            _yaw = NpcStepLogic.TurnStep(_yaw, want, ref _yawVelocity, maxSpeed > 0f ? Mathf.Min(maxSpeed, turnSpeed) : turnSpeed, TurnAccel,
                Time.deltaTime);
        }

        int _turnFrame = -1;

        /// <summary>Khung này thân đã được quay (đi, Face, đặt chỗ): NpcGaze không quay thêm lần nữa.</summary>
        public bool TurnedThisFrame => _turnFrame == Time.frameCount;

        /// <summary>
        /// Quay thân một bước về hướng dir với tốc độ quay tối đa maxSpeed (NpcGaze: nói chuyện thì quay theo người chơi). Khung này
        /// thân đã quay bởi việc khác thì thôi.
        /// </summary>
        public void TrackTurn(Vector3 dir, float maxSpeed)
        {
            EnsureInit();
            if (TurnedThisFrame)
                return;
            float yawBefore = _yaw;
            TurnToward(dir, maxSpeed);
            if (!ReachAllows(_tf.position, _yaw))
            {
                _yaw = yawBefore;
                _yawVelocity = 0f;
            }
            _tf.rotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        /// <summary>Làm một động tác tại chỗ trong khoảng thời gian.</summary>
        public IEnumerator Do(string state, float seconds)
        {
            Play(state);
            yield return new WaitForSeconds(seconds);
        }

        public void PickUp(Transform item)
        {
            EnsureInit();
            Carrying = item;
            // Đồ theo tay (CarryHold): điểm bê bắt đầu đúng chỗ đồ đang nằm rồi trộn về tay, không nhảy từ đất lên bụng, lên vai.
            if (Application.isPlaying && followCarryBones && carryPoint != null)
            {
                if (!_bonesLooked) LookBones();
                if (CarryHold != Hold.None)
                {
                    carryPoint.SetPositionAndRotation(item.position, item.rotation);
                    StartBlend(item.position, item.rotation);
                    _hold = CarryHold;
                }
            }
            item.SetParent(carryPoint != null ? carryPoint : transform, true);
            item.localPosition = Vector3.zero;
            item.localRotation = Quaternion.identity;
            WidenForShoulder(CarryHold == Hold.Shoulder);
        }

        CapsuleCollider _capsule;
        float _capsuleRadius;
        Vector3 _capsuleCenter;
        bool _widened;

        /// <summary>
        /// Bao vác vai nhô ngoài capsule thân 10–15 cm (bao đè vai phải, nghiêng ra ngoài): người chơi đi sát chỉ bị capsule chặn nên ngực
        /// lún bao (bot 23/10: 14 cm). Vác bao thì capsule phình sang phía vai bao, đặt xuống trả lại.
        /// </summary>
        void WidenForShoulder(bool on)
        {
            if (_capsule == null)
                _capsule = _collider as CapsuleCollider;
            if (_capsule == null || on == _widened)
                return;
            if (on)
            {
                _capsuleRadius = _capsule.radius;
                _capsuleCenter = _capsule.center;
                _capsule.radius = _capsuleRadius + 0.12f;
                _capsule.center = _capsuleCenter + new Vector3(0.05f, 0f, -0.04f);
            }
            else
            {
                _capsule.radius = _capsuleRadius;
                _capsule.center = _capsuleCenter;
            }
            _widened = on;
        }

        public Transform PutDown(Vector3 position, Quaternion rotation, Transform parent)
        {
            var item = Carrying;
            Carrying = null;
            WidenForShoulder(false);
            if (item != null)
            {
                item.SetParent(parent, true);
                item.SetPositionAndRotation(position, rotation);
            }
            return item;
        }

        // ---------- Đồ theo tay, vai ----------

        /// <summary>Đặt đồ đang bê theo tay, vai mỗi khung (CarryHold). Tắt thì đồ đứng yên ở điểm bê như trước.</summary>
        [System.NonSerialized] public bool followCarryBones = true;
        /// <summary>Chỗ đặt từng kiểu bê (mặc định là hằng số bên dưới; test dò chỗ đặt đổi trên từng NPC).</summary>
        [System.NonSerialized] public Vector3 bagOffset = BagBelowPalm, sackOffset = SackOverShoulder, sackTilt = SackTilt, loadOffset = LoadBetweenPalms;

        // Chỗ đặt dò bằng CarryFit (bộ dò xuyên thân, một vòng bước ở 60 Hz, ba vai phu và một khách, 15/10).
        /// <summary>Va li: tâm hộp so với lòng bàn tay phải (m, hệ quay theo thân: x phải, y lên, z trước). Không lún vào chân, tay cách quai 1 cm.</summary>
        public static readonly Vector3 BagBelowPalm = new Vector3(0.06f, -0.17f, 0f);
        /// <summary>
        /// Bao vác: tâm bao so với khớp vai phải (hệ quay theo thân) và góc nghiêng bao (độ). Bao ra ngoài vai, lùi về sau: lún vào đầu
        /// 0–0,4 cm (gắn ở gốc NPC như trước: 15–18 cm), tựa vai 3,5–5 cm như bao mềm đè lên vai. Tay phải theo clip, cách bao ~14 cm;
        /// kéo tay áp vào bao thì cẳng tay lún 8 cm vào bao, tệ hơn.
        /// </summary>
        public static readonly Vector3 SackOverShoulder = new Vector3(0.14f, 0.10f, -0.12f);
        public static readonly Vector3 SackTilt = new Vector3(78f, 0f, 0f);
        /// <summary>Bê trước bụng: tâm đồ đúng giữa hai lòng bàn tay (tay áp vào đồ, đồ không lún vào thân).</summary>
        public static readonly Vector3 LoadBetweenPalms = Vector3.zero;
        /// <summary>Đổi chỗ đặt (nắm bao dưới đất, nhặt xong vác lên vai) thì trộn trong chừng này giây, đồ không nhảy.</summary>
        public const float CarryBlendSeconds = 0.3f;
        /// <summary>Lòng bàn tay theo clip cách mặt đồ trong chừng này (m) thì kéo bàn tay áp vào mặt đồ (IK); xa hơn là tay rảnh.</summary>
        public const float GripReach = 0.2f;

        public enum Hold { None, Palms, Hand, Shoulder }

        Transform _leftHand, _leftFingers, _rightHand, _rightFingers, _rightUpperArm, _gripL, _gripR, _shapeOf;
        NpcArmIK _armIK;
        bool _bonesLooked, _handWork;
        Hold _hold = Hold.None;
        float _blend = 1f;
        Vector3 _blendPos, _shapeCenter, _shapeExtents;
        Quaternion _blendRot;
        Transform _shape;
        Renderer _shapeRenderer;

        /// <summary>
        /// Chỗ đặt đồ đang bê: đang cúi nhặt/đặt (PickUp) thì giữa hai bàn tay; xách va li thì dưới bàn tay phải; vác vai thì trên vai
        /// phải; bê trước bụng thì giữa hai bàn tay. NPC có tay IK riêng (NpcHandWork: tay bám chỗ cầm trên đồ) và đồ có kịch bản riêng
        /// (khay bồi bàn) thì không.
        /// </summary>
        public Hold CarryHold
        {
            get
            {
                if (_handWork || Carrying == null) return Hold.None;
                if (_state == "PickUp") return Hold.Palms;
                if (carryWalkState == "BagWalk") return Hold.Hand;
                if (carryWalkState == "ShoulderCarry") return Hold.Shoulder;
                return carryWalkState == "CarryWalk" ? Hold.Palms : Hold.None;
            }
        }

        void LookBones()
        {
            _bonesLooked = true;
            _handWork = GetComponent<NpcHandWork>() != null;
            if (_animator == null || !_animator.isHuman) return;
            _leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _leftFingers = _animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            _rightFingers = _animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            _rightUpperArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        }

        bool CarryTarget(Hold hold, out Vector3 position, out Quaternion rotation)
        {
            var yaw = Quaternion.Euler(0f, _tf.eulerAngles.y, 0f);
            position = default;
            rotation = yaw;
            switch (hold)
            {
                case Hold.Hand when _rightHand != null:
                    BagPose(_rightHand, _rightFingers, yaw, bagOffset, out position, out rotation);
                    return true;
                case Hold.Shoulder when _rightUpperArm != null:
                    SackPose(_rightUpperArm, yaw, sackOffset, sackTilt, out position, out rotation);
                    return true;
                case Hold.Palms when _rightHand != null && _leftHand != null:
                    LoadPose(_leftHand, _leftFingers, _rightHand, _rightFingers, yaw, loadOffset, out position, out rotation);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Đồ bê theo tay, vai (đo xuyên thân 15/10): đồ gắn cố định ở gốc NPC thì tay vung theo clip mà đồ đứng yên — va li cách tay
        /// 20–40 cm, bao vác nằm ngang tầm đầu nuốt cả mặt (lún 15–18 cm vào đầu, cổ), bao bê bụng cách hai tay 12 cm, cúi đặt bao thì
        /// tay xuống đất mà bao còn ở bụng. Chạy sau Animator, trước NpcArmIK: đặt đồ theo xương rồi kéo bàn tay đang đỡ đồ áp vào mặt
        /// đồ. NPC CrowdLod đóng băng (không chạy Animator) thì giữ chỗ cũ.
        /// </summary>
        void LateUpdate()
        {
            if (!followCarryBones || carryPoint == null || !_animated) return;
            if (Carrying == null)
            {
                if (_armIK != null) Grip(false, false);
                _hold = Hold.None;
                PropInHands();
                return;
            }
            if (!_bonesLooked) LookBones();
            RefreshShape();
            var hold = CarryHold;
            if (hold != _hold)
            {
                // Đổi chỗ đặt giữa chừng (cúi đặt bao, nhặt xong vác lên vai): trộn từ chỗ đang đứng.
                if (_hold != Hold.None) StartBlend(carryPoint.position, carryPoint.rotation);
                _hold = hold;
            }
            if (!CarryTarget(hold, out var p, out var r)) return;
            if (_blend < 1f)
            {
                _blend = Mathf.Min(1f, _blend + Time.deltaTime / CarryBlendSeconds);
                float k = Mathf.SmoothStep(0f, 1f, _blend);
                p = Vector3.Lerp(_blendPos, p, k);
                r = Quaternion.Slerp(_blendRot, r, k);
            }
            // Cúi nhặt: tay xuống sát đất (12 cm) mà bao cao 60 cm; đáy đồ không xuống dưới mặt chân đứng.
            if (hold == Hold.Palms && _shapeRenderer != null)
            {
                var b = _shapeRenderer.bounds;
                float below = _tf.position.y - (p.y + (b.center.y - carryPoint.position.y) - b.extents.y);
                if (below > 0f) p.y += below;
            }
            carryPoint.SetPositionAndRotation(p, r);
            _propMoved = true;
            Grip(hold == Hold.Palms, hold == Hold.Palms);
        }

        Transform _prop;
        float _propHalfHeight, _propTwoHands;
        Vector3 _carryHomePos;
        Quaternion _carryHomeRot = Quaternion.identity;
        bool _propMoved;
        /// <summary>Cách cầm đồ kịch bản đang dùng: 0 chưa chọn, 1 hai tay, 2 tay trái, 3 tay phải. Giữ nguyên tới khi đổi dáng/đồ.</summary>
        int _propGrip;

        /// <summary>Dáng cầm đồ kịch bản ra trước người: đưa đồ, nói (tay cầm đồ), xem giấy tờ.</summary>
        static bool PropPose(string state) => state == "HandOver" || state == "Talk" || state == "Documents";

        /// <summary>
        /// Đồ kịch bản bật sẵn dưới điểm bê mà không qua PickUp (cốc kem anh Lê, áo ông Mai cho mượn, sách lính, phong bì lương, báo):
        /// điểm bê đứng yên theo gốc NPC trong khi clip đưa tay ra (bot 04/11: tay cách đồ 34–65 cm). Hai bàn tay gần nhau thì đồ ở
        /// giữa hai lòng bàn tay; đang đưa đồ hay đang nói (tay cầm đồ ra trước) thì đồ nằm trên lòng bàn tay đưa ra trước hơn; dáng
        /// khác (tay buông) giữ điểm bê như cũ, không kéo đồ xuống đùi (bot 04/11: khay bồi bàn lún đùi khi đứng IdleLook).
        /// </summary>
        void PropInHands()
        {
            Transform prop = null;
            for (int i = 0; i < carryPoint.childCount; i++)
                if (carryPoint.GetChild(i).gameObject.activeSelf)
                {
                    prop = carryPoint.GetChild(i);
                    break;
                }
            if (prop == null) return;
            if (!_bonesLooked) LookBones();
            if (_leftHand == null || _rightHand == null) return;
            // Dáng khác (đứng chờ, đi, xếp hàng) giữ điểm bê ở chỗ dựng cảnh: điểm bê ở lại chỗ đặt lần trước thì va li khách xếp hàng
            // lơ lửng cách tay 62 cm (bot 04/11).
            if (!PropPose(_state))
            {
                RestoreCarryHome();
                _propGrip = 0;
                return;
            }
            if (prop != _prop)
            {
                _prop = prop;
                _propGrip = 0;
                var renderer = prop.GetComponentInChildren<Renderer>();
                _propHalfHeight = renderer != null ? renderer.bounds.extents.y : 0f;
                // Hai tay ôm được đồ khi khe hai lòng bàn tay không rộng hơn bề ngang đồ quá PropGripSlack (cốc kem 8 cm thì tay phải sát
                // nhau; bot 04/11: cốc nằm giữa hai tay cách 21 cm, mỗi tay hở 11 cm).
                _propTwoHands = renderer != null ? 2f * Mathf.Max(renderer.bounds.extents.x, renderer.bounds.extents.z) + PropGripSlack : 0f;
            }
            // Va li, bao (đồ cao) xách dưới tay, không đặt lên lòng bàn tay: để chỗ dựng cảnh như cũ.
            if (_propHalfHeight > PropMaxHalfHeight)
            {
                RestoreCarryHome();
                return;
            }
            var yaw = Quaternion.Euler(0f, _tf.eulerAngles.y, 0f);
            var left = Palm(_leftHand, _leftFingers);
            var right = Palm(_rightHand, _rightFingers);
            // Chọn cách cầm một lần rồi giữ (bot 04/11: hai tay ngang nhau thì cốc kem nhảy qua lại giữa hai tay mỗi khung). Đổi khi tay
            // kia đưa ra trước hẳn (PropSwitchLead) hay hai tay tách/chụm qua ngưỡng có trễ.
            float gap = (left - right).magnitude;
            float lead = Vector3.Dot(right - left, yaw * Vector3.forward);
            if (_propGrip == 1 ? gap > _propTwoHands + 0.08f : gap < _propTwoHands)
                _propGrip = gap < _propTwoHands ? 1 : 0;
            if (_propGrip != 1 && _state != "Documents")
            {
                if (_propGrip == 0) _propGrip = lead >= 0f ? 3 : 2;
                else if (_propGrip == 2 && lead > PropSwitchLead) _propGrip = 3;
                else if (_propGrip == 3 && lead < -PropSwitchLead) _propGrip = 2;
            }
            Vector3 p;
            if (_propGrip == 1)
                p = (left + right) * 0.5f;
            else if (_propGrip >= 2 && _state != "Documents")
                p = (_propGrip == 3 ? right : left) + Vector3.up * _propHalfHeight;
            else
            {
                RestoreCarryHome();
                return;
            }
            carryPoint.SetPositionAndRotation(p, yaw);
            _propMoved = true;
        }

        void RestoreCarryHome()
        {
            if (!_propMoved) return;
            _propMoved = false;
            carryPoint.SetLocalPositionAndRotation(_carryHomePos, _carryHomeRot);
        }

        /// <summary>Đồ kịch bản nằm giữa hai tay khi khe hai lòng bàn tay không quá bề ngang đồ cộng chừng này (m).</summary>
        public const float PropGripSlack = 0.1f;
        /// <summary>Tay kia đưa ra trước hơn tay đang cầm chừng này (m) mới chuyển đồ sang tay kia.</summary>
        public const float PropSwitchLead = 0.25f;
        /// <summary>Đồ kịch bản cao hơn gấp đôi chừng này (m: cốc kem 10 cm, áo gấp 8 cm qua; va li 30–45 cm không) thì không đặt lên tay.</summary>
        public const float PropMaxHalfHeight = 0.1f;

        void StartBlend(Vector3 position, Quaternion rotation)
        {
            _blendPos = position;
            _blendRot = rotation;
            _blend = 0f;
        }

        /// <summary>
        /// Bàn tay đang đỡ đồ áp vào mặt đồ: lòng bàn tay theo clip, dời tới điểm gần nhất trên mặt đồ (đồ coi như elip theo bao hình
        /// mesh) nếu trong GripReach; hướng bàn tay giữ như clip. NpcArmIK thêm khi cần lần đầu, mức bám đổi dần 0,2 s.
        /// </summary>
        void Grip(bool left, bool right)
        {
            if ((left || right) && _armIK == null && _leftHand != null)
            {
                _armIK = gameObject.AddComponent<NpcArmIK>();
                _armIK.leftWeight = _armIK.rightWeight = 0f;
                _gripL = new GameObject("CarryGripL").transform;
                _gripR = new GameObject("CarryGripR").transform;
                _gripL.SetParent(_tf, false);
                _gripR.SetParent(_tf, false);
            }
            if (_armIK == null) return;
            GripHand(true, left);
            GripHand(false, right);
        }

        void GripHand(bool left, bool want)
        {
            var hand = left ? _leftHand : _rightHand;
            var grip = left ? _gripL : _gripR;
            bool on = false;
            if (want && Carrying != null && Surface(Palm(hand, left ? _leftFingers : _rightFingers), out var offset)
                && _armIK.HandFrame(left, out var wrist, out var frame))
            {
                grip.SetPositionAndRotation(wrist + offset, frame);
                on = true;
            }
            float w = Mathf.MoveTowards(left ? _armIK.leftWeight : _armIK.rightWeight, on ? 1f : 0f, Time.deltaTime / 0.2f);
            if (left) { _armIK.leftTarget = grip; _armIK.leftWeight = w; }
            else { _armIK.rightTarget = grip; _armIK.rightWeight = w; }
        }

        /// <summary>Dời từ lòng bàn tay tới mặt đồ đang bê (elip theo bao hình mesh của đồ); false nếu xa quá GripReach.</summary>
        /// <summary>Mesh của đồ đang bê (đổi đồ thì tìm lại một lần, không tìm mỗi khung).</summary>
        void RefreshShape()
        {
            if (_shapeOf == Carrying) return;
            _shapeOf = Carrying;
            var filter = Carrying != null ? Carrying.GetComponentInChildren<MeshFilter>() : null;
            _shape = filter != null && filter.sharedMesh != null ? filter.transform : null;
            _shapeRenderer = _shape != null ? filter.GetComponent<Renderer>() : null;
            if (_shape != null)
            {
                _shapeCenter = filter.sharedMesh.bounds.center;
                _shapeExtents = filter.sharedMesh.bounds.extents;
            }
        }

        bool Surface(Vector3 palm, out Vector3 offset)
        {
            offset = Vector3.zero;
            RefreshShape();
            if (_shape == null || _shapeExtents.x < 1e-4f || _shapeExtents.y < 1e-4f || _shapeExtents.z < 1e-4f) return false;
            var local = _shape.InverseTransformPoint(palm) - _shapeCenter;
            var q = new Vector3(local.x / _shapeExtents.x, local.y / _shapeExtents.y, local.z / _shapeExtents.z);
            if (q.sqrMagnitude < 1e-6f) return false;
            var onSurface = Vector3.Scale(q.normalized, _shapeExtents) + _shapeCenter;
            offset = _shape.TransformPoint(onSurface) - palm;
            return offset.magnitude <= GripReach;
        }

        /// <summary>Lòng bàn tay (giữa cổ tay và gốc ngón giữa, nghiêng về phía ngón).</summary>
        public static Vector3 Palm(Transform hand, Transform fingers) => fingers != null ? Vector3.Lerp(hand.position, fingers.position, 0.6f) : hand.position;

        /// <summary>Va li treo thẳng đứng (trọng lực) dưới lòng bàn tay, mặt hộp theo hướng thân.</summary>
        public static void BagPose(Transform hand, Transform fingers, Quaternion yaw, Vector3 offset, out Vector3 position, out Quaternion rotation)
        {
            position = Palm(hand, fingers) + yaw * offset;
            rotation = yaw;
        }

        /// <summary>Bao trên đỉnh vai: tâm theo khớp vai phải, hướng theo thân.</summary>
        public static void SackPose(Transform shoulder, Quaternion yaw, Vector3 offset, Vector3 tilt, out Vector3 position, out Quaternion rotation)
        {
            position = shoulder.position + yaw * offset;
            rotation = yaw * Quaternion.Euler(tilt);
        }

        /// <summary>Đồ bê hai tay: tâm ở giữa hai lòng bàn tay, hướng theo thân.</summary>
        public static void LoadPose(Transform leftHand, Transform leftFingers, Transform rightHand, Transform rightFingers, Quaternion yaw,
            Vector3 offset, out Vector3 position, out Quaternion rotation)
        {
            position = (Palm(leftHand, leftFingers) + Palm(rightHand, rightFingers)) * 0.5f + yaw * offset;
            rotation = yaw;
        }
    }
}
