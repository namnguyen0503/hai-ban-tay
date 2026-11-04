using System.Collections.Generic;
using HaiBanTay.Editor.Setup;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>Một cột đèn điện của phố: vị trí bóng đèn, và có phải đèn realtime (không bóng) hay chỉ emissive + đèn bake.</summary>
    public struct SaigonLamp
    {
        public Vector3 bulb;
        public bool realtime;
    }

    /// <summary>
    /// Điểm neo của phố Sài Gòn (hệ thế giới Unity, mét) khớp Art/Models/Env/SaigonStreet/layout.json (xem
    /// <see cref="SaigonStreet.Layout"/>): player_spawn, le_walk_start, conversation_ba/le, electric_light_bulb, cafe_seat, crowd.paths.
    /// Hình phố là model thật (SaigonStreet.fbx). Phố chạy dọc trục X, lòng đường z ∈ [−6; 6] (y = 0), vỉa hè y = 0,12 ở hai bên,
    /// quán cà phê ở dãy nhà phía nam (−Z). Đèn: 4 cột mỗi bên (x −36, −13, 8, 31), bóng ở y 4,84.
    /// </summary>
    public sealed class SaigonAnchors
    {
        public Vector3 playerSpawn;
        public float playerYaw;
        public Vector3 leStart;
        /// <summary>Đường anh Lê đi, điểm cuối là chỗ đứng nói chuyện.</summary>
        public Vector3[] lePath;
        public Vector3 conversationBa, conversationLe;
        public float baYaw, leYaw;
        public Vector3 cafeSeat;
        public SaigonLamp[] lamps;
        /// <summary>Chỗ ngồi của khách quán (chân NPC trên sàn, yaw nhìn vào bàn).</summary>
        public (Vector3 pos, float yaw)[] cafeGuests;
        public Vector3[] northWalk, southWalk, roadWalk;
        public Vector3[] talkGroups;
        public Vector3[] standers;
        /// <summary>Biên khu vực đi được theo X: ngoài đoạn này là hàng rào, xe thồ và ván che của model.</summary>
        public float minX, maxX;

        public static SaigonAnchors Default()
        {
            const float y = 0.12f;
            return new SaigonAnchors
            {
                playerSpawn = new Vector3(-27f, y, -7.5f),
                playerYaw = 90f,
                leStart = new Vector3(-25.6f, y, -6.9f),
                lePath = new[] { new Vector3(-25.6f, y, -6.9f), new Vector3(-18f, y, -7.2f), new Vector3(-10f, y, -7.4f), new Vector3(-3.7f, y, -7.4f) },
                conversationBa = new Vector3(-5f, y, -7.4f),
                conversationLe = new Vector3(-3.7f, y, -7.4f),
                baYaw = 90f,
                leYaw = 270f,
                cafeSeat = new Vector3(9.8f, y, -8.25f),
                // Đèn thật của model (LampGlow); hai đèn realtime không bóng: một ở chỗ nói chuyện (x −13), một trước quán (x 8).
                lamps = new[]
                {
                    new SaigonLamp { bulb = new Vector3(-36f, 4.84f, -6.2f) },
                    new SaigonLamp { bulb = new Vector3(-13f, 4.84f, -6.2f), realtime = true },
                    new SaigonLamp { bulb = new Vector3(8f, 4.84f, -6.2f), realtime = true },
                    new SaigonLamp { bulb = new Vector3(31f, 4.84f, -6.2f) },
                    new SaigonLamp { bulb = new Vector3(-36f, 4.84f, 6.2f) },
                    new SaigonLamp { bulb = new Vector3(-13f, 4.84f, 6.2f) },
                    new SaigonLamp { bulb = new Vector3(8f, 4.84f, 6.2f) },
                    new SaigonLamp { bulb = new Vector3(31f, 4.84f, 6.2f) },
                },
                // Ghế quán của model (tâm ghế x 8,15 / 9,85 / 11,15 / 12,85 / 14,15 / 15,85, z −8,25); khách quay vào bàn.
                cafeGuests = new[]
                {
                    (new Vector3(8.15f, y, -8.25f), 90f), (new Vector3(12.85f, y, -8.25f), 270f), (new Vector3(14.15f, y, -8.25f), 90f),
                },
                northWalk = new[] { new Vector3(-26f, y, 8.1f), new Vector3(-8f, y, 8.1f), new Vector3(10f, y, 8.1f), new Vector3(22f, y, 8.1f) },
                southWalk = new[] { new Vector3(21f, y, -7.6f), new Vector3(30f, y, -7.4f), new Vector3(38f, y, -7.6f) },
                roadWalk = new[] { new Vector3(-8f, 0f, -3.5f), new Vector3(-1f, 0f, 3.5f), new Vector3(7f, 0f, 0f), new Vector3(-2f, 0f, -3f) },
                talkGroups = new[] { new Vector3(-16f, y, 8.4f), new Vector3(10f, y, 8.4f) },
                // Người đứng chờ lùi vào trong vỉa hè, ngoài tuyến southWalk (bot 23/10: phu lách người đứng giữa tuyến, thân chồng gốc me).
                standers = new[] { new Vector3(24.5f, y, -8.4f) },
                minX = -41f,
                maxX = 41f,
            };
        }
    }

    /// <summary>
    /// Phố Sài Gòn (00_Prologue): model Env_SaigonStreet đặt đúng chỗ theo layout.json (hệ thế giới, không dịch), biên đi bằng vật
    /// thật của model (nhà, hàng rào gỗ, xe thồ ở hai đầu phố; không tường vô hình). Mặt tiền quán cà phê và các cửa hàng đổi từ
    /// ô sáng phẳng sang cửa kính có khung và nội thất mờ phía sau (<see cref="SaigonStorefronts"/>).
    /// </summary>
    public static class SaigonStreet
    {
        public const string RootName = "SaigonStreet";

        public static EnvLayout Layout => EnvLayout.Load("SaigonStreet");

        public static Transform Place(Transform parent)
        {
            var root = BuilderUtils.Group(RootName, parent).transform;
            var instance = Layout.Place(root);
            SaigonStorefronts.Dress(root, instance);
            return root;
        }
    }
}
