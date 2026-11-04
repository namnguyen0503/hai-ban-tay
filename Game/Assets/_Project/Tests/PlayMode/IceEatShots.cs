using System;
using System.Collections;
using System.IO;
using HaiBanTay.Core;
using HaiBanTay.Narrative;
using HaiBanTay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>Ảnh các pha ăn kem ở prologue (cốc giấy, thìa xanh, miếng kem) để so với ảnh tham chiếu. Tay đứng yên ở từng pha rồi chụp.</summary>
    [Category("Screenshots"), Explicit]
    public class IceEatShots
    {
        static GameRoot Root => GameRoot.Instance;
        static PlayerRig Rig => PlayerRig.Current;

        static IEnumerator Until(Func<bool> done)
        {
            yield return PlayTestUtil.WaitUntil(done, 240f);
            Assert.IsTrue(done(), "Không tới nhịp cần chụp: " + SceneDirector.Current?.Beat);
        }

        static IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        /// <summary>Quay đầu nhìn người bán kem như ảnh tham chiếu (không dịch chuyển người chơi).</summary>
        static void LookAtSeller(PrologueDirector d)
        {
            var delta = d.iceSeller.Position + Vector3.up * 1.45f - Rig.head.position;
            Rig.look.AssistTarget = null;
            Rig.look.SetAngles(Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg, -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg);
        }

        static IEnumerator Start(Action<PrologueDirector> ready)
        {
            PlayTestUtil.Root().ResetProgress();
            PlayTestUtil.EnterSim(1f);
            Time.captureDeltaTime = 1f / 60f;
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.PrologueScene);
            ScreenshotCapture.RedirectUi();
            var d = UnityEngine.Object.FindAnyObjectByType<PrologueDirector>();
            yield return Until(() => d.iceCup.gameObject.activeInHierarchy && d.iceCup.parent == Rig.hands.left);
            ready(d);
        }

        [TearDown]
        public void Done()
        {
            Time.captureDeltaTime = 0f;
            ScreenshotCapture.RestoreUi();
            PlayTestUtil.ExitSim();
            SceneFlow.FromPrologue = false;
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator IceEatPhases()
        {
            PlayTestUtil.Root().ResetProgress();
            PlayTestUtil.EnterSim(1f);
            Time.captureDeltaTime = 1f / 60f;
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.PrologueScene);
            ScreenshotCapture.RedirectUi();
            var d = UnityEngine.Object.FindAnyObjectByType<PrologueDirector>();
            // Cốc của anh Lê lúc đưa kem.
            yield return Until(() => d.leIceCup.gameObject.activeInHierarchy);
            Sim.Enabled = false;
            Rig.motor.Teleport(d.le.Position + new Vector3(1.4f, 0f, -1.1f), 0f);
            var toLe = d.le.Position + Vector3.up * 1.15f - Rig.head.position;
            Rig.look.AssistTarget = null;
            Rig.look.SetAngles(Mathf.Atan2(toLe.x, toLe.z) * Mathf.Rad2Deg, -Mathf.Atan2(toLe.y, new Vector2(toLe.x, toLe.z).magnitude) * Mathf.Rad2Deg);
            yield return Wait(.5f);
            yield return ScreenshotCapture.Capture("ice_le_cup");
            Sim.Enabled = true;
            yield return Until(() => d.iceCup.gameObject.activeInHierarchy && d.iceCup.parent == Rig.hands.left);
            Sim.Enabled = false;
            Rig.motor.Teleport(new Vector3(6.1f, .12f, -7.3f), 0f);
            Sim.Enabled = true;
            float per = PrologueDirector.IceEatSeconds / IceEatMotion.Bites;
            // (tên ảnh, số giây đã giữ chuột): thìa 1 với tay, xúc, đưa lên, ăn; thìa 2 giữa đường; cuối cùng cốc rỗng.
            var phases = new[]
            {
                ("ice_a_rest", 0f), ("ice_b_dip", .25f * per), ("ice_c_lift", .45f * per), ("ice_d_mouth", .66f * per),
                ("ice_e_second_lift", 1.5f * per), ("ice_f_third_dip", 2.27f * per), ("ice_h_last_mouth", 2.7f * per), ("ice_g_empty", PrologueDirector.IceEatSeconds)
            };
            foreach (var (name, seconds) in phases)
            {
                Sim.Enabled = true;
                yield return Until(() => d.IceEaten >= seconds || d.Beat != "P1b");
                Sim.Enabled = false;
                LookAtSeller(d);
                // Ảnh đầu: tay vừa nhận cốc còn đang trượt về tư thế nghỉ, chờ lâu hơn cho tới chỗ.
                yield return Wait(name == "ice_a_rest" ? 1.5f : .38f);
                DumpPose(name);
                yield return ScreenshotCapture.Capture(name);
            }
        }

        /// <summary>Ghi khung tay, vai/khuỷu/cổ tay của thân (khung camera) ra Logs/shots/ice_pose.txt để dựng lại ảnh ngoài Unity.</summary>
        static void DumpPose(string name)
        {
            var cam = Rig.hands.transform.parent;
            var animator = Rig.body.GetComponentInChildren<Animator>();
            string P(Vector3 world) { var p = cam.InverseTransformPoint(world); return $"{p.x:F4},{p.y:F4},{p.z:F4}"; }
            string Bone(HumanBodyBones bone) => P(animator.GetBoneTransform(bone).position);
            string Local(Transform t) => $"{P(t.position)};{(Quaternion.Inverse(cam.rotation) * t.rotation).eulerAngles.x:F2},{(Quaternion.Inverse(cam.rotation) * t.rotation).eulerAngles.y:F2},{(Quaternion.Inverse(cam.rotation) * t.rotation).eulerAngles.z:F2}";
            var line = $"{name} L={Local(Rig.hands.left)} R={Local(Rig.hands.right)} " +
                $"LS={Bone(HumanBodyBones.LeftUpperArm)} LE={Bone(HumanBodyBones.LeftLowerArm)} LW={Bone(HumanBodyBones.LeftHand)} " +
                $"RS={Bone(HumanBodyBones.RightUpperArm)} RE={Bone(HumanBodyBones.RightLowerArm)} RW={Bone(HumanBodyBones.RightHand)} " +
                $"fov={Camera.main.fieldOfView:F1}\n";
            File.AppendAllText(Path.Combine(Application.dataPath, "../../Logs/shots/ice_pose.txt"), line);
        }

        [Serializable]
        public class IceVariant
        {
            public string name;
            public FingerPose left, right;
            public bool cupDefault, spoonDefault;
            public Vector3 cupPos, cupEuler, spoonPos, spoonEuler, leftHandPos, leftHandEuler, restPos, restEuler;
        }

        [Serializable]
        public class IceVariantList { public IceVariant[] variants; }

        /// <summary>Thử nhiều bộ tư thế ngón/vị trí cầm một lượt, đọc từ Logs/ice_variants.json (không cần biên dịch lại): ảnh ice_&lt;tên&gt;.png.</summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator IceHandVariants()
        {
            string path = Path.Combine(Application.dataPath, "../../Logs/ice_variants.json");
            var list = JsonUtility.FromJson<IceVariantList>(File.ReadAllText(path));
            PrologueDirector d = null;
            yield return Start(x => d = x);
            Sim.Enabled = false;
            Rig.motor.Teleport(new Vector3(6.1f, .12f, -7.3f), 0f);
            var spoon = Rig.hands.right.Find("Spoon");
            Assert.IsTrue(Rig.hands.LeftFingersActive && Rig.hands.RightFingersActive, "tay có xương phải bật trong cảnh ăn kem");
            foreach (var v in list.variants)
            {
                IceEatMotion.LeftHandPosition = v.leftHandPos;
                IceEatMotion.LeftHandEuler = v.leftHandEuler;
                IceEatMotion.Rest = new IceEatMotion.Key(v.restPos.x, v.restPos.y, v.restPos.z, v.restEuler.x, v.restEuler.y, v.restEuler.z);
                d.iceCup.localPosition = v.cupDefault ? IceEatMotion.CupGrip.position : v.cupPos;
                d.iceCup.localRotation = Quaternion.Euler(v.cupDefault ? IceEatMotion.CupGrip.euler : v.cupEuler);
                spoon.localPosition = v.spoonDefault ? IceEatMotion.SpoonGrip.position : v.spoonPos;
                spoon.localRotation = Quaternion.Euler(v.spoonDefault ? IceEatMotion.SpoonGrip.euler : v.spoonEuler);
                Rig.hands.leftFingers.Target = v.left;
                Rig.hands.rightFingers.Target = v.right;
                Rig.hands.leftFingers.Snap();
                Rig.hands.rightFingers.Snap();
                LookAtSeller(d);
                yield return Wait(.5f);
                yield return ScreenshotCapture.Capture("ice_" + v.name);
            }
        }
    }
}
