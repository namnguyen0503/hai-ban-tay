using System;
using UnityEngine;

namespace HaiBanTay.Performance
{
    /// <summary>
    /// Kính, ngọn lửa của vật đèn chỉ sáng khi Light của nó đang bật: LightBudget tắt đèn realtime theo vùng/giờ (đèn treo ban
    /// ngày ↔ đèn dầu buổi tối, nhường suất cho đèn fill tay) thì ngọn lửa ẩn (tắt renderer lõi lửa, hoặc đổi sang vật liệu ẩn)
    /// và kính đổi sang bản trong suốt không phát sáng, không để đèn tắt mà kính vẫn sáng hay thành khối đen đặc. Không tạo vật liệu mới lúc chạy (chỉ đổi sharedMaterials), không Find, chỉ đọc một cờ mỗi frame.
    /// </summary>
    public sealed class LampGlowSync : MonoBehaviour
    {
        [Serializable]
        public struct Part
        {
            public Renderer renderer;
            public int slot;
            public Material lit;
            public Material unlit;
            /// <summary>Renderer riêng của ngọn lửa: đèn tắt thì tắt hẳn renderer (không vẽ), thay vì đổi vật liệu.</summary>
            public bool toggleRenderer;
        }

        public Light lamp;
        public Part[] parts = Array.Empty<Part>();

        bool _on;
        bool _applied;

        void OnEnable() => Apply(IsLit());

        void LateUpdate()
        {
            bool on = IsLit();
            if (!_applied || on != _on)
                Apply(on);
        }

        bool IsLit() => lamp == null || lamp.isActiveAndEnabled;

        void Apply(bool on)
        {
            _on = on;
            _applied = true;
            foreach (var part in parts)
            {
                if (part.renderer == null)
                    continue;
                if (part.toggleRenderer)
                {
                    part.renderer.enabled = on;
                    continue;
                }
                var mats = part.renderer.sharedMaterials;
                if (part.slot < 0 || part.slot >= mats.Length)
                    continue;
                var wanted = on ? part.lit : part.unlit;
                if (wanted != null && mats[part.slot] != wanted)
                {
                    mats[part.slot] = wanted;
                    part.renderer.sharedMaterials = mats;
                }
            }
        }
    }
}
