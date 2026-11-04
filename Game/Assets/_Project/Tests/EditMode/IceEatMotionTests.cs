using HaiBanTay.Narrative;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    public class IceEatMotionTests
    {
        [Test]
        public void EachBiteStartsAndEndsAtTheSameRestPose()
        {
            for (int bite = 0; bite < IceEatMotion.Bites; bite++)
            {
                var start = IceEatMotion.Evaluate(bite, 0f);
                var end = IceEatMotion.Evaluate(bite, 1f);
                Assert.Less(Vector3.Distance(start.rightPosition, end.rightPosition), 1e-4f, "Hết thìa thì tay về chỗ nghỉ: " + bite);
                Assert.AreEqual(0f, start.spoonScoop, 1e-4f, "Đầu thìa chưa có kem trên thìa: " + bite);
                Assert.AreEqual(0f, end.spoonScoop, 1e-4f, "Cuối thìa kem đã vào miệng: " + bite);
            }
        }

        [Test]
        public void CupEmptiesStepByStepAndSpoonCarriesTheBiteToTheMouth()
        {
            float previous = 1f;
            for (int bite = 0; bite < IceEatMotion.Bites; bite++)
            {
                bool carried = false;
                for (int i = 0; i <= 100; i++)
                {
                    var frame = IceEatMotion.Evaluate(bite, i / 100f);
                    Assert.LessOrEqual(frame.cupScoop, previous + 1e-4f, $"Kem trong cốc không tự đầy lại (thìa {bite}, pha {i})");
                    previous = frame.cupScoop;
                    carried |= frame.spoonScoop > .99f;
                    Assert.IsFalse(float.IsNaN(frame.rightPosition.x + frame.rightEuler.z));
                }
                Assert.IsTrue(carried, "Thìa phải mang đủ miếng kem lên miệng: " + bite);
            }
            Assert.AreEqual(0f, previous, 1e-4f, "Ăn đủ số thìa thì cốc rỗng");
            Assert.AreEqual(0f, IceEatMotion.Idle(IceEatMotion.Bites).cupScoop, 1e-4f);
            Assert.AreEqual(1f, IceEatMotion.Idle(0).cupScoop, 1e-4f);
        }

        // Điểm giữa lòng thìa và đỉnh kem trong khung camera, theo đúng kiểu cầm mà PrologueDirector gắn cốc/thìa vào tay.
        static Vector3 Bowl(IceEatMotion.Frame frame) =>
            frame.rightPosition + Quaternion.Euler(frame.rightEuler) * (IceEatMotion.SpoonGrip.position
                + IceEatMotion.SpoonGrip.Rotation * new Vector3(0f, IceEatMotion.SpoonBowlY, IceEatMotion.SpoonBowlZ));

        static Quaternion CupRotation => Quaternion.Euler(IceEatMotion.LeftHandEuler) * IceEatMotion.CupGrip.Rotation;
        static Vector3 CupBase => IceEatMotion.LeftHandPosition + Quaternion.Euler(IceEatMotion.LeftHandEuler) * IceEatMotion.CupGrip.position;

        [Test]
        public void EachDipPutsTheBowlOnTheCreamLeft()
        {
            for (int bite = 0; bite < IceEatMotion.Bites; bite++)
            {
                var bowl = Bowl(IceEatMotion.Evaluate(bite, .2f));
                var top = CupBase + CupRotation * (Vector3.up * IceEatMotion.CreamTop(IceEatMotion.RemainingBefore(bite)) * IceEatMotion.CupScale);
                Assert.Less(Vector3.Distance(bowl, top), .015f, $"Thìa {bite}: lòng thìa phải tới đỉnh phần kem còn lại");
            }
        }

        [Test]
        public void RestingSpoonStaysOutsideTheCup()
        {
            var local = Quaternion.Inverse(CupRotation) * (Bowl(IceEatMotion.Idle(0)) - CupBase) / IceEatMotion.CupScale;
            bool inside = local.y < IceEatMotion.CupHeight && new Vector2(local.x, local.z).magnitude < IceEatMotion.CupTopRadius;
            Assert.IsFalse(inside, "Lúc nghỉ thìa nằm ngoài cốc: " + local);
        }

        [Test]
        public void MouthIsNearerTheCameraThanTheCup()
        {
            var dip = IceEatMotion.Evaluate(0, .25f);
            var mouth = IceEatMotion.Evaluate(0, .66f);
            Assert.Less(mouth.rightPosition.z, dip.rightPosition.z, "Đưa thìa lên miệng là kéo vào gần mắt");
        }
    }
}
