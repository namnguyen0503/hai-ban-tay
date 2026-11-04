using System.Collections;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>
    /// Phụ bếp Pháp (nhân vật hư cấu): nạp than từng ô lò bằng xẻng của mình — khom gối nắm cán xẻng cắm sẵn trong thùng than,
    /// đẩy lưỡi vào đống than rồi nhấc lên có than, đứng dậy bê xẻng chéo trước người tới cửa nạp, khom gối đưa lưỡi vào buồng đốt
    /// lật cho than rơi xuống ghi, mang xẻng về cắm lại vào thùng than; xong một lượt thì đứng trông lò nam. Tay bám cán xẻng
    /// (NpcHandWork), chỗ đứng tính theo tầm tay để lưỡi xẻng nằm đúng đống than / cửa lò; đi theo NavMesh (useNavMesh).
    /// Lượt phục vụ, MealPots dùng phụ bếp bê nồi bẩn (tay bám hai quai nồi).
    /// </summary>
    public sealed class HelperRoutine : NpcRoutine
    {
        public CoalStation coal;
        [Tooltip("Xẻng của phụ bếp: gốc ở gót lưỡi, +Z dọc cán về tay cầm, +Y mặt lưỡi; con GripR (cuối cán), GripL (giữa cán), CoalLoad.")]
        public Transform shovel;
        public Transform shovelGripLeft, shovelGripRight;
        public GameObject shovelLoad;
        [Tooltip("Chỗ đứng gần thùng than (chỉ để biết thùng than nằm hướng nào; chỗ xúc thật tính theo tầm tay).")]
        public Vector3 coalStand = new Vector3(0.3f, 0f, 1.75f);
        public float coalYaw;
        [Tooltip("Chỗ đứng trông lò nam giữa hai lượt nạp than.")]
        public Vector3 watchSpot = new Vector3(-3.4f, 0f, -1.85f);
        public float watchYaw = 180f;
        public Vector2 watchSeconds = new Vector2(6f, 9f);
        [Tooltip("Lưỡi xẻng vào sâu trong cửa nạp (m) lúc lật đổ than.")]
        public float feedDepth = 0.2f;

        /// <summary>
        /// Ba đứng cách đường lưỡi xẻng (chỗ đứng → đống than / cửa lò) dưới chừng này (m) thì phụ bếp đứng chờ, không xúc, không nạp
        /// (thân Ba 0,25 + nửa lưỡi 0,13 + lề). Bot 17/10: lưỡi xẻng xuyên ngực, hông Ba 5–13 cm lúc Ba cời tro ở cửa dưới cùng ô lò.
        /// </summary>
        const float ShovelClear = 0.6f;
        /// <summary>Đang cầm xẻng thì chờ Ba ở phía trước xa hơn khoảng thường chừng này (lưỡi xẻng bê chéo trước người).</summary>
        const float CarryClear = 0.45f;
        /// <summary>Ba đứng chắn chỗ xúc quá lâu thì bỏ lượt này, ra đứng trông lò.</summary>
        const float ClearWaitMax = 20f;
        /// <summary>
        /// Ba đứng cách phụ bếp dưới chừng này (m) thì chưa đổi tư thế xẻng (xúc → bê, bê → nạp, cắm lại): cán dài 0,72 m quét chéo
        /// trước người, bot 18/10 đứng cạnh chỗ nạp bị cán xuyên hông 8–12 cm.
        /// </summary>
        const float SweepClear = 1.0f;

        NpcHandWork _hands;
        int _cell;
        bool _clear;
        BoxCollider _guard;

        public int Loops { get; private set; }
        public int Feeds { get; private set; }

        void Start()
        {
            // Hai người bếp nhường nhau, không đi xuyên người nhau ở lối trước lò (clip 14/10: quản bếp đi xuyên vai phụ bếp 0,7 s).
            if (agent != null) agent.avoidOthers = true;
            if (shovelLoad != null) shovelLoad.SetActive(false);
            Rest();
        }

        Vector3 PileTop()
        {
            var top = coal.binPile.position;
            var pile = coal.binPile.GetComponentInChildren<Renderer>();
            if (pile != null) top = new Vector3(pile.bounds.center.x, pile.bounds.max.y, pile.bounds.center.z);
            return top;
        }

        /// <summary>Chỗ đứng và hướng mặt để lưỡi xẻng (tư thế khom) nằm ở target, đi vào theo hướng inward (ngang).</summary>
        public static void StandFor(Vector3 target, Vector3 inward, float floorY, out Vector3 stand, out float yaw)
        {
            NpcHandWork.LowBlade(out var blade, out var thrust);
            inward.y = 0f;
            inward = inward.sqrMagnitude > 1e-4f ? inward.normalized : Vector3.forward;
            yaw = Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg - Mathf.Atan2(thrust.x, thrust.z) * Mathf.Rad2Deg;
            stand = target - Quaternion.Euler(0f, yaw, 0f) * new Vector3(blade.x, 0f, blade.z);
            stand.y = floorY;
        }

        /// <summary>Chỗ xúc: lưỡi xẻng ở phần trước đống than, đi vào theo hướng từ chỗ đứng gần thùng tới đống than.</summary>
        public void ScoopStand(out Vector3 stand, out float yaw)
        {
            var top = PileTop();
            var inward = top - coalStand;
            inward.y = 0f;
            inward.Normalize();
            StandFor(top - inward * 0.08f - Vector3.up * 0.04f, inward, coalStand.y, out stand, out yaw);
        }

        /// <summary>Chỗ nạp than cho một cửa lò: lưỡi xẻng vào sâu feedDepth trong cửa, thẳng góc mặt lò.</summary>
        public void FeedStand(Transform mouth, out Vector3 stand, out float yaw)
        {
            var into = mouth.forward;
            into.y = 0f;
            var room = coalStand - mouth.position;
            room.y = 0f;
            // Hướng vào lò là hướng xa phía phòng (người đứng ở phòng).
            if (Vector3.Dot(into, room) > 0f) into = -into;
            into.Normalize();
            StandFor(mouth.position + into * feedDepth, into, coalStand.y, out stand, out yaw);
        }

        /// <summary>
        /// Xẻng cắm dựng trong đống than giữa hai lượt: lưỡi vùi sâu, cán ngả về phía sau thùng (xa lối người đứng đổ sọt than).
        /// Tư thế xúc (cán chĩa ra phòng) chỉ có lúc phụ bếp cầm; trước đó cán chĩa đúng chỗ Ba đứng đổ sọt, hông Ba xuyên cán 11 cm.
        /// </summary>
        void Rest()
        {
            if (shovel == null || coal == null || coal.binPile == null) return;
            LeanPose(out var p, out var r);
            shovel.SetParent(transform.parent, true);
            shovel.SetPositionAndRotation(p, r);
        }

        /// <summary>Tư thế cắm dựng: quay tư thế xúc quanh gót lưỡi cho cán ngả 28° về phía sau thùng, gót lùi sâu 12 cm vào đống than.</summary>
        void LeanPose(out Vector3 position, out Quaternion rotation)
        {
            ScoopStand(out var stand, out float yaw);
            NpcHandWork.ShovelRest(stand, yaw, out var p, out var r);
            var inward = PileTop() - coalStand;
            inward.y = 0f;
            inward.Normalize();
            var lean = (Vector3.up * 0.88f + inward * 0.47f).normalized;
            rotation = Quaternion.FromToRotation(r * Vector3.forward, lean) * r;
            position = p + inward * 0.12f;
        }

        /// <summary>Đổi xẻng (đang nằm ngoài tay) giữa tư thế xúc và tư thế cắm dựng trong <paramref name="seconds"/> giây.</summary>
        IEnumerator Tip(bool toLean, float seconds)
        {
            ScoopStand(out var stand, out float yaw);
            NpcHandWork.ShovelRest(stand, yaw, out var sp, out var sr);
            LeanPose(out var lp, out var lr);
            Vector3 p0 = shovel.position, p1 = toLean ? lp : sp;
            Quaternion r0 = shovel.rotation, r1 = toLean ? lr : sr;
            for (float e = 0f; e < seconds; e += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, e / seconds);
                shovel.SetPositionAndRotation(Vector3.Lerp(p0, p1, k), Quaternion.Slerp(r0, r1, k));
                yield return null;
            }
            shovel.SetPositionAndRotation(p1, r1);
        }

        protected override void Stopped()
        {
            if (agent != null) agent.extraPlayerClearance = 0f;
            Guard(false);
            if (_hands == null) return;
            _hands.Abort();
            if (shovelLoad != null) shovelLoad.SetActive(false);
            // Cả nhóm NPC đang tắt (hết ngày): không đổi cha lúc này; Run cắm xẻng lại khi bật.
            if (gameObject.activeInHierarchy) Rest();
        }

        protected override IEnumerator Run()
        {
            _hands = GetComponent<NpcHandWork>();
            if (shovel != null && shovel.parent == transform) Rest();
            while (true)
            {
                if (_hands != null && shovel != null && coal != null && coal.binPile != null && coal.cells.Length > 0)
                {
                    int cells = coal.cells.Length;
                    for (int n = 0; n < cells && coal.cells[_cell % cells].mouth == null; n++) _cell++;
                    var mouth = coal.cells[_cell % cells].mouth;
                    _cell++;
                    if (mouth != null)
                        yield return FeedOnce(mouth.transform);
                }
                yield return agent.Go(watchSpot);
                yield return agent.Face(watchYaw);
                agent.Play("IdleLook");
                yield return new WaitForSeconds(Random.Range(watchSeconds.x, watchSeconds.y));
                Loops++;
            }
        }

        IEnumerator FeedOnce(Transform mouth)
        {
            ScoopStand(out var scoopStand, out float scoopYaw);
            FeedStand(mouth, out var feedStand, out float feedYaw);
            yield return agent.GoWhenFree(scoopStand);
            yield return agent.Face(scoopYaw);
            // Ba đứng ở đống than hay trước cửa lò sắp nạp thì chưa cầm xẻng; chờ lâu quá thì bỏ lượt.
            yield return WaitPlayerClear(scoopStand, PileTop(), ClearWaitMax, true);
            if (!_clear) yield break;
            yield return WaitPlayerClear(feedStand, mouth.position, ClearWaitMax);
            if (!_clear) yield break;
            // Cán xẻng dựng ngả sau thùng: cúi người với tay phải nắm giữa cán, kéo cán ngả về phía mình, buông ra rồi mới khom nắm hai tay.
            yield return HandTip(false);
            yield return _hands.PickShovel(shovel, shovelGripLeft, shovelGripRight);
            Guard(true);
            yield return _hands.Scoop(shovel, shovelLoad);

            agent.extraPlayerClearance = CarryClear;
            yield return agent.GoWhenFree(feedStand);
            yield return agent.Face(feedYaw);
            // Đang bê xẻng đầy than: chờ tới khi Ba tránh khỏi cửa lò, không bỏ lượt giữa chừng.
            yield return WaitPlayerClear(feedStand, mouth.position, float.PositiveInfinity, true);
            yield return _hands.Feed(shovel, shovelLoad);
            Feeds++;

            // Mang xẻng về cắm lại vào thùng than.
            yield return agent.GoWhenFree(scoopStand);
            yield return agent.Face(scoopYaw);
            yield return WaitPlayerClear(scoopStand, PileTop(), float.PositiveInfinity, true);
            yield return _hands.PutShovel(shovel, transform.parent);
            Guard(false);
            agent.extraPlayerClearance = 0f;
            yield return HandTip(true);
        }

        /// <summary>
        /// Xẻng đang cầm chặn thân người chơi: hộp bao lưỡi và cán (layer Ignore Raycast, không chắn tia nhắm của Interactor); tắt theo lúc
        /// NPC nhường đường (đồ vẫn chặn Ba qua NpcAgent.LimitPlayerStep). Bot 18/10 ngày 3: Ba lùi kéo chảo đi sát phụ bếp đang bê xẻng, bụng/ngực xuyên cán và mép lưỡi 7–10 cm.
        /// </summary>
        void Guard(bool on)
        {
            if (shovel == null) return;
            if (_guard == null)
            {
                if (!on) return;
                var go = new GameObject("PlayerGuard") { layer = 2 };
                go.transform.SetParent(shovel, false);
                _guard = go.AddComponent<BoxCollider>();
                // Lưỡi từ z −0,26 tới cán cuối z 0,72 (GalleyNpcs.Shovel), cộng lề 12 cm: ngực, bụng thân Ba nhô trước vỏ va chạm ~10 cm,
                // lề 2 cm thì ngực vẫn lún lưỡi và than trên lưỡi 11–14 cm (bot 23/10 D4); nới tới 18 cm không bớt lún (8–9 cm ở đầu lưỡi) mà có lần kẹt Ba cạnh lò (bot 24/10 D3): giữ 12 cm.
                _guard.center = new Vector3(0f, 0.06f, 0.23f);
                _guard.size = new Vector3(0.48f, 0.3f, 1.22f);
            }
            // NPC đang nhường (NpcAgent.SetSolid) thì hộp chờ tới lúc hết nhường mới bật.
            _guard.enabled = on && (agent == null || !agent.YieldingToPlayer);
            if (agent != null)
            {
                agent.playerGuard = on ? _guard : null;
                // Mesh xẻng (lưỡi, cán, than trên lưỡi) đo với vỏ thân Ba ở cả hai phía: hộp PlayerGuard chỉ chặn capsule (QA-0150).
                agent.SetHeldTool(on ? shovel : null);
            }
        }

        /// <summary>Ba (chân) cách đoạn from → to trên mặt sàn dưới ShovelClear.</summary>
        static bool PlayerOnPath(Vector3 from, Vector3 to)
        {
            var player = PlayerRig.Current;
            if (player == null) return false;
            var q = new Vector2(player.Position.x, player.Position.z);
            var a = new Vector2(from.x, from.z);
            var ab = new Vector2(to.x, to.z) - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude) : 0f;
            return (a + ab * t - q).sqrMagnitude < ShovelClear * ShovelClear;
        }

        /// <summary>
        /// Đứng yên chờ Ba ra khỏi đường lưỡi xẻng from → to (và, với aroundMe, ra khỏi vòng SweepClear quanh phụ bếp); _clear = false
        /// nếu quá maxWait giây.
        /// </summary>
        IEnumerator WaitPlayerClear(Vector3 from, Vector3 to, float maxWait, bool aroundMe = false)
        {
            _clear = true;
            for (float t = 0f; PlayerOnPath(from, to) || aroundMe && PlayerNear(); t += Time.deltaTime)
            {
                if (t >= maxWait)
                {
                    _clear = false;
                    break;
                }
                agent.SetWaiting(true);
                yield return null;
            }
            agent.SetWaiting(false);
        }

        bool PlayerNear()
        {
            var player = PlayerRig.Current;
            if (player == null) return false;
            var d = player.Position - transform.position;
            d.y = 0f;
            return d.sqrMagnitude < SweepClear * SweepClear;
        }

        /// <summary>Tay phải nắm giữa cán, người cúi tới, đẩy cán dựng lên (cắm xong) hay kéo cán ngả xuống (sắp xúc), rồi buông.</summary>
        IEnumerator HandTip(bool toLean)
        {
            StartCoroutine(_hands.Lean(28f, 0.35f));
            yield return _hands.Reach(false, shovelGripLeft, NpcHandWork.FistCurl);
            StartCoroutine(_hands.Lean(0f, 0.5f));
            yield return Tip(toLean, 0.5f);
            yield return _hands.Release(false, 0.25f);
        }
    }
}
