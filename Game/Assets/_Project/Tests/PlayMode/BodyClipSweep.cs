using System.Collections;
using System.IO;
using System.Linq;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using HaiBanTay.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>
    /// Quét xuyên thân NPC từng cảnh (yêu cầu 15/10): mọi NPC chạy Animator (kể cả ở xa người chơi), giờ game 1/60 s mỗi khung,
    /// BodyClipProbe đo mỗi khung và chụp cận chỗ lún. Báo cáo Logs/qa/body_clip_&lt;cảnh&gt;.md, ảnh Logs/shots/bodyclip/&lt;cảnh&gt;/.
    /// Không đánh đỏ theo số lỗi (đây là bản đo); chỉ đỏ khi bộ dò không đo được NPC nào.
    /// </summary>
    [Category("Screenshots"), Explicit]
    public sealed class BodyClipSweep
    {
        [SetUp] public void SetUp() => Time.captureDeltaTime = 1f / 60f;

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            foreach (var p in Object.FindObjectsByType<BodyClipProbe>(FindObjectsSortMode.None)) Object.Destroy(p.gameObject);
            PlayTestUtil.ExitSim();
        }

        static BodyClipProbe Probe()
        {
            var go = new GameObject("BodyClipProbe");
            Object.DontDestroyOnLoad(go);
            var probe = go.AddComponent<BodyClipProbe>();
            probe.shots = true;
            probe.animateAll = true;
            return probe;
        }

        static void Clear(string scene)
        {
            var folder = BodyClipProbe.ShotFolder(scene);
            if (Directory.Exists(folder))
                foreach (var old in Directory.GetFiles(folder)) File.Delete(old);
        }

        static IEnumerator Run(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
        }

        static void Write(BodyClipProbe probe, string scene, string notes)
        {
            var findings = probe.Track.Findings.Values.ToList();
            string text = BodyClipProbe.Report(findings, $"Xuyên thân NPC — {scene}", $"Mã bản dựng `{BuildInfo.Id}`. {probe.Track.Samples} khung đo. {notes}");
            File.WriteAllText(Path.Combine(BodyClipProbe.QaFolder, $"body_clip_{scene}.md"), text, new System.Text.UTF8Encoding(false));
            Debug.Log($"BODY_CLIP {scene}: MAJOR {findings.Count(f => f.Severity == "MAJOR")}, MINOR {findings.Count(f => f.Severity == "MINOR")}, {probe.Track.Samples} khung\n"
                      + string.Join("\n", findings.Where(f => f.Severity != "INFO").OrderByDescending(f => f.Severity == "MAJOR").ThenByDescending(f => f.maxDepth)
                          .Take(60).Select(BodyClipTrack.Line)));
            Assert.Greater(probe.Track.Samples, 100, "bộ dò không chạy");
        }

        [UnityTest, Timeout(1200000)]
        public IEnumerator Prologue()
        {
            Clear(SceneFlow.PrologueScene);
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.PrologueScene);
            var probe = Probe();
            yield return Run(60f);
            Write(probe, SceneFlow.PrologueScene, "60 s từ đầu cảnh mở đầu (phố Sài Gòn).");
        }

        [UnityTest, Timeout(1200000)]
        public IEnumerator NhaRong()
        {
            Clear(SceneFlow.GameScenes[0]);
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[0]);
            var probe = Probe();
            yield return Run(75f);
            Write(probe, SceneFlow.GameScenes[0], "75 s từ đầu cảnh Nhà Rồng: phu vác bao, khách xách va li, hàng chờ.");
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator Galley()
        {
            Clear(SceneFlow.GameScenes[1]);
            yield return ChoreTestUtil.Galley();
            GameRoot.Instance.UI.SetFade(0f);
            var people = Object.FindAnyObjectByType<VoyagePeople>();
            foreach (var g in new[] { people.cooks, people.cardPlayers, people.seasick, people.firstClass, people.letterFriend, people.crewMeal })
                if (g != null) g.SetActive(true);
            var probe = Probe();
            var meals = Object.FindAnyObjectByType<MealPots>();
            var cook = Object.FindAnyObjectByType<CookRoutine>();
            var helper = Object.FindAnyObjectByType<HelperRoutine>();
            // Nấu: nhấc nắp, khuấy, phụ bếp xúc than nạp lò; thùng khoai đầy thì quản bếp bê đổ vào nồi; rồi dọn bữa.
            meals.BeginMeal(new PhaseData { Id = "bodyclip", Dirty = 2 });
            yield return Run(25f);
            var sacks = Object.FindAnyObjectByType<SackStation>();
            for (int i = 0; i < 4 && sacks.InTub < sacks.potatoesPerSack; i++) sacks.SimulateWork();
            int tubs = cook.TubsCarried;
            for (float t = 0f; t < 45f && cook.TubsCarried == tubs; t += Time.deltaTime) yield return null;
            yield return Run(5f);
            bool served = false;
            meals.StartCoroutine(Track(meals.Serve(), () => served = true));
            for (float t = 0f; t < 120f && !served; t += Time.deltaTime) yield return null;
            Assert.IsTrue(served, "dọn bữa chưa xong");
            // Nồi bão (LoosePot) cũng là nồi bữa: chờ phụ bếp đặt hết nồi rồi mới thả nồi trượt, không thì nồi đang bê rơi khỏi tay
            // (bản quét 15/10 đầu tiên: tay rời nồi 101 m là do bài quét thả nồi lúc phụ bếp còn bê).
            for (float t = 0f; t < 20f && helper.agent.Carrying != null; t += Time.deltaTime) yield return null;
            // Bão: Jacques giữ nồi lỏng, đỡ nồi cao.
            var cast = Object.FindAnyObjectByType<DayCast>();
            foreach (var p in cast.stormPots) p.Secure(false);
            cast.jacques.gameObject.SetActive(true);
            cast.jacques.Place(cast.jacquesPots.position, cast.jacquesPots.eulerAngles.y);
            cast.StartCoroutine(cast.HoldStormPots());
            float start = Time.time;
            for (float t = 0f; t < 14f; t += Time.deltaTime)
            {
                if (Time.time - start > 4f && cast.HeldPot != null && !cast.HeldPot.Secured) cast.HeldPot.Secure(true);
                yield return null;
            }
            foreach (var p in cast.stormPots) p.Secure(true);
            var tall = Object.FindAnyObjectByType<TallPotStation>().potRoot;
            tall.gameObject.SetActive(true);
            cast.StartCoroutine(cast.HoldTallPot());
            yield return Run(8f);
            Write(probe, SceneFlow.GameScenes[1], "Bếp: nấu (nắp, khuấy, xúc than), bê thùng khoai đổ nồi, dọn bữa (bê nồi), Jacques giữ nồi lúc bão; " +
                                                  "các nhóm người trên tàu bật cùng lúc (đầu bếp boong, đánh bài, say sóng, khoang hạng nhất, bạn viết thư, bữa thủy thủ).");
            Assert.IsNotNull(helper);
        }

        [UnityTest, Timeout(1200000)]
        public IEnumerator Marseille()
        {
            Clear(SceneFlow.GameScenes[2]);
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[2]);
            var probe = Probe();
            yield return Run(90f);
            Write(probe, SceneFlow.GameScenes[2], "90 s từ đầu cảnh Marseille: phu bến, hàng lĩnh lương, bữa cuối.");
        }

        static IEnumerator Track(IEnumerator inner, System.Action finished)
        {
            yield return inner;
            finished();
        }
    }

    /// <summary>
    /// Tự kiểm bộ dò xuyên thân (W1: oracle phải tự chứng minh): hộp đặt trong đùi quản bếp phải bị bắt (lún ≥ 5 cm), cùng hộp đặt
    /// trước ngực cách thân không bị bắt; tay trái kéo IK vào giữa thân phải bị bắt là tay xuyên thân (bàn tay hay cẳng tay), đưa ra trước
    /// ngực thì không.
    /// </summary>
    public sealed class BodyClipSelfTests
    {
        [TearDown]
        public void TearDown()
        {
            foreach (var p in Object.FindObjectsByType<BodyClipProbe>(FindObjectsSortMode.None)) Object.Destroy(p.gameObject);
            PlayTestUtil.ExitSim();
        }

        static Mesh Box(float size)
        {
            float h = size * 0.5f;
            var mesh = new Mesh { name = "BodyClipFixtureBox" };
            mesh.SetVertices(new[]
            {
                new Vector3(-h, -h, -h), new Vector3(h, -h, -h), new Vector3(h, h, -h), new Vector3(-h, h, -h),
                new Vector3(-h, -h, h), new Vector3(h, -h, h), new Vector3(h, h, h), new Vector3(-h, h, h),
            });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 3, 7, 6, 3, 6, 2, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5 }, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        static IEnumerator Measure(BodyClipProbe probe, int frames)
        {
            probe.Track.Findings.Clear();
            for (int i = 0; i < frames; i++) yield return null;
        }

        static float Depth(BodyClipProbe probe, BodyClipDetector.Kind kind, System.Func<BodyClipTrack.Finding, bool> match) =>
            probe.Track.Findings.Values.Where(f => f.kind == kind && match(f)).Select(f => f.maxDepth).DefaultIfEmpty(0f).Max();

        [UnityTest, Timeout(300000)]
        public IEnumerator FlagsABoxInTheThighAndAHandInTheChestButNotTheSameApart()
        {
            yield return ChoreTestUtil.Galley();
            var cook = Object.FindAnyObjectByType<CookRoutine>();
            cook.enabled = false;
            var agent = cook.agent;
            agent.ClearWalkState();
            var hands = cook.GetComponent<NpcHandWork>();
            hands.Abort();
            agent.Play("Idle", 0f);
            var animator = agent.GetComponentInChildren<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            agent.keepAnimated = true;
            agent.SetAnimated(true);
            yield return new WaitForSeconds(0.6f);

            var probe = new GameObject("BodyClipProbe").AddComponent<BodyClipProbe>().Watch(agent);
            var box = new GameObject("BodyClipFixture");
            box.AddComponent<MeshFilter>().sharedMesh = Box(0.16f);
            box.AddComponent<MeshRenderer>();
            agent.PickUp(box.transform);
            var thigh = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            var knee = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            box.transform.position = Vector3.Lerp(thigh.position, knee.position, 0.5f);
            yield return Measure(probe, 30);
            float inThigh = Depth(probe, BodyClipDetector.Kind.ItemInBody, f => f.b == "BodyClipFixture");
            string all = string.Join("\n", probe.Track.Findings.Values.Select(BodyClipTrack.Line));
            Assert.Greater(inThigh, 0.05f, "hộp 16 cm giữa đùi phải bị bắt là đồ xuyên thân:\n" + all);

            var chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            box.transform.position = chest.position + agent.transform.forward * 0.45f;
            yield return Measure(probe, 30);
            all = string.Join("\n", probe.Track.Findings.Values.Select(BodyClipTrack.Line));
            Assert.AreEqual(0f, Depth(probe, BodyClipDetector.Kind.ItemInBody, f => f.b == "BodyClipFixture"), "hộp cách ngực không được báo:\n" + all);
            Assert.AreEqual(0f, Depth(probe, BodyClipDetector.Kind.HandInItem, f => f.b == "BodyClipFixture"), "hộp cách ngực không được báo:\n" + all);
            agent.PutDown(Vector3.down * 50f, Quaternion.identity, null);
            Object.Destroy(box);

            var target = new GameObject("BodyClipHandTarget").transform;
            target.SetParent(agent.transform, false);
            var spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            target.SetPositionAndRotation(Vector3.Lerp(spine.position, chest.position, 0.6f), agent.transform.rotation * Quaternion.Euler(0f, 90f, 0f));
            hands.arms.leftTarget = target;
            hands.arms.leftWeight = 1f;
            yield return Measure(probe, 30);
            all = string.Join("\n", probe.Track.Findings.Values.Select(BodyClipTrack.Line));
            float handIn = Depth(probe, BodyClipDetector.Kind.LimbInBody, f => f.a == "bàn tay trái" || f.a == "cẳng tay trái");
            Debug.Log("BODY_CLIP_SELF tay trong ngực:\n" + all);
            Assert.Greater(handIn, BodyClipDetector.Limits(BodyClipDetector.Kind.LimbInBody).major, "tay trái kéo vào giữa thân phải bị bắt (bàn tay hay cẳng tay):\n" + all);

            target.position = chest.position + agent.transform.forward * 0.45f - agent.transform.right * 0.1f;
            yield return Measure(probe, 30);
            all = string.Join("\n", probe.Track.Findings.Values.Select(BodyClipTrack.Line));
            float apart = Mathf.Max(Depth(probe, BodyClipDetector.Kind.LimbInBody, f => f.a == "bàn tay trái"),
                Depth(probe, BodyClipDetector.Kind.LimbInBody, f => f.a == "cẳng tay trái"));
            Debug.Log("BODY_CLIP_SELF tay trước ngực:\n" + all);
            Assert.Less(apart, BodyClipDetector.Limits(BodyClipDetector.Kind.LimbInBody).major, "tay trái đưa ra trước ngực không được báo xuyên thân:\n" + all);
            hands.arms.leftWeight = 0f;
            hands.arms.leftTarget = null;
            Debug.Log("BODY_CLIP_SELF hộp trong đùi " + inThigh * 100f + " cm, tay trong ngực " + handIn * 100f + " cm, tay trước ngực " + apart * 100f + " cm");
        }
    }
}
