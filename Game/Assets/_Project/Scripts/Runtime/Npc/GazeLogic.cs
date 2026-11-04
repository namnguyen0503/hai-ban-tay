using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>
    /// Phần tính thuần của NpcGaze (test EditMode): góc nhìn tới mắt người chơi theo hệ thân NPC, giới hạn cổ, khi nào thân quay theo.
    /// </summary>
    public static class GazeLogic
    {
        /// <summary>Đầu + cổ quay ngang tối đa so với thân (độ).</summary>
        public const float MaxYaw = 70f;
        /// <summary>Ngẩng, cúi tối đa (độ).</summary>
        public const float MaxPitchUp = 30f, MaxPitchDown = 35f;
        /// <summary>Người chơi lệch quá góc này sau lưng thì thôi ngoái theo (đầu về theo clip, chờ thân quay).</summary>
        public const float GiveUpYaw = 120f;
        /// <summary>Lệch quá góc này thì thân bắt đầu quay theo người chơi; quay tới khi còn dưới BodyTurnStop.</summary>
        public const float BodyTurnStart = 40f, BodyTurnStop = 6f;

        /// <summary>Hướng dir (thế giới) đổi sang góc ngang (độ, dương sang phải) và góc ngẩng (độ, dương là ngẩng) theo hệ thân bodyYaw.</summary>
        public static void Angles(Vector3 dir, float bodyYaw, out float yaw, out float pitch)
        {
            var local = Quaternion.Euler(0f, -bodyYaw, 0f) * dir;
            yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            pitch = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
        }

        /// <summary>Góc nhìn trong tầm cổ; false khi người chơi lệch quá GiveUpYaw (không ngoái).</summary>
        public static bool Clamp(ref float yaw, ref float pitch)
        {
            bool reachable = Mathf.Abs(yaw) <= GiveUpYaw;
            yaw = Mathf.Clamp(yaw, -MaxYaw, MaxYaw);
            pitch = Mathf.Clamp(pitch, -MaxPitchDown, MaxPitchUp);
            return reachable;
        }

        /// <summary>Hướng nhìn (thế giới) từ góc ngang, góc ngẩng theo hệ thân bodyYaw.</summary>
        public static Vector3 Direction(float bodyYaw, float yaw, float pitch) =>
            Quaternion.Euler(-pitch, bodyYaw + yaw, 0f) * Vector3.forward;

        /// <summary>Thân có quay theo người chơi không (trễ hai ngưỡng: lệch ít thì chỉ đầu theo, lệch nhiều thì quay tới gần thẳng mặt).</summary>
        public static bool BodyTurning(bool turning, float yaw)
        {
            float a = Mathf.Abs(yaw);
            return turning ? a > BodyTurnStop : a > BodyTurnStart;
        }

        /// <summary>
        /// Xoay (thế giới) đưa hướng mặt theo clip face tới hướng want: quay ngang quanh trục đứng trước, rồi ngẩng/cúi trong mặt phẳng
        /// đứng; không lăn đầu nghiêng sang bên.
        /// </summary>
        public static Quaternion Delta(Vector3 face, Vector3 want)
        {
            var f = new Vector3(face.x, 0f, face.z);
            var w = new Vector3(want.x, 0f, want.z);
            var yaw = f.sqrMagnitude > 1e-6f && w.sqrMagnitude > 1e-6f
                ? Quaternion.AngleAxis(Vector3.SignedAngle(f, w, Vector3.up), Vector3.up)
                : Quaternion.identity;
            var pitch = Quaternion.FromToRotation(yaw * face, want);
            return pitch * yaw;
        }
    }
}
