using System;
using System.Collections.Generic;
using System.IO;
using HaiBanTay.Editor.Import;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Npc;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>
    /// API dựng đám đông cho builder cảnh (sandbox, bến Nhà Rồng, cầu cảng Marseille). Mọi vị trí là tọa độ cục bộ của
    /// Transform gốc đám đông (crowd, tạo bằng Root); gốc này không xoay, không co giãn. Dưới gốc có ba nhóm: Npcs, Waypoints
    /// (điểm đi dạng Transform con, nên dựng lại được và đi theo vật cha khi bến dời) và Props (bao hàng mang tay).
    /// Thân NPC là Prefabs/NPC/{vai}.prefab (Rocketbox); chưa có prefab thì khối xám. prefabRole "any" chọn vai theo seed
    /// trong số prefab đám đông hiện có; vai truyện riêng chỉ xuất hiện khi builder chỉ định.
    /// Biến thể màu theo seed (CrowdVariation): slot material nhận diện theo hậu tố tên material Rocketbox, xem TintKindOf.
    /// </summary>
    public static class CrowdBuilder
    {
        public const string AnyRole = "any";

        /// <summary>Cách bê vật của đám đông: trước bụng (mặc định), vác vai (phu bốc hàng) hoặc xách va li bằng tay phải (hành khách).</summary>
        public enum CarryStyle { Belly, Shoulder, Bag }

        // ---------- Gốc, vai, màu ----------

        /// <summary>Gốc đám đông: một Transform với các nhóm con Npcs, Waypoints, Props.</summary>
        public static Transform Root(Transform parent, string name = "Crowd")
        {
            var root = BuilderUtils.Group(name, parent).transform;
            BuilderUtils.Group("Npcs", root);
            BuilderUtils.Group("Waypoints", root);
            BuilderUtils.Group("Props", root);
            return root;
        }

        /// <summary>Các vai đám đông có prefab, trừ vai truyện riêng; thứ tự tên cố định để giữ lựa chọn theo seed.</summary>
        public static string[] AvailableRoles()
        {
            var roles = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { CharacterImporter.PrefabRoot }))
            {
                var role = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
                if (Array.IndexOf(CharacterImporter.StoryRoles, role) < 0) roles.Add(role);
            }
            roles.Sort(StringComparer.Ordinal);
            return roles.ToArray();
        }

        static string ResolveRole(string prefabRole, int seed)
        {
            // "A,B,C": chọn một vai trong danh sách theo seed (đám đông nhiều kiểu người mà không đụng vai chính như Mai, Le).
            if (!string.IsNullOrEmpty(prefabRole) && prefabRole.IndexOf(',') >= 0)
            {
                var list = prefabRole.Split(',');
                return list[(int)(CrowdHash.Mix(seed, 3) % (uint)list.Length)].Trim();
            }
            if (!string.IsNullOrEmpty(prefabRole) && prefabRole != AnyRole)
                return prefabRole;
            var roles = AvailableRoles();
            return roles.Length == 0 ? "Grey" : roles[(int)(CrowdHash.Mix(seed, 3) % (uint)roles.Length)];
        }

        /// <summary>
        /// Slot material có thể đổi màu theo hậu tố tên material của Rocketbox (null: không đổi). Hiện có:
        /// *_Body (áo, quần và da tay chung một texture, đổi nhẹ), *_Cotton (tạp dề, mũ bếp, cuff của Cook và Helper),
        /// *_CapCloth (mũ của Jacques và SoldierB), *_Hair (tóc của Helper, SoldierA, SoldierB).
        /// Không đổi: *_Head, *_ForearmSkin (da), *_PlainDarkLeather (giày, thắt lưng). Quần chưa tách khỏi áo trong
        /// texture Body nên chưa đổi riêng được: cần avatar có quần là material riêng.
        /// </summary>
        public static CrowdTintKind? TintKindOf(string materialName)
        {
            if (string.IsNullOrEmpty(materialName))
                return null;
            string n = materialName.ToLowerInvariant();
            if (n.EndsWith("_capcloth")) return CrowdTintKind.Hat;
            if (n.EndsWith("_cotton")) return CrowdTintKind.Cotton;
            if (n.EndsWith("_hair")) return CrowdTintKind.Hair;
            if (n.EndsWith("_body")) return CrowdTintKind.Clothes;
            return null;
        }

        // ---------- NPC ----------

        /// <summary>
        /// Một NPC đứng tại pos (cục bộ của parent), quay yaw độ. Có sẵn NpcAgent (nhường nhau, nhường người chơi, lệch pha
        /// animation và tốc độ theo seed), collider, điểm bê vật và biến thể màu. Chưa gắn routine.
        /// </summary>
        public static NpcAgent Spawn(Transform parent, string prefabRole, Vector3 pos, float yaw, int seed)
        {
            string role = ResolveRole(prefabRole, seed);
            var go = new GameObject($"Npc_{role}_{seed}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            GameObject body = null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterImporter.PrefabRoot + role + ".prefab");
            if (prefab != null)
            {
                body = (GameObject)PrefabUtility.InstantiatePrefab(prefab, go.transform);
                body.name = "Body";
            }
            else
                GreyBody(go.transform, seed);
            BuilderUtils.ClearStatic(go);

            var col = go.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.875f, 0f);
            col.height = 1.75f;
            col.radius = 0.24f;
            go.AddComponent<Rigidbody>().isKinematic = true;
            var carry = new GameObject("CarryPoint").transform;
            carry.SetParent(go.transform, false);
            carry.localPosition = new Vector3(0f, 0.72f, 0.42f);

            var agent = go.AddComponent<NpcAgent>();
            agent.carryPoint = carry;
            agent.avoidOthers = true;
            agent.yieldToPlayer = true;
            agent.walkSpeed = 0.95f + 0.25f * CrowdHash.Unit(seed, 1);
            agent.carrySpeed = agent.walkSpeed * 0.75f;
            agent.phaseSeconds = 0.5f + 3f * CrowdHash.Unit(seed, 2);
            AddVariation(go, body, seed);
            return agent;
        }

        /// <summary>Đổi điểm bê và trạng thái animation theo kiểu bê (xem CarryStyle).</summary>
        public static void SetCarryStyle(NpcAgent npc, CarryStyle style, bool slope = false)
        {
            npc.followSlope = slope;
            if (style == CarryStyle.Belly)
                return;
            npc.carryRemapIdle = true;
            var t = npc.carryPoint;
            if (style == CarryStyle.Shoulder)
            {
                // Bao vắt trên vai phải, nằm dọc sống lưng (clip ShoulderCarry/ShoulderIdle: tay phải đỡ bao).
                npc.carryWalkState = "ShoulderCarry";
                npc.carryIdleState = "ShoulderIdle";
                t.localPosition = new Vector3(0.17f, 1.5f, -0.04f);
                t.localRotation = Quaternion.Euler(78f, 0f, 10f);
            }
            else
            {
                // Va li trong tay phải (cổ tay cao 0,96 m trong clip BagWalk).
                npc.carryWalkState = "BagWalk";
                npc.carryIdleState = "BagIdle";
                t.localPosition = new Vector3(0.3f, 0.8f, 0.1f);
                t.localRotation = Quaternion.identity;
            }
        }

        /// <summary>Va li trơn (không collider) trong tay NPC; xách luôn theo animation BagWalk/BagIdle.</summary>
        public static Transform GiveBag(Transform crowd, NpcAgent npc, string name, int seed)
        {
            var leather = AssetFactory.Lit("Suitcase", seed % 2 == 0 ? new Color(0.32f, 0.2f, 0.12f) : new Color(0.18f, 0.2f, 0.24f), 0.3f);
            var bag = BuilderUtils.Prim(PrimitiveType.Cube, name, Group(crowd, "Props"), Vector3.zero, new Vector3(0.1f, 0.32f, 0.44f), leather,
                false, false);
            SetCarryStyle(npc, CarryStyle.Bag);
            npc.PickUp(bag.transform);
            return bag.transform;
        }

        static void GreyBody(Transform npc, int seed)
        {
            var cloth = AssetFactory.Lit("CrowdGreyClothes", new Color(0.45f, 0.42f, 0.38f), 0.1f);
            var skin = AssetFactory.Lit("Skin", new Color(0.78f, 0.6f, 0.47f), 0.35f);
            var body = BuilderUtils.Group("Body", npc).transform;
            BuilderUtils.Prim(PrimitiveType.Capsule, "Torso", body, new Vector3(0f, 0.78f, 0f), new Vector3(0.44f, 0.78f, 0.3f), cloth, false, false);
            BuilderUtils.Prim(PrimitiveType.Sphere, "Head", body, new Vector3(0f, 1.62f, 0f), new Vector3(0.22f, 0.26f, 0.24f), skin, false, false);
        }

        static void AddVariation(GameObject npc, GameObject body, int seed)
        {
            if (body == null)
                return;
            var slots = new List<CrowdTintSlot>();
            foreach (var r in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var kind = TintKindOf(mats[i] != null ? mats[i].name : null);
                    if (kind.HasValue)
                        slots.Add(new CrowdTintSlot { renderer = r, materialIndex = i, kind = kind.Value });
                }
            }
            if (slots.Count == 0)
                return;
            var v = npc.AddComponent<CrowdVariation>();
            v.seed = seed;
            v.slots = slots.ToArray();
        }

        // ---------- Điểm đi, đạo cụ ----------

        static Transform Group(Transform crowd, string name)
        {
            var t = crowd.Find(name);
            return t != null ? t : BuilderUtils.Group(name, crowd).transform;
        }

        /// <summary>Điểm đi: Transform con của nhóm Waypoints (vị trí cục bộ của gốc đám đông, quay yaw độ).</summary>
        public static Transform Waypoint(Transform crowd, string name, Vector3 pos, float yaw = 0f)
        {
            var t = new GameObject(name).transform;
            t.SetParent(Group(crowd, "Waypoints"), false);
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return t;
        }

        static Transform[] Waypoints(Transform crowd, string name, Vector3[] pts, float[] yaws = null)
        {
            var list = new Transform[pts.Length];
            for (int i = 0; i < pts.Length; i++)
                list[i] = Waypoint(crowd, $"{name}_{i}", pts[i], yaws != null ? yaws[i] : 0f);
            return list;
        }

        static float YawOf(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        /// <summary>Bao hàng trơn (không collider) để phu bê; ẩn cho tới khi được nhặt.</summary>
        public static Transform Sack(Transform crowd, string name, Vector3 pos, bool visible)
        {
            var burlap = AssetFactory.Lit("Burlap", new Color(0.55f, 0.45f, 0.3f), 0.05f);
            var s = BuilderUtils.Prim(PrimitiveType.Capsule, name, Group(crowd, "Props"), pos, new Vector3(0.45f, 0.3f, 0.35f),
                burlap, false, false);
            s.SetActive(visible);
            return s.transform;
        }

        // ---------- Routine ----------

        static CrowdRoutine Attach(NpcAgent npc, CrowdMode mode, int seed)
        {
            var r = npc.gameObject.AddComponent<CrowdRoutine>();
            r.agent = npc;
            r.mode = mode;
            r.seed = seed;
            return r;
        }

        /// <summary>
        /// Vòng bốc hàng: workers phu đi từ route[0] (chỗ nhặt) qua các điểm tới route[cuối] (chỗ đặt) rồi về theo back
        /// (làn về; trống thì về theo đường đi). Mỗi phu bê một bao; họ xuất phát nối đuôi sau chỗ nhặt, cách nhau
        /// spacingSeconds giây. Hướng đứng ở hai đầu quay về đống hàng bên phải đường đi.
        /// </summary>
        public static CrowdRoutine[] AddLoop(Transform crowd, string name, Vector3[] route, Vector3[] back, int workers, int seed,
            float spacingSeconds = 4f, string role = AnyRole, CarryStyle style = CarryStyle.Belly, bool slope = false, bool piles = true)
        {
            var first = (route[1] - route[0]).normalized;
            var lastDir = (route[route.Length - 1] - route[route.Length - 2]).normalized;
            var rightA = Vector3.Cross(Vector3.up, first);
            var rightB = Vector3.Cross(Vector3.up, lastDir);
            var pts = Waypoints(crowd, name, route);
            pts[0].localRotation = Quaternion.Euler(0f, YawOf(rightA), 0f);
            pts[pts.Length - 1].localRotation = Quaternion.Euler(0f, YawOf(rightB), 0f);
            var backPts = back == null ? new Transform[0] : Waypoints(crowd, name + "_back", back);
            // Đống hàng ở hai đầu: vài bao chất sát nhau bên phải đường đi.
            for (int k = 0; k < (piles ? 3 : 0); k++)
            {
                Sack(crowd, $"{name}_pileA_{k}", route[0] + rightA * 0.9f + Vector3.up * (0.2f + 0.3f * (k / 2)) + first * (0.25f * (k % 2)), true);
                Sack(crowd, $"{name}_pileB_{k}", route[route.Length - 1] + rightB * 0.9f + Vector3.up * (0.2f + 0.3f * (k / 2)) + lastDir * (0.25f * (k % 2)), true);
            }
            var result = new CrowdRoutine[workers];
            for (int i = 0; i < workers; i++)
            {
                int s = seed + i;
                var npc = Spawn(Group(crowd, "Npcs"), role, route[0] - first * (1.0f * i), YawOf(first), s);
                SetCarryStyle(npc, style, slope);
                var r = Attach(npc, CrowdMode.Loop, s);
                r.points = pts;
                r.backPoints = backPts;
                r.startDelay = i * spacingSeconds;
                r.prop = Sack(crowd, $"{name}_sack_{i}", route[0], false);
                result[i] = r;
            }
            return result;
        }

        /// <summary>
        /// Nhóm count người (2–4) đứng trên vòng bán kính radius quanh center, quay vào nhau, luân phiên nói chuyện.
        /// angleOffset là góc của người đầu tiên (độ, 0 = phía +Z).
        /// </summary>
        public static CrowdRoutine[] AddGroup(Transform crowd, string name, Vector3 center, int count, float radius, float angleOffset,
            int seed, bool optional = false, string role = AnyRole)
        {
            var result = new CrowdRoutine[count];
            for (int i = 0; i < count; i++)
            {
                float angle = angleOffset + 360f * i / count;
                var dir = new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad), 0f, Mathf.Cos(angle * Mathf.Deg2Rad));
                float facing = angle + 180f;
                var npc = Spawn(Group(crowd, "Npcs"), role, center + dir * radius, facing, seed * 10 + i);
                var r = Attach(npc, CrowdMode.Group, seed * 10 + i);
                r.groupSeed = seed;
                r.groupIndex = i;
                r.groupCount = count;
                r.yaw = facing;
                r.optional = optional;
                result[i] = r;
            }
            return result;
        }

        /// <summary>
        /// Hàng chờ count người: ô 0 ở front, các ô sau lùi dần theo toTail (hướng về đuôi) cách nhau spacing mét, mọi người
        /// quay về phía đầu hàng. Người đầu hàng rời đi qua exit rồi returnPath, và nếu recycle thì vào lại đuôi hàng.
        /// autoAdvanceSeconds &gt; 0 thì hàng tự tiến; 0 thì director gọi CrowdQueue.Advance().
        /// </summary>
        public static CrowdQueue AddQueue(Transform crowd, string name, Vector3 front, Vector3 toTail, int count, float spacing,
            Vector3 exit, Vector3[] returnPath, int seed, float autoAdvanceSeconds, bool recycle = true, string role = AnyRole,
            Vector3[] boardPath = null, Vector3? respawn = null, bool bags = false)
        {
            var go = new GameObject("Queue_" + name);
            go.transform.SetParent(crowd, false);
            var q = go.AddComponent<CrowdQueue>();
            var dir = toTail.normalized;
            float facing = YawOf(-dir);
            var slotPts = new Vector3[count];
            var slotYaws = new float[count];
            for (int i = 0; i < count; i++)
            {
                slotPts[i] = front + dir * (spacing * i);
                slotYaws[i] = facing;
            }
            q.slots = Waypoints(crowd, name + "_slot", slotPts, slotYaws);
            q.exit = Waypoint(crowd, name + "_exit", exit, facing);
            q.returnPath = returnPath == null ? new Transform[0] : Waypoints(crowd, name + "_return", returnPath);
            q.recycle = recycle;
            q.autoAdvanceSeconds = autoAdvanceSeconds;
            if (boardPath != null)
                q.boardPath = Waypoints(crowd, name + "_board", boardPath);
            if (respawn.HasValue)
                q.respawn = Waypoint(crowd, name + "_respawn", respawn.Value, facing);
            var members = new CrowdRoutine[count];
            for (int i = 0; i < count; i++)
            {
                var npc = Spawn(Group(crowd, "Npcs"), role, slotPts[i], facing, seed + i);
                if (bags)
                    GiveBag(crowd, npc, $"{name}_bag_{i}", seed + i);
                var r = Attach(npc, CrowdMode.Queue, seed + i);
                r.queue = q;
                members[i] = r;
            }
            q.members = members;
            return q;
        }

        /// <summary>
        /// Một người ngồi: seat là chỗ gốc NPC đứng trên sàn (chỗ ngồi của ghế ngay dưới), yaw là hướng nhìn. read xen kẽ
        /// SitRead (đọc báo); standUp thì thỉnh thoảng đứng dậy rồi ngồi lại.
        /// </summary>
        public static CrowdRoutine AddSit(Transform crowd, Vector3 seat, float yaw, int seed, bool read = false, bool standUp = false,
            float startDelay = 0f, string role = AnyRole, float seatHeight = 0f)
        {
            var npc = Spawn(Group(crowd, "Npcs"), role, seat, yaw, seed);
            var r = Attach(npc, CrowdMode.Sit, seed);
            r.yaw = yaw;
            r.read = read;
            r.standUp = standUp;
            r.startDelay = startDelay;
            if (seatHeight <= 0f) return r;
            // Đo đúng clip ở scale của khách; mông trên mặt ghế, đế giày ở sàn.
            var animator = npc.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) throw new System.InvalidOperationException("Khách ngồi thiếu Animator humanoid.");
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var lod = npc.GetComponentInChildren<LODGroup>();
            var baked = new Mesh();
            float Bottom()
            {
                float bottom = float.PositiveInfinity;
                foreach (var renderer in lod.GetLODs()[0].renderers)
                {
                    if (renderer is not SkinnedMeshRenderer skin) continue;
                    skin.BakeMesh(baked);
                    foreach (var vertex in baked.vertices) bottom = Mathf.Min(bottom, skin.transform.TransformPoint(vertex).y);
                }
                if (float.IsInfinity(bottom)) throw new System.InvalidOperationException("Không đo được tiếp xúc ghế.");
                return bottom;
            }
            // Rebind sau đổi scale để humanoid tính lại chiều cao; đo lại thay vì ngoại suy pose cũ.
            for (int pass = 0; pass < 2; pass++)
            {
                animator.Rebind(); animator.Update(0f); animator.Play("SitIdle", 0, 0f); animator.Update(.01f);
                float height = animator.GetBoneTransform(HumanBodyBones.Hips).position.y - Bottom();
                if (height <= 0f) throw new System.InvalidOperationException("Khách ngồi có chiều cao không hợp lệ.");
                animator.transform.localScale *= (seatHeight + .1f) / height;
            }
            animator.Rebind(); animator.Update(0f); animator.Play("SitIdle", 0, 0f); animator.Update(.01f);
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips).position;
            var intended = crowd.TransformPoint(seat);
            animator.transform.position += new Vector3(intended.x - hips.x, intended.y - Bottom(), intended.z - hips.z);
            UnityEngine.Object.DestroyImmediate(baked);
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            return r;
        }

        /// <summary>
        /// Đi dạo: count người dùng chung các điểm points, mỗi người xuất phát ở một điểm khác nhau và chọn điểm kế tiếp
        /// ngẫu nhiên theo seed của mình.
        /// </summary>
        public static CrowdRoutine[] AddWander(Transform crowd, string name, Vector3[] points, int count, int seed,
            bool optional = false, string role = AnyRole, bool midStart = false)
        {
            var pts = Waypoints(crowd, name, points);
            var result = new CrowdRoutine[count];
            for (int i = 0; i < count; i++)
            {
                var start = points[i % points.Length];
                if (midStart && points.Length > 1)
                    start = Vector3.Lerp(start, points[(i + 1) % points.Length], i < points.Length ? 0.5f : 0.3f);
                var npc = Spawn(Group(crowd, "Npcs"), role, start, 0f, seed + i);
                var r = Attach(npc, CrowdMode.Wander, seed + i);
                r.points = pts;
                r.optional = optional;
                result[i] = r;
            }
            return result;
        }

        /// <summary>Đứng yên quay hướng yaw (lean: tựa lan can, chủ yếu nhìn ra xa).</summary>
        public static CrowdRoutine AddStand(Transform crowd, Vector3 pos, float yaw, int seed, bool lean = false,
            bool optional = false, string role = AnyRole)
        {
            var npc = Spawn(Group(crowd, "Npcs"), role, pos, yaw, seed);
            var r = Attach(npc, lean ? CrowdMode.Lean : CrowdMode.Stand, seed);
            r.yaw = yaw;
            r.optional = optional;
            return r;
        }

        /// <summary>
        /// Đứng làm một động tác (state: Squat, LeanRail, Pull, WorkMid, Documents, Talk…), thỉnh thoảng xen alt. oneShot: làm một
        /// lần (Wave) rồi nghỉ, lặp lại.
        /// </summary>
        public static CrowdRoutine AddPose(Transform crowd, Vector3 pos, float yaw, int seed, string state, string alt = null,
            string role = AnyRole, bool optional = false, bool oneShot = false)
        {
            var npc = Spawn(Group(crowd, "Npcs"), role, pos, yaw, seed);
            var r = Attach(npc, CrowdMode.Pose, seed);
            r.yaw = yaw;
            r.poseState = state;
            r.altState = alt;
            r.oneShot = oneShot;
            r.optional = optional;
            return r;
        }

        // ---------- LOD ----------

        /// <summary>CrowdLod trên gốc đám đông: tối đa maxAnimated NPC chạy Animator (tầng Laptop: maxAnimatedLaptop).</summary>
        public static CrowdLod AddLod(Transform crowd, int maxAnimatedPc = 16, int maxAnimatedLaptop = 10, float animatedRadius = 25f,
            float staticRadius = 80f, float silhouetteRadius = 160f)
        {
            var lod = crowd.gameObject.AddComponent<CrowdLod>();
            lod.maxAnimatedPc = maxAnimatedPc;
            lod.maxAnimatedLaptop = maxAnimatedLaptop;
            lod.animatedRadius = animatedRadius;
            lod.staticRadius = staticRadius;
            lod.silhouetteRadius = silhouetteRadius;
            return lod;
        }
    }
}
