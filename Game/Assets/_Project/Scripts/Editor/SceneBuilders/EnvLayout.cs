using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    public enum EnvAnchorKind { Ground, Ship, Decoration }

    /// <summary>Schema 1 layouts are already Unity world metres. Never rotate anchors with the FBX.</summary>
    public sealed class EnvLayout
    {
        public const string Root = "Assets/_Project/Art/Models/Env/";
        public const string PrefabRoot = "Assets/_Project/Prefabs/Art/";

        public sealed class CrowdPath
        {
            public string Name { get; internal set; }
            public Vector3[] Points { get; internal set; }
            public int Count { get; internal set; }
            public bool Loop { get; internal set; }
            public string Motion { get; internal set; }
        }

        public sealed class Spot
        {
            public string Name { get; internal set; }
            public Vector3[] Points { get; internal set; }
            public Vector3 Position => Points[0];
            public int Count { get; internal set; }
        }

        public sealed class Blocker
        {
            public string Name { get; internal set; }
            public string MeshName { get; internal set; }
            public Vector3 Center { get; internal set; }
            public Vector3 Size { get; internal set; }
            public float Yaw { get; internal set; }
            public string[] Visuals { get; internal set; }
        }

        public sealed class WalkRegion
        {
            public string Name { get; internal set; }
            public Bounds Bounds { get; internal set; }

            /// <summary>Horizontal membership; light/object anchors may be above the floor.</summary>
            public bool Contains(Vector3 point, float margin = 0f) =>
                point.x >= Bounds.min.x - margin && point.x <= Bounds.max.x + margin
                && point.z >= Bounds.min.z - margin && point.z <= Bounds.max.z + margin;
        }

        public string Area { get; private set; }
        public string Scene { get; private set; }
        public string Fbx { get; private set; }
        public Vector3 ModelPosition { get; private set; }
        public Quaternion ModelRotation { get; private set; }
        public Vector3 ModelScale { get; private set; }
        public Vector3 PlayerSpawn { get; private set; }
        public float PlayerYaw { get; private set; }
        public IReadOnlyDictionary<string, Vector3> Anchors { get; private set; }
        public CrowdPath[] Paths { get; private set; }
        public Spot[] Spots { get; private set; }
        public Blocker[] Blockers { get; private set; }
        public Blocker[] CollisionSurfaces { get; private set; }
        public WalkRegion[] WalkBounds { get; private set; }
        public int CrowdCapacity { get; private set; }
        public string PrefabPath => PrefabRoot + "Env_" + Area + ".prefab";
        readonly Dictionary<string, EnvAnchorKind> _kinds = new Dictionary<string, EnvAnchorKind>();

        public EnvAnchorKind PlayerSpawnKind => Anchors.Any(a => Kind(a.Key) == EnvAnchorKind.Ship
            && Vector3.Distance(a.Value, PlayerSpawn) < 0.001f) ? EnvAnchorKind.Ship : EnvAnchorKind.Ground;

        public static EnvLayout Load(string area)
        {
            if (area != "NhaRong" && area != "SaigonStreet" && area != "Marseille")
                throw new ArgumentException("Khu môi trường không có: " + area, nameof(area));
            string path = Root + area + "/layout.json";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null)
                throw new InvalidOperationException("Thiếu " + path + "; chạy Tools/import_env.sh.");
            var data = JObject.Parse(asset.text);
            if ((int?)data["schema_version"] != 1 || (string)data["frame"] != "unity_world"
                || (string)data["units"] != "metres")
                throw new InvalidOperationException(path + ": cần schema 1, unity_world, metres.");
            var layout = new EnvLayout { Area = area, Scene = (string)data["scene"], Fbx = (string)data["fbx"] };
            if (layout.Fbx != area + ".fbx")
                throw new InvalidOperationException(path + ": sai tên FBX.");
            var transform = data["fbx_transform"];
            layout.ModelPosition = Vector(transform["position"]);
            layout.ModelRotation = Quaternion.Euler(Vector(transform["rotation_euler"]));
            layout.ModelScale = transform["scale"] is JArray ? Vector(transform["scale"])
                : Vector3.one * Number(transform["scale"]);
            if (layout.ModelScale.x <= 0f || layout.ModelScale.y <= 0f || layout.ModelScale.z <= 0f)
                throw new InvalidOperationException(path + ": scale phải dương.");
            layout.PlayerSpawn = Vector(data["player_spawn"]["pos"]);
            layout.PlayerYaw = Number(data["player_spawn"]["yaw"]);
            layout.Anchors = ((JObject)data["anchors"]).Properties().ToDictionary(p => p.Name, p => Vector(p.Value));
            foreach (var anchor in layout.Anchors)
            {
                // Use delivery's anchor_roles in code; keep layout.json byte-for-byte unchanged.
                string role = (string)data["anchor_roles"]?[anchor.Key];
                if (!new[] { "ship", "ship_task", "walk", "seat", "object", "light", "tram", "background" }.Contains(role))
                    throw new InvalidOperationException(path + ": anchor_role không có: " + anchor.Key + "/" + role);
                layout._kinds.Add(anchor.Key, role == "ship" || role == "ship_task" ? EnvAnchorKind.Ship
                    : role == "walk" || role == "seat" ? EnvAnchorKind.Ground : EnvAnchorKind.Decoration);
            }
            layout.Paths = data["crowd"]["paths"].Select(p => new CrowdPath
            {
                Name = (string)p["name"], Points = p["points"].Select(Vector).ToArray(),
                Count = (int)p["count"], Loop = (bool)p["loop"], Motion = (string)p["motion"],
            }).ToArray();
            if (layout.Paths.Select(p => p.Name).Distinct().Count() != layout.Paths.Length || layout.Paths.Any(p => p.Points.Length < 2))
                throw new InvalidOperationException(path + ": tuyến trùng tên hoặc thiếu điểm.");
            layout.Spots = data["crowd"]["spots"].Select(p => new Spot
            {
                Name = (string)p["name"], Count = (int)p["count"],
                Points = p["points"] != null ? p["points"].Select(Vector).ToArray() : new[] { Vector(p["pos"]) },
            }).ToArray();
            if (layout.Spots.Any(p => p.Points.Length == 0))
                throw new InvalidOperationException(path + ": spot thiếu điểm.");
            layout.CrowdCapacity = (int)data["crowd"]["capacity"];
            layout.Blockers = data["blockers"].Select(ReadBlocker).ToArray();
            layout.CollisionSurfaces = data["collision_surfaces"].Select(ReadBlocker).ToArray();
            layout.WalkBounds = data["walk_bounds"].Select(p =>
            {
                var min = Vector(p["min"]);
                var max = Vector(p["max"]);
                if (min.x > max.x || min.y > max.y || min.z > max.z)
                    throw new InvalidOperationException(path + ": walk_bounds đảo min/max.");
                return new WalkRegion { Name = (string)p["name"], Bounds = new Bounds((min + max) * 0.5f, max - min) };
            }).ToArray();
            if (layout.WalkBounds.Length == 0)
                throw new InvalidOperationException(path + ": thiếu walk_bounds.");
            return layout;
        }

        public Vector3 Anchor(string name) => Anchors.TryGetValue(name, out var point) ? point
            : throw new KeyNotFoundException(Area + ": không có anchor " + name);
        public EnvAnchorKind Kind(string name) => _kinds.TryGetValue(name, out var kind) ? kind
            : throw new KeyNotFoundException(Area + ": không có anchor " + name);
        public Vector3[] Path(string name) => Paths.Single(p => p.Name == name).Points;

        /// <summary>Preserve world placement even when parent is translated/rotated. FBX correction lives in prefab.</summary>
        public GameObject Place(Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
                throw new InvalidOperationException("Thiếu " + PrefabPath + "; chạy Tools/import_env.sh.");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetParent(parent, true);
            WorldTreeBuilder.Replace(instance);
            return instance;
        }

        static Blocker ReadBlocker(JToken data)
        {
            var size = Vector(data["size"]);
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
                throw new InvalidOperationException("COL size phải dương: " + data["name"]);
            return new Blocker { Name = (string)data["name"], MeshName = (string)data["mesh_name"],
                Center = Vector(data["center"]), Size = size, Yaw = Number(data["yaw"]),
                Visuals = data["visuals"].Select(v => (string)v).ToArray() };
        }

        static float Number(JToken value)
        {
            if (value == null || (value.Type != JTokenType.Float && value.Type != JTokenType.Integer))
                throw new InvalidOperationException("Layout cần số: " + value);
            float result = (float)value;
            if (float.IsNaN(result) || float.IsInfinity(result))
                throw new InvalidOperationException("Layout có số không hữu hạn: " + value);
            return result;
        }

        static Vector3 Vector(JToken value)
        {
            if (!(value is JArray a) || a.Count != 3)
                throw new InvalidOperationException("Layout cần vector [x,y,z]: " + value);
            return new Vector3(Number(a[0]), Number(a[1]), Number(a[2]));
        }
    }
}
