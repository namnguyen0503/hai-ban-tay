using HaiBanTay.Content;
using HaiBanTay.Npc;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>NpcGaze: góc nhìn tới mắt Ba theo hệ thân, giới hạn cổ, ngưỡng quay thân, phép xoay không lăn đầu.</summary>
    public sealed class GazeLogicTests
    {
        [Test]
        public void AnglesAreInTheBodyFrame()
        {
            GazeLogic.Angles(new Vector3(1f, 0f, 0f), 90f, out float yaw, out float pitch);
            Assert.AreEqual(0f, yaw, 1e-3f, "thân quay 90° nhìn +X: Ba ở thẳng trước mặt");
            Assert.AreEqual(0f, pitch, 1e-3f);
            GazeLogic.Angles(new Vector3(1f, 1f, 0f), 0f, out yaw, out pitch);
            Assert.AreEqual(90f, yaw, 1e-3f, "Ba bên phải");
            Assert.AreEqual(45f, pitch, 1e-3f, "Ba cao hơn: ngẩng");
            var dir = GazeLogic.Direction(30f, 20f, 10f);
            GazeLogic.Angles(dir, 30f, out yaw, out pitch);
            Assert.AreEqual(20f, yaw, 1e-3f);
            Assert.AreEqual(10f, pitch, 1e-3f);
        }

        [Test]
        public void NeckIsClampedAndGivesUpBehind()
        {
            float yaw = 100f, pitch = -60f;
            Assert.IsTrue(GazeLogic.Clamp(ref yaw, ref pitch), "100°: vẫn ngoái được (tới giới hạn)");
            Assert.AreEqual(GazeLogic.MaxYaw, yaw);
            Assert.AreEqual(-GazeLogic.MaxPitchDown, pitch);
            yaw = -150f; pitch = 0f;
            Assert.IsFalse(GazeLogic.Clamp(ref yaw, ref pitch), "Ba sau lưng: không ngoái");
        }

        [Test]
        public void BodyTurnsOnlyPastTheStartAngleAndStopsNearFacing()
        {
            Assert.IsFalse(GazeLogic.BodyTurning(false, 30f), "lệch ít: chỉ đầu theo");
            Assert.IsTrue(GazeLogic.BodyTurning(false, 50f));
            Assert.IsTrue(GazeLogic.BodyTurning(true, 20f), "đã quay thì quay tới gần thẳng mặt");
            Assert.IsFalse(GazeLogic.BodyTurning(true, 4f));
        }

        [Test]
        public void DeltaTurnsFaceOntoTargetWithoutRoll()
        {
            var face = Quaternion.Euler(10f, -20f, 0f) * Vector3.forward;
            var want = Quaternion.Euler(-15f, 60f, 0f) * Vector3.forward;
            var q = GazeLogic.Delta(face, want);
            Assert.Less(Vector3.Angle(q * face, want), 0.1f);
            // Đỉnh đầu (vuông góc mặt, trong mặt phẳng đứng) vẫn ở mặt phẳng đứng chứa hướng nhìn: không nghiêng sang bên.
            var up = Vector3.Cross(Vector3.Cross(face, Vector3.up), face).normalized;
            var newUp = q * up;
            var side = Vector3.Cross(Vector3.up, want).normalized;
            Assert.Less(Mathf.Abs(Vector3.Dot(newUp, side)), 0.02f, "không lăn đầu");
        }

        [Test]
        public void SubtitleGazeFlagsMapToModes()
        {
            Assert.AreEqual(NpcGaze.Mode.Talk, TalkGaze.ModeOf(null));
            Assert.AreEqual(NpcGaze.Mode.Task, TalkGaze.ModeOf("task"));
            Assert.AreEqual(NpcGaze.Mode.Other, TalkGaze.ModeOf("other"));
            var db = ContentDatabase.LoadFromResources();
            Assert.AreEqual("task", db.GetLine("mai_peel").Gaze, "làm mẫu gọt: không nhìn theo Ba");
            Assert.IsNull(db.GetLine("chef_fire").Gaze, "lệnh của quản bếp vẫn nói với Ba");
        }
    }
}
