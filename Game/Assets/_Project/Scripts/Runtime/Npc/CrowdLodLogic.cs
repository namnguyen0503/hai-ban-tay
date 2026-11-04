using System.Collections.Generic;
using UnityEngine;

namespace HaiBanTay.Npc
{
    public enum CrowdLodMode
    {
        /// <summary>Animator và SkinnedMeshRenderer chạy.</summary>
        Animated,
        /// <summary>Tư thế bake sẵn, MeshRenderer tĩnh, Animator tắt.</summary>
        Static,
        /// <summary>Ẩn hẳn (vẫn chạy routine để vị trí đúng khi hiện lại).</summary>
        Hidden
    }

    public struct CrowdLodCandidate
    {
        public float Distance;
        public bool InFrustum;
        public bool WasAnimated;
        /// <summary>Vừa đổi chế độ, chưa được lên Animator: bỏ qua để không đẩy người khác xuống vô ích.</summary>
        public bool Locked;
    }

    /// <summary>
    /// Chọn NPC nào chạy Animator (lớp thuần, test được): tối đa max người gần camera nhất, nằm trong frustum và trong
    /// bán kính. Người đang chạy Animator được ưu tiên giữ (khoảng cách tính bớt keepBonus, bán kính nới thêm keepMargin)
    /// để khi người chơi đứng yên hoặc rung nhẹ, tập được chọn không đổi qua lại.
    /// </summary>
    public static class CrowdLodLogic
    {
        public const float DefaultKeepBonus = 3f;
        public const float DefaultKeepMargin = 2f;

        /// <summary>Đánh dấu chosen[i] = true cho các ứng viên được chọn (chosen cùng độ dài ứng viên). Trả về số người được chọn.</summary>
        public static int SelectAnimated(IReadOnlyList<CrowdLodCandidate> candidates, int max, float radius, bool[] chosen,
            float keepBonus = DefaultKeepBonus, float keepMargin = DefaultKeepMargin)
        {
            int n = candidates.Count;
            for (int i = 0; i < n; i++)
                chosen[i] = false;
            int picked = 0;
            while (picked < max)
            {
                int best = -1;
                float bestScore = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    if (chosen[i])
                        continue;
                    var c = candidates[i];
                    float limit = c.WasAnimated ? radius + keepMargin : radius;
                    if (!c.InFrustum || c.Distance > limit || (c.Locked && !c.WasAnimated))
                        continue;
                    float score = c.Distance - (c.WasAnimated ? keepBonus : 0f);
                    // Bằng điểm thì lấy chỉ số nhỏ hơn (duyệt tăng dần, so sánh nghiêm ngặt).
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = i;
                    }
                }
                if (best < 0)
                    break;
                chosen[best] = true;
                picked++;
            }
            return picked;
        }

        /// <summary>
        /// Người không chạy Animator: giữ mesh tĩnh trong staticRadius, hoặc trong silhouetteRadius nếu là bóng dáng ở xa;
        /// xa hơn thì ẩn. margin là độ trễ để không nhấp nháy ở ranh giới.
        /// </summary>
        public static CrowdLodMode Classify(float distance, CrowdLodMode previous, float staticRadius, float silhouetteRadius,
            bool silhouette, float margin)
        {
            float limit = silhouette ? Mathf.Max(staticRadius, silhouetteRadius) : staticRadius;
            limit += previous == CrowdLodMode.Hidden ? -margin : margin;
            return distance <= limit ? CrowdLodMode.Static : CrowdLodMode.Hidden;
        }

        /// <summary>Hình cầu (tâm, bán kính) có chạm frustum không (6 mặt phẳng từ GeometryUtility.CalculateFrustumPlanes).</summary>
        public static bool InFrustum(Plane[] planes, Vector3 center, float radius)
        {
            for (int i = 0; i < planes.Length; i++)
                if (planes[i].GetDistanceToPoint(center) < -radius)
                    return false;
            return true;
        }

        /// <summary>Tư thế bake gần nhất cho một trạng thái Animator (xem CrowdLod.PoseStates).</summary>
        public static int PoseOf(string state)
        {
            switch (state)
            {
                case "Walk": return 1;
                case "CarryWalk": return 2;
                case "HoldCarry":
                case "PickUp": return 3;
                case "SitIdle":
                case "SitRead":
                case "SitChair":
                case "Drink":
                case "SitDown": return 4;
                case "WorkTable":
                case "WorkMid":
                case "Stir": return 5;
                case "ShoulderCarry":
                case "ShoulderIdle": return 6;
                case "BagWalk":
                case "BagIdle": return 7;
                case "Squat": return 8;
                case "LeanRail": return 9;
                default: return 0;
            }
        }
    }
}
