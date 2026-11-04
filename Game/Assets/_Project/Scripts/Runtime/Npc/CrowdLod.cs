using System.Collections.Generic;
using HaiBanTay.Core;
using HaiBanTay.Performance;
using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>
    /// LOD cho đám đông dưới một Transform gốc (PLAN: Đám đông và hiệu năng). Chỉ tối đa maxAnimated NPC chạy Animator, chọn
    /// theo khoảng cách tới camera và có trong frustum (CrowdLodLogic). Số còn lại đóng băng ở một tư thế bake sẵn
    /// (SkinnedMeshRenderer.BakeMesh một lần cho mỗi mesh nguồn và tư thế, dùng chung mọi NPC cùng prefab), hiện bằng
    /// MeshRenderer tĩnh, tắt Animator và skinned renderer. Ngoài staticRadius thì ẩn hẳn (trừ NPC đánh dấu bóng dáng).
    /// NPC vẫn chạy routine ở mọi chế độ nên vị trí luôn đúng. Cập nhật ~4 lần/giây, mọi tham chiếu cache lúc khởi tạo.
    /// Tầng Laptop: ít NPC chạy Animator hơn và bỏ bớt NPC đứng thừa (CrowdRoutine.optional).
    /// </summary>
    public sealed class CrowdLod : MonoBehaviour
    {
        /// <summary>Trạng thái Animator dùng làm tư thế bake; CrowdLodLogic.PoseOf ánh xạ trạng thái bất kỳ về một trong số này.</summary>
        public static readonly string[] PoseStates = { "Idle", "Walk", "CarryWalk", "HoldCarry", "SitIdle", "WorkTable", "ShoulderCarry", "BagWalk", "Squat", "LeanRail" };
        static readonly float[] PoseTimes = { 0f, 0.25f, 0.25f, 0f, 0.5f, 0.3f, 0.25f, 0.25f, 0.5f, 0.5f };

        public int maxAnimatedPc = 16;
        public int maxAnimatedLaptop = 10;
        /// <summary>Chỉ NPC trong bán kính này (m) mới được chạy Animator.</summary>
        public float animatedRadius = 25f;
        /// <summary>Ngoài bán kính này NPC bị ẩn hẳn.</summary>
        public float staticRadius = 80f;
        /// <summary>NPC đánh dấu bóng dáng (CrowdRoutine.silhouette) giữ mesh tĩnh tới bán kính này.</summary>
        public float silhouetteRadius = 160f;
        public float tickSeconds = 0.25f;
        /// <summary>Độ trễ ở ranh giới tĩnh/ẩn (m).</summary>
        public float boundaryMargin = 3f;
        /// <summary>Một NPC đổi chế độ rồi thì ít nhất chừng này giây sau mới đổi lại (chống nhấp nháy); hạ khỏi Animator không phải chờ.</summary>
        public float minSeconds = 1.5f;
        /// <summary>Tầng Laptop giữ lại tỉ lệ này của các NPC đứng thừa (optional).</summary>
        [Range(0f, 1f)] public float laptopOptionalKeep = 0.5f;
        /// <summary>Ép tầng chất lượng (test); null thì theo GameRoot.Quality.</summary>
        public QualityTier? tierOverride;

        sealed class Member
        {
            public NpcAgent agent;
            public CrowdRoutine routine;
            public Animator animator;
            public SkinnedMeshRenderer[] skins;
            public MeshRenderer[] statics;
            public MeshFilter[] filters;
            public LODGroup lodGroup;
            public LOD[] skinnedLods;
            public LOD[] staticLods;
            public CrowdLodMode mode = CrowdLodMode.Animated;
            public float since = -999f;
            public int pose = -1;
            public bool bakeFailed;
        }

        // Tư thế bake theo mesh nguồn: dùng chung mọi NPC cùng prefab.
        static readonly Dictionary<Mesh, Mesh[]> Bank = new Dictionary<Mesh, Mesh[]>();
        static int Instances;

        readonly List<Member> _members = new List<Member>();
        readonly List<CrowdLodCandidate> _candidates = new List<CrowdLodCandidate>();
        readonly Plane[] _planes = new Plane[6];
        MaterialPropertyBlock _block;
        bool[] _chosen = new bool[0];
        Camera _camera;
        QualityTier _tier = QualityTier.PC;
        QualityTierManager _quality;
        float _timer;
        bool _started;

        public int MemberCount => _members.Count;
        public int AnimatedCount { get; private set; }
        public int StaticCount { get; private set; }
        public int HiddenCount { get; private set; }
        /// <summary>Tổng số lần một NPC đổi chế độ (chẩn đoán nhấp nháy).</summary>
        public int Switches { get; private set; }
        /// <summary>Tổng thời gian (ms) và số mesh đã bake của đám đông này (chẩn đoán chi phí bake).</summary>
        public float BakeMilliseconds { get; private set; }
        public int BakedMeshes { get; private set; }

        public QualityTier Tier => tierOverride ?? _tier;
        public int MaxAnimated => Tier == QualityTier.Laptop ? maxAnimatedLaptop : maxAnimatedPc;

        void Start()
        {
            _quality = GameRoot.Instance != null ? GameRoot.Instance.Quality : null;
            if (_quality != null)
            {
                _tier = _quality.Current;
                _quality.TierChanged += OnTierChanged;
            }
            foreach (var agent in GetComponentsInChildren<NpcAgent>(true))
                Register(agent);
            _started = true;
            ApplyDensity();
            Tick();
        }

        void Awake()
        {
            Instances++;
        }

        void OnDestroy()
        {
            if (_quality != null)
                _quality.TierChanged -= OnTierChanged;
            // Hết đám đông thì bỏ các tư thế bake (khóa là mesh nguồn của cảnh vừa dỡ).
            if (--Instances <= 0)
            {
                foreach (var poses in Bank.Values)
                    foreach (var mesh in poses)
                        if (mesh != null)
                            Destroy(mesh);
                Bank.Clear();
                Instances = 0;
            }
        }

        /// <summary>Thêm một NPC vào LOD (NPC dựng lúc chạy). NPC không có skinned mesh thì bỏ qua.</summary>
        public void Register(NpcAgent agent)
        {
            var skins = agent.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var animator = agent.GetComponentInChildren<Animator>(true);
            if (skins.Length == 0 || animator == null)
                return;
            var group = agent.GetComponentInChildren<LODGroup>(true);
            _members.Add(new Member
            {
                agent = agent,
                routine = agent.GetComponent<CrowdRoutine>(),
                animator = animator,
                skins = skins,
                lodGroup = group,
                skinnedLods = group != null ? group.GetLODs() : null,
            });
            _chosen = new bool[_members.Count];
        }

        void OnTierChanged(QualityTier tier)
        {
            _tier = tier;
            if (_started)
                ApplyDensity();
        }

        /// <summary>Đổi tầng bằng tay (test).</summary>
        public void ForceTier(QualityTier? tier)
        {
            tierOverride = tier;
            if (_started)
                ApplyDensity();
        }

        /// <summary>Tầng Laptop bỏ bớt các NPC đứng thừa; tầng PC dùng đủ.</summary>
        void ApplyDensity()
        {
            foreach (var m in _members)
            {
                if (m.routine == null || !m.routine.optional)
                    continue;
                bool keep = Tier != QualityTier.Laptop || CrowdHash.Unit(m.routine.seed, 99) < laptopOptionalKeep;
                if (m.agent.gameObject.activeSelf != keep)
                    m.agent.gameObject.SetActive(keep);
            }
        }

        void Update()
        {
            // Người vừa được giữ chạy Animator (bắt đầu làm mẫu bằng tay thật) thì bật ngay, không đợi nhịp tick: không có
            // khung nào hiện mesh tĩnh tay buông thõng trong lúc tay thật đang cầm đồ.
            foreach (var m in _members)
                if (m.agent.keepAnimated && m.mode != CrowdLodMode.Animated && m.agent.isActiveAndEnabled)
                    Apply(m, CrowdLodMode.Animated, Time.time);
            _timer += Time.unscaledDeltaTime;
            if (_timer < tickSeconds)
                return;
            _timer = 0f;
            Tick();
        }

        void Tick()
        {
            if (_camera == null || !_camera.isActiveAndEnabled)
                _camera = Camera.main;
            if (_camera == null || _members.Count == 0)
                return;
            GeometryUtility.CalculateFrustumPlanes(_camera, _planes);
            var eye = _camera.transform.position;

            _candidates.Clear();
            foreach (var m in _members)
            {
                var c = new CrowdLodCandidate { Distance = float.MaxValue };
                if (m.agent.isActiveAndEnabled)
                {
                    var center = m.agent.Position + Vector3.up * 0.9f;
                    c.Distance = Vector3.Distance(center, eye);
                    c.WasAnimated = m.mode == CrowdLodMode.Animated;
                    c.Locked = Time.time - m.since < minSeconds;
                    // Người đang chạy Animator dùng hình cầu rộng hơn để rung camera ở mép màn hình không làm đổi chế độ.
                    c.InFrustum = CrowdLodLogic.InFrustum(_planes, center, c.WasAnimated ? 2.4f : 1.2f);
                }
                _candidates.Add(c);
            }
            CrowdLodLogic.SelectAnimated(_candidates, MaxAnimated, animatedRadius, _chosen);

            // Thời gian game: tạm dừng thì đứng yên; máy chậm không làm đổi chế độ dày hơn theo khung.
            float now = Time.time;
            int animated = 0, statics = 0, hidden = 0;
            for (int i = 0; i < _members.Count; i++)
            {
                var m = _members[i];
                // NPC đang bị tắt (tầng Laptop bỏ bớt): không đếm, không đổi chế độ.
                if (!m.agent.isActiveAndEnabled)
                    continue;
                var want = _chosen[i] || m.agent.keepAnimated
                    ? CrowdLodMode.Animated
                    : CrowdLodLogic.Classify(_candidates[i].Distance, m.mode, staticRadius, silhouetteRadius,
                        m.routine != null && m.routine.silhouette, boundaryMargin);
                // Hạ khỏi Animator thì làm ngay (giữ chặt giới hạn maxAnimated); các đổi chế độ còn lại chờ minSeconds.
                if (want != m.mode && (m.mode == CrowdLodMode.Animated || now - m.since >= minSeconds || m.agent.keepAnimated))
                    Apply(m, want, now);
                if (m.mode == CrowdLodMode.Static)
                {
                    int pose = CrowdLodLogic.PoseOf(m.agent.State);
                    if (pose != m.pose)
                        SetPose(m, pose);
                }
                switch (m.mode)
                {
                    case CrowdLodMode.Animated: animated++; break;
                    case CrowdLodMode.Static: statics++; break;
                    default: hidden++; break;
                }
            }
            AnimatedCount = animated;
            StaticCount = statics;
            HiddenCount = hidden;
        }

        void Apply(Member m, CrowdLodMode mode, float now)
        {
            switch (mode)
            {
                case CrowdLodMode.Animated:
                    m.agent.SetAnimated(true);
                    SetRenderers(m, skins: true, statics: false);
                    break;
                case CrowdLodMode.Static:
                    if (!Bake(m))
                    {
                        // Không bake được (mesh không đọc được): giữ nguyên, không hạ cấp.
                        m.since = now;
                        return;
                    }
                    SetPose(m, CrowdLodLogic.PoseOf(m.agent.State));
                    m.agent.SetAnimated(false);
                    SetRenderers(m, skins: false, statics: true);
                    break;
                default:
                    m.agent.SetAnimated(false);
                    SetRenderers(m, skins: false, statics: false);
                    break;
            }
            m.mode = mode;
            m.since = now;
            Switches++;
        }

        static void SetRenderers(Member m, bool skins, bool statics)
        {
            foreach (var s in m.skins)
                s.enabled = skins;
            if (m.statics != null)
                foreach (var s in m.statics)
                    s.enabled = statics;
            if (m.lodGroup != null)
            {
                // Keep native LOD culling for both representations. Without this, baked LOD0/1/2 overlap.
                if (skins || statics)
                    m.lodGroup.SetLODs(skins ? m.skinnedLods : m.staticLods);
                m.lodGroup.enabled = skins || statics;
            }
        }

        void SetPose(Member m, int pose)
        {
            for (int i = 0; i < m.skins.Length; i++)
            {
                if (Bank.TryGetValue(m.skins[i].sharedMesh, out var poses) && poses[pose] != null)
                    m.filters[i].sharedMesh = poses[pose];
            }
            m.pose = pose;
        }

        /// <summary>Bảo đảm NPC có MeshRenderer tĩnh và đủ bộ tư thế bake. Trả về false khi bake thất bại.</summary>
        bool Bake(Member m)
        {
            if (m.bakeFailed)
                return false;
            if (m.statics == null)
                CreateStatics(m);
            bool complete = true;
            foreach (var smr in m.skins)
                if (!Bank.TryGetValue(smr.sharedMesh, out var poses) || System.Array.IndexOf(poses, null) >= 0)
                    complete = false;
            if (complete)
                return true;

            var animator = m.animator;
            if (animator.runtimeAnimatorController == null)
            {
                m.bakeFailed = true;
                return false;
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var culling = animator.cullingMode;
            bool wasEnabled = animator.enabled;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.enabled = true;
            var wasSkinEnabled = new bool[m.skins.Length];
            for (int i = 0; i < m.skins.Length; i++)
            {
                wasSkinEnabled[i] = m.skins[i].enabled;
                m.skins[i].enabled = true;
            }
            bool ok = true;
            for (int p = 0; p < PoseStates.Length && ok; p++)
            {
                int hash = Animator.StringToHash(PoseStates[p]);
                if (!animator.HasState(0, hash))
                    hash = Animator.StringToHash(PoseStates[0]);
                animator.Play(hash, 0, PoseTimes[p]);
                animator.Update(0f);
                foreach (var smr in m.skins)
                {
                    if (!Bank.TryGetValue(smr.sharedMesh, out var poses))
                        Bank[smr.sharedMesh] = poses = new Mesh[PoseStates.Length];
                    if (poses[p] != null)
                        continue;
                    var mesh = new Mesh { name = smr.sharedMesh.name + "_" + PoseStates[p] };
                    smr.BakeMesh(mesh, true);
                    if (mesh.vertexCount == 0)
                    {
                        Debug.LogWarning($"CrowdLod: BakeMesh trả về mesh rỗng cho {smr.sharedMesh.name}; NPC này không hạ xuống tư thế tĩnh.");
                        Destroy(mesh);
                        ok = false;
                        break;
                    }
                    // Chỉ dùng để vẽ: giải phóng bản sao trên CPU.
                    mesh.UploadMeshData(true);
                    poses[p] = mesh;
                    BakedMeshes++;
                }
            }
            animator.cullingMode = culling;
            animator.enabled = wasEnabled;
            for (int i = 0; i < m.skins.Length; i++)
                m.skins[i].enabled = wasSkinEnabled[i];
            BakeMilliseconds += (float)watch.Elapsed.TotalMilliseconds;
            if (!ok)
            {
                m.bakeFailed = true;
                return false;
            }
            return true;
        }

        /// <summary>Mỗi SkinnedMeshRenderer một MeshRenderer tĩnh cùng vị trí, vật liệu và biến thể màu, đang tắt.</summary>
        void CreateStatics(Member m)
        {
            if (_block == null)
                _block = new MaterialPropertyBlock();
            int n = m.skins.Length;
            m.statics = new MeshRenderer[n];
            m.filters = new MeshFilter[n];
            for (int i = 0; i < n; i++)
            {
                var smr = m.skins[i];
                var go = new GameObject("LodStatic_" + smr.name) { layer = smr.gameObject.layer };
                var t = go.transform;
                t.SetParent(m.agent.transform, false);
                // BakeMesh(useScale: true): đỉnh nằm trong không gian cục bộ có tỉ lệ của SMR, nên đặt cùng tư thế thế giới, tỉ lệ 1.
                t.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = smr.sharedMaterials;
                for (int j = 0; j < smr.sharedMaterials.Length; j++)
                {
                    smr.GetPropertyBlock(_block, j);
                    if (!_block.isEmpty)
                        mr.SetPropertyBlock(_block, j);
                }
                mr.shadowCastingMode = smr.shadowCastingMode;
                mr.receiveShadows = smr.receiveShadows;
                mr.renderingLayerMask = smr.renderingLayerMask;
                mr.lightProbeUsage = smr.lightProbeUsage;
                mr.reflectionProbeUsage = smr.reflectionProbeUsage;
                mr.enabled = false;
                m.statics[i] = mr;
                m.filters[i] = mf;
            }
            if (m.lodGroup != null)
            {
                m.staticLods = new LOD[m.skinnedLods.Length];
                for (int level = 0; level < m.skinnedLods.Length; level++)
                {
                    var source = m.skinnedLods[level];
                    var renderers = new Renderer[source.renderers.Length];
                    for (int i = 0; i < renderers.Length; i++)
                        renderers[i] = m.statics[System.Array.IndexOf(m.skins, source.renderers[i])];
                    m.staticLods[level] = new LOD(source.screenRelativeTransitionHeight, renderers)
                    {
                        fadeTransitionWidth = source.fadeTransitionWidth,
                    };
                }
            }
        }
    }
}
