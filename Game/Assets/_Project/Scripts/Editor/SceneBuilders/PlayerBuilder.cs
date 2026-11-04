using System.Collections.Generic;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Editor.Import;
using HaiBanTay.Interaction;
using HaiBanTay.Player;
using HaiBanTay.Ship;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>Dựng người chơi góc nhìn thứ nhất: thân (CharacterController), đầu, camera Cinemachine có noise,
    /// đôi tay theo tư thế, mesh giữ tỉ lệ và gốc cổ tay, xẻng, điểm giữ đồ, hơi thở.</summary>
    public static class PlayerBuilder
    {
        const string NoiseProfilePath = "Packages/com.unity.cinemachine/Presets/Noise/Handheld_normal_mild.asset";

        public static PlayerRig Spawn(Camera mainCam, Vector3 position, float yaw)
        {
            var player = new GameObject("Player") { tag = "Player" };
            player.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var cc = player.AddComponent<CharacterController>();
            // Giữ capsule đi qua trần thấp ở thang hầm.
            cc.height = 1.75f;
            cc.radius = 0.28f;
            // Nâng tâm 5 cm để mắt nằm trong capsule truy vấn PhysX; không tăng chiều cao va chạm.
            cc.center = new Vector3(0f, 0.925f, 0f);
            cc.stepOffset = 0.3f;
            cc.slopeLimit = 50f;

            var head = new GameObject("Head").transform;
            head.SetParent(player.transform, false);
            head.localPosition = new Vector3(0f, 1.62f, 0f);

            var vcamGo = new GameObject("PlayerVCam");
            vcamGo.transform.SetParent(head, false);
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            var lens = vcam.Lens;
            lens.FieldOfView = 65f;
            lens.NearClipPlane = 0.03f;
            lens.FarClipPlane = 600f;
            vcam.Lens = lens;
            var noise = vcamGo.AddComponent<CinemachineBasicMultiChannelPerlin>();
            noise.NoiseProfile = AssetDatabase.LoadAssetAtPath<NoiseSettings>(NoiseProfilePath);
            noise.AmplitudeGain = 0.25f;
            noise.FrequencyGain = 0.6f;

            mainCam.transform.SetPositionAndRotation(head.position, head.rotation);

            var motor = player.AddComponent<FirstPersonMotor>();
            motor.head = head;
            var look = player.AddComponent<LookController>();
            look.head = head;
            var hands = BuildHands(mainCam.transform);
            var interactor = player.AddComponent<Interactor>();
            interactor.cam = mainCam;
            interactor.motor = motor;
            interactor.hands = hands;
            interactor.holdOneHand = Point("HoldOneHand", mainCam.transform, new Vector3(0.2f, -0.25f, 0.5f));
            interactor.holdTwoHands = Point("HoldTwoHands", mainCam.transform, new Vector3(0f, -0.32f, 0.55f));
            // Bao nằm dọc vai phải, trục dài ngang mặt đất; điểm cha giữ độ nghiêng StairsBalance.
            interactor.holdRightShoulder = Point("HoldRightShoulder", interactor.holdTwoHands,
                new Vector3(0.50f, 0.36f, 0.23f));
            interactor.holdRightShoulder.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var rig = player.AddComponent<PlayerRig>();
            rig.motor = motor;
            rig.look = look;
            rig.interactor = interactor;
            rig.hands = hands;
            rig.head = head;
            rig.cam = mainCam;
            rig.vcam = vcam;
            rig.noise = noise;
            rig.body = PlayerBodyBuilder.Build(rig);
            rig.breath = BuildBreath(mainCam.transform);
            // Fill tay: không bóng, tầm 1,3 m (chỉ chạm tay, xu, vật trong tầm tay), tắt sẵn; PlayerRig bật trong khung hai bàn tay.
            rig.handsFill = BuilderUtils.PointLight("HandsFill", mainCam.transform, new Vector3(0f, 0.2f, 0.1f),
                new Color(1f, 0.93f, 0.82f), 1.6f, 1.3f, true);
            BuilderUtils.SetLightLayer(rig.handsFill, uint.MaxValue);
            rig.handsFill.gameObject.SetActive(false);
            player.AddComponent<BodyState>();
            player.AddComponent<SitController>();
            // Tay, đồ cầm, tay NPC gắn camera đổi lớp ánh sáng theo vùng đang đứng (trong khoang / ngoài trời).
            var region = player.AddComponent<RegionLighting>();
            region.root = mainCam.transform;
            region.interactor = interactor;
            region.Apply(ShipRegion.Exterior);
            return rig;
        }

        static Transform Point(string name, Transform parent, Vector3 local)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            return t;
        }

        static HandsRig BuildHands(Transform cam)
        {
            var skin = AssetFactory.Lit("Skin", new Color(0.78f, 0.6f, 0.47f), 0.35f);
            var cloth = AssetFactory.Lit("Sleeve", new Color(0.42f, 0.38f, 0.3f), 0.1f);

            var root = new GameObject("Hands");
            root.transform.SetParent(cam, false);
            var rig = root.AddComponent<HandsRig>();
            rig.left = Point("Left", root.transform, rig.targets[0].leftPos);
            rig.right = Point("Right", root.transform, rig.targets[0].rightPos);

            var skinRenderers = new List<Renderer>();
            var poses = (HandPose[])System.Enum.GetValues(typeof(HandPose));
            rig.leftPoseMeshes = new GameObject[poses.Length];
            rig.rightPoseMeshes = new GameObject[poses.Length];
            foreach (var pose in poses)
            {
                rig.leftPoseMeshes[(int)pose] = HandMesh("L", pose, rig.left, skin, -1f);
                rig.rightPoseMeshes[(int)pose] = HandMesh("R", pose, rig.right, skin, 1f);
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("Skin") || r.name == "Palm" || r.name == "Fingers" || r.name == "Thumb")
                    skinRenderers.Add(r);
            rig.skinRenderers = skinRenderers.ToArray();

            // FBX chứa áo nối cổ tay, mỗi pose đổi cả da/áo. Fallback vẫn có áo.
            if (AssetDatabase.LoadAssetAtPath<GameObject>(HandsImporter.Folder + "/Hand_L_Open.fbx") != null)
            {
                rig.skinColor = Color.white; // Atlas Rocketbox đã chứa màu da.
                // Bàn tay có xương ngón (tắt sẵn): cảnh nào cần cầm đồ bằng ngón thật thì bật bằng HandsRig.SetFingers.
                var skinAsset = AssetDatabase.LoadAssetAtPath<Material>(HandsImporter.SkinPath);
                var sleeveAsset = AssetDatabase.LoadAssetAtPath<Material>(HandsImporter.SleevePath);
                if (HandRigBuilder.Available && skinAsset != null)
                {
                    rig.leftFingers = HandRigBuilder.Build(rig.left, "L", skinAsset, sleeveAsset);
                    rig.rightFingers = HandRigBuilder.Build(rig.right, "R", skinAsset, sleeveAsset);
                    rig.riggedLeft = rig.leftFingers.gameObject;
                    rig.riggedRight = rig.rightFingers.gameObject;
                    rig.riggedRenderers = new Renderer[]
                    {
                        rig.riggedLeft.GetComponentInChildren<SkinnedMeshRenderer>(true), rig.riggedRight.GetComponentInChildren<SkinnedMeshRenderer>(true)
                    };
                }
            }
            else
            {
                Sleeve(rig.left, cloth);
                Sleeve(rig.right, cloth);
            }

            // Xẻng than (B2.1).
            var wood = AssetFactory.Lit("Wood", new Color(0.4f, 0.27f, 0.16f), 0.15f);
            var iron = AssetFactory.Lit("Iron", new Color(0.22f, 0.22f, 0.23f), 0.4f, 0.6f);
            var coal = AssetFactory.Lit("LooseCoal", new Color(0.045f, 0.05f, 0.055f), 0.08f);
            var shovel = ChoreProps.Shovel(root.transform, new Vector3(0.05f, -0.33f, 0.55f), new Vector3(70f, 0f, 0f));
            rig.shovelProp = shovel;
            rig.shovelLoad = BuilderUtils.Group("CoalLoad", shovel.transform);
            rig.shovelLoad.transform.localPosition = new Vector3(0f, 0.52f, -0.018f);
            rig.shovelLoad.transform.localScale = new Vector3(0.18f, 0.18f, 0.05f);
            // Than vụn nằm trong lòng xẻng, không còn một viên gạch nâu phẳng.
            var lumpMesh = AssetFactory.CoalLumpMesh();
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                {
                    var lump = BuilderUtils.Group("Lump", rig.shovelLoad.transform);
                    lump.transform.localPosition = new Vector3((col - 1) * 0.28f, (row - 1) * 0.28f, 0f);
                    lump.transform.localScale = new Vector3(0.38f, 0.38f, 0.9f);
                    lump.transform.localRotation = Quaternion.Euler(0f, 0f, (row * 3 + col) * 37f);
                    lump.AddComponent<MeshFilter>().sharedMesh = lumpMesh;
                    lump.AddComponent<MeshRenderer>().sharedMaterial = coal;
                }
            rig.shovelLoad.SetActive(false);
            ChoreProps.SeatShovelLoad(shovel.transform, rig.shovelLoad.transform);
            shovel.SetActive(false);

            // Bàn chải (brush17): con của bàn tay phải, cán nằm trong nắm tay; trạm cọ đặt nó lên mặt cọ khi chà (HandsRig.DetachBrush).
            rig.brushProp = ChoreProps.Brush(rig.right);
            ChoreProps.SeatInRightFist(rig.brushProp.transform, HaiBanTay.Tasks.BrushShape.Short.GripAt);
            rig.brushProp.SetActive(false);
            rig.longBrushProp = ChoreProps.LongBrush(rig.right);
            ChoreProps.SeatInRightFist(rig.longBrushProp.transform, LongBrushCarryGrip);
            rig.longBrushProp.SetActive(false);

            // Cào lò và chổi (M9): cùng tư thế cầm cán dài như xẻng.
            var rake = new GameObject("RakeProp");
            rake.transform.SetParent(root.transform, false);
            rake.transform.localPosition = new Vector3(0.05f, -0.33f, 0.55f);
            rake.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Handle", rake.transform, new Vector3(0f, 0f, 0f), new Vector3(0.03f, 0.55f, 0.03f), iron, false, false);
            BuilderUtils.Block("Blade", rake.transform, new Vector3(0f, 0.55f, 0.04f), new Vector3(0.2f, 0.02f, 0.1f), iron, false, false);
            rig.rakeProp = rake;
            rake.SetActive(false);

            var bristle = AssetFactory.Lit("Bristle", new Color(0.48f, 0.38f, 0.22f), 0.05f);
            var broom = new GameObject("BroomProp");
            broom.transform.SetParent(root.transform, false);
            broom.transform.localPosition = new Vector3(0.05f, -0.4f, 0.55f);
            broom.transform.localRotation = Quaternion.Euler(60f, 0f, 0f);
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Handle", broom.transform, new Vector3(0f, 0f, 0f), new Vector3(0.03f, 0.5f, 0.03f), wood, false, false);
            BuilderUtils.Block("Bristles", broom.transform, new Vector3(0f, 0.56f, 0f), new Vector3(0.3f, 0.14f, 0.05f), bristle, false, false);
            rig.broomProp = broom;
            broom.SetActive(false);

            foreach (var c in root.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(c);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return rig;
        }

        /// <summary>Cầm bàn chải cán dài đi lại: nắm cách mũi lông chừng này (m), mũi lông gần sàn khi tay thả thấp bên hông.</summary>
        const float LongBrushCarryGrip = 1.12f;

        static GameObject HandMesh(string side, HandPose pose, Transform parent, Material skin, float sign)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{HandsImporter.Folder}/Hand_{side}_{pose}.fbx");
            GameObject go;
            if (model != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
                go.name = $"Hand_{side}_{pose}";
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
            }
            else
            {
                go = new GameObject($"Hand_{side}_{pose}");
                go.transform.SetParent(parent, false);
                bool curled = pose == HandPose.Grip || pose == HandPose.Carry || pose == HandPose.Shovel;
                BuilderUtils.Block("Palm", go.transform, Vector3.zero, new Vector3(0.085f, 0.028f, 0.1f), skin, false, false);
                BuilderUtils.Block("Fingers", go.transform, new Vector3(0f, curled ? -0.03f : 0f, 0.075f),
                    new Vector3(0.08f, 0.022f, 0.06f), skin, false, false, new Vector3(curled ? 80f : 0f, 0f, 0f));
                BuilderUtils.Block("Thumb", go.transform, new Vector3(-0.05f * sign, 0f, 0.02f),
                    new Vector3(0.025f, 0.022f, 0.06f), skin, false, false, new Vector3(0f, 35f * sign, 0f));
            }
            go.SetActive(pose == HandPose.Lowered);
            return go;
        }

        static void Sleeve(Transform hand, Material cloth)
        {
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Sleeve", hand, new Vector3(0f, 0f, -0.16f), new Vector3(0.1f, 0.12f, 0.1f),
                cloth, false, false, new Vector3(90f, 0f, 0f));
        }

        static ParticleSystem BuildBreath(Transform cam)
        {
            var go = new GameObject("Breath");
            go.transform.SetParent(cam, false);
            go.transform.localPosition = new Vector3(0f, -0.12f, 0.2f);
            go.transform.localRotation = Quaternion.Euler(-10f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = 1.4f;
            main.startSpeed = 0.35f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.startColor = new Color(1f, 1f, 1f, 0.18f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8, 12, 1, 3.2f) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.02f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.25f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2f));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat")
                ?? AssetFactory.Transparent("Breath", new Color(1f, 1f, 1f, 0.35f));
            return ps;
        }
    }
}
