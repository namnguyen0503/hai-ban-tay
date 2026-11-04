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
    /// <summary>Ảnh QA debug notes 22/10 (QA-0132…0148): vòi chậu đang chảy, nồi úp trên giá, nồi cao có quai, chảo trên bếp cạnh nồi cao.</summary>
    [Category("Screenshots"), Explicit]
    public sealed class Props2210Shots
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
        public IEnumerator WashTapRackTallPotAndPan()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            d.meals.cook.enabled = false;
            foreach (var npc in Object.FindObjectsByType<NpcAgent>(FindObjectsSortMode.None)) npc.gameObject.SetActive(false);
            Root.UI.SetFade(0f);
            var wash = Object.FindAnyObjectByType<WashStation>();
            var meals = d.meals;
            for (int i = 0; i < meals.pots.Length; i++) meals.Rack(meals.pots[i], i);
            wash.OpenTap();
            var basin = wash.basin.transform.position;
            ChoreTestUtil.Look(new Vector3(5.75f, 0f, -1.35f), new Vector3(basin.x + 0.2f, 1.0f, -1.75f));
            for (int i = 0; i < 12; i++) yield return null;
            yield return ScreenshotCapture.Capture("p2210_basin_tap_running");
            ChoreTestUtil.Look(new Vector3(5.6f, 0f, -0.9f), new Vector3(6.72f, 1.6f, -1.6f));
            for (int i = 0; i < 8; i++) yield return null;
            yield return ScreenshotCapture.Capture("p2210_rack_pots");
            ChoreTestUtil.Look(new Vector3(5.4f, 0f, -1.6f), new Vector3(6.8f, 0.0f, -1.6f));
            for (int i = 0; i < 8; i++) yield return null;
            yield return ScreenshotCapture.Capture("p2210_washroom_floor");
            var tall = Object.FindAnyObjectByType<TallPotStation>();
            if (tall != null && tall.potRoot != null)
            {
                var p = tall.potRoot.position;
                ChoreTestUtil.Look(p + new Vector3(-1.3f, 0f, 0.6f), p + Vector3.up * 0.7f);
                for (int i = 0; i < 8; i++) yield return null;
                yield return ScreenshotCapture.Capture("p2210_tallpot_handles");
            }
            var pan = Object.FindAnyObjectByType<PanStation>();
            if (pan != null && pan.rangeSpot != null)
            {
                var at = pan.rangeSpot.position;
                pan.pan.isKinematic = true;
                pan.pan.transform.SetPositionAndRotation(at, pan.rangeSpot.rotation);
                ChoreTestUtil.Look(new Vector3(at.x, 0f, at.z - 1.1f), at + Vector3.up * 0.1f);
                for (int i = 0; i < 8; i++) yield return null;
                yield return ScreenshotCapture.Capture("p2210_pan_on_range");
                ChoreTestUtil.Look(new Vector3(at.x - 0.9f, 0f, at.z - 0.9f), at + Vector3.up * 0.05f);
                for (int i = 0; i < 8; i++) yield return null;
                yield return ScreenshotCapture.Capture("p2210_pan_shape");
            }
        }
    }
}
