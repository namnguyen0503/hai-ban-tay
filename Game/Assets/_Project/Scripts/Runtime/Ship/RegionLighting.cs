using HaiBanTay.Interaction;
using UnityEngine;
using UnityEngine.Rendering;

namespace HaiBanTay.Ship
{
    /// <summary>
    /// Đôi tay, đồ đang cầm và các cánh tay NPC gắn camera theo vùng người chơi đứng (ShipZone): trong khoang nhận đèn
    /// Interior và light probe bake; ngoài trời nhận mặt trời/trăng (Exterior) và ambient của TimeOfDay. Không có bước này
    /// thì tay người chơi không thuộc lớp nào và không đèn nào chiếu tới. Chỉ đổi khi đổi vùng hoặc nhặt đồ, không chạy mỗi khung.
    /// </summary>
    public sealed class RegionLighting : MonoBehaviour
    {
        public Transform root;
        public Interactor interactor;

        public ShipRegion Region { get; private set; } = ShipRegion.Exterior;

        void OnEnable()
        {
            ShipZone.RegionChanged += Apply;
            if (interactor != null)
                interactor.Picked += OnPicked;
            Apply(ShipZone.CurrentRegion);
        }

        void OnDisable()
        {
            ShipZone.RegionChanged -= Apply;
            if (interactor != null)
                interactor.Picked -= OnPicked;
        }

        void OnPicked(Holdable held)
        {
            Apply(Region);
        }

        public void Apply(ShipRegion region)
        {
            Region = region;
            if (root == null)
                return;
            bool inside = region == ShipRegion.Interior;
            uint mask = inside ? WorldRenderingLayers.Interior : WorldRenderingLayers.Exterior;
            var usage = inside ? LightProbeUsage.BlendProbes : LightProbeUsage.Off;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                // Hạt (hơi thở) dùng shader unlit, không cần lớp đèn.
                if (r is ParticleSystemRenderer)
                    continue;
                r.renderingLayerMask = mask;
                r.lightProbeUsage = usage;
            }
        }
    }
}
