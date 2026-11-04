using System.Collections;
using UnityEngine;

namespace HaiBanTay.Npc
{
    public enum CrowdMode
    {
        /// <summary>Phu bốc hàng: đi tới chỗ nhặt, nhặt, bê tới chỗ đặt, đặt, đi về.</summary>
        Loop,
        /// <summary>Nhóm 2–4 người đứng quay vào nhau, luân phiên Talk, Idle, IdleLook.</summary>
        Group,
        /// <summary>Hàng chờ (CrowdQueue).</summary>
        Queue,
        /// <summary>Ngồi: SitDown, SitIdle hoặc SitRead, tùy chọn StandUp.</summary>
        Sit,
        /// <summary>Đứng tựa lan can, nhìn ra xa.</summary>
        Lean,
        /// <summary>Đứng yên, thỉnh thoảng nhìn quanh.</summary>
        Stand,
        /// <summary>Đi giữa vài điểm ngẫu nhiên (có seed), nghỉ ngẫu nhiên.</summary>
        Wander,
        /// <summary>Đứng tại chỗ làm một động tác lặp (poseState: Squat, LeanRail, Pull, WorkMid, Documents…), thỉnh thoảng xen altState;
        /// oneShot thì làm động tác một lần (Wave) rồi đứng nghỉ.</summary>
        Pose
    }

    /// <summary>
    /// Một routine nhiều chế độ cho đám đông, chạy trên NpcAgent (WalkTo, Play, Face). Các điểm là Transform con do builder
    /// đặt (CrowdBuilder) nên đi theo vật cha khi bến dời. Mọi ngẫu nhiên dùng System.Random theo seed: cùng seed cùng hành vi.
    /// </summary>
    public sealed class CrowdRoutine : NpcRoutine
    {
        public CrowdMode mode;
        public int seed;
        /// <summary>Loop: [0] chỗ nhặt, rồi các điểm qua, [cuối] chỗ đặt. Wander: các điểm đi dạo.</summary>
        public Transform[] points = new Transform[0];
        /// <summary>Loop: đường về nếu khác đường đi (làn ngược chiều); trống thì về theo đường cũ.</summary>
        public Transform[] backPoints = new Transform[0];
        /// <summary>Hướng đứng (độ) cho Group, Sit, Lean, Stand.</summary>
        public float yaw;
        /// <summary>Đứng chờ bấy nhiêu giây trước khi bắt đầu, để các người cùng việc lệch nhịp nhau.</summary>
        public float startDelay;
        /// <summary>Loop: bao hoặc thùng bê trên tay (đặt ở chỗ nhặt).</summary>
        public Transform prop;
        public float pickSeconds = 1.3f;
        /// <summary>Phần thời gian clip PickUp tới lúc hai tay xuống thấp nhất (CarryFit 15/10: 0,78 s của 1,3 s, tay cách đất 12 cm): nắm / buông bao ở đây.</summary>
        public const float GrabAt = 0.6f;
        public float dropSeconds = 1.3f;
        /// <summary>Group: số hiệu seed của nhóm, thứ tự trong nhóm và sĩ số.</summary>
        public int groupSeed;
        public int groupIndex;
        public int groupCount = 1;
        /// <summary>Queue: hàng chờ dùng chung.</summary>
        public CrowdQueue queue;
        /// <summary>Sit: đứng dậy sau khi ngồi một lúc rồi ngồi lại. Tắt thì ngồi mãi.</summary>
        public bool standUp;
        /// <summary>Sit: xen kẽ SitRead (đọc báo) với SitIdle.</summary>
        public bool read;
        public Vector2 sitSeconds = new Vector2(20f, 45f);
        /// <summary>Wander, Stand, Lean: thời gian nghỉ giữa các lần.</summary>
        public Vector2 restSeconds = new Vector2(2f, 7f);
        /// <summary>Pose: động tác chính, động tác xen kẽ (có thể trống) và thời lượng của mỗi lần (giây).</summary>
        public string poseState = "Idle";
        public string altState;
        public Vector2 poseSeconds = new Vector2(8f, 20f);
        public Vector2 altSeconds = new Vector2(2f, 5f);
        /// <summary>Pose: động tác chạy một lần (Wave) trong oneShotSeconds rồi nghỉ restSeconds.</summary>
        public bool oneShot;
        public float oneShotSeconds = 2.8f;
        /// <summary>Đứng thừa: tầng Laptop bỏ bớt một phần số này (CrowdLod).</summary>
        public bool optional;
        /// <summary>Bóng dáng ở xa: CrowdLod giữ mesh tĩnh tới silhouetteRadius thay vì ẩn ở staticRadius.</summary>
        public bool silhouette;

