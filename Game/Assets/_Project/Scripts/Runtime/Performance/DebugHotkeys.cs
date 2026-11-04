using HaiBanTay.Core;
using HaiBanTay.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HaiBanTay.Performance
{
    /// <summary>Phím hỗ trợ demo (PLAN mục 7.1).</summary>
    public sealed class DebugHotkeys : MonoBehaviour
    {
        UIRoot _ui;
        SceneFlow _scenes;
        QualityTierManager _quality;
        FpsOverlay _fps;
        PerfRecorder _perf;

        public void Initialize(UIRoot ui, SceneFlow scenes, QualityTierManager quality, FpsOverlay fps, PerfRecorder perf)
        {
            _ui = ui;
            _scenes = scenes;
            _quality = quality;
            _fps = fps;
            _perf = perf;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null)
                return;

            bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            if (ctrl)
            {
                if (kb.f1Key.wasPressedThisFrame) _scenes.Load(SceneFlow.PrologueScene);
                else if (kb.f2Key.wasPressedThisFrame) _scenes.LoadByNumber(1);
                else if (kb.f3Key.wasPressedThisFrame) _scenes.LoadByNumber(2);
                else if (kb.f4Key.wasPressedThisFrame) _scenes.LoadByNumber(3);
                // Ctrl+F5: nhảy tới E1 — EndingDirector luôn bắt đầu cảnh 03 từ E1.
                else if (kb.f5Key.wasPressedThisFrame) _scenes.LoadByNumber(3);
                else if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) StepSpeed(1);
                else if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) StepSpeed(-1);
                return;
            }

            if (kb.f1Key.wasPressedThisFrame) _fps.Toggle();
            if (kb.f8Key.wasPressedThisFrame) _perf.Toggle();
            if (kb.f9Key.wasPressedThisFrame) _quality.Toggle();
            if (kb.f10Key.wasPressedThisFrame) _ui.ToggleSafeFrame();
        }

        void StepSpeed(int direction)
        {
            DevSpeed.Step(direction);
            _fps.ShowSpeed();
        }
    }
}
