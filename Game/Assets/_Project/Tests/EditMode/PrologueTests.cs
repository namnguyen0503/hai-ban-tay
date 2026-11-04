using System.IO;
using System.Linq;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Editor.SceneBuilders;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>Prologue 00_Prologue: danh sách cảnh, điểm neo của phố Sài Gòn và nội dung (thoại S16, thẻ, trang sổ).</summary>
    public class PrologueTests
    {
        static ContentDatabase LoadFromDisk()
        {
            string dir = Path.Combine(Application.dataPath, "_Project/Resources/Content");
            return ContentDatabase.Load(name =>
            {
                var path = Path.Combine(dir, name + ".vi.json");
                return File.Exists(path) ? File.ReadAllText(path) : null;
            });
        }

        [Test]
        public void PrologueIsItsOwnEntryAndGameSceneIndicesAreUnchanged()
        {
            Assert.AreEqual("00_Prologue", SceneFlow.PrologueScene);
            CollectionAssert.AreEqual(new[] { "01_NhaRong", "02_Galley", "03_Marseille" }, SceneFlow.GameScenes);
            CollectionAssert.DoesNotContain(SceneFlow.GameScenes, SceneFlow.PrologueScene);
            Assert.AreEqual(SceneFlow.PrologueScene, PrologueSceneBuilder.SceneName);
        }

        [Test]
        public void BuildScenesPutPrologueBetweenMenuAndNhaRong()
        {
            CollectionAssert.AreEqual(
                new[] { "00_Boot", "00_Menu", "00_Prologue", "01_NhaRong", "02_Galley", "03_Marseille" }, SceneBuildAll.BuildScenes);
        }

        [Test]
        public void StreetAnchorsAreCompleteAndConsistent()
        {
            var a = SaigonAnchors.Default();
            Assert.GreaterOrEqual(a.lePath.Length, 2);
            Assert.AreEqual(a.leStart, a.lePath[0]);
            Assert.AreEqual(a.conversationLe, a.lePath[a.lePath.Length - 1], "anh Lê phải dừng đúng chỗ nói chuyện");
            float gap = Vector3.Distance(a.conversationBa, a.conversationLe);
            Assert.That(gap, Is.InRange(1f, 2.5f), "hai người đứng đủ gần để nói chuyện");
            Assert.Less(Vector3.Distance(a.playerSpawn, a.leStart), 2.5f, "anh Lê xuất phát cạnh người chơi");
            Assert.Greater(a.conversationBa.x - a.playerSpawn.x, 15f, "người chơi phải tự đi một đoạn phố");
            Assert.That(a.lamps.Length, Is.InRange(4, 8), "đèn điện của model: 4 cột mỗi bên phố");
            Assert.LessOrEqual(a.lamps.Count(l => l.realtime), 2, "tối đa 2 đèn realtime không bóng");
            Assert.That(a.cafeGuests.Length, Is.InRange(2, 3));
            foreach (var p in a.lePath.Concat(new[] { a.playerSpawn, a.conversationBa }))
                Assert.That(p.x, Is.InRange(a.minX, a.maxX), "điểm neo nằm trong đoạn phố đã chặn hai đầu: " + p);
            Assert.GreaterOrEqual(a.northWalk.Length + a.southWalk.Length + a.roadWalk.Length, 6);
        }

        [Test]
        public void AnchorsMatchTheSaigonStreetModelLayout()
        {
            var a = SaigonAnchors.Default();
            var layout = EnvLayout.Load("SaigonStreet");
            Assert.AreEqual(layout.PlayerSpawn.x, a.playerSpawn.x, 1e-3f);
            Assert.AreEqual(layout.PlayerSpawn.z, a.playerSpawn.z, 1e-3f);
            Assert.AreEqual(layout.PlayerYaw, a.playerYaw, 1e-3f);
            Assert.AreEqual(layout.Anchor("conversation_ba"), a.conversationBa);
            Assert.AreEqual(layout.Anchor("conversation_le"), a.conversationLe);
            Assert.AreEqual(layout.Anchor("cafe_seat"), a.cafeSeat);
            Assert.AreEqual(layout.Anchor("electric_light_bulb"), a.lamps[2].bulb, "bóng đèn trước quán trùng anchor của model");
            // Đèn khớp các cột ElectricLamp của model.
            foreach (var lamp in a.lamps)
                Assert.IsTrue(layout.Blockers.Any(b => b.Name.StartsWith("ElectricLamp")
                    && Mathf.Abs(b.Center.x - lamp.bulb.x) < 0.1f && Mathf.Abs(b.Center.z - lamp.bulb.z) < 0.1f), "không có cột đèn ở " + lamp.bulb);
            // Khách ngồi đúng ghế của model.
            foreach (var guest in a.cafeGuests)
                Assert.IsTrue(layout.Blockers.Any(b => b.Name.StartsWith("CafeChair")
                    && Vector2.Distance(new Vector2(b.Center.x, b.Center.z), new Vector2(guest.pos.x, guest.pos.z)) < 0.1f), "khách không ngồi ghế: " + guest.pos);
        }

        [Test]
        public void StreetCrowdWalksOnTheSidewalksAndAvoidsFurniture()
        {
            var a = SaigonAnchors.Default();
            var layout = EnvLayout.Load("SaigonStreet");
            var points = a.northWalk.Concat(a.southWalk).Concat(a.roadWalk).Concat(a.talkGroups).Concat(a.standers)
                .Concat(a.lePath).Concat(new[] { a.playerSpawn, a.conversationBa, a.conversationLe }).ToArray();
            foreach (var p in points)
            {
                Assert.IsTrue(layout.WalkBounds.Any(b => b.Contains(p) && Mathf.Abs(p.y - b.Bounds.center.y) <= 0.05f), "ngoài mặt đi được: " + p);
                foreach (var b in layout.Blockers.Where(b => b.Center.y - b.Size.y * 0.5f < 1.5f && b.Center.y + b.Size.y * 0.5f > 0.3f))
                {
                    // Điểm trong hệ của khối va chạm (xoay yaw quanh trục Y).
                    var local = Quaternion.Euler(0f, -b.Yaw, 0f) * (p - b.Center);
                    bool inside = Mathf.Abs(local.x) < b.Size.x * 0.5f + 0.3f && Mathf.Abs(local.z) < b.Size.z * 0.5f + 0.3f;
                    Assert.IsFalse(inside, $"{p} nằm trong/sát vật cản {b.Name}");
                }
            }
        }

        [Test]
        public void StreetModelPrefabHasTheCafeGlassPanelsWeReplace()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(EnvLayout.PrefabRoot + "Env_SaigonStreet.prefab");
            Assert.IsNotNull(prefab);
            int panels = prefab.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.StartsWith("Shop_"))
                .Count(r => r.sharedMaterials.Any(m => m != null && m.name == "Gen_Env_CafeGlass"));
            Assert.AreEqual(1, panels, "một mặt tiền quán cà phê có CafeGlass");
        }

        [Test]
        public void ConversationLinesAreVerbatimS16AndSpeakersExist()
        {
            var db = LoadFromDisk();
            Assert.AreEqual("Anh Lê", db.Subtitles.Speakers["le"]);
            Assert.AreEqual("Anh Ba", db.Subtitles.Speakers["ba"]);
            var expected = new (string id, string speaker, string text)[]
            {
                ("pro_ba_patriot", "ba", "Anh Lê, anh có yêu nước không?"),
                ("pro_le_patriot", "le", "Tất nhiên là có chứ!"),
                ("pro_ba_secret", "ba", "Anh có thể giữ bí mật không?"),
                ("pro_le_secret", "le", "Có."),
                ("pro_le_money", "le", "Nhưng bạn ơi, chúng ta lấy đâu ra tiền mà đi?"),
                ("pro_ba_hands", "ba", "Đây, tiền đây."),
                ("pro_ba_work", "ba", "Chúng ta sẽ làm việc. Chúng ta sẽ làm bất cứ việc gì để sống và để đi."),
                ("pro_ba_work2", "ba", "Thế thì anh cùng đi với tôi chứ?"),
                ("pro_ba_abroad", "ba", "Tôi muốn đi ra ngoài, xem nước Pháp và các nước khác."),
                ("pro_ba_abroad2", "ba", "Sau khi xem xét họ làm như thế nào, tôi sẽ trở về giúp đồng bào chúng ta."),
                ("pro_ba_invite", "ba", "Nhưng nếu đi một mình, thật ra cũng có điều mạo hiểm, ví như khi đau ốm..."),
                ("pro_ba_invite2", "ba", "Anh muốn đi với tôi không?"),
            };
            foreach (var e in expected)
            {
                var line = db.GetLine(e.id);
                Assert.AreEqual(e.speaker, line.Speaker, e.id);
                Assert.AreEqual(e.text, line.Vi, e.id);
            }
            foreach (var id in new[] { "pro_ba_abroad", "pro_ba_invite", "pro_le_agree" })
                Assert.IsNotNull(db.GetLine(id), id);
        }

        [Test]
        public void JobAndMarseilleQuotesKeepTheS16Text()
        {
            var db = LoadFromDisk();
            foreach (var pair in new[] {
                ("job_staff_whisper", "Một người như thế có thể làm được công việc gì trên tàu?"),
                ("job_mai_lead", "Đi theo tôi, tôi sẽ dẫn anh đến gặp chủ tàu."),
                ("job_mai_lead2", "Có lẽ ông ta sẽ có việc cho anh làm."),
                ("job_captain_work", "Anh có thể làm việc gì?"),
                ("job_ba_any", "Tôi có thể làm bất cứ việc gì!"),
                ("job_captain_accept", "Được, ta sẽ lấy anh làm phụ bếp. Sáng mai anh đến đây nhận việc."),
                ("mrs_ba_first", "Lần đầu tiên, tôi mới thấy cái này...") })
                Assert.AreEqual(pair.Item2, db.GetLine(pair.Item1).Vi, pair.Item1);
            Assert.AreEqual("Xong việc rồi. Anh mặc tạm bộ áo quần của tôi. Rồi ta ra tiệm cà phê ở đường Canơbia.",
                db.GetLine("mrs_mai_clothes").Vi + " " + db.GetLine("mrs_mai_clothes2").Vi);
            Assert.AreEqual("Lần đầu anh vào tiệm cà phê phải không? Người ta gọi anh là ông đấy.",
                db.GetLine("mrs_mai_monsieur").Vi + " " + db.GetLine("mrs_mai_monsieur2").Vi);
            Assert.AreEqual("— Người Pháp ở Pháp tốt, lễ phép hơn người Pháp ở Đông Dương.", db.GetString("end.mai.1"));
            Assert.AreEqual("Từ đấy, tôi không được tin tức gì của anh Ba nữa...", db.GetString("end.mai.3"));
            Assert.AreEqual("Không bao giờ tôi đoán rằng người bạn nhỏ của tôi, người phụ bếp, anh Ba ngây thơ, siêng năng và ngoan ngoãn ấy, lại trở thành Chủ tịch Chính phủ ta, người xây dựng nên nước Cộng hòa chúng ta.",
                string.Join(" ", new[] { db.GetString("end.mai.4"), db.GetString("end.mai.5"), db.GetString("end.mai.6") }));
            StringAssert.Contains("S16", db.GetString("end.mai.source"));
        }

        [Test]
        public void PrologueHasCardsHintAndNotebookPage()
        {
            var db = LoadFromDisk();
            foreach (var key in new[] { "scene.00.name", "card.prologue.place", "card.prologue.days", "card.prologue.noon", "hint.prologue_follow" })
                Assert.IsFalse(string.IsNullOrEmpty(db.GetString(key)), key);
            Assert.AreEqual("Sài Gòn, 1911", db.GetString("card.prologue.place"));
            Assert.AreEqual("Trưa 2/6/1911 · Bến Nhà Rồng", db.GetString("card.prologue.noon"));
            Assert.IsTrue(db.SourceCards.Cards.Any(c => c.Id == "sc_hands_story"));
            var page = db.Notebook.Pages.Single(p => p.Id == "p_two_hands");
            Assert.AreEqual(EventIds.S1HandsRaised, page.UnlockEvent);
            CollectionAssert.Contains(page.SourceCardIds, "sc_hands_story");
            Assert.AreEqual("Ít hôm sau", db.GetString("card.prologue.later"));
            Assert.AreEqual("Trưa 5/6/1911 · Tàu sắp nhổ neo", db.GetString("card.job.departure"));
            foreach (var id in new[] { "p_saigon_new", "p_other_path", "p_job", "p_mrs_context", "p_mrs_clothes", "p_mai_end" })
                Assert.IsTrue(db.Notebook.Pages.Any(p => p.Id == id), id);
            Assert.IsEmpty(db.FindBrokenReferences());
        }
    }
}
