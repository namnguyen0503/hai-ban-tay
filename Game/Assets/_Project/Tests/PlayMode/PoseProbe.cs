using System.Collections;
using HaiBanTay.Npc;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>Đo tư thế clip (vai, hông, đầu theo hệ NPC) ở vài thời điểm, để đặt chỗ cầm dụng cụ trong tầm tay. Chạy tay.</summary>
    [Explicit]
    public sealed class PoseProbe
    {
        /// <summary>
        /// Tốc độ cổ tay (hệ thân) theo từng nhịp 60 Hz trong clip đi, đứng: tìm chỗ clip tự khựng rồi vụt (verify 14/10: cổ tay đứng yên
        /// một nhịp rồi dời 2,1 cm ở Walk 0,40, không có chuyển trạng thái).
        /// </summary>
        [UnityTest, Timeout(180000)]
        public IEnumerator ClipWristSpeeds()
        {
            yield return ChoreTestUtil.Galley();
            var helper = Object.FindAnyObjectByType<HelperRoutine>();
            helper.enabled = false;
            var npc = helper.transform;
            var animator = npc.GetComponentInChildren<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            yield return null;
            foreach (var clip in new[] { "Walk", "Idle", "CarryWalk" })
            {
                int hash = Animator.StringToHash(clip);
                if (!animator.HasState(0, hash)) { Debug.Log($"WRIST {clip}: không có"); continue; }
                animator.enabled = false;
                animator.Play(hash, 0, 0f);
                animator.Update(0f);
                var l = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                var r = animator.GetBoneTransform(HumanBodyBones.RightHand);
                Vector3 lastL = npc.InverseTransformPoint(l.position), lastR = npc.InverseTransformPoint(r.position);
                float lastStep = 0f;
                var sb = new System.Text.StringBuilder();
                for (int i = 1; i <= 150; i++)
                {
                    animator.Update(1f / 60f);
                    var nl = npc.InverseTransformPoint(l.position);
                    var nr = npc.InverseTransformPoint(r.position);
                    float step = Mathf.Max((nl - lastL).magnitude, (nr - lastR).magnitude);
                    var info = animator.GetCurrentAnimatorStateInfo(0);
                    if (step > 0.015f && step > 2.5f * lastStep + 0.005f)
                        sb.Append($" [nhịp {i} t={info.normalizedTime:F2}: {lastStep * 100f:F1}→{step * 100f:F1} cm]");
                    sb.Append(i % 10 == 0 ? $"\n  {i}: {step * 100f:F1}" : $" {step * 100f:F1}");
                    lastL = nl; lastR = nr; lastStep = step;
                }
                animator.enabled = true;
                Debug.Log($"WRIST {clip}:{sb}");
                yield return null;
            }
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator ClipPoses()
        {
            yield return ChoreTestUtil.Galley();
            var helper = Object.FindAnyObjectByType<HelperRoutine>();
            helper.enabled = false;
            var npc = helper.transform;
            var animator = npc.GetComponentInChildren<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var clip in new[] { "Idle", "PickUp", "Squat", "WorkMid", "WorkTable" })
            {
                int hash = Animator.StringToHash(clip);
                if (!animator.HasState(0, hash)) { Debug.Log($"POSE {clip}: không có"); continue; }
                foreach (float t in new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f })
                {
                    animator.speed = 1f;
                    animator.Play(hash, 0, t);
                    animator.Update(0f);
                    animator.speed = 0f;
                    yield return null;
                    yield return null;
                    string P(HumanBodyBones b) { var x = animator.GetBoneTransform(b); var l = npc.InverseTransformPoint(x.position); return $"({l.x:F2},{l.y:F2},{l.z:F2})"; }
                    Debug.Log($"POSE {clip} t={t:F1} hips={P(HumanBodyBones.Hips)} head={P(HumanBodyBones.Head)} lShoulder={P(HumanBodyBones.LeftUpperArm)} rShoulder={P(HumanBodyBones.RightUpperArm)} lHand={P(HumanBodyBones.LeftHand)} rHand={P(HumanBodyBones.RightHand)} lKnee={P(HumanBodyBones.LeftLowerLeg)}");
                }
                animator.speed = 1f;
            }
            var arm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var fore = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Debug.Log($"POSE arm lengths upper={Vector3.Distance(arm.position, fore.position):F3} fore={Vector3.Distance(fore.position, hand.position):F3}");
        }
    }
}
