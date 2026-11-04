using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HaiBanTay.Npc;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Bộ dò xuyên thân NPC (yêu cầu 15/10: đồ vật, tay, xô, túi xuyên người lúc đi). Mỗi khúc thân (hông, bụng, ngực, cổ, đầu, vai,
    /// bắp tay, cẳng tay, bàn tay, đùi, bắp chân, bàn chân) là một vỏ lồi dựng từ chính mesh nhân vật: đỉnh theo xương nào nhiều nhất
    /// thì thuộc khúc của xương đó (xương xoắn, ngón tay, ngón chân tính vào khúc cha), lưu trong hệ trục xương nên đi theo animation.
    /// Đồ (bao, va li, xô, nồi, muôi, xẻng, khay…) là vỏ lồi của từng mesh. Độ lún = quãng ngắn nhất phải dời để hai vỏ rời nhau
    /// (Physics.ComputePenetration trên vỏ đặt xa cảnh, không va chạm gì).
    /// Đo năm loại: đồ lún vào thân (trừ bàn tay), bàn tay lún vào đồ quá chỗ nắm, bàn tay rời đồ đang bê theo dáng bê, cẳng tay /
    /// bàn tay lún vào thân mình và hai cẳng chân lún vào nhau, đồ hay tay lún vào người khác. Vỏ lồi lấp chỗ lõm (nách, lòng bàn tay
    /// cầm) và đỉnh gần khớp theo hai xương nên ngưỡng có dư (Limits).
    /// </summary>
    public sealed class BodyClipDetector
    {
        public enum Kind { ItemInBody, HandInItem, HandOff, LimbInBody, ItemInOther, LimbInOther }

        public static string KindText(Kind k) => k switch
        {
            Kind.ItemInBody => "đồ xuyên thân",
            Kind.HandInItem => "tay lún vào đồ",
            Kind.HandOff => "tay rời đồ đang bê",
            Kind.LimbInBody => "tay/chân xuyên thân mình",
            Kind.ItemInOther => "đồ xuyên người khác",
            _ => "tay xuyên người khác",
        };

        /// <summary>Ngưỡng (m): từ minor là đáng xem, từ major (kéo dài ≥ MajorSeconds) là lỗi.</summary>
        public static (float minor, float major) Limits(Kind k) => k switch
        {
            Kind.HandInItem => (0.05f, 0.08f),
            Kind.HandOff => (0.06f, 0.10f),
            Kind.LimbInBody => (0.025f, 0.04f),
            _ => (0.02f, 0.04f),
        };

        public const float MajorSeconds = 0.2f;

        public struct Contact
        {
            public NpcAgent npc, other;
            public Kind kind;
            public string a, b;
            public float depth;
            public Vector3 point;
            /// <summary>Phía của chỗ chạm trong hệ NPC (x &gt; 0: bên phải) để đặt máy chụp.</summary>
            public Vector3 local;
        }

        // ---------- Khúc thân ----------

        const int Hips = 0, Spine = 1, Chest = 2, UpperChest = 3, Neck = 4, Head = 5, ShoulderL = 6, ShoulderR = 7, UpperArmL = 8,
            UpperArmR = 9, ForearmL = 10, ForearmR = 11, HandL = 12, HandR = 13, ThighL = 14, ThighR = 15, CalfL = 16, CalfR = 17,
            FootL = 18, FootR = 19, PartCount = 20;

        static readonly HumanBodyBones[] PartBones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck,
            HumanBodyBones.Head, HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder, HumanBodyBones.LeftUpperArm,
            HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightHand, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
        };

        public static readonly string[] PartNames =
        {
            "hông", "bụng", "ngực", "ngực trên", "cổ", "đầu", "vai trái", "vai phải", "bắp tay trái", "bắp tay phải", "cẳng tay trái",
            "cẳng tay phải", "bàn tay trái", "bàn tay phải", "đùi trái", "đùi phải", "bắp chân trái", "bắp chân phải", "bàn chân trái",
            "bàn chân phải",
        };

        /// <summary>Khúc đâm vào thân mình: cẳng tay, bàn tay (bắp tay luôn sát nách, không tính).</summary>
        static readonly int[] SelfIntruders = { ForearmL, ForearmR, HandL, HandR };
        /// <summary>Khúc bị đâm: thân, đầu, đùi, bắp chân (tay chạm tay khi khoanh tay là dáng thật).</summary>
        static readonly int[] SelfTargets = { Hips, Spine, Chest, UpperChest, Neck, Head, ThighL, ThighR, CalfL, CalfR };
        static readonly (int, int)[] LegPairs = { (CalfL, CalfR), (FootL, FootR), (CalfL, FootR), (FootL, CalfR) };
        static readonly int[] OtherIntruders = { ForearmL, ForearmR, HandL, HandR };

        /// <summary>Vỏ lồi các khúc của một mesh nhân vật ở một cỡ (dùng chung mọi NPC cùng vai).</summary>
        sealed class Shape
        {
            /// <summary>Vỏ đủ (mọi mesh: thân, tạp dề, mũ): đồ cầm, đồ người khác so với vỏ này.</summary>
            public readonly MeshCollider[] hulls = new MeshCollider[PartCount];
            public readonly Bounds[] local = new Bounds[PartCount];
            /// <summary>
            /// Vỏ lõi (chỉ mesh thân chính, nhiều đỉnh nhất): tay, chân chạm thân mình hay thân người khác. Tạp dề gắn xương đùi, bắp
            /// chân phủ ngang cả hai chân nên vỏ đủ của bắp chân trùm sang chân kia (quản bếp đứng thẳng bị báo hai bắp chân lún 15 cm).
            /// </summary>
            public readonly MeshCollider[] core = new MeshCollider[PartCount];
            public readonly Bounds[] coreLocal = new Bounds[PartCount];
        }

        sealed class Item
        {
            public string name;
            public Transform tf, root;
            public MeshCollider hull;
            public Bounds world;
            /// <summary>Tay đang nắm vật này (IK bám chỗ cầm hay vật là con của xương tay): bàn tay đó lún vào chỗ nắm là đúng.</summary>
            public bool gripL, gripR;
            public bool carried;
            /// <summary>Đồ kịch bản dưới điểm bê, không qua PickUp (NpcAgent.PropInHands): một bàn tay cầm là đủ.</summary>
            public bool prop;
        }

        sealed class Rig
        {
            public NpcAgent npc;
            public Animator animator;
            public NpcArmIK arms;
            public NpcToolFollow follow;
            public Transform carryL, carryR;
            public Shape shape;
            public string role;
            public readonly Transform[] bones = new Transform[PartCount];
            public readonly Bounds[] world = new Bounds[PartCount];
            public readonly Bounds[] coreWorld = new Bounds[PartCount];
            public Transform palmL, palmR;
            public readonly List<Item> items = new List<Item>();
            public Bounds all;
        }

        static readonly Dictionary<string, Shape> Shapes = new Dictionary<string, Shape>();
        static readonly Dictionary<string, MeshCollider> ItemHulls = new Dictionary<string, MeshCollider>();
        static Transform _proxies;
        static readonly Vector3[] Directions = Fibonacci(96);

        readonly Dictionary<NpcAgent, Rig> _rigs = new Dictionary<NpcAgent, Rig>();
        readonly List<Rig> _live = new List<Rig>();

        /// <summary>Vai (tên mesh nhân vật) của NPC, để gom lỗi theo vai; rỗng nếu không phải nhân vật Humanoid.</summary>
        public string Role(NpcAgent npc) => _rigs.TryGetValue(npc, out var rig) && rig != null ? rig.role : "";

        /// <summary>Tên chung cho nhiều bản cùng loại (bao số 3 → bao số #).</summary>
        public static string Generic(string name) => Regex.Replace(name ?? "", @"\d+", "#");

        // ---------- Dựng vỏ ----------

        static Transform Proxies
        {
            get
            {
                if (_proxies == null)
                {
                    Shapes.Clear();
                    ItemHulls.Clear();
                    var go = new GameObject("BodyClipProxies");
                    go.transform.position = new Vector3(0f, -5000f, 0f);
                    Object.DontDestroyOnLoad(go);
                    _proxies = go.transform;
                }
                return _proxies;
            }
        }

        static Vector3[] Fibonacci(int n)
        {
            var dirs = new Vector3[n];
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < n; i++)
            {
                float y = 1f - 2f * (i + 0.5f) / n, r = Mathf.Sqrt(1f - y * y), a = golden * i;
                dirs[i] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            }
            return dirs;
        }

        /// <summary>
        /// Vỏ lồi qua các điểm cực theo 96 hướng (≤ 96 đỉnh: dưới trần 255 của PhysX, không có cảnh báo cắt vỏ). Vỏ này nằm trong vỏ
        /// thật nên chỉ hụt chút ở mép, không phình ra ngoài.
        /// </summary>
        internal static MeshCollider Hull(string name, List<Vector3> points)
        {
            if (points.Count < 4) return null;
            var picked = new HashSet<int>();
            foreach (var d in Directions)
            {
                int best = 0;
                float max = float.NegativeInfinity;
                for (int i = 0; i < points.Count; i++)
                {
                    float v = Vector3.Dot(points[i], d);
                    if (v > max) { max = v; best = i; }
                }
                picked.Add(best);
            }
            if (picked.Count < 4) return null;
            var verts = picked.Select(i => points[i]).ToList();
            var size = new Bounds(verts[0], Vector3.zero);
            foreach (var v in verts) size.Encapsulate(v);
            // Vỏ dẹt (khay, nắp mỏng): PhysX không dựng được vỏ phẳng tuyệt đối.
            if (Mathf.Min(size.size.x, Mathf.Min(size.size.y, size.size.z)) < 0.004f)
            {
                int thin = size.size.x <= size.size.y && size.size.x <= size.size.z ? 0 : size.size.y <= size.size.z ? 1 : 2;
                var n = Vector3.zero;
                n[thin] = 0.003f;
                verts = verts.Select(v => v + n).Concat(verts.Select(v => v - n)).ToList();
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            var tris = new List<int>();
            for (int i = 1; i + 1 < verts.Count; i++) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var go = new GameObject(name) { layer = 2 };
            go.transform.SetParent(Proxies, false);
            var col = go.AddComponent<MeshCollider>();
            col.convex = true;
            col.sharedMesh = mesh;
            return col;
        }

        /// <summary>Hệ trục chỉ xoay (không co giãn) của một Transform: vỏ lưu theo mét.</summary>
        internal static Vector3 ToLocal(Transform t, Vector3 world) => Quaternion.Inverse(t.rotation) * (world - t.position);

        static SkinnedMeshRenderer[] Skins(Animator animator)
        {
            var all = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var lod0 = all.Where(s => s.sharedMesh != null && s.name.IndexOf("_LOD1", System.StringComparison.Ordinal) < 0
                                      && s.name.IndexOf("_LOD2", System.StringComparison.Ordinal) < 0).ToArray();
            return lod0.Length > 0 ? lod0 : all.Where(s => s.sharedMesh != null).ToArray();
        }

        Rig Build(NpcAgent npc)
        {
            var animator = npc.GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isHuman || animator.avatar == null) return null;
            var skins = Skins(animator);
            if (skins.Length == 0) return null;
            var rig = new Rig
            {
                npc = npc, animator = animator, arms = npc.GetComponent<NpcArmIK>(), follow = npc.GetComponent<NpcToolFollow>(),
                carryL = npc.transform.Find("ToolCarryL"), carryR = npc.transform.Find("ToolCarryR"),
                // Mesh nhiều đỉnh nhất là thân (Cook còn có tạp dề, mũ là mesh riêng).
                role = Regex.Replace(skins.OrderByDescending(s => s.sharedMesh.vertexCount).First().sharedMesh.name, @"(_LOD\d)?(_Mesh)?$", ""),
            };
            for (int p = 0; p < PartCount; p++) rig.bones[p] = animator.GetBoneTransform(PartBones[p]);
            rig.palmL = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            rig.palmR = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            if (rig.bones[Hips] == null || rig.bones[HandL] == null || rig.bones[HandR] == null) return null;
            string key = string.Join("+", skins.Select(s => s.sharedMesh.GetInstanceID())) + "@" + Mathf.RoundToInt(animator.transform.lossyScale.x * 100f);
            if (!Shapes.TryGetValue(key, out var shape) || shape.hulls[Hips] == null)
            {
                shape = Calibrate(rig, skins);
                Shapes[key] = shape;
            }
            rig.shape = shape;
            return rig;
        }

        /// <summary>Khúc thân, đùi, bắp chân: chỗ đồ người chơi đang cầm không được lún vào (PlayerViewProbe).</summary>
        public static readonly int[] TorsoAndLegs = { Hips, Spine, Chest, UpperChest, ThighL, ThighR, CalfL, CalfR };

        /// <summary>Vỏ lồi từng khúc (theo PartNames) của một nhân vật Humanoid bất kỳ ở tư thế hiện tại, kèm xương của từng khúc: dùng cho
        /// thân người chơi.</summary>
        public static (Transform[] bones, MeshCollider[] hulls) BodyHulls(Animator animator, SkinnedMeshRenderer[] skins, string role)
        {
            var rig = new Rig { animator = animator, role = role };
            for (int p = 0; p < PartCount; p++) rig.bones[p] = animator.GetBoneTransform(PartBones[p]);
            var shape = Calibrate(rig, skins);
            return (rig.bones, shape.hulls);
        }

        /// <summary>Gom đỉnh mesh theo khúc ở tư thế hiện tại (xương nặng ký nhất của mỗi đỉnh), dựng vỏ lồi từng khúc trong hệ xương.</summary>
        static Shape Calibrate(Rig rig, SkinnedMeshRenderer[] skins)
        {
            var shape = new Shape();
            var map = new Dictionary<Transform, int>();
            for (int p = 0; p < PartCount; p++)
                if (rig.bones[p] != null) map[rig.bones[p]] = p;
            var points = new List<Vector3>[PartCount];
            var corePoints = new List<Vector3>[PartCount];
            for (int p = 0; p < PartCount; p++) { points[p] = new List<Vector3>(); corePoints[p] = new List<Vector3>(); }
            var main = skins.OrderByDescending(x => x.sharedMesh.vertexCount).First();
            var stop = rig.animator.transform.parent;
            foreach (var skin in skins)
            {
                var mesh = skin.sharedMesh;
                var bones = skin.bones;
                var bind = mesh.bindposes;
                var verts = mesh.vertices;
                var weights = mesh.boneWeights;
                if (weights.Length != verts.Length || bones.Length == 0) continue;
                var partOf = new int[bones.Length];
                var skinMatrix = new Matrix4x4[bones.Length];
                for (int b = 0; b < bones.Length; b++)
                {
                    partOf[b] = -1;
                    for (var t = bones[b]; t != null && t != stop; t = t.parent)
                        if (map.TryGetValue(t, out int p)) { partOf[b] = p; break; }
                    skinMatrix[b] = bones[b] != null && b < bind.Length ? bones[b].localToWorldMatrix * bind[b] : Matrix4x4.zero;
                }
                for (int i = 0; i < verts.Length; i++)
                {
                    var w = weights[i];
                    int dom = w.boneIndex0;
                    float best = w.weight0;
                    if (w.weight1 > best) { best = w.weight1; dom = w.boneIndex1; }
                    if (w.weight2 > best) { best = w.weight2; dom = w.boneIndex2; }
                    if (w.weight3 > best) { dom = w.boneIndex3; }
                    if (dom < 0 || dom >= bones.Length || partOf[dom] < 0) continue;
                    int part = partOf[dom];
                    // Vị trí đỉnh ở tư thế hiện tại (trộn đủ bốn xương, như GPU skinning) rồi đổi sang hệ xương của khúc.
                    var v = verts[i];
                    var world = skinMatrix[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0 + skinMatrix[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1
                              + skinMatrix[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2 + skinMatrix[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
                    var local = ToLocal(rig.bones[part], world);
                    points[part].Add(local);
                    if (skin == main) corePoints[part].Add(local);
                }
            }
            for (int p = 0; p < PartCount; p++)
            {
                shape.hulls[p] = Hull($"{rig.role}_{PartNames[p]}", points[p]);
                if (shape.hulls[p] != null) shape.local[p] = shape.hulls[p].sharedMesh.bounds;
                shape.core[p] = corePoints[p].Count == points[p].Count ? shape.hulls[p] : Hull($"{rig.role}_{PartNames[p]}_lõi", corePoints[p]);
                if (shape.core[p] != null) shape.coreLocal[p] = shape.core[p].sharedMesh.bounds;
            }
            return shape;
        }

        MeshCollider ItemHull(MeshFilter filter)
        {
            var mesh = filter.sharedMesh;
            var t = filter.transform;
            var scale = t.lossyScale;
            string key = mesh.GetInstanceID() + "@" + Mathf.RoundToInt(scale.x * 1000f) + "," + Mathf.RoundToInt(scale.y * 1000f) + "," + Mathf.RoundToInt(scale.z * 1000f);
            if (ItemHulls.TryGetValue(key, out var hull) && hull != null) return hull;
            if (!mesh.isReadable) return null;
            var verts = mesh.vertices;
            var pts = new List<Vector3>(verts.Length);
            foreach (var v in verts) pts.Add(Vector3.Scale(v, scale));
            hull = Hull("item_" + mesh.name, pts);
            ItemHulls[key] = hull;
            return hull;
        }

        // ---------- Đồ đang cầm ----------

        readonly HashSet<Transform> _roots = new HashSet<Transform>();
        readonly Dictionary<Transform, float> _gaps = new Dictionary<Transform, float>();
        readonly List<MeshFilter> _filters = new List<MeshFilter>();

        void GatherItems(Rig rig)
        {
            rig.items.Clear();
            _roots.Clear();
            var npc = rig.npc;
            if (npc.Carrying != null && npc.Carrying.gameObject.activeInHierarchy) _roots.Add(npc.Carrying);
            // Đồ treo ở điểm bê dù Carrying rỗng (va li builder cho cầm sẵn: Carrying không lưu vào cảnh) vẫn là đồ đang mang.
            if (npc.carryPoint != null)
                for (int c = 0; c < npc.carryPoint.childCount; c++)
                    if (npc.carryPoint.GetChild(c).gameObject.activeInHierarchy) _roots.Add(npc.carryPoint.GetChild(c));
            if (rig.follow != null && rig.follow.tool != null) _roots.Add(rig.follow.tool);
            if (rig.arms != null && rig.arms.enabled)
            {
                AddGrip(rig.arms.leftTarget, rig.arms.leftWeight, npc.transform);
                AddGrip(rig.arms.rightTarget, rig.arms.rightWeight, npc.transform);
            }
            foreach (var holder in new[] { rig.bones[HandL], rig.bones[HandR], rig.carryL, rig.carryR })
                if (holder != null)
                    for (int c = 0; c < holder.childCount; c++)
                    {
                        var child = holder.GetChild(c);
                        if (child.gameObject.activeInHierarchy && child.GetComponentInChildren<MeshFilter>() != null) _roots.Add(child);
                    }
            // Đồ đã nằm trong đồ khác cũng đang cầm (muôi trong thùng) chỉ tính một lần.
            foreach (var root in _roots)
            {
                if (_roots.Any(o => o != root && root.IsChildOf(o))) continue;
                bool gripL = Grips(rig, root, true), gripR = Grips(rig, root, false);
                root.GetComponentsInChildren(false, _filters);
                foreach (var f in _filters)
                {
                    var r = f.GetComponent<MeshRenderer>();
                    if (r == null || !r.enabled || f.sharedMesh == null) continue;
                    var b = r.bounds;
                    if (b.size.x < 0.03f && b.size.y < 0.03f && b.size.z < 0.03f) continue;
                    var hull = ItemHull(f);
                    if (hull == null) continue;
                    rig.items.Add(new Item
                    {
                        name = root == f.transform ? root.name : root.name + "/" + f.name, tf = f.transform, root = root, hull = hull, world = b,
                        gripL = gripL, gripR = gripR, carried = root == npc.Carrying || (npc.carryPoint != null && root.parent == npc.carryPoint),
                        prop = npc.Carrying == null && npc.carryPoint != null && root.parent == npc.carryPoint,
                    });
                }
            }
        }

        void AddGrip(Transform target, float weight, Transform npc)
        {
            if (target == null || weight < 0.5f) return;
            var owner = target.parent;
            if (owner == null || owner.IsChildOf(npc)) return;
            _roots.Add(owner);
        }

        static bool Grips(Rig rig, Transform item, bool left)
        {
            var hand = rig.bones[left ? HandL : HandR];
            if (hand != null && item.IsChildOf(hand)) return true;
            var carry = left ? rig.carryL : rig.carryR;
            if (carry != null && item.IsChildOf(carry)) return true;
            if (rig.arms == null || !rig.arms.enabled) return false;
            var target = left ? rig.arms.leftTarget : rig.arms.rightTarget;
            float w = left ? rig.arms.leftWeight : rig.arms.rightWeight;
            return target != null && w > 0.5f && target.IsChildOf(item);
        }

        /// <summary>
        /// Tay đặt lên đồ đang bê theo kiểu bê của NPC (carryWalkState: vác vai, xách va li thì tay phải; bê trước bụng thì hai tay),
        /// không theo dáng đang phát: NPC bê đồ mà đi bằng clip Walk thường (tay vung, đồ lơ lửng) cũng phải bị bắt.
        /// </summary>
        static bool ExpectsHand(Rig rig, bool left)
        {
            string s = rig.npc.carryWalkState;
            if (s == "BagWalk" || s == "ShoulderCarry" || s == "TrayCarry") return !left;
            return s == "CarryWalk";
        }

        // ---------- Đo ----------

        bool Pose(Rig rig, int p, out Vector3 pos, out Quaternion rot)
        {
            var bone = rig.bones[p];
            pos = bone != null ? bone.position : Vector3.zero;
            rot = bone != null ? bone.rotation : Quaternion.identity;
            return bone != null && rig.shape.hulls[p] != null;
        }

        bool CorePose(Rig rig, int p, out Vector3 pos, out Quaternion rot) => Pose(rig, p, out pos, out rot) && rig.shape.core[p] != null;

        static Bounds WorldBounds(Bounds local, Vector3 pos, Quaternion rot)
        {
            var c = pos + rot * local.center;
            var e = local.extents;
            var ax = rot * new Vector3(e.x, 0f, 0f);
            var ay = rot * new Vector3(0f, e.y, 0f);
            var az = rot * new Vector3(0f, 0f, e.z);
            var ext = new Vector3(Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x), Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y),
                Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
            return new Bounds(c, ext * 2f);
        }

        static float Penetration(MeshCollider a, Vector3 pa, Quaternion ra, MeshCollider b, Vector3 pb, Quaternion rb)
        {
            return Physics.ComputePenetration(a, pa, ra, b, pb, rb, out _, out float d) ? d : 0f;
        }

        static Vector3 Overlap(Bounds a, Bounds b)
        {
            var min = Vector3.Max(a.min, b.min);
            var max = Vector3.Min(a.max, b.max);
            return (min + max) * 0.5f;
        }

        /// <summary>Giữ mọi lần chạm (lún &gt; 0, mọi khoảng tay–đồ), không lọc theo ngưỡng minor: test dò chỗ đặt đồ đọc số đo thô.</summary>
        public bool raw;

        void Add(List<Contact> into, Rig rig, NpcAgent other, Kind kind, string a, string b, float depth, Vector3 point)
        {
            if (!raw && depth < Limits(kind).minor) return;
            into.Add(new Contact { npc = rig.npc, other = other, kind = kind, a = a, b = b, depth = depth, point = point, local = rig.npc.transform.InverseTransformPoint(point) });
        }

        /// <summary>NPC đo được: đang bật, thân hiện, có Animator Humanoid đang chạy (NPC CrowdLod đóng băng tư thế thì bỏ qua).</summary>
        public static bool Measurable(NpcAgent npc)
        {
            if (npc == null || !npc.isActiveAndEnabled || !npc.BodyActive || !npc.Animated) return false;
            var animator = npc.GetComponentInChildren<Animator>();
            return animator != null && animator.enabled && animator.isHuman;
        }

        /// <summary>Đo một khung cho các NPC (gọi sau IK và NpcToolFollow, tức sau LateUpdate thứ tự 13000).</summary>
        public void Sample(IEnumerable<NpcAgent> npcs, List<Contact> into)
        {
            _live.Clear();
            foreach (var npc in npcs)
            {
                if (!Measurable(npc)) continue;
                if (!_rigs.TryGetValue(npc, out var rig) || (rig != null && (rig.shape == null || rig.shape.hulls[Hips] == null)))
                    _rigs[npc] = rig = Build(npc);
                if (rig == null) continue;
                bool first = true;
                for (int p = 0; p < PartCount; p++)
                {
                    if (!Pose(rig, p, out var pos, out var rot)) continue;
                    rig.world[p] = WorldBounds(rig.shape.local[p], pos, rot);
                    if (rig.shape.core[p] != null) rig.coreWorld[p] = WorldBounds(rig.shape.coreLocal[p], pos, rot);
                    if (first) { rig.all = rig.world[p]; first = false; } else rig.all.Encapsulate(rig.world[p]);
                }
                GatherItems(rig);
                foreach (var item in rig.items) rig.all.Encapsulate(item.world);
                _live.Add(rig);
            }
            foreach (var rig in _live) Self(rig, into);
            for (int i = 0; i < _live.Count; i++)
                for (int j = 0; j < _live.Count; j++)
                    if (i != j && _live[i].all.Intersects(_live[j].all))
                        Others(_live[i], _live[j], into);
        }

        /// <summary>Dáng tay đặt lên đùi (ngồi, ngồi xổm, cúi nhặt).</summary>
        static bool Lap(string state) => !string.IsNullOrEmpty(state) && (state.StartsWith("Sit") || state == "Drink" || state == "StandUp"
                                                                          || state == "Squat" || state == "PickUp");

        void Self(Rig rig, List<Contact> into)
        {
            // Đồ đang cầm với từng khúc thân.
            foreach (var item in rig.items)
            {
                var ip = item.tf.position;
                var ir = item.tf.rotation;
                for (int p = 0; p < PartCount; p++)
                {
                    if (!Pose(rig, p, out var pos, out var rot) || !rig.world[p].Intersects(item.world)) continue;
                    float d = Penetration(rig.shape.hulls[p], pos, rot, item.hull, ip, ir);
                    if (d <= 0f) continue;
                    // Bàn tay đang nắm đồ (hay dáng bê đặt tay lên đồ) thì lún vào chỗ nắm là đúng, chỉ tính khi quá sâu; tay khác
                    // lún vào đồ như mọi khúc thân.
                    bool left = p == HandL;
                    bool holding = (p == HandL || p == HandR) && ((left ? item.gripL : item.gripR) || item.prop || (item.carried && ExpectsHand(rig, left)));
                    Add(into, rig, null, holding ? Kind.HandInItem : Kind.ItemInBody, PartNames[p], item.name, d, Overlap(rig.world[p], item.world));
                }
            }
            // Tay rời đồ đang bê: lòng bàn tay cách món đồ (mesh gần nhất của cả món: tay nắm vành thùng thì không tính khoảng tới đáy).
            for (int k = 0; k < 2; k++)
            {
                bool left = k == 0;
                if (!ExpectsHand(rig, left)) continue;
                var hand = rig.bones[left ? HandL : HandR];
                var mid = left ? rig.palmL : rig.palmR;
                var palm = mid != null ? Vector3.Lerp(hand.position, mid.position, 0.7f) : hand.position;
                _gaps.Clear();
                foreach (var item in rig.items)
                {
                    if (!item.carried || item.prop) continue;
                    var near = Physics.ClosestPoint(palm, item.hull, item.tf.position, item.tf.rotation);
                    float gap = Vector3.Distance(palm, near);
                    _gaps[item.root] = _gaps.TryGetValue(item.root, out float g) ? Mathf.Min(g, gap) : gap;
                }
                foreach (var kv in _gaps)
                    Add(into, rig, null, Kind.HandOff, PartNames[left ? HandL : HandR], kv.Key.name, kv.Value, palm);
            }
            // Đồ kịch bản: tay gần đồ hơn phải chạm đồ.
            if (rig.bones[HandL] != null && rig.bones[HandR] != null)
            {
                var palmL = rig.palmL != null ? Vector3.Lerp(rig.bones[HandL].position, rig.palmL.position, 0.7f) : rig.bones[HandL].position;
                var palmR = rig.palmR != null ? Vector3.Lerp(rig.bones[HandR].position, rig.palmR.position, 0.7f) : rig.bones[HandR].position;
                _gaps.Clear();
                var side = new Dictionary<Transform, bool>();
                foreach (var item in rig.items)
                {
                    if (!item.prop) continue;
                    float gl = Vector3.Distance(palmL, Physics.ClosestPoint(palmL, item.hull, item.tf.position, item.tf.rotation));
                    float gr = Vector3.Distance(palmR, Physics.ClosestPoint(palmR, item.hull, item.tf.position, item.tf.rotation));
                    float gap = Mathf.Min(gl, gr);
                    if (!_gaps.TryGetValue(item.root, out float g) || gap < g)
                    {
                        _gaps[item.root] = gap;
                        side[item.root] = gl < gr;
                    }
                }
                foreach (var kv in _gaps)
                    Add(into, rig, null, Kind.HandOff, PartNames[side[kv.Key] ? HandL : HandR], kv.Key.name, kv.Value, side[kv.Key] ? palmL : palmR);
            }
            // Cẳng tay, bàn tay lún vào thân mình.
            foreach (int a in SelfIntruders)
            {
                if (!CorePose(rig, a, out var pa, out var ra)) continue;
                foreach (int b in SelfTargets)
                {
                    // Ngồi, ngồi xổm, cúi nhặt: tay đặt lên đùi là dáng thật, mà gối gập sâu thì đỉnh quanh gối theo hai xương, vỏ đùi lệch
                    // (bản quét 15/10: khách ngồi đặt tay lên đùi bị báo lún 9 cm).
                    if ((b == ThighL || b == ThighR) && Lap(rig.npc.State)) continue;
                    if (!CorePose(rig, b, out var pb, out var rb) || !rig.coreWorld[a].Intersects(rig.coreWorld[b])) continue;
                    float d = Penetration(rig.shape.core[a], pa, ra, rig.shape.core[b], pb, rb);
                    if (d > 0f) Add(into, rig, null, Kind.LimbInBody, PartNames[a], PartNames[b], d, Overlap(rig.coreWorld[a], rig.coreWorld[b]));
                }
            }
            foreach (var (a, b) in LegPairs)
            {
                if (!CorePose(rig, a, out var pa, out var ra) || !CorePose(rig, b, out var pb, out var rb) || !rig.coreWorld[a].Intersects(rig.coreWorld[b])) continue;
                float d = Penetration(rig.shape.core[a], pa, ra, rig.shape.core[b], pb, rb);
                if (d > 0f) Add(into, rig, null, Kind.LimbInBody, PartNames[a], PartNames[b], d, Overlap(rig.coreWorld[a], rig.coreWorld[b]));
            }
        }

        void Others(Rig rig, Rig other, List<Contact> into)
        {
            for (int p = 0; p < PartCount; p++)
            {
                if (!Pose(other, p, out var pb, out var rb)) continue;
                foreach (var item in rig.items)
                {
                    if (!other.world[p].Intersects(item.world)) continue;
                    float d = Penetration(other.shape.hulls[p], pb, rb, item.hull, item.tf.position, item.tf.rotation);
                    if (d > 0f) Add(into, rig, other.npc, Kind.ItemInOther, item.name, other.npc.name + " " + PartNames[p], d, Overlap(other.world[p], item.world));
                }
                if (other.shape.core[p] == null) continue;
                foreach (int a in OtherIntruders)
                {
                    if (!CorePose(rig, a, out var pa, out var ra) || !rig.coreWorld[a].Intersects(other.coreWorld[p])) continue;
                    float d = Penetration(rig.shape.core[a], pa, ra, other.shape.core[p], pb, rb);
                    if (d > 0f) Add(into, rig, other.npc, Kind.LimbInOther, PartNames[a], other.npc.name + " " + PartNames[p], d, Overlap(rig.coreWorld[a], other.coreWorld[p]));
                }
            }
        }
    }

    /// <summary>
    /// Gom các lần chạm qua thời gian: mỗi cặp (NPC, loại, khúc, đồ) có độ lún lớn nhất, tổng thời gian, đoạn liền dài nhất ở mức
    /// minor và ở mức major, các dáng (state Animator) đã gặp và khung tệ nhất.
    /// </summary>
    public sealed class BodyClipTrack
    {
        public sealed class Finding
        {
            public string key, scene, npc, role, a, b, worst, shot;
            public BodyClipDetector.Kind kind;
            public float maxDepth, total, longest, stretch, majorStretch, majorLongest, shotDepth;
            public readonly Dictionary<string, float> states = new Dictionary<string, float>();
            public int seenFrame = -1;
            public Vector3 point;

            public string Severity
            {
                get
                {
                    var (minor, major) = BodyClipDetector.Limits(kind);
                    return maxDepth >= major && majorLongest >= BodyClipDetector.MajorSeconds ? "MAJOR" : maxDepth >= minor ? "MINOR" : "INFO";
                }
            }
        }

        public readonly Dictionary<string, Finding> Findings = new Dictionary<string, Finding>();
        public int Samples { get; private set; }
        readonly HashSet<string> _present = new HashSet<string>();

        public static string KeyOf(BodyClipDetector.Contact c) => c.npc.name + "|" + c.kind + "|" + c.a + "|" + c.b;

        /// <summary>Ghi một mẫu (dt: thời gian từ mẫu trước). Trả về các mục vừa lần đầu thành MAJOR ở mẫu này.</summary>
        public List<Finding> Feed(string scene, List<BodyClipDetector.Contact> contacts, float dt, System.Func<NpcAgent, string> role)
        {
            Samples++;
            _present.Clear();
            var promoted = new List<Finding>();
            // Một cặp có thể có nhiều vỏ (nhiều mesh của cùng đồ): lấy chỗ lún sâu nhất.
            foreach (var c in contacts.OrderByDescending(x => x.depth))
            {
                string key = KeyOf(c);
                if (!_present.Add(key)) continue;
                if (!Findings.TryGetValue(key, out var f))
                    Findings[key] = f = new Finding { key = key, scene = scene, npc = c.npc.name, role = role(c.npc), kind = c.kind, a = c.a, b = c.b };
                string was = f.Severity;
                var (_, major) = BodyClipDetector.Limits(c.kind);
                f.total += dt;
                f.stretch += dt;
                f.longest = Mathf.Max(f.longest, f.stretch);
                if (c.depth >= major) { f.majorStretch += dt; f.majorLongest = Mathf.Max(f.majorLongest, f.majorStretch); }
                else f.majorStretch = 0f;
                f.seenFrame = Time.frameCount;
                string state = c.npc.State ?? "";
                f.states[state] = Mathf.Max(f.states.TryGetValue(state, out float s) ? s : 0f, c.depth);
                if (c.depth > f.maxDepth)
                {
                    f.maxDepth = c.depth;
                    f.point = c.point;
                    var animator = c.npc.GetComponentInChildren<Animator>();
                    string clip = animator != null && animator.isActiveAndEnabled ? $", clip {animator.GetCurrentAnimatorStateInfo(0).normalizedTime % 1f:F2}" : "";
                    f.worst = $"t={Time.time:F1} s, dáng {state}{clip}, đang đi {c.npc.Walking}, ở {c.npc.Position}";
                }
                if (was != "MAJOR" && f.Severity == "MAJOR") promoted.Add(f);
            }
            foreach (var f in Findings.Values)
                if (!_present.Contains(f.key)) f.stretch = f.majorStretch = 0f;
            return promoted;
        }

        public static string Line(Finding f) =>
            $"[{f.Severity}] {f.npc} ({f.role}) {BodyClipDetector.KindText(f.kind)}: {f.a} ↔ {f.b}, lún sâu nhất {f.maxDepth * 100f:F1} cm, " +
            $"tổng {f.total:F1} s, liền dài nhất {f.longest:F1} s (mức lỗi {f.majorLongest:F1} s); dáng: " +
            string.Join(", ", f.states.OrderByDescending(x => x.Value).Select(x => $"{(x.Key == "" ? "-" : x.Key)} {x.Value * 100f:F1} cm")) + $"; tệ nhất {f.worst}";
    }
}
