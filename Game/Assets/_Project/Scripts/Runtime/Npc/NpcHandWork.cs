using System.Collections;
using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>
    /// Tay NPC làm việc với vật thật (W5.3): hai đích IK (NpcArmIK) bám vào chỗ cầm của vật — núm nắp nồi, cán muôi, thành thùng,
    /// quai nồi, cán xẻng — và đổi dần mức bám để vươn tới hay buông vật không giật. Mỗi động tác là một coroutine; routine (đầu
    /// bếp, phụ bếp) chọn vật, đi tới chỗ đứng rồi gọi. Không clip "khuấy", "làm bàn" nào chạy khi tay không chạm vật.
    /// Quy ước chỗ cầm: transform con của vật đặt ở cổ tay, +Z hướng ngón (cổ tay → khớp ngón giữa), +Y mu bàn tay.
    /// </summary>
    [RequireComponent(typeof(NpcAgent))]
    [DefaultExecutionOrder(11000)]
    public sealed class NpcHandWork : MonoBehaviour
    {
        public NpcArmIK arms;
        [Tooltip("Nghiêng thân tới trước (độ) để với qua mặt bếp; xoay xương Spine sau Animator, trước IK tay.")]
        public float lean;
        [Tooltip("Thời gian vươn tay tới vật hoặc buông vật (giây).")]
        public float reachSeconds = 0.4f;

        /// <summary>Ngón nắm cán tròn (muôi, xẻng) và ngón kẹp núm/quai.</summary>
        public static readonly Vector3 FistCurl = new Vector3(70f, 80f, 60f);
        public static readonly Vector3 PinchCurl = new Vector3(45f, 55f, 40f);
        public static readonly Vector3 HoldCurl = new Vector3(35f, 45f, 30f);
        /// <summary>Lòng bàn tay áp phẳng vào thân vật (ngón chỉ hơi cong).</summary>
        public static readonly Vector3 PalmCurl = new Vector3(10f, 16f, 10f);

        NpcAgent _agent;
        Animator _animator;
        Transform _tf, _snapLeft, _snapRight, _carryLeft, _carryRight;

        /// <summary>Động tác đang làm (test, observer đọc): "", "stir", "lid", "pour", "carry", "scoop", "feed".</summary>
        public string Activity { get; private set; } = "";
        /// <summary>Bước nhỏ trong động tác (đo tầm tay theo từng bước): "bend", "thrust", "flip", "lift", "stand"...</summary>
        public string Step { get; private set; } = "";
        public NpcAgent Agent => _agent;

        void Awake()
        {
            _tf = transform;
            _agent = GetComponent<NpcAgent>();
            _animator = GetComponentInChildren<Animator>();
            if (arms == null) arms = GetComponent<NpcArmIK>();
            if (arms == null) arms = gameObject.AddComponent<NpcArmIK>();
            arms.leftWeight = arms.rightWeight = 0f;
            // Tay đang bám vật thật: không thay bằng tư thế tĩnh của CrowdLod; Animator ghi tư thế mỗi khung kể cả khi khuất (nghiêng
            // thân và IK cộng lên tư thế animation, không được cộng dồn lên tư thế khung trước).
            _agent.keepAnimated = true;
            if (_animator != null) _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _snapLeft = new GameObject("HandSnapL").transform;
            _snapRight = new GameObject("HandSnapR").transform;
            _snapLeft.SetParent(_tf, false);
            _snapRight.SetParent(_tf, false);
            _carryLeft = new GameObject("ToolCarryL").transform;
            _carryRight = new GameObject("ToolCarryR").transform;
            _carryLeft.SetParent(_tf, false);
            _carryRight.SetParent(_tf, false);
        }

        /// <summary>Tay đang bám vật (mức bám gần đủ): đích của tay đó; null khi tay theo animation.</summary>
        public Transform Grip(bool left)
        {
            if (arms == null || !arms.enabled) return null;
            float w = left ? arms.leftWeight : arms.rightWeight;
            return w > 0.98f ? (left ? arms.leftTarget : arms.rightTarget) : null;
        }

        /// <summary>Cổ tay thật của tay trái/phải (đo khoảng cách tay–chỗ cầm).</summary>
        public Transform Wrist(bool left) => arms != null ? arms.Hand(left) : null;

        // ---------- Chỗ cầm ----------

        /// <summary>Cổ tay khi lòng bàn tay áp vào contact: ngón theo fingers, mu tay theo back (thế giới).</summary>
        public static void WristAt(Vector3 contact, Vector3 fingers, Vector3 back, out Vector3 position, out Quaternion rotation)
        {
            rotation = Quaternion.LookRotation(fingers, back);
            position = contact - rotation * new Vector3(0f, -0.035f, 0.075f);
        }

        /// <summary>Đặt (hoặc tạo) chỗ cầm tên name là con của owner, theo tư thế thế giới.</summary>
        public static Transform PlaceGrip(Transform owner, string name, Vector3 contact, Vector3 fingers, Vector3 back)
        {
            var grip = owner.Find(name);
            if (grip == null)
            {
                grip = new GameObject(name).transform;
                grip.SetParent(owner, false);
            }
            WristAt(contact, fingers, back, out var p, out var r);
            grip.SetPositionAndRotation(p, r);
            return grip;
        }

        // ---------- Mức bám ----------

        float Weight(bool left) => left ? arms.leftWeight : arms.rightWeight;

        void SetWeight(bool left, float w)
        {
            StopFade();
            if (left) arms.leftWeight = w; else arms.rightWeight = w;
        }

        void SetTarget(bool left, Transform t, Vector3 curl)
        {
            if (left) { arms.leftTarget = t; arms.leftCurl = curl; }
            else { arms.rightTarget = t; arms.rightCurl = curl; }
        }

        IEnumerator Blend(bool left, float to, float seconds)
        {
            float from = Weight(left);
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                SetWeight(left, Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds)));
                yield return null;
            }
            SetWeight(left, to);
        }

        IEnumerator BlendBoth(float to, float seconds)
        {
            StopFade();
            float fromL = arms.leftWeight, fromR = arms.rightWeight;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / seconds);
                arms.leftWeight = Mathf.Lerp(fromL, to, k);
                arms.rightWeight = Mathf.Lerp(fromR, to, k);
                yield return null;
            }
            arms.leftWeight = arms.rightWeight = to;
        }

        /// <summary>Vươn tay tới chỗ cầm (đích đi theo vật).</summary>
        public IEnumerator Reach(bool left, Transform grip, Vector3 curl)
        {
            if (Weight(left) > 0.01f) yield return Snap(left);
            SetTarget(left, grip, curl);
            yield return Blend(left, 1f, reachSeconds);
        }

        /// <summary>Vươn cả hai tay cùng lúc.</summary>
        public IEnumerator ReachBoth(Transform leftGrip, Transform rightGrip, Vector3 curl)
        {
            SetTarget(true, leftGrip, curl);
            SetTarget(false, rightGrip, curl);
            yield return BlendBoth(1f, reachSeconds);
        }

        /// <summary>Buông tay: đích đứng yên ở chỗ vừa cầm (không bám theo vật nữa) rồi tay về animation.</summary>
        public IEnumerator Release(bool left, float seconds = -1f)
        {
            yield return Snap(left);
            yield return Blend(left, 0f, seconds < 0f ? reachSeconds : seconds);
        }

        public IEnumerator ReleaseBoth(float seconds = -1f)
        {
            yield return Snap(true);
            yield return Snap(false);
            yield return BlendBoth(0f, seconds < 0f ? reachSeconds : seconds);
            if (_elbowHinted)
            {
                arms.elbowHint = _restElbowHint;
                _elbowHinted = false;
            }
        }

        /// <summary>Đích tạm đứng yên (theo thân NPC) ở tư thế đích hiện tại.</summary>
        IEnumerator Snap(bool left)
        {
            var current = left ? arms.leftTarget : arms.rightTarget;
            var snap = left ? _snapLeft : _snapRight;
            if (current != null && current != snap)
            {
                snap.SetPositionAndRotation(current.position, current.rotation);
                if (left) arms.leftTarget = snap; else arms.rightTarget = snap;
            }
            yield break;
        }

        // ---------- Dụng cụ cầm tay (muôi, xẻng) ----------

        /// <summary>Dụng cụ đang trong tay: mang theo người (tay bám chỗ cầm) hay con của xương bàn tay.</summary>
        public bool InHand(Transform tool, bool left) =>
            tool.parent == (left ? _carryLeft : _carryRight) || (arms.Hand(left) != null && tool.parent == arms.Hand(left));

        /// <summary>
        /// Cầm dụng cụ để thao tác: đang trong tay thì tách khỏi xương tay (giữ tư thế, tay bám chỗ cầm, không giật); đang để ở chỗ khác
        /// thì vươn tay tới chỗ cầm. Sau đó dụng cụ ở không gian thế giới, tay theo dụng cụ.
        /// </summary>
        public IEnumerator TakeTool(Transform tool, Transform grip, bool left, Vector3 curl)
        {
            if (InHand(tool, left))
            {
                tool.SetParent(_tf.parent, true);
                SetTarget(left, grip, curl);
                SetWeight(left, 1f);
                yield break;
            }
            yield return Reach(left, grip, curl);
            tool.SetParent(_tf.parent, true);
        }

        /// <summary>Cổ tay khi mang dụng cụ một tay (hệ NPC, tay phải; tay trái lật x): cẳng tay đưa ra trước, cổ tay trước hông.</summary>
        public static readonly Vector3 ToolCarryWrist = new Vector3(0.24f, 1.0f, 0.24f);

        /// <summary>
        /// Tư thế dụng cụ (hệ NPC) khi mang một tay: cán (+Y của dụng cụ) gần dựng đứng, đầu dụng cụ ở dưới hơi ra trước, mu tay
        /// hướng ra ngoài, cổ tay ở ToolCarryWrist. Tính từ chỗ cầm của chính dụng cụ.
        /// </summary>
        public static void ToolCarryLocal(Transform tool, Transform grip, bool left, out Vector3 position, out Quaternion rotation)
        {
            float side = left ? -1f : 1f;
            var gripRot = Quaternion.Inverse(tool.rotation) * grip.rotation;
            var gripPos = Quaternion.Inverse(tool.rotation) * (grip.position - tool.position);
            var handle = Quaternion.Inverse(gripRot) * Vector3.up;
            var hand = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.up, handle), handle);
            var want = new Vector3(0f, 1f, -0.15f).normalized;
            var wanted = Quaternion.LookRotation(Vector3.ProjectOnPlane(new Vector3(side, 0f, 0f), want), want);
            rotation = wanted * Quaternion.Inverse(hand) * Quaternion.Inverse(gripRot);
            position = new Vector3(side * ToolCarryWrist.x, ToolCarryWrist.y, ToolCarryWrist.z) - rotation * gripPos;
        }

        /// <summary>
        /// Mang dụng cụ theo người khi đi: dụng cụ về tư thế mang trước hông (theo thân), tay vẫn bám chỗ cầm. Thả tay về animation
        /// đi thì muôi vung theo tay tới 6 m/s ở đầu muôi (đo giật 14/10).
        /// </summary>
        public IEnumerator HoldTool(Transform tool, Transform grip, bool left, float seconds = 0.5f)
        {
            if (arms.Hand(left) == null) yield break;
            SetTarget(left, grip, left ? arms.leftCurl : arms.rightCurl);
            tool.SetParent(left ? _carryLeft : _carryRight, true);
            ToolCarryLocal(tool, grip, left, out var p, out var r);
            if (Weight(left) < 0.99f) StartCoroutine(Blend(left, 1f, seconds));
            yield return MoveLocal(tool, p, r, seconds);
        }

        /// <summary>Đặt dụng cụ xuống chỗ để (tay đưa dụng cụ tới rồi buông).</summary>
        public IEnumerator PutTool(Transform tool, Vector3 position, Quaternion rotation, Transform parent, bool left)
        {
            yield return Move(tool, position, rotation, 0.5f);
            tool.SetParent(parent, true);
            yield return Release(left);
        }

        // ---------- Nồi: nắp, khuấy ----------

        /// <summary>Tâm miệng nồi (x, z của thân), cao độ miệng và bán kính lòng nồi, đo từ hình thân nồi.</summary>
        public static void MeasurePot(Transform pot, out Vector3 center, out float rim, out float radius)
        {
            var body = pot.Find("Body");
            var r = body != null ? body.GetComponent<Renderer>() : pot.GetComponentInChildren<Renderer>();
            var b = r != null ? r.bounds : new Bounds(pot.position + Vector3.up * 0.14f, new Vector3(0.34f, 0.27f, 0.34f));
            center = new Vector3(b.center.x, b.max.y, b.center.z);
            rim = b.max.y;
            radius = Mathf.Min(b.extents.x, b.extents.z) * 0.88f;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        /// <summary>
        /// Khuấy nồi như thật: tay trái cầm núm nhấc nắp ra cạnh trái, tay phải đưa muôi vào lòng nồi khuấy vòng (cán nghiêng về
        /// phía người, muôi luôn trong lòng nồi), nhấc muôi gõ nhẹ vào miệng nồi, đậy nắp lại. Muôi xong việc vẫn trong tay phải.
        /// </summary>
        public IEnumerator Stir(Transform pot, Transform ladle, Transform ladleGrip, float seconds, System.Action<float> onStir = null)
        {
            Activity = "lid";
            MeasurePot(pot, out var center, out float rim, out float radius);
            var away = Flat(center - _tf.position);
            var right = Vector3.Cross(Vector3.up, away);
            var lid = pot.Find("MealLid");
            var knob = pot.Find("LidKnob");
            bool lidded = lid != null && knob != null && lid.gameObject.activeInHierarchy && knob.gameObject.activeInHierarchy;
            Transform pivot = null;
            Quaternion pivotRot = default;
            Vector3 pivotPos = default;
            if (lidded)
            {
                _lid = lid; _knob = knob; _lidPot = pot;
                _lidPos = lid.localPosition; _lidRot = lid.localRotation; _knobPos = knob.localPosition; _knobRot = knob.localRotation;
                pivot = _pivot = new GameObject("LidPivot").transform;
                pivot.SetParent(pot, true);
                pivotPos = knob.position;
                pivotRot = Quaternion.LookRotation(away, Vector3.up);
                pivot.SetPositionAndRotation(pivotPos, pivotRot);
                lid.SetParent(pivot, true);
                knob.SetParent(pivot, true);
                var fingers = (away * 0.45f + Vector3.down * 0.9f).normalized;
                var grip = PlaceGrip(pivot, "KnobGrip", knob.position + Vector3.up * 0.012f, fingers, Vector3.Cross(right, fingers));
                yield return Reach(true, grip, PinchCurl);
                // Nhấc thẳng lên cho khỏi miệng nồi, rồi đưa sang trái, nghiêng mặt nắp ra ngoài (hơi nước không phả vào người).
                yield return Move(pivot, pivotPos + Vector3.up * 0.09f, pivotRot, 0.25f);
                // Nắp giữ ở phía trước bên trái, gần người (trong tầm tay trái khi đứng sát lò).
                yield return Move(pivot, center + Vector3.up * 0.16f - away * 0.22f - right * (radius + 0.08f), Quaternion.AngleAxis(70f, away) * pivotRot, 0.45f);
            }

            Activity = "stir";
            Step = "enter";
            yield return TakeTool(ladle, ladleGrip, false, FistCurl);
            float depth = rim - 0.13f;
            Vector3 Bowl(float a) => new Vector3(center.x + Mathf.Cos(a) * radius * 0.45f, depth, center.z + Mathf.Sin(a) * radius * 0.45f);
            Quaternion Lean(Vector3 bowl)
            {
                // Cán nghiêng về phía người và ngược hướng muôi lệch khỏi tâm: tay quay vòng nhỏ, muôi quét vòng to trong lòng nồi.
                var offset = bowl - new Vector3(center.x, bowl.y, center.z);
                var up = (Vector3.up - away * 0.36f - offset * 0.8f).normalized;
                return Quaternion.LookRotation(Vector3.ProjectOnPlane(away, up), up);
            }
            float angle = Mathf.Atan2(-away.z, -away.x);
            var entry = Bowl(angle);
            yield return Move(ladle, entry + Vector3.up * (rim + 0.1f - depth), Lean(entry), 0.35f);
            yield return Move(ladle, entry, Lean(entry), 0.3f);
            Step = "circle";
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                // Khoảng 0,7 vòng mỗi giây, chậm dần rồi nhanh lại như người khuấy thật.
                angle += Time.deltaTime * Mathf.PI * 2f * (0.55f + 0.2f * Mathf.Sin(t * 1.3f));
                var bowl = Bowl(angle);
                ladle.SetPositionAndRotation(bowl, Lean(bowl));
                onStir?.Invoke(t);
                yield return null;
            }
            // Nhấc muôi lên, gõ nhẹ hai lần vào miệng nồi cho rơi nước.
            Step = "tap";
            var lip = new Vector3(center.x, rim + 0.05f, center.z) - away * radius * 0.4f;
            var tilt = Quaternion.LookRotation(away, (Vector3.up - away * 0.5f).normalized);
            yield return Move(ladle, lip, tilt, 0.35f);
            for (int i = 0; i < 2; i++)
            {
                yield return Move(ladle, lip + Vector3.down * 0.03f, tilt, 0.08f);
                yield return Move(ladle, lip, tilt, 0.1f);
            }

            if (lidded)
            {
                Activity = "lid";
                yield return Move(pivot, pivotPos + Vector3.up * 0.09f, pivotRot, 0.45f);
                yield return Move(pivot, pivotPos, pivotRot, 0.2f);
                yield return Release(true);
                PutLidBack();
            }
            Activity = "";
            Step = "";
        }

        Transform _lid, _knob, _lidPot, _pivot;
        Vector3 _lidPos, _knobPos;
        Quaternion _lidRot, _knobRot;

        void PutLidBack()
        {
            if (_lid != null && _lidPot != null)
            {
                _lid.SetParent(_lidPot, false);
                _lid.SetLocalPositionAndRotation(_lidPos, _lidRot);
            }
            if (_knob != null && _lidPot != null)
            {
                _knob.SetParent(_lidPot, false);
                _knob.SetLocalPositionAndRotation(_knobPos, _knobRot);
            }
            if (_pivot != null) Destroy(_pivot.gameObject);
            _lid = _knob = _lidPot = _pivot = null;
        }

        /// <summary>Đặt dụng cụ vào tư thế mang và tay bám chỗ cầm ngay: lúc bắt đầu cảnh, hay khi bị dừng giữa chừng.</summary>
        public void SnapToolToHand(Transform tool, Transform grip, bool left, Vector3 curl)
        {
            if (arms == null || arms.Hand(left) == null) return;
            ToolCarryLocal(tool, grip, left, out var p, out var r);
            tool.SetParent(left ? _carryLeft : _carryRight, false);
            tool.SetLocalPositionAndRotation(p, r);
            SetTarget(left, grip, curl);
            SetWeight(left, 1f);
        }

        /// <summary>
        /// Tay và thân về animation sau Abort trong chừng này giây (không bật về một khung). 0,25 s thì tay đang với đích cách 72 cm
        /// (mức bám 0,66) vẫn vụt 2,3 cm/khung 60 Hz.
        /// </summary>
        const float AbortFade = 0.6f;

        Coroutine _fade;

        /// <summary>
        /// Dừng mọi động tác (routine bị tắt giữa chừng: lượt phục vụ, dạy gọt): nắp về miệng nồi, clip chạy lại; thân thẳng và hai tay về
        /// animation trong AbortFade giây. Đặt về 0 ngay thì cổ tay nhảy 15 cm một khung (KitchenHands bê nồi 18/10: phụ bếp đang với tay
        /// dựng xẻng thì bị gọi đi bưng nồi). Động tác mới đặt mức bám/nghiêng thì thôi mờ dần.
        /// </summary>
        public void Abort()
        {
            PutLidBack();
            if (_animator != null) _animator.speed = 1f;
            if (_follow != null) _follow.tool = null;
            Activity = "";
            Step = "";
            StopFade();
            if (isActiveAndEnabled && arms != null)
                _fade = StartCoroutine(FadeOut());
            else
            {
                lean = 0f;
                if (arms != null) arms.leftWeight = arms.rightWeight = 0f;
            }
        }

        IEnumerator FadeOut()
        {
            float fromL = arms.leftWeight, fromR = arms.rightWeight, fromLean = lean;
            for (float t = 0f; t < AbortFade; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(1f, 0f, t / AbortFade);
                arms.leftWeight = fromL * k;
                arms.rightWeight = fromR * k;
                lean = fromLean * k;
                yield return null;
            }
            arms.leftWeight = arms.rightWeight = 0f;
            lean = 0f;
            _fade = null;
        }

        void StopFade()
        {
            if (_fade == null) return;
            StopCoroutine(_fade);
            _fade = null;
        }

        // ---------- Thùng, nồi bê bằng hai tay ----------

        /// <summary>Bao hình của vật trong hệ tọa độ của chính nó (để đặt chỗ cầm hai bên thành).</summary>
        public static Bounds LocalBounds(Transform item)
        {
            var bounds = new Bounds();
            bool any = false;
            foreach (var r in item.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var m = item.worldToLocalMatrix * r.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }
            return bounds;
        }

        /// <summary>
        /// Chỗ cầm hai bên vật: quai "Handle" nếu có (nồi), không thì giữa hai thành trái/phải (thùng). Lòng bàn tay úp vào thành,
        /// ngón hướng ra trước theo người bê (+Z của vật khi bê).
        /// </summary>
        public static void SideGrips(Transform item, out Transform left, out Transform right)
        {
            Vector3 l = default, r = default;
            bool handles = false;
            int n = 0;
            foreach (Transform child in item)
                if (child.name == "Handle")
                {
                    var local = item.InverseTransformPoint(child.position);
                    if (local.x < 0f) l = local; else r = local;
                    n++;
                }
            handles = n >= 2;
            if (!handles)
            {
                var b = LocalBounds(item);
                l = new Vector3(b.min.x, b.center.y + b.extents.y * 0.45f, b.center.z);
                r = new Vector3(b.max.x, b.center.y + b.extents.y * 0.45f, b.center.z);
            }
            var fwd = item.forward;
            left = PlaceGrip(item, "GripL", item.TransformPoint(l) - item.right * 0.01f, (fwd * 0.6f + item.right * 0.4f).normalized, -item.right);
            right = PlaceGrip(item, "GripR", item.TransformPoint(r) + item.right * 0.01f, (fwd * 0.6f - item.right * 0.4f).normalized, item.right);
        }

        /// <summary>
        /// Chỗ cầm hai quai theo hướng người giữ (vật đứng yên trước mặt, trục quai bất kỳ): quai bên trái người là tay trái,
        /// ngón hướng vào vật theo hướng nhìn của người, mu tay ra ngoài.
        /// </summary>
        public static void HandleGrips(Transform item, Transform holder, out Transform left, out Transform right)
        {
            Transform l = null, r = null;
            float lDot = float.MaxValue, rDot = float.MinValue;
            foreach (Transform child in item)
            {
                if (child.name != "Handle") continue;
                float d = Vector3.Dot(child.position - item.position, holder.right);
                if (d < lDot) { lDot = d; l = child; }
                if (d > rDot) { rDot = d; r = child; }
            }
            var fwd = Flat(holder.forward);
            var side = Vector3.Cross(Vector3.up, fwd);
            if (l == null || r == null || l == r)
            {
                SideGrips(item, out left, out right);
                return;
            }
            left = PlaceGrip(item, "HoldL", l.position - side * 0.01f, (fwd * 0.6f + side * 0.4f).normalized, -side);
            right = PlaceGrip(item, "HoldR", r.position + side * 0.01f, (fwd * 0.6f - side * 0.4f).normalized, side);
        }

        /// <summary>Đứng trước vật, hai tay nắm hai quai (vật không dời theo người).</summary>
        public IEnumerator HoldHandles(Transform item)
        {
            Activity = "hold";
            HandleGrips(item, _tf, out var l, out var r);
            yield return ReachBoth(l, r, HoldCurl);
        }

        /// <summary>
        /// Hai lòng bàn tay áp vào thân vật tròn (nồi cao úp) hai bên phía người đứng, thấp hơn mặt trên drop m, cách nhau spread
        /// độ quanh trục: giữ cho vật khỏi chao. Ngón theo thân vật hướng ra phía xa người, hơi chếch lên; mu tay quay ra ngoài.
        /// top: tâm mặt trên (thế giới), radius: bán kính thân.
        /// </summary>
        public IEnumerator HoldBody(Transform item, Vector3 top, float radius, float drop = 0.22f, float spread = 55f)
        {
            Activity = "hold";
            var outward = Flat(_tf.position - top);
            var right = Vector3.Cross(Vector3.up, -outward);
            Transform l = null, r = null;
            foreach (float s in new[] { -1f, 1f })
            {
                var d = Quaternion.AngleAxis(s * spread, Vector3.up) * outward;
                var tangent = Vector3.Cross(Vector3.up, d);
                if (Vector3.Dot(tangent, -outward) < 0f) tangent = -tangent;
                var fingers = (tangent * 0.85f + Vector3.up * 0.5f).normalized;
                var grip = PlaceGrip(item, s < 0f ? "BodyA" : "BodyB", top - Vector3.up * drop + d * radius, fingers, d);
                if (Vector3.Dot(d, right) > 0f) r = grip; else l = grip;
            }
            yield return ReachBoth(l, r, PalmCurl);
        }

        /// <summary>Thân nghiêng về trước (độ) trong seconds giây.</summary>
        public IEnumerator Lean(float degrees, float seconds = 0.4f)
        {
            StopFade();
            float from = lean;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                lean = Mathf.Lerp(from, degrees, Mathf.SmoothStep(0f, 1f, t / seconds));
                yield return null;
            }
            lean = degrees;
        }

        /// <summary>Buông hai tay, thân thẳng lại.</summary>
        public IEnumerator LetGo()
        {
            if (arms != null && (arms.leftWeight > 0.001f || arms.rightWeight > 0.001f))
                yield return ReleaseBoth();
            if (Mathf.Abs(lean) > 0.01f)
                yield return Lean(0f, 0.3f);
            Activity = "";
        }

        /// <summary>Hai tay bám hai bên vật (vật đứng yên hay đang bê trước bụng đều được).</summary>
        public IEnumerator HoldSides(Transform item)
        {
            Activity = "carry";
            SideGrips(item, out var l, out var r);
            yield return ReachBoth(l, r, HoldCurl);
        }

        /// <summary>
        /// Đổ vật đang bê vào nồi: đưa vật lên trên miệng nồi, nghiêng về trước cho đồ trong vật rơi vào lòng nồi, rồi đưa về tư thế
        /// bê. Hai tay vẫn bám hai bên vật suốt lúc đổ. onPour gọi lúc nghiêng nhiều nhất.
        /// </summary>
        public IEnumerator PourInto(Transform item, Transform pot, System.Action onPour)
        {
            Activity = "pour";
            MeasurePot(pot, out var center, out float rim, out _);
            var away = Flat(center - _tf.position);
            var right = Vector3.Cross(Vector3.up, away);
            var parent = item.parent;
            var carryPos = item.localPosition;
            var carryRot = item.localRotation;
            var local = Vector3.Scale(LocalBounds(item).center, item.lossyScale);
            item.SetParent(_tf.parent, true);
            var level = Quaternion.LookRotation(away, Vector3.up);
            var tilted = Quaternion.AngleAxis(72f, right) * level;
            var over = new Vector3(center.x, rim + 0.24f, center.z) - away * 0.16f;
            yield return Move(item, over - level * local, level, 0.5f);
            yield return Move(item, over - tilted * local, tilted, 0.6f);
            onPour?.Invoke();
            yield return new WaitForSeconds(0.6f);
            yield return Move(item, over - level * local, level, 0.45f);
            item.SetParent(parent, true);
            yield return MoveLocal(item, carryPos, carryRot, 0.4f);
            Activity = "carry";
        }

        // ---------- Xẻng than ----------

        /// <summary>Chỗ hai tay trên cán xẻng (hệ của xẻng: gốc ở gót lưỡi, +Z dọc cán): tay phải cuối cán, tay trái giữa cán.</summary>
        public static readonly Vector3 ShovelRightContact = new Vector3(0f, 0.03f, 0.70f);
        public static readonly Vector3 ShovelLeftContact = new Vector3(0f, 0.03f, 0.42f);
        /// <summary>Giữa lưỡi xẻng (hệ của xẻng).</summary>
        public static readonly Vector3 ShovelBladeMid = new Vector3(0f, 0f, -0.13f);

        // Tư thế theo chỗ hai tay (hệ NPC, đo từ clip: vai cao 1,42 m khi đứng, 1,17 m khi khom ở 28 % clip PickUp; tay dài 0,56 m).
        /// <summary>
        /// Khom gối (PickUp 28 %): tay phải trước hông phải, tay trái thấp phía trước; lưỡi ngang 0,58 m, cách người ~0,9 m. Chỗ nắm cũ
        /// (0,2; 0,95; 0,18) và (0,05; 0,825; 0,38) cho cẳng tay trái ↔ ngực 10–14 cm; sau khi clip dừng đúng 28 % (BendBlend) và khuỷu ra
        /// trước (ShovelElbowHint), tay trái dời (0; +0,07; +0,10) (vai–tay 0,55 m; dời thêm 0,06 sang ngoài thì vai–tay 0,57 m, cổ tay cách chỗ nắm
        /// 6 cm, KitchenHandsPlayTests đỏ), tay phải dời (+0,06; +0,02; +0,15): tay ↔ thân 0 cm, xẻng ↔ thân 5,2 cm chỉ ở đầu lượt khom (bảng
        /// dò ScanLow, ScanLowRight).
        /// </summary>
        public static Vector3 LowRight = new Vector3(0.26f, 0.97f, 0.33f), LowLeft = new Vector3(0.05f, 0.895f, 0.48f);
        /// <summary>
        /// Đứng/đi: xẻng chéo trước người ở ngang ngực, tay phải trước hông phải, tay trái trước ngực (khuỷu còn gập: vai trái–chỗ nắm 0,52 m;
        /// 0,56 m là duỗi hết tầm, lúc đứng dậy vai kéo tay trái lên, đầu xẻng vụt 3,6 cm/khung), lưỡi phía trước bên trái. Chỗ nắm cũ
        /// (0,18; 0,91; −0,067) và (−0,05; 0,99; 0,15) nằm trong bụng/hông: cẳng tay ↔ bụng 8,5 cm, bàn tay ↔ hông 8 cm (bot 19/10; bảng dò
        /// HelperShovelCarryScan: dời (+0,05; +0,12; +0,09) là sạch và vai–tay 0,52 m).
        /// </summary>
        public static Vector3 CarryRight = new Vector3(0.23f, 1.03f, 0.023f), CarryLeft = new Vector3(0f, 1.11f, 0.24f);
        const float BendAt = 0.28f;
        /// <summary>Chuyển sang khom: 0,2 s thì tay vụt về dáng khom (3,7 cm/khung 60 fps), 0,35 s vẫn 2 cm/khung.</summary>
        const float BendFade = 0.5f;

        float _bendBlend = -1f;
        bool _bendFrozen;

        /// <summary>
        /// Thời gian chuyển sang dáng khom tính theo giờ Animator: đúng BendAt × độ dài clip PickUp (1,3 s) để clip dừng đúng dáng BendAt
        /// khi chuyển xong. Chuyển 0,5 s giờ Animator thì clip chạy tới ~0,38 và ngực chúi sâu hơn dáng mà chỗ nắm LowRight/LowLeft tính
        /// cho (QA-0119b).
        /// </summary>
        float BendBlend()
        {
            if (_bendBlend < 0f)
            {
                _bendBlend = BendFade;
                foreach (var clip in _animator.runtimeAnimatorController.animationClips)
                    if (clip.name == "PickUp")
                    {
                        _bendBlend = Mathf.Min(BendFade, BendAt * clip.length);
                        break;
                    }
            }
            return _bendBlend;
        }

        /// <summary>Animator chạy chậm lại để chuyển ngắn hơn (BendBlend) vẫn mất BendFade giây thực, tay không vụt nhanh hơn.</summary>
        float BendSpeed() => BendBlend() / BendFade;

        /// <summary>
        /// Mỗi khung lúc vào dáng khom: dừng clip ngay khi tới BendAt (true), không đợi xẻng bám kịp. Chạy tốc độ đầy rồi dừng khi xẻng bám kịp
        /// thì clip PickUp (1,3 s) qua BendAt giữa lúc chuyển 0,5 s và dừng ở ~0,45: ngực chúi sâu hơn dáng mà chỗ nắm LowRight/LowLeft
        /// tính cho, xẻng xuyên ngực và đầu 6–10 cm, cẳng tay trái ngực 12 cm (QA-0119b).
        /// </summary>
        bool DriveBend()
        {
            if (_bendFrozen)
                return true;
            var info = _animator.GetCurrentAnimatorStateInfo(0);
            if (!_animator.IsInTransition(0) && info.IsName("PickUp") && info.normalizedTime >= BendAt)
            {
                _animator.speed = 0f;
                _bendFrozen = true;
                return true;
            }
            _animator.speed = BendSpeed();
            return false;
        }

        /// <summary>Tư thế xẻng (cục bộ trong NPC) để tay phải, tay trái ở hai chỗ cho trước (hệ NPC).</summary>
        public static void ShovelLocal(Vector3 right, Vector3 left, out Vector3 position, out Quaternion rotation)
        {
            var d = (right - left).normalized;
            rotation = Quaternion.LookRotation(d, Vector3.ProjectOnPlane(Vector3.up, d).normalized);
            position = right - rotation * ShovelRightContact;
        }

        /// <summary>Giữa lưỡi xẻng ở tư thế khom (hệ NPC) và hướng lưỡi đi vào (hệ NPC, ngang): để routine tính chỗ đứng.</summary>
        public static void LowBlade(out Vector3 bladeMid, out Vector3 thrust)
        {
            ShovelLocal(LowRight, LowLeft, out var p, out var r);
            bladeMid = p + r * ShovelBladeMid;
            thrust = Vector3.ProjectOnPlane(r * Vector3.back, Vector3.up).normalized;
        }

        /// <summary>Giữ thân ở tư thế khom gối của clip PickUp (dừng clip ở BendAt) để tay với tới đống than / miệng lò thấp.</summary>
        public IEnumerator Bend(bool on)
        {
            if (_animator == null) yield break;
            if (on)
            {
                _agent.Play("PickUp", BendBlend());
                _bendFrozen = false;
                for (float t = 0f; t < 1.2f; t += Time.deltaTime)
                {
                    if (DriveBend()) break;
                    yield return null;
                }
                _animator.speed = 0f;
            }
            else
            {
                _agent.Play("Idle", 0.35f);
                // Chuyển trạng thái chạy theo giờ Animator: 0,3 s tăng tốc (0,15 s giờ Animator) + 0,2 s là đủ 0,35 s.
                yield return ResumeClip();
                yield return new WaitForSeconds(0.2f);
            }
        }

        /// <summary>Clip PickUp đang dừng ở dáng khom tăng tốc chạy lại trong chừng này giây.</summary>
        const float ResumeSeconds = 0.3f;

        /// <summary>
        /// Cho Animator đang dừng (speed 0) chạy lại có tăng tốc. Đặt speed = 1 ngay thì clip PickUp chạy tiếp pha nhấc tay với vận
        /// tốc đầy từ đứng yên, hai cổ tay vừa buông thùng vụt 0,6 → 2 cm/khung (verify 15/10).
        /// </summary>
        IEnumerator ResumeClip()
        {
            for (float t = 0f; t < ResumeSeconds; t += Time.deltaTime)
            {
                _animator.speed = Mathf.SmoothStep(0f, 1f, t / ResumeSeconds);
                yield return null;
            }
            _animator.speed = 1f;
        }

        void OnDisable()
        {
            if (_animator != null) _animator.speed = 1f;
            // Tắt giữa lúc mờ dần sau Abort (hết ngày, ẩn diễn viên): coroutine dừng theo, về hẳn animation.
            if (_fade != null)
            {
                _fade = null;
                lean = 0f;
                if (arms != null) arms.leftWeight = arms.rightWeight = 0f;
            }
        }

        Transform _spine;

        void LateUpdate()
        {
            if (lean < 0.01f || _animator == null || !_animator.isHuman) return;
            if (_spine == null) _spine = _animator.GetBoneTransform(HumanBodyBones.Spine);
            if (_spine != null) _spine.rotation = Quaternion.AngleAxis(lean, _tf.right) * _spine.rotation;
        }

        /// <summary>
        /// Đổi dáng (đứng ↔ khom) và dời xẻng tới tư thế mới theo đúng tiến độ của thân (đọc từ Animator: thời điểm clip PickUp và
        /// mức trộn chuyển trạng thái), để hai tay không phải với quá tầm giữa chừng.
        /// </summary>
        IEnumerator BendWith(bool bend, Transform shovel, Vector3 right, Vector3 left, Vector3 shift, float seconds)
        {
            Step = bend ? "bend" : "stand";
            Follow(shovel);
            try
            {
                yield return BendShovel(bend, shovel, right, left, shift, seconds);
            }
            finally
            {
                Follow(null);
            }
        }

        NpcToolFollow _follow;

        /// <summary>Bật/tắt chế độ dụng cụ bám tay (lúc đổi dáng); dụng cụ phải có con GripR, GripL.</summary>
        void Follow(Transform tool)
        {
            if (_follow == null)
            {
                _follow = GetComponent<NpcToolFollow>();
                if (_follow == null) _follow = gameObject.AddComponent<NpcToolFollow>();
                _follow.arms = arms;
            }
            _follow.tool = tool;
            _follow.rightGrip = tool != null ? tool.Find("GripR") : null;
            _follow.leftGrip = tool != null ? tool.Find("GripL") : null;
        }

        IEnumerator BendShovel(bool bend, Transform shovel, Vector3 right, Vector3 left, Vector3 shift, float seconds)
        {
            ShovelLocal(right, left, out var p1, out var r1);
            p1 += shift;
            var p0 = shovel.localPosition;
            var r0 = shovel.localRotation;
            if (_animator == null)
            {
                yield return MoveLocal(shovel, p1, r1, seconds);
                yield break;
            }
            // Tiến độ đọc từ Animator có lúc nhảy cóc (hết chuyển trạng thái thì về 1 ngay): xẻng chỉ theo tiến độ đó với nhịp có trần,
            // không nhảy chỗ (đo giật 14/10).
            const float MaxRate = 5f, Smooth = 0.06f;
            float shown = 0f, rate = 0f;
            if (bend)
            {
                _agent.Play("PickUp", BendBlend());
                _bendFrozen = false;
                for (float t = 0f; t < 1.5f; t += Time.deltaTime)
                {
                    DriveBend();
                    shown = Mathf.Min(1f, Mathf.SmoothDamp(shown, BendProgress(), ref rate, Smooth, MaxRate, Time.deltaTime));
                    shovel.SetLocalPositionAndRotation(Vector3.Lerp(p0, p1, shown), Quaternion.Slerp(r0, r1, shown));
                    if (shown >= 0.995f) break;
                    yield return null;
                }
                _animator.speed = 0f;
            }
            else
            {
                _animator.speed = 1f;
                const float Rise = 0.35f;
                _agent.Play("Idle", Rise);
                // Đứng dậy đúng nhịp chuyển clip (Rise giây), có tăng tốc rồi giảm tốc. Đọc mức trộn của Animator thì cuối chuyển trạng
                // thái nhảy cóc về 1, xẻng giật 5–6 cm (đo giật 60 fps 14/10).
                for (float t = 0f; t < Rise; t += Time.deltaTime)
                {
                    shown = Mathf.SmoothStep(0f, 1f, t / Rise);
                    shovel.SetLocalPositionAndRotation(Vector3.Lerp(p0, p1, shown), Quaternion.Slerp(r0, r1, shown));
                    yield return null;
                }
            }
            shovel.SetLocalPositionAndRotation(p1, r1);
            // Thêm một khung cho NpcToolFollow đặt xẻng theo tay thật rồi mới thôi bám: tắt ngay trong khung này thì xẻng nhảy về đúng
            // tư thế tính sẵn, lệch tay 5–6 cm.
            yield return null;
        }

        /// <summary>Thân đã khom tới đâu (0: đứng, 1: dáng khom BendAt của clip PickUp), tính cả mức trộn lúc chuyển trạng thái.</summary>
        float BendProgress()
        {
            if (_animator.IsInTransition(0))
            {
                var next = _animator.GetNextAnimatorStateInfo(0);
                float w = _animator.GetAnimatorTransitionInfo(0).normalizedTime;
                return next.IsName("PickUp") ? w * Mathf.Clamp01(next.normalizedTime / BendAt) : 0f;
            }
            var info = _animator.GetCurrentAnimatorStateInfo(0);
            return info.IsName("PickUp") ? Mathf.Clamp01(info.normalizedTime / BendAt) : 0f;
        }

        /// <summary>Xẻng về tư thế bê (đứng/đi), xẻng là con của NPC.</summary>
        public IEnumerator CarryShovel(Transform shovel, float seconds = 0.35f)
        {
            shovel.SetParent(_tf, true);
            ShovelLocal(CarryRight, CarryLeft, out var p, out var r);
            yield return MoveLocal(shovel, p, r, seconds);
        }

        /// <summary>Tư thế xẻng nằm yên ở chỗ để (cắm trong đống than) khi NPC đứng ở stand, quay yaw (thế giới).</summary>
        public static void ShovelRest(Vector3 stand, float yaw, out Vector3 position, out Quaternion rotation)
        {
            ShovelLocal(LowRight, LowLeft, out var p, out var r);
            var frame = Quaternion.Euler(0f, yaw, 0f);
            position = stand + frame * (p - (r * Vector3.forward) * 0.04f - Vector3.up * 0.03f);
            rotation = frame * r;
        }

        /// <summary>Cầm xẻng đang cắm trong đống than: khom gối, hai tay nắm cán (tay phải cuối cán, tay trái giữa cán).</summary>
        /// <summary>
        /// Hướng khuỷu (hệ gốc NPC) khi cầm xẻng: ra ngoài và hơi ra trước. Khuỷu mặc định ở sau lưng (z −0,25) làm cẳng tay trái chéo qua ngực lúc
        /// khom và đứng dậy (ngực 10–12 cm; HelperShovelCarryScan.ScanElbow: z +0,25 còn 5,9 cm, chỉ bàn tay phải ↔ ngực).
        /// </summary>
        public static Vector3 ShovelElbowHint = new Vector3(0.75f, 0.85f, 0.25f);
        Vector3 _restElbowHint;
        bool _elbowHinted;

        public IEnumerator PickShovel(Transform shovel, Transform leftGrip, Transform rightGrip)
        {
            Activity = "scoop";
            if (!_elbowHinted)
            {
                _restElbowHint = arms.elbowHint;
                _elbowHinted = true;
            }
            arms.elbowHint = ShovelElbowHint;
            yield return Bend(true);
            SetTarget(true, leftGrip, FistCurl);
            SetTarget(false, rightGrip, FistCurl);
            yield return BendBothWeights();
            shovel.SetParent(_tf, true);
        }

        IEnumerator BendBothWeights() => BlendBoth(1f, reachSeconds);

        /// <summary>Xúc than (đang khom, tay nắm xẻng): đẩy lưỡi sâu vào đống than, nhấc lên có than, đứng dậy bê xẻng.</summary>
        public IEnumerator Scoop(Transform shovel, GameObject load)
        {
            Activity = "scoop";
            shovel.SetParent(_tf, true);
            ShovelLocal(LowRight, LowLeft, out var p, out var r);
            var along = r * Vector3.forward;
            Step = "ready";
            yield return MoveLocal(shovel, p + along * 0.12f + Vector3.up * 0.05f, r, 0.3f);
            Step = "thrust";
            yield return MoveLocal(shovel, p, r, 0.3f);
            if (load != null) load.SetActive(true);
            Step = "lift";
            // Nhấc: lưỡi ngửa lên chút (quay quanh gót lưỡi), cả xẻng lên 10 cm cho than khỏi rơi.
            yield return MoveLocal(shovel, p + Vector3.up * 0.1f + along * 0.06f, r * Quaternion.AngleAxis(12f, Vector3.right), 0.35f);
            Activity = "carry";
            yield return BendWith(false, shovel, CarryRight, CarryLeft, Vector3.zero, 0.45f);
            Step = "carry";
        }

        /// <summary>
        /// Nạp than (NPC đã đứng ở chỗ do LowBlade tính, lưỡi hướng vào cửa lò): khom gối đưa lưỡi tới cửa, đẩy vào buồng đốt, lật
        /// lưỡi đổ than, rút ra, đứng dậy bê xẻng.
        /// </summary>
        public IEnumerator Feed(Transform shovel, GameObject load)
        {
            Activity = "feed";
            shovel.SetParent(_tf, true);
            ShovelLocal(LowRight, LowLeft, out var p, out var r);
            var along = r * Vector3.forward;
            yield return BendWith(true, shovel, LowRight, LowLeft, along * 0.15f, 0.5f);
            Step = "thrust";
            yield return MoveLocal(shovel, p, r, 0.3f);
            Step = "flip";
            yield return MoveLocal(shovel, p, r * Quaternion.AngleAxis(110f, Vector3.forward), 0.25f);
            if (load != null) load.SetActive(false);
            Step = "withdraw";
            yield return MoveLocal(shovel, p + along * 0.15f, r, 0.3f);
            Activity = "carry";
            yield return BendWith(false, shovel, CarryRight, CarryLeft, Vector3.zero, 0.45f);
            Step = "carry";
        }

        /// <summary>Cắm xẻng lại vào đống than (NPC đứng ở chỗ xúc): khom gối, đặt xẻng, buông hai tay, đứng dậy.</summary>
        public IEnumerator PutShovel(Transform shovel, Transform parent)
        {
            Activity = "carry";
            ShovelLocal(LowRight, LowLeft, out var p, out var r);
            var along = r * Vector3.forward;
            yield return BendWith(true, shovel, LowRight, LowLeft, along * 0.1f, 0.5f);
            yield return MoveLocal(shovel, p - along * 0.04f - Vector3.up * 0.03f, r, 0.3f);
            shovel.SetParent(parent, true);
            yield return ReleaseBoth();
            yield return Bend(false);
            Activity = "";
        }

        // ---------- Dời vật ----------

        public static IEnumerator Move(Transform t, Vector3 toPos, Quaternion toRot, float seconds)
        {
            Vector3 p0 = t.position;
            Quaternion r0 = t.rotation;
            for (float e = 0f; e < seconds; e += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, e / seconds);
                t.SetPositionAndRotation(Vector3.Lerp(p0, toPos, k), Quaternion.Slerp(r0, toRot, k));
                yield return null;
            }
            t.SetPositionAndRotation(toPos, toRot);
        }

        public static IEnumerator MoveLocal(Transform t, Vector3 toPos, Quaternion toRot, float seconds)
        {
            Vector3 p0 = t.localPosition;
            Quaternion r0 = t.localRotation;
            for (float e = 0f; e < seconds; e += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, e / seconds);
                t.SetLocalPositionAndRotation(Vector3.Lerp(p0, toPos, k), Quaternion.Slerp(r0, toRot, k));
                yield return null;
            }
            t.SetLocalPositionAndRotation(toPos, toRot);
        }
    }
}
