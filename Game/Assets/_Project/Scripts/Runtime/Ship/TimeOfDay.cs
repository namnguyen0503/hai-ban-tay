using HaiBanTay.Shift;
using UnityEngine;
using UnityEngine.Rendering;

namespace HaiBanTay.Ship
{
    public enum WaterKind { Sea, River, Harbor }

    /// <summary>Đổi đèn trời, ambient, fog và vật liệu theo giờ cố định hoặc đồng hồ ca được truyền vào.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class TimeOfDay : MonoBehaviour
    {
        [Range(0f, 24f)] public float hour = 12f;
        public bool followShiftClock;
        public ShiftDirector shift;
        public Light mainLight;
        public ShipMotion ship;
        public Renderer sky, ocean, clouds;
        public WaterKind water;
        public WorldLightingKeyframe[] keyframes;
        [Tooltip("Giờ tự trôi (giờ game mỗi giây thật) khi không theo đồng hồ ca; 0 = đứng yên.")]
        public float hoursPerSecond;
        [Tooltip("Giờ tự trôi dừng ở mốc này.")]
        public float stopAtHour = 24f;
        [Tooltip("Tắt khi cảnh có bộ điều khiển sương riêng (FogLift ở Marseille): chỉ đổi màu sương, giữ mật độ.")]
        public bool driveFogDensity = true;
        [Tooltip("Nhân ba màu ambient (Trilight) lấy từ keyframe; 1 = đúng màu keyframe. Cảnh hoàng hôn dùng để nâng ánh sáng nền mà không đổi màu trời.")]
        public float ambientScale = 1f;
        [Tooltip("Nhân màu phần sáng / phần tối của mây; trắng = mây ban ngày. Hoàng hôn: đỉnh mây ấm, bụng mây hồng tím.")]
        public Color cloudLitTint = Color.white, cloudShadeTint = Color.white;

        MaterialPropertyBlock _skyBlock, _waterBlock, _cloudBlock;
        public WorldLightingState State { get; private set; }

        void OnEnable()
        {
            _skyBlock = new MaterialPropertyBlock();
            _waterBlock = new MaterialPropertyBlock();
            Apply();
        }

        void LateUpdate()
        {
            if (hoursPerSecond > 0f && !followShiftClock && hour < stopAtHour)
                hour = Mathf.Min(stopAtHour, hour + hoursPerSecond * Time.deltaTime);
            Apply();
        }

        public void Apply()
        {
            float h = followShiftClock && shift != null && shift.Clock != null ? shift.Clock.Minutes / 60f : hour;
            State = TimeOfDayLogic.Evaluate(h, keyframes);
            var motion = ship != null ? ship : ShipMotion.Current;
            float storm = water == WaterKind.Sea && motion != null
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 1f, motion.Intensity)) : 0f;
            var weather = State;
            float daylight = Mathf.Lerp(1f, .22f, State.Night);
            weather.Sky = Color.Lerp(State.Sky, new Color(.25f,.30f,.36f) * daylight, storm);
            weather.Horizon = Color.Lerp(State.Horizon, new Color(.42f,.46f,.49f) * daylight, storm);
            weather.FogColor = Color.Lerp(State.FogColor, weather.Horizon, storm);
            weather.LightColor = Color.Lerp(State.LightColor, new Color(.79f,.84f,.90f), storm);
            weather.LightIntensity *= Mathf.Lerp(1f, .42f, storm);
            weather.DeepWater = Color.Lerp(State.DeepWater, new Color(.025f,.07f,.09f) * daylight, storm);
            weather.ShallowWater = Color.Lerp(State.ShallowWater, new Color(.10f,.17f,.19f) * daylight, storm);
            State = weather;
            if (mainLight != null)
            {
                mainLight.transform.localRotation = Quaternion.LookRotation(State.MainLightDirection, Vector3.up);
                mainLight.color = State.LightColor;
                mainLight.intensity = State.LightIntensity;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = State.Sky * ambientScale;
            RenderSettings.ambientEquatorColor = State.Horizon * ambientScale;
            RenderSettings.ambientGroundColor = State.Ground * ambientScale;
            RenderSettings.fog = true;
            // Bình phương khoảng cách: gần giữ tương phản, xa vẫn có haze.
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = State.FogColor;
            if (driveFogDensity)
                RenderSettings.fogDensity = State.FogDensity;
            if (sky != null)
            {
                _skyBlock ??= new MaterialPropertyBlock();
                sky.GetPropertyBlock(_skyBlock);
                _skyBlock.SetColor("_ZenithColor", State.Sky);
                _skyBlock.SetColor("_HorizonColor", State.Horizon);
                _skyBlock.SetColor("_BelowColor", State.Ground);
                _skyBlock.SetVector("_SunDirection", State.SunDirection);
                _skyBlock.SetVector("_MoonDirection", State.MoonDirection);
                _skyBlock.SetFloat("_Night", State.Night);
                _skyBlock.SetFloat("_Stars", TimeOfDayLogic.StarVisibility(State));
                _skyBlock.SetFloat("_Storm", storm);
                sky.SetPropertyBlock(_skyBlock);
            }
            if (ocean != null)
            {
                _waterBlock ??= new MaterialPropertyBlock();
                ocean.GetPropertyBlock(_waterBlock);
                float brightness = Mathf.Lerp(1f, 0.16f, State.Night);
                _waterBlock.SetFloat("_WaveScale", Mathf.Lerp(1f, 2.4f, storm));
                _waterBlock.SetColor("_DeepColor", water == WaterKind.River
                    ? new Color(0.2f, 0.105f, 0.042f) * brightness : water == WaterKind.Harbor ? Color.Lerp(State.DeepWater, new Color(0.035f, 0.10f, 0.09f) * brightness, 0.6f) : State.DeepWater);
                _waterBlock.SetColor("_ShallowColor", water == WaterKind.River
                    ? new Color(0.38f, 0.24f, 0.105f) * brightness : water == WaterKind.Harbor ? Color.Lerp(State.ShallowWater, new Color(0.11f, 0.19f, 0.14f) * brightness, 0.6f) : State.ShallowWater);
                _waterBlock.SetColor("_SkyColor", State.Horizon);
                _waterBlock.SetColor("_ZenithColor", State.Sky);
                _waterBlock.SetVector("_SunDirection", transform.TransformDirection(State.SunDirection));
                float sunVisible = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.08f, 0.06f, State.SunDirection.y));
                _waterBlock.SetColor("_SunRadiance", State.LightColor * (0.6f + State.LightIntensity) * sunVisible);
                ocean.SetPropertyBlock(_waterBlock);
            }
            if (clouds != null)
            {
                _cloudBlock ??= new MaterialPropertyBlock();
                clouds.GetPropertyBlock(_cloudBlock);
                _cloudBlock.SetColor("_HorizonColor", State.Horizon);
                _cloudBlock.SetVector("_SunDirection", State.MoonIsMain ? State.MoonDirection : State.SunDirection);
                _cloudBlock.SetFloat("_Night", State.Night);
                _cloudBlock.SetFloat("_Storm", storm);
                _cloudBlock.SetColor("_LitTint", cloudLitTint);
                _cloudBlock.SetColor("_ShadeTint", cloudShadeTint);
                clouds.SetPropertyBlock(_cloudBlock);
            }
        }
    }
}
