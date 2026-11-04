using System.Collections.Generic;
using System.Linq;
using HaiBanTay.Player;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Dò lỗi góc nhìn thứ nhất của người chơi trong một khung (ghi chú 15/10 trên điện thoại: ôm sọt cúi xuống, dốc bao khoai thấy lỗ
    /// cổ áo rỗng; ôm sọt nhìn thẳng thấy xuyên sọt; đổ tro bàn tay bám quai nhưng cánh tay hỏng):
    /// <list type="bullet">
    /// <item>Lỗ cổ: vòng đáy của phần đầu bị ẩn với camera chính (các đỉnh thấp nhất của submesh đầu, theo xương) lọt vào khung hình,
    /// đo cả 16:9 lẫn 2760×1256 của điện thoại.</item>
    /// <item>Nhìn xuyên (SeeThroughProbe, render): pixel mà mặt gần nhất là mặt sau của vật liệu chỉ vẽ mặt trước, mọi vật trong 3 m
    /// kể cả thân người chơi (lòng sọt, bao chỉ có vỏ ngoài; mắt lọt vào trong đồ; lỗ cổ áo).</item>
    /// <item>Cổ tay hở: xương cổ tay của thân cách bàn tay góc nhìn thứ nhất (IK không với tới, bàn tay bay một mình).</item>
    /// <item>Cổ tay gập: góc giữa cẳng tay và bàn tay.</item>
    /// <item>Khuỷu trong thân: khuỷu lọt vào thân (gần trục hông–cổ hơn bề dày nửa thân).</item>
    /// <item>Đồ xuyên thân: đồ đang cầm lún vào bụng, ngực, hông, tạp dề, đùi, bắp chân của chính người chơi (vỏ lồi từng khúc như
    /// BodyClipDetector của NPC; ghi chú 15/10: "bụng xuyên xô").</item>
    /// </list>
    /// </summary>
    public sealed class PlayerViewProbe
    {
        public enum Kind { NeckHole, SeeThrough, WristGap, WristBend, ElbowInBody, ItemInBody, HangingArmAngle, GripContactGap }

        public static string Text(Kind k) => k switch
        {
            Kind.NeckHole => "thấy lỗ cổ áo",
            Kind.SeeThrough => "nhìn xuyên mặt sau",
            Kind.WristGap => "cổ tay hở",
            Kind.WristBend => "cổ tay gập",
            Kind.ElbowInBody => "khuỷu trong thân",
            Kind.HangingArmAngle => "tay xách giạng khỏi phương thẳng đứng",
            Kind.GripContactGap => "lòng bàn tay rời điểm nắm",
            _ => "đồ xuyên thân",
        };

        /// <summary>Ngưỡng lỗi: lỗ cổ (số điểm vòng trong khung), nhìn xuyên (pixel ở 594×270), cổ tay hở (m), cổ tay gập (độ), khuỷu
        /// trong thân (m lún).</summary>
        public static float Limit(Kind k) => k switch
        {
            Kind.NeckHole => 0f,
            Kind.SeeThrough => 40f,
            Kind.WristGap => 0.02f,
            Kind.WristBend => 70f,
            Kind.ItemInBody => 0.015f,
            Kind.HangingArmAngle => 25f,
            Kind.GripContactGap => 0.01f,
            _ => 0.01f,
        };

        public static string Unit(Kind k) => k switch
        {
            Kind.NeckHole => "điểm",
            Kind.SeeThrough => "px",
            Kind.WristBend => "°",
            Kind.HangingArmAngle => "°",
            _ => "cm",
        };

        public struct Issue
        {
            public Kind kind;
            public string where;
            public float value;
            public bool Bad => value > Limit(kind);
            public string Show => Unit(kind) == "cm" ? $"{value * 100f:F1} cm" : $"{value:F0} {Unit(kind)}";
            public override string ToString() => $"{Text(kind)} {where} {Show}";
        }

        public static readonly float[] Aspects = { 16f / 9f, 2760f / 1256f };

        readonly PlayerRig _rig;
        readonly List<(Transform bone, Vector3 local)> _neck = new List<(Transform, Vector3)>();
        readonly Transform _lUpper, _lElbow, _lWrist, _rUpper, _rElbow, _rWrist, _hips, _neckBone;
        SeeThroughProbe _see;
        List<Renderer> _renderers;

        public PlayerViewProbe(PlayerRig rig)
        {
            _rig = rig;
            var a = rig.body.animator;
            _lUpper = a.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _lElbow = a.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _lWrist = a.GetBoneTransform(HumanBodyBones.LeftHand);
            _rUpper = a.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _rElbow = a.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _rWrist = a.GetBoneTransform(HumanBodyBones.RightHand);
            _hips = a.GetBoneTransform(HumanBodyBones.Hips);
            _neckBone = a.GetBoneTransform(HumanBodyBones.Neck) ?? a.GetBoneTransform(HumanBodyBones.Head);
            NeckRing();
            BodyHulls();
        }

        // ---------- Đồ đang cầm lún vào thân ----------

        Transform[] _bodyBones;
        MeshCollider[] _bodyHulls;
        MeshCollider _apronHull;
        readonly Dictionary<string, MeshCollider> _itemHulls = new Dictionary<string, MeshCollider>();

        void BodyHulls()
        {
            var skins = _rig.body.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh != null && s.sharedMesh.isReadable).ToArray();
            if (skins.Length > 0)
                (_bodyBones, _bodyHulls) = BodyClipDetector.BodyHulls(_rig.body.animator, skins, "player");
        }

        /// <summary>Tạp dề biến dạng theo chân mỗi khung (PlayerApron.FitToLegs): dựng lại vỏ theo mesh hiện tại, hệ của tạp dề.</summary>
        void ApronHull()
        {
            if (_apronHull != null)
            {
                Object.Destroy(_apronHull.sharedMesh);
                Object.Destroy(_apronHull.gameObject);
                _apronHull = null;
            }
            var apron = _rig.body.apron;
            if (apron == null || !apron.isActiveAndEnabled || apron.Mesh == null) return;
            _apronHull = BodyClipDetector.Hull("player_apron", apron.Mesh.vertices.ToList());
        }

        /// <summary>Vỏ lồi của một mesh đồ (hệ quay của mesh, theo mét). Mesh art không đọc được thì lấy ống elip trong hộp bao của mesh.</summary>
        MeshCollider ItemHull(MeshFilter f)
        {
            var mesh = f.sharedMesh;
            var scale = f.transform.lossyScale;
            string key = mesh.GetInstanceID() + "@" + scale.ToString("F3");
            if (_itemHulls.TryGetValue(key, out var hull) && hull != null) return hull;
            var points = new List<Vector3>();
            if (mesh.isReadable)
                foreach (var v in mesh.vertices) points.Add(Vector3.Scale(v, scale));
            else
            {
                var b = mesh.bounds;
                for (int i = 0; i < 16; i++)
                {
                    float a = i * Mathf.PI / 8f;
                    foreach (float y in new[] { b.min.y, b.max.y })
                        points.Add(Vector3.Scale(new Vector3(b.center.x + Mathf.Cos(a) * b.extents.x, y, b.center.z + Mathf.Sin(a) * b.extents.z), scale));
                }
            }
            hull = BodyClipDetector.Hull("player_item_" + mesh.name, points);
            _itemHulls[key] = hull;
            return hull;
        }

        static readonly string[] PartText = BodyClipDetector.PartNames;

        /// <summary>Số khúc thân/chân dựng được vỏ (0: mesh thân không đọc được, ItemInBody luôn 0).</summary>
        public int BodyParts => _bodyHulls == null ? 0 : BodyClipDetector.TorsoAndLegs.Count(p => _bodyHulls[p] != null);

        /// <summary>Độ lún sâu nhất của đồ đang cầm vào thân người chơi (m) và chỗ lún.</summary>
        public float ItemInBody(out string where)
        {
            var held = _rig.interactor != null ? _rig.interactor.Holding : null;
            return ItemInBody(held != null ? held.transform : null, out where);
        }

        /// <summary>Đo cả đồ làm việc không phải Holdable (nồi rửa, chảo, củ/dao khi gọt), giữ nguyên API đồ đang cầm cũ.</summary>
        public float ItemInBody(Transform item, out string where)
        {
            where = "";
            float worst = 0f;
            if (item == null || _bodyHulls == null) return 0f;
            ApronHull();
            foreach (var f in item.GetComponentsInChildren<MeshFilter>())
            {
                var r = f.GetComponent<MeshRenderer>();
                if (r == null || !r.enabled || f.sharedMesh == null || r.bounds.size.magnitude < 0.05f) continue;
                var hull = ItemHull(f);
                if (hull == null) continue;
                foreach (int p in BodyClipDetector.TorsoAndLegs)
                {
                    var bone = _bodyBones[p];
                    if (bone == null || _bodyHulls[p] == null) continue;
                    if (Physics.ComputePenetration(hull, f.transform.position, f.transform.rotation, _bodyHulls[p], bone.position, bone.rotation, out _, out float d)
                        && d > worst)
                    {
                        worst = d;
                        where = $"{f.name}→{PartText[p]}";
                    }
                }
                var apronAt = _rig.body.apron.transform;
                if (_apronHull != null && Physics.ComputePenetration(hull, f.transform.position, f.transform.rotation, _apronHull, apronAt.position,
                        apronAt.rotation, out _, out float da) && da > worst)
                {
                    worst = da;
                    where = $"{f.name}→tạp dề";
                }
            }
            return worst;
        }

        /// <summary>Bộ dò render (tạo khi cần lần đầu: test hình học chạy -nographics không dùng tới).</summary>
        public SeeThroughProbe See => _see ??= new SeeThroughProbe { skipRenderer = _rig.body.skin, skipSubmesh = _rig.body.headSubmesh };

        /// <summary>Số điểm vòng cổ trong khung (lấy tỉ lệ khung tệ hơn).</summary>
        public int NeckInFrame()
        {
            int neck = 0;
            foreach (float aspect in Aspects)
                neck = Mathf.Max(neck, NeckWorld().Count(p => InFrame(_rig.cam, p, aspect)));
            return neck;
        }

        /// <summary>Xương cổ tay của thân cách bàn tay góc nhìn thứ nhất (m).</summary>
        public float WristGap(bool left) => Vector3.Distance((left ? _lWrist : _rWrist).position, (left ? _rig.hands.left : _rig.hands.right).position);

        public float WristBend(bool left) => Vector3.Angle((left ? _lWrist.position - _lElbow.position : _rWrist.position - _rElbow.position),
            (left ? _rig.hands.left : _rig.hands.right).forward);

        public float GripContactGap(bool left)
        {
            var hands = _rig.hands;
            var grip = hands.GripAnchor(left);
            if (grip == null) return 0f;
            var offset = hands.ContactOffset(left);
            return Vector3.Distance(grip.TransformPoint(offset), (left ? hands.left : hands.right).TransformPoint(offset));
        }

        /// <summary>Tay buông/xách bên hông: vai tới cổ tay phải gần phương thẳng đứng, không giơ ngang để tránh thân.</summary>
        public float HangingArmAngle(bool left) => Mathf.Max(
            Vector3.Angle(left ? _lWrist.position - _lUpper.position : _rWrist.position - _rUpper.position, -_rig.transform.up),
            Vector3.Angle(left ? _lElbow.position - _lUpper.position : _rElbow.position - _rUpper.position, -_rig.transform.up));

        /// <summary>Gom lại danh sách renderer (sau khi đổi cảnh, bật tắt đồ).</summary>
        public void Refresh() => _renderers = SeeThroughProbe.Candidates();

        /// <summary>Số điểm vòng cổ dùng để đo (test tự kiểm có dựng được vòng).</summary>
        public int NeckPoints => _neck.Count;

        void NeckRing()
        {
            var skin = _rig.body.skin;
            var mesh = skin.sharedMesh;
            var vertices = mesh.vertices;
            var weights = mesh.boneWeights;
            var bindposes = mesh.bindposes;
            var up = skin.transform.InverseTransformDirection(_rig.transform.up).normalized;
            var head = new HashSet<int>(mesh.GetTriangles(_rig.body.headSubmesh));
            float low = head.Min(i => Vector3.Dot(vertices[i], up));
            foreach (int i in head)
            {
                if (Vector3.Dot(vertices[i], up) > low + 0.03f) continue;
                var w = weights[i];
                int bone = w.weight0 >= w.weight1 && w.weight0 >= w.weight2 && w.weight0 >= w.weight3 ? w.boneIndex0
                    : w.weight1 >= w.weight2 && w.weight1 >= w.weight3 ? w.boneIndex1 : w.weight2 >= w.weight3 ? w.boneIndex2 : w.boneIndex3;
                _neck.Add((skin.bones[bone], bindposes[bone].MultiplyPoint3x4(vertices[i])));
            }
        }

        /// <summary>Vòng đáy phần đầu bị ẩn (tọa độ thế giới, khung hiện tại).</summary>
        public IEnumerable<Vector3> NeckWorld() => _neck.Select(n => n.bone.TransformPoint(n.local));

        /// <summary>Điểm thế giới nằm trong khung hình camera chính với tỉ lệ cho trước.</summary>
        public static bool InFrame(Camera cam, Vector3 world, float aspect)
        {
            var p = cam.worldToCameraMatrix.MultiplyPoint(world);
            float z = -p.z;
            if (z <= cam.nearClipPlane) return false;
            float t = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return Mathf.Abs(p.y) < t * z && Mathf.Abs(p.x) < t * aspect * z;
        }

        /// <summary>Đo một khung (seeThrough = false: bỏ phần render cho nhanh).</summary>
        public List<Issue> Measure(bool seeThrough = true)
        {
            var issues = new List<Issue>();
            var cam = _rig.cam;

            issues.Add(new Issue { kind = Kind.NeckHole, where = "", value = NeckInFrame() });

            if (seeThrough)
            {
                if (_renderers == null) Refresh();
                foreach (var h in See.Render(cam, _renderers, 3f))
                    issues.Add(new Issue { kind = Kind.SeeThrough, where = $"{h.renderer.name}[{h.Material}]", value = h.pixels });
            }

            Arm(issues, "trái", _lUpper, _lElbow, _lWrist, _rig.hands.left);
            Arm(issues, "phải", _rUpper, _rElbow, _rWrist, _rig.hands.right);
            if (_rig.hands.Hanging && (_rig.interactor.Holding == null || !_rig.interactor.Holding.Busy))
                foreach (bool left in new[] { true, false })
                    issues.Add(new Issue { kind = Kind.HangingArmAngle, where = left ? "trái" : "phải", value = HangingArmAngle(left) });
            foreach (bool left in new[] { true, false })
                if (_rig.hands.GripAnchor(left) != null)
                    issues.Add(new Issue { kind = Kind.GripContactGap, where = left ? "trái" : "phải", value = GripContactGap(left) });
            float into = ItemInBody(out string at);
            issues.Add(new Issue { kind = Kind.ItemInBody, where = at, value = into });
            return issues;
        }

        void Arm(List<Issue> issues, string side, Transform upper, Transform elbow, Transform wrist, Transform hand)
        {
            issues.Add(new Issue { kind = Kind.WristGap, where = side, value = Vector3.Distance(wrist.position, hand.position) });
            var forearm = wrist.position - elbow.position;
            issues.Add(new Issue { kind = Kind.WristBend, where = side, value = Vector3.Angle(forearm, hand.forward) });
            // Thân như ống tròn bán kính 0,13 m quanh đoạn hông–cổ.
            var a = _hips.position;
            var b = _neckBone.position;
            var ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(elbow.position - a, ab) / ab.sqrMagnitude);
            float inside = 0.13f - Vector3.Distance(elbow.position, a + ab * t);
            issues.Add(new Issue { kind = Kind.ElbowInBody, where = side, value = Mathf.Max(0f, inside) });
        }

    }
}
