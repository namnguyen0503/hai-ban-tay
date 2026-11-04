using HaiBanTay.Audio;
using HaiBanTay.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using FontAsset = UnityEngine.TextCore.Text.FontAsset;
using UnityEngine.UIElements;

namespace HaiBanTay.Core
{
    /// <summary>Tham chiếu asset dùng chung, nằm ở Resources/GameConfig.asset (ProjectSetup tạo).</summary>
    public sealed class GameConfig : ScriptableObject
    {
        public const string ResourcePath = "GameConfig";

        public PanelSettings panelSettings;
        public InputActionAsset input;
        public FontAsset serifFont;
        public AudioLibrary audio;
        public HandsShotPose handsShot;
        public TextAsset credits;
    }
}
