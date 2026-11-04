using System.Collections.Generic;
using System.Linq;
using HaiBanTay.Editor.SceneBuilders;
using HaiBanTay.Interaction;
using HaiBanTay.Narrative;
using HaiBanTay.Npc;
using HaiBanTay.Ship;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// 01_NhaRong dựng trên model bến thật (M10c): đám đông ~50 người theo tầng, lối bắt buộc không bị người đứng yên chắn, biên
    /// vùng đi bằng vật thật (không có tường vô hình), ba bao khoai và chỗ xếp ở hầm. Mở cảnh đã dựng (chạy rebuild_scenes trước).
    /// </summary>
    public class NhaRongSceneTests
    {
        [OneTimeSetUp]
        public void Open()
        {
            EditorSceneManager.OpenScene(BuilderUtils.ScenePath(NhaRongSceneBuilder.SceneName), OpenSceneMode.Single);
        }

        [OneTimeTearDown]
        public void Close()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static NpcAgent[] Npcs() => Object.FindObjectsByType<NpcAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        [Test]
        public void CrowdHasAboutFiftyPeopleAndLaptopTierKeepsFewer()
        {
            var all = Npcs();
            var routines = all.Select(n => n.GetComponent<CrowdRoutine>()).Where(r => r != null).ToArray();
            Assert.That(routines.Length, Is.InRange(45, 60), "khoảng 40–60 người ở tầng PC");
            int optional = routines.Count(r => r.optional);
            Assert.GreaterOrEqual(optional, 8, "đủ người đứng thừa để tầng Laptop bớt đáng kể");
            // CrowdLod giữ laptopOptionalKeep (0,5) số người đứng thừa ở tầng Laptop.
            float laptop = routines.Length - optional * (1f - 0.5f);
            Assert.That(laptop, Is.InRange(35f, routines.Length * 0.9f), "tầng Laptop ít người hơn nhưng vẫn là một bến đông");
            foreach (var crowd in new[] { "Crowd_Quay", "Crowd_Deck" })
            {
                var lod = GameObject.Find(crowd).GetComponent<CrowdLod>();
                Assert.IsNotNull(lod, crowd + " cần CrowdLod");
                Assert.LessOrEqual(lod.maxAnimatedLaptop, lod.maxAnimatedPc);
            }
        }

        [Test]
        public void CrowdUsesTheNewCastNotTheKitchenRoles()
        {
            // Tên NPC: Npc_{vai}_{seed} (Mai, Purser, Chef đặt tên riêng: Npc_Mai, Npc_Purser, Npc_Chef).
            var roles = Npcs().Select(n => n.transform.name).Where(n => n.StartsWith("Npc_"))
                .Select(n => n.Substring(4)).Select(n => n.LastIndexOf('_') > 0 && char.IsDigit(n[n.Length - 1]) ? n.Substring(0, n.LastIndexOf('_')) : n).ToArray();
            CollectionAssert.IsSubsetOf(new[] { "Mai", "Purser" }, roles, "ông Mai và người soát vé có mặt");
            Assert.IsFalse(roles.Contains("Jacques"), "Jacques không ở trên bến");
            Assert.IsTrue(roles.Any(r => r.StartsWith("PorterVN")), "phu bốc hàng");
            Assert.IsTrue(roles.Any(r => r.StartsWith("PassengerFR")), "hành khách");
            Assert.IsTrue(roles.Contains("SailorFR"), "thủy thủ");
            Assert.IsTrue(roles.Contains("VendorVN_F"), "người bán hàng");
        }

        static float DistanceToPolyline(Vector3 p, Vector3[] line)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                var a = new Vector2(line[i].x, line[i].z);
                var b = new Vector2(line[i + 1].x, line[i + 1].z);
                var q = new Vector2(p.x, p.z);
                var ab = b - a;
                float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, (q - (a + ab * t)).magnitude);
            }
            return best;
        }

        [Test]
        public void StaticPeopleKeepTheMandatoryRoutesClear()
        {
            var staticModes = new[] { CrowdMode.Stand, CrowdMode.Lean, CrowdMode.Group, CrowdMode.Sit, CrowdMode.Pose };
            var crowded = new List<string>();
            foreach (var r in Npcs().Select(n => n.GetComponent<CrowdRoutine>()).Where(r => r != null && staticModes.Contains(r.mode)))
            {
                var p = r.transform.position;
                foreach (var route in new[] { NhaRongCrowd.MandatoryRoute, NhaRongCrowd.GalleyDoorRoute })
                    if (DistanceToPolyline(p, route) < NhaRongCrowd.ClearRadius)
                        crowded.Add($"{r.name} @ {p:F1}");
            }
            Assert.IsEmpty(crowded, "người đứng yên chắn lối bắt buộc: " + string.Join("; ", crowded));
        }

        [Test]
        public void QueueMembersStandOnTheQuayAndWestOfThePlayersLane()
        {
            var layout = EnvLayout.Load("NhaRong");
            var quay = layout.WalkBounds.Single(b => b.Name == "quay");
            foreach (var r in GameObject.Find("Crowd_Quay").GetComponentsInChildren<CrowdRoutine>(true))
            {
                var p = r.transform.position;
                Assert.IsTrue(quay.Contains(p), $"{r.name} nằm ngoài kè: {p}");
                Assert.AreEqual(-4.1f, p.y, 0.05f, $"{r.name} đứng trên mặt kè");
            }
            var director = Object.FindAnyObjectByType<NhaRongDirector>();
            Assert.IsNotNull(director.mai);
            Assert.IsNotNull(director.purser);
            Assert.IsNotNull(director.chef);
        }

        [Test]
        public void BoundaryIsPhysicalPropsNotInvisibleWalls()
        {
            var backdrop = GameObject.Find("Backdrop_NhaRong");
            Assert.IsNotNull(backdrop);
            var invisible = new List<string>();
            foreach (var box in backdrop.GetComponentsInChildren<BoxCollider>(true))
            {
                if (box.isTrigger || box.GetComponentInParent<Holdable>() != null || box.GetComponentInParent<NpcAgent>() != null)
                    continue;
                // Đồ trang trí đặt trong ArtSlot (thùng hàng): khối va chạm của vật chưa có art, không phải tường.
                if (box.GetComponentsInParent<Transform>().Any(t => t.name.StartsWith("ArtSlot_")))
                    continue;
                if (box.GetComponentInChildren<Renderer>(true) == null)
                    invisible.Add(box.name);
            }
            Assert.IsEmpty(invisible, "không dùng tường vô hình: " + string.Join(", ", invisible));
            var env = backdrop.GetComponentsInChildren<MeshCollider>(true).Where(c => c.name.StartsWith("COL_")).ToArray();
            Assert.Greater(env.Length, 90, "biên bằng hàng rào, kho, xe của model (COL_*)");
            Assert.IsTrue(env.Any(c => c.name.Contains("QuayRail")) && env.Any(c => c.name.Contains("Back")), "hàng rào mép sông và phía sau");
        }

        [Test]
        public void ThreeSacksAndAStackInTheFoodHold()
        {
            var d = Object.FindAnyObjectByType<NhaRongDirector>();
            Assert.AreEqual(3, d.sacks.Length);
            Assert.GreaterOrEqual(d.sacks.Length, d.sackTarget, "đủ bao cho mục tiêu");
            Assert.That(d.sackTarget, Is.InRange(2, 3));
            Assert.GreaterOrEqual(d.stack.Capacity, d.sackTarget);
            var layout = Object.FindAnyObjectByType<ShipLayout>();
            foreach (var a in d.stack.anchors)
                Assert.IsTrue(layout.holdZone.GetComponent<Collider>().bounds.Contains(a.position), "chỗ xếp bao nằm trong hầm thực phẩm");
            foreach (var s in d.sacks)
            {
                Assert.AreEqual("sack", s.kind);
                Assert.AreEqual(-4.1f + 0.3f, s.transform.position.y, 0.05f, "bao đặt trên mặt kè");
                Assert.IsNotNull(s.GetComponent<ReturnToStart>());
            }
        }
    }
}
