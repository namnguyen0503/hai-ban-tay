using System.Collections.Generic;
using UnityEngine;

namespace HaiBanTay.Player
{
    /// <summary>Nguồn nóng (lò): sức nóng giảm tuyến tính theo khoảng cách, nhân với Level (mức lửa).
    /// Đăng ký vào danh sách tĩnh để BodyState không phải Find.</summary>
    public sealed class HeatSource : MonoBehaviour
    {
        public static readonly List<HeatSource> All = new List<HeatSource>();

        public float radius = 3f;
        public float strength = 1f;
        /// <summary>0–1, do CoalStation cập nhật theo lửa lò.</summary>
        public float Level = 1f;

        void OnEnable()
        {
            All.Add(this);
        }

        void OnDisable()
        {
            All.Remove(this);
        }

        public float At(Vector3 position)
        {
            float d = Vector3.Distance(position, transform.position);
            if (d >= radius)
                return 0f;
            return strength * Level * (1f - d / radius);
        }
    }
}
