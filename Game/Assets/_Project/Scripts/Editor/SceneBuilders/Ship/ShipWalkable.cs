using System;
using System.Collections.Generic;
using System.Linq;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Ship;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders.Ship
{
    /// <summary>
    /// Va chạm đi lại trên cả con tàu theo deck_zones.json v2, thay cho sàn và tường vô hình cũ (ShipDeck):
    /// mỗi vùng boong một sàn (đa giác trừ lỗ sàn, theo đúng cao độ và độ cong), một khối vật cản (hộp có hướng), một khối
    /// thành chắn (lan can, thành mạn; mép mở để trống). Cầu thang: dốc ẩn đi được bằng CharacterController đặt trên bậc
    /// nhìn thấy (bậc của model, riêng thang hầm do Unity dựng), hai bên có tay vịn chặn rơi.
    /// Mọi tọa độ ở hệ cục bộ Unity của gốc tàu.
    /// </summary>
    public static class ShipWalkable
    {
        public const string MeshFolder = "Assets/_Project/Art/Generated/Ship";
        public const string HoldStairsId = "hold_stairs";
        /// <summary>Bề dày thành chắn dựng từ các cạnh kín.</summary>
        const float EdgeThickness = 0.1f;
        /// <summary>Bề dày tấm dốc ẩn của cầu thang.</summary>
        const float RampThickness = 0.07f;
        /// <summary>Dốc kéo dài thêm ở mỗi đầu để chân không vấp mép sàn.</summary>
        const float RampOverhang = 0.09f;
        /// <summary>Tay vịn ẩn hai bên cầu thang, cao hơn mặt dốc.</summary>
        const float StairRailHeight = 1.0f;
        /// <summary>
        /// Khoảng hở dưới collider của vật cản đứng trên lỗ sàn ngoài thang hầm (lỗ thang boong xuồng): người đi trên thang
        /// chui qua mép dưới.
        /// </summary>
        const float HoleHeadroom = 0.4f;
        /// <summary>
        /// Khoảng đầu cần trên thang hầm, đo thẳng đứng từ mặt dốc ẩn tại mép vật cản phía đỉnh thang: capsule đứng trên dốc
        /// 38,7° cao hơn mặt dốc 0,18 m (kể cả skin), mép cầu trước của đầu nhô 2,11 m trên mặt dốc; mỗi lần Move PhysX còn nhấc
        /// capsule lên stepOffset 0,3 m trước khi quét ngang; dư 5 cm. Collider thấp hơn mức này làm người xuống thang kẹt
        /// dưới mép vách trước buồng thang (Blender x 8,36), thành miệng hầm hàng (8,81), mép sàn boong và trần hầm.
        /// </summary>
        public const float HoldHeadClearance = 2.46f;
        /// <summary>Nửa bề rộng hành lang đầu người trên thang hầm: tới mặt trong vách hai bên buồng thang (0,66; thang rộng 1,1 m).</summary>
        const float HoldCorridorHalfWidth = 0.66f;

        /// <summary>
        /// Vật cản model nằm ngang tầm đầu trên nửa trong lòng cầu thang boong xuồng: la bàn hai cánh buồng lái
        /// (bridge_binnacle_±1, Blender |y| 5,185…5,615, z 11,35…12,29) chồng lên thang |y| 5,1…6,1. Collider la bàn hẹp bớt
        /// 0,2 m phía mạn (hình model giữ nguyên) để còn làn |y| 5,415…6,1 rộng 0,685 m cho capsule 0,56 m.
        /// </summary>
        static readonly Dictionary<string, float> OutboardTrim = new Dictionary<string, float>
        {
            ["bridge_binnacle_-1"] = 0.2f,
            ["bridge_binnacle_1"] = 0.2f,
        };

        /// <summary>Làn đi thông lệch về phía mạn so với đường giữa thang (giữa làn |y| 5,415…6,1).</summary>
        static readonly Dictionary<string, float> OutboardLane = new Dictionary<string, float>
        {
            ["boat_deck_stairs_-1"] = 0.1575f,
            ["boat_deck_stairs_1"] = 0.1575f,
        };

        public static Transform Build(Transform ship, JObject data)
        {
            var root = BuilderUtils.Group("ShipWalkable", ship).transform;
            var seen = new HashSet<string>();
            foreach (var pair in DeckZones.Zones(data))
            {
                var deck = BuilderUtils.Group(pair.Key, root).transform;
                var holes = Holes(data, pair.Key, pair.Value);
                BuildFloor(deck, pair.Key, pair.Value, holes);
                BuildObstacles(deck, pair.Key, data, pair.Value, holes, seen);
                BuildEdges(deck, pair.Key, pair.Value);
            }
            return root;
        }

        /// <summary>
        /// Lỗ sàn của vùng theo deck_zones.json. Collider sàn quanh lỗ thang hầm lùi mép phía mũi tới chỗ đầu người xuống thang
        /// không còn chạm mặt boong (HoldClearX); hình model giữ nguyên. Phần nới nằm dưới thành miệng hầm hàng nên không ai
        /// rơi xuống: khe giữa vách buồng thang và thành miệng hầm (0,41 m) hẹp hơn capsule.
        /// </summary>
        public static Vector2[][] Holes(JObject data, string zoneId, JObject zone)
        {
            var holes = zone["deck_holes"]?.Select(DeckZones.Polygon).ToArray() ?? Array.Empty<Vector2[]>();
            var link = DeckZones.Connection(data, HoldStairsId);
            if (link == null || (string)link["from"] != zoneId)
                return holes;
            var middle = (DeckZones.Point2(link["bottom"]) + DeckZones.Point2(link["top"])) * 0.5f;
            float clear = HoldClearX(link, (float)zone["deck_surface_z"]) + 0.02f;
            foreach (var hole in holes)
            {
                if (!DeckZones.Contains(hole, middle))
                    continue;
                float far = hole.Max(p => p.x);
                for (int i = 0; i < hole.Length; i++)
                {
                    if (Mathf.Abs(hole[i].x - far) < 1e-3f)
                        hole[i].x = Mathf.Max(far, clear);
                }
            }
            return holes;
        }

        /// <summary>Cao độ (Unity y) mặt dốc ẩn của thang hầm tại x, theo đường chân bậc; ngoài thang thì lấy đầu gần nhất.</summary>
        public static float HoldRampHeight(JToken link, float x)
        {
            var top = DeckZones.Point(link["top"]);
            var bottom = DeckZones.Point(link["bottom"]);
            return Mathf.Lerp(top.y, bottom.y, Mathf.InverseLerp(top.x, bottom.x, x));
        }

        /// <summary>
        /// x nhỏ nhất (thang hầm xuống theo +x) mà một mặt dưới ở cao độ height không chạm đầu người đi trên thang
        /// (HoldHeadClearance). Dùng cho mép sàn boong, vật cản trên miệng thang và collider trần hầm.
        /// </summary>
        public static float HoldClearX(JToken link, float height)
        {
            var top = DeckZones.Point(link["top"]);
            var bottom = DeckZones.Point(link["bottom"]);
            float t = (top.y + HoldHeadClearance - height) / (top.y - bottom.y);
            return Mathf.LerpUnclamped(top.x, bottom.x, t);
        }

        static void BuildFloor(Transform deck, string zoneId, JObject zone, Vector2[][] holes)
        {
            var combine = new List<CombineInstance>();
            foreach (var polygon in zone["walkable_polygons"])
            {
                var mesh = FloorMesh(DeckZones.Polygon(polygon), holes, zone);
                combine.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.identity });
            }
            var floor = new Mesh { name = zoneId + "_floor" };
            floor.CombineMeshes(combine.ToArray(), true, false);
            foreach (var c in combine)
                UnityEngine.Object.DestroyImmediate(c.mesh);
            var go = BuilderUtils.Group("Floor", deck);
            go.AddComponent<MeshCollider>().sharedMesh = SaveMesh(floor, zoneId + "_floor");
        }

        /// <summary>
        /// Vật cản boong (tời, miệng hầm, cột, ống thông gió, xuồng…) thành một mesh va chạm; bỏ hộp bao cầu thang. Vật cản
        /// đứng phía trên thang hầm (vách trước buồng thang, đầu thành miệng hầm hàng) được nâng mép dưới collider cho đủ
        /// khoảng đầu (HoldHeadClearance); hộp dài thì chỉ nâng đoạn gần thang, đoạn còn lại giữ nguyên. Hình model giữ nguyên.
        /// </summary>
        static void BuildObstacles(Transform deck, string zoneId, JObject data, JObject zone, Vector2[][] holes, HashSet<string> seen)
        {
            var link = DeckZones.Connection(data, HoldStairsId);
            bool holdZone = link != null && (string)link["from"] == zoneId;
            var boxes = new List<(Vector3 center, Quaternion rotation, Vector3 size)>();
            foreach (var obstacle in zone["obstacles"])
            {
                string id = (string)obstacle["id"];
                if ((string)obstacle["kind"] == "stairs" || !seen.Add(id))
                    continue;
                var center = DeckZones.Point(obstacle["center_absolute"]);
                float yaw = (float)obstacle["yaw_deg"];
                var rotation = Quaternion.Euler(0f, ShipCoordinates.YawFromBlender(yaw), 0f);
                var size = ShipCoordinates.SizeFromBlender(DeckZones.Vector(obstacle["size"]));
                if (OutboardTrim.TryGetValue(id, out float trim))
                {
                    // Hộp không xoay (yaw 0): mặt phía mạn lùi vào trong một đoạn trim.
                    center.z -= Mathf.Sign(center.z) * trim * 0.5f;
                    size.z -= trim;
                }
                if (holdZone && Mathf.Abs(yaw) < 0.01f && AboveHoldStairs(link, center, size))
                {
                    ClipOverHoldStairs(link, center, size, boxes);
                    continue;
                }
                // Vật cản đứng trên miệng lỗ sàn khác (thang boong xuồng): collider bắt đầu cao hơn mặt boong một đoạn.
                if (holes.Any(h => DeckZones.Contains(h, DeckZones.Point2(obstacle["center_absolute"]))))
                {
                    center.y += HoleHeadroom * 0.5f;
                    size.y = Mathf.Max(0.05f, size.y - HoleHeadroom);
                }
                boxes.Add((center, rotation, size));
            }
            if (boxes.Count == 0)
                return;
            var go = BuilderUtils.Group("Obstacles", deck);
            go.AddComponent<MeshCollider>().sharedMesh = SaveMesh(BoxesMesh(boxes, zoneId + "_obstacles"), zoneId + "_obstacles");
        }

        /// <summary>Hộp (không xoay) nằm phía trên lòng thang hầm, tính từ đỉnh thang trở về phía chân thang.</summary>
        static bool AboveHoldStairs(JToken link, Vector3 center, Vector3 size)
        {
            var top = DeckZones.Point(link["top"]);
            var bottom = DeckZones.Point(link["bottom"]);
            float minX = center.x - size.x * 0.5f;
            float maxX = center.x + size.x * 0.5f;
            bool alongStairs = minX >= top.x - 1e-3f && minX < bottom.x && maxX > top.x;
            bool acrossStairs = Mathf.Abs(center.z - top.z) - size.z * 0.5f < HoldCorridorHalfWidth;
            return alongStairs && acrossStairs;
        }

        /// <summary>
        /// Nâng mép dưới collider của hộp phía trên thang hầm tới khoảng đầu cần tại mép gần đỉnh thang. Hộp kéo dài qua chỗ
        /// đã đủ khoảng đầu (HoldClearX) thì tách: đoạn gần thang nâng lên, đoạn xa giữ nguyên. Đoạn nâng hết chiều cao thì bỏ.
        /// </summary>
        static void ClipOverHoldStairs(JToken link, Vector3 center, Vector3 size, List<(Vector3 center, Quaternion rotation, Vector3 size)> boxes)
        {
            float minX = center.x - size.x * 0.5f;
            float maxX = center.x + size.x * 0.5f;
            float bottomY = center.y - size.y * 0.5f;
            float topY = center.y + size.y * 0.5f;
            float required = HoldRampHeight(link, minX) + HoldHeadClearance;
            if (bottomY >= required)
            {
                boxes.Add((center, Quaternion.identity, size));
                return;
            }
            float split = Mathf.Min(maxX, HoldClearX(link, bottomY));
            if (split < maxX)
            {
                float farLength = maxX - split;
                boxes.Add((new Vector3(split + farLength * 0.5f, center.y, center.z), Quaternion.identity, new Vector3(farLength, size.y, size.z)));
            }
            if (topY - required < 0.05f)
                return;
            float nearLength = split - minX;
            boxes.Add((new Vector3(minX + nearLength * 0.5f, (required + topY) * 0.5f, center.z), Quaternion.identity,
                new Vector3(nearLength, topY - required, size.z)));
        }

        /// <summary>Thành chắn cao theo height dọc mọi cạnh kín; cạnh open (lối cầu thang, cửa, mép cầu tàu) để trống.</summary>
        static void BuildEdges(Transform deck, string zoneId, JObject zone)
        {
            var boxes = new List<(Vector3 center, Quaternion rotation, Vector3 size)>();
            foreach (var edge in zone["edges"])
            {
                if ((bool?)edge["open"] == true)
                    continue;
                float height = (float)edge["height"];
                var points = DeckZones.Polygon(edge["points"]);
                for (int i = 1; i < points.Length; i++)
                {
                    var a = points[i - 1];
                    var b = points[i];
                    float length = Vector2.Distance(a, b);
                    if (length < 0.001f)
                        continue;
                    var mid = (a + b) * 0.5f;
                    float surface = DeckZones.Surface(zone, mid.x);
                    var center = new Vector3(mid.x, surface + height * 0.5f, mid.y);
                    var rotation = Quaternion.LookRotation(new Vector3(b.x - a.x, 0f, b.y - a.y));
                    boxes.Add((center, rotation, new Vector3(EdgeThickness, height, length + EdgeThickness)));
                }
            }
            if (boxes.Count == 0)
                return;
            var go = BuilderUtils.Group("Edges", deck);
            go.AddComponent<MeshCollider>().sharedMesh = SaveMesh(BoxesMesh(boxes, zoneId + "_edges"), zoneId + "_edges");
        }

        /// <summary>
        /// Mọi lối nối (connections): cầu thang có dốc ẩn cao hơn đường nối chân–đỉnh nửa bậc nên mặt dốc nằm giữa mũi bậc
        /// và hõm bậc nhìn thấy, tay vịn ẩn hai bên; thang hầm dựng thêm bậc, cốn thang và tay vịn nhìn thấy. Lối đồng mức
        /// (0 bậc) chỉ có điểm đánh dấu. Mỗi lối mang một ShipStair để test đi bộ và NPC đọc.
        /// </summary>
        public static Transform BuildStairs(Transform ship, JObject data, Material planks, Material rail)
        {
            var root = BuilderUtils.Group("ShipStairs", ship).transform;
            foreach (var link in data["connections"])
            {
                int steps = (int)link["steps"];
                string id = (string)link["id"];
                var bottom = DeckZones.Point(link["bottom"]);
                var top = DeckZones.Point(link["top"]);
                float width = (float)link["width"];
                GameObject stair;
                if (steps > 0)
                {
                    // Mặt dốc qua giữa mũi bậc và hõm bậc (cao hơn đường chân–đỉnh nửa bậc). Thay vì nâng đường chân–đỉnh lên
                    // nửa bậc, dời nó về phía chân nửa bước ngang: cùng một mặt dốc nhưng hai đầu trùng đúng cao độ hai sàn,
                    // đỉnh dốc không nhô thành bậc dưới cửa thấp (cửa buồng thang hầm cao 2,0 m).
                    // Riêng thang hầm (bậc do Unity dựng, không có collider): dốc đi theo đường chân bậc, thấp hơn 0,1 m. Capsule
                    // đứng trên dốc 38,7° cao hơn mặt dốc 0,18 m (kể cả skin) và mép cầu trước của đầu nhô cao 2,11 m trên mặt
                    // dốc; dốc giữa bậc chạm mép dưới vách trước buồng thang (6,0) và thành miệng hầm hàng (Blender x 8,81).
                    var halfRun = id == HoldStairsId ? Vector3.zero : Vector3.ProjectOnPlane(top - bottom, Vector3.up) / steps * 0.5f;
                    stair = Ramp(root, id, bottom - halfRun, top - halfRun, width);
                    SideRails(stair.transform, width, Vector3.Distance(bottom, top));
                    if (id == HoldStairsId)
                        VisibleSteps(root, link, bottom, top, width, planks, rail);
                }
                else
                {
                    stair = BuilderUtils.Group(id, root);
                    stair.transform.localPosition = (bottom + top) * 0.5f;
                }
                var info = stair.AddComponent<ShipStair>();
                info.id = id;
                info.fromZone = (string)link["from"];
                info.toZone = (string)link["to"];
                info.bottom = bottom;
                info.top = top;
                info.width = width;
                info.steps = steps;
                if (OutboardLane.TryGetValue(id, out float lane))
                    info.lane = new Vector3(0f, 0f, Mathf.Sign(bottom.z) * lane);
            }
            return root;
        }

        /// <summary>Tấm dốc ẩn từ a tới b (trục +Z của nhóm hướng từ a lên b); collider đi được, không có hình.</summary>
        public static GameObject Ramp(Transform parent, string name, Vector3 a, Vector3 b, float width)
        {
            var root = BuilderUtils.Group(name, parent);
            root.transform.localPosition = (a + b) * 0.5f;
            root.transform.localRotation = Quaternion.LookRotation(b - a);
            var slab = BuilderUtils.Group("Ramp", root.transform);
            slab.transform.localPosition = new Vector3(0f, -RampThickness * 0.5f, 0f);
            var box = slab.AddComponent<BoxCollider>();
            box.size = new Vector3(width, RampThickness, Vector3.Distance(a, b) + RampOverhang * 2f);
            return root;
        }

        /// <summary>Tay vịn ẩn hai bên dốc: không rơi khỏi mép thang giữa chừng. Hộp nghiêng theo dốc nên góc trên ở đầu chân
        /// chìa ra trước chân thang (cao × tan dốc, ~0,7 m ở dốc 35°) ngang ngực, chặn người đi vòng vào chân thang:
        /// cắt bớt đoạn đó ở đầu chân.</summary>
        static void SideRails(Transform stair, float width, float length)
        {
            float tilt = Mathf.Asin(Mathf.Clamp((stair.localRotation * Vector3.forward).y, -1f, 1f));
            float trim = Mathf.Min(StairRailHeight * Mathf.Tan(Mathf.Abs(tilt)), length * 0.5f);
            foreach (float side in new[] { -1f, 1f })
            {
                var rail = BuilderUtils.Group("SideRail", stair);
                rail.transform.localPosition = new Vector3(side * (width * 0.5f + EdgeThickness * 0.5f), StairRailHeight * 0.5f, trim * 0.5f);
                rail.AddComponent<BoxCollider>().size = new Vector3(EdgeThickness, StairRailHeight, length - trim);
            }
        }

        /// <summary>Thanh hình hộp nối a–b (tay vịn, cốn thang).</summary>
        public static GameObject Beam(Transform parent, string name, Vector3 a, Vector3 b, float thickness, Material material)
        {
            var go = BuilderUtils.Block(name, parent, (a + b) * 0.5f, new Vector3(thickness, thickness, Vector3.Distance(a, b)), material, true, false);
            go.transform.localRotation = Quaternion.LookRotation(b - a);
            return go;
        }

        /// <summary>Thang hầm 14 bậc (rise 0,20; run 0,25): mặt bậc gỗ, hai cốn thang, tay vịn có trụ. Bậc i (đếm từ chân)
        /// có mặt ở độ cao chân + (i + 1) × rise.</summary>
        static void VisibleSteps(Transform root, JToken link, Vector3 bottom, Vector3 top, float width, Material planks, Material rail)
        {
            var group = BuilderUtils.Group("HoldStairs_Visual", root).transform;
            int steps = (int)link["steps"];
            float rise = link["step_rise"] != null ? (float)link["step_rise"] : (top.y - bottom.y) / steps;
            var run = (top - bottom) / steps;
            run.y = 0f;
            const float tread = 0.05f;
            for (int i = 0; i < steps; i++)
            {
                var center = bottom + run * (i + 0.5f);
                center.y = bottom.y + rise * (i + 1) - tread * 0.5f;
                BuilderUtils.Block("Tread_" + i, group, center, new Vector3(run.magnitude + 0.04f, tread, width), planks, true, false);
            }
            foreach (float side in new[] { -1f, 1f })
            {
                var offset = new Vector3(0f, 0f, side * (width * 0.5f + 0.03f));
                // Cốn thang: thanh nghiêng đỡ đầu bậc.
                Beam(group, "Stringer", bottom + offset + Vector3.up * 0.1f, top + offset + Vector3.up * 0.1f, 0.06f, planks);
                var railA = bottom + offset + Vector3.up * 0.95f;
                var railB = top + offset + Vector3.up * 0.95f;
                Beam(group, "Handrail", railA, railB, 0.045f, rail);
                for (int i = 0; i <= 3; i++)
                {
                    var foot = Vector3.Lerp(bottom, top, i / 3f) + offset;
                    var post = BuilderUtils.Block("Post", group, foot + Vector3.up * 0.5f, new Vector3(0.04f, 0.95f, 0.04f), rail, true, false);
                    post.name = "HandrailPost";
                }
            }
            // Thang nằm trong buồng thang có mái và trong hầm: chỉ nhận đèn trong khoang (mặt trời không xuyên boong).
            BuilderUtils.SetRenderingLayer(group.gameObject, WorldRenderingLayers.Interior);
        }

        /// <summary>
        /// Phân hoạch hình thang chính xác theo cạnh đa giác và lỗ sàn (không xấp xỉ bằng lưới): cắt theo mọi hoành độ đỉnh,
        /// giao điểm cạnh và mẫu độ cong; giữa hai cạnh liền nhau trong một dải là một hình thang nằm trong sàn.
        /// </summary>
        public static Mesh FloorMesh(Vector2[] outline, Vector2[][] holes, JToken zone)
        {
            var loops = new[] { outline }.Concat(holes).ToArray();
            var segments = new List<(Vector2 a, Vector2 b)>();
            var cuts = new SortedSet<float>();
            foreach (var loop in loops)
                for (int i = 0; i < loop.Length; i++)
                {
                    var a = loop[i];
                    var b = loop[(i + 1) % loop.Length];
                    cuts.Add(a.x);
                    if (Mathf.Abs(a.x - b.x) > 1e-5f)
                        segments.Add((a, b));
                }
            var samples = zone["surface_rule"]?["samples"];
            if (samples != null)
                foreach (var sample in samples)
                    cuts.Add((float)sample["x"]);
            // Giao điểm giữa các cạnh cũng là đường cắt (lỗ sàn có thể cắt ngang cạnh đa giác).
            for (int i = 0; i < segments.Count; i++)
                for (int j = i + 1; j < segments.Count; j++)
                {
                    if (Intersection(segments[i], segments[j], out float x))
                        cuts.Add(x);
                }

            var xs = cuts.ToArray();
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int s = 1; s < xs.Length; s++)
            {
                float x0 = xs[s - 1];
                float x1 = xs[s];
                if (x1 - x0 < 1e-5f)
                    continue;
                float mid = (x0 + x1) * 0.5f;
                var crossing = segments.Where(e => mid > Mathf.Min(e.a.x, e.b.x) && mid < Mathf.Max(e.a.x, e.b.x))
                    .OrderBy(e => YAt(e, mid)).ToArray();
                for (int k = 1; k < crossing.Length; k++)
                {
                    float y0 = YAt(crossing[k - 1], mid);
                    float y1 = YAt(crossing[k], mid);
                    var probe = new Vector2(mid, (y0 + y1) * 0.5f);
                    if (y1 - y0 < 1e-5f || !DeckZones.Contains(outline, probe) || holes.Any(h => DeckZones.Contains(h, probe)))
                        continue;
                    int v = vertices.Count;
                    float h0 = DeckZones.Surface(zone, x0);
                    float h1 = DeckZones.Surface(zone, x1);
                    vertices.Add(new Vector3(x0, h0, YAt(crossing[k - 1], x0)));
                    vertices.Add(new Vector3(x0, h0, YAt(crossing[k], x0)));
                    vertices.Add(new Vector3(x1, h1, YAt(crossing[k], x1)));
                    vertices.Add(new Vector3(x1, h1, YAt(crossing[k - 1], x1)));
                    // Thứ tự đỉnh cho pháp tuyến hướng lên (+Y): tia dò sàn từ trên xuống trúng mặt trước.
                    triangles.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
                }
            }
            var mesh = new Mesh { name = "ShipFloor" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static float YAt((Vector2 a, Vector2 b) e, float x) => Mathf.LerpUnclamped(e.a.y, e.b.y, (x - e.a.x) / (e.b.x - e.a.x));

        static bool Intersection((Vector2 a, Vector2 b) p, (Vector2 a, Vector2 b) q, out float x)
        {
            x = 0f;
            float dp = (p.b.y - p.a.y) / (p.b.x - p.a.x);
            float dq = (q.b.y - q.a.y) / (q.b.x - q.a.x);
            if (Mathf.Abs(dp - dq) < 1e-5f)
                return false;
            x = (q.a.y - dq * q.a.x - p.a.y + dp * p.a.x) / (dp - dq);
            float lo = Mathf.Max(Mathf.Min(p.a.x, p.b.x), Mathf.Min(q.a.x, q.b.x));
            float hi = Mathf.Min(Mathf.Max(p.a.x, p.b.x), Mathf.Max(q.a.x, q.b.x));
            return x > lo + 1e-5f && x < hi - 1e-5f;
        }

        /// <summary>Gộp các hộp có hướng thành một mesh kín (mặt hướng ra ngoài) cho MeshCollider.</summary>
        static Mesh BoxesMesh(List<(Vector3 center, Quaternion rotation, Vector3 size)> boxes, string name)
        {
            var vertices = new List<Vector3>(boxes.Count * 8);
            var triangles = new List<int>(boxes.Count * 36);
            int[] faces =
            {
                0, 2, 1, 0, 3, 2, // −Z
                4, 5, 6, 4, 6, 7, // +Z
                0, 1, 5, 0, 5, 4, // −Y
                3, 7, 6, 3, 6, 2, // +Y
                0, 4, 7, 0, 7, 3, // −X
                1, 2, 6, 1, 6, 5, // +X
            };
            foreach (var (center, rotation, size) in boxes)
            {
                int v = vertices.Count;
                var e = size * 0.5f;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i == 1 || i == 2 || i == 5 || i == 6) ? e.x : -e.x,
                        (i == 2 || i == 3 || i == 6 || i == 7) ? e.y : -e.y,
                        i >= 4 ? e.z : -e.z);
                    vertices.Add(center + rotation * corner);
                }
                foreach (int f in faces)
                    triangles.Add(v + f);
            }
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Lưu mesh sinh ra thành asset (scene chỉ tham chiếu); dựng lại thì ghi đè cùng đường dẫn, giữ GUID.</summary>
        public static Mesh SaveMesh(Mesh mesh, string name)
        {
            AssetFactory.EnsureFolder(MeshFolder);
            string path = MeshFolder + "/" + name + ".asset";
            mesh.name = name;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }
}
