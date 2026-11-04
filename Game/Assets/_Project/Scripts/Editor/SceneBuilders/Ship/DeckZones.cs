using System;
using System.Collections.Generic;
using System.Linq;
using HaiBanTay.Ship;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders.Ship
{
    /// <summary>
    /// Đọc deck_zones.json (schema 2) của model tàu v4. Builder chỉ đọc TextAsset đã chép vào Game (ArtImporter.RunShip),
    /// không đọc Staging lúc dựng cảnh. Tọa độ trong JSON là Blender; đổi sang Unity bằng ShipCoordinates.
    /// </summary>
    public static class DeckZones
    {
        public const string AssetPath = "Assets/_Project/Art/Models/Ship/deck_zones.json";
        public const int Schema = 2;

        public static JObject Read()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetPath);
            if (asset == null)
                throw new InvalidOperationException("Thiếu " + AssetPath + "; chạy ArtImporter.RunShip.");
            var data = JObject.Parse(asset.text);
            if ((int)data["schema_version"] != Schema)
                throw new InvalidOperationException("deck_zones.json phải có schema " + Schema + ".");
            return data;
        }

        /// <summary>Điểm Blender [x, y, z] nguyên dạng.</summary>
        public static Vector3 Vector(JToken p) => new Vector3((float)p[0], (float)p[1], (float)p[2]);

        /// <summary>Điểm Blender sang hệ cục bộ Unity của gốc tàu.</summary>
        public static Vector3 Point(JToken p) => ShipCoordinates.FromBlender(Vector(p));

        /// <summary>Điểm 2D trên mặt boong (Blender x, y).</summary>
        public static Vector2 Point2(JToken p) => new Vector2((float)p[0], (float)p[1]);

        public static Vector2[] Polygon(JToken points) => points.Select(Point2).ToArray();

        public static JObject Opening(JObject data, string id) =>
            data["openings"].Children<JObject>().Single(o => (string)o["id"] == id);

        public static JObject Connection(JObject data, string id) =>
            data["connections"].Children<JObject>().Single(o => (string)o["id"] == id);

        public static JObject Zone(JObject data, string id) => (JObject)data["zones"][id];

        public static IEnumerable<KeyValuePair<string, JObject>> Zones(JObject data) =>
            ((JObject)data["zones"]).Properties().Select(p => new KeyValuePair<string, JObject>(p.Name, (JObject)p.Value));

        /// <summary>Hộp thể tích nội thất (galley_block, hold) trong hệ Unity của gốc tàu.</summary>
        public static Bounds Volume(JObject data, string id)
        {
            var volume = data["interior_volumes"][id];
            var min = Point(volume["min"]);
            var max = Point(volume["max"]);
            var bounds = new Bounds((min + max) * 0.5f, Vector3.zero);
            bounds.Encapsulate(min);
            bounds.Encapsulate(max);
            return bounds;
        }

        /// <summary>Cao độ mặt boong (Blender z) tại x; boong mũi, boong lái cong dọc theo surface_rule.samples.</summary>
        public static float Surface(JToken zone, float x)
        {
            var samples = zone["surface_rule"]?["samples"]?.ToArray();
            if (samples == null || samples.Length == 0)
                return (float)zone["deck_surface_z"];
            Array.Sort(samples, (a, b) => ((float)a["x"]).CompareTo((float)b["x"]));
            if (x <= (float)samples[0]["x"])
                return (float)samples[0]["z"];
            for (int i = 1; i < samples.Length; i++)
            {
                float x1 = (float)samples[i]["x"];
                if (x > x1)
                    continue;
                float x0 = (float)samples[i - 1]["x"];
                float t = Mathf.InverseLerp(x0, x1, x);
                return Mathf.Lerp((float)samples[i - 1]["z"], (float)samples[i]["z"], t);
            }
            return (float)samples[samples.Length - 1]["z"];
        }

        /// <summary>Các vùng tới được từ start theo đồ thị connections (cầu thang và lối đồng mức).</summary>
        public static HashSet<string> Reachable(JObject data, string start)
        {
            var seen = new HashSet<string> { start };
            var pending = new Queue<string>();
            pending.Enqueue(start);
            while (pending.Count > 0)
            {
                string current = pending.Dequeue();
                foreach (var link in data["connections"])
                {
                    string a = (string)link["from"];
                    string b = (string)link["to"];
                    string next = a == current ? b : b == current ? a : null;
                    if (next != null && seen.Add(next))
                        pending.Enqueue(next);
                }
            }
            return seen;
        }

        public static bool Contains(Vector2[] polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                var a = polygon[i];
                var b = polygon[j];
                if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>Điểm (Blender x, y) nằm trên sàn đi được của vùng: trong một đa giác, ngoài mọi lỗ sàn.</summary>
        public static bool OnWalkable(JToken zone, Vector2 point)
        {
            if (zone["deck_holes"] != null && zone["deck_holes"].Any(h => Contains(Polygon(h), point)))
                return false;
            return zone["walkable_polygons"].Any(p => Contains(Polygon(p), point));
        }
    }
}
