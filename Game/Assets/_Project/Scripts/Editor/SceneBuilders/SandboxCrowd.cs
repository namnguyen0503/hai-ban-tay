using System;
using HaiBanTay.Editor.Setup;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>
    /// Khu thử đám đông trong 90_Sandbox, đặt lệch ra ngoài (gốc khu ở x = +60, ngoài căn phòng 10 m quanh gốc tọa độ).
    /// Sàn phẳng 56 × 44 m, 40 NPC: 2 vòng bốc hàng (10), 3 nhóm nói chuyện (10), 1 hàng chờ lĩnh (6), 4 người ngồi, 5 người
    /// đi dạo (3 trong sân, 2 đi xuyên lối hẹp), 3 người tựa lan can, 2 người đứng (nhân viên bàn và lính gác). Vật cản:
    /// vài đống thùng và một lối hẹp 1,2 m dài 10 m (kiểm việc nhường đường với người chơi và giữa NPC).
    /// Tọa độ trong hàm là cục bộ của CrowdZone; test tìm điểm đo theo tên trong ViewPoints (View_*, Lane_*).
    /// </summary>
    public static class SandboxCrowd
    {
        public const string ZoneName = "CrowdZone";
        public static readonly Vector3 Origin = new Vector3(60f, 0f, 0f);

        public static void Build(Transform parent)
        {
            var zone = BuilderUtils.Group(ZoneName, parent).transform;
            zone.localPosition = Origin;

            var floor = AssetFactory.Lit("Floor", new Color(0.45f, 0.4f, 0.34f));
            var wall = AssetFactory.Lit("Wall", new Color(0.6f, 0.57f, 0.5f));
            var wood = AssetFactory.Lit("Wood", new Color(0.4f, 0.27f, 0.16f), 0.15f);
            BuilderUtils.Block("CrowdGround", zone, new Vector3(0f, -0.05f, 0f), new Vector3(56f, 0.1f, 44f), floor);

            BuildProps(zone, wall, wood);

            var crowd = CrowdBuilder.Root(zone, "Crowd");

            // 2 vòng bốc hàng: làn đi z = 13,2, làn về z = 11,8 (cách nhau 1,4 m).
            CrowdBuilder.AddLoop(crowd, "Loop1", new[] { new Vector3(-19f, 0f, 13.2f), new Vector3(-12f, 0f, 13.2f), new Vector3(-5f, 0f, 13.2f) },
                new[] { new Vector3(-12f, 0f, 11.8f) }, 5, 100);
            CrowdBuilder.AddLoop(crowd, "Loop2", new[] { new Vector3(3f, 0f, 13.2f), new Vector3(10.5f, 0f, 13.2f), new Vector3(18f, 0f, 13.2f) },
                new[] { new Vector3(10.5f, 0f, 11.8f) }, 5, 200);

            // 3 nhóm nói chuyện.
            CrowdBuilder.AddGroup(crowd, "Talk1", new Vector3(-14f, 0f, 5f), 3, 0.8f, 20f, 301);
            CrowdBuilder.AddGroup(crowd, "Talk2", new Vector3(-5f, 0f, -3f), 4, 0.95f, 45f, 302);
            CrowdBuilder.AddGroup(crowd, "Talk3", new Vector3(9f, 0f, -3f), 3, 0.8f, 100f, 303, optional: true);

            // Hàng chờ lĩnh 6 người trước bàn phát ở x = 22,2; tự tiến mỗi 7 giây, người xong đi vòng về đuôi.
            CrowdBuilder.AddQueue(crowd, "Pay", new Vector3(21f, 0f, -6f), Vector3.left, 6, 1f, new Vector3(21f, 0f, -9f),
                new[] { new Vector3(14f, 0f, -9f) }, 400, 7f);
            CrowdBuilder.AddStand(crowd, new Vector3(23.6f, 0f, -6f), 270f, 410);
            CrowdBuilder.AddStand(crowd, new Vector3(13f, 0f, -4f), 90f, 411, optional: true);

            // 4 người ngồi trên 2 ghế băng.
            CrowdBuilder.AddSit(crowd, new Vector3(-19.4f, 0f, -13f), 0f, 500);
            CrowdBuilder.AddSit(crowd, new Vector3(-18.6f, 0f, -13f), 0f, 501, read: true);
            CrowdBuilder.AddSit(crowd, new Vector3(-14.4f, 0f, -16.5f), 0f, 502, standUp: true, startDelay: 3f);
            CrowdBuilder.AddSit(crowd, new Vector3(-13.6f, 0f, -16.5f), 0f, 503, read: true);

            // 3 người đi dạo trong sân (các đoạn đi cắt nhóm Talk2 để thử vòng qua người đứng yên).
            CrowdBuilder.AddWander(crowd, "Wander", new[]
            {
                new Vector3(-3f, 0f, 5f), new Vector3(3f, 0f, 6.5f), new Vector3(5.5f, 0f, -0.5f), new Vector3(0f, 0f, -6.5f), new Vector3(-8f, 0f, 0.5f),
            }, 3, 600);
            // 2 người đi qua lối hẹp, ngược chiều nhau: gặp nhau giữa lối thì một người phải nhường.
            var laneWalkers = CrowdBuilder.AddWander(crowd, "Lane", new[] { new Vector3(-6f, 0f, -13f), new Vector3(10f, 0f, -13f) }, 2, 700);
            laneWalkers[0].gameObject.name = "LaneWalker_0";
            laneWalkers[1].gameObject.name = "LaneWalker_1";

            // 3 người tựa lan can phía bắc.
            for (int i = 0; i < 3; i++)
                CrowdBuilder.AddStand(crowd, new Vector3(-3f + 3.2f * i, 0f, 16.3f), 0f, 800 + i, lean: true, optional: i > 0);

            CrowdBuilder.AddLod(crowd);
            BuildViewPoints(zone);
        }

        static void BuildProps(Transform zone, Material wall, Material wood)
        {
            var props = BuilderUtils.Group("Obstacles", zone).transform;
            // Lối hẹp: hai tường song song, rộng 1,2 m, dài 10 m.
            BuilderUtils.Block("Lane_N", props, new Vector3(2f, 1.2f, -12.25f), new Vector3(10f, 2.4f, 0.3f), wall);
            BuilderUtils.Block("Lane_S", props, new Vector3(2f, 1.2f, -13.75f), new Vector3(10f, 2.4f, 0.3f), wall);
            // Đống thùng nằm ngoài các đoạn đi của NPC.
            BuilderUtils.Block("Crates_A", props, new Vector3(-10f, 0.6f, -8f), new Vector3(1.4f, 1.2f, 1.2f), wood);
            BuilderUtils.Block("Crates_B", props, new Vector3(12f, 0.6f, 2f), new Vector3(2f, 1.2f, 1.2f), wood);
            BuilderUtils.Block("Crates_C", props, new Vector3(10.5f, 0.6f, 16.4f), new Vector3(2.4f, 1.2f, 1.4f), wood);
            BuilderUtils.Block("Crates_D", props, new Vector3(-22f, 0.6f, 4f), new Vector3(1.2f, 1.2f, 2f), wood);
            // Bàn phát lương, lan can, hai ghế băng.
            BuilderUtils.Block("PayTable", props, new Vector3(22.4f, 0.45f, -6f), new Vector3(0.9f, 0.9f, 2.2f), wood);
            BuilderUtils.Block("Rail", props, new Vector3(0f, 0.52f, 17.4f), new Vector3(12f, 0.08f, 0.1f), wood);
            BuilderUtils.Block("Rail_Post0", props, new Vector3(-6f, 0.26f, 17.4f), new Vector3(0.1f, 0.52f, 0.1f), wood);
            BuilderUtils.Block("Rail_Post1", props, new Vector3(0f, 0.26f, 17.4f), new Vector3(0.1f, 0.52f, 0.1f), wood);
            BuilderUtils.Block("Rail_Post2", props, new Vector3(6f, 0.26f, 17.4f), new Vector3(0.1f, 0.52f, 0.1f), wood);
            BuilderUtils.Block("Bench_1", props, new Vector3(-19f, 0.225f, -13f), new Vector3(1.8f, 0.45f, 0.45f), wood);
            BuilderUtils.Block("Bench_2", props, new Vector3(-14f, 0.225f, -16.5f), new Vector3(1.8f, 0.45f, 0.45f), wood);
        }

        /// <summary>Điểm đo cho test: View_* (chân đứng + hướng nhìn), Lane_* (hai đầu lối hẹp).</summary>
        static void BuildViewPoints(Transform zone)
        {
            var g = BuilderUtils.Group("ViewPoints", zone).transform;
            View(g, "View_Overview", new Vector3(-27f, 0f, -8f), new Vector3(8f, 1.2f, 3f));
            View(g, "View_Sit", new Vector3(-25f, 0f, -13f), new Vector3(-19f, 0.9f, -13f));
            View(g, "View_Close", new Vector3(-6f, 0f, 3f), new Vector3(-9f, 1.3f, 12.5f));
            View(g, "View_Queue", new Vector3(12f, 0f, -12f), new Vector3(19f, 1.3f, -6.5f));
            View(g, "Lane_West", new Vector3(-8f, 0f, -13f), new Vector3(10f, 1.62f, -13f));
            View(g, "Lane_East", new Vector3(12f, 0f, -13f), new Vector3(-6f, 1.62f, -13f));
        }

        static void View(Transform parent, string name, Vector3 feet, Vector3 lookAt)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = feet;
            t.rotation = Quaternion.LookRotation(lookAt - (feet + Vector3.up * 1.62f));
        }

        /// <summary>
        /// Dựng lại riêng 90_Sandbox (không đụng 5 cảnh còn lại):
        /// ./Tools/unity.sh -nographics -quit -executeMethod HaiBanTay.Editor.SceneBuilders.SandboxCrowd.RebuildSandbox
        /// </summary>
        public static void RebuildSandbox()
        {
            int errors = 0;
            void Count(string condition, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    errors++;
            }
            Application.logMessageReceived += Count;
            try
            {
                SandboxSceneBuilder.Build();
                AssetDatabase.SaveAssets();
                Debug.Log($"SandboxCrowd: đã dựng lại 90_Sandbox, {errors} lỗi.");
            }
            catch (Exception e)
            {
                Debug.LogError("SandboxCrowd thất bại: " + e);
                errors++;
            }
            finally
            {
                Application.logMessageReceived -= Count;
            }
            if (Application.isBatchMode)
                EditorApplication.Exit(errors == 0 ? 0 : 1);
        }
    }
}
