using System;
using System.Collections.Generic;
using HaiBanTay.Player;
using UnityEngine;

namespace HaiBanTay.Ship
{
    public enum ShipRegion { Exterior, Interior }

    /// <summary>Rendering Layers độc lập physics layers; giữ lớp Default cho nội dung cũ.</summary>
    public static class WorldRenderingLayers
    {
        public const uint Interior = 1u << 1;
        public const uint Exterior = 1u << 2;
    }

    /// <summary>Trigger người chơi vào/ra khoang; nhiều collider và vùng lồng nhau không gây đổi vùng sớm.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class ShipZone : MonoBehaviour
    {
        public ShipRegion region = ShipRegion.Interior;
        public event Action<bool> PlayerChanged;
        public bool PlayerInside => _players.Count > 0;
        public static event Action<ShipRegion> RegionChanged;
        public static ShipRegion CurrentRegion { get; private set; } = ShipRegion.Exterior;

        static readonly HashSet<ShipZone> Occupied = new HashSet<ShipZone>();
        readonly HashSet<Collider> _players = new HashSet<Collider>();
        readonly List<Collider> _left = new List<Collider>();
        Collider[] _areas;

        void Awake()
        {
            _areas = GetComponents<Collider>();
        }

        /// <summary>Điểm thế giới nằm trong vùng (chỉ dẫn biết đích ở bếp hay ở hầm).</summary>
        public bool Contains(Vector3 point)
        {
            if (_areas == null)
                Awake();
            foreach (var a in _areas)
                if (a != null && a.bounds.Contains(point))
                    return true;
            return false;
        }

        void OnEnable()
        {
            FirstPersonMotor.Teleported += Recheck;
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerRig>() == null || !_players.Add(other) || _players.Count != 1)
                return;
            Occupied.Add(this);
            PublishRegion();
            PlayerChanged?.Invoke(true);
        }

        void OnTriggerExit(Collider other)
        {
            if (!_players.Remove(other) || _players.Count != 0)
                return;
            Leave();
        }

        void OnDisable()
        {
            FirstPersonMotor.Teleported -= Recheck;
            _players.Clear();
            Leave();
        }

        /// <summary>
        /// Sau dịch chuyển tức thời: bỏ collider người chơi không còn chạm vùng (PhysX không gửi OnTriggerExit khi
        /// CharacterController bị tắt rồi bật lại ở chỗ khác). Vào vùng mới thì OnTriggerEnter vẫn tới như thường.
        /// </summary>
        void Recheck()
        {
            if (_players.Count == 0)
                return;
            _left.Clear();
            foreach (var player in _players)
            {
                if (player == null || !Touches(player))
                    _left.Add(player);
            }
            foreach (var player in _left)
                _players.Remove(player);
            if (_players.Count == 0)
                Leave();
        }

        bool Touches(Collider player)
        {
            var bounds = player.bounds;
            foreach (var area in _areas)
            {
                if (area.enabled && area.bounds.Intersects(bounds))
                    return true;
            }
            return false;
        }

        void Leave()
        {
            if (!Occupied.Remove(this))
                return;
            PublishRegion();
            PlayerChanged?.Invoke(false);
        }

        static void PublishRegion()
        {
            var next = ShipRegion.Exterior;
            foreach (var zone in Occupied)
                if (zone.region == ShipRegion.Interior)
                {
                    next = ShipRegion.Interior;
                    break;
                }
            if (next == CurrentRegion)
                return;
            CurrentRegion = next;
            RegionChanged?.Invoke(next);
        }
    }
}
