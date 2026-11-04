using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Narrative;
using HaiBanTay.Performance;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace HaiBanTay.DevTools
{
    /// <summary>Chụp trạng thái game thành cây Dictionary (ghi JSON) cho ghi chú debug. Chỉ chạy khi người chơi gửi ghi chú.</summary>
    public static class StateDump
    {
        public static Dictionary<string, object> Capture(string note, float fps)
        {
            var d = new Dictionary<string, object>
            {
                ["note"] = note,
                ["time_local"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ["time_utc"] = DateTime.UtcNow.ToString("o"),
            };
            Safe(d, "build", Build);
            Safe(d, "device", Device);
            Safe(d, "perf", () => Perf(fps));
            Safe(d, "scene", Scene);
            Safe(d, "player", Player);
            Safe(d, "shift", Shift);
            Safe(d, "progress", Progress);
            Safe(d, "settings", () => GameSettings.Current);
            Safe(d, "components", Components);
            return d;
        }

        static void Safe(Dictionary<string, object> d, string key, Func<object> f)
        {
            try { d[key] = f(); }
            catch (Exception e) { d[key] = "LỖI khi đọc: " + e.Message; }
        }

        static object Build() => new Dictionary<string, object>
        {
            ["version"] = Application.version,
            ["unity"] = Application.unityVersion,
            ["platform"] = Application.platform.ToString(),
            ["dev_build"] = Debug.isDebugBuild,
            ["build_guid"] = Application.buildGUID,
            ["build_id"] = BuildInfo.Id,
            ["editor"] = Application.isEditor,
            ["touch_controls"] = UI.TouchControls.Enabled,
            ["log_errors"] = LogBuffer.ErrorCount,
            ["log_warnings"] = LogBuffer.WarningCount,
        };

        static object Device() => new Dictionary<string, object>
        {
            ["model"] = SystemInfo.deviceModel,
            ["os"] = SystemInfo.operatingSystem,
            ["cpu"] = $"{SystemInfo.processorType} ×{SystemInfo.processorCount}",
            ["ram_mb"] = SystemInfo.systemMemorySize,
            ["gpu"] = SystemInfo.graphicsDeviceName,
            ["gpu_api"] = SystemInfo.graphicsDeviceType.ToString(),
            ["gpu_version"] = SystemInfo.graphicsDeviceVersion,
            ["gpu_mem_mb"] = SystemInfo.graphicsMemorySize,
            ["screen"] = $"{Screen.width}×{Screen.height}",
            ["display"] = $"{Screen.currentResolution.width}×{Screen.currentResolution.height} @ {Screen.currentResolution.refreshRateRatio.value:0.##} Hz",
            ["dpi"] = Screen.dpi,
            ["safe_area"] = Screen.safeArea.ToString(),
            ["fullscreen"] = Screen.fullScreenMode.ToString(),
            ["orientation"] = Screen.orientation.ToString(),
            ["battery"] = SystemInfo.batteryLevel,
            ["battery_status"] = SystemInfo.batteryStatus.ToString(),
            ["mem_used_mb"] = (long)(UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024)),
        };

        static object Perf(float fps)
        {
            var root = GameRoot.Instance;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            return new Dictionary<string, object>
            {
                ["fps_recent"] = Mathf.Round(fps * 10f) / 10f,
                ["frame_cap"] = FrameCap.Current.ToString(),
                ["target_frame_rate"] = Application.targetFrameRate,
                ["vsync"] = QualitySettings.vSyncCount,
                ["quality_level"] = QualitySettings.names[QualitySettings.GetQualityLevel()],
                ["tier"] = root != null && root.Quality != null ? root.Quality.Current.ToString() : null,
                ["render_scale"] = urp != null ? urp.renderScale : -1f,
                ["urp_asset"] = urp != null ? urp.name : null,
                ["time_scale"] = Time.timeScale,
                ["frame"] = Time.frameCount,
                ["realtime_s"] = Time.realtimeSinceStartup,
            };
        }

        static object Scene()
        {
            var loaded = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                loaded.Add(SceneManager.GetSceneAt(i).name);
            var directors = new List<object>();
            foreach (var dir in UnityEngine.Object.FindObjectsByType<SceneDirector>(FindObjectsSortMode.None))
                directors.Add(new Dictionary<string, object> { ["type"] = dir.GetType().Name, ["beat"] = dir.Beat, ["enabled"] = dir.isActiveAndEnabled });
            return new Dictionary<string, object>
            {
                ["active"] = SceneManager.GetActiveScene().name,
                ["loaded"] = loaded,
                ["time_since_load_s"] = Time.timeSinceLevelLoad,
                ["from_prologue"] = SceneFlow.FromPrologue,
                ["directors"] = directors,
            };
        }

        static object Player()
        {
            var d = new Dictionary<string, object>();
            var input = GameRoot.Instance != null ? GameRoot.Instance.Input : null;
            if (input != null)
            {
                d["gameplay_enabled"] = input.GameplayEnabled;
                d["cursor_free"] = input.CursorFree;
                var blocks = new List<string>();
                foreach (var r in new[] { "pause", "notebook", SceneDirector.CutsceneBlock, "debug_note", "sit", "menu" })
                {
                    if (input.IsBlockedBy(r))
                        blocks.Add(r);
                }
                d["known_blocks"] = blocks;
            }
            var rig = UnityEngine.Object.FindFirstObjectByType<PlayerRig>();
            if (rig != null)
            {
                d["position"] = V(rig.transform.position);
                d["rotation_euler"] = V(rig.transform.eulerAngles);
                d["control_enabled"] = rig.ControlEnabled;
            }
            var cam = Camera.main;
            if (cam != null)
            {
                d["camera_position"] = V(cam.transform.position);
                d["camera_forward"] = V(cam.transform.forward);
                d["camera_fov"] = cam.fieldOfView;
                if (Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 30f, ~0, QueryTriggerInteraction.Collide))
                {
                    d["looking_at"] = Path(hit.collider.transform);
                    d["looking_at_distance"] = hit.distance;
                    d["looking_at_point"] = V(hit.point);
                }
            }
            var it = UnityEngine.Object.FindFirstObjectByType<Interactor>();
            if (it != null)
            {
                d["holding"] = it.Holding != null ? Path(it.Holding.transform) : null;
                d["tool"] = it.Tool.ToString();
                d["hands_free"] = it.HandsFree;
                d["work_carrying"] = it.WorkCarrying;
                d["prompt_target"] = it.Current != null ? it.Current.GetType().Name : null;
                d["look_collider"] = it.LookCollider != null ? Path(it.LookCollider.transform) : null;
            }
            return d;
        }

        static object Shift()
        {
            var s = UnityEngine.Object.FindFirstObjectByType<ShiftDirector>();
            if (s == null)
                return null;
            return new Dictionary<string, object>
            {
                ["day"] = s.Day != null ? $"{s.Day.Id} (#{s.Day.Number}) {s.Day.Title}" : null,
                ["phase_index"] = s.PhaseIndex,
                ["phase"] = s.Phase != null ? s.Phase.Id : null,
                ["in_shift"] = s.InShift,
                ["finished"] = s.Finished,
                ["clock"] = s.Clock != null ? s.Clock.Text : null,
                ["clock_running"] = s.Clock != null && s.Clock.Running,
                ["evening_minutes"] = s.EveningMinutes,
            };
        }

        static object Progress()
        {
            var r = GameRoot.Instance;
            if (r == null || r.State == null)
                return null;
            return SaveSystem.Capture(SceneManager.GetActiveScene().name, r.State, r.Flags, r.Notebook, r.Events);
        }

        // Thuộc tính/trường công khai kiểu đơn giản của các thành phần điều phối chính.
        static readonly string[] ComponentTypes =
        {
            "ShiftDirector", "NhaRongDirector", "PrologueDirector", "EndingDirector", "MenuDirector",
            "Interactor", "FirstPersonMotor", "LookController", "SitController", "PlayerBody", "HandsRig",
            "ShipMotion", "StormController", "TimeOfDay", "StudyTask", "PauseController",
        };

        static object Components()
        {
            var result = new Dictionary<string, object>();
            foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb == null)
                    continue;
                var t = mb.GetType();
                bool wanted = Array.IndexOf(ComponentTypes, t.Name) >= 0 || typeof(Tasks.TaskBase).IsAssignableFrom(t) || t.Name.EndsWith("Station");
                if (!wanted)
                    continue;
                string key = t.Name + " @ " + Path(mb.transform);
                if (result.ContainsKey(key))
                    key += "#" + mb.GetInstanceID();
                result[key] = Reflect(mb);
            }
            return result;
        }

        static Dictionary<string, object> Reflect(object o)
        {
            var d = new Dictionary<string, object>();
            var t = o.GetType();
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public;
            if (o is Behaviour b)
                d["enabled"] = b.isActiveAndEnabled;
            while (t != null && t != typeof(MonoBehaviour))
            {
                foreach (var f in t.GetFields(F | BindingFlags.DeclaredOnly))
                    Put(d, f.Name, f.FieldType, () => f.GetValue(o));
                foreach (var p in t.GetProperties(F | BindingFlags.DeclaredOnly))
                {
                    if (p.GetIndexParameters().Length == 0 && p.CanRead)
                        Put(d, p.Name, p.PropertyType, () => p.GetValue(o));
                }
                t = t.BaseType;
            }
            return d;
        }

        static void Put(Dictionary<string, object> d, string name, Type type, Func<object> get)
        {
            if (d.ContainsKey(name))
                return;
            try
            {
                if (type.IsPrimitive || type.IsEnum || type == typeof(string))
                    d[name] = type.IsEnum ? get()?.ToString() : get();
                else if (type == typeof(Vector3))
                    d[name] = V((Vector3)get());
                else if (type == typeof(Vector2))
                    d[name] = ((Vector2)get()).ToString("0.###");
                else if (typeof(UnityEngine.Object).IsAssignableFrom(type))
                {
                    var u = get() as UnityEngine.Object;
                    d[name] = u == null ? null : u is Component c ? Path(c.transform) : u.name;
                }
                else if (typeof(ICollection).IsAssignableFrom(type))
                    d[name] = "count " + ((get() as ICollection)?.Count ?? 0);
            }
            catch (Exception e)
            {
                d[name] = "! " + (e.InnerException ?? e).Message;
            }
        }

        static string V(Vector3 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";

        static string Path(Transform t)
        {
            if (t == null)
                return null;
            string p = t.name;
            for (var x = t.parent; x != null; x = x.parent)
                p = x.name + "/" + p;
            return p;
        }
    }
}
