using System;
using UnityEngine;

namespace HaiBanTay.Player
{
    /// <summary>Tư thế đôi tay (D5). Lowered = tay thả thấp gần mép khung hình.</summary>
    public enum HandPose
    {
        Lowered,
        Open,
        Grip,
        Carry,
        Shovel
    }

    /// <summary>Dụng cụ đang cầm (lấy ở móc treo, treo lại khi xong). Cầm dụng cụ thì không nhấc được đồ khác.</summary>
    public enum Tool
    {
        None,
        Shovel,
        Rake,
        Broom,
        /// <summary>Bàn chải ngắn cọ nồi ở chậu (một tay phải, kệ cạnh chậu).</summary>
        Brush,
        /// <summary>Bàn chải cán dài (chảo dưới sàn, nồi cao), treo móc góc nồi cao; hai tay trên cán khi cọ.</summary>
        LongBrush
    }

    [Serializable]
    public struct HandPoseTarget
    {
        public HandPose pose;
        public Vector3 leftPos;
        public Vector3 leftEuler;
        public Vector3 rightPos;
        public Vector3 rightEuler;

        public HandPoseTarget(HandPose pose, Vector3 leftPos, Vector3 leftEuler, Vector3 rightPos, Vector3 rightEuler)
        {
            this.pose = pose;
            this.leftPos = leftPos;
            this.leftEuler = leftEuler;
            this.rightPos = rightPos;
            this.rightEuler = rightEuler;
        }
    }
}
