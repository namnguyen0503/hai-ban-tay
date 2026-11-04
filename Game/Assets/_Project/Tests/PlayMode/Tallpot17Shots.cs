using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>Ảnh QA tallpot17 (AWAITING VISUAL REVIEW): khung giơ hai bàn tay ở ba cảnh, phòng rửa mới, chảo ở vòng rửa và vòng trước lò.</summary>
    [Category("Screenshots"), Explicit]
    public sealed class Tallpot17Shots
    {
        static GameRoot Root => GameRoot.Instance;
        [SetUp] public void SetUp() => Time.captureDeltaTime = 1f / 60f;
        [TearDown]
        public void TearDown()
        {
            if (Root != null) { Root.Input.SimUse = false; Root.Subtitles.StopAll(); }
            Time.captureDeltaTime = 0f; ScreenshotCapture.RestoreUi(); PlayTestUtil.ExitSim();
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator HandsShotInThreeScenes()
        {
            foreach (string scene in new[] { SceneFlow.PrologueScene, SceneFlow.GameScenes[0], SceneFlow.GameScenes[2] })
            {
                Root.ResetProgress();
                PlayTestUtil.EnterSim(20f);
                yield return PlayTestUtil.LoadAndSettle(scene);
                var rig = PlayerRig.Current;
                rig.SetControl(false);
                HaiBanTay.Narrative.SceneDirector.Current.StopAllCoroutines();
                yield return rig.EnterHandsShot(Root.Config.handsShot, scene == SceneFlow.PrologueScene);
                rig.hands.Snap();
                for (int i = 0; i < 5; i++) yield return null;
                Root.UI.Card.Hide(); Root.UI.SetFade(0f);
                for (int i = 0; i < 5; i++) yield return null;
                yield return ScreenshotCapture.Capture("tp17_handsshot_" + scene);
                PlayTestUtil.ExitSim();
            }
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator WashRoomAndPan()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            d.meals.cook.enabled = false;
            foreach (var npc in Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None)) npc.gameObject.SetActive(false);
            var rig = PlayerRig.Current;
            var station = Object.FindAnyObjectByType<PanStation>();
            Root.UI.SetFade(0f);
            ChoreTestUtil.Look(new Vector3(3.0f, 0f, 0.3f), new Vector3(5.6f, 0.9f, 0.1f));
            for (int i = 0; i < 8; i++) yield return null;
            yield return ScreenshotCapture.Capture("tp17_washroom_from_door");
            ChoreTestUtil.Look(new Vector3(4.7f, 0f, 0.35f), new Vector3(5.8f, 0.2f, 0.35f));
            for (int i = 0; i < 8; i++) yield return null;
            yield return ScreenshotCapture.Capture("tp17_pan_wash_ring");
            var ring = station.stops[0].position;
            station.pan.transform.position = ring; station.pan.position = ring;
            ChoreTestUtil.Look(ring + new Vector3(0.95f, 0f, 0f), ring + Vector3.up * 0.2f);
            for (int i = 0; i < 8; i++) yield return null;
            yield return ScreenshotCapture.Capture("tp17_pan_stove_ring");
        }
    }
}
