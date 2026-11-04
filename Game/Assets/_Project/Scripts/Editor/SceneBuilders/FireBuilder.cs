using System.Collections.Generic;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Ship;
using UnityEngine;
using UnityEngine.Rendering;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>
    /// Thân bếp gang rỗng, than 3D và lửa hạt URP cộng sáng bên trong.
    /// Không thêm Light: dùng đèn lửa trong ngân sách hiện có hoặc đèn bake.
    /// </summary>
    public static class FireBuilder
    {
        public enum Shape { Box, Ring }

        static ParticleSystem NewSystem(string name, Transform parent, Vector3 pos, Vector3 euler)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.prewarm = true;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return ps;
        }

        /// <summary>
        /// Ngọn lửa bay lên từ một hộp (Box: extent × depth) hoặc một vành tròn (Ring: bán kính extent). pos là gốc ngọn lửa,
        /// flame là cỡ một ngọn (rộng, cao), life giây, rise m/s.
        /// </summary>
        public static ParticleSystem Flames(string name, Transform parent, Vector3 pos, Shape shape, float extent, float depth,
            float rate, Vector2 flame, float life, float rise)
        {
            var ps = NewSystem(name, parent, pos, new Vector3(-90f, 0f, 0f));
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life * 1.15f);
            main.startSpeed = 0f;
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(flame.x * 0.75f, flame.x * 1.2f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(flame.y * 0.7f, flame.y * 1.25f);
            main.startSizeZ = 1f;
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            main.startColor = Color.white;
            main.maxParticles = 48;
            var emission = ps.emission;
            emission.rateOverTime = rate;
            var sh = ps.shape;
            if (shape == Shape.Box)
            {
                sh.shapeType = ParticleSystemShapeType.Box;
                sh.scale = new Vector3(extent, depth, 0.01f);
            }
            else
            {
                sh.shapeType = ParticleSystemShapeType.Circle;
                sh.radius = extent;
                sh.radiusThickness = 0.1f;
                sh.arc = 360f;
            }
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
            velocity.y = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
            velocity.z = new ParticleSystem.MinMaxCurve(rise * 0.75f, rise * 1.2f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.05f;
            noise.frequency = 2.5f;
            noise.scrollSpeed = 1.6f;
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.85f, 0.55f), 0.5f), new GradientColorKey(new Color(1f, 0.5f, 0.2f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.85f, 0.6f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.6f), new Keyframe(0.18f, 1f), new Keyframe(1f, 0.4f)));
            var sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = 4;
            sheet.numTilesY = 4;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.cycleCount = 1;
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f);
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = AssetFactory.FlameMaterial();
            r.renderMode = ParticleSystemRenderMode.VerticalBillboard;
            // Gốc ngọn lửa ở chân hình, không ở tâm.
            r.pivot = new Vector3(0f, 0.5f, 0f);
            return ps;
        }

        /// <summary>
        /// Thân gang rỗng; mỗi nắp (lids) có buồng đốt riêng ngay dưới: cửa gió thật, ghi lò, khay tro, than hồng và ngọn lửa
        /// của riêng ô đó. Trả về một FireFlicker cho mỗi ô, theo thứ tự lids.
        /// </summary>
        public static FireFlicker[] CastRange(Transform root, float width, float depth, float[] lids)
        {
            var iron = AssetFactory.RangeIron();
            var edge = AssetFactory.Lit("RangeEdge", new Color(0.18f, 0.18f, 0.17f), 0.35f, 0.7f);
            var brass = AssetFactory.Lit("RangeBrass", new Color(0.50f, 0.35f, 0.17f), 0.32f, 0.8f);
            var coal = AssetFactory.Embers("RangeCoal3D", new Color(1.1f, 0.3f, 0.04f), 0.018f);
            var coalMesh = AssetFactory.CoalLumpMesh();
            var hot = AssetFactory.Emissive("RangeWarmLid", new Color(0.12f, 0.10f, 0.09f), new Color(0.24f, 0.025f, 0.004f), false, 0.3f);
            float front = depth * 0.5f;
            var glows = new List<FireFlicker.Glow>[lids.Length];
            for (int i = 0; i < glows.Length; i++) glows[i] = new List<FireFlicker.Glow>();
            // Các tấm có bề dày thật, không có hộp kín che buồng đốt.
            BuilderUtils.Block("HearthFloor", root, new Vector3(0f, 0.2f, 0f), new Vector3(width, 0.05f, depth), iron, true, false);
            BuilderUtils.Block("BackWall", root, new Vector3(0f, 0.54f, -front + 0.025f), new Vector3(width, 0.64f, 0.05f), iron, true, false);
            foreach (float side in new[] { -1f, 1f })
            {
                BuilderUtils.Block("SideWall", root, new Vector3(side * (width * 0.5f - 0.025f), 0.54f, 0f), new Vector3(0.05f, 0.64f, depth), iron, true, false);
                foreach (float z in new[] { -front + 0.07f, front - 0.07f })
                    BuilderUtils.Block("Foot", root, new Vector3(side * (width * 0.5f - 0.08f), 0.09f, z), new Vector3(0.12f, 0.18f, 0.12f), iron, true, false);
            }
            // Mặt bếp có ô khoét vuông dưới từng lưới (lửa bốc thẳng lên qua lưới); vành tròn che góc ô khoét.
            const float Hole = 0.22f;
            var grate = AssetFactory.RangeGrate();
            {
                var holes = (float[])lids.Clone();
                System.Array.Sort(holes);
                BuilderUtils.Block("HotplateFront", root, new Vector3(0f, 0.875f, (Hole + front) * 0.5f), new Vector3(width, 0.05f, front - Hole), iron, true, false);
                BuilderUtils.Block("HotplateBack", root, new Vector3(0f, 0.875f, -(Hole + front) * 0.5f), new Vector3(width, 0.05f, front - Hole), iron, true, false);
                float from = -width * 0.5f;
                for (int i = 0; i <= holes.Length; i++)
                {
                    float to = i < holes.Length ? holes[i] - Hole : width * 0.5f;
                    if (to > from + 0.005f)
                        BuilderUtils.Block("HotplateBar", root, new Vector3((from + to) * 0.5f, 0.875f, 0f), new Vector3(to - from, 0.05f, Hole * 2f), iron, true, false);
                    if (i < holes.Length) from = holes[i] + Hole;
                }
            }
            BuilderUtils.Block("FrontHeader", root, new Vector3(0f, 0.835f, front - 0.025f), new Vector3(width, 0.03f, 0.05f), iron, true, false);
            BuilderUtils.Block("FrontSill", root, new Vector3(0f, 0.39f, front - 0.025f), new Vector3(width, 0.03f, 0.05f), iron, true, false);
            // Cửa nạp rộng 0,58 m dưới từng nắp; các tấm trước nối giữa các cửa và tới mép thân, hốc sâu hơn 25 cm.
            float left = -width * 0.5f, right = width * 0.5f;
            var sorted = (float[])lids.Clone();
            System.Array.Sort(sorted);
            void Panels(string name, float y, float height, float half)
            {
                float from = left;
                for (int i = 0; i <= sorted.Length; i++)
                {
                    float to = i < sorted.Length ? sorted[i] - half : right;
                    if (to > from + 0.005f)
                        BuilderUtils.Block(name, root, new Vector3((from + to) * 0.5f, y, front - 0.025f), new Vector3(to - from, height, 0.05f), iron, true, false);
                    if (i < sorted.Length) from = sorted[i] + half;
                }
            }
            Panels("FrontPanel", 0.6125f, 0.415f, 0.29f);
            Panels("AshFrontPanel", 0.3f, 0.15f, 0.26f);
            // Nẹp và đinh tán nằm trong sai số thân 2 cm; không có collider trang trí lấn vào lối đi.
            foreach (float x in new[] { left + 0.035f, right - 0.035f })
            {
                BuilderUtils.Block("CornerStrap", root, new Vector3(x, 0.53f, front + 0.005f), new Vector3(0.038f, 0.60f, 0.014f), edge, true, false);
                foreach (float y in new[] { 0.25f, 0.40f, 0.60f, 0.80f })
                    BuilderUtils.Prim(PrimitiveType.Sphere, "Rivet", root, new Vector3(x, y, front + 0.011f), new Vector3(0.014f, 0.014f, 0.014f), edge, true, false);
            }
            BuilderUtils.Block("PlinthTrim", root, new Vector3(0f, 0.205f, front + 0.005f), new Vector3(width, 0.028f, 0.014f), edge, true, false);
            var cellFlames = new ParticleSystem[lids.Length][];
            for (int ci = 0; ci < lids.Length; ci++)
            {
                float hearthX = lids[ci];
                var door = BuilderUtils.Group("FireDoor", root).transform;
                door.localPosition = new Vector3(hearthX, 0.6125f, front - 0.0175f);
                // Cửa đóng phẳng với thân; ô cửa gió không phải một texture đen giả hốc.
                foreach (var plate in new[] { new Vector2(-0.185f, 0.045f), new Vector2(0.1925f, 0.03f) })
                    BuilderUtils.Block("DoorPlate", door, new Vector3(0f, plate.x, 0f), new Vector3(0.58f, plate.y, 0.035f), edge, true, false);
                foreach (float x in new[] { -0.265f, 0.265f })
                    BuilderUtils.Block("DoorPlate", door, new Vector3(x, 0.0075f, 0f), new Vector3(0.05f, 0.34f, 0.035f), edge, true, false);
                foreach (float x in new[] { -0.14f, 0f, 0.14f })
                    BuilderUtils.Block("DraftBar", door, new Vector3(x, 0.0075f, -0.004f), new Vector3(0.016f, 0.34f, 0.027f), iron, true, false);
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Hinge", door, new Vector3(-0.282f, 0f, -0.005f), new Vector3(0.026f, 0.2f, 0.026f), edge, true, false);
                var handle = BuilderUtils.Group("Handle", door).transform;
                BuilderUtils.Block("Latch", handle, new Vector3(0.20f, -0.185f, 0.035f), new Vector3(0.10f, 0.025f, 0.06f), iron, true, false);
                // Ghi lò ngang và khay tro bên dưới; nhìn thấy chiều sâu qua cửa gió.
                // Ghi lò từ cửa nạp tới tâm ô: than đổ qua cửa rơi lên ghi ngay dưới lưới.
                float grateLength = front + 0.22f, grateCenter = (front - 0.04f - 0.26f) * 0.5f;
                for (int i = 0; i < 9; i++)
                    BuilderUtils.Block("Grate", root, new Vector3(hearthX - 0.24f + i * 0.06f, 0.405f, grateCenter), new Vector3(0.025f, 0.025f, grateLength), edge, true, false);
                BuilderUtils.Block("AshTray", root, new Vector3(hearthX, 0.25f, front - 0.3f), new Vector3(0.51f, 0.035f, 0.42f), iron, true, false);
                BuilderUtils.Block("Ash", root, new Vector3(hearthX, 0.277f, front - 0.3f), new Vector3(0.45f, 0.018f, 0.36f), AssetFactory.Lit("RangeAsh", new Color(0.2f, 0.18f, 0.16f)), true, false);
                var ashDoor = BuilderUtils.Group("AshDoor", root).transform;
                ashDoor.localPosition = new Vector3(hearthX, 0.30f, front - 0.012f);
                var ashHinge = BuilderUtils.Group("AshHinge", ashDoor).transform;
                ashHinge.localPosition = new Vector3(0f, 0.075f, 0f);
                BuilderUtils.Block("AshPlate", ashHinge, new Vector3(0f, -0.075f, 0f), new Vector3(0.52f, 0.15f, 0.024f), edge, false, false);
                BuilderUtils.Block("AshHandle", handle, new Vector3(0f, -0.3125f, 0.025f), new Vector3(0.14f, 0.025f, 0.045f), iron, true, false);
                for (int row = 0; row < 4; row++)
                    for (int col = 0; col < 6; col++)
                    {
                        float a = (row * 6 + col) * 2.39996f;
                        float size = 0.105f + 0.023f * Mathf.Sin(a);
                        var lump = BuilderUtils.Prim(PrimitiveType.Cube, "Ember", root,
                            new Vector3(hearthX - 0.205f + col * 0.082f, 0.45f + 0.01f * Mathf.Sin(a), 0.17f - row * 0.082f),
                            new Vector3(size, size * 0.75f, size * 1.1f), coal, true, false, new Vector3(17f * col, 31f * row, 83f + 11f * col));
                        lump.GetComponent<MeshFilter>().sharedMesh = coalMesh;
                        // Kê đáy bounds của mesh sau xoay đúng mặt trên ghi, không để than chìm vào thanh sắt.
                        var coalBounds = HaiBanTay.Editor.Import.ArtImporter.TransformBounds(lump.GetComponent<MeshFilter>().sharedMesh.bounds,
                            root.worldToLocalMatrix * lump.transform.localToWorldMatrix);
                        if (coalBounds.size.y > 0.11f)
                        {
                            lump.transform.localScale *= 0.11f / coalBounds.size.y;
                            coalBounds = HaiBanTay.Editor.Import.ArtImporter.TransformBounds(lump.GetComponent<MeshFilter>().sharedMesh.bounds,
                                root.worldToLocalMatrix * lump.transform.localToWorldMatrix);
                        }
                        lump.transform.localPosition += Vector3.up * (0.418f - coalBounds.min.y);
                        glows[ci].Add(new FireFlicker.Glow { renderer = lump.GetComponent<Renderer>(), strength = 2.4f });
                    }
                var collar = ChoreProps.Collar(root, "StoveRing", new Vector3(hearthX, 0.8995f, 0f), Hole - 0.005f, 0.31f, 0.0045f, hot);
                glows[ci].Add(new FireFlicker.Glow { renderer = collar.GetComponent<Renderer>(), strength = 0.19f });
                // Lưới nướng thép đan thoi: xuyên thấu, nồi đặt trên lưới; collider đĩa mỏng làm mặt đỡ nồi.
                var lid = BuilderUtils.Prim(PrimitiveType.Cylinder, "StoveLid", root, new Vector3(hearthX, 0.904f, 0f), new Vector3(0.43f, 0.004f, 0.43f), grate, true, false);
                // Cylinder mặc định có CapsuleCollider: dùng mesh tĩnh để mặt tiếp xúc đúng nắp gang mỏng.
                lid.AddComponent<MeshCollider>().sharedMesh = lid.GetComponent<MeshFilter>().sharedMesh;
                BuilderUtils.Block("LidLiftSlot", root, new Vector3(hearthX, 0.9075f, 0f), new Vector3(0.055f, 0.001f, 0.012f), iron, true, false);
                // Lửa bốc cao từ than hồng, xuyên qua lưới tới ngang đáy nồi.
                cellFlames[ci] = new[] {
                    Flames("Flames", root, new Vector3(hearthX, 0.47f, 0.02f), Shape.Box, 0.34f, 0.30f, 30f, new Vector2(0.13f, 0.34f), 0.6f, 0.62f),
                    Flames("FlamesLow", root, new Vector3(hearthX, 0.46f, 0.0f), Shape.Box, 0.38f, 0.34f, 20f, new Vector2(0.17f, 0.2f), 0.45f, 0.36f)
                };
            }
            // Viền đúc liền cao 19 mm giữ đáy nồi khi tàu lắc; toàn bộ nằm trong bao thân +2 cm.
            foreach (float z in new[] { -front + 0.009f, front - 0.009f })
                BuilderUtils.Block("SeaRim", root, new Vector3(0f, 0.9095f, z), new Vector3(width, 0.019f, 0.018f), iron, true, true);
            foreach (float x in new[] { left + 0.009f, right - 0.009f })
                BuilderUtils.Block("SeaRim", root, new Vector3(x, 0.9095f, 0f), new Vector3(0.018f, 0.019f, depth - 0.036f), iron, true, true);
            // Thanh đồng quanh mép mặt bếp giữ nồi; thấp hơn đỉnh viền gang, không đổi mặt đỡ nồi.
            foreach (float z in new[] { -front + 0.022f, front - 0.022f })
                BuilderUtils.Prim(PrimitiveType.Cylinder, "PotRail", root, new Vector3(0f, 0.904f, z),
                    new Vector3(0.024f, (width - 0.10f) * 0.5f, 0.024f), brass, true, false, new Vector3(0f, 0f, 90f));
            foreach (float x in new[] { left + 0.022f, right - 0.022f })
                BuilderUtils.Prim(PrimitiveType.Cylinder, "PotRail", root, new Vector3(x, 0.904f, 0f),
                    new Vector3(0.024f, (depth - 0.07f) * 0.5f, 0.024f), brass, true, false, new Vector3(90f, 0f, 0f));
            // Một ống đứng nối từ góc sau nóc tới trần; khớp đai chồng lên ống.
            var flue = BuilderUtils.Group("Chimney", root).transform;
            float flueX = left + 0.18f, flueZ = -front + 0.13f;
            BuilderUtils.Prim(PrimitiveType.Cylinder, "Flue", flue, new Vector3(flueX, 1.74f, flueZ), new Vector3(0.20f, 0.84f, 0.20f), iron, true, false);
            foreach (float y in new[] { 0.92f, 1.4f, 2.582f })
                BuilderUtils.Prim(PrimitiveType.Cylinder, "Joint", flue, new Vector3(flueX, y, flueZ), new Vector3(y > 2.5f ? 0.28f : 0.23f, 0.018f, y > 2.5f ? 0.28f : 0.23f), edge, true, false);
            var result = new FireFlicker[lids.Length];
            for (int ci = 0; ci < lids.Length; ci++)
            {
                var flicker = BuilderUtils.Group("FireFx_" + ci, root).AddComponent<FireFlicker>();
                flicker.glows = glows[ci].ToArray();
                flicker.flames = cellFlames[ci];
                // Bounds cố định bao gồm cả kích cỡ billboard, nhiễu và dịch chuyển tối đa; không che lỗi bằng bounds động rỗng ở EditMode.
                foreach (var ps in flicker.flames)
                {
                    var r = ps.GetComponent<ParticleSystemRenderer>();
                    var world = new Bounds(root.TransformPoint(new Vector3(lids[ci], 0.8f, 0.0f)), new Vector3(0.90f, 0.95f, 0.70f));
                    r.localBounds = HaiBanTay.Editor.Import.ArtImporter.TransformBounds(world, r.transform.worldToLocalMatrix);
                }
                flicker.flickerSpeed = 0.55f + 0.07f * ci;
                result[ci] = flicker;
            }
            var collider = root.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.45f, 0f);
            collider.size = new Vector3(width, 0.9f, depth);
            return result;
        }

        /// <summary>Một hệ hơi mỏng cho mỗi nồi; tối đa 12 hạt, không có đèn hoặc bóng.</summary>
        public static void PotSteam(Transform pot)
        {
            var ps = NewSystem("PotSteam", pot, new Vector3(0f, 0.33f, 0f), Vector3.zero);
            var main = ps.main;
            main.startLifetime = 1.4f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.12f);
            main.startColor = new Color(0.55f, 0.55f, 0.52f, 0.07f);
            main.maxParticles = 12;
            var emission = ps.emission;
            emission.rateOverTime = 3f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.y = 0.16f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 2.2f));
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = AssetFactory.SteamMaterial();
        }
    }
}
