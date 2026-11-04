using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Ship;
using HaiBanTay.Shift;
using HaiBanTay.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// W1 (PRODUCTION_PLAN mục 4 S6, mục 6): kiểm tĩnh trong bếp đã dựng. DOOR: thân người chơi và đồ đang bê đi lọt các cửa,
    /// cầu thang trên tuyến chơi (đi thẳng qua giữa ô cửa, bám mặt sàn và bậc thang). NPC_ROUTE: mọi đoạn tuyến NPC builder đặt
    /// sẵn không cắt qua vật cố định (bán kính thân, cộng phần đồ bê). Lỗi đã biết, chưa tới đợt sửa, ghi trong <see cref="Known"/>
    /// kèm mã QA; lỗi mới làm đỏ test, lỗi đã biết mà hết thì cũng đỏ để gỡ khỏi danh sách.
    /// </summary>
    public class OracleSceneTests
    {
        static Vector3 V(float x, float z, float y = 0f) => new Vector3(x, y, z);

        /// <summary>Mã phát hiện (tiền tố) → mã QA và đợt sửa. Chỉ ghi từng chỗ một, không ghi cả nhóm.</summary>
        static readonly Dictionary<string, string> Known = new Dictionary<string, string>
        {
        };

        sealed class Load
        {
            public string name;
            public Vector3 half;
            public float ahead, height;
        }

        // Đồ bê hai tay to nhất trên từng tuyến (kích thước theo builder): thùng cá 0,58 × 0,56; sọt than; nồi.
        static readonly Load None = null;
        static readonly Load FishCrate = new Load { name = "fish_crate", half = new Vector3(0.29f, 0.24f, 0.28f), ahead = 0.42f, height = 1.0f };
        static readonly Load CoalBasket = new Load { name = "coal_basket", half = new Vector3(0.24f, 0.2f, 0.22f), ahead = 0.4f, height = 0.95f };
        static readonly Load Pot = new Load { name = "pot", half = new Vector3(0.2f, 0.16f, 0.2f), ahead = 0.38f, height = 1.0f };

        static string Report(string name, List<string> found, params string[] prefixes)
        {
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Logs", "oracle"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, name + ".txt"), string.Join("\n", found) + "\n", new UTF8Encoding(false));
            var unknown = found.Where(f => !Known.Keys.Any(k => f.StartsWith(k))).ToList();
            var stale = Known.Keys.Where(k => prefixes.Any(k.StartsWith) && !found.Any(f => f.StartsWith(k))).ToList();
            var text = new StringBuilder();
            if (unknown.Count > 0) text.AppendLine("Mới (chưa có mã QA):\n  " + string.Join("\n  ", unknown));
            if (stale.Count > 0) text.AppendLine("Đã hết, gỡ khỏi Known:\n  " + string.Join("\n  ", stale));
            return text.ToString();
        }

        static bool IsStatic(Collider c)
        {
            if (c == null || c.isTrigger) return false;
            return c.GetComponentInParent<PlayerRig>() == null && c.GetComponentInParent<NpcAgent>() == null && c.GetComponentInParent<Holdable>() == null;
        }

        static IEnumerator OpenDoors()
        {
            foreach (var door in Object.FindObjectsByType<HingedDoor>(FindObjectsSortMode.None))
                door.Set(true);
            yield return new WaitForSeconds(1.2f);
        }

        /// <summary>Mặt đỡ chân cao nhất dưới điểm, không cao hơn điểm 0,5 m (bỏ trần, lanh tô phía trên).</summary>
        static bool Ground(Vector3 p, out float y)
        {
            y = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 0.5f, Vector3.down, 3f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                if (IsStatic(h.collider) && h.point.y > y) y = h.point.y;
            return !float.IsNegativeInfinity(y);
        }

        static void Add(List<string> found, HashSet<string> seen, string key, Vector3 at)
        {
            if (seen.Add(key)) found.Add($"{key} @{at.x:F2},{at.y:F2},{at.z:F2}");
        }

        /// <summary>
        /// Đi theo tuyến từng 5 cm, đứng trên mặt sàn/bậc thang bên dưới: thân (capsule của CharacterController, trên mép bậc,
        /// chừa 1 cm) và đồ bê (hộp trước ngực) không được chồng vật cố định. Ở góc cua của tuyến, đồ bê thôi kiểm từ lúc nó chạm
        /// góc: người chơi rẽ trước đó, tuyến bot chỉ là đường gấp khúc. Có <paramref name="door"/> thì chỉ tính vật nằm trong
        /// khoảng 0,35 m quanh mặt phẳng ô cửa (khung, lanh tô, ngưỡng); đồ trong phòng là chuyện bày biện, không phải cửa hẹp.
        /// Sàn hở dưới 12 cm (khe giữa hai tấm sàn) capsule bước qua được, không tính.
        /// </summary>
        static void Sweep(List<string> found, string route, Vector3[] path, Load load, CharacterController cc, Transform door = null)
        {
            float r = cc.radius - 0.01f, h = cc.height, step = cc.stepOffset;
            var seen = new HashSet<string>();
            bool InDoor(Collider c, Vector3 near)
            {
                if (door == null) return true;
                var n = door.right;
                n.y = 0f;
                return Mathf.Abs(Vector3.Dot(c.bounds.ClosestPoint(near) - door.position, n.normalized)) <= 0.35f;
            }
            for (int i = 0; i + 1 < path.Length; i++)
            {
                var a = path[i];
                var b = path[i + 1];
                var flat = new Vector3(b.x - a.x, 0f, b.z - a.z);
                float len = flat.magnitude;
                if (len < 0.02f) continue;
                flat /= len;
                var rot = Quaternion.LookRotation(flat);
                bool turns = false;
                if (i + 2 < path.Length)
                {
                    var next = new Vector3(path[i + 2].x - b.x, 0f, path[i + 2].z - b.z);
                    turns = next.sqrMagnitude > 1e-4f && Vector3.Angle(flat, next) > 30f;
                }
                float loadUntil = turns && load != null ? len - load.ahead - load.half.z : len;
                string seg = $"{route}[{i}->{i + 1}]";
                int n = Mathf.Max(1, Mathf.CeilToInt(len / 0.05f));
                float holeFrom = -1f;
                for (int k = 0; k <= n; k++)
                {
                    float t = k / (float)n;
                    var p = Vector3.Lerp(a, b, t);
                    if (!Ground(p, out float gy))
                    {
                        if (holeFrom < 0f) holeFrom = t * len;
                        if (t * len - holeFrom >= 0.12f) Add(found, seen, $"DOOR:{seg}:no_floor", p);
                        continue;
                    }
                    holeFrom = -1f;
                    p.y = gy;
                    var body = p + Vector3.up * (h * 0.5f);
                    foreach (var c in Physics.OverlapCapsule(p + Vector3.up * (step + r), p + Vector3.up * (h - r), r, ~(1 << 2), QueryTriggerInteraction.Ignore))
                        if (IsStatic(c) && InDoor(c, body)) Add(found, seen, $"DOOR:{seg}:body:{c.name}", p);
                    if (load == null || t * len > loadUntil) continue;
                    var center = p + Vector3.up * load.height + flat * load.ahead;
                    foreach (var c in Physics.OverlapBox(center, load.half - Vector3.one * 0.01f, rot, ~(1 << 2), QueryTriggerInteraction.Ignore))
                        if (IsStatic(c) && InDoor(c, center)) Add(found, seen, $"DOOR:{seg}:{load.name}:{c.name}", center);
                }
            }
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator DoorsAndStairsFitThePlayerAndTheLoad()
        {
            yield return ChoreTestUtil.Galley();
            yield return OpenDoors();
            var cc = PlayerRig.Current.motor.GetComponent<CharacterController>();
            var ship = Object.FindAnyObjectByType<ShipLayout>().transform;
            Vector3 S(float x, float y, float z) => ship.TransformPoint(new Vector3(x, y, z));
            var galley = Object.FindAnyObjectByType<ShipLayout>().galley;
            Vector3 G(float x, float z) => galley.TransformPoint(V(x, z));
            var found = new List<string>();
            // Ô cửa không cánh trên tuyến chơi (PlayerBot.Travel): đi thẳng qua giữa, hai chiều.
            var routes = new List<(string name, Vector3[] path, Load load, Transform door)>
            {
                ("dining_door", new[] { G(-0.9f, -2.4f), G(-0.9f, -3.6f) }, None, null),
                ("coal_door", new[] { G(1.45f, -2.65f), G(1.45f, -3.45f) }, CoalBasket, null),
                // Cửa phòng rửa mới (17/10): vách x 3,3, ô cửa z −0,3…0,9 (tâm 0,3). Tuyến cũ (x 4,65 → 5,6) là cửa x 5,05 đã bỏ và nay đâm vào chảo ở vòng rửa.
                ("wash_door", new[] { G(2.6f, 0.3f), G(4.2f, 0.3f) }, Pot, null),
                ("corridor_door", new[] { G(4.5f, 1.5f), G(5.3f, 1.5f) }, FishCrate, null),
                // Boong giếng trước → buồng thang → 14 bậc xuống hầm thực phẩm (PlaythroughTests.ToFoodHold), vác bao/sọt.
                ("hold_stairs", new[] { S(5.45f, 5.6f, -2.2f), S(5.45f, 5.6f, 0f), S(6.35f, 5.6f, 0f), S(9.9f, 2.8f, 0f), S(10.75f, 2.8f, 0f) }, FishCrate, null),
            };
            // Người chơi ít khi đi đúng giữa: thân lệch 0,22 m mỗi bên vẫn qua cửa buồng thang và xuống hết thang
            // (cửa cũ 0,9 m chắn từ lệch 0,18 m).
            foreach (float off in new[] { -0.22f, 0.22f })
                routes.Add(("hold_stairs_off" + off.ToString("+0.00;-0.00", System.Globalization.CultureInfo.InvariantCulture),
                    new[] { S(5.45f, 5.6f, off), S(6.35f, 5.6f, off), S(9.9f, 2.8f, off), S(10.75f, 2.8f, off) }, None, null));
            // Cửa có cánh (cửa bếp, cửa hông hành lang…): trục +X của cửa hướng ra ngoài; đi thẳng qua giữa ô cửa.
            foreach (var door in Object.FindObjectsByType<HingedDoor>(FindObjectsSortMode.None).OrderBy(d => d.name))
            {
                var through = door.transform.right;
                through.y = 0f;
                through.Normalize();
                var at = door.transform.position;
                routes.Add(("door_" + door.name, new[] { at - through * 0.75f, at + through * 0.75f }, FishCrate, door.transform));
            }
            foreach (var (name, path, load, door) in routes)
            {
                Sweep(found, name, path, load, cc, door);
                Sweep(found, name + "_back", path.Reverse().ToArray(), load, cc, door);
            }
            string problems = Report("door_scene", found, "DOOR:");
            Assert.IsEmpty(problems, problems);
        }

        /// <summary>
        /// Lưới phải bắt được lỗi cửa hẹp dù cảnh hiện tại không còn (PRODUCTION_PLAN W1 nghiệm thu): ô cửa dựng tạm 0,45 m chắn thân,
        /// 0,55 m thân lọt nhưng chắn thùng cá, 1 m không báo.
        /// </summary>
        [UnityTest, Timeout(120000)]
        public IEnumerator DoorCheckCatchesANarrowDoorwayFixture()
        {
            yield return ChoreTestUtil.Galley();
            var cc = PlayerRig.Current.motor.GetComponent<CharacterController>();
            List<string> Through(float width)
            {
                var root = new GameObject("DoorFixture_" + width).transform;
                root.position = new Vector3(200f, 40f, 200f);
                void Box(string name, Vector3 at, Vector3 size)
                {
                    var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    b.name = name;
                    b.transform.SetParent(root, false);
                    b.transform.localPosition = at;
                    b.transform.localScale = size;
                }
                Box("FixtureFloor", new Vector3(0f, -0.05f, 0f), new Vector3(6f, 0.1f, 6f));
                Box("FixtureJambL", new Vector3(-width * 0.5f - 1f, 1.1f, 0f), new Vector3(2f, 2.2f, 0.12f));
                Box("FixtureJambR", new Vector3(width * 0.5f + 1f, 1.1f, 0f), new Vector3(2f, 2.2f, 0.12f));
                Box("FixtureLintel", new Vector3(0f, 2.1f, 0f), new Vector3(width, 0.2f, 0.12f));
                Physics.SyncTransforms();
                var result = new List<string>();
                Sweep(result, "fixture_" + width, new[] { root.TransformPoint(V(0f, -1.5f)), root.TransformPoint(V(0f, 1.5f)) }, FishCrate, cc);
                Object.DestroyImmediate(root.gameObject);
                Physics.SyncTransforms();
                return result;
            }
            var tight = Through(0.45f);
            Assert.IsTrue(tight.Any(f => f.Contains(":body:FixtureJamb")), $"ô cửa 0,45 m hẹp hơn thân (r = {cc.radius:F2} m) phải bị báo\n" + string.Join("\n", tight));
            var narrow = Through(0.55f);
            Assert.IsFalse(narrow.Any(f => f.Contains(":body:")), "ô cửa 0,55 m: thân lọt\n" + string.Join("\n", narrow));
            Assert.IsTrue(narrow.Any(f => f.Contains(":fish_crate:FixtureJamb")), "ô cửa 0,55 m: thùng cá 0,58 m phải bị báo");
            var wide = Through(1.0f);
            Assert.IsEmpty(wide, "ô cửa 1 m không báo:\n" + string.Join("\n", wide));
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator NpcRoutesClearBodyAndLoad()
        {
            yield return ChoreTestUtil.Galley();
            yield return OpenDoors();
            var found = new List<string>();
            // workEnd: điểm cuối là chỗ đứng làm việc mà đồ bê được đưa ra trên mặt việc (đổ thùng vào nồi trên lò): ở điểm đó chỉ
            // thân người phải trống, đồ bê nằm trên lò là đúng ý.
            void Route(string name, Vector3[] path, NpcAgent agent, bool carrying, bool workEnd = false)
            {
                if (path == null || path.Length == 0) { found.Add($"NPC_ROUTE:{name}:empty"); return; }
                float body = agent != null ? agent.bodyRadius : 0.25f;
                float radius = body + (carrying ? 0.08f : 0f);
                var self = agent != null ? agent.transform : null;
                for (int i = 0; i < path.Length; i++)
                {
                    float r = workEnd && i == path.Length - 1 ? body : radius;
                    if (!NpcPathing.FreeAt(path[i], r, self))
                        found.Add($"NPC_ROUTE:{name}[{i}]:point_inside {InsideWhat(path[i], r, self)} @{path[i].x:F2},{path[i].z:F2}");
                }
                for (int i = 0; i + 1 < path.Length; i++)
                    if (NpcPathing.Blocked(path[i], path[i + 1], radius, self, out var blocker))
                        found.Add($"NPC_ROUTE:{name}[{i}->{i + 1}]:{blocker.name}");
            }
            var cook = Object.FindAnyObjectByType<CookRoutine>();
            var helper = Object.FindAnyObjectByType<HelperRoutine>();
            var cast = Object.FindAnyObjectByType<DayCast>();
            Assert.IsNotNull(cook); Assert.IsNotNull(helper); Assert.IsNotNull(cast);
            var cookAgent = cook.GetComponent<NpcAgent>();
            var helperAgent = helper.GetComponent<NpcAgent>();
            // W5.2: quản bếp, phụ bếp đi theo NavMesh. Mỗi đoạn việc: có đường trọn vẹn trên NavMesh, các góc đường (đã nới khi bê đồ,
            // NpcAgent.Widen, đúng như NPC đi) không đụng vật tĩnh.
            void Nav(string name, Vector3 from, Vector3 to, NpcAgent agent, bool carrying, bool workEnd = false)
            {
                var path = new UnityEngine.AI.NavMeshPath();
                if (!UnityEngine.AI.NavMesh.SamplePosition(from, out var a, 0.6f, UnityEngine.AI.NavMesh.AllAreas)
                    || !UnityEngine.AI.NavMesh.SamplePosition(to, out var b, 0.6f, UnityEngine.AI.NavMesh.AllAreas)
                    || !UnityEngine.AI.NavMesh.CalculatePath(a.position, b.position, UnityEngine.AI.NavMesh.AllAreas, path)
                    || path.status != UnityEngine.AI.NavMeshPathStatus.PathComplete)
                {
                    found.Add($"NPC_ROUTE:{name}:no_navmesh_path @{from.x:F2},{from.z:F2}->{to.x:F2},{to.z:F2}");
                    return;
                }
                var raw = path.corners;
                if (carrying) NpcAgent.Widen(raw, raw.Length, agent.bodyRadius + 0.08f, agent.transform);
                var corners = raw.Select(c => new Vector3(c.x, from.y, c.z)).ToList();
                corners[0] = from; corners[corners.Count - 1] = to;
                Route(name, corners.ToArray(), agent, carrying, workEnd);
            }
            var meals = Object.FindAnyObjectByType<MealPots>();
            var stands = meals.hobs.Select(h => new Vector3(h.position.x, 0f, h.position.z + (h.position.z > 0f ? -1f : 1f) * (0.5f + cook.standOff))).ToArray();
            for (int i = 0; i < stands.Length; i++)
            {
                Nav($"cook.watch->hob{i}", cook.watchSpot, stands[i], cookAgent, false);
                Nav($"cook.hob{i}->tub", stands[i], cook.tubStand, cookAgent, false);
                Nav($"cook.tub->hob{i}", cook.tubStand, stands[i], cookAgent, true, workEnd: true);
            }
            var coal = helper.coal;
            helper.ScoopStand(out var scoopStand, out _);
            for (int i = 0; i < coal.cells.Length; i++)
            {
                helper.FeedStand(coal.cells[i].mouth.transform, out var mouthStand, out _);
                Nav($"helper.coal->mouth{i}", scoopStand, mouthStand, helperAgent, true);
                Nav($"helper.mouth{i}->coal", mouthStand, scoopStand, helperAgent, true);
            }
            Nav("helper.coal->watch", scoopStand, helper.watchSpot, helperAgent, false);
            Route("cast.deckToGalley", cast.deckToGalley, null, false);
            Route("cast.corridorToGalley", cast.corridorToGalley, null, false);
            string problems = Report("npc_route_scene", found.Distinct().ToList(), "NPC_ROUTE:");
            Assert.IsEmpty(problems, problems);
        }

        /// <summary>
        /// Vùng nhắm (collider không hình vẽ trên layer Interactable) mà để đặc thì thành tường vô hình: chặn người chơi
        /// (mọi layer va nhau) và NPC (NpcPathing coi là vật cản). Interactor bắn tia có tính trigger, nên vùng nhắm phải là trigger.
        /// </summary>
        [UnityTest, Timeout(180000)]
        public IEnumerator AimVolumesAreNotInvisibleWalls()
        {
            yield return ChoreTestUtil.Galley();
            int interactable = LayerMask.NameToLayer("Interactable");
            var found = new List<string>();
            foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (!c.enabled || c.isTrigger || !c.gameObject.activeInHierarchy || c.gameObject.layer != interactable || !IsStatic(c)) continue;
                if (c.GetComponentsInChildren<Renderer>().Any(r => r.enabled && !(r is ParticleSystemRenderer))) continue;
                // Va chạm của vật kéo/nhấc được (nồi cao) nằm cạnh hình vẽ, cùng Rigidbody: hình nghiêng khi kéo còn hộp va chạm đứng yên.
                if (c.attachedRigidbody != null && c.attachedRigidbody.GetComponentsInChildren<Renderer>().Any(r => r.enabled && !(r is ParticleSystemRenderer))) continue;
                var b = c.bounds;
                // Chỉ tính phần chắn ngang tầm thân người đứng trên sàn bếp (0,3–1,75 m).
                if (b.max.y < 0.3f || b.min.y > 1.75f) continue;
                found.Add($"INVISIBLE:{c.name}:{b.size.x:F2}x{b.size.y:F2}x{b.size.z:F2} @{b.center.x:F2},{b.center.y:F2},{b.center.z:F2}");
            }
            found.Sort(System.StringComparer.Ordinal);
            string problems = Report("invisible_scene", found, "INVISIBLE:");
            Assert.IsEmpty(problems, problems);
        }

        /// <summary>Tên vật cố định chứa điểm đứng (cùng phép thử với NpcPathing.FreeAt: trên bậc 0,3 m, tới 1,4 m).</summary>
        static string InsideWhat(Vector3 p, float radius, Transform self)
        {
            var names = Physics.OverlapCapsule(p + Vector3.up * (0.3f + radius), p + Vector3.up * 1.4f, radius * 0.9f, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore).Where(c => NpcPathing.IsObstacle(c, self, p.y)).Select(c => c.name).Distinct();
            return string.Join("+", names);
        }

        /// <summary>Tâm các ô bếp: khoảng hở giữa hai thanh "HotplateBar" liền nhau trên mặt mỗi lò (FireBuilder.CastRange).</summary>
        static List<Vector3> HobCenters()
        {
            var centers = new List<Vector3>();
            foreach (var front in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name == "HotplateFront"))
            {
                var root = front.parent;
                var bars = root.Cast<Transform>().Where(t => t.name == "HotplateBar")
                    .Select(t => (lo: t.localPosition.x - t.localScale.x * 0.5f, hi: t.localPosition.x + t.localScale.x * 0.5f))
                    .OrderBy(b => b.lo).ToList();
                for (int i = 0; i + 1 < bars.Count; i++)
                    centers.Add(root.TransformPoint(new Vector3((bars[i].hi + bars[i + 1].lo) * 0.5f, front.localPosition.y, 0f)));
            }
            return centers;
        }

        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        /// <summary>
        /// POT_HOB (W3.2): mỗi ô bếp một anchor Hob nằm đúng tâm ô; mọi bữa trong days.vi.json (nồi bữa và nồi cao) đặt mỗi nồi
        /// vào một ô riêng, đúng tâm ô.
        /// </summary>
        [UnityTest, Timeout(180000)]
        public IEnumerator CookingPotsSitOnHobCentres()
        {
            yield return ChoreTestUtil.Galley();
            var centers = HobCenters();
            Assert.GreaterOrEqual(centers.Count, 6, "hai lò, mỗi lò ba ô");
            var meals = Object.FindAnyObjectByType<MealPots>();
            var found = new List<string>();
            if (meals.hobs.Length != centers.Count) found.Add($"POT_HOB:anchors:{meals.hobs.Length} anchor cho {centers.Count} ô");
            foreach (var hob in meals.hobs)
            {
                float off = centers.Min(c => Flat(c, hob.position));
                if (off > 0.02f) found.Add($"POT_HOB:{hob.name}:anchor lệch {off:F2} m khỏi tâm ô");
            }
            var meal = meals;
            meal.cookTimeout = 0.5f;
            foreach (var day in GameRoot.Instance.Content.Days.Days)
                foreach (var phase in day.Phases.Where(p => p.Dirty > 0 || p.CookTall))
                {
                    meal.BeginMeal(phase, instant: true); // không chờ người chơi nhấc nồi cao lên bếp (brush17)
                    var placed = meal.CookingPots.Select(p => (p.name, p.transform.position)).ToList();
                    if (phase.CookTall) placed.Add(("TallPot", meal.tall.potRoot.position));
                    var taken = new Dictionary<int, string>();
                    foreach (var (name, at) in placed)
                    {
                        int best = Enumerable.Range(0, centers.Count).OrderBy(i => Flat(centers[i], at)).First();
                        float off = Flat(centers[best], at);
                        if (off > 0.03f) found.Add($"POT_HOB:D{day.Number}.{phase.Id}:{name}:lệch {off:F2} m khỏi tâm ô");
                        else if (taken.TryGetValue(best, out var other)) found.Add($"POT_HOB:D{day.Number}.{phase.Id}:{name}:chung ô với {other}");
                        else taken[best] = name;
                    }
                    yield return meal.Serve(instant: true);
                }
            string problems = Report("pot_hob_scene", found, "POT_HOB:");
            Assert.IsEmpty(problems, problems);
        }

        static Bounds? RenderBounds(Transform root)
        {
            Bounds? b = null;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue;
                if (b == null) b = r.bounds; else { var x = b.Value; x.Encapsulate(r.bounds); b = x; }
            }
            return b;
        }

        /// <summary>Độ chồng của hai hộp bao theo trục chồng ít nhất (âm: hai hộp rời nhau).</summary>
        static float BoxDepth(Bounds a, Bounds b)
        {
            float dx = Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x);
            float dy = Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y);
            float dz = Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z);
            return Mathf.Min(dx, Mathf.Min(dy, dz));
        }

        /// <summary>
        /// OVERLAP/SUPPORT tạm (chưa có anchor, đo theo tọa độ + bounds). Vật bê được (cả đồ chỉ bật theo lượt như khay phục vụ,
        /// thùng cá: bật lên ở chỗ dựng sẵn) và nồi phải nằm trên một mặt (đáy cách mặt đỡ > 2 cm WARN, > 5 cm ERROR) và không lún
        /// vào nhau (collider chồng > 3 cm ERROR, > 1 cm WARN). Đồ thao tác có hình trên tuyến chơi (rổ, thớt, bát… có TaskTarget)
        /// thường không có collider đặc: cặp có nó đo bằng hộp bao hình vẽ, chồng quá 3 cm theo cả ba trục là ERROR.
        /// </summary>
        [UnityTest, Timeout(180000)]
        public IEnumerator KeyPropsRestOnASurfaceAndDoNotInterpenetrate()
        {
            yield return ChoreTestUtil.Galley();
            // Đồ theo lượt tắt sẵn ở chỗ dựng (VoyageStoryBuilder.Delivery): bật lên để kiểm chỗ đặt; cảnh test bỏ sau đó.
            var byPhase = new HashSet<Transform>();
            foreach (var h in Object.FindObjectsByType<Holdable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!h.gameObject.activeSelf && h.transform.parent != null && h.transform.parent.gameObject.activeInHierarchy)
                {
                    h.gameObject.SetActive(true);
                    byPhase.Add(h.transform);
                }
            // Bàn gọt khoai (thớt, xô, chậu vỏ, nồi nước, đĩa vỏ của ông Mai) chỉ bật khi có lệnh gọt; bàn măng tây cùng chỗ, khác ngày.
            foreach (var peel in Object.FindObjectsByType<PeelStation>(FindObjectsSortMode.None))
                if (!peel.asparagus) peel.board.gameObject.SetActive(true);
            // Sọt rau mang từ hầm lên đặt ở đầu tây bàn (GreensStation gắn sọt vào rawTablePoint): kiểm như vật bê được.
            var greens = Object.FindAnyObjectByType<GreensStation>();
            greens.rawBasket.SetParent(greens.rawTablePoint, false);
            greens.rawBasket.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            yield return new WaitForSeconds(1f);
            var roots = new List<Transform>();
            foreach (var h in Object.FindObjectsByType<Holdable>(FindObjectsSortMode.None))
                if (h.isActiveAndEnabled && !h.IsHeld) roots.Add(h.transform);
            var meals = Object.FindAnyObjectByType<MealPots>();
            foreach (var pot in meals.pots) if (pot.activeInHierarchy && !roots.Contains(pot.transform)) roots.Add(pot.transform);
            roots.Add(greens.rawBasket);
            int movable = roots.Count;
            // Đồ thao tác có hình; vùng nhắm không hình bỏ qua. Cụm nhiều món rời (bàn gọt: thớt, xô, chậu, nồi, đĩa, dao) có hộp
            // bao chung rộng hơn 1,2 m hoặc các món chỉ chiếm dưới 35% hộp chung thì xét từng món con.
            bool Big(Bounds bb) => Mathf.Max(bb.size.x, Mathf.Max(bb.size.y, bb.size.z)) > 1.2f;
            float Volume(Bounds bb) => bb.size.x * bb.size.y * bb.size.z;
            bool Taken(Transform t) => roots.Any(r => t.IsChildOf(r) || r.IsChildOf(t));
            foreach (var target in Object.FindObjectsByType<TaskTarget>(FindObjectsSortMode.None).OrderBy(t => t.name))
            {
                var t = target.transform;
                if (Taken(t)) continue;
                var tb = RenderBounds(t);
                if (tb == null) continue;
                var parts = t.Cast<Transform>().Select(c => (c, b: RenderBounds(c))).Where(p => p.b != null).ToList();
                bool sparse = parts.Count > 1 && parts.Sum(p => Volume(p.b.Value)) < 0.35f * Volume(tb.Value);
                if (!Big(tb.Value) && !sparse) { roots.Add(t); continue; }
                foreach (var (child, cb) in parts)
                    if (!Big(cb.Value) && !Taken(child)) roots.Add(child);
            }
            var found = new List<string>();
            var hits = new RaycastHit[32];
            for (int ri = 0; ri < movable; ri++)
            {
                var root = roots[ri];
                var b = RenderBounds(root);
                if (b == null) continue;
                var bounds = b.Value;
                float best = float.PositiveInfinity;
                string under = "";
                for (int sample = 0; sample < 5; sample++)
                {
                    float x = sample == 1 ? 0.6f : sample == 2 ? -0.6f : 0f, z = sample == 3 ? 0.6f : sample == 4 ? -0.6f : 0f;
                    var origin = new Vector3(bounds.center.x + bounds.extents.x * x, bounds.min.y + 0.03f, bounds.center.z + bounds.extents.z * z);
                    int n = Physics.RaycastNonAlloc(origin, Vector3.down, hits, 3f, ~(1 << 2), QueryTriggerInteraction.Ignore);
                    float top = float.NegativeInfinity;
                    string name = "";
                    for (int i = 0; i < n; i++)
                    {
                        if (hits[i].transform.IsChildOf(root)) continue;
                        if (hits[i].point.y > top) { top = hits[i].point.y; name = hits[i].collider.name; }
                    }
                    float gap = bounds.min.y - top;
                    if (gap < best) { best = gap; under = name; }
                }
                if (best > 0.05f) found.Add($"SUPPORT:{root.name}:đáy cách mặt đỡ {best:F3} m ({under})");
                else if (best > 0.02f) found.Add($"SUPPORT_WARN:{root.name}:đáy cách mặt đỡ {best:F3} m ({under})");
            }
            // Lún vào nhau: từng cặp của hai vật khác nhau.
            var colliders = roots.ToDictionary(r => r, r => r.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToArray());
            var boxes = roots.ToDictionary(r => r, r => RenderBounds(r));
            for (int i = 0; i < roots.Count; i++)
                for (int j = i + 1; j < roots.Count; j++)
                {
                    if (roots[i].IsChildOf(roots[j]) || roots[j].IsChildOf(roots[i])) continue;
                    // Hai đồ theo lượt khác nhau (khay phục vụ ban ngày, cốc cà phê buổi tối) không bao giờ cùng nằm ở chỗ dựng.
                    if (byPhase.Contains(roots[i]) && byPhase.Contains(roots[j])) continue;
                    float deepest = 0f;
                    if (i < movable && j < movable)
                    {
                        foreach (var a in colliders[roots[i]])
                            foreach (var c in colliders[roots[j]])
                                if (a.bounds.Intersects(c.bounds) && Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, c, c.transform.position, c.transform.rotation, out _, out float depth))
                                    deepest = Mathf.Max(deepest, depth);
                    }
                    else if (boxes[roots[i]] != null && boxes[roots[j]] != null)
                        deepest = Mathf.Max(0f, BoxDepth(boxes[roots[i]].Value, boxes[roots[j]].Value));
                    string pair = string.CompareOrdinal(roots[i].name, roots[j].name) < 0 ? roots[i].name + "~" + roots[j].name : roots[j].name + "~" + roots[i].name;
                    if (deepest > 0.03f) found.Add($"OVERLAP:{pair}:lún {deepest:F3} m");
                    // Tấm ván, đai, chân của cùng một món (đã tách khỏi cụm) chạm nhau là chuyện ghép; chỉ báo khi lún hẳn.
                    else if (deepest > 0.01f && roots[i].parent != roots[j].parent) found.Add($"OVERLAP_WARN:{pair}:lún {deepest:F3} m");
                }
            string problems = Report("props_scene", found.Where(f => f.StartsWith("OVERLAP:") || f.StartsWith("SUPPORT:")).ToList(), "OVERLAP:", "SUPPORT:");
            File.AppendAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Logs", "oracle", "props_scene.txt")),
                string.Join("\n", found.Where(f => f.Contains("_WARN:"))) + $"\n# đã kiểm {roots.Count} vật ({movable} bê được/nồi): {string.Join(", ", roots.Select(r => r.name))}\n");
            Assert.IsEmpty(problems, problems);
        }
    }
}
