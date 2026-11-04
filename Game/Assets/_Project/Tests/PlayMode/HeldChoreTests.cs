using System.Collections;
using HaiBanTay.Interaction;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    public class HeldChoreTests
    {
        [UnityTest]
        public IEnumerator SackAndAshBucketStayWithinFifteenCentimetresAtFastTurns()
        {
            foreach (string kind in new[] { "sack", "ash_bucket" })
            {
                var camera = new GameObject("CameraPoint");
                var hand = new GameObject("HandPoint").transform;
                hand.SetParent(camera.transform, false);
                hand.localPosition = new Vector3(0.2f, -0.3f, 0.65f);
                var item = new GameObject(kind);
                var rb = item.AddComponent<Rigidbody>();
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                var held = item.AddComponent<Holdable>();
                held.kind = kind; held.twoHanded = kind == "sack";
                held.AttachTo(hand);
                float angle = 0f;
                while (angle < 360f)
                {
                    angle += 180f * Time.deltaTime;
                    camera.transform.rotation = Quaternion.Euler(0f, angle, 0f);
                    yield return null;
                    Assert.LessOrEqual(Vector3.Distance(hand.position, item.transform.position), 0.15f, kind);
                    Assert.LessOrEqual(Quaternion.Angle(hand.rotation, item.transform.rotation), 1f, kind);
                }
                Assert.AreEqual(RigidbodyInterpolation.None, rb.interpolation);
                held.Detach(Vector3.zero, Quaternion.identity, false);
                Assert.AreEqual(RigidbodyInterpolation.Interpolate, rb.interpolation);
                Object.Destroy(item); Object.Destroy(camera);
                yield return null;
            }
        }
    }
}
