using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    [Category("Screenshots"), Explicit]
    public sealed class StoveShots
    {
        [TearDown]
        public void Close()
        {
            Time.captureDeltaTime = 0f;
            ScreenshotCapture.RestoreUi();
            PlayTestUtil.ExitSim();
        }

        [UnityTest]
        public IEnumerator Stove15CoalFireAsh()
        {
            yield return StovePlayTests.Galley();
            var coal = Object.FindAnyObjectByType<CoalStation>();
            var ash = coal.ashStation;
            StovePlayTests.LookAtCell(coal, 0);
            yield return new WaitForSeconds(0.3f);
            yield return ScreenshotCapture.Capture("stove15_01_cold");
            coal.CellState(0).AddCoal(coal.fuelPerPour);
            yield return new WaitForSeconds(2f);
            yield return ScreenshotCapture.Capture("stove15_02_hot");
            coal.Fire.Set(0f, 0f);
            coal.CellState(0).Set(0f, 0f, 0.48f);
            ash.bucket.Detach(coal.cells[0].bucketSpot.transform.position, coal.cells[0].bucketSpot.transform.rotation, false);
            StovePlayTests.LookAtCell(coal, 0, true);
            yield return new WaitForSeconds(0.3f);
            yield return ScreenshotCapture.Capture("stove15_03_ash_full");
            yield return StovePlayTests.RakeCell(ash, 0);
            yield return ScreenshotCapture.Capture("stove15_04_after_rake");
            ash.hook.ReturnFrom(PlayerRig.Current.interactor);
            yield return StovePlayTests.LoadShovel(coal);
            yield return new WaitForSeconds(0.4f);
            yield return ScreenshotCapture.Capture("stove15_05_loaded_shovel");
            GameRoot.Instance.Input.SimUse = false;
        }
    }
}
