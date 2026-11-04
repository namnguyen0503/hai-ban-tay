using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HaiBanTay.Player;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Bộ dò xuyên vật của người chơi (17/10: "Tay của Ba vẫn xuyên chậu xuyên nồi", "làm bộ phát hiện clipping ở mọi nơi"). Mỗi khung,
    /// sau IK tay và thân, đo độ lún vào mọi mesh nhìn thấy được của cảnh (đồ đạc, vách, chậu, nồi, đồ NPC đang cầm…) của:
    /// <list type="bullet">
    /// <item>bắp tay, cẳng tay Ba: vỏ lồi dựng từ mesh thân theo xương (BodyClipDetector.BodyHulls);</item>
    /// <item>bàn tay góc nhìn thứ nhất (mesh dáng tay, tay có ngón: nướng mesh mỗi khung);</item>
    /// <item>dụng cụ đang cầm (chổi, xẻng, cào, bàn chải) và đồ đang bê (mọi mesh khác dưới PlayerRig, đồ Interactor đang cầm);</item>
    /// <item>thân, đùi, bắp chân Ba (lưng chạm vách khi thân lùi sau mắt, bụng lún vào bàn khi bước sát chỗ làm).</item>
    /// </list>
    /// Mesh cảnh dùng nguyên lưới tam giác (nồi rỗng, chậu hở: tay trong lòng nồi không tính lún), khối nguyên thủy (hộp, trụ, cầu) là
    /// vỏ đặc. Bỏ qua mesh trong suốt hay cắt alpha (nước, vết bẩn, kính, khói), mesh chỉ đổ bóng, LOD xa. Bàn tay được lún vào vật nó
    /// đang nắm (điểm cầm của trạm nằm trong vật) tới ngưỡng nắm.
    /// </summary>
    public sealed class PlayerClipDetector
    {
        public enum Kind { Arm, Hand, Grip, Tool, Held, Body }

        public static string KindText(Kind k) => k switch
        {
            Kind.Arm => "cánh tay xuyên vật",
            Kind.Hand => "bàn tay xuyên vật",
            Kind.Grip => "bàn tay lún quá chỗ nắm",
            Kind.Tool => "dụng cụ xuyên vật",
            Kind.Held => "đồ bê xuyên vật",
            _ => "thân xuyên vật",
        };

        /// <summary>Ngưỡng (m): từ minor là đáng xem, quá major liền ≥ MajorSeconds là lỗi.</summary>
        public static (float minor, float major) Limits(Kind k) => k switch
        {
            Kind.Arm => (0.01f, 0.02f),
            Kind.Hand => (0.015f, 0.03f),
            Kind.Grip => (0.035f, 0.06f),
            Kind.Tool => (0.02f, 0.04f),
            Kind.Held => (0.015f, 0.03f),
            _ => (0.025f, 0.05f),
        };

        public const float MajorSeconds = 0.2f;

        /// <summary>
        /// Cẳng tay vặn ngược (17/10: "Tay bê nồi bạn làm ngược cẳng rồi. Flip lại 180 độ"): độ xoay dọc cẳng tay so với xương gốc
        /// (PlayerBody.TwistLeft/Right) khi tay thả lỏng là −90° (trái) / +90° (phải); bê nồi đúng (lòng bàn tay áp thành nồi) −140° / +143°;
        /// dáng sai (lòng bàn tay ngửa về phía người, ngón chĩa lên) +38° / −37°, tay trái cầm củ sai +72…+111°. Quá ngưỡng này về phía
        /// đó là ngược cẳng (ClipFit17.FlipFit).
        /// </summary>
        public const float ReversedTwist = 25f;
        /// <summary>Gần ±180° là đầu ngửa/úp hết cỡ của phía đúng (−180° và +180° là một hướng): không tính ngược.</summary>
        public const float WrapTwist = 155f;

        public static float Reversal(PlayerBody body, out string which)
        {
            // Tay đang ẩn (ArmsVisibility, 17/10): không ai thấy cẳng tay, không tính.
            if (body.rig != null && !body.rig.ArmsShown) { which = "tay ẩn"; return 0f; }
            float Band(float t) => t > ReversedTwist && t < WrapTwist ? t - ReversedTwist : 0f;
            float left = Band(body.TwistLeft), right = Band(-body.TwistRight);
            which = left >= right ? $"trái {body.TwistLeft:F0}°" : $"phải {body.TwistRight:F0}°";
            return Mathf.Max(0f, Mathf.Max(left, right));
        }

        public struct Contact
        {
            public Kind kind;
            public string part, obj;
            public float depth;
            public Vector3 point;
        }

        sealed class Target
        {
            public Renderer renderer;
            public Transform tf;
            public Mesh mesh;
            public Vector3 scale;
            /// <summary>Tạo khi vật lần đầu ở gần tay (nấu lưới cho mọi mesh cảnh ngay từ đầu thì quá chậm).</summary>
            public MeshCollider proxy;
            public string name;
        }

        struct Mover
        {
            public string part;
            public Kind kind;
            public MeshCollider hull;
            public Vector3 position;
            public Quaternion rotation;
            public Bounds bounds;
            /// <summary>Bàn tay: điểm cầm trạm đang giao (null nếu không nắm gì của cảnh).</summary>
            public Transform grip;
        }

        // Bắp tay, cẳng tay trái/phải theo BodyClipDetector.PartNames.
        static readonly int[] ArmParts = { 8, 9, 10, 11 };
        // Bàn chân luôn chạm sàn: không đo.
        static readonly int[] BodyParts = BodyClipDetector.TorsoAndLegs;
        static readonly HashSet<string> SolidPrimitives = new HashSet<string> { "Cube", "Sphere", "Cylinder", "Capsule" };

        static Transform _proxies;
        static readonly Dictionary<string, MeshCollider> Proxies = new Dictionary<string, MeshCollider>();
        static readonly Dictionary<string, MeshCollider> Hulls = new Dictionary<string, MeshCollider>();

        PlayerRig _rig;
        Transform[] _bones;
        MeshCollider[] _armHulls;
        readonly List<Target> _world = new List<Target>();
        readonly List<Target> _near = new List<Target>();
        readonly List<Mover> _movers = new List<Mover>();
        // Đồ đang bê cũng là vật cánh tay không được xuyên (tay ôm sọt thì cẳng tay ở ngoài thành sọt).
        readonly List<Target> _held = new List<Target>();
        readonly Dictionary<Renderer, Target> _heldTargets = new Dictionary<Renderer, Target>();
        readonly Dictionary<SkinnedMeshRenderer, (Mesh mesh, MeshCollider hull)> _baked = new Dictionary<SkinnedMeshRenderer, (Mesh, MeshCollider)>();
        float _scanAt = -1f;
        string _scene;

        /// <summary>Số mesh cảnh không đọc được (bỏ qua, ghi vào báo cáo).</summary>
        public int Unreadable { get; private set; }
        public int WorldCount => _world.Count;
        public bool ArmsMeasured => _armHulls != null && ArmParts.All(p => _armHulls[p] != null);

        static Transform ProxyRoot
        {
            get
            {
                if (_proxies == null)
                {
                    Proxies.Clear();
                    Hulls.Clear();
                    var go = new GameObject("PlayerClipProxies");
                    go.transform.position = new Vector3(0f, -6000f, 0f);
                    Object.DontDestroyOnLoad(go);
                    _proxies = go.transform;
                }
                return _proxies;
            }
        }

        static string ScaleKey(Vector3 s) => Mathf.RoundToInt(s.x * 1000f) + "," + Mathf.RoundToInt(s.y * 1000f) + "," + Mathf.RoundToInt(s.z * 1000f);

        /// <summary>
        /// Collider thay thế cho mesh cảnh (đặt xa cảnh, cùng tỉ lệ): lưới tam giác, hay vỏ đặc với khối nguyên thủy cỡ đồ đạc (vòm trời,
        /// mặt biển dựng từ khối cầu/hộp lớn bao quanh người chơi thì chỉ tính mặt).
        /// </summary>
        static MeshCollider Proxy(Mesh mesh, Vector3 scale)
        {
            bool solid = SolidPrimitives.Contains(mesh.name) && Vector3.Scale(mesh.bounds.size, scale).magnitude < 8f;
            string key = mesh.GetInstanceID() + "@" + ScaleKey(scale) + (solid ? "s" : "");
            if (Proxies.TryGetValue(key, out var c) && c != null) return c;
            var go = new GameObject("proxy_" + mesh.name);
            go.transform.SetParent(ProxyRoot, false);
            go.transform.localScale = scale;
            c = go.AddComponent<MeshCollider>();
            c.convex = solid;
            c.sharedMesh = mesh;
            Proxies[key] = c;
            return c;
        }

        /// <summary>Vỏ lồi của một mesh tay/đồ (hệ quay của mesh, đã nhân tỉ lệ).</summary>
        static MeshCollider Hull(Mesh mesh, Vector3 scale)
        {
            string key = mesh.GetInstanceID() + "@" + ScaleKey(scale);
            if (Hulls.TryGetValue(key, out var h) && h != null) return h;
            h = BodyClipDetector.Hull("playerclip_" + mesh.name, mesh.vertices.Select(v => Vector3.Scale(v, scale)).ToList());
            Hulls[key] = h;
            return h;
        }

        static Bounds WorldBounds(Bounds local, Vector3 position, Quaternion rotation)
        {
            var b = new Bounds(position + rotation * local.center, Vector3.zero);
            var e = local.extents;
            for (int i = 0; i < 8; i++)
                b.Encapsulate(position + rotation * (local.center + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z)));
            return b;
        }

        public static string Generic(string name) => BodyClipDetector.Generic(name);

        static string NameOf(Transform t) => t.parent != null ? t.parent.name + "/" + t.name : t.name;

        void Bind(PlayerRig rig)
        {
            _rig = rig;
            _bones = null;
            _armHulls = null;
            var skins = rig.body.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh != null && s.sharedMesh.isReadable).ToArray();
            if (skins.Length > 0) (_bones, _armHulls) = BodyClipDetector.BodyHulls(rig.body.animator, skins, "player_clip");
        }

        /// <summary>Danh sách mesh cảnh nhìn thấy được, làm lại mỗi giây (đồ bật/tắt, đổi cảnh).</summary>
        void Scan()
        {
            _world.Clear();
            Unreadable = 0;
            var farLods = new HashSet<Renderer>();
            foreach (var g in Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
            {
                var lods = g.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                    foreach (var r in lods[i].renderers)
                        if (r != null) farLods.Add(r);
            }
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || r.shadowCastingMode == ShadowCastingMode.ShadowsOnly || farLods.Contains(r)) continue;
                if (OfPlayer(_rig, r.transform)) continue;
                var f = r.GetComponent<MeshFilter>();
                var mesh = f != null ? f.sharedMesh : null;
                if (mesh == null || mesh.vertexCount == 0 || r.bounds.size.magnitude < 0.01f) continue;
                bool seeThrough = false;
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.renderQueue >= 2450) { seeThrough = true; break; }
                if (seeThrough) continue;
                if (!mesh.isReadable) { Unreadable++; continue; }
                var s = r.transform.lossyScale;
                if (Mathf.Abs(s.x) < 1e-4f || Mathf.Abs(s.y) < 1e-4f || Mathf.Abs(s.z) < 1e-4f) continue;
                _world.Add(new Target { renderer = r, tf = r.transform, mesh = mesh, scale = s, name = NameOf(r.transform) });
            }
        }

        /// <summary>Thuộc người chơi: gốc PlayerRig, thân, tay góc nhìn thứ nhất, camera (đồ đang bê nằm dưới điểm giữ của tay).</summary>
        static bool OfPlayer(PlayerRig rig, Transform t) =>
            t.IsChildOf(rig.transform) || t.IsChildOf(rig.hands.transform) || t.IsChildOf(rig.body.transform) || (rig.cam != null && t.IsChildOf(rig.cam.transform));

        Target HeldTarget(MeshRenderer r, string name)
        {
            if (!_heldTargets.TryGetValue(r, out var t))
            {
                var f = r.GetComponent<MeshFilter>();
                if (f == null || f.sharedMesh == null || !f.sharedMesh.isReadable) return null;
                _heldTargets[r] = t = new Target { renderer = r, tf = r.transform, mesh = f.sharedMesh, scale = r.transform.lossyScale };
            }
            t.name = name;
            return t;
        }

        void AddMeshMover(Renderer r, Kind kind, string part, Transform grip = null)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) return;
            MeshCollider hull = null;
            if (r is SkinnedMeshRenderer skin)
            {
                if (skin.sharedMesh == null) return;
                if (!_baked.TryGetValue(skin, out var b))
                    b = (new Mesh { name = skin.name + "_baked" }, null);
                skin.BakeMesh(b.mesh, true);
                if (b.hull != null) { Object.Destroy(b.hull.sharedMesh); Object.Destroy(b.hull.gameObject); }
                b.hull = BodyClipDetector.Hull("playerclip_" + skin.name, b.mesh.vertices.ToList());
                _baked[skin] = b;
                hull = b.hull;
            }
            else
            {
                var f = r.GetComponent<MeshFilter>();
                if (f == null || f.sharedMesh == null || !f.sharedMesh.isReadable) return;
                hull = Hull(f.sharedMesh, r.transform.lossyScale);
            }
            if (hull == null) return;
            var t = r.transform;
            _movers.Add(new Mover
            {
                part = part, kind = kind, hull = hull, position = t.position, rotation = t.rotation,
                bounds = WorldBounds(hull.sharedMesh.bounds, t.position, t.rotation), grip = grip,
            });
        }

        /// <summary>Đo một khung. contacts: mọi cặp lún &gt; 1 mm.</summary>
        public void Sample(List<Contact> contacts)
        {
            var rig = PlayerRig.Current;
            if (rig == null || rig.body == null || rig.hands == null) return;
            string scene = SceneManager.GetActiveScene().name;
            if (rig != _rig) { Bind(rig); _scanAt = -1f; }
            if (scene != _scene || Time.time >= _scanAt)
            {
                _scene = scene;
                _scanAt = Time.time + 1f;
                Scan();
            }

            // ---- Phần của Ba: tay thân, bàn tay góc nhìn thứ nhất, dụng cụ, đồ bê ----
            _movers.Clear();
            _held.Clear();
            // Ngồi ghế thật: hông, đùi tì lên mặt ghế là dáng ngồi (vỏ lồi cứng của hông không theo da ép xuống); bắp chân vẫn đo.
            var sit = rig.GetComponent<SitController>();
            bool seated = sit != null && (sit.Seated || sit.StudySeated);
            // Tay đang ẩn (ArmsVisibility, 17/10): chỉ đo thân, dụng cụ và đồ đang bê (vẫn hiện).
            bool arms = rig.ArmsShown;
            if (_armHulls != null)
                foreach (int p in ArmParts.Concat(BodyParts))
                {
                    if (seated && (p == 0 || p == 14 || p == 15)) continue;
                    if (!arms && ArmParts.Contains(p)) continue;
                    var bone = _bones[p];
                    var hull = _armHulls[p];
                    if (bone == null || hull == null) continue;
                    _movers.Add(new Mover
                    {
                        part = BodyClipDetector.PartNames[p], kind = ArmParts.Contains(p) ? Kind.Arm : Kind.Body, hull = hull, position = bone.position,
                        rotation = bone.rotation, bounds = WorldBounds(hull.sharedMesh.bounds, bone.position, bone.rotation),
                    });
                }
            var hands = rig.hands;
            var tools = new[] { hands.shovelProp, hands.shovelLoad, hands.rakeProp, hands.broomProp, hands.brushProp }.Where(x => x != null).ToArray();
            bool IsTool(Transform t) => tools.Any(x => t.IsChildOf(x.transform));
            // Tay đang cầm dụng cụ thì nắm cán, không nắm vật nào của cảnh: bàn chải ở tay phải; chổi, xẻng, cào cả hai tay.
            bool brushInRight = hands.brushProp != null && hands.brushProp.activeInHierarchy;
            bool twoHandTool = tools.Any(x => x != hands.brushProp && x.activeInHierarchy);
            foreach (var (side, left) in new[] { (hands.left, true), (hands.right, false) })
            {
                if (side == null || !arms) continue;
                string part = left ? "bàn tay trái" : "bàn tay phải";
                var grip = twoHandTool || (!left && brushInRight) ? null : hands.GripAnchor(left);
                foreach (var r in side.GetComponentsInChildren<Renderer>())
                    if (!IsTool(r.transform) && !(r is ParticleSystemRenderer)) AddMeshMover(r, Kind.Hand, part, grip);
            }
            foreach (var tool in tools)
                if (tool.activeInHierarchy)
                    foreach (var r in tool.GetComponentsInChildren<MeshRenderer>())
                        AddMeshMover(r, Kind.Tool, tool.name);
            var bodyRoot = rig.body.transform;
            var held = rig.interactor != null && rig.interactor.Holding != null ? rig.interactor.Holding.transform : null;
            var carried = rig.GetComponentsInChildren<MeshRenderer>().Concat(hands.GetComponentsInChildren<MeshRenderer>()).Distinct();
            if (held != null && !OfPlayer(rig, held)) carried = carried.Concat(held.GetComponentsInChildren<MeshRenderer>());
            foreach (var r in carried)
            {
                var t = r.transform;
                if (t.IsChildOf(bodyRoot) || (hands.left != null && t.IsChildOf(hands.left)) || (hands.right != null && t.IsChildOf(hands.right)) || IsTool(t)) continue;
                if (!r.enabled || r.shadowCastingMode == ShadowCastingMode.ShadowsOnly || r.sharedMaterials.Any(m => m != null && m.renderQueue >= 2450)) continue;
                string name = Generic(held != null && t.IsChildOf(held) ? held.name : NameOf(t));
                AddMeshMover(r, Kind.Held, name);
                var target = HeldTarget(r, "đang bê: " + NameOf(t));
                if (target != null) _held.Add(target);
            }
            if (_movers.Count == 0) return;

            // ---- Mesh cảnh gần tay ----
            var all = _movers[0].bounds;
            foreach (var m in _movers) all.Encapsulate(m.bounds);
            _near.Clear();
            // Danh sách cảnh quét lại mỗi giây: đồ vừa nhặt (ra khỏi cảnh, vào tay) còn nằm trong đó tới lần quét sau, không so với chính nó
            // (bot 17/10: xô tro ↔ tro trong xô, thịt ↔ xương của nó, sọt ↔ than trong sọt, đều < 1 s).
            foreach (var w in _world)
                if (w.renderer != null && w.renderer.enabled && w.tf.gameObject.activeInHierarchy && w.renderer.bounds.Intersects(all)
                    && !OfPlayer(rig, w.tf) && (held == null || !w.tf.IsChildOf(held)))
                    _near.Add(w);
            foreach (var m in _movers)
                foreach (var w in m.kind == Kind.Arm ? _near.Concat(_held) : _near)
                {
                    var wb = w.renderer.bounds;
                    if (!wb.Intersects(m.bounds)) continue;
                    if (w.proxy == null) w.proxy = Proxy(w.mesh, w.scale);
                    if (!Physics.ComputePenetration(m.hull, m.position, m.rotation, w.proxy, w.tf.position, w.tf.rotation, out _, out float depth) || depth < 0.001f)
                        continue;
                    var kind = m.kind;
                    // Bàn tay nắm vật của cảnh: điểm cầm trạm nằm trong vật (dư 3 cm) thì lún tới ngưỡng nắm.
                    if (kind == Kind.Hand && m.grip != null)
                    {
                        var g = wb;
                        g.Expand(0.06f);
                        if (g.Contains(m.grip.position)) kind = Kind.Grip;
                    }
                    var c = m.bounds.center;
                    contacts.Add(new Contact { kind = kind, part = m.part, obj = w.name, depth = depth, point = (c + wb.ClosestPoint(c)) * 0.5f });
                }
        }

        public void Dispose()
        {
            foreach (var b in _baked.Values)
            {
                if (b.mesh != null) Object.Destroy(b.mesh);
                if (b.hull != null) { Object.Destroy(b.hull.sharedMesh); Object.Destroy(b.hull.gameObject); }
            }
            _baked.Clear();
            _heldTargets.Clear();
        }
    }

    /// <summary>Gom các mẫu của PlayerClipDetector theo (loại, phần của Ba, vật cảnh): lún sâu nhất, liền dài nhất trên ngưỡng major.</summary>
    public sealed class PlayerClipTrack
    {
        public sealed class Finding
        {
            public string key, scene, part, obj, when, shot;
            public PlayerClipDetector.Kind kind;
            public float maxDepth, run, longest, shotDepth;
            public Vector3 point;
            public int seenFrame = -1;

            public string Severity
            {
                get
                {
                    var (minor, major) = PlayerClipDetector.Limits(kind);
                    return longest >= PlayerClipDetector.MajorSeconds ? "MAJOR" : maxDepth >= minor ? "MINOR" : "INFO";
                }
            }

            public string Line => $"{Severity} {scene}: {PlayerClipDetector.KindText(kind)} — {part} ↔ {obj}: lún {maxDepth * 100f:F1} cm, " +
                                  $"quá ngưỡng {PlayerClipDetector.Limits(kind).major * 100f:F0} cm liền {longest:F2} s ({when})";
        }

        public readonly Dictionary<string, Finding> Findings = new Dictionary<string, Finding>();
        public int Samples { get; private set; }
        readonly HashSet<string> _present = new HashSet<string>();

        public static string KeyOf(PlayerClipDetector.Contact c) => c.kind + "|" + c.part + "|" + PlayerClipDetector.Generic(c.obj);

        /// <summary>Ghi một mẫu (dt: giờ game từ mẫu trước). Trả về các mục vừa thành MAJOR ở mẫu này.</summary>
        public List<Finding> Feed(string scene, string when, List<PlayerClipDetector.Contact> contacts, float dt)
        {
            Samples++;
            _present.Clear();
            var promoted = new List<Finding>();
            foreach (var c in contacts.OrderByDescending(x => x.depth))
            {
                string key = scene + "|" + KeyOf(c);
                if (!_present.Add(key)) continue;
                if (!Findings.TryGetValue(key, out var f))
                    Findings[key] = f = new Finding { key = key, scene = scene, part = c.part, obj = PlayerClipDetector.Generic(c.obj), kind = c.kind };
                string was = f.Severity;
                if (c.depth > f.maxDepth) { f.maxDepth = c.depth; f.point = c.point; f.when = when; }
                if (c.depth >= PlayerClipDetector.Limits(c.kind).major) f.run += dt;
                else f.run = 0f;
                f.longest = Mathf.Max(f.longest, f.run);
                f.seenFrame = Time.frameCount;
                if (was != "MAJOR" && f.Severity == "MAJOR") promoted.Add(f);
            }
            foreach (var f in Findings.Values)
                if (!_present.Contains(f.key)) f.run = 0f;
            return promoted;
        }

        public IEnumerable<Finding> Majors => Findings.Values.Where(f => f.Severity == "MAJOR").OrderByDescending(f => f.maxDepth);
        public IEnumerable<Finding> Minors => Findings.Values.Where(f => f.Severity == "MINOR").OrderByDescending(f => f.maxDepth);

        public string Summary(int max = 12) => string.Join("\n", Findings.Values.Where(f => f.Severity != "INFO")
            .OrderByDescending(f => f.Severity == "MAJOR").ThenByDescending(f => f.maxDepth).Take(max).Select(f => f.Line));
    }

    /// <summary>
    /// Chạy PlayerClipDetector mỗi khung sau IK tay/thân. shots: khi một cặp lún sâu hơn ảnh trước ≥ 1 cm (tối đa 2 lần mỗi cặp) chụp
    /// góc nhìn người chơi và hai góc cận chỗ lún vào Logs/shots/playerclip/&lt;cảnh&gt;/. Label: bước đang làm (ghi vào mục lỗi).
    /// </summary>
    [DefaultExecutionOrder(14350)]
    public sealed class PlayerClipProbe : MonoBehaviour
    {
        public readonly PlayerClipDetector Detector = new PlayerClipDetector();
        public readonly PlayerClipTrack Track = new PlayerClipTrack();
        public bool shots;
        public System.Func<string> Label;
        /// <summary>Gọi khi một cặp vừa thành MAJOR (bot ghi lỗi).</summary>
        public System.Action<PlayerClipTrack.Finding> OnMajor;

        readonly List<PlayerClipDetector.Contact> _contacts = new List<PlayerClipDetector.Contact>();
        readonly Dictionary<string, int> _shotCount = new Dictionary<string, int>();
        Camera _camera;
        RenderTexture _rt;
        Texture2D _tex;
        const int ShotW = 640, ShotH = 400;

        public static PlayerClipProbe Attach(bool shots = false)
        {
            var go = new GameObject("PlayerClipProbe");
            DontDestroyOnLoad(go);
            var p = go.AddComponent<PlayerClipProbe>();
            p.shots = shots;
            return p;
        }

        public static string ShotFolder(string scene) => Path.Combine(ScreenshotCapture.Folder, "playerclip", scene);

        void LateUpdate()
        {
            _contacts.Clear();
            Detector.Sample(_contacts);
            string scene = SceneManager.GetActiveScene().name;
            foreach (var f in Track.Feed(scene, Label != null ? Label() : $"{Time.time:F1} s", _contacts, Time.deltaTime))
                OnMajor?.Invoke(f);
            if (!shots || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            foreach (var c in _contacts.OrderByDescending(x => x.depth))
            {
                var f = Track.Findings[scene + "|" + PlayerClipTrack.KeyOf(c)];
                if (f.Severity == "INFO" || c.depth < f.maxDepth - 1e-4f || c.depth < f.shotDepth + 0.01f) continue;
                _shotCount.TryGetValue(f.key, out int n);
                if (n >= 2) continue;
                _shotCount[f.key] = n + 1;
                f.shotDepth = c.depth;
                f.shot = Shoot(c, f);
            }
        }

        string Shoot(PlayerClipDetector.Contact c, PlayerClipTrack.Finding f)
        {
            var rig = PlayerRig.Current;
            if (rig == null || rig.cam == null) return null;
            if (_camera == null)
            {
                _camera = new GameObject("PlayerClipCamera").AddComponent<Camera>();
                DontDestroyOnLoad(_camera.gameObject);
                _camera.CopyFrom(rig.cam);
                _camera.enabled = false;
                var source = rig.cam.GetUniversalAdditionalCameraData();
                var data = _camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = source.renderPostProcessing;
                data.volumeLayerMask = source.volumeLayerMask;
                _rt = new RenderTexture(ShotW, ShotH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                _tex = new Texture2D(ShotW * 3, ShotH, TextureFormat.RGB24, false);
            }
            var root = rig.transform;
            var views = new (Vector3 look, Vector3 eye, float fov)[]
            {
                (rig.cam.transform.position + rig.cam.transform.forward, rig.cam.transform.position, rig.cam.fieldOfView),
                (c.point, c.point + root.right * 0.8f + root.forward * 0.25f + Vector3.up * 0.2f, 45f),
                (c.point, c.point - root.right * 0.8f + root.forward * 0.25f + Vector3.up * 0.2f, 45f),
            };
            for (int v = 0; v < views.Length; v++)
            {
                var (look, eye, fov) = views[v];
                _camera.fieldOfView = fov;
                _camera.nearClipPlane = v == 0 ? rig.cam.nearClipPlane : 0.03f;
                _camera.transform.position = eye;
                if (v == 0) _camera.transform.rotation = rig.cam.transform.rotation;
                else _camera.transform.LookAt(look);
                RenderPipeline.SubmitRenderRequest(_camera, new RenderPipeline.StandardRequest { destination = _rt });
                var prev = RenderTexture.active;
                RenderTexture.active = _rt;
                _tex.ReadPixels(new Rect(0, 0, ShotW, ShotH), v * ShotW, 0);
                RenderTexture.active = prev;
            }
            _tex.Apply();
            var folder = ShotFolder(f.scene);
            Directory.CreateDirectory(folder);
            string name = Slug($"{f.kind}_{f.part}_{f.obj}") + ".jpg";
            File.WriteAllBytes(Path.Combine(folder, name), _tex.EncodeToJPG(85));
            return "playerclip/" + f.scene + "/" + name;
        }

        static string Slug(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s.Normalize(NormalizationForm.FormD))
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                char k = ch == 'đ' ? 'd' : ch == 'Đ' ? 'D' : ch;
                sb.Append(char.IsLetterOrDigit(k) && k < 128 ? k : '_');
            }
            var text = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "_+", "_").Trim('_');
            return text.Length > 110 ? text.Substring(0, 110) : text;
        }

        /// <summary>Báo cáo Markdown vào Logs/qa/player_clip_&lt;nhãn&gt;.md, trả về đường dẫn.</summary>
        public string WriteReport(string label)
        {
            var sb = new StringBuilder($"# Xuyên vật của người chơi — {label}\n\n");
            sb.AppendLine($"Mẫu {Track.Samples}; mesh cảnh đo được {Detector.WorldCount}, bỏ qua vì không đọc được {Detector.Unreadable}; " +
                          $"tay thân đo được: {(Detector.ArmsMeasured ? "có" : "KHÔNG")}.\n");
            sb.AppendLine("Ngưỡng (lún sâu nhất / quá ngưỡng major liền ≥ 0,2 s là MAJOR): " + string.Join("; ",
                System.Enum.GetValues(typeof(PlayerClipDetector.Kind)).Cast<PlayerClipDetector.Kind>().Select(k =>
                    $"{PlayerClipDetector.KindText(k)} {PlayerClipDetector.Limits(k).minor * 100f:F1}/{PlayerClipDetector.Limits(k).major * 100f:F1} cm")) + ".\n");
            sb.AppendLine("| Mức | Cảnh | Loại | Phần của Ba ↔ vật | Lún sâu nhất | Liền dài nhất | Lúc | Ảnh |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var f in Track.Findings.Values.Where(f => f.Severity != "INFO").OrderByDescending(f => f.Severity == "MAJOR").ThenByDescending(f => f.maxDepth))
                sb.AppendLine($"| {f.Severity} | {f.scene} | {PlayerClipDetector.KindText(f.kind)} | {f.part} ↔ {f.obj} | {f.maxDepth * 100f:F1} cm | {f.longest:F2} s | {f.when} | " +
                              (f.shot != null ? $"[ảnh](../shots/{f.shot})" : "") + " |");
            sb.AppendLine("\n## Dưới ngưỡng minor, lún ≥ 0,5 cm (để soát)\n");
            foreach (var f in Track.Findings.Values.Where(f => f.Severity == "INFO" && f.maxDepth >= 0.005f).OrderByDescending(f => f.maxDepth).Take(40))
                sb.AppendLine($"- {f.scene}: {PlayerClipDetector.KindText(f.kind)} — {f.part} ↔ {f.obj}: {f.maxDepth * 100f:F1} cm ({f.when})");
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Logs", "qa", "player_clip_" + Slug(label) + ".md"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        void OnDestroy()
        {
            Detector.Dispose();
            if (_camera != null) Destroy(_camera.gameObject);
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            if (_tex != null) Destroy(_tex);
        }
    }
}