        System.Random _rng;
        bool _leaving;

        /// <summary>Queue: ô đang đứng yên (đã tới nơi), -1 khi đang đi. CrowdQueue tự Advance khi đầu hàng đứng đủ lâu.</summary>
        public int PlacedSlot { get; private set; } = -1;

        float Range(Vector2 v) => v.x + (float)_rng.NextDouble() * (v.y - v.x);

        protected override IEnumerator Run()
        {
            _rng = new System.Random(seed * 7919 + 17);
            PlacedSlot = -1;
            _leaving = false;
            if (startDelay > 0f)
            {
                agent.Play("Idle");
                yield return new WaitForSeconds(startDelay);
            }
            switch (mode)
            {
                case CrowdMode.Loop: yield return RunLoop(); break;
                case CrowdMode.Group: yield return RunGroup(); break;
                case CrowdMode.Queue: yield return RunQueue(); break;
                case CrowdMode.Sit: yield return RunSit(); break;
                case CrowdMode.Lean: yield return RunStand(true); break;
                case CrowdMode.Stand: yield return RunStand(false); break;
                case CrowdMode.Pose: yield return RunPose(); break;
                default: yield return RunWander(); break;
            }
        }

        IEnumerator WalkRange(Transform[] pts, int from, int to)
        {
            int step = to >= from ? 1 : -1;
            for (int i = from; ; i += step)
            {
                yield return agent.WalkTo(pts[i]);
                if (i == to)
                    break;
            }
        }

        IEnumerator RunLoop()
        {
            if (points.Length < 2)
                yield break;
            int last = points.Length - 1;
            var a = points[0];
            var b = points[last];
            while (true)
            {
                yield return agent.WalkTo(a);
                yield return agent.Face(a.eulerAngles.y);
                if (prop != null)
                {
                    prop.gameObject.SetActive(true);
                    prop.SetPositionAndRotation(a.position + Vector3.up * 0.3f, a.rotation);
                }
                // Nắm bao lúc tay xuống thấp nhất trong clip rồi bao theo tay lên (NpcAgent.CarryHold), không nhảy từ đất lên vai
                // sau khi clip đã xong; đặt bao cũng buông ở đáy clip (đo xuyên thân 15/10).
                agent.Play("PickUp");
                yield return new WaitForSeconds(pickSeconds * GrabAt);
                if (prop != null)
                    agent.PickUp(prop);
                yield return new WaitForSeconds(pickSeconds * (1f - GrabAt));
                yield return WalkRange(points, 1, last);
                yield return agent.Face(b.eulerAngles.y);
                agent.Play("PickUp");
                yield return new WaitForSeconds(dropSeconds * GrabAt);
                if (agent.Carrying != null)
                {
                    var item = agent.PutDown(b.position + Vector3.up * 0.3f, b.rotation, b.parent);
                    item.gameObject.SetActive(false);
                }
                yield return new WaitForSeconds(dropSeconds * (1f - GrabAt));
                if (backPoints.Length > 0)
                    yield return WalkRange(backPoints, 0, backPoints.Length - 1);
                else if (last > 1)
                    yield return WalkRange(points, last - 1, 1);
            }
        }

