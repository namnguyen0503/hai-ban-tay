using UnityEngine;

namespace HaiBanTay.Audio
{
    public enum FootstepKind
    {
        Wood,
        Metal,
        Stone
    }

    /// <summary>Chọn tiếng bước chân theo tên collider dưới chân: kè, phố, sàn bếp lát gạch là đá; cầu thang sắt là kim loại;
    /// còn lại (boong, hầm, cầu thang tàu gỗ) là gỗ. Logic thuần, không tạo chuỗi mới mỗi bước.</summary>
    public static class FootstepSurface
    {
        static readonly string[] StoneWords =
            { "quay", "street", "sidewalk", "pavement", "cobble", "stone", "foundation", "ground", "road", "verandah", "galleyfloor" };
        static readonly string[] MetalWords = { "stair", "ladder", "metal", "iron", "grate", "hatch" };

        static readonly string[][] Keys =
        {
            new[] { "SFX/footstep_wood_1", "SFX/footstep_wood_2", "SFX/footstep_wood_3", "SFX/footstep_wood_4" },
            new[] { "SFX/footstep_metal_1", "SFX/footstep_metal_2", "SFX/footstep_metal_3" },
            new[] { "SFX/footstep_stone_1", "SFX/footstep_stone_2", "SFX/footstep_stone_3", "SFX/footstep_stone_4" },
        };

        public static FootstepKind Classify(string colliderName, string parentName = null)
        {
            if (Has(colliderName, MetalWords) || Has(parentName, MetalWords))
                return FootstepKind.Metal;
            if (Has(colliderName, StoneWords) || Has(parentName, StoneWords))
                return FootstepKind.Stone;
            return FootstepKind.Wood;
        }

        /// <summary>Khóa clip cho bước thứ <paramref name="step"/>; xoay vòng các biến thể, không lặp liền một biến thể.</summary>
        public static string Key(FootstepKind kind, int step)
        {
            var set = Keys[(int)kind];
            return set[Mathf.Abs(step) % set.Length];
        }

        static bool Has(string name, string[] words)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            foreach (var w in words)
                if (name.IndexOf(w, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }
    }
}
