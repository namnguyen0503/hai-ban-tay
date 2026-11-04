using HaiBanTay.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Mặt chữ trong thế giới (bảng phấn, biển tên tàu, sổ lương): UI Toolkit vẽ vào RenderTexture riêng (PanelSettings tạo lúc chạy,
    /// dùng theme và font của giao diện chính), gắn vào _BaseMap và _EmissionMap của bề mặt qua MaterialPropertyBlock.
    /// Panel chỉ vẽ nội dung vài khung sau mỗi lần đổi chữ (Redraw), rồi ảnh được chép sang texture tĩnh và panel rỗng vẽ vào RT 1×1:
    /// panel UI Toolkit vẽ vào RenderTexture tốn GPU mỗi khung dù chữ không đổi (đo được ~25 W trên bếp).
    /// </summary>
    public sealed class WorldPanel
    {
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        /// <summary>Số khung panel chạy sau mỗi Redraw (bố cục, atlas chữ, vẽ) trước khi chép ảnh.</summary>
        public const int AwakeFrames = 3;

        RenderTexture _rt;
        RenderTexture _frozen;
        RenderTexture _idle;
        bool _awake;
        PanelSettings _settings;
        UIDocument _doc;
        WorldPanelPump _pump;

        /// <summary>Nội dung panel; sửa xong gọi Redraw().</summary>
        public VisualElement Root { get; private set; }

        public bool Awake => _awake;

        /// <summary>Trả về null nếu thiếu bề mặt hoặc GameRoot chưa có PanelSettings chính.</summary>
        public static WorldPanel Create(Transform host, Renderer surface, int width, int height, Color background, float glow, string name)
        {
            var root = GameRoot.Instance;
            var main = root != null && root.Config != null ? root.Config.panelSettings : null;
            if (surface == null || main == null)
                return null;
            var panel = new WorldPanel();
            panel._rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) { name = name, useMipMap = false };
            panel._rt.Create();
            panel._frozen = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) { name = name + "Frozen", useMipMap = false };
            panel._frozen.Create();
            panel._idle = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGB32) { name = name + "Idle" };
            panel._idle.Create();
            var s = ScriptableObject.CreateInstance<PanelSettings>();
            s.name = name + "Panel";
            s.themeStyleSheet = main.themeStyleSheet;
            s.textSettings = main.textSettings;
            s.targetTexture = panel._rt;
            s.scaleMode = PanelScaleMode.ConstantPixelSize;
            s.clearColor = true;
            s.colorClearValue = background;
            s.sortingOrder = -10;
            // Không nhận chuột: mọi điểm màn hình nằm ngoài panel.
            s.SetScreenToPanelSpaceFunction(_ => new Vector2(float.NaN, float.NaN));
            panel._settings = s;

            var go = new GameObject(name + "UI");
            go.transform.SetParent(host, false);
            panel._doc = go.AddComponent<UIDocument>();
            panel._doc.panelSettings = s;
            panel._doc.rootVisualElement.pickingMode = PickingMode.Ignore;
            panel.Root = new VisualElement { name = name + "Content", pickingMode = PickingMode.Ignore };
            panel.Root.style.flexGrow = 1;
            panel._doc.rootVisualElement.Add(panel.Root);

            var mpb = new MaterialPropertyBlock();
            surface.GetPropertyBlock(mpb);
            mpb.SetTexture(BaseMapId, panel._frozen);
            mpb.SetTexture(EmissionMapId, panel._frozen);
            mpb.SetColor(EmissionColorId, Color.white * glow);
            surface.SetPropertyBlock(mpb);

            panel._pump = host.gameObject.AddComponent<WorldPanelPump>();
            panel._pump.Panel = panel;
            panel.Redraw();
            return panel;
        }

        /// <summary>Bật panel vài khung để vẽ lại nội dung rồi đóng băng ảnh.</summary>
        public void Redraw()
        {
            if (_doc == null)
                return;
            if (!_awake)
            {
                _awake = true;
                _settings.targetTexture = _rt;
            }
            _pump.SleepAt = Time.frameCount + AwakeFrames;
            _pump.enabled = true;
        }

        internal void Freeze()
        {
            if (_doc == null || !_awake)
                return;
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                Graphics.CopyTexture(_rt, _frozen);
            // Không tắt GameObject của UIDocument: bật/tắt panel có RenderTexture riêng làm job vẽ GL của cảnh sau crash.
            // Panel rỗng vẽ vào RT 1×1 nên gần như không tốn GPU.
            _settings.targetTexture = _idle;
            _awake = false;
        }

        public void Dispose()
        {
            if (_pump != null)
                Object.Destroy(_pump);
            foreach (var rt in new[] { _rt, _frozen, _idle })
            {
                if (rt == null)
                    continue;
                rt.Release();
                Object.Destroy(rt);
            }
            _rt = _frozen = _idle = null;
            if (_settings != null)
            {
                Object.Destroy(_settings);
                _settings = null;
            }
        }
    }

    /// <summary>Đếm khung cho WorldPanel: hết AwakeFrames thì chép ảnh và tắt panel.</summary>
    public sealed class WorldPanelPump : MonoBehaviour
    {
        internal WorldPanel Panel;
        internal int SleepAt;

        void LateUpdate()
        {
            if (Time.frameCount < SleepAt)
                return;
            enabled = false;
            Panel?.Freeze();
        }
    }
}
