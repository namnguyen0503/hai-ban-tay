using System.Collections;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Yêu cầu 15/10: NPC nói chuyện với Ba thì mặt nhìn theo mắt Ba (trừ lúc làm mẫu thái gọt), đã nói thẳng mặt thì thân quay
    /// theo Ba. Ông Mai đứng ở chỗ của mình trong bếp, Ba đứng lệch sang bên rồi đi vòng.
    /// </summary>
    public sealed class TalkGazePlayTests
    {
        [TearDown] public void CleanUp() => PlayTestUtil.ExitSim();

        static NpcAgent StandMai(out NpcGaze gaze)
        {
            var cast = Object.FindAnyObjectByType<DayCast>();
            var mai = cast.mai;
            mai.gameObject.SetActive(true);
            mai.Place(cast.maiSpot.position, cast.maiSpot.eulerAngles.y);
            mai.Play("Idle");
            gaze = NpcGaze.Of(mai);
            return mai;
        }

        /// <summary>Đặt Ba cách NPC distance m, lệch angle độ so với hướng mặt NPC (dương: bên phải NPC), mặt nhìn về NPC.</summary>
        static void PlaceBa(NpcAgent npc, float angle, float distance)
        {
            var dir = Quaternion.Euler(0f, npc.transform.eulerAngles.y + angle, 0f) * Vector3.forward;
            var feet = npc.Position + dir * distance;
            ChoreTestUtil.Look(feet, npc.Position + Vector3.up * 1.6f);
        }

        static float HeadToEye(NpcGaze gaze, Transform head)
        {
            var eye = PlayerRig.Current.cam.transform.position;
            return Vector3.Angle(head.rotation * gaze.FaceLocal, eye - head.position);
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator MaiLooksAtBasEyesThenTurnsToFaceHim()
        {
            yield return ChoreTestUtil.Galley();
            var mai = StandMai(out var gaze);
            var head = mai.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.Head);
            yield return new WaitForSeconds(0.5f);

            // Hướng mặt lấy từ bind pose: đứng Idle thì mặt gần trùng hướng thân (sai số hướng mặt = sai số nhìn).
            var face = head.rotation * gaze.FaceLocal;
            float idleOff = Vector3.Angle(new Vector3(face.x, 0f, face.z), mai.transform.forward);
            Debug.Log($"GAZE_IDLE_FACE bind pose {gaze.FromBindPose} lệch thân {idleOff:F1}° ngẩng {Mathf.Asin(face.y) * Mathf.Rad2Deg:F1}°");
            Assert.IsTrue(gaze.FromBindPose, "hướng mặt lấy từ bind pose, không từ tư thế clip lúc thêm component");
            Assert.Less(idleOff, 25f, "hướng mặt theo bind pose phải gần hướng thân khi đứng Idle");

            // Ba lệch 30° bên phải, cách 1,6 m: chỉ đầu quay theo, thân đứng yên (dưới ngưỡng quay thân).
            PlaceBa(mai, 30f, 1.6f);
            float yaw0 = mai.transform.eulerAngles.y;
            TalkGaze.Bind("ong_mai", mai);
            TalkGaze.LineStarted("ong_mai", null, 30f);
            yield return new WaitForSeconds(1.2f);
            Assert.Greater(gaze.Weight, 0.99f, "đang nói với Ba thì đầu theo hẳn");
            float err = HeadToEye(gaze, head);
            Debug.Log($"GAZE_SIDE lệch mắt Ba {err:F1}° thân quay {Mathf.DeltaAngle(yaw0, mai.transform.eulerAngles.y):F1}°");
            Assert.Less(err, 8f, "mặt ông Mai nhìn vào mắt Ba");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(yaw0, mai.transform.eulerAngles.y)), 0.5f, "lệch ít: thân không quay");

            // Ba đi vòng sang ngang 100°: thân quay theo tới gần thẳng mặt, đầu vẫn nhìn mắt Ba; đo tốc độ quay đầu theo giờ game.
            // Tốc độ quay đầu đo theo giờ game, mỗi 1/60 s (batch chạy ~1 ms/khung: chia theo khung thì nhiễu).
            var lastFace = head.rotation * gaze.FaceLocal;
            float worstSpeed = 0f, window = 0f;
            void Sample(ref Vector3 last, ref float elapsed, ref float worst)
            {
                elapsed += Time.deltaTime;
                if (elapsed < 1f / 60f) return;
                var f = head.rotation * gaze.FaceLocal;
                worst = Mathf.Max(worst, Vector3.Angle(last, f) / elapsed);
                last = f;
                elapsed = 0f;
            }
            for (int step = 1; step <= 7; step++)
            {
                PlaceBa(mai, 30f + step * 10f, 1.6f);
                for (float t = 0f; t < 0.25f; t += Time.deltaTime)
                {
                    yield return null;
                    Sample(ref lastFace, ref window, ref worstSpeed);
                }
            }
            for (float t = 0f; t < 3f && (gaze.BodyTurning || gaze.BodyError > GazeLogic.BodyTurnStop + 1f); t += Time.deltaTime)
            {
                yield return null;
                Sample(ref lastFace, ref window, ref worstSpeed);
            }
            yield return new WaitForSeconds(0.5f);
            err = HeadToEye(gaze, head);
            Debug.Log($"GAZE_AROUND thân lệch {gaze.BodyError:F1}° mặt lệch mắt Ba {err:F1}° đầu quay nhanh nhất {worstSpeed:F0}°/s");
            Assert.Less(gaze.BodyError, GazeLogic.BodyTurnStop + 1f, "Ba đi vòng sang ngang: thân quay theo cho thẳng mặt");
            Assert.Less(err, 8f, "mặt vẫn nhìn mắt Ba");
            Assert.Less(worstSpeed, 400f, "đầu không giật (quay mượt theo Ba)");

            // Hết câu (cộng Linger) thì đầu trả về clip.
            gaze.Release();
            yield return new WaitForSeconds(NpcGaze.BlendSeconds + 0.2f);
            Assert.AreEqual(0f, gaze.Weight, 1e-4f, "hết chuyện thì đầu theo clip");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator DemoLineKeepsTheClipHeadAndBaRepliesKeepMaiListening()
        {
            yield return ChoreTestUtil.Galley();
            var mai = StandMai(out var gaze);
            yield return new WaitForSeconds(0.3f);
            PlaceBa(mai, 70f, 1.6f);
            float yaw0 = mai.transform.eulerAngles.y;
            TalkGaze.Bind("ong_mai", mai);

            // Câu làm mẫu ("gaze": "task", ví dụ mai_peel): không nhìn theo, không quay thân.
            TalkGaze.LineStarted("ong_mai", "task", 2f);
            for (float t = 0f; t < 1.5f; t += Time.deltaTime)
            {
                yield return null;
                Assert.AreEqual(0f, gaze.Weight, 1e-4f, "làm mẫu thái gọt: đầu theo clip");
            }
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(yaw0, mai.transform.eulerAngles.y)), 0.5f, "làm mẫu: thân không quay theo Ba");

            // Câu thường 1 s rồi Ba đáp 5 s: ông Mai vẫn nhìn Ba suốt câu đáp.
            TalkGaze.LineStarted("ong_mai", null, 1f);
            yield return new WaitForSeconds(0.5f);
            TalkGaze.LineStarted(TalkGaze.Player, null, 5f);
            yield return new WaitForSeconds(1f + TalkGaze.Linger + 0.5f);
            Assert.IsTrue(gaze.Talking, "Ba đang đáp: người vừa nói vẫn nhìn Ba");
            Assert.Greater(gaze.Weight, 0.99f);
            Assert.Less(gaze.BodyError, GazeLogic.BodyTurnStop + 1f, "và đã quay thân thẳng mặt Ba");

            // Câu nói với người khác ("gaze": "other"): thôi nhìn Ba.
            TalkGaze.LineStarted("ong_mai", "other", 3f);
            yield return new WaitForSeconds(NpcGaze.BlendSeconds + 0.2f);
            Assert.AreEqual(0f, gaze.Weight, 1e-4f, "nói với người khác: đầu theo clip");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator WalkingNpcDoesNotTurnItsHeadToBa()
        {
            yield return ChoreTestUtil.Galley();
            var cast = Object.FindAnyObjectByType<DayCast>();
            var mai = StandMai(out var gaze);
            PlaceBa(mai, 60f, 2f);
            TalkGaze.Bind("ong_mai", mai);
            TalkGaze.LineStarted("ong_mai", null, 10f);
            yield return new WaitForSeconds(1f);
            Assert.Greater(gaze.Weight, 0.99f);
            var away = cast.maiSpot.position - mai.transform.forward * 2f;
            mai.StartCoroutine(mai.Go(away));
            yield return PlayTestUtil.WaitUntil(() => mai.Walking, 2f);
            yield return new WaitForSeconds(NpcGaze.BlendSeconds + 0.1f);
            if (mai.Walking)
                Assert.AreEqual(0f, gaze.Weight, 1e-4f, "đang đi: đầu theo clip");
        }
    }
}
