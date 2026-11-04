using System.Collections;
using HaiBanTay.Content;
using HaiBanTay.Tasks;
using HaiBanTay.Core;
using HaiBanTay.Narrative;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>Ảnh đối chiếu chores 10, cùng camera/UI của gameplay.</summary>
    [Category("Screenshots"), Explicit]
    public class ChoreShots
    {
        static GameRoot Root => GameRoot.Instance;
        static PlayerRig Rig => PlayerRig.Current;

        [TearDown]
        public void TearDown()
        {
            ScreenshotCapture.RestoreUi();
            PlayTestUtil.ExitSim();
        }

        static IEnumerator Galley() => ChoreTestUtil.Galley();

        static void Look(Vector3 feet, Vector3 point)
        {
            var direction = point - feet - Vector3.up * 1.62f;
            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
            Rig.motor.Teleport(feet, yaw);
            Rig.look.SetAngles(yaw, pitch);
        }

        [UnityTest]
        public IEnumerator ShovelCloseAndHeld()
        {
            yield return Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            Look(new Vector3(1.1f, 0f, 1.7f), new Vector3(1.1f, 0.75f, 2.9f));
            yield return new WaitForSeconds(0.5f);
            yield return ScreenshotCapture.Capture("02ch01_shovel_close");
            d.coal.hook.Interact(Rig.interactor);
            Rig.hands.SetShovelLoaded(true);
            Look(new Vector3(-2.5f, 0f, 0.8f), new Vector3(-3f, 0.8f, 2.3f));
            yield return new WaitForSeconds(0.5f);
            yield return ScreenshotCapture.Capture("02ch02_shovel_held");
        }
        [UnityTest, Timeout(600000)]
        public IEnumerator PotatoSinkMealGreensStudyAndTemperature()
        {
            yield return Galley();
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            var sacks = d.sacks;
            d.meals.cook.enabled = false;
            Look(new Vector3(1.32f, 0f, -1.05f), sacks.mouth.transform.position);
            sacks.zone.Place(sacks.sacks[0]);
            sacks.OnInteract(sacks.mouth, Rig.interactor);
            Root.Input.SimUse = true;
            float end = Time.realtimeSinceStartup + 12f;
            while (sacks.Pour.Progress < 0.65f && Time.realtimeSinceStartup < end)
            {
                Root.Input.SimLook = new Vector2(0f, -12f);
                yield return null;
            }
            Root.Input.SimLook = Vector2.zero;
            Assert.Greater(sacks.Pour.Progress, 0.6f);
            Assert.IsTrue(sacks.OnTable.transform.Find("OpenSack").gameObject.activeSelf);
            Assert.IsFalse(sacks.OnTable.transform.Find("Art").gameObject.activeSelf);
            Assert.Greater(sacks.Rolling, 0);
            Assert.AreEqual(HandPose.Carry, Rig.hands.Pose);
            // Chờ củ rời miệng bao; chụp ngay khung phát củ chỉ thấy các mesh chồng nhau ở miệng.
            yield return new WaitForSeconds(0.2f);
            Assert.Greater(sacks.Rolling, 0);
            yield return ScreenshotCapture.Capture("02ch03_pour_two_hands_falling");
            Root.Input.SimUse = false;
            sacks.FinishPour();
            yield return PlayTestUtil.WaitUntil(() => sacks.Rolling == 0, 5f);
            Assert.AreEqual(20, sacks.InTub);
            Assert.AreEqual(0, sacks.Rolling);
            yield return ScreenshotCapture.Capture("02ch03b_potatoes_settled_in_tub");
            d.meals.BeginMeal(new PhaseData { Id = "ảnh_bữa_trưa", Dirty = 8 });
            d.meals.AddPotatoes();
            Look(new Vector3(-1.2f, 0f, 0.85f), new Vector3(-2.6f, 1.15f, 2.45f));
            yield return new WaitForSeconds(0.5f);
            yield return ScreenshotCapture.Capture("02ch04_meal_cooking");
            Look(new Vector3(1f, 0f, -1f), new Vector3(-3f, 1f, 2.45f));
            yield return d.meals.Serve();
            Assert.AreEqual(8, d.meals.Pending);
            d.meals.BeginMeal(new PhaseData { Id = "ảnh_nồi_cao", CookTall = true });
            yield return d.meals.Serve();
            Look(new Vector3(5.5f, 0f, -0.75f), new Vector3(5.9f, 0.8f, -2.5f));
            yield return new WaitForSeconds(0.5f);
            yield return ScreenshotCapture.Capture("02ch05_dirty_pile_sink");
            var wash = d.wash;
            wash.OnInteract(wash.pile, Rig.interactor);
            wash.OnInteract(wash.basin, Rig.interactor); wash.OpenTap(); // nước chảy rồi mới cọ (QA-0132)
            Look(new Vector3(5.85f, 0f, -1.6f), wash.basin.transform.position + Vector3.up * 0.68f);
            // Camera Cinemachine và tia tương tác cập nhật sau teleport, trước khi rê để cọ.
            yield return new WaitForSeconds(0.3f);
            Root.Input.SimUse = true;
            Root.Input.SimLook = new Vector2(12f, 8f);
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(Rig.interactor.IsLookingAt(wash.basin),
                $"Tia cọ gặp {Rig.interactor.LookCollider?.name}; nồi={wash.basinPot.transform.position}; bồn={wash.basin.transform.position}; state={wash.State}; tay trống={Rig.interactor.HandsFree}");
            Assert.IsTrue(Rig.hands.brushProp.activeSelf,
                $"state={wash.State}; tay trống={Rig.interactor.HandsFree}; giữ={Root.Input.UseHeld}; trong bồn={wash.Logic.InBasin}; đang mang={wash.CarryingPot}; cọ={wash.Logic.Scrub.Progress}");
            yield return ScreenshotCapture.Capture("02ch06_sink_brush_in_hand");
            Root.Input.SimUse = false; Root.Input.SimLook = Vector2.zero;
            yield return null;
            yield return ChoreTestUtil.Scrub(wash.Logic.Scrub);
            Root.Input.SimUse = true;
            yield return PlayTestUtil.WaitUntil(() => wash.Rinsed, 5f);
            Root.Input.SimUse = false;
            Assert.IsTrue(wash.Rinsed);
            wash.OnInteract(wash.rack, Rig.interactor);
            Assert.IsFalse(wash.Logic.InBasin, "Bồn trống rồi mới đưa rổ rau vào rửa.");
            Look(new Vector3(4.3f, 0f, 0.1f), new Vector3(4.4f, 0.65f, -1.8f));
            yield return new WaitForSeconds(0.5f);
            yield return ScreenshotCapture.Capture("02ch07_inverted_tall_pot_stool");
            var greens = d.greens;
            greens.OnOrder(2);
            greens.OnInteract(greens.crate, Rig.interactor);
            greens.OnInteract(greens.board, Rig.interactor);
            Look(new Vector3(-1.3f, 0f, -0.65f), new Vector3(-0.6f, 0.95f, 0.1f));
            greens.OnInteract(greens.board, Rig.interactor);
            yield return null;
            yield return ScreenshotCapture.Capture("02ch08_greens_baskets_A_B");
            for (int bunch = 0; bunch < 2; bunch++)
            {
                if (!greens.Logic.HasBunch) greens.OnInteract(greens.board, Rig.interactor);
                float sortEnd = Time.realtimeSinceStartup + 15f;
                while (greens.Logic.HasBunch && Time.realtimeSinceStartup < sortEnd)
                {
                    Root.Input.SimUse = true;
                    Root.Input.SimLook = new Vector2(greens.Logic.CurrentWilted ? -120f : 120f, 0f);
                    yield return null;
                    Root.Input.SimUse = false; Root.Input.SimLook = Vector2.zero;
                    yield return null;
                }
            }
            Assert.IsFalse(greens.Logic.HasBunch, "Nhặt rau phải hoàn thành bằng đầu vào thật.");
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(greens.ReadyToWash);
            greens.OnInteract(greens.cleanTarget, Rig.interactor);
            greens.OnInteract(greens.sink, Rig.interactor);
            Look(new Vector3(5.85f, 0f, -1.6f), greens.sink.transform.position);
            Root.Input.SimUse = true;
            yield return new WaitForSeconds(0.5f);
            Assert.Greater(greens.WashProgress, 0f);
            yield return ScreenshotCapture.Capture("02ch09_greens_washing");
            Root.Input.SimUse = false;
            yield return null;
            foreach (var go in d.eveningOnly) if (go != null) go.SetActive(true);
            d.Clock.Minutes = 22f * 60f;
            d.lights.SetInterior(d.coal.fireLight, d.eveningLamp);
            Root.UI.Shift.Show(false);
            d.cast.Begin(Root.Content.GetDay(2));
            d.cast.BeginEvening();
            yield return new WaitForSeconds(0.5f);
            d.study.Begin(StudyPlanner.Make(Root.Content, Root.State, Root.Content.GetDay(2).Evening, 22f * 60f));
            d.study.Activate();
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(Rig.GetComponent<SitController>().StudySeated);
            yield return ScreenshotCapture.Capture("02ch10_study_seated_chair");
            d.study.ForceComplete();
            yield return new WaitForSeconds(0.4f);
            d.Clock.Minutes = 6f * 60f;
            d.lights.SetInterior(d.coal.fireLight, d.dayLamp);
            BodyState.Current.enabled = false;
            Look(new Vector3(-1.2f, 0f, 0.85f), new Vector3(-2.6f, 1.15f, 2.45f));
            Root.UI.Tint.Clear(); Root.UI.Tint.SetBody(0f, 1f, 0f);
            yield return new WaitForSeconds(1.5f);
            yield return ScreenshotCapture.Capture("02ch11_heat_reduced");
            Root.UI.Tint.Clear(); Root.UI.Tint.SetBody(1f, 0f, 0f);
            yield return new WaitForSeconds(1.5f);
            yield return ScreenshotCapture.Capture("02ch12_cold_reduced");
        }
    }
}