        IEnumerator RunGroup()
        {
            yield return agent.Face(yaw);
            var tick = new WaitForSeconds(0.25f);
            while (true)
            {
                int speaker = CrowdGroupLogic.SpeakerAt(Time.time, groupSeed, groupCount);
                if (speaker == groupIndex)
                    agent.Play("Talk");
                else
                    agent.Play(CrowdGroupLogic.ListenerLooksAround(Time.time, groupSeed, groupIndex) ? "IdleLook" : "Idle");
                yield return tick;
            }
        }

        IEnumerator RunQueue()
        {
            if (queue == null || queue.slots.Length == 0)
                yield break;
            int placed = -1;
            while (true)
            {
                if (_leaving)
                {
                    PlacedSlot = -1;
                    yield return Leave();
                    placed = -1;
                    continue;
                }
                int slot = queue.SlotOf(this);
                if (slot < 0)
                {
                    yield return new WaitForSeconds(0.3f);
                    continue;
                }
                if (slot != placed)
                {
                    PlacedSlot = -1;
                    // Mỗi người tiến lên chậm hơn người trước một nhịp nhỏ, như người thật.
                    yield return new WaitForSeconds(0.15f * (slot + 1) + (float)_rng.NextDouble() * 0.4f);
                    if (_leaving)
                        continue;
                    var target = queue.slots[Mathf.Min(slot, queue.slots.Length - 1)];
                    yield return agent.WalkTo(target);
                    yield return agent.Face(target.eulerAngles.y);
                    placed = slot;
                    // Trong lúc đi, hàng có thể đã tiến tiếp.
                    if (queue.SlotOf(this) != slot)
                        continue;
                    PlacedSlot = slot;
                }
                agent.Play(slot > 0 && _rng.NextDouble() < 0.3 ? "IdleLook" : "Idle");
                yield return new WaitForSeconds(0.6f + (float)_rng.NextDouble());
            }
        }

        /// <summary>CrowdQueue gọi khi người đầu hàng được Advance.</summary>
        public void BeginLeaving()
        {
            _leaving = true;
        }

        IEnumerator Leave()
        {
            _leaving = false;
            if (queue.boardPath.Length > 0)
            {
                yield return Board();
                yield break;
            }
            // Xong việc ở đầu hàng: gật đầu một nhịp rồi đi.
            yield return agent.Do("Point", 1.2f);
            if (queue.exit != null)
                yield return agent.WalkTo(queue.exit);
            if (!queue.recycle)
            {
                agent.Play("Idle");
                yield break;
            }
            foreach (var p in queue.returnPath)
                yield return agent.WalkTo(p);
            while (true)
            {
                int tail = queue.TailSlot;
                yield return agent.WalkTo(queue.slots[tail]);
                if (queue.TryJoinTail(this, tail))
                    break;
            }
        }

        /// <summary>Trình vé, lên tàu theo boardPath (đi dốc), khuất vào khoang, rồi hiện lại ở respawn và về đuôi hàng.</summary>
        IEnumerator Board()
        {
            yield return agent.Do("HandOver", 2.2f);
            // Cầu tàu rộng 1,4 m: Ba đang ở lối cầu tàu (vác bao lên xuống) thì khách đứng chờ, không lách ngang cho túi quệt người
            // (bot 23/10: túi lún hông Ba 23 cm ở chân cầu tàu).
            agent.Play("Idle");
            for (float w = 0f; w < 20f && PlayerOnBoardPath(); w += 0.25f)
                yield return new WaitForSeconds(0.25f);
            agent.followSlope = true;
            foreach (var p in queue.boardPath)
                yield return agent.WalkTo(p);
            agent.followSlope = false;
            agent.SetBodyActive(false);
            yield return new WaitForSeconds(queue.boardedSeconds + Range(new Vector2(0f, 4f)));
            if (queue.respawn != null)
                agent.TeleportTo(queue.respawn.position, queue.respawn.eulerAngles.y);
            agent.SetBodyActive(true);
            foreach (var p in queue.returnPath)
                yield return agent.WalkTo(p);
            while (true)
            {
                int tail = queue.TailSlot;
                yield return agent.WalkTo(queue.slots[tail]);
                if (queue.TryJoinTail(this, tail))
                    break;
            }
        }

