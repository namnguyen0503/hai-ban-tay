using System.Collections;
using System.Linq;
using HaiBanTay.Core;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Narrative;
using UnityEngine.SceneManagement;
using HaiBanTay.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>Ảnh nghiệm thu vải, khớp tay và đạo cụ; giữ nguyên bộ ảnh kiểm cũ.</summary>
    [Category("Screenshots"), Explicit]
    public sealed class GalleyPolishShots
    {
        static PlayerRig Rig => PlayerRig.Current;
        static GameRoot Root => GameRoot.Instance;
        static string Prefix => System.Environment.GetEnvironmentVariable("GALLEY_SHOT_PREFIX") ?? "galley11_";
        [TearDown] public void TearDown()
        {
            if (Root != null) { Root.Input.SimUse = false; Root.Input.SimLook = Vector2.zero; }
            ScreenshotCapture.RestoreUi(); PlayTestUtil.ExitSim();
        }

        static void CleanUi()
        {
            Root.UI.Shift.Clear(); Root.UI.Prompt.HideHint(); Root.UI.Toast.Hide();
            Root.Subtitles.StopAll(); Root.UI.Card.Hide(); Root.UI.SetFade(0f);
        }
        static IEnumerator Side(string name)
        {
            CleanUi();
            var go = new GameObject("GalleyPolishCamera");
            var cam = go.AddComponent<Camera>(); cam.CopyFrom(Rig.cam);
            cam.enabled = false; cam.fieldOfView = 50f;
            go.transform.position = Rig.transform.TransformPoint(new Vector3(2.3f, 1.15f, 0.65f));
            go.transform.LookAt(Rig.transform.TransformPoint(new Vector3(0f, 0.85f, 0.06f)));
            // Đối chứng khớp tay: NPC bếp không che tay người chơi trong góc cạnh.
            var skins = Object.FindObjectsByType<HaiBanTay.Npc.NpcAgent>(FindObjectsSortMode.None)
                .SelectMany(x => x.GetComponentsInChildren<Renderer>()).Where(x => x.enabled).ToArray();
            foreach (var skin in skins) skin.enabled = false;
            yield return ScreenshotCapture.CaptureCamera(cam, Prefix + name);
            foreach (var skin in skins) skin.enabled = true;
            Object.Destroy(go);
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator ApronAndWorkingElbows()
        {
            yield return ChoreTestUtil.Galley();
            ChoreTestUtil.Look(new Vector3(2f, 0f, -1.2f), new Vector3(2f, 1.6f, 2f));
            yield return new WaitForSeconds(0.5f);
            Assert.IsNotNull(Rig.GetComponentInChildren<PlayerApron>());
            yield return Side("apron_right");
            Rig.look.SetAngles(Rig.look.Yaw, 80f);
            yield return new WaitForSeconds(0.5f);
            yield return Side("apron_bent_right");
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            ChoreTestUtil.Look(new Vector3(2f, 0f, -1.2f), new Vector3(2f, 1.6f, 2f));
            var sack = d.sacks.sacks[0]; sack.Locked = false;
            sack.Interact(Rig.interactor);
            yield return new WaitForSeconds(0.6f);
            Assert.AreSame(sack, Rig.interactor.Holding);
            Assert.IsTrue(sack.ShoulderCarried);
            yield return Side("sack_elbow_right");
            yield return ScreenshotCapture.Capture(Prefix + "sack_fp");
            Rig.interactor.ReleaseHeld(); sack.gameObject.SetActive(false);
            var water = d.storyStations.OfType<VoyageDelivery>().First(x => x.Kind == OrderKinds.Water);
            water.OnOrder(1); water.load.Interact(Rig.interactor);
            water.OnInteract(water.destination, Rig.interactor);
            ChoreTestUtil.Look(water.deliveryStand, water.destination.transform.position);
            yield return new WaitForSeconds(0.4f);
            Root.Input.SimUse = true;
            yield return PlayTestUtil.WaitUntil(() => water.Progress > 0.35f, 5f);
            Assert.Greater(water.Progress, 0.35f);
            yield return ScreenshotCapture.Capture(Prefix + "water_pouring_fp");
            yield return Side("water_elbow_right");
            yield return PlayTestUtil.WaitUntil(() => water.Pending == 0, 5f);
            Assert.AreEqual(0, water.Pending); Root.Input.SimUse = false;
            d.coal.hook.Interact(Rig.interactor);
            ChoreTestUtil.Look(new Vector3(0.3f, 0f, 1.5f), d.coal.bin.transform.position);
            yield return new WaitForSeconds(0.4f);
            Root.Input.SimUse = true;
            yield return PlayTestUtil.WaitUntil(() => d.coal.Shovel.Loaded, 5f);
            Assert.IsTrue(d.coal.Shovel.Loaded);
            ChoreTestUtil.Look(new Vector3(-2.5f, 0f, 0.8f), d.coal.stove.transform.position);
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(Rig.interactor.IsLookingAt(d.coal.stove), $"Tia gặp {Rig.interactor.LookCollider?.name}; bếp={d.coal.stove.transform.position}");
            yield return Side("coal_elbow_right");
            Root.Input.SimUse = false;
            yield return new WaitForSeconds(0.15f);
            Assert.IsFalse(d.coal.Shovel.Loaded);
            yield return ScreenshotCapture.Capture(Prefix + "coal_poured_fp");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator GreensAndSoldierClothes()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>(); var greens = d.greens;
            d.cast.StopAllCoroutines(); d.cast.enabled = false;
            greens.OnOrder(2); greens.OnInteract(greens.crate, Rig.interactor);
            greens.OnInteract(greens.board, Rig.interactor);
            greens.OnInteract(greens.board, Rig.interactor);
            ChoreTestUtil.Look(new Vector3(-1.3f, 0f, -0.65f), new Vector3(-0.6f, 0.95f, 0.1f));
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(greens.RawReady); Assert.IsTrue(greens.Logic.HasBunch);
            // Nhặt một bó qua đầu vào thật để rổ B có lá, rổ A vẫn còn bó rau.
            float end = Time.realtimeSinceStartup + 15f;
            while (greens.Logic.HasBunch && Time.realtimeSinceStartup < end)
            {
                Root.Input.SimUse = true;
                Root.Input.SimLook = new Vector2(greens.Logic.CurrentWilted ? -120f : 120f, 0f);
                yield return null;
                Root.Input.SimUse = false; Root.Input.SimLook = Vector2.zero; yield return null;
            }
            Assert.IsFalse(greens.Logic.HasBunch);
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(greens.keptLeaves.Any(x => x.gameObject.activeSelf));
            Root.UI.Shift.Clear(); Root.Subtitles.StopAll();
            yield return ScreenshotCapture.Capture(Prefix + "greens_raw_clean");
            var go = new GameObject("GreensSideCamera"); var cam = go.AddComponent<Camera>(); cam.CopyFrom(Rig.cam);
            cam.enabled = false; cam.fieldOfView = 38f;
            go.transform.position = new Vector3(-0.7f, 1.08f, -0.8f);
            go.transform.LookAt(new Vector3(-0.2f, 1.02f, 0.15f));
            yield return ScreenshotCapture.CaptureCamera(cam, Prefix + "greens_low_side"); Object.Destroy(go);
            foreach (var soldier in new[] { d.cast.soldierA, d.cast.soldierB })
            {
                soldier.gameObject.SetActive(true); soldier.Place(new Vector3(0.2f, 0f, -0.9f), 0f);
                soldier.Play("WorkTable");
                ChoreTestUtil.Look(new Vector3(-0.6f, 0f, 0.8f), new Vector3(0.2f, 1.25f, -0.9f));
                yield return new WaitForSeconds(0.6f);
                CleanUi();
                yield return ScreenshotCapture.Capture(Prefix + soldier.name + "_work");
                soldier.Play("SitIdle"); yield return new WaitForSeconds(0.6f);
                yield return ScreenshotCapture.Capture(Prefix + soldier.name + "_sit");
                var lod = soldier.GetComponentInChildren<LODGroup>();
                for (int level = 0; level < 3; level++)
                {
                    lod.ForceLOD(level);
                    ChoreTestUtil.Look(new Vector3(1.4f, 0f, -0.2f), new Vector3(.2f, 1.05f, -.9f));
                    yield return new WaitForSeconds(.3f); CleanUi();
                    yield return ScreenshotCapture.Capture(Prefix + soldier.name + "_side_lod" + level);
                }
                lod.ForceLOD(-1); soldier.gameObject.SetActive(false);
            }
        }
        [UnityTest, Timeout(240000)]
        public IEnumerator RecognizableCargo()
        {
            yield return ChoreTestUtil.Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            foreach (var kind in new[] { OrderKinds.Meat, OrderKinds.Fish, OrderKinds.Ice })
            {
                var task = d.storyStations.OfType<VoyageDelivery>().First(x => x.Kind == kind);
                task.OnOrder(1);
                ChoreTestUtil.Look(task.pickupStand, task.load.transform.position + Vector3.up * .2f);
                yield return new WaitForSeconds(.4f); CleanUi();
                yield return ScreenshotCapture.Capture(Prefix + kind + "_source");
                string meshName = kind == OrderKinds.Meat ? "ButcherLegCut" : kind == OrderKinds.Fish ? "WholeFish" : "BevelledIceBlock";
                Assert.IsTrue(task.load.GetComponentsInChildren<MeshFilter>().Any(x => x.sharedMesh.name == "Gen_" + meshName + "_v1"));
                task.load.Interact(Rig.interactor);
                Assert.AreSame(task.load, Rig.interactor.Holding);
                ChoreTestUtil.Look(new Vector3(-1.5f, 0f, .8f), new Vector3(-2.8f, 1f, 2.3f));
                yield return new WaitForSeconds(.4f); CleanUi();
                yield return ScreenshotCapture.Capture(Prefix + kind + "_held");
                if (kind == OrderKinds.Meat) yield return Side("meat_shoulder");
                task.OnInteract(task.destination, Rig.interactor);
                Assert.AreEqual(0, task.Pending);
                task.load.gameObject.SetActive(false);
            }
        }
        [UnityTest, Timeout(240000)]
        public IEnumerator HumanProjectionFrames()
        {
            var root = PlayTestUtil.Root(); root.ResetProgress();
            SceneManager.LoadScene(SceneFlow.PrologueScene);
            yield return PlayTestUtil.WaitUntil(() => Object.FindAnyObjectByType<ProjectionFlicker>() != null && Rig != null, 40f);
            var d = Object.FindAnyObjectByType<PrologueDirector>(); d.StopAllCoroutines();
            var projection = Object.FindAnyObjectByType<ProjectionFlicker>();
            Assert.AreEqual(12, projection.limbs.Length);
            ChoreTestUtil.Look(new Vector3(-9f, .12f, -7.2f), new Vector3(-9f, 1.9f, -9.3f));
            yield return new WaitForSeconds(.3f); CleanUi();
            var rotation = projection.limbs[0].localRotation;
            yield return ScreenshotCapture.Capture(Prefix + "cinema_frame_a");
            yield return new WaitForSeconds(.25f);
            Assert.AreNotEqual(rotation, projection.limbs[0].localRotation);
            yield return ScreenshotCapture.Capture(Prefix + "cinema_frame_b");
        }

    }
}
