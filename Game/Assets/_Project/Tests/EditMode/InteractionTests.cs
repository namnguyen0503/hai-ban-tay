using System.Collections.Generic;
using HaiBanTay.Interaction;
using HaiBanTay.Player;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    public class InteractionTests
    {
        readonly List<GameObject> _spawned = new List<GameObject>();

        GameObject Spawn(string name, PrimitiveType type = PrimitiveType.Cube)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            _spawned.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        Interactor MakeInteractor()
        {
            var go = new GameObject("Player");
            _spawned.Add(go);
            var interactor = go.AddComponent<Interactor>();
            interactor.motor = go.AddComponent<FirstPersonMotor>();
            var hands = new GameObject("Hands");
            hands.transform.SetParent(go.transform);
            interactor.hands = hands.AddComponent<HandsRig>();
            interactor.holdOneHand = new GameObject("One").transform;
            interactor.holdOneHand.SetParent(go.transform);
            interactor.holdTwoHands = new GameObject("Two").transform;
            interactor.holdTwoHands.SetParent(go.transform);
            return interactor;
        }

        InteractTarget Target(string name, float z)
        {
            var go = Spawn(name);
            go.transform.position = new Vector3(0f, 0f, z);
            go.transform.localScale = new Vector3(0.4f, 0.4f, 0.1f);
            var t = go.AddComponent<InteractTarget>();
            t.promptKey = "prompt.pick";
            t.available = true;
            return t;
        }

        static Ray Forward => new Ray(new Vector3(0f, 0f, -0.5f), Vector3.forward);

        [Test]
        public void ChoosesNearestInteractable()
        {
            var interactor = MakeInteractor();
            var near = Target("Near", 0.8f);
            Target("Far", 1.3f);
            Physics.SyncTransforms();
            interactor.Scan(Forward);
            Assert.AreSame(near, interactor.Current);
            Assert.IsTrue(interactor.IsLookingAt(near));
        }

        [Test]
        public void SolidObjectWithoutPromptBlocksView()
        {
            var interactor = MakeInteractor();
            var wall = Spawn("Wall");
            wall.transform.position = new Vector3(0f, 0f, 0.3f);
            wall.transform.localScale = new Vector3(1f, 1f, 0.1f);
            Target("Behind", 1f);
            Physics.SyncTransforms();
            interactor.Scan(Forward);
            Assert.IsNull(interactor.Current);
        }

        [Test]
        public void TriggerWithoutPromptIsIgnored()
        {
            var interactor = MakeInteractor();
            var zone = Spawn("Zone");
            zone.transform.position = new Vector3(0f, 0f, 0.3f);
            zone.GetComponent<Collider>().isTrigger = true;
            var target = Target("Target", 1f);
            Physics.SyncTransforms();
            interactor.Scan(Forward);
            Assert.AreSame(target, interactor.Current);
        }

        [Test]
        public void OutOfRangeIsNotSelected()
        {
            var interactor = MakeInteractor();
            Target("TooFar", 2.4f);
            Physics.SyncTransforms();
            interactor.Scan(Forward);
            Assert.IsNull(interactor.Current);
        }

        Holdable MakeHoldable(string kind, bool twoHanded)
        {
            var go = Spawn("Holdable_" + kind);
            go.AddComponent<Rigidbody>();
            var h = go.AddComponent<Holdable>();
            h.kind = kind;
            h.twoHanded = twoHanded;
            return h;
        }

        [Test]
        public void PickTwoHandedSlowsPlayerAndUsesCarryPose()
        {
            var interactor = MakeInteractor();
            var sack = MakeHoldable("sack", twoHanded: true);
            Assert.AreEqual("prompt.pick_two_hands", sack.GetPromptKey(interactor));
            sack.Interact(interactor);
            Assert.AreSame(sack, interactor.Holding);
            Assert.IsTrue(sack.IsHeld);
            Assert.IsTrue(interactor.motor.Carrying);
            Assert.AreEqual(HandPose.Carry, interactor.hands.Pose);
            Assert.IsTrue(sack.Body.isKinematic);
            Assert.IsFalse(sack.GetComponent<Collider>().enabled);
        }

        [Test]
        public void CannotPickSecondItemWhileHolding()
        {
            var interactor = MakeInteractor();
            var a = MakeHoldable("crate", false);
            var b = MakeHoldable("crate", false);
            a.Interact(interactor);
            Assert.IsFalse(b.CanInteract(interactor));
            Assert.IsNull(b.GetPromptKey(interactor));
        }

        [Test]
        public void DropZoneAcceptsOnlyMatchingKind()
        {
            var interactor = MakeInteractor();
            var zoneGo = Spawn("Zone");
            var zone = zoneGo.AddComponent<DropZone>();
            zone.acceptKind = "sack";
            Holdable placed = null;
            zone.Placed += h => placed = h;

            var crate = MakeHoldable("crate", false);
            crate.Interact(interactor);
            Assert.IsFalse(zone.CanInteract(interactor));
            Assert.IsNull(zone.GetPromptKey(interactor));
            interactor.DropInFrontForTests();

            var sack = MakeHoldable("sack", true);
            sack.Interact(interactor);
            Assert.AreEqual("prompt.drop", zone.GetPromptKey(interactor));
            zone.Interact(interactor);
            Assert.AreSame(sack, placed);
            Assert.AreSame(sack, zone.Contents);
            Assert.IsNull(interactor.Holding);
            Assert.IsFalse(interactor.motor.Carrying);
            Assert.IsTrue(sack.Locked);
            Assert.IsFalse(sack.CanInteract(interactor));
        }

        [Test]
        public void ReturnToStartDetectsOutsideArea()
        {
            var go = Spawn("Thing");
            var r = go.AddComponent<ReturnToStart>();
            r.useArea = true;
            r.area = new Bounds(Vector3.zero, new Vector3(4f, 4f, 4f));
            r.killY = -3f;
            Assert.IsFalse(r.IsOutside(Vector3.zero));
            Assert.IsTrue(r.IsOutside(new Vector3(5f, 0f, 0f)));
            Assert.IsTrue(r.IsOutside(new Vector3(0f, -4f, 0f)));
        }
    }
}