        bool PlayerOnBoardPath()
        {
            var player = HaiBanTay.Player.PlayerRig.Current;
            if (player == null)
                return false;
            var p = player.Position;
            var from = agent.Position;
            foreach (var t in queue.boardPath)
            {
                var to = t.position;
                var seg = new Vector2(to.x - from.x, to.z - from.z);
                var rel = new Vector2(p.x - from.x, p.z - from.z);
                float k = seg.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(rel, seg) / seg.sqrMagnitude) : 0f;
                float y = Mathf.Lerp(from.y, to.y, k);
                if ((rel - seg * k).sqrMagnitude < 1.0f && Mathf.Abs(p.y - y) < 1.5f)
                    return true;
                from = to;
            }
            return false;
        }

        /// <summary>Dừng mọi việc đi lại (bến lùi xa, người đi bộ không đuổi theo điểm đã dời). Đứng, nhóm, vẫy tay thì giữ nguyên.</summary>
        public void HaltIfWalking()
        {
            if (mode != CrowdMode.Loop && mode != CrowdMode.Queue && mode != CrowdMode.Wander)
                return;
            StopAllCoroutines();
            if (agent != null)
            {
                if (agent.Carrying != null)
                    agent.Carrying.gameObject.SetActive(false);
                agent.followSlope = false;
                agent.ClearWalkState();
                if (agent.BodyActive)
                    agent.Play("Idle");
            }
        }

        IEnumerator RunSit()
        {
            yield return agent.Face(yaw);
            while (true)
            {
                // Khách ngồi suốt cảnh đã được builder căn theo SitIdle; không phát chuyển tiếp đứng chưa căn.
                if (standUp) yield return agent.Do("SitDown", 2.9f);
                float until = Time.time + Range(sitSeconds);
                while (!standUp || Time.time < until)
                {
                    agent.Play(read && _rng.NextDouble() < 0.5 ? "SitRead" : "SitIdle");
                    yield return new WaitForSeconds(Range(new Vector2(5f, 12f)));
                }
                yield return agent.Do("StandUp", 2.5f);
                yield return agent.Do("Idle", Range(restSeconds));
            }
        }

        IEnumerator RunStand(bool lean)
        {
            yield return agent.Face(yaw);
            while (true)
            {
                // Tựa lan can thì chủ yếu nhìn ra xa; đứng thường chủ yếu đứng yên.
                double look = lean ? 0.7 : 0.3;
                yield return agent.Do(_rng.NextDouble() < look ? "IdleLook" : "Idle", Range(restSeconds));
            }
        }

        IEnumerator RunPose()
        {
            yield return agent.Face(yaw);
            while (true)
            {
                if (oneShot)
                {
                    // Vẫy tay: một nhịp động tác, rồi đứng nghỉ một lúc.
                    yield return agent.Do(poseState, oneShotSeconds);
                    yield return agent.Do(_rng.NextDouble() < 0.3 ? "IdleLook" : "Idle", Range(restSeconds));
                    continue;
                }
                yield return agent.Do(poseState, Range(poseSeconds));
                if (!string.IsNullOrEmpty(altState))
                    yield return agent.Do(altState, Range(altSeconds));
            }
        }

        IEnumerator RunWander()
        {
            if (points.Length < 2)
            {
                yield return RunStand(false);
                yield break;
            }
            int current = -1;
            while (true)
            {
                int next;
                do
                    next = _rng.Next(points.Length);
                while (next == current);
                yield return agent.WalkTo(points[next]);
                current = next;
                yield return agent.Face((float)_rng.NextDouble() * 360f);
                yield return agent.Do(_rng.NextDouble() < 0.4 ? "IdleLook" : "Idle", Range(restSeconds));
            }
        }
    }
}
