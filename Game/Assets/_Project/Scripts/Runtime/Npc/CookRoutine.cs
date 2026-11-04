using System.Collections;
using System.Collections.Generic;
using HaiBanTay.Shift;
using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>
    /// Quản bếp (nhân vật hư cấu): đi lần lượt tới từng nồi đang nấu, nhấc nắp, khuấy bằng muôi của mình trong lòng nồi rồi đậy
    /// lại (NpcHandWork, tay bám núm nắp và cán muôi). Thùng khoai đã đầy một bao thì gác muôi lên mép bếp, cúi bê thùng bằng hai
    /// tay, đổ khoai vào nồi đang nấu, mang thùng trả chỗ cũ rồi cầm muôi lại. Không có nồi nào đang nấu thì đứng trông bếp.
    /// Đi lại theo NavMesh của cảnh (useNavMesh), không theo tuyến vẽ tay.
    /// </summary>
    public sealed class CookRoutine : NpcRoutine
    {
        public SackStation sacks;
        public MealPots meals;
        public Transform tub;
        [Tooltip("Muôi của quản bếp: gốc ở lòng muôi, +Y dọc cán; ladleGrip là chỗ cổ tay phải khi nắm cán.")]
        public Transform ladle;
        public Transform ladleGrip;
        [Tooltip("Chỗ đứng trông bếp khi chưa có nồi nào đang nấu (đầu đông lò bắc).")]
        public Vector3 watchSpot = new Vector3(-0.72f, 0f, 1.65f);
        public float watchYaw;
        [Tooltip("Chỗ đứng bê thùng khoai và hướng mặt.")]
        public Vector3 tubStand = new Vector3(1.93f, 0f, 0f);
        public float tubYaw = -90f;
        public Vector2 stirSeconds = new Vector2(6f, 9f);
        public float watchSeconds = 5f;
        [Tooltip("Khoảng cách từ mép trước lò tới tâm thân khi đứng khuấy (m).")]
        public float standOff = 0.25f;

        NpcHandWork _hands;
        Vector3 _tubHome;
        Quaternion _tubHomeRot;
        Transform _tubParent;
        int _turn;
        bool _tubInit, _ladleResting;
        Vector3 _restStand;
        float _restYaw;

        public int TubsCarried { get; private set; }
        public int PotsStirred { get; private set; }
        /// <summary>Nồi đang khuấy (test đo muôi trong lòng nồi).</summary>
        public Transform StirringPot { get; private set; }

        protected override IEnumerator Run()
        {
            _hands = GetComponent<NpcHandWork>();
            if (_hands != null) KeepLadle();
            if (tub != null && !_tubInit)
            {
                _tubHome = tub.position;
                _tubHomeRot = tub.rotation;
                _tubParent = tub.parent;
                _tubInit = true;
            }
            // Bị dừng lúc cả nhóm đang tắt (Stopped không đổi cha được): bật lại thì thùng về chỗ trước.
            SettleTub();
            while (true)
            {
                var pot = NextPot();
                if (pot == null || _hands == null || ladle == null)
                {
                    yield return agent.Go(watchSpot);
                    yield return agent.Face(watchYaw);
                    agent.Play("IdleLook");
                    yield return new WaitForSeconds(watchSeconds);
                    continue;
                }
                if (_ladleResting)
                {
                    // Muôi còn gác trên mép lò (lần trước bị gọi đi giữa chừng): tới lấy trước.
                    yield return agent.GoWhenFree(_restStand);
                    yield return agent.Face(_restYaw);
                    yield return Lean(16f);
                    yield return _hands.TakeTool(ladle, ladleGrip, false, NpcHandWork.FistCurl);
                    _ladleResting = false;
                    yield return _hands.HoldTool(ladle, ladleGrip, false);
                    yield return Lean(0f);
                }
                var stand = StandFor(pot, out float yaw);
                yield return agent.GoWhenFree(stand);
                yield return agent.Face(yaw);
                agent.Play("Idle");
                yield return Lean(16f);
                StirringPot = pot;
                yield return _hands.Stir(pot, ladle, ladleGrip, Random.Range(stirSeconds.x, stirSeconds.y));
                StirringPot = null;
                PotsStirred++;
                if (TubReady())
                {
                    yield return FetchTub(pot, stand, yaw);
                    continue;
                }
                yield return _hands.HoldTool(ladle, ladleGrip, false);
                yield return Lean(0f);
            }
        }

        bool TubReady() => sacks != null && tub != null && sacks.OnTable == null && sacks.InTub >= sacks.potatoesPerSack;

        /// <summary>Nồi đang nấu kế tiếp (lần lượt), có nắp, nằm trên ô bếp; null nếu không có.</summary>
        Transform NextPot()
        {
            if (meals == null) return null;
            var pots = meals.CookingPots;
            for (int k = 0; k < pots.Count; k++)
            {
                var p = pots[(_turn + k) % pots.Count];
                if (p != null && p.activeInHierarchy && meals.HobOf(p) >= 0)
                {
                    _turn = (_turn + k + 1) % Mathf.Max(1, pots.Count);
                    return p.transform;
                }
            }
            return null;
        }

        /// <summary>Chỗ đứng trước nồi: phía lối đi của lò (lò bắc đứng phía nam, lò nam đứng phía bắc), sát mép trước lò.</summary>
        public Vector3 StandFor(Transform pot, out float yaw)
        {
            NpcHandWork.MeasurePot(pot, out var center, out _, out _);
            float side = center.z > 0f ? -1f : 1f;
            yaw = side < 0f ? 0f : 180f;
            // Lò sâu 1 m, ô bếp ở giữa chiều sâu: mép trước cách tâm ô 0,5 m.
            return new Vector3(center.x, agent.Position.y, center.z + side * (0.5f + standOff));
        }

        IEnumerator Lean(float degrees)
        {
            float from = _hands.lean;
            for (float t = 0f; t < 0.4f; t += Time.deltaTime)
            {
                _hands.lean = Mathf.Lerp(from, degrees, Mathf.SmoothStep(0f, 1f, t / 0.4f));
                yield return null;
            }
            _hands.lean = degrees;
        }

        void Start()
        {
            // Hai người bếp nhường nhau, không đi xuyên người nhau ở lối trước lò (clip 14/10: quản bếp đi xuyên vai phụ bếp 0,7 s).
            if (agent != null) agent.avoidOthers = true;
            // Câu của quản bếp: cảnh kết (EndingDirector) gán người nói riêng thì giữ.
            TalkGaze.Bind("quan_bep", agent, overwrite: false);
            // Đầu cảnh: muôi trong tay phải quản bếp.
            var hands = GetComponent<NpcHandWork>();
            if (hands != null && ladle != null && ladleGrip != null) hands.SnapToolToHand(ladle, ladleGrip, false, NpcHandWork.FistCurl);
        }

        protected override void Stopped()
        {
            StirringPot = null;
            // Thùng đang bê, hay đang nhấc lên / hạ xuống giữa chừng, thì về chỗ: dừng giữa NpcHandWork.Move là thùng lơ lửng trước bụng
            // rồi quản bếp đi xuyên nó (bot 01/11, QA-0154: MealPots tắt routine lúc thùng đang hạ, quản bếp ra chỗ trông bếp chồng Stave).
            // Cả nhóm NPC đang tắt (hết ngày) thì không đổi cha lúc này (Unity cấm); Run làm việc đó khi bật lại.
            if (gameObject.activeInHierarchy) SettleTub();
            if (_hands == null) return;
            _hands.Abort();
            // Muôi đang trong nồi hay đang đưa giữa chừng thì về tay; đang gác trên mép lò thì để đó.
            if (gameObject.activeInHierarchy) KeepLadle();
        }

        /// <summary>Thùng khoai đang rời chỗ: đang bê, hay đang nhấc lên / hạ xuống (chưa tính là bê, chưa nằm lại chỗ cũ).</summary>
        public bool TubOut => tub != null && _tubInit && (agent.Carrying == tub || (tub.position - _tubHome).sqrMagnitude > 1e-4f);

        /// <summary>Đặt thùng về đúng chỗ, đúng cha (routine bị dừng giữa chừng).</summary>
        void SettleTub()
        {
            if (!TubOut) return;
            if (agent.Carrying == tub)
                agent.PutDown(_tubHome, _tubHomeRot, _tubParent);
            else
            {
                tub.SetParent(_tubParent, true);
                tub.SetPositionAndRotation(_tubHome, _tubHomeRot);
            }
            if (sacks != null) sacks.TubAway = false;
        }

        void KeepLadle()
        {
            // Abort vừa thả hai tay về animation: muôi đang mang theo người thì tay nắm lại ngay (không để muôi lơ lửng trước hông).
            if (ladle != null && ladleGrip != null && !_ladleResting)
                _hands.SnapToolToHand(ladle, ladleGrip, false, NpcHandWork.FistCurl);
        }

        void Update()
        {
            // Muôi trong tay tính như đồ NPC cầm (NpcAgent.ReachAllows / LimitPlayerStep): Ba không đi hông vào cán muôi, quản bếp
            // không quét muôi vào thân Ba. Bot 01/11 (QA-0152): hông Ba lún 10,4 cm vào CookLadle/Shaft lúc kéo nồi cao lùi qua lò.
            if (agent != null)
                agent.SetHeldTool(ladle != null && !_ladleResting ? ladle : null);
        }

        /// <summary>Trả thùng trước khi routine bị hủy để dành vùng dạy gọt; không ghi chỗ trên tay thành home.</summary>
        public void ReturnTubBeforePause()
        {
            SettleTub();
            if (_hands != null)
            {
                _hands.arms.leftWeight = _hands.arms.rightWeight = 0f;
                _hands.lean = 0f;
            }
            if (sacks != null) sacks.TubAway = false;
        }

        /// <summary>Cao độ mặt bếp gang ngay dưới điểm (bắn tia xuống); không trúng thì lấy đáy nồi.</summary>
        static float StoveTop(Vector3 at, Transform pot)
        {
            var from = new Vector3(at.x, pot.position.y + 0.5f, at.z);
            return Physics.Raycast(from, Vector3.down, out var hit, 1f, ~0, QueryTriggerInteraction.Ignore) && hit.point.y > pot.position.y - 0.1f
                ? hit.point.y : pot.position.y - 0.008f;
        }

        /// <summary>Muôi dài chừng này từ tâm lòng muôi tới đuôi cán (m, GalleyNpcs.Ladle).</summary>
        const float LadleLength = 0.48f;

        /// <summary>
        /// Hướng cán muôi gác trên mặt bếp (QA-0138): nằm dọc mép lò (sang phải hay trái người đứng), nằm trọn trong viền gang và thanh đồng
        /// quanh mặt bếp. Cán chìa chéo ra ngoài mép trước thì thân cán (cao 0,906–0,918) cắt qua gờ viền (đỉnh 0,919). Dò bằng tia ngang
        /// sát mặt bếp: phía nào chạm gờ viền (hay nồi, chảo ô bên) trước hết chiều dài cán (thêm 5 cm cho thanh đồng) thì gác sang phía kia.
        /// </summary>
        static Vector3 RestHandle(Vector3 rest, Vector3 right)
        {
            // Tia thấp (ngang thân cán) gặp gờ viền; tia cao 3 cm gặp thân nồi, chảo trên ô bên cạnh.
            var low = rest + Vector3.down * 0.014f;
            var high = rest + Vector3.up * 0.03f;
            float need = LadleLength + 0.05f;
            float Ray(Vector3 from, Vector3 dir) => Physics.Raycast(from, dir, out var hit, need, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : need;
            float RoomTo(Vector3 dir) => Mathf.Min(Ray(low, dir), Ray(high, dir));
            float toRight = RoomTo(right), toLeft = RoomTo(-right);
            return toRight >= need - 1e-3f || toRight >= toLeft ? right : -right;
        }

        IEnumerator FetchTub(Transform pot, Vector3 stand, float yaw)
        {
            sacks.TubAway = true;
            // Gác muôi trên mặt bếp trước nồi: lòng muôi ngửa nằm trên mặt gang, cán chéo về phía tay phải người đứng, chìa ra
            // ngoài mép lò (không chạm nồi ô bên cạnh).
            NpcHandWork.MeasurePot(pot, out var center, out _, out float radius);
            var away = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var right = Vector3.Cross(Vector3.up, away);
            var rest = new Vector3(center.x, 0f, center.z) - away * (radius / 0.88f + 0.09f) + right * 0.12f;
            rest.y = StoveTop(rest, pot) + 0.02f;
            var handle = RestHandle(rest, right);
            yield return _hands.PutTool(ladle, rest, Quaternion.LookRotation(Vector3.up, handle), transform.parent, false);
            _ladleResting = true;
            _restStand = stand;
            _restYaw = yaw;
            yield return Lean(0f);

            yield return agent.GoWhenFree(tubStand);
            yield return agent.Face(tubYaw);
            yield return _hands.Bend(true);
            yield return _hands.HoldSides(tub);
            // Đứng dậy và nhấc thùng cùng lúc (nâng bằng chân) lên trước bụng rồi mới tính là đang bê. Đứng thẳng trước rồi mới nhấc
            // thì hai tay duỗi hết tầm, rời thành thùng còn nằm dưới sàn (đo giật 14/10).
            var carry = agent.carryPoint != null ? agent.carryPoint : transform;
            var rise = StartCoroutine(_hands.Bend(false));
            yield return NpcHandWork.Move(tub, carry.position, carry.rotation, 0.5f);
            yield return rise;
            agent.PickUp(tub);

            yield return agent.GoWhenFree(stand);
            yield return agent.Face(yaw);
            yield return Lean(12f);
            yield return _hands.PourInto(tub, pot, () =>
            {
                // Khoai rơi khỏi thùng vào lòng nồi; củ cuối chạm nồi thì nồi có khoai, thùng rỗng.
                if (sacks.isActiveAndEnabled)
                    sacks.StartCoroutine(sacks.DumpTub(center - Vector3.up * 0.06f, radius, () => meals?.AddPotatoes()));
                else
                {
                    sacks.ClearTub();
                    meals?.AddPotatoes();
                }
            });
            yield return Lean(0f);

            yield return agent.GoWhenFree(tubStand);
            yield return agent.Face(tubYaw);
            // Khom xuống và hạ thùng cùng lúc, thùng chạm sàn thì buông.
            agent.PutDown(tub.position, tub.rotation, _tubParent);
            var bend = StartCoroutine(_hands.Bend(true));
            yield return NpcHandWork.Move(tub, _tubHome, _tubHomeRot, 0.5f);
            yield return bend;
            yield return _hands.ReleaseBoth();
            yield return _hands.Bend(false);
            sacks.TubAway = false;
            TubsCarried++;

            // Về lại lò, cầm muôi lên.
            yield return agent.GoWhenFree(stand);
            yield return agent.Face(yaw);
            yield return Lean(16f);
            yield return _hands.TakeTool(ladle, ladleGrip, false, NpcHandWork.FistCurl);
            _ladleResting = false;
            yield return _hands.HoldTool(ladle, ladleGrip, false);
            yield return Lean(0f);
        }
    }
}
