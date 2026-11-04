#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Text;
using HaiBanTay.Core;
using HaiBanTay.Performance;
using HaiBanTay.Player;
using HaiBanTay.Ship;
using NUnit.Framework;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>Đo cùng góc sandbox trước/sau Track B; chạy với GPU qua Tools/screenshots.sh.</summary>
    [Category("Screenshots"), Explicit]
    public class SandboxWorldRenderTests
    {
        RenderTexture _target;
        int _quality;
        bool _toggled;
        SerializedObject _pipeline, _renderer;
        bool _layers;
        int _mode;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var root = PlayTestUtil.Root();
            _quality = QualitySettings.GetQualityLevel();
            if (root.Quality.Current != QualityTier.Laptop)
            {
                root.Quality.Toggle();
                _toggled = true;
            }
            EditorSceneManager.LoadSceneInPlayMode("Assets/_Project/Scenes/90_Sandbox.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            yield return null;
            var rig = PlayerRig.Current;
            rig.SetControl(false);
            var brain = Camera.main.GetComponent("CinemachineBrain") as Behaviour;
            Assert.IsNotNull(brain);
            brain.enabled = false;
            _target = new RenderTexture(1920, 1080, 24);
            _target.Create();
        }

        [TearDown]
        public void TearDown()
        {
            if (_pipeline != null)
            {
                _pipeline.FindProperty("m_SupportsLightLayers").boolValue = _layers;
                _pipeline.ApplyModifiedPropertiesWithoutUndo();
                _renderer.FindProperty("m_RenderingMode").intValue = _mode;
                _renderer.ApplyModifiedPropertiesWithoutUndo();
            }
            if (_target != null)
            {
                _target.Release();
                Object.Destroy(_target);
            }
            if (_toggled) GameRoot.Instance?.Quality.Toggle();
            QualitySettings.SetQualityLevel(_quality, true);
            PlayTestUtil.ExitSim();
        }

        void Look(Vector3 eye, Vector3 target)
        {
            Camera.main.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
        }

        void RenderOnce()
        {
            var request = new RenderPipeline.StandardRequest { destination = _target };
            Assert.IsTrue(RenderPipeline.SupportsRenderRequest(Camera.main, request));
            RenderPipeline.SubmitRenderRequest(Camera.main, request);
        }

        IEnumerator Measure(StringBuilder report, string phase, string view)
        {
            for (int i = 0; i < 15; i++)
            {
                RenderOnce();
                yield return null;
            }
            using var sp = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            using var dc = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            using var tr = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            long setpass = 0, draws = 0, triangles = 0;
            for (int i = 0; i < 30; i++)
            {
                RenderOnce();
                yield return null;
                setpass = System.Math.Max(setpass, sp.LastValue);
                draws = System.Math.Max(draws, dc.LastValue);
                triangles = System.Math.Max(triangles, tr.LastValue);
            }
            report.AppendLine($"90_Sandbox,{phase},{view},{setpass},{draws},{triangles}");
            Assert.Greater(draws, 0, "RenderStats cần GPU và bộ đếm thật");
            Assert.LessOrEqual(setpass, 150, report.ToString());
        }

        [UnityTest]
        public IEnumerator RenderStatsSandboxBefore() => MeasureSandbox("before");

        [UnityTest]
        public IEnumerator RenderStatsSandboxAfter() => MeasureSandbox("after");

        IEnumerator MeasureSandbox(string phase)
        {
            var report = new StringBuilder("scene,phase,view,setpass_max,draws_max,tris_max\n");
            Look(new Vector3(0f, 1.62f, -3f), new Vector3(0f, 1f, 2.5f));
            yield return Measure(report, phase, "room");
            Look(new Vector3(-60f, 4.5f, -10f), new Vector3(-60f, 0f, 45f));
            yield return Measure(report, phase, "world");
            File.WriteAllText(Path.Combine(ScreenshotCapture.Folder, "track_b_render_" + phase + ".csv"), report.ToString());
            Debug.Log("RenderStats:\n" + report);
        }

        void FreezeWorld(TimeOfDay clock)
        {
            clock.enabled = false;
            var motion = clock.GetComponent<WorldMotion>();
            motion.enabled = false;
            motion.transform.localPosition = Vector3.zero;
            motion.transform.localRotation = Quaternion.identity;
        }

        void CaptureImage(string name)
        {
            RenderOnce();
            var previous = RenderTexture.active;
            RenderTexture.active = _target;
            var texture = new Texture2D(_target.width, _target.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(ScreenshotCapture.Folder, name + ".png"), texture.EncodeToPNG());
            Object.Destroy(texture);
        }

        [UnityTest]
        public IEnumerator ShotsSandboxWorld()
        {
            var clock = Object.FindAnyObjectByType<TimeOfDay>();
            Assert.IsNotNull(clock);
            FreezeWorld(clock);
            Look(new Vector3(-61.8f, 2f, -7f), new Vector3(-95f, 0f, -45f));
            foreach (float hour in new[] { 4f, 6f, 12f, 19f })
            {
                clock.hour = hour;
                clock.Apply();
                for (int i = 0; i < 5; i++) yield return null;
                CaptureImage("B_world_" + hour.ToString("00") + "00");
            }
            clock.hour = 12f;
            clock.water = WaterKind.River;
            clock.ocean.sharedMaterial = Resources.Load<Material>("World/Ocean_River");
            Assert.IsNotNull(clock.ocean.sharedMaterial);
            clock.Apply();
            yield return null;
            CaptureImage("B_river_noon");
            clock.water = WaterKind.Harbor;
            clock.ocean.sharedMaterial = Resources.Load<Material>("World/Ocean_Harbor");
            clock.Apply();
            yield return null;
            CaptureImage("B_harbor_noon");
            Look(new Vector3(-60f, 1.62f, 2f), new Vector3(-61.4f, 1.2f, 5f));
            Object.FindAnyObjectByType<LightBudget>().SetRegion(ShipRegion.Interior);
            clock.hour = 4f;
            clock.Apply();
            yield return null;
            CaptureImage("B_cabin_0400");
        }

        IEnumerator SetPipeline(int mode, bool layers)
        {
            if (_pipeline == null)
            {
                _pipeline = new SerializedObject(QualitySettings.renderPipeline);
                _layers = _pipeline.FindProperty("m_SupportsLightLayers").boolValue;
                var data = _pipeline.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue;
                _renderer = new SerializedObject(data);
                _mode = _renderer.FindProperty("m_RenderingMode").intValue;
            }
            _pipeline.FindProperty("m_SupportsLightLayers").boolValue = layers;
            _pipeline.ApplyModifiedPropertiesWithoutUndo();
            _renderer.FindProperty("m_RenderingMode").intValue = mode;
            _renderer.ApplyModifiedPropertiesWithoutUndo();
            // Tạo lại pipeline để phép đo dùng đúng mode và keyword, không dùng cache trước đó.
            var asset = QualitySettings.renderPipeline;
            QualitySettings.renderPipeline = null;
            yield return null;
            QualitySettings.renderPipeline = asset;
            for (int i = 0; i < 15; i++)
            {
                RenderOnce();
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator RenderStatsWorldRenderingLayersAndModes()
        {
            var clock = Object.FindAnyObjectByType<TimeOfDay>();
            Assert.IsNotNull(clock);
            FreezeWorld(clock);
            clock.hour = 12f;
            clock.Apply();
            Look(new Vector3(-60f, 4.5f, -10f), new Vector3(-60f, 0f, 45f));
            var report = new StringBuilder("scene,phase,view,setpass_max,draws_max,tris_max\n");
            foreach (int mode in new[] { 0, 2 })
                foreach (bool layers in new[] { false, true })
                {
                    yield return SetPipeline(mode, layers);
                    string name = (mode == 0 ? "Forward" : "ForwardPlus") + (layers ? "_layers_on" : "_layers_off");
                    yield return Measure(report, "after", name);
                }
            File.WriteAllText(Path.Combine(ScreenshotCapture.Folder, "track_b_render_modes.csv"), report.ToString());
            Debug.Log("RenderStats:\n" + report);
        }

        float PixelBrightness(Vector3 center)
        {
            RenderOnce();
            var point = Camera.main.WorldToViewportPoint(center);
            var previous = RenderTexture.active;
            RenderTexture.active = _target;
            var pixel = new Texture2D(1, 1, TextureFormat.RGB24, false);
            pixel.ReadPixels(new Rect((int)(point.x * _target.width), (int)(point.y * _target.height), 1, 1), 0, 0);
            pixel.Apply();
            RenderTexture.active = previous;
            float value = pixel.GetPixel(0, 0).grayscale;
            Object.Destroy(pixel);
            return value;
        }

        [UnityTest]
        public IEnumerator RenderingLayersActuallyIsolateSunInForwardAndForwardPlus()
        {
            var clock = Object.FindAnyObjectByType<TimeOfDay>();
            FreezeWorld(clock);
            Object.FindAnyObjectByType<LightBudget>().enabled = false;
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) light.enabled = false;
            var sun = clock.mainLight;
            sun.enabled = true;
            sun.shadows = LightShadows.None;
            sun.intensity = 1.5f;
            sun.color = Color.white;
            sun.transform.rotation = Quaternion.identity;
            RenderSettings.ambientSkyColor = RenderSettings.ambientEquatorColor = RenderSettings.ambientGroundColor = Color.black;
            RenderSettings.fog = false;
            Look(new Vector3(-60f, 1.5f, -9f), new Vector3(-60f, 1.5f, -5f));
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0f);
            var a = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            a.transform.position = new Vector3(-61f, 1.5f, -5f);
            b.transform.position = new Vector3(-59f, 1.5f, -5f);
            var ra = a.GetComponent<Renderer>();
            var rb = b.GetComponent<Renderer>();
            ra.sharedMaterial = rb.sharedMaterial = material;
            try
            {
                foreach (int mode in new[] { 0, 2 })
                {
                    ra.renderingLayerMask = WorldRenderingLayers.Exterior;
                    rb.renderingLayerMask = WorldRenderingLayers.Interior;
                    yield return SetPipeline(mode, true);
                    float exterior = PixelBrightness(a.transform.position);
                    float interior = PixelBrightness(b.transform.position);
                    Assert.Greater(exterior, 0.1f, "mặt ngoài phải nhận mặt trời");
                    Assert.Less(interior, exterior * 0.2f, "mặt trong không được nhận mặt trời");
                    ra.renderingLayerMask = WorldRenderingLayers.Interior;
                    rb.renderingLayerMask = WorldRenderingLayers.Exterior;
                    for (int i = 0; i < 5; i++) yield return null;
                    Assert.Less(PixelBrightness(a.transform.position), exterior * 0.2f);
                    Assert.Greater(PixelBrightness(b.transform.position), 0.1f);
                    Debug.Log($"RenderingLayers mode={mode}: exterior={exterior:F3}, interior={interior:F3}");
                }
            }
            finally
            {
                Object.Destroy(a);
                Object.Destroy(b);
                Object.Destroy(material);
            }
        }
    }
}
#endif
