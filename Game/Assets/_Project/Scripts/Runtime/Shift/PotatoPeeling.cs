using System.Collections;
using System.Collections.Generic;
using HaiBanTay.Core;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Gọt khoai như thật ở bàn sơ chế (D1, S16 "gọt khoai"): xô khoai bên trái, chậu vỏ dưới tay, nồi nước bên phải. Tay trái
    /// lấy từng củ trong xô và giữ củ trên chậu; tay phải kéo dao từ đầu xa về phía ngón cái, mỗi nhát một dải vỏ cuộn lên
    /// rồi rơi xuống chậu; xoay củ, nhát tiếp. Củ trắng thì thả vào nồi nước, lấy củ kế cho tới hết xô.
    /// </summary>
    public sealed class PotatoPeeling : MonoBehaviour
    {
        [Tooltip("Tâm củ khi cầm; tay trái bám theo (X phải, Y lên, Z dọc củ ra xa).")]
        public Transform palm;
        public Transform hold, rest, knife, knifeRest, leftGrip, rightGrip;
        [Tooltip("Chỗ từng củ trong xô, từ đáy lên.")]
        public Transform[] bucketSlots = new Transform[0];
        [Tooltip("Chỗ củ đã gọt dưới mặt nước trong nồi.")]
        public Transform[] potSlots = new Transform[0];
        public Transform potMouth, bowl;
        public float bowlRadius = 0.08f;
        [Tooltip("Ông Mai làm mẫu: tâm củ trong tay ông (+Z dọc củ ra xa, +X phía tay phải ông) và đĩa hứng vỏ trước mặt ông.")]
        public Transform demoHold, demoBowl;
        public float demoBowlRadius = 0.07f;
        public Renderer ripple;
        public Material skin, flesh;
        [Tooltip("Củ cải trong xô (S16: \"đống củ cải và khoai tây\"): vỏ trắng ngả tím phía cuống, ruột trắng.")]
        public Material turnipSkin, turnipFlesh;
        [Tooltip("Góc (độ, từ +X sang +Y quanh trục củ) mà dải đang gọt ngửa về phía dao.")]
        public float faceAngle = 75f;
        public float reachSeconds = 0.3f, bringSeconds = 0.35f, turnSeconds = 0.3f, tossSeconds = 0.4f, sinkSeconds = 0.35f;

        sealed class Spud
        {
            public GameObject go;
            public PotatoMesh mesh;
            public bool turnip;
        }

        public PotatoPeelLogic Logic { get; } = new PotatoPeelLogic();
        public int InBucket => _bucket.Count;
        /// <summary>Số củ cải còn trong xô (còn lại là khoai).</summary>
        public int TurnipsInBucket { get { int n = 0; foreach (var s in _bucket) if (s.turnip) n++; return n; } }
        public int InPot => _pot.Count;
        public bool HasPotato => _current != null;
        /// <summary>Hai tay đang ở việc gọt (cầm củ và dao, hoặc đang lấy, xoay, thả).</summary>
        public bool Engaged { get; private set; }
        /// <summary>Củ ở tư thế gọt, dao đặt trên dải: rê là gọt.</summary>
        public bool Held { get; private set; }
        /// <summary>Đang có động tác (lấy củ, xoay củ, thả nồi, đặt xuống): không nhận rê.</summary>
        public bool Busy { get; private set; }
        /// <summary>Gọt xong một củ thì tay tự lấy củ kế (người chơi vẫn giữ chuột).</summary>
        public bool AutoNext { get; set; }
        /// <summary>Số dải vỏ đã rơi vào chậu.</summary>
        public int StripsInBowl { get; private set; }
        public bool Demonstrating => _demo != null;
        /// <summary>Củ đang cầm (null nếu không có).</summary>
        public Transform Current => _current != null ? _current.go.transform : null;
        /// <summary>Củ ông Mai đang gọt mẫu (null nếu không làm mẫu).</summary>
        public Transform DemoPotato => _demo != null ? _demo.go.transform : null;
        /// <summary>Số dải vỏ ông Mai gọt mẫu đã rơi xuống đĩa trước mặt ông.</summary>
        public int StripsOnDemoPlate { get; private set; }

        readonly List<Spud> _bucket = new List<Spud>();
        readonly List<Spud> _pot = new List<Spud>();
        readonly List<PeelStrip> _strips = new List<PeelStrip>();
        readonly List<PeelStrip> _inBowl = new List<PeelStrip>();
        Spud _current, _demo;
        PeelStrip _strip, _demoStrip;
        float _stripFrom, _roll, _pile;
        int _seed;
        GameRoot _root;
        MaterialPropertyBlock _rippleBlock;
        Color _rippleColor;
        Transform _demoKnife, _demoLeftWrist, _demoRightWrist;
        NpcArmIK _demoIK;
        float _demoTime, _demoRoll;
        const int MaxStripsShown = 24;
        public const float DemoBandSeconds = 1f;
        /// <summary>Từ cổ tay tới lòng bàn tay (chỗ nắm), dọc hướng ngón (m).</summary>
        public const float HandReach = 0.07f;

        void Awake()
        {
            _rippleBlock = new MaterialPropertyBlock();
            if (ripple != null)
            {
                ripple.GetComponent<MeshFilter>().sharedMesh = RingMesh();
                _rippleColor = ripple.sharedMaterial.GetColor("_BaseColor");
                ripple.enabled = false;
            }
        }

        /// <summary>Lượt mới: xô đầy count củ còn vỏ, nồi và chậu trống, dao nằm trên thớt.</summary>
        public void Fill(int count)
        {
            StopAllCoroutines();
            Clear();
            for (int i = 0; i < count; i++)
            {
                // Lấy từ trên xuống: củ làm mẫu và củ đầu của Ba là khoai, cứ ba củ có một củ cải.
                var spud = Spawn((count - 1 - i) % 3 == 2);
                var slot = bucketSlots[Mathf.Min(i, bucketSlots.Length - 1)];
                spud.go.transform.SetPositionAndRotation(slot.position, slot.rotation);
                spud.mesh.ShowFlesh(false);
                _bucket.Add(spud);
            }
            Logic.Reset();
            knife.SetPositionAndRotation(knifeRest.position, knifeRest.rotation);
            palm.SetPositionAndRotation(hold.position, hold.rotation);
        }

        /// <summary>Hết lượt hoặc chương mới: dọn mọi củ và vỏ sinh lúc chạy.</summary>
        public void Clear()
        {
            Busy = Held = Engaged = false;
            foreach (var s in _bucket) Kill(s);
            foreach (var s in _pot) Kill(s);
            Kill(_current);
            Kill(_demo);
            _bucket.Clear(); _pot.Clear(); _current = null; _demo = null;
            foreach (var s in _strips) if (s != null) { Destroy(s.GetComponent<MeshFilter>().sharedMesh); Destroy(s.gameObject); }
            _strips.Clear();
            _inBowl.Clear();
            foreach (var s in new[] { _strip, _demoStrip })
                if (s != null) { Destroy(s.GetComponent<MeshFilter>().sharedMesh); Destroy(s.gameObject); }
            _strip = _demoStrip = null;
            if (_demoKnife != null) Destroy(_demoKnife.gameObject);
            _demoKnife = null;
            StripsInBowl = StripsOnDemoPlate = 0;
            _pile = 0f;
            if (ripple != null) ripple.enabled = false;
        }

        void OnDestroy() => Clear();

        Spud Spawn(bool turnip)
        {
            turnip &= turnipSkin != null && turnipFlesh != null;
            var mesh = new PotatoMesh(1911 + 37 * _seed++, 1f, turnip);
            var go = new GameObject(turnip ? "Turnip" : "Potato");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh.Mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = turnip ? new[] { turnipSkin, turnipFlesh } : new[] { skin, flesh };
            r.shadowCastingMode = ShadowCastingMode.Off;
            if (ripple != null) r.renderingLayerMask = ripple.renderingLayerMask;
            return new Spud { go = go, mesh = mesh, turnip = turnip };
        }

        static void Kill(Spud s)
        {
            if (s == null) return;
            if (s.go != null) Destroy(s.go);
            Destroy(s.mesh.Mesh);
        }

        /// <summary>Hai tay vào việc: cầm lại củ đang gọt dở trên thớt, hoặc lấy củ mới trong xô.</summary>
        public void Engage()
        {
            if (Busy || Engaged || (_current == null && _bucket.Count == 0))
                return;
            StartCoroutine(Take(_current == null));
        }

        /// <summary>Nghỉ tay: đặt củ gọt dở xuống thớt, dao nằm cạnh.</summary>
        public void Disengage()
        {
            if (Busy || !Engaged)
                return;
            StartCoroutine(PutDown());
        }

        /// <summary>Rê dao (phần của cả củ). Trả về true khi củ vừa trắng hết (sẽ tự thả vào nồi).</summary>
        public bool Peel(float potatoFraction)
        {
            if (!Held || Busy || _current == null || Logic.Done || potatoFraction <= 0f)
                return false;
            if (_strip == null)
            {
                _strip = NewStrip(_current);
                _stripFrom = Logic.Stroke;
            }
            bool bandDone = Logic.Add(potatoFraction);
            Pose();
            if (!bandDone)
                return false;
            bool last = Logic.Band == PotatoPeelLogic.Bands - 1;
            StartCoroutine(Turn());
            return last;
        }

        float RollFor(int band) => faceAngle - PotatoMesh.BandCenter(band) * Mathf.Rad2Deg;

        void ApplyRoll()
        {
            if (_current != null && _current.go.transform.parent == palm)
                _current.go.transform.localRotation = Quaternion.Euler(0f, 0f, _roll);
        }

        /// <summary>Củ, vỏ, dao và dải vỏ theo nhát hiện tại.</summary>
        void Pose()
        {
            if (_current == null)
                return;
            ApplyRoll();
            int band = Logic.Band;
            if (Logic.Done)
            {
                _current.mesh.SetPeeled(PotatoPeelLogic.Bands, -1, 1f);
                return;
            }
            float knifeU = PotatoMesh.KnifeU(Logic.Stroke);
            _current.mesh.SetPeeled(band, band, Logic.Stroke > 0f ? knifeU : 1.01f);
            KnifeAt(_current, band, Logic.Stroke, out var kp, out var kr, out var contact);
            knife.SetPositionAndRotation(kp, kr);
            if (_strip != null)
                ShapeStrip(_strip, _current, band, _stripFrom, Logic.Stroke, contact, kr);
        }

        PeelStrip NewStrip(Spud on)
        {
            var strip = on.turnip ? PeelStrip.Create(transform, turnipSkin, turnipFlesh) : PeelStrip.Create(transform, skin, flesh);
            strip.GetComponent<Renderer>().renderingLayerMask = on.go.GetComponent<Renderer>().renderingLayerMask;
            return strip;
        }

        /// <summary>Dải vỏ bám lưỡi dao: phần đã cắt từ chỗ bắt đầu nhát (from) tới lưỡi dao (to), bề rộng theo dải trên củ.</summary>
        static void ShapeStrip(PeelStrip strip, Spud spud, int band, float from, float to, Vector3 contact, Quaternion knifeRotation)
        {
            var mesh = spud.mesh;
            float length = mesh.CutLength(band, to) - mesh.CutLength(band, from);
            float uKnife = PotatoMesh.KnifeU(to), uStart = PotatoMesh.KnifeU(from);
            strip.transform.SetPositionAndRotation(contact, knifeRotation);
            strip.Shape(length, t => mesh.BandWidth(band, Mathf.Lerp(uKnife, uStart, t)) * 0.72f);
        }

        /// <summary>Dao đặt ở dải band, nhát stroke: giữa lưỡi ở điểm cắt, mặt lưỡi tiếp xúc ruột, cán sang tay phải,
        /// lưỡi quay về phía ngón cái.</summary>
        void KnifeAt(Spud spud, int band, float stroke, out Vector3 position, out Quaternion rotation, out Vector3 contact)
        {
            var t = spud.go.transform;
            spud.mesh.Frame(band, PotatoMesh.KnifeU(stroke), out var p, out var n, out var far);
            contact = t.TransformPoint(p);
            rotation = Quaternion.LookRotation(t.TransformDirection(far), t.TransformDirection(n));
            position = contact - rotation * new Vector3(-0.012f, 0f, 0f);
        }

        void DropStrip()
        {
            if (_strip == null)
                return;
            Release(_strip, bowl, bowlRadius, _pile, Landed);
            _strip = null;
        }

        /// <summary>Dải rời dao, rơi vào lòng chậu (đĩa) into: vận tốc ngang đủ đưa dải vào trong lòng, mỗi dải rơi một chỗ
        /// (vỏ rải ra, không chồng đúng một điểm).</summary>
        void Release(PeelStrip strip, Transform into, float radius, float pile, System.Action<PeelStrip> landed)
        {
            strip.transform.SetParent(transform, true);
            var spread = Random.insideUnitCircle * radius * 0.8f;
            var land = into.position + new Vector3(spread.x, 0f, spread.y);
            float fall = Mathf.Max(0.05f, strip.transform.position.y - into.position.y);
            float time = Mathf.Sqrt(2f * fall / (Physics.gravity.magnitude * 0.7f));
            var v = Vector3.ProjectOnPlane(land - strip.transform.position, Vector3.up) / Mathf.Max(0.1f, time);
            strip.Drop(into.position.y + pile, v, land, landed);
            _strips.Add(strip);
            _root ??= GameRoot.Instance;
            _root?.Audio.PlaySfxAt("SFX/leaf", strip.transform.position, 0.25f);
        }

        void Landed(PeelStrip strip)
        {
            StripsInBowl++;
            _pile = Mathf.Min(0.02f, _pile + 0.0008f);
            // Dải cũ trong chậu bị dải mới phủ lên: tắt bớt để không tốn lượt vẽ.
            _inBowl.Add(strip);
            if (_inBowl.Count > MaxStripsShown && _inBowl[_inBowl.Count - 1 - MaxStripsShown] != null)
                _inBowl[_inBowl.Count - 1 - MaxStripsShown].gameObject.SetActive(false);
        }

        void LandedOnDemoPlate(PeelStrip strip) => StripsOnDemoPlate++;

        IEnumerator Take(bool fromBucket)
        {
            Busy = true;
            Engaged = true;
            Held = false;
            if (fromBucket)
            {
                _current = _bucket[_bucket.Count - 1];
                _bucket.RemoveAt(_bucket.Count - 1);
                Logic.Reset();
            }
            _current.mesh.ShowFlesh(true);
            var t = _current.go.transform;
            _root ??= GameRoot.Instance;
            // Tay trái với tới củ (trong xô hoặc trên thớt).
            var palmFrom = palm.position;
            var palmRotFrom = palm.rotation;
            var target = t.position;
            for (float k = 0f; k < 1f; k += Time.deltaTime / reachSeconds)
            {
                float e = Mathf.SmoothStep(0f, 1f, k);
                palm.SetPositionAndRotation(Vector3.Lerp(palmFrom, target, e) + Vector3.up * (0.05f * Mathf.Sin(Mathf.PI * e)),
                    Quaternion.Slerp(palmRotFrom, hold.rotation, e));
                yield return null;
            }
            palm.SetPositionAndRotation(target, hold.rotation);
            _root?.Audio.PlaySfxAt(fromBucket ? "SFX/potatoes" : "SFX/pot_place", target, 0.2f);
            // Cầm củ, đưa lên trên chậu vỏ; dao từ thớt về đầu dải.
            t.SetParent(palm, true);
            var localFrom = t.localPosition;
            var localRotFrom = t.localRotation;
            _roll = RollFor(Logic.Band);
            var localRotTo = Quaternion.Euler(0f, 0f, _roll);
            palmFrom = palm.position;
            var knifeFrom = knife.position;
            var knifeRotFrom = knife.rotation;
            for (float k = 0f; k < 1f; k += Time.deltaTime / bringSeconds)
            {
                float e = Mathf.SmoothStep(0f, 1f, k);
                palm.SetPositionAndRotation(Vector3.Lerp(palmFrom, hold.position, e) + Vector3.up * (0.03f * Mathf.Sin(Mathf.PI * e)), hold.rotation);
                t.localPosition = Vector3.Lerp(localFrom, Vector3.zero, e);
                t.localRotation = Quaternion.Slerp(localRotFrom, localRotTo, e);
                KnifeAt(_current, Logic.Band, Logic.Stroke, out var kp, out var kr, out _);
                knife.SetPositionAndRotation(Vector3.Lerp(knifeFrom, kp, e), Quaternion.Slerp(knifeRotFrom, kr, e));
                yield return null;
            }
            palm.SetPositionAndRotation(hold.position, hold.rotation);
            t.localPosition = Vector3.zero;
            Pose();
            Held = true;
            Busy = false;
        }

        IEnumerator Turn()
        {
            Busy = true;
            int band = Logic.Band;
            DropStrip();
            _current.mesh.SetPeeled(band + 1, -1, 1f);
            if (band == PotatoPeelLogic.Bands - 1)
            {
                Logic.NextBand();
                yield return TossAndNext();
                yield break;
            }
            // Ngón tay xoay củ một nấc, dao nhấc lên quay về đầu xa của dải kế.
            float from = _roll, to = RollFor(band + 1);
            var knifeFrom = knife.position;
            var knifeRotFrom = knife.rotation;
            for (float k = 0f; k < 1f; k += Time.deltaTime / turnSeconds)
            {
                float e = Mathf.SmoothStep(0f, 1f, k);
                _roll = Mathf.LerpAngle(from, to, e);
                ApplyRoll();
                KnifeAt(_current, band + 1, 0f, out var kp, out var kr, out _);
                var lift = (palm.up * 0.018f + palm.right * 0.01f) * Mathf.Sin(Mathf.PI * e);
                knife.SetPositionAndRotation(Vector3.Lerp(knifeFrom, kp, e) + lift, Quaternion.Slerp(knifeRotFrom, kr, e));
                yield return null;
            }
            _roll = to;
            Logic.NextBand();
            Pose();
            Busy = false;
        }

        IEnumerator TossAndNext()
        {
            Busy = true;
            Held = false;
            var spud = _current;
            _current = null;
            yield return IntoPot(spud, palm, 0.35f);
            if (AutoNext && _bucket.Count > 0)
            {
                yield return Take(true);
                yield break;
            }
            yield return MoveKnife(knifeRest.position, knifeRest.rotation, 0.3f);
            Engaged = false;
            Busy = false;
        }

        /// <summary>Thả củ trắng vào nồi: bay vòng cung tới mặt nước, tóe nước, chìm xuống chỗ của nó.</summary>
        IEnumerator IntoPot(Spud spud, Transform hand, float handFollow)
        {
            var t = spud.go.transform;
            t.SetParent(transform, true);
            spud.mesh.SetPeeled(PotatoPeelLogic.Bands, -1, 1f);
            var slot = potSlots[Mathf.Min(_pot.Count, potSlots.Length - 1)];
            _pot.Add(spud);
            var start = t.position;
            var startRot = t.rotation;
            var mouth = potMouth.position + Vector3.ProjectOnPlane(slot.position - potMouth.position, Vector3.up);
            var handFrom = hand != null ? hand.position : Vector3.zero;
            float arc = 0.07f + 0.12f * Vector3.Distance(start, mouth);
            for (float k = 0f; k < 1f; k += Time.deltaTime / tossSeconds)
            {
                float e = Mathf.SmoothStep(0f, 1f, k);
                t.SetPositionAndRotation(Vector3.Lerp(start, mouth, e) + Vector3.up * (arc * Mathf.Sin(Mathf.PI * e)),
                    Quaternion.Slerp(startRot, slot.rotation, e));
                // Tay đưa theo một đoạn rồi về.
                if (hand != null)
                    hand.position = Vector3.Lerp(handFrom, mouth, handFollow * Mathf.Sin(Mathf.PI * e));
                yield return null;
            }
            Splash(mouth);
            var sinkFrom = t.position;
            for (float k = 0f; k < 1f; k += Time.deltaTime / sinkSeconds)
            {
                t.position = Vector3.Lerp(sinkFrom, slot.position, Mathf.SmoothStep(0f, 1f, k));
                yield return null;
            }
            t.SetPositionAndRotation(slot.position, slot.rotation);
        }

        IEnumerator PutDown()
        {
            Busy = true;
            Held = false;
            // Dải đang cắt dở rơi luôn xuống chậu; cầm lại thì dải mới bắt đầu từ chỗ dao dừng.
            DropStrip();
            var t = _current.go.transform;
            var palmFrom = palm.position;
            var knifeFrom = knife.position;
            var knifeRotFrom = knife.rotation;
            var restRot = rest.rotation * Quaternion.Euler(0f, 0f, _roll);
            var potRotFrom = t.rotation;
            for (float k = 0f; k < 1f; k += Time.deltaTime / bringSeconds)
            {
                float e = Mathf.SmoothStep(0f, 1f, k);
                palm.position = Vector3.Lerp(palmFrom, rest.position, e) + Vector3.up * (0.03f * Mathf.Sin(Mathf.PI * e));
                t.rotation = Quaternion.Slerp(potRotFrom, restRot, e);
                knife.SetPositionAndRotation(Vector3.Lerp(knifeFrom, knifeRest.position, e), Quaternion.Slerp(knifeRotFrom, knifeRest.rotation, e));
                yield return null;
            }
            t.SetParent(transform, true);
            t.SetPositionAndRotation(rest.position, restRot);
            knife.SetPositionAndRotation(knifeRest.position, knifeRest.rotation);
            Engaged = false;
            Busy = false;
        }

        IEnumerator MoveKnife(Vector3 position, Quaternion rotation, float seconds)
        {
            var from = knife.position;
            var rotFrom = knife.rotation;
            for (float k = 0f; k < 1f; k += Time.deltaTime / seconds)
            {
                float e = Mathf.SmoothStep(0f, 1f, k);
                knife.SetPositionAndRotation(Vector3.Lerp(from, position, e), Quaternion.Slerp(rotFrom, rotation, e));
                yield return null;
            }
            knife.SetPositionAndRotation(position, rotation);
        }

        void Splash(Vector3 at)
        {
            _root ??= GameRoot.Instance;
            _root?.Audio.PlaySfxAt("SFX/water_wash", at, 0.3f);
            if (ripple != null)
                StartCoroutine(Ripple(at));
        }

        IEnumerator Ripple(Vector3 at)
        {
            ripple.transform.position = new Vector3(at.x, potMouth.position.y + 0.002f, at.z);
            ripple.enabled = true;
            const float seconds = 0.7f;
            for (float k = 0f; k < 1f; k += Time.deltaTime / seconds)
            {
                float r = Mathf.Lerp(0.015f, 0.11f, 1f - (1f - k) * (1f - k));
                ripple.transform.localScale = new Vector3(r, 1f, r);
                var c = _rippleColor;
                c.a *= 1f - k;
                _rippleBlock.SetColor("_BaseColor", c);
                ripple.SetPropertyBlock(_rippleBlock);
                yield return null;
            }
            ripple.enabled = false;
        }

        /// <summary>Tua nhanh (sim, chương chọn lại): một củ trong xô thành củ trắng trong nồi.</summary>
        public void SimulateOne()
        {
            Spud spud = _current;
            if (spud != null) { _current = null; Held = Engaged = Busy = false; }
            else if (_bucket.Count > 0) { spud = _bucket[_bucket.Count - 1]; _bucket.RemoveAt(_bucket.Count - 1); }
            if (spud == null) return;
            spud.mesh.ShowFlesh(true);
            spud.mesh.SetPeeled(PotatoPeelLogic.Bands, -1, 1f);
            var slot = potSlots[Mathf.Min(_pot.Count, potSlots.Length - 1)];
            spud.go.transform.SetParent(transform, true);
            spud.go.transform.SetPositionAndRotation(slot.position, slot.rotation);
            _pot.Add(spud);
            Logic.Reset();
            knife.SetPositionAndRotation(knifeRest.position, knifeRest.rotation);
        }

        // ---------- Ông Mai làm mẫu: củ ở tay trái, dao ở tay phải của chính ông (IK tay vào củ và cán dao) ----------

        /// <summary>Ông Mai lấy một củ trong xô, cầm củ trước bụng; hai tay bám củ và cán dao bằng IK.</summary>
        public void BeginDemo(NpcArmIK ik)
        {
            if (_bucket.Count == 0 || ik == null)
                return;
            _demo = _bucket[_bucket.Count - 1];
            _bucket.RemoveAt(_bucket.Count - 1);
            _demo.mesh.ShowFlesh(true);
            _demo.mesh.SetPeeled(0, -1, 1f);
            _demoKnife = Instantiate(knife.gameObject, transform).transform;
            _demoKnife.name = "MaiKnife";
            if (_demoLeftWrist == null) _demoLeftWrist = new GameObject("MaiLeftWrist").transform;
            if (_demoRightWrist == null) _demoRightWrist = new GameObject("MaiRightWrist").transform;
            _demoLeftWrist.SetParent(transform, false);
            _demoRightWrist.SetParent(transform, false);
            _demoIK = ik;
            // Tay trái khum đỡ củ, tay phải nắm tròn quanh cán (cùng độ cong với mesh Grip của Ba).
            ik.leftCurl = new Vector3(25f, 35f, 25f);
            ik.rightCurl = new Vector3(58f, 75f, 55f);
            _demoTime = 0f;
            _demoRoll = RollFor(0);
            PlaceDemo();
            ik.leftTarget = _demoLeftWrist;
            ik.rightTarget = _demoRightWrist;
            ik.enabled = true;
        }

        /// <summary>Thời gian làm mẫu (giây): mỗi dải một nhát rồi xoay củ.</summary>
        public void DemoAt(float seconds)
        {
            if (_demo == null)
                return;
            _demoTime = seconds;
            int band = Mathf.Min(PotatoPeelLogic.Bands - 1, Mathf.FloorToInt(seconds / DemoBandSeconds));
            float inBand = seconds - band * DemoBandSeconds;
            float stroke = Mathf.Clamp01(inBand / (DemoBandSeconds * 0.75f));
            float turn = Mathf.Clamp01((inBand - DemoBandSeconds * 0.75f) / (DemoBandSeconds * 0.25f));
            bool done = seconds >= PotatoPeelLogic.Bands * DemoBandSeconds;
            if (done)
                _demo.mesh.SetPeeled(PotatoPeelLogic.Bands, -1, 1f);
            else if (turn > 0f)
                _demo.mesh.SetPeeled(band + 1, -1, 1f);
            else
                _demo.mesh.SetPeeled(band, band, stroke > 0f ? PotatoMesh.KnifeU(stroke) : 1.01f);
            // Mỗi nhát của ông cũng bong một dải vỏ cuộn trên lưỡi dao; hết nhát (xoay củ) thì dải rơi xuống đĩa trước mặt ông.
            bool cutting = !done && turn <= 0f && stroke > 0f;
            if (!cutting && _demoStrip != null)
            {
                Release(_demoStrip, demoBowl, demoBowlRadius, 0f, LandedOnDemoPlate);
                _demoStrip = null;
            }
            else if (cutting && _demoStrip == null && demoBowl != null)
                _demoStrip = NewStrip(_demo);
            _demoRoll = Mathf.LerpAngle(RollFor(band), RollFor(Mathf.Min(band + 1, PotatoPeelLogic.Bands - 1)), done ? 0f : Mathf.SmoothStep(0f, 1f, turn));
        }

        /// <summary>Làm mẫu xong: ông thả củ trắng vào nồi nước, cất dao, buông tay.</summary>
        public IEnumerator EndDemo()
        {
            if (_demo == null)
                yield break;
            var spud = _demo;
            _demo = null;
            if (_demoStrip != null)
            {
                Release(_demoStrip, demoBowl, demoBowlRadius, 0f, LandedOnDemoPlate);
                _demoStrip = null;
            }
            if (_demoKnife != null) Destroy(_demoKnife.gameObject);
            _demoKnife = null;
            if (_demoIK != null) { _demoIK.leftTarget = _demoIK.rightTarget = null; _demoIK.enabled = false; }
            _demoIK = null;
            yield return IntoPot(spud, null, 0f);
        }

        void LateUpdate()
        {
            if (_demo != null && _demoIK != null)
                PlaceDemo();
        }

        /// <summary>Củ trong tay ông Mai ở demoHold (ngang ngực dưới, trục dài chúi ra trước), dao ở dải đang gọt, dải vỏ bám
        /// lưỡi dao; cổ tay trái đỡ dưới củ, tay phải nắm cán dao. Chạy trước NpcArmIK.</summary>
        void PlaceDemo()
        {
            Quaternion holdRotation;
            Vector3 center;
            if (demoHold != null)
            {
                holdRotation = demoHold.rotation;
                center = demoHold.position;
            }
            else
            {
                var body = _demoIK.transform;
                var forward = Vector3.ProjectOnPlane(body.forward, Vector3.up).normalized;
                var right = Vector3.Cross(Vector3.up, forward);
                holdRotation = Quaternion.LookRotation(Quaternion.AngleAxis(18f, right) * forward, Vector3.up);
                center = body.position + forward * 0.4f - right * 0.04f + Vector3.up * 1.26f;
            }
            var t = _demo.go.transform;
            t.SetPositionAndRotation(center, holdRotation * Quaternion.Euler(0f, 0f, _demoRoll));
            int band = Mathf.Min(PotatoPeelLogic.Bands - 1, Mathf.FloorToInt(_demoTime / DemoBandSeconds));
            float stroke = Mathf.Clamp01((_demoTime - band * DemoBandSeconds) / (DemoBandSeconds * 0.75f));
            KnifeAt(_demo, band, stroke, out var kp, out var kr, out var contact);
            _demoKnife.SetPositionAndRotation(kp, kr);
            if (_demoStrip != null)
                ShapeStrip(_demoStrip, _demo, band, 0f, stroke, contact, kr);
            var leftFingers = holdRotation * new Vector3(0.75f, 0.35f, 0.55f).normalized;
            var leftBack = holdRotation * new Vector3(-0.3f, -0.95f, 0f).normalized;
            var leftPalm = center + holdRotation * new Vector3(-0.012f, -0.045f, 0f);
            _demoLeftWrist.SetPositionAndRotation(leftPalm - leftFingers * HandReach + leftBack * 0.01f, Quaternion.LookRotation(leftFingers, leftBack));
            var rightFingers = kr * Vector3.forward;
            var grip = _demoKnife.TransformPoint(new Vector3(0.075f, 0f, 0f));
            _demoRightWrist.SetPositionAndRotation(grip - rightFingers * HandsRig.GripForward + kr * Vector3.up * HandsRig.GripBelow, Quaternion.LookRotation(rightFingers, kr * Vector3.up));
        }

        /// <summary>Vòng gợn nước (bán kính trong 0,8, ngoài 1, đơn vị), dựng một lần.</summary>
        static Mesh RingMesh()
        {
            const int n = 40;
            var v = new Vector3[(n + 1) * 2];
            var tri = new int[n * 6];
            for (int i = 0; i <= n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v[i * 2] = d * 0.8f;
                v[i * 2 + 1] = d;
            }
            for (int i = 0; i < n; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                tri[i * 6] = a; tri[i * 6 + 1] = c; tri[i * 6 + 2] = b;
                tri[i * 6 + 3] = b; tri[i * 6 + 4] = c; tri[i * 6 + 5] = d;
            }
            var mesh = new Mesh { name = "Ripple", vertices = v, triangles = tri };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
