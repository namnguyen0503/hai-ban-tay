using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Ship;
using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Kéo lê một đồ nặng đặt trên sàn (chảo đồng, nồi cao): bám, đi lùi, đồ cạ sàn theo sau và trượt dọc vật cản thay vì kẹt;
    /// tàu lắc đẩy đồ trượt ngang (TowLogic); người đi chậm, mệt dần, có tiếng cạ sàn. Tách khỏi PanStation để chảo và nồi cao
    /// cùng một đường kéo. Trạm sở hữu đồ (đích, giao, hướng dẫn) và gọi Begin / Step / End.
    /// </summary>
    public sealed class VesselTow
    {
        readonly Rigidbody _body;
        float _scraped;

        public TowLogic Logic { get; } = new TowLogic();
        /// <summary>Tốc độ người đi khi kéo (m/s).</summary>
        public float TowSpeed = 0.6f;
        public float ExertPerSecond = 0.006f;
        /// <summary>Quãng kéo giữa hai tiếng cạ sàn (m).</summary>
        public float ScrapeEvery = 0.6f;
        public bool Gripping => Logic.Gripping;

        /// <param name="body">Rigidbody kinematic của vật (SweepTest khung sau thấy đúng chỗ).</param>
        public VesselTow(Rigidbody body)
        {
            _body = body;
        }

        /// <summary>Bám vật: hạn tốc độ người, vật thành vật cản của NPC, tiếng kim loại. false nếu đã bám.</summary>
        public bool Begin(PlayerRig rig, GameRoot root)
        {
            if (!Logic.Grip())
                return false;
            rig.motor.SpeedCap = TowSpeed;
            if (!NpcAgent.Obstacles.Contains(_body.transform))
                NpcAgent.Obstacles.Add(_body.transform);
            root?.Audio.PlaySfx("SFX/metal_clank", 0.6f);
            _scraped = 0f;
            return true;
        }

        /// <summary>Buông (hết kéo, tuột tay, giao tới nơi): trả tốc độ người và bỏ vật cản NPC.</summary>
        public void End(PlayerRig rig)
        {
            Logic.Release();
            if (_body != null)
                NpcAgent.Obstacles.Remove(_body.transform);
            if (rig != null)
                rig.motor.SpeedCap = float.PositiveInfinity;
        }

        /// <summary>
        /// Một khung kéo: vật đi theo tay (TowLogic.Step) và trượt dọc vật cản, người mệt thêm, cạ sàn phát tiếng.
        /// Trả về quãng đã đi (m); <paramref name="slipped"/> true khi tuột tay (quay mặt đi hoặc vật kẹt lùi xa).
        /// </summary>
        public float Step(PlayerRig rig, GameRoot root, float dt, out bool slipped)
        {
            var ship = ShipMotion.Current;
            float roll = ship != null && ship.maxRollDegrees > 0f ? ship.Roll / ship.maxRollDegrees : 0f;
            var beam = ship != null ? ship.transform.right : Vector3.right;
            var from = _body.position;
            var want = Logic.Step(from, rig.Position, roll, beam, dt);
            // Vật nặng không giật theo tay: mỗi khung đi tối đa 1,5 × tốc độ kéo (bám lúc đứng xa hơn tầm tay thì vật nhích dần tới, không
            // nhảy 5 cm trong một khung).
            var delta = want - from;
            float most = TowSpeed * 1.5f * Mathf.Max(0f, dt);
            if (delta.magnitude > most) want = from + delta.normalized * most;
            float moved = Move(from, want);
            if (moved > 0.0005f)
            {
                _scraped += moved;
                BodyState.Current?.Exert(ExertPerSecond * dt);
                if (_scraped >= ScrapeEvery)
                {
                    _scraped = 0f;
                    root?.Audio.PlaySfxAt("SFX/scrape", _body.position);
                }
            }
            slipped = Logic.CheckSlip(_body.position, rig.Position, rig.cam.transform.forward);
            return moved;
        }

        /// <summary>
        /// Đẩy vật tới vị trí mong muốn, dừng trước tường/đồ đạc (SweepTest); phần còn lại trượt dọc mặt vật cản để vật cạ qua góc
        /// thùng, chân bàn thay vì kẹt chết. Trả về quãng đã đi.
        /// </summary>
        public float Move(Vector3 from, Vector3 want)
        {
            var delta = want - from;
            delta.y = 0f;
            float moved = 0f;
            for (int i = 0; i < 2; i++)
            {
                float dist = delta.magnitude;
                if (dist < 1e-5f)
                    break;
                var dir = delta / dist;
                float free = dist;
                bool blocked = Sweep(dir, dist + 0.02f, out var hit);
                if (blocked)
                    free = Mathf.Max(0f, hit.distance - 0.02f);
                Place(_body.position + dir * free, _body.rotation);
                moved += free;
                if (!blocked)
                    break;
                delta = Vector3.ProjectOnPlane(dir * (dist - free), hit.normal);
                delta.y = 0f;
            }
            return moved;
        }

        /// <summary>
        /// Vật cản gần nhất trên đường vật. Thân NPC cũng chặn: NPC đứng chờ vật đi qua, vật trượt vòng qua người thay vì đè lên
        /// (bot 23/10: Ba kéo nồi cao vào phụ bếp đang đứng chờ). NPC đang nhường người chơi thì collider tắt, vật đi qua như cũ.
        /// Đồ NPC cầm không chặn.
        /// </summary>
        bool Sweep(Vector3 dir, float distance, out RaycastHit nearest)
        {
            nearest = default;
            bool any = false;
            foreach (var h in _body.SweepTestAll(dir, distance, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.GetComponentInParent<PlayerRig>() != null)
                    continue;
                // Chỉ thân NPC chặn; đồ NPC cầm (xẻng phụ bếp) không: thân đã nhường mà xẻng vẫn chặn thì nồi đứng, Ba kẹt 10 s (bot 23/10 D4).
                var npc = h.collider.GetComponentInParent<NpcAgent>();
                if (npc != null && h.collider != npc.BodyCollider)
                    continue;
                if (!any || h.distance < nearest.distance)
                    nearest = h;
                any = true;
            }
            return any;
        }

        /// <summary>Đặt cả Transform lẫn Rigidbody (kinematic) để SweepTest khung sau thấy đúng chỗ.</summary>
        public void Place(Vector3 position, Quaternion rotation)
        {
            _body.transform.SetPositionAndRotation(position, rotation);
            _body.position = position;
            _body.rotation = rotation;
        }
    }
}
