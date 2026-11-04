using HaiBanTay.Player;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>Chiều quay của xương ngón: gập dương đưa đầu ngón về phía lòng bàn tay (−Y), dạng dương đưa ngón ra xa ngón giữa,
    /// đúng cho cả tay trái lẫn tay phải (đối xứng qua x).</summary>
    public class HandFingersTests
    {
        // Đầu xương (khung bàn tay trái) của Thumb1/Index1/Middle1/Ring1/Little1 và độ dài các đốt, đo từ Hand_L_Rig.json.
        static readonly Vector3[] Heads =
        {
            new Vector3(.0299f, -.0118f, .0273f), new Vector3(.0185f, -.0067f, .0950f), new Vector3(-.0052f, -.0041f, .0954f),
            new Vector3(-.0269f, -.0052f, .0887f), new Vector3(-.0442f, -.0122f, .0778f)
        };
        static readonly Vector3[] Directions =
        {
            new Vector3(.62f, -.06f, .78f).normalized, new Vector3(.12f, 0f, .99f).normalized, Vector3.forward,
            new Vector3(-.09f, 0f, .99f).normalized, new Vector3(-.2f, 0f, .98f).normalized
        };
        static readonly float[] Lengths = { .03f, .03f, .035f, .03f, .025f };

        static HandFingers Build(bool mirrored, out Transform[] tips)
        {
            var root = new GameObject("TestHand");
            var fingers = root.AddComponent<HandFingers>();
            fingers.bones = new Transform[1 + HandFingers.Digits * HandFingers.Joints];
            fingers.bones[0] = new GameObject("Wrist").transform;
            fingers.bones[0].SetParent(root.transform, false);
            fingers.restDirections = new Vector3[HandFingers.Digits * HandFingers.Joints];
            fingers.restHeads = new Vector3[HandFingers.Digits];
            tips = new Transform[HandFingers.Digits];
            for (int d = 0; d < HandFingers.Digits; d++)
            {
                var head = Heads[d];
                var direction = Directions[d];
                if (mirrored)
                {
                    head.x = -head.x;
                    direction.x = -direction.x;
                }
                fingers.restHeads[d] = head;
                var parent = fingers.bones[0];
                var parentHead = Vector3.zero;
                for (int j = 0; j < HandFingers.Joints; j++)
                {
                    var bone = new GameObject($"D{d}J{j}").transform;
                    bone.SetParent(parent, false);
                    bone.localPosition = head - parentHead;
                    fingers.bones[1 + d * HandFingers.Joints + j] = bone;
                    fingers.restDirections[d * HandFingers.Joints + j] = direction;
                    parent = bone;
                    parentHead = head;
                    head += direction * Lengths[d];
                }
                var tip = new GameObject($"Tip{d}").transform;
                tip.SetParent(parent, false);
                tip.localPosition = head - parentHead;
                tips[d] = tip;
            }
            return fingers;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FlexMovesEveryFingertipTowardPalm(bool mirrored)
        {
            var fingers = Build(mirrored, out var tips);
            try
            {
                var rest = new Vector3[HandFingers.Digits];
                for (int d = 0; d < rest.Length; d++) rest[d] = tips[d].position;
                var curl = new DigitPose(60f, 60f, 40f);
                fingers.Target = new FingerPose { thumb = curl, index = curl, middle = curl, ring = curl, little = curl };
                fingers.Snap();
                fingers.Apply();
                for (int d = 0; d < tips.Length; d++)
                {
                    Assert.Less(tips[d].position.y, rest[d].y - .005f, $"ngón {d}: đầu ngón phải đi xuống phía lòng bàn tay (−Y)");
                    Assert.Less(tips[d].position.z, rest[d].z - .005f, $"ngón {d}: đầu ngón phải co về cổ tay");
                }
            }
            finally { Object.DestroyImmediate(fingers.gameObject); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SpreadMovesFingersAwayFromMiddle(bool mirrored)
        {
            var fingers = Build(mirrored, out var tips);
            try
            {
                var rest = new Vector3[HandFingers.Digits];
                for (int d = 0; d < rest.Length; d++) rest[d] = tips[d].position;
                var spread = new DigitPose(0f, 0f, 0f, 20f);
                fingers.Target = new FingerPose { thumb = spread, index = spread, middle = default, ring = spread, little = spread };
                fingers.Snap();
                fingers.Apply();
                float sign = mirrored ? -1f : 1f;
                // Tay trái: ngón cái và trỏ ở +x so với ngón giữa, nhẫn và út ở −x.
                foreach (int d in new[] { 0, 1 })
                    Assert.Greater((tips[d].position.x - rest[d].x) * sign, .003f, $"ngón {d} phải dạng ra phía ngón cái");
                foreach (int d in new[] { 3, 4 })
                    Assert.Less((tips[d].position.x - rest[d].x) * sign, -.003f, $"ngón {d} phải dạng ra phía ngón út");
            }
            finally { Object.DestroyImmediate(fingers.gameObject); }
        }
    }
}
