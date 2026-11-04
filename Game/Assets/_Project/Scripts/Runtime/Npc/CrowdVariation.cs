using System;
using System.Collections.Generic;
using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>Nhóm màu của một slot material có thể đổi màu.</summary>
    public enum CrowdTintKind
    {
        /// <summary>Áo và quần (Rocketbox gộp áo, quần, da tay trong một texture Body nên chỉ đổi nhẹ để da không lệch màu).</summary>
        Clothes,
        /// <summary>Tạp dề, khăn, cuff (material Cotton): đổi mạnh được.</summary>
        Cotton,
        /// <summary>Mũ (CapCloth).</summary>
        Hat,
        /// <summary>Tóc: đổi độ đậm.</summary>
        Hair
    }

    /// <summary>Bảng màu theo nhóm (thuần, test được). Màu nhân với texture gốc nên nhạt và không quá rực.</summary>
    public static class CrowdPalette
    {
        static readonly Color[] Clothes =
        {
            new Color(0.55f, 0.52f, 0.48f), new Color(0.42f, 0.38f, 0.34f), new Color(0.62f, 0.58f, 0.5f),
            new Color(0.5f, 0.55f, 0.62f), new Color(0.38f, 0.43f, 0.5f), new Color(0.6f, 0.5f, 0.4f),
            new Color(0.34f, 0.32f, 0.3f), new Color(0.7f, 0.68f, 0.62f),
        };

        static readonly Color[] Cotton =
        {
            new Color(0.95f, 0.93f, 0.86f), new Color(0.78f, 0.7f, 0.56f), new Color(0.3f, 0.36f, 0.5f),
            new Color(0.4f, 0.3f, 0.22f), new Color(0.22f, 0.22f, 0.24f), new Color(0.55f, 0.58f, 0.46f),
            new Color(0.7f, 0.66f, 0.6f), new Color(0.5f, 0.42f, 0.36f),
        };

        static readonly Color[] Hat =
        {
            new Color(0.78f, 0.7f, 0.56f), new Color(0.3f, 0.3f, 0.32f), new Color(0.45f, 0.34f, 0.24f),
            new Color(0.86f, 0.82f, 0.72f), new Color(0.26f, 0.32f, 0.44f), new Color(0.55f, 0.5f, 0.42f),
        };

        static readonly Color[] Hair =
        {
            new Color(1f, 1f, 1f), new Color(0.75f, 0.72f, 0.7f), new Color(0.5f, 0.48f, 0.46f),
            new Color(0.9f, 0.82f, 0.74f), new Color(0.62f, 0.62f, 0.64f),
        };

        public static Color[] ColorsOf(CrowdTintKind kind)
        {
            switch (kind)
            {
                case CrowdTintKind.Cotton: return Cotton;
                case CrowdTintKind.Hat: return Hat;
                case CrowdTintKind.Hair: return Hair;
                default: return Clothes;
            }
        }

        /// <summary>Màu của một slot, xác định theo (seed, slot, nhóm): cùng đầu vào luôn ra cùng màu.</summary>
        public static Color Pick(CrowdTintKind kind, int seed, int slot)
        {
            var colors = ColorsOf(kind);
            return colors[(int)(CrowdHash.Mix(seed * 17 + (int)kind, slot) % (uint)colors.Length)];
        }
    }

    /// <summary>Một slot material của một renderer có thể đổi màu.</summary>
    [Serializable]
    public struct CrowdTintSlot
    {
        public Renderer renderer;
        public int materialIndex;
        public CrowdTintKind kind;
    }

    /// <summary>
    /// Biến thể màu của một NPC theo seed. Danh sách slot do CrowdBuilder lập theo tên material của từng prefab
    /// (CrowdBuilder.TintKindOf). Hai cách áp màu:
    /// - Vật liệu dùng chung (mặc định): mỗi (material gốc, màu bảng) có đúng một bản dùng chung cho mọi NPC, nên số vật liệu
    ///   bị chặn bởi cỡ bảng màu. SRP Batcher vẫn gộp được, SetPass gần như không tăng.
    /// - MaterialPropertyBlock (_BaseColor, usePropertyBlock): không sinh vật liệu nhưng phá SRP Batcher, mỗi draw có block
    ///   tốn một SetPass (đo ở sandbox 40 NPC, tầng Laptop: 33 → 178 SetPass), vượt ngân sách ≲ 150.
    /// </summary>
    public sealed class CrowdVariation : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // Bản dùng chung theo (material gốc, màu); bản biến thể ánh xạ ngược về material gốc để Apply gọi lại được.
        static readonly Dictionary<long, Material> Pool = new Dictionary<long, Material>();
        static readonly Dictionary<Material, Material> BaseOf = new Dictionary<Material, Material>();
        static int Instances;

        public int seed;
        public CrowdTintSlot[] slots = new CrowdTintSlot[0];
        /// <summary>Dùng MaterialPropertyBlock thay vì vật liệu dùng chung (xem mô tả lớp).</summary>
        public bool usePropertyBlock;

        /// <summary>Số vật liệu biến thể đang có (chẩn đoán, test).</summary>
        public static int PooledMaterialCount => Pool.Count;

        void Awake()
        {
            Instances++;
            Apply();
        }

        void OnDestroy()
        {
            // Hết NPC có biến thể thì bỏ các vật liệu dùng chung (cảnh vừa dỡ).
            if (--Instances > 0)
                return;
            Instances = 0;
            ClearPool();
        }

        /// <summary>Bỏ mọi vật liệu biến thể dùng chung (hết cảnh, hoặc test).</summary>
        public static void ClearPool()
        {
            foreach (var m in Pool.Values)
            {
                if (m == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(m);
                else
                    DestroyImmediate(m);
            }
            Pool.Clear();
            BaseOf.Clear();
        }

        public void Apply()
        {
            if (usePropertyBlock)
                ApplyBlocks();
            else
                ApplyShared();
        }

        void ApplyBlocks()
        {
            var block = new MaterialPropertyBlock();
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s.renderer == null)
                    continue;
                s.renderer.GetPropertyBlock(block, s.materialIndex);
                block.SetColor(BaseColorId, CrowdPalette.Pick(s.kind, seed, i));
                s.renderer.SetPropertyBlock(block, s.materialIndex);
            }
        }

        void ApplyShared()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s.renderer == null)
                    continue;
                var mats = s.renderer.sharedMaterials;
                if (s.materialIndex < 0 || s.materialIndex >= mats.Length || mats[s.materialIndex] == null)
                    continue;
                mats[s.materialIndex] = Shared(mats[s.materialIndex], CrowdPalette.Pick(s.kind, seed, i));
                s.renderer.sharedMaterials = mats;
            }
        }

        /// <summary>Bản vật liệu dùng chung của material nhân màu color (tạo lần đầu cần tới).</summary>
        public static Material Shared(Material material, Color color)
        {
            if (BaseOf.TryGetValue(material, out var original))
                material = original;
            long key = ((long)material.GetInstanceID() << 32) | (uint)ColorKey(color);
            if (!Pool.TryGetValue(key, out var variant) || variant == null)
            {
                variant = new Material(material) { name = material.name + " tint", hideFlags = HideFlags.DontSave };
                variant.SetColor(BaseColorId, color);
                Pool[key] = variant;
                BaseOf[variant] = material;
            }
            return variant;
        }

        static int ColorKey(Color c)
        {
            Color32 b = c;
            return b.r | (b.g << 8) | (b.b << 16) | (b.a << 24);
        }
    }
}
